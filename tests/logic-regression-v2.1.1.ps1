$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'logic-regression-v2.1.0.ps1')

$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$constructor = $storeType.GetConstructor(
    [Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$arguments = [object[]]::new(1)
$arguments[0] = [string](Join-Path $repoRoot '枫语幕词库.tsv')
$store = $constructor.Invoke($arguments)
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null

function Invoke-One([string]$method, [string]$value) {
    $args = [object[]]::new(1); $args[0] = [string]$value
    $storeType.GetMethod($method).Invoke($store, $args)
}

$normalize = $storeType.GetMethod('Normalize',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$normalizeArgs = [object[]]::new(1); $normalizeArgs[0] = [string]"'LERVE STORE."
if ($normalize.Invoke($null, $normalizeArgs) -ne 'leave store') {
    throw '带引号的商店按钮 OCR 纠错失败'
}

$taskArgs = [object[]]::new(2)
$taskArgs[0] = [string]"Biggs's Collection ot Items"
$taskArgs[1] = $null
$taskMatches = $storeType.GetMethod('FindTaskMatches').Invoke($store, $taskArgs)
$taskChinese = @($taskMatches | ForEach-Object {
    $entry = $_.GetType().GetField('Entry').GetValue($_)
    $entry.GetType().GetField('Chinese').GetValue($entry)
})
if (-not ($taskChinese -contains '比格斯的物品收集')) {
    throw '多任务列表中的非当前任务标题被上下文吞掉'
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($forbidden in @('ReadProcessMemory','WriteProcessMemory','CreateRemoteThread',
    'VirtualAllocEx','SetWindowsHookEx','SendInput','D:\TEMP\codex-clipboard')) {
    if ($source.Contains($forbidden)) { throw "源码包含禁止能力或样本硬编码：$forbidden" }
}
foreach ($required in @('FindClassicTooltipCrop','AddStablePanelLayouts',
    'BuildVisualRows','TryTranslateStructuredLine','RemoveContainedLabels')) {
    if (-not $source.Contains($required)) { throw "通用识别管线缺少：$required" }
}

Write-Output 'v2.1.1 逻辑与安全边界回归：通过'
