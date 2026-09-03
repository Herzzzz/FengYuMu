$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$store = $ctor.Invoke(@([string](Join-Path $repoRoot '枫语幕词库.tsv')))
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$overlay = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($overlayType)
$overlayType.GetField('translations', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($overlay, $store)
$structured = $overlayType.GetMethod('TryTranslateStructuredLine',
    [Reflection.BindingFlags]'Instance,NonPublic')

function Assert-Structured([string]$text, [bool]$equipment, [bool]$skill, [string[]]$expected) {
    $args = [object[]]@($text, $equipment, $skill, $null)
    if (-not [bool]$structured.Invoke($overlay, $args)) {
        throw "结构化文字未识别：$text"
    }
    foreach ($value in $expected) {
        if (-not ([string]$args[3]).Contains($value)) {
            throw "结构化文字缺少 $value：$text -> $($args[3])"
        }
    }
}

Assert-Structured 'REQ LEV : 2S REQ STR : O REQ DEX : O REQ INT : O REQ LUK : O' $true $false `
    @('需要等级：25','需要力量：0','需要敏捷：0','需要智力：0','需要运气：0')
Assert-Structured 'REQ LEV : SO REQ STR : O REQ DEX : O REQ INT : O REQ LUK : O' $true $false `
    @('需要等级：50','需要力量：0','需要敏捷：0','需要智力：0','需要运气：0')
Assert-Structured 'MAGIC 38' $true $false @('魔法防御力：38')

$sceneType = $assembly.GetType('MapleOverlay.SceneClassifier', $true)
$classify = $sceneType.GetMethod('Classify', [Reflection.BindingFlags]'Static,NonPublic,Public')
foreach ($case in @(
    [pscustomobject]@{ Text='[Master Level : 30]'; Flag='Skill' },
    [pscustomobject]@{ Text='REQ LEV : 50 REQ STR : 0'; Flag='Item' },
    [pscustomobject]@{ Text='Quest Helper (1/5)'; Flag='Quest' },
    [pscustomobject]@{ Text='CHARACTER STAT'; Flag='Character' },
    [pscustomobject]@{ Text='NEXT'; Flag='Dialogue' },
    [pscustomobject]@{ Text='Put your game face on, ace!'; Flag='Dialogue' })) {
    $evidence = $classify.Invoke($null, @($case.Text, $store, $true))
    $kinds = [string]$evidence.GetType().GetField('Kinds').GetValue($evidence)
    if (-not ($kinds -split ', ' -contains $case.Flag)) {
        throw "场景分类错误：$($case.Text) -> $kinds"
    }
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$sceneSource = Get-Content (Join-Path $repoRoot 'src\SceneRecognition.cs') -Raw -Encoding UTF8
foreach ($forbidden in @('ReadProcessMemory','WriteProcessMemory','CreateRemoteThread',
    'VirtualAllocEx','SetWindowsHookEx','SendInput')) {
    if (($source + $sceneSource).Contains($forbidden)) { throw "安全边界失败：$forbidden" }
}
Write-Output 'v3.0 场景分类、结构化 OCR 数值纠错与安全边界回归：通过'
