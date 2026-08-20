$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'logic-regression-v2.1.1.ps1')

$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$storeCtor = $storeType.GetConstructor(
    [Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$storeArgs = [object[]]::new(1)
$storeArgs[0] = [string](Join-Path $repoRoot '枫语幕词库.tsv')
$store = $storeCtor.Invoke($storeArgs)
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null

# Directly exercise the structured Quest Helper line parser without opening the UI.
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$overlay = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($overlayType)
$translationsField = $overlayType.GetField('translations',
    [Reflection.BindingFlags]'Instance,NonPublic')
$translationsField.SetValue($overlay, $store)
$structured = $overlayType.GetMethod('TryTranslateStructuredLine',
    [Reflection.BindingFlags]'Instance,NonPublic')
function Invoke-Structured([string]$text) {
    $args = [object[]]::new(4)
    $args[0] = $text; $args[1] = $false; $args[2] = $false; $args[3] = $null
    $matched = [bool]$structured.Invoke($overlay, $args)
    [pscustomobject]@{ Matched=$matched; Text=[string]$args[3] }
}
$objective = Invoke-Structured '3/10 Snail'
if (-not $objective.Matched -or $objective.Text -ne '3/10 蜗牛') {
    throw "任务助手进度解析失败：$($objective.Text)"
}
$wobbled = Invoke-Structured '3/lO Snail'
if (-not $wobbled.Matched -or $wobbled.Text -ne '3/10 蜗牛') {
    throw "任务助手计数 OCR 容错失败：$($wobbled.Text)"
}

$taskArgs = [object[]]::new(2)
$taskArgs[0] = [string]"Sam's Suggestion"
$taskArgs[1] = $null
$taskMatches = $storeType.GetMethod('FindTaskMatches').Invoke($store, $taskArgs)
$taskChinese = @($taskMatches | ForEach-Object {
    $entry = $_.GetType().GetField('Entry').GetValue($_)
    $entry.GetType().GetField('Chinese').GetValue($entry)
})
if (-not ($taskChinese -contains '萨姆的建议')) {
    throw "任务名没有按资料库译为萨姆的建议：$($taskChinese -join ' | ')"
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @('VisualColorBand','CharacterStatVisualLayout',
    'FindCharacterStatVisualLayout','AddCharacterStatVisualLayoutLabels',
    'AddCharacterStatHoverHelp','LockBits','minimumHoverHeight = tooltipLocal.IsEmpty ? 220 : 82')) {
    if (-not $source.Contains($required)) { throw "v2.1.2 结构识别缺少：$required" }
}
foreach ($forbidden in @('D:\TEMP\codex-clipboard','ReadProcessMemory','WriteProcessMemory',
    'CreateRemoteThread','VirtualAllocEx','SetWindowsHookEx','SendInput')) {
    if ($source.Contains($forbidden)) { throw "源码包含禁止能力或样本硬编码：$forbidden" }
}
if (-not $source.Contains('枫语幕 v2.1.2')) { throw '程序版本号不是 v2.1.2' }

Write-Output 'v2.1.2 结构识别、任务进度与安全边界回归：通过'
