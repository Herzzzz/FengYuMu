$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $repoRoot '枫语幕.exe'
$dictionaryPath = Join-Path $repoRoot '枫语幕词库.tsv'
$assembly = [Reflection.Assembly]::LoadFile($assemblyPath)
$instanceFlags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic,Public'

$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$storeCtor = $storeType.GetConstructor($instanceFlags, $null, [Type[]]@([string]), $null)
$store = $storeCtor.Invoke(@([string]$dictionaryPath))
$storeType.GetMethod('Load', $instanceFlags).Invoke($store, @()) | Out-Null
$findSettings = $storeType.GetMethod('FindSettingsTextMatches', $instanceFlags)
$findDialogue = $storeType.GetMethod('FindDialogueTextMatches', $instanceFlags)
$findGeneric = $storeType.GetMethod('FindMatches', $instanceFlags)
$detectTask = $storeType.GetMethod('DetectTaskId', $instanceFlags)
if ($null -eq $findSettings -or $null -eq $findDialogue -or
    $null -eq $findGeneric -or $null -eq $detectTask) {
    throw '缺少设置/NPC 对话专属匹配 API'
}

function Get-Field($value, [string]$name) {
    $field = $value.GetType().GetField($name, $instanceFlags)
    if ($null -eq $field) { throw "缺少字段：$name" }
    return $field.GetValue($value)
}

function Find-Chinese($method, [string]$text) {
    $matches = @($method.Invoke($store, @([string]$text)))
    if ($matches.Count -lt 1) { return '' }
    $entry = Get-Field $matches[0] 'Entry'
    return [string](Get-Field $entry 'Chinese')
}

function Find-ChineseValues($method, [string]$text) {
    $values = @()
    foreach ($match in @($method.Invoke($store, @([string]$text)))) {
        $entry = Get-Field $match 'Entry'
        $values += [string](Get-Field $entry 'Chinese')
    }
    return @($values)
}

$settings = @{
    'OPTIONS' = '设置'
    'Graphics' = '图像'
    'Windowed Mode' = '窗口模式'
    'VSync' = '垂直同步'
    'UI Size' = '界面大小'
    'UI Fade' = '界面淡化'
    'Damage Number Size' = '伤害数字大小'
    'Master' = '主音量'
    'BGM' = '背景音乐'
    'HP Warning' = '低生命值警告'
    'Screenshot File Format' = '截图文件格式'
    'MapleStory Folder' = '冒险岛文件夹'
    '2GB to 64GB Available' = '可设置 2GB 到 64GB'
    'Enable Controller Support' = '启用手柄支持'
    'Chat Font Size' = '聊天字号'
}
foreach ($key in $settings.Keys) {
    $actual = Find-Chinese $findSettings $key
    if ($actual -ne $settings[$key]) {
        throw "设置词匹配错误：$key -> $actual（预期 $($settings[$key])）"
    }
}

# Every settings row must remain reachable through the settings-only matcher.
$settingsRows = @(Import-Csv -LiteralPath $dictionaryPath -Delimiter "`t" -Header English,Chinese,Category,Source,Location |
    Where-Object { $_.Category -like '怀旧服-设置-*' })
if ($settingsRows.Count -lt 68) {
    throw "设置词条数量异常：$($settingsRows.Count)"
}
foreach ($row in $settingsRows) {
    $values = @(Find-ChineseValues $findSettings ([string]$row.English))
    if ($values -notcontains [string]$row.Chinese) {
        throw "设置词条无法通过专属通道命中：$($row.English) -> $($values -join ' / ')"
    }
}

# Compound labels must win as a whole instead of being fragmented into short words.
$compoundSettings = @{
    'Screenshot File Format' = '截图文件格式'
    'Screenshot Location' = '截图保存位置'
    'Damage Number Size' = '伤害数字大小'
    '2GB to 64GB Available' = '可设置 2GB 到 64GB'
}
foreach ($key in $compoundSettings.Keys) {
    $values = @(Find-ChineseValues $findSettings $key)
    if ($values.Count -ne 1 -or $values[0] -ne $compoundSettings[$key]) {
        throw "设置复合标签被拆分：$key -> $($values -join ' / ')"
    }
}
$dynamicValues = @(Find-ChineseValues $findSettings 'HP Warning 0% 100%')
if ($dynamicValues -notcontains '低生命值警告') {
    throw "设置动态数值导致标签丢失：$($dynamicValues -join ' / ')"
}

# Short settings words must never enter the ordinary full-screen dictionary path.
foreach ($short in @('OPTIONS', 'Graphics', 'Game', 'Language')) {
    $generic = @($findGeneric.Invoke($store, @([string]$short)))
    foreach ($match in $generic) {
        $entry = Get-Field $match 'Entry'
        if ([bool](Get-Field $entry 'IsSettingsText')) {
            throw "设置短词泄漏到普通匹配通道：$short"
        }
    }
}

$settingsPolicy = $assembly.GetType('MapleOverlay.SettingsPanelPolicy', $true)
$isOptionsWindow = $settingsPolicy.GetMethod('IsOptionsWindow', $staticFlags)
foreach ($optionsPage in @(
    'OPTIONS Graphics Resolution',
    'OPTIONS Sound Master',
    'OPTIONS Game HP Warning',
    'OPTIONS Social Whispers',
    'OPTIONS Controller Stick Sensitivity',
    'OPTIONS HP Warning MP Warning')) {
    if (-not [bool]$isOptionsWindow.Invoke($null, @([string]$optionsPage))) {
        throw "Options 分页未被识别：$optionsPage"
    }
}
foreach ($notOptions in @('OPTIONS', 'OPTIONS Language', 'OPTIONS ITEM INVENTORY Use Set-Up',
    'Game', 'Use', 'Game Language', 'ITEM INVENTORY Use Set-Up')) {
    if ([bool]$isOptionsWindow.Invoke($null, @([string]$notOptions))) {
        throw "普通界面误判为 Options：$notOptions"
    }
}

$tommyOcr = "I help with all sorts ot housing matters tor the residents of Henesys. However, it's taking a bit ot time to get everything ready, so please come back and visit me again next time!"
$tommy = Find-Chinese $findDialogue $tommyOcr
if ($tommy -ne '我负责处理射手村居民的各种住房事务。不过目前还需要一点时间准备，等下次开放后再来找我吧！') {
    throw "Tommy OCR 对话未命中自然译文：$tommy"
}

$janeOcr = "Even if I come back alive with these charms. I wont be fully alive. per se. kind of like a zombie. I cant believe it took me this long to come to terms with it... but I will never torget the fact that you took great lengths to help me out. Thankyou so, so much."
$jane = Find-Chinese $findDialogue $janeOcr
if ($jane -notmatch '道符' -or $jane -match '魅力') {
    throw "Jane Doe 对话仍为错误机翻：$jane"
}
$taskId = [string]$detectTask.Invoke($store, @([string]$janeOcr))
if ($taskId -ne '10310') { throw "Jane Doe OCR 对话未解析到任务 10310：$taskId" }

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @('IsSettingsText', 'IsNpcDialogue', 'FindSettingsTextMatches',
    'FindDialogueTextMatches', 'settingsPanel',
    'mainInterface ? SceneClassifier.EvidenceScore(evidence, SceneKind.Interface)')) {
    if (-not $source.Contains($required)) { throw "源码缺少场景隔离标记：$required" }
}

Write-Output "设置与 NPC 对话专项测试通过：$($settingsRows.Count) 条设置词条全部可达，五分页、复合标签、动态数值与场景隔离通过；Tommy/Jane OCR 误字可命中自然译文，Jane 任务 ID 为 10310"
