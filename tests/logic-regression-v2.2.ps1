$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# v2.1.2 is the frozen accuracy baseline. Every v2.2 run must pass it first.
& (Join-Path $PSScriptRoot 'logic-regression-v2.1.2.ps1')

$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor(
    [Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$ctorArgs = [object[]]::new(1)
$ctorArgs[0] = [string](Join-Path $repoRoot '枫语幕词库.tsv')
$store = $ctor.Invoke($ctorArgs)
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null

# Same-name entries with different translations must remain ambiguous without an icon.
$ambiguous = @($storeType.GetMethod('FindMatches').Invoke($store, @('Afro')))
if ($ambiguous.Count -ne 0) {
    throw '同名词条 Afro 被文本路径任意选择；必须继续由图标+文字路径消歧'
}

# Dynamic counters must preserve arbitrary current/target numbers and common OCR wobble.
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$overlay = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($overlayType)
$overlayType.GetField('translations', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($overlay, $store)
$structured = $overlayType.GetMethod('TryTranslateStructuredLine',
    [Reflection.BindingFlags]'Instance,NonPublic')
function Assert-Structured([string]$text, [string]$expected) {
    $args = [object[]]::new(4)
    $args[0] = $text; $args[1] = $false; $args[2] = $false; $args[3] = $null
    if (-not [bool]$structured.Invoke($overlay, $args) -or [string]$args[3] -ne $expected) {
        throw "动态数字模板回归失败：$text -> $($args[3])"
    }
}
Assert-Structured '37/120 Snail' '37/120 蜗牛'
Assert-Structured '7/lO Blue Snail' '7/10 蓝蜗牛'

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'BeginSafeWarmup',
    'AI知识将在打开AI功能时按需同步',
    '已继续使用上一次的稳定索引',
    'F8词库翻译仍可继续使用',
    'MainPanelForm',
    'if (!Program.Benchmark) ShowMainPanel()',
    'ShowTranslationFromHotkeyAsync',
    'GetChatExclusionBounds',
    'IsChatLine')) {
    if (-not $source.Contains($required)) { throw "v2.2 稳定性基线缺少：$required" }
}
if ($source -match 'Shown\s*\+=?[\s\S]{0,900}SyncAiKnowledge\(false\)') {
    throw '启动链路仍在主动同步AI知识，AI懒加载未生效'
}

Write-Output 'v2.2 冻结基线：装备/物品/技能/任务/角色/长短句/动态数字/同名词条/聊天排除逻辑通过'
