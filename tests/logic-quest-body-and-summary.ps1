$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$dict = Join-Path $repoRoot '枫语幕词库.tsv'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }
if (-not (Test-Path -LiteralPath $dict)) { throw "找不到词库：$dict" }

if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1') }
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor($all, $null, [Type[]]@([string]), $null)
if ($null -eq $ctor) { throw '未找到词库构造函数' }
$store = $ctor.Invoke(@([string]$dict))
$storeType.GetMethod('Load', $all).Invoke($store, @()) | Out-Null

function Get-EntryField($match, [string]$field) {
    $entry = $match.GetType().GetField('Entry', $all).GetValue($match)
    return $entry.GetType().GetField($field, $all).GetValue($entry)
}
function Get-MatchTexts($matches) {
    return @($matches | ForEach-Object { [string](Get-EntryField $_ 'Chinese') })
}
function Get-MatchTaskIds($matches) {
    return @($matches | ForEach-Object { [string](Get-EntryField $_ 'TaskId') })
}

$find = $storeType.GetMethod('FindMatches', $all)
$shared = $storeType.GetMethod('FindSharedTaskBodyMatches', $all)
$taskMatches = $storeType.GetMethod('FindTaskMatches', $all)
$detectTaskId = $storeType.GetMethod('DetectTaskId', $all)
if ($null -eq $find -or $null -eq $shared -or $null -eq $taskMatches -or
    $null -eq $detectTaskId) {
    throw 'EXE 缺少 Quest Summary / 任务正文匹配 API'
}

# ---------------------------------------------------------------------------
# 1. Quest Summary. A Quest-pane heading is short, but the surrounding panel
#    already scopes it, so it must survive the generic short-UI suppression.
#    Windows OCR also renders the space as a bullet in this caption.
# ---------------------------------------------------------------------------
foreach ($variant in @('Quest Summary', 'Quest•Summary', 'QUEST SUMMARY', 'quest summary')) {
    $texts = Get-MatchTexts @($find.Invoke($store, @([string]$variant)))
    if ($texts -notcontains '任务概要') {
        throw "Quest Summary 未翻译：'$variant' => $($texts -join ' | ')"
    }
}

# ---------------------------------------------------------------------------
# 2. Shared chain body. The citizenship donation body is repeated across 18
#    identically named quests, so it carries no id. The merged text below mirrors
#    what the OCR actually produced after the quest window's own label strip
#    ("Citizen's R..") split the sentence and dropped part of "Bubbling's".
# ---------------------------------------------------------------------------
$sharedBody = 'Roxy, the City Clerk, requested a donation of 30 ge Bubbles for Kerning City.'
$sharedHits = @($shared.Invoke($store, @([string]$sharedBody)))
if ($sharedHits.Count -eq 0) { throw "共享任务正文未命中：$sharedBody" }
$sharedTexts = Get-MatchTexts $sharedHits
$expectedShared = '城市职员洛克希请求为废弃都市捐助 30 个蓝水灵大水珠。'
if ($sharedTexts -notcontains $expectedShared) {
    throw "共享任务正文译文错误：$($sharedTexts -join ' | ')"
}

# ---------------------------------------------------------------------------
# 3. The shared entry point must never hand back an entry that owns a task id.
#    Otherwise one selected task could borrow another task's body.
# ---------------------------------------------------------------------------
foreach ($taskId in (Get-MatchTaskIds $sharedHits)) {
    if ($taskId.Length -gt 0) { throw "共享入口返回了带 ID 的条目：$taskId" }
}

# ---------------------------------------------------------------------------
# 4. A body that belongs to one specific task must not leak through the shared
#    entry point, and must stay strictly bound when an id IS known.
# ---------------------------------------------------------------------------
$ownedBody = 'Arthur, the Town Clerk I met at Henesys Town Hall, suggested I become a resident of Henesys.'
$ownedChinese = '我在射手村市政厅见到的镇务员亚瑟建议我成为射手村居民。决定以后，我应该再和亚瑟谈谈，了解更多详情。'
$leaked = @($shared.Invoke($store, @([string]$ownedBody)))
if ((Get-MatchTexts $leaked) -contains $ownedChinese) {
    throw '带 ID 的 506000 任务说明被共享入口错误借出'
}
foreach ($taskId in (Get-MatchTaskIds $leaked)) {
    if ($taskId.Length -gt 0) { throw "带 ID 的任务正文从共享入口泄漏：$taskId" }
}

$ownedHits = @($taskMatches.Invoke($store, [object[]]@([string]$ownedBody, '506000')))
if ($ownedHits.Count -eq 0) { throw '带 ID 的任务正文在严格模式下未命中' }
if ((Get-MatchTexts $ownedHits) -notcontains $ownedChinese) {
    throw '带 ID 的任务正文在严格模式下译文错误'
}
$foreign = @($taskMatches.Invoke($store, [object[]]@([string]$ownedBody, '506001')))
foreach ($taskId in (Get-MatchTaskIds $foreign)) {
    if ($taskId -eq '506000') {
        throw '任务正文没有严格绑定任务 ID：506000 的正文在 506001 语境下仍被返回'
    }
}

$viciousBody = 'After I told Vicious, the carpenter of Henesys. that I wanted to become his apprentice, he gave me a test. He said that woodcrafting requires one to have the proper strength and knowledge and asked me to defeat monsters near Henesys and collect Tree Branches.'
$viciousTaskId = [string]$detectTaskId.Invoke($store,
    @([string]("Vicious in Need of an Apprentice`n" + $viciousBody)))
if ($viciousTaskId -ne '80017') {
    throw "Vicious 任务窗口未恢复任务 ID 80017：$viciousTaskId"
}
$viciousChinese = Get-MatchTexts @($taskMatches.Invoke($store,
    [object[]]@([string]$viciousBody, '80017')))
if (-not ($viciousChinese | Where-Object { $_ -match '木匠维西' })) {
    throw "Vicious 当前任务正文未命中：$($viciousChinese -join ' | ')"
}
$viciousShared = Get-MatchTexts @($shared.Invoke($store, @([string]$viciousBody)))
if (-not ($viciousShared | Where-Object { $_ -match '木匠维西' })) {
    throw "Vicious OCR 分段共享兜底未命中：$($viciousShared -join ' | ')"
}

# ---------------------------------------------------------------------------
# 5. The current international classic client changed both the visible chain
#    title and the 10308 body wording. The long detail text must still recover
#    the task id and return the exact terminology-locked translation.
# ---------------------------------------------------------------------------
$current10308 = "I got another request from Nella. Cutthroat Manny, the owner of the weapon shop, is quite the unusual foodie--he wants to cook up a special dish of Evil Eye Tail Stew. He needs me to hunt 50 Evil Eyes and bring back 30 Evil Eye Tails. Sounds like he'll be eating well for a while! Once I've gotten everything, I should bring it all back to Nella."
$detected10308 = [string]$detectTaskId.Invoke($store, @([string]$current10308))
if ($detected10308 -ne '10308') {
    throw "当前 10308 任务正文未恢复任务 ID：$detected10308"
}
$currentHits = @($taskMatches.Invoke($store,
    [object[]]@([string]$current10308, [string]$detected10308)))
$currentChinese = Get-MatchTexts $currentHits
if ($currentChinese -notcontains '我又从内拉那里接到一个委托。武器店老板曼斯塔是个相当特别的美食家——他想做一道火独眼兽尾炖菜。他要我消灭 50 只火独眼兽，并带回 30 条火独眼兽之尾。看来他这阵子能吃得很满足！收集齐后，我应该把东西交回给内拉。') {
    throw "当前 10308 任务正文未按实测版本翻译：$($currentChinese -join ' | ')"
}

# Evil Eye and Curse Eye are two different monsters. Keep the four canonical
# noun rows paired so a task-body correction cannot silently swap them again.
$dictionaryRows = @(Import-Csv -LiteralPath $dict -Delimiter "`t" `
    -Header English,Chinese,Category,Source,Location)
$canonicalTerms = @{
    'Evil Eye|怀旧服-怪物#23' = '火独眼兽'
    'Evil Eye Tail|怀旧服-道具#4000022' = '火独眼兽之尾'
    'Curse Eye|怀旧服-怪物#31' = '风独眼兽'
    'Curse Eye Tail|怀旧服-道具#4000027' = '风独眼兽之尾'
}
foreach ($key in $canonicalTerms.Keys) {
    $parts = $key.Split('|')
    $row = $dictionaryRows | Where-Object {
        $_.English -eq $parts[0] -and $_.Category -eq $parts[1]
    } | Select-Object -First 1
    if ($null -eq $row -or $row.Chinese -ne $canonicalTerms[$key]) {
        throw "独眼兽术语映射错误：$key -> $($row.Chinese)"
    }
}

# ---------------------------------------------------------------------------
# 6. Guard the guards, so the behaviours above cannot be silently deleted.
# ---------------------------------------------------------------------------
$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'public List<MatchResult> FindSharedTaskBodyMatches(string text)',
    'results.RemoveAll(delegate(MatchResult match) { return match.Entry.TaskId.Length > 0; });',
    'bool questPaneHeading = questSurface && IsQuestPanelChrome(match.Entry.Normalized);',
    'private static bool IsQuestPanelChrome(string normalized)',
    'if (sharedOnly && match.Entry.TaskId.Length > 0) continue;',
    'public bool IsQuestTarget;')) {
    if (-not $source.Contains($required)) {
        throw "Quest Summary / 任务正文支持缺少守卫：$required"
    }
}

Write-Output ('任务正文与 Quest Summary：面板内短标题可翻译且不外溢，共享链正文可跨 OCR 碎片与换行匹配，' +
    '带 ID 正文严格绑定且不从共享入口泄漏')
