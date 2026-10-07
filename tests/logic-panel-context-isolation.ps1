$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $repoRoot '枫语幕.exe'
$dictionaryPath = Join-Path $repoRoot '枫语幕词库.tsv'
$assembly = [Reflection.Assembly]::LoadFile($assemblyPath)

$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$storeCtor = $storeType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
if ($null -eq $storeCtor) { throw '未找到词库构造函数' }
$store = $storeCtor.Invoke(@([string]$dictionaryPath))
$storeType.GetMethod('Load', [Reflection.BindingFlags]'Instance,NonPublic,Public').Invoke($store, @()) | Out-Null

$resolveTitle = $storeType.GetMethod('ResolveUniqueTaskTitleId',
    [Reflection.BindingFlags]'Instance,NonPublic,Public')
$findTaskNames = $storeType.GetMethod('FindTaskNameMatches',
    [Reflection.BindingFlags]'Instance,NonPublic,Public')
if ($null -eq $resolveTitle -or $null -eq $findTaskNames) {
    throw 'EXE 缺少唯一任务标题解析 API'
}

function Assert-TaskId([string]$text, [string]$expected, [string]$name) {
    $actual = [string]$resolveTitle.Invoke($store, @([string]$text))
    if ($actual -ne $expected) {
        throw "$name：$text -> $actual（预期 $expected）"
    }
}

Assert-TaskId "Mai's Training" '1009' '唯一任务标题可解析'
Assert-TaskId "Mai's Train..." '' '带省略号的截断标题必须拒绝'
Assert-TaskId 'Proof of Qualification' '' '同名多任务 ID 必须拒绝'
Assert-TaskId "Rain's Maple Quiz 1" '1013' '阿拉伯数字后缀 1 必须精确匹配'
Assert-TaskId "Rain's Maple Quiz 2" '1014' '阿拉伯数字后缀 2 不得混淆'
Assert-TaskId 'Special Taste of Florina Beach I' '10700' '罗马数字后缀 I 必须精确匹配'
Assert-TaskId 'Special Taste of Florina Beach II' '10701' '罗马数字后缀 II 不得混淆'
Assert-TaskId "Rain's Maple Quiz" '' '缺少编号后缀时不得猜测任务 ID'

$nameMatches = @($findTaskNames.Invoke($store, @([string]"Mai's Training")))
if ($nameMatches.Count -lt 1) { throw 'FindTaskNameMatches 未返回唯一任务标题匹配' }

$policyType = $assembly.GetType('MapleOverlay.PanelContextPolicy', $true)
$resolveContext = $policyType.GetMethod('ResolveTaskId',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
if ($null -eq $resolveContext -or $resolveContext.GetParameters().Count -ne 5) {
    throw '未找到五参数 PanelContextPolicy.ResolveTaskId API'
}

function Resolve-PanelContext([string]$detailText, [string[]]$localLines,
    [bool]$focusedPass, [bool]$mainQuestShell) {
    $args = [object[]]::new(5)
    $args[0] = $detailText
    $args[1] = $localLines
    $args[2] = $store
    $args[3] = $focusedPass
    $args[4] = $mainQuestShell
    return [string]$resolveContext.Invoke($null, $args)
}

if ((Resolve-PanelContext "Mai's Training" @("Mai's Training") $false $false) -ne '') {
    throw 'Discovery/非 focused 面板不应锁定任务 ID'
}
if ((Resolve-PanelContext "Mai's Training" @("Mai's Training") $false $true) -ne '') {
    throw 'Discovery/非 focused 主任务窗不应锁定任务 ID'
}
if ((Resolve-PanelContext "Mai's Training" @('Proof of Qualification') $true $true) -ne '1009') {
    throw 'focused 主任务窗未锁定右侧唯一具体任务标题'
}
if ((Resolve-PanelContext 'To Henesys, the Prairie...' @(
    'To Henesys, the Prairie...',
    "Arthur, the Town Clerk I met at Henesys Town Hall, suggested I become a resident of Henesys. Once I've made up my mind, I should speak with Arthur for more details."
) $true $true) -ne '506000') {
    throw 'focused 主任务窗未用右侧唯一详情消歧被截断的公民任务标题'
}
if ((Resolve-PanelContext 'Donating to Kerning City' @(
    'Only characters at Level 12 or higher can complete this quest.',
    'Only characters at Level 12 or higher can complete this quest.'
) $true $true) -ne '') {
    throw '重名公民任务仅有通用正文时不应猜测任务 ID'
}
if ((Resolve-PanelContext '' @("Mai's Training", 'Letter for Lucas') $true $false) -ne '') {
    throw '多个任务上下文不应借用单一任务 ID'
}

$mapleSource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$sceneSource = Get-Content (Join-Path $repoRoot 'src\SceneRecognition.cs') -Raw -Encoding UTF8
foreach ($required in @('LabelBuildScope', 'FindQuestConcreteTaskTitle', 'FindTaskNameMatches',
    'normalized.Contains("quest summary")', 'TryAddQuestObjectiveNameLabel',
    'if (normalized == "quest") header = true;',
    'if (tabRight > panelLeft) detailBoundary = tabRight + 12.0f;')) {
    if (-not ($mapleSource.Contains($required) -or $sceneSource.Contains($required))) {
        throw "源码缺少任务上下文隔离标记：$required"
    }
}
if ($mapleSource.Contains('normalized.StartsWith("quest ", StringComparison.Ordinal)')) {
    throw 'Quest Summary 等右侧内容仍可能被误当作 QUEST 窗口标题'
}
if (-not $mapleSource.Contains('if (!overlapping && !adjacentRows) continue;')) {
    throw '相同译文仍可能跨越远距离面板合并成巨型覆盖框'
}
if ($mapleSource.Contains('globalTaskId') -or $sceneSource.Contains('globalTaskId')) {
    throw '源码仍存在 globalTaskId 全局任务上下文'
}
if (-not $mapleSource.Contains('PanelContextPolicy.ResolveTaskId') -or
    -not $mapleSource.Contains('scope == LabelBuildScope.FocusedPanel')) {
    throw '源码未按 LabelBuildScope 调用 PanelContextPolicy'
}

Write-Output '任务标题解析与 PanelContextPolicy 上下文隔离通过：唯一/截断/重名/编号后缀、Discovery 与 focused 作用域均已覆盖'
