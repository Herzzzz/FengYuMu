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
$settings = [Activator]::CreateInstance($settingsType, $true)
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'test-key')

$buildOnline = $onlineType.GetMethod('BuildRequestBody', $all)
$englishBody = [string]$buildOnline.Invoke($null, [object[]]@($settings,
    '废弃三缺一', '英语', "废弃都市组队任务 => KPQ (full: Kerning Party Quest)`n"))
$spanishBody = [string]$buildOnline.Invoke($null, [object[]]@($settings,
    '有人做废弃吗', '拉美西班牙语', "废弃都市组队任务 => KPQ (full: Kerning Party Quest)`n"))
foreach ($required in @('KPQ 3/4','members','国际服正式名称','Kerning Party Quest')) {
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
    $protectArgs = [object[]]@('我刚到天空之城，有人一起刷小幽灵吗？', '拉美西班牙语', $null)
    $protectedText = [string]$protectTerms.Invoke($form, $protectArgs)
    $termTokens = $protectArgs[2]
    $tokenValues = @($termTokens.Values)
    if ($protectedText -notmatch '__FYM_TERM_\d+__' -or
        $tokenValues -notcontains 'Orbis' -or $tokenValues -notcontains 'Jr. Wraith') {
        throw "地图或怪物正式名没有在交给AI前锁定：$protectedText | $($tokenValues -join ',')"
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

Write-Output 'AI对外翻译：中译英/拉美西语、中文输入并复制、固定玩家黑话、中文反向术语、简称全称和多义项防乱猜通过'
