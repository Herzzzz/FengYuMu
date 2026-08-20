$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$constructor = $storeType.GetConstructor(
    [Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$dictionaryPath = [string](Join-Path $repoRoot '枫语幕词库.tsv')
$constructorArgs = New-Object 'object[]' 1
$constructorArgs[0] = $dictionaryPath
$store = $constructor.Invoke($constructorArgs)
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null

function Invoke-Store([string]$method, [object[]]$arguments) {
    $storeType.GetMethod($method).Invoke($store, $arguments)
}

function Get-Translations($matches) {
    @($matches | ForEach-Object {
        if ($null -eq $_) { return }
        $entry = $_.GetType().GetField('Entry').GetValue($_)
        if ($null -eq $entry) { return }
        $entry.GetType().GetField('Chinese').GetValue($entry)
    })
}

function Assert-Translation([string]$name, $matches, [string]$expected) {
    $values = Get-Translations $matches
    if (-not ($values -match [regex]::Escape($expected))) {
        throw "$name 未命中：$expected"
    }
}

# 不同面板必须按各自上下文匹配，Quest Helper 不能再关闭技能/装备详情。
$ice = 'Ice Charge Imbues your sword or blunt weapon with the Ice element for a set duration to increase its power, granting a chance to freeze enemies, greatly reducing their Movement Speed for 2 seconds.'
$iceId = Invoke-Store 'DetectSkillContentId' @($ice)
if ($iceId -ne '1211002') { throw "寒冰充能内容识别错误：$iceId" }
Assert-Translation '寒冰充能整段' (Invoke-Store 'FindSkillTextMatches' @($ice, $iceId)) '将你的剑或钝器附魔冰元素'
Assert-Translation '寒冰充能等级效果' (Invoke-Store 'FindSkillTextMatches' @('MP -20 Imbues weapon with Ice for 90 seconds Damage +2% 45% chance to Freeze enemies for 2 sec', $iceId)) '持续90秒'

$rush = 'Rushes forward a certain distance in the direction you are facing. If there are monsters within the rush range, you can deal damage to up to 4 of them and knock them back a certain distance.'
Assert-Translation '突进说明' (Invoke-Store 'FindSkillTextMatches' @($rush, '1321003')) '最多可攻击4只'
$quest = 'I arn now under the tutelage of the famous sword master Mai in A Split Road For my first lesson she told me to hunt Blue Snails Shroorns and Fed Snails Mai also said to meet up with Biggs in Southperrv tor a useful quest'
Assert-Translation '任务整段' (Invoke-Store 'FindTaskMatches' @($quest, '1009')) '岔路口'

# 通用人物/装备面板词条必须完整，且 OCR 常见形变仍能容错。
Assert-Translation '人物名称' (Invoke-Store 'FindCharacterStatMatches' @('NAME')) '名称'
Assert-Translation '人物防御' (Invoke-Store 'FindCharacterStatMatches' @('WEAPON DEF.')) '物理防御力'
Assert-Translation '装备需求' (Invoke-Store 'FindMatches' @('REQ STR 10')) '需要力量'
Assert-Translation '装备类型' (Invoke-Store 'FindMatches' @('Type: Shoes')) '鞋子'
Assert-Translation '强化次数' (Invoke-Store 'FindMatches' @('Remaining Enhancements: 5')) '剩余强化次数'
Assert-Translation '人物信息按钮' (Invoke-Store 'FindMatches' @('REQUEST PARTY')) '邀请组队'
Assert-Translation '任务放弃按钮' (Invoke-Store 'FindMatches' @('FORFEIT')) '放弃任务'

# 聊天帧：静止画面和轻微 OCR 抖动不得重复入队；玩家真发出的相同消息必须保留。
$chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$getNew = $chatType.GetMethod('GetNewChatLines', $flags)
$parse = $chatType.GetMethod('ParseChatLines', $flags)
function Invoke-NewChat([string[]]$previous, [string[]]$current) {
    $left = [Collections.Generic.List[string]]::new()
    $right = [Collections.Generic.List[string]]::new()
    foreach ($line in $previous) { $left.Add($line) }
    foreach ($line in $current) { $right.Add($line) }
    $invokeArgs = [object[]]::new(2)
    $invokeArgs.SetValue($left, 0); $invokeArgs.SetValue($right, 1)
    $getNew.Invoke($null, $invokeArgs)
}
function Invoke-ParseChat([string]$text) {
    $invokeArgs = [object[]]::new(1); $invokeArgs.SetValue($text, 0)
    $parse.Invoke($null, $invokeArgs)
}
$static = Invoke-NewChat -previous @('Alice: hello','Bob: hi') -current @('Alice: hello','Bob: hi')
if ($static.Count -ne 0) { throw '静止聊天画面被重复翻译' }
$wobble = Invoke-NewChat -previous @('Alice: where can i get meso sack') -current @('Alice: where can l get meso sack')
if ($wobble.Count -ne 0) { throw 'OCR微小抖动被当成新消息' }
$duplicate = Invoke-NewChat -previous @('Alice: hello','Bob: hi') -current @('Bob: hi','Bob: hi')
$duplicateItems = @($duplicate)
if ($duplicateItems.Count -ne 1 -or $duplicateItems[0] -ne 'Bob: hi') { throw '玩家连续发送相同真消息被吞掉' }
$parsed = Invoke-ParseChat "Chadson CH01: hello TugaStyle CH02: hi`n[Notice] Money lost through cash transactions cannot be recovered."
if ($parsed.Count -ne 3 -or $parsed[0] -notlike 'Chadson:*' -or $parsed[1] -notlike 'TugaStyle:*' -or $parsed[2] -notlike '系统公告:*') {
    throw "玩家行或系统公告分隔失败：$($parsed -join ' | ')"
}

# 当前怀旧服技能快照必须完整中文化，防止脚本半途失败留下空词库。
$skills = Get-Content (Join-Path $repoRoot 'data\henesys-skills-zh.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($skills.skills.Count -ne 261) { throw "技能快照数量错误：$($skills.skills.Count)" }
if (@($skills.skills | Where-Object {-not ($_.nameZh -match '[\u4e00-\u9fff]') -or -not ($_.descriptionZh -match '[\u4e00-\u9fff]')}).Count -ne 0) {
    throw '技能快照存在未中文化条目'
}
$rows = Get-Content $dictionaryPath -Encoding UTF8 | Where-Object {$_ -and -not $_.StartsWith('#')}
if (@($rows | Where-Object {$_.Split("`t").Count -lt 3}).Count -ne 0) { throw '词库存在损坏行' }

Write-Output '逻辑回归：20/20 通过'
