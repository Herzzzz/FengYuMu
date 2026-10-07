$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$assembly = [Reflection.Assembly]::LoadFile($exe)
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)
$onlineType = $assembly.GetType('MapleOverlay.OnlineAiClient', $true)
$offlineType = $assembly.GetType('MapleOverlay.OfflineAiClient', $true)
$chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$intentPolicyType = $assembly.GetType('MapleOverlay.OutboundChatIntentPolicy', $true)
$settings = [Activator]::CreateInstance($settingsType, $true)
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'test-key')

$buildOnline = $onlineType.GetMethod('BuildRequestBody', $all)
$englishBody = [string]$buildOnline.Invoke($null, [object[]]@($settings,
    '废弃三缺一', '英语', "废弃都市组队任务 => KPQ (full: Kerning Party Quest)`n"))
$spanishBody = [string]$buildOnline.Invoke($null, [object[]]@($settings,
    '有人做废弃吗', '拉美西班牙语', "废弃都市组队任务 => KPQ (full: Kerning Party Quest)`n"))
foreach ($required in @('KPQ 3/4','members','国际服正式名称','Kerning Party Quest',
    'MapleStory Classic / Global','匹配优先级固定为','禁止按字面创造',
    '普通日常聊天','禁止强行套用游戏黑话')) {
    if (-not $englishBody.Contains($required)) { throw "中译英提示词缺少：$required" }
}
if (-not $englishBody.Contains('收/求购用') -or -not $englishBody.Contains('禁止擅自添加PQ') -or
    -not $spanishBody.Contains('禁止擅自添加PQ')) {
    throw '对外翻译缺少买卖方向或禁止乱加PQ约束'
}
foreach ($required in @('¿Alguien para KPQ?','Busco gente.','拉美西班牙语','Kerning Party Quest')) {
    if (-not $spanishBody.Contains($required)) { throw "中译西提示词缺少：$required" }
}
foreach ($body in @($englishBody, $spanishBody)) {
    if ($body.Contains('必须使用简体中文')) { throw '对外翻译仍混入强制中文约束' }
    foreach ($required in @('装备','怪物','地图','任务','多义项','证据不足')) {
        if (-not $body.Contains($required)) { throw "对外翻译防乱组词约束缺少：$required" }
    }
}

$buildOffline = $offlineType.GetMethod('BuildSystemPrompt', $all)
$offlineEnglish = [string]$buildOffline.Invoke($null, [object[]]@('中文','英语',
    "雪花镖 => Kumbi`n"))
$offlineSpanish = [string]$buildOffline.Invoke($null, [object[]]@('中文','拉美西班牙语',
    "废弃都市组队任务 => KPQ (full: Kerning Party Quest)`n"))
if (-not $offlineEnglish.Contains('R> KPQ 3/4') -or
    -not $offlineSpanish.Contains('¿Alguien para KPQ?') -or
    $offlineEnglish.Contains('必须使用简体中文') -or
    $offlineSpanish.Contains('必须使用简体中文')) {
    throw '离线备用模型的中译英/西提示词不完整或含中文冲突规则'
}

$known = $chatType.GetMethod('TryKnownChatIntentTranslationForTarget', $all)
$cases = @(
    @{ Source='废弃三缺一'; Target='英语'; Expected='R> KPQ 3/4' },
    @{ Source='废弃都市3缺1'; Target='拉美西班牙语'; Expected='R> KPQ 3/4' },
    @{ Source='废弃都市组队，3缺1'; Target='英语'; Expected='R> KPQ 3/4' },
    @{ Source='招人'; Target='英语'; Expected='R> members' },
    @{ Source='招人'; Target='拉美西班牙语'; Expected='Busco gente.' },
    @{ Source='有人做废弃吗？'; Target='英语'; Expected='Anyone for KPQ?' },
    @{ Source='有人做KPQ吗'; Target='拉美西班牙语'; Expected='¿Alguien para KPQ?' },
    @{ Source='卖雪花镖20万'; Target='英语'; Expected='S> Kumbi 200k' }
)
foreach ($case in $cases) {
    $args = [object[]]@($case.Source, $case.Target, '')
    if (-not [bool]$known.Invoke($null, $args) -or $args[2] -ne $case.Expected) {
        throw "固定玩家黑话没有稳定直出：$($case.Source) => $($args[2])"
    }
}
$ambiguous = [object[]]@('MM来吗', '英语', '')
if ([bool]$known.Invoke($null, $ambiguous)) { throw '多义简称MM被固定规则擅自展开' }

$analyzeIntent = $intentPolicyType.GetMethod('Analyze', $all)
$buildIntentConstraint = $intentPolicyType.GetMethod('BuildPromptConstraint', $all)
$normalizeIntent = $intentPolicyType.GetMethod('NormalizeTranslation', $all)
$intentCases = @(
    @{ Source='乌鲁城有人缺一个远程吗？'; Expected='PartyJoin' },
    @{ Source='有队缺牧师吗'; Expected='PartyJoin' },
    @{ Source='标飞求组废弃'; Expected='PartyJoin' },
    @{ Source='我是弓手，有队伍缺人吗'; Expected='PartyJoin' },
    @{ Source='谁的队缺输出'; Expected='PartyJoin' },
    @{ Source='还缺输出吗'; Expected='PartyJoin' },
    @{ Source='乌鲁城缺一个远程'; Expected='PartyRecruit' },
    @{ Source='队伍还缺牧师'; Expected='PartyRecruit' },
    @{ Source='射手村组队，3缺1'; Expected='PartyRecruit' },
    @{ Source='招一个标飞'; Expected='PartyRecruit' },
    @{ Source='还缺一人，有人吗'; Expected='PartyRecruit' },
    @{ Source='收一面干净枫叶盾'; Expected='Buy' },
    @{ Source='卖一组日之镖'; Expected='Sell' },
    @{ Source='枫叶盾换工地手套'; Expected='Trade' },
    @{ Source='拿枫叶盾换工地手套'; Expected='Trade' },
    @{ Source='日之镖现在多少钱'; Expected='PriceCheck' },
    @{ Source='谁能带我去乌鲁城'; Expected='RequestHelp' },
    @{ Source='我可以帮你过任务'; Expected='OfferHelp' },
    @{ Source='远程职业厉害吗'; Expected='General' },
    @{ Source='收一个牧师'; Expected='General' },
    @{ Source='队伍收人'; Expected='General' },
    @{ Source='收人'; Expected='General' },
    @{ Source='有队收牧师吗'; Expected='General' },
    @{ Source='牧师有队收人吗'; Expected='General' },
    @{ Source='我想用猫头鹰找一面干净枫叶盾'; Expected='General' },
    @{ Source='我去吃午饭'; Expected='General' }
)
foreach ($case in $intentCases) {
    $intent = $analyzeIntent.Invoke($null, @($case.Source))
    $kind = [string]$intent.GetType().GetField('Kind', $all).GetValue($intent)
    if ($kind -ne $case.Expected) {
        throw "通用玩家意图方向错误：$($case.Source) => $kind，应为 $($case.Expected)"
    }
}
$joinIntent = $analyzeIntent.Invoke($null, @('乌鲁城有人缺一个远程吗？'))
$joinConstraint = [string]$buildIntentConstraint.Invoke($null, @($joinIntent, '英语'))
if (-not $joinConstraint.Contains('本人正在求组') -or
    -not $joinConstraint.Contains('J>') -or -not $joinConstraint.Contains('不得写成R>')) {
    throw "求组视角没有作为强约束交给模型：$joinConstraint"
}
$normalizedJoin = [string]$normalizeIntent.Invoke($null,
    @('乌鲁城有人缺一个远程吗？', 'LF> ranged at Ulu City?', '英语', $joinIntent))
if ($normalizedJoin -ne 'J> ranged at Ulu City?') {
    throw "模型方向写反后没有被通用后处理纠正：$normalizedJoin"
}
$recruitIntent = $analyzeIntent.Invoke($null, @('乌鲁城缺一个远程'))
$normalizedRecruit = [string]$normalizeIntent.Invoke($null,
    @('乌鲁城缺一个远程', 'LFG Ulu ranged', '英语', $recruitIntent))
if ($normalizedRecruit -ne 'R> Ulu ranged') {
    throw "招募被模型写成求组后没有纠正：$normalizedRecruit"
}

$plausible = $chatType.GetMethod('IsPlausibleChatTranslationForTarget', $all)
if (-not [bool]$plausible.Invoke($null, @('废弃三缺一','R> KPQ 3/4','英语')) -or
    -not [bool]$plausible.Invoke($null, @('有人做废弃吗','¿Alguien para KPQ?','拉美西班牙语')) -or
    [bool]$plausible.Invoke($null, @('废弃三缺一','废弃三缺一','英语'))) {
    throw '中译英/西结果校验边界错误'
}

$constructor = $chatType.GetConstructor($all, $null, [Type[]]@($overlayType, [string]), $null)
$form = $constructor.Invoke([object[]]@($null, [string]$repoRoot))
try {
    $direction = $chatType.GetField('translationDirection', $all).GetValue($form)
    $input = $chatType.GetField('manualInput', $all).GetValue($form)
    $button = $chatType.GetField('manualTranslate', $all).GetValue($form)
    $items = @($direction.Items)
    foreach ($required in @('外语 → 中文（看聊天）','中文 → English（发消息）','中文 → Español（发消息）')) {
        if ($items -notcontains $required) { throw "AI翻译方向缺少：$required" }
    }
    if ($direction.SelectedIndex -lt 0 -or [string]::IsNullOrWhiteSpace([string]$direction.SelectedItem)) {
        throw 'AI翻译方向下拉框没有默认选中项'
    }
    if ($null -eq $input -or $button.Text -ne '翻译并复制') {
        throw 'AI翻译页面缺少中文输入或一键复制入口'
    }
    $buildGlossary = $chatType.GetMethod('BuildGlossaryForTarget', $all)
    $glossary = [string]$buildGlossary.Invoke($form, @('废弃三缺一，卖雪花镖20万', '英语'))
    if (-not $glossary.Contains('废弃（组队语境） => KPQ') -or
        -not $glossary.Contains('full: Kerning Party Quest') -or
        -not $glossary.Contains('雪花镖 => Kumbi')) {
        throw "中文反向术语检索或简称全称缺失：$glossary"
    }
    $protectTerms = $chatType.GetMethod('ProtectOutboundTerms', $all)
    $correctTerms = $chatType.GetMethod('CorrectOutboundChineseTerms', $all)
    $correctedItem = [string]$correctTerms.Invoke($form, @('收花蘑菇伞盖 50个', '英语'))
    if ($correctedItem -ne '收花蘑菇盖 50个') {
        throw "常见中文术语错字没有在交给AI前纠正：$correctedItem"
    }
    foreach ($unchanged in @('收花蘑菇盖50个','明明女士的第一个担心 任务 3缺1')) {
        $actual = [string]$correctTerms.Invoke($form, @($unchanged, '英语'))
        if ($actual -ne $unchanged) { throw "正确术语被纠错器误改：$unchanged => $actual" }
    }
    $protectArgs = [object[]]@('我刚到天空之城，有人一起刷小幽灵吗？', '拉美西班牙语', $null)
    $protectedText = [string]$protectTerms.Invoke($form, $protectArgs)
    $termTokens = $protectArgs[2]
    $tokenValues = @($termTokens.Values)
    if ($protectedText -notmatch '__FYM_TERM_\d+__' -or
        $tokenValues -notcontains 'Orbis' -or $tokenValues -notcontains 'Jr. Wraith') {
        throw "地图或怪物正式名没有在交给AI前锁定：$protectedText | $($tokenValues -join ',')"
    }
    $jobProtectArgs = [object[]]@('有队缺牧师吗？', '英语', $null)
    $null = $protectTerms.Invoke($form, $jobProtectArgs)
    if (@($jobProtectArgs[2].Values) -notcontains 'Cleric') {
        throw "职业正式名没有在交给AI前锁定：$(@($jobProtectArgs[2].Values) -join ',')"
    }
    $lockEquipmentArgs = [object[]]@('收工地手套，卖雪花镖', '英语', $null)
    $null = $protectTerms.Invoke($form, $lockEquipmentArgs)
    $lockedEquipment = @($lockEquipmentArgs[2].Values)
    if ($lockedEquipment -notcontains 'WG' -or $lockedEquipment -notcontains 'Kumbi') {
        throw "装备或道具没有优先锁定玩家简称：$($lockedEquipment -join ',')"
    }
    $tradeIntent = $chatType.GetMethod('TryProtectedOutboundTradeIntent', $all)
    $tradeArgs = [object[]]@('收__FYM_TERM_0__，10万一个', '英语',
        $lockEquipmentArgs[2], '')
    if (-not [bool]$tradeIntent.Invoke($null, $tradeArgs) -or
        $tradeArgs[3] -ne 'B> __FYM_TERM_0__ 100k') {
        throw "收购被错误写成出售或价格单位错误：$($tradeArgs[3])"
    }
    $correctedTradeProtectArgs = [object[]]@($correctedItem, '英语', $null)
    $correctedTradeProtected = [string]$protectTerms.Invoke($form, $correctedTradeProtectArgs)
    $correctedTradeArgs = [object[]]@($correctedTradeProtected, '英语',
        $correctedTradeProtectArgs[2], '')
    if (-not [bool]$tradeIntent.Invoke($null, $correctedTradeArgs)) {
        throw "纠错后的道具收购没有走确定性玩家句式：$correctedTradeProtected"
    }
    $restoreTerms = $chatType.GetMethod('RestoreProtectedTokens', $all)
    $correctedTradeResult = [string]$restoreTerms.Invoke($null,
        @($correctedTradeArgs[3], $correctedTradeProtectArgs[2]))
    if ($correctedTradeResult -ne 'B> Orange Mushroom Cap 50') {
        throw "错词纠正后的正式道具名或数量错误：$correctedTradeResult"
    }
    $gameIntent = $chatType.GetMethod('TryProtectedOutboundGameIntent', $all)
    $farmArgs = [object[]]@($protectedText, '英语', $termTokens, '')
    if (-not [bool]$gameIntent.Invoke($null, $farmArgs) -or
        $farmArgs[3] -ne 'Just got to __FYM_TERM_0__. Anyone want to farm __FYM_TERM_1__?') {
        throw "刷怪意图仍可能被模型乱加PQ：$($farmArgs[3])"
    }
    $taskProtectArgs = [object[]]@('玛雅与奇怪的药这个任务在哪接？', '拉美西班牙语', $null)
    $taskProtected = [string]$protectTerms.Invoke($form, $taskProtectArgs)
    $taskIntentArgs = [object[]]@($taskProtected, '拉美西班牙语', $taskProtectArgs[2], '')
    if (-not [bool]$gameIntent.Invoke($null, $taskIntentArgs) -or
        $taskIntentArgs[3] -notlike '*__FYM_TERM_0__?') {
        throw "任务询问没有锁定正式任务名：$($taskIntentArgs[3])"
    }
    $recruitProtectArgs = [object[]]@('明明女士的第一个担心 任务 3缺1', '英语', $null)
    $recruitProtected = [string]$protectTerms.Invoke($form, $recruitProtectArgs)
    $recruitIntentArgs = [object[]]@($recruitProtected, '英语', $recruitProtectArgs[2], '')
    if (-not [bool]$gameIntent.Invoke($null, $recruitIntentArgs)) {
        throw "具体任务名加3缺1没有走通用招募句式：$recruitProtected"
    }
    $recruitResult = [string]$restoreTerms.Invoke($null,
        @($recruitIntentArgs[3], $recruitProtectArgs[2]))
    if ($recruitResult -ne "R> Mrs. Ming Ming's First Worry 3/4") {
        throw "具体任务招募没有保留官方任务名或人数：$recruitResult"
    }
    $taskRecruitCount = 0
    $uniqueTaskNames = @{}
    $uniqueTaskIds = @{}
    foreach ($line in Get-Content (Join-Path $repoRoot '枫语幕词库.tsv') -Encoding UTF8) {
        if ($line.StartsWith('#')) { continue }
        $columns = @($line -split "`t")
        if ($columns.Count -lt 3 -or $columns[2] -notlike '怀旧服-任务#*' -or
            [string]::IsNullOrWhiteSpace($columns[1])) { continue }
        if (-not $uniqueTaskNames.ContainsKey($columns[1])) {
            $uniqueTaskNames[$columns[1]] = $columns[0]
        }
        $taskId = $columns[2].Substring('怀旧服-任务#'.Length)
        $uniqueTaskIds[$taskId] = $true
    }
    foreach ($taskName in $uniqueTaskNames.Keys) {
        $source = "$taskName 任务 3缺1"
        $corrected = [string]$correctTerms.Invoke($form, @($source, '英语'))
        if ($corrected -ne $source) { throw "正式任务名被纠错器误改：$source => $corrected" }
        $allTaskProtectArgs = [object[]]@($source, '英语', $null)
        $allTaskProtected = [string]$protectTerms.Invoke($form, $allTaskProtectArgs)
        $allTaskIntentArgs = [object[]]@($allTaskProtected, '英语', $allTaskProtectArgs[2], '')
        if (-not [bool]$gameIntent.Invoke($null, $allTaskIntentArgs)) {
            throw "任务招募通用规则未覆盖：$taskName | $allTaskProtected"
        }
        $allTaskResult = [string]$restoreTerms.Invoke($null,
            @($allTaskIntentArgs[3], $allTaskProtectArgs[2]))
        $expectedTaskResult = "R> $($uniqueTaskNames[$taskName]) 3/4"
        if ($allTaskResult -ne $expectedTaskResult) {
            throw "任务招募没有使用该任务的正式英文名：$taskName => $allTaskResult | 应为 $expectedTaskResult"
        }
        $taskRecruitCount++
    }
    if ($taskRecruitCount -lt 180) {
        throw "任务招募全库验证数量异常：$taskRecruitCount"
    }
    if ($uniqueTaskIds.Count -ne 276) {
        throw "当前任务ID审计数量异常：$($uniqueTaskIds.Count)"
    }
    $taskGlossary = [string]$buildGlossary.Invoke($form,
        @('玛雅与奇怪的药这个任务在哪接？', '拉美西班牙语'))
    if ($taskGlossary.Contains('？ =>') -or -not $taskGlossary.Contains('Maya and the Weird Medicine')) {
        throw "标点噪声混入术语或任务正式名缺失：$taskGlossary"
    }
} finally {
    $form.Dispose()
}

$dictionaryRows = Get-Content (Join-Path $repoRoot '枫语幕词库.tsv') -Encoding UTF8
foreach ($entry in @(
    @{ Source='Hene'; Target='射手村' },
    @{ Source='HHG1'; Target='射手村狩猎场 I' },
    @{ Source='WG'; Target='工地手套' },
    @{ Source='PE'; Target='超级药水' },
    @{ Source='ZMM'; Target='僵尸蘑菇王' },
    @{ Source='Orbis'; Target='天空之城' },
    @{ Source='Ludi'; Target='玩具城' },
    @{ Source='DC'; Target='掉线' },
    @{ Source='A/W'; Target='一口价' })) {
    $prefix = $entry.Source + [char]9 + $entry.Target + [char]9
    if (@($dictionaryRows | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }).Count -ne 1) {
        throw "装备/怪物/地图/交易简称缺少唯一词条：$($entry.Source)"
    }
}

$chatSource = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
foreach ($required in @('鸣谢与声明','@奇怪小鸭','四水年华','https://mscw-guidebook.com/',
    'https://henesys.gg/skills','非官方、非商业翻译辅助工具','不作任何商业用途')) {
    if (-not $chatSource.Contains($required)) { throw "AI窗口鸣谢或免责声明缺少：$required" }
}

$uiError = Join-Path $repoRoot 'ai_chat_ui_test_error.txt'
$uiImage = Join-Path $repoRoot 'ai_chat_ui_test.png'
Remove-Item -LiteralPath $uiError -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList '--ai-chat-ui-test' `
    -WorkingDirectory $repoRoot -WindowStyle Hidden -Wait
if (Test-Path -LiteralPath $uiError) {
    throw "AI翻译页面渲染失败：$(Get-Content $uiError -Raw -Encoding UTF8)"
}
if (-not (Test-Path -LiteralPath $uiImage) -or (Get-Item -LiteralPath $uiImage).Length -lt 14000) {
    throw 'AI翻译方向和输入区界面截图未生成或内容为空'
}

Write-Output "AI对外翻译：中译英/拉美西语、悬浮输入复用链路、固定玩家黑话、术语纠错及全部$($uniqueTaskIds.Count)个任务ID/$($taskRecruitCount)个唯一任务名招募通过"
