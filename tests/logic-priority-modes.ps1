$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$planner = $assembly.GetType('MapleOverlay.RecognitionPriorityPlanner', $true)
$select = $planner.GetMethods([Reflection.BindingFlags]'Static,NonPublic,Public') |
    Where-Object { $_.Name -eq 'Select' -and $_.GetParameters().Count -eq 5 } |
    Select-Object -First 1
if ($null -eq $select) { throw '未找到五参数优先级规划器' }

function Get-Plan([bool]$detail, [bool]$dialogue, [bool]$outside, [bool]$current, [int]$maximum) {
    return @($select.Invoke($null, @($detail, $dialogue, $outside, $current, $maximum)))
}
function Assert-Plan($plan, [string[]]$kinds, [double[]]$levels, [string]$name) {
    if ($plan.Count -ne $kinds.Count) { throw "$name 数量错误：$($plan.Count)" }
    for ($i = 0; $i -lt $plan.Count; $i++) {
        if ([string]$plan[$i].Kind -ne $kinds[$i]) {
            throw "$name 第$($i + 1)项错误：$($plan[$i].Kind)"
        }
        if ([Math]::Abs([double]$plan[$i].ResourceLevel - $levels[$i]) -gt 0.001) {
            throw "$name 第$($i + 1)档资源错误：$($plan[$i].ResourceLevel)"
        }
    }
}

Assert-Plan (Get-Plan $true $true $true $true 3) `
    @('Detail','Dialogue','OutsideDialogue') @(1.0,0.75,0.5) '均衡四类同屏'
Assert-Plan (Get-Plan $false $true $true $true 3) `
    @('Dialogue','OutsideDialogue','CurrentInterface') @(1.0,0.75,0.5) '均衡自动前移'
Assert-Plan (Get-Plan $true $true $true $true 2) `
    @('Detail','Dialogue') @(1.0,0.75) '最小范围'
Assert-Plan (Get-Plan $false $false $false $true 2) `
    @('CurrentInterface') @(1.0) '仅当前界面'

$target = $planner.GetMethod('TargetLongEdge', [Reflection.BindingFlags]'Static,NonPublic,Public')
$currentInterface = $planner.GetMethod('ShouldUseCurrentInterface', [Reflection.BindingFlags]'Static,NonPublic,Public')
if ($null -eq $currentInterface) { throw '未找到当前界面优先级裁决' }
if ([bool]$currentInterface.Invoke($null, @($true, $false, $false, $true))) {
    throw '存在详情框时不应再加入当前界面层'
}
if ([bool]$currentInterface.Invoke($null, @($false, $true, $false, $true))) {
    throw '存在对话框时不应再加入当前界面层'
}
if ([bool]$currentInterface.Invoke($null, @($false, $false, $true, $true))) {
    throw '存在对话框外面板时不应再加入当前界面层'
}
if (-not [bool]$currentInterface.Invoke($null, @($false, $false, $false, $true))) {
    throw '无前三类面板时应自动前移到当前界面层'
}
$rect = New-Object System.Drawing.Rectangle -ArgumentList 0,0,900,700
$full = [single]$target.Invoke($null, @($rect, [single]10, [double]1.0, $false))
$middle = [single]$target.Invoke($null, @($rect, [single]10, [double]0.75, $false))
$low = [single]$target.Invoke($null, @($rect, [single]10, [double]0.5, $false))
if (-not ($full -gt $middle -and $middle -gt $low)) {
    throw "分辨率归一化未按资源档位递减：$full/$middle/$low"
}

$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$store = $ctor.Invoke(@([string](Join-Path $repoRoot '枫语幕词库.tsv')))
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$overlay = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($overlayType)
$overlayType.GetField('translations', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($overlay, $store)
$structured = $overlayType.GetMethod('TryTranslateStructuredLine', [Reflection.BindingFlags]'Instance,NonPublic')
function Assert-LevelRepair([string]$text, [string]$expected) {
    $args = [object[]]::new(4)
    $args[0] = $text; $args[1] = $false; $args[2] = $true; $args[3] = $null
    if (-not [bool]$structured.Invoke($overlay, $args) -or [string]$args[3] -ne $expected) {
        throw "技能等级括号修复失败：$text -> $($args[3])"
    }
}
Assert-LevelRepair '[Current Level 51' '当前等级：5'
Assert-LevelRepair '(Next Level 61' '下一级：6'
Assert-LevelRepair '[Current Level 11]' '当前等级：11'
Assert-LevelRepair '[Current Level 21]' '当前等级：21'

$normalize = $storeType.GetMethod('Normalize', [Reflection.BindingFlags]'Static,NonPublic,Public')
if ([string]$normalize.Invoke($null, @('Ice for go seconds')) -ne 'ice for 90 seconds') {
    throw '技能持续时间 OCR 纠错失败：go seconds 未修复为 90 seconds'
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$forms = Get-Content (Join-Path $repoRoot 'src\SimpleForms.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'translationRangeMode != TranslationRangeMode.Maximum',
    'FindPanelCrops(result, ocrScale, screen)',
    'RecognitionPriorityPlanner.TargetLongEdge',
    'TranslationRangeMode", (int)mode',
    '范围最大（兼容路径）')) {
    if (-not $source.Contains($required)) { throw "缺少三档范围安全基线：$required" }
}
foreach ($required in @('StableLayout', 'if (label.StableLayout) continue;')) {
    if (-not $source.Contains($required)) { throw "角色属性稳定布局保护缺少：$required" }
}
foreach ($required in @(
    'RegisterWindowMessage("TaskbarCreated")',
    'RestoreTrayIcon(false)',
    'FengYuMu.SingleInstance.v2',
    'FengYuMu.Activate.v2',
    'BeginActivationRecovery()',
    'RestoreTrayIcon(true)')) {
    if (-not $source.Contains($required)) { throw "缺少托盘恢复机制：$required" }
}
foreach ($required in @('TrackBar range', 'range.Minimum = 1', 'range.Maximum = 3', '兼容最大', '推荐均衡', '精简最小')) {
    if (-not $forms.Contains($required)) { throw "主界面滑动条缺少：$required" }
}

Write-Output '三档范围与托盘恢复：动态优先级、资源级别、分辨率归一化、最大档兼容路径、滑动条和单实例唤回通过'
