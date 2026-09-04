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

function Assert-NotStructured([string]$text, [bool]$equipment, [bool]$skill) {
    $args = [object[]]@($text, $equipment, $skill, $null)
    if ([bool]$structured.Invoke($overlay, $args)) {
        throw "普通职业文字被误判成结构化装备：$text -> $($args[3])"
    }
}

Assert-Structured 'REQ LEV : 2S REQ STR : O REQ DEX : O REQ INT : O REQ LUK : O' $true $false `
    @('需要等级：25','需要力量：0','需要敏捷：0','需要智力：0','需要运气：0')
Assert-Structured 'REQ LEV : SO REQ STR : O REQ DEX : O REQ INT : O REQ LUK : O' $true $false `
    @('需要等级：50','需要力量：0','需要敏捷：0','需要智力：0','需要运气：0')
Assert-Structured 'MAGIC 38' $true $false @('魔法防御力：38')
Assert-NotStructured 'Spearman' $true $false
Assert-NotStructured 'Bowman' $true $false
Assert-NotStructured 'Assassin Wizard Cleric Bowman Thief Pirate' $false $false

$characterPolicy = $assembly.GetType('MapleOverlay.CharacterPanelPolicy', $true)
$isInformation = $characterPolicy.GetMethod('IsInformation', [Reflection.BindingFlags]'Static,NonPublic,Public')
$isStatistics = $characterPolicy.GetMethod('IsStatistics', [Reflection.BindingFlags]'Static,NonPublic,Public')
$characterInfo = 'CHARACTER INFO CITIZENSHIP LEVEL JOB FAME GUILD REQUEST PARTY REQUEST TRADE SHOW PET INFO'
if (-not [bool]$isInformation.Invoke($null, @($characterInfo))) {
    throw '角色信息面板未被识别'
}
if ([bool]$isStatistics.Invoke($null, @($characterInfo))) {
    throw '角色信息面板仍被错误归入角色属性面板'
}
$characterStats = 'CHARACTER STAT NAME JOB LEVEL HP MP EXP FAME STR DEX INT LUK ACCURACY EVASION'
if (-not [bool]$isStatistics.Invoke($null, @($characterStats))) {
    throw '角色属性面板未被识别'
}

$sceneType = $assembly.GetType('MapleOverlay.SceneClassifier', $true)
$classify = $sceneType.GetMethod('Classify', [Reflection.BindingFlags]'Static,NonPublic,Public')
$looksLikePlayerChat = $sceneType.GetMethod('LooksLikePlayerChat',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
foreach ($case in @(
    [pscustomobject]@{ Text='[Master Level : 30]'; Flag='Skill' },
    [pscustomobject]@{ Text='REQ LEV : 50 REQ STR : 0'; Flag='Item' },
    [pscustomobject]@{ Text='Quest Helper (1/5)'; Flag='Quest' },
    [pscustomobject]@{ Text='CHARACTER STAT'; Flag='Character' },
    [pscustomobject]@{ Text='NEXT'; Flag='Dialogue' },
    [pscustomobject]@{ Text='Put your game face on, ace!'; Flag='Dialogue' },
    [pscustomobject]@{ Text='MapleRoyals notice Jack Put your game face on, ace! event starts soon'; Flag='Dialogue' })) {
    $evidence = $classify.Invoke($null, @($case.Text, $store, $true))
    $kinds = [string]$evidence.GetType().GetField('Kinds').GetValue($evidence)
    if (-not ($kinds -split ', ' -contains $case.Flag)) {
        throw "场景分类错误：$($case.Text) -> $kinds"
    }
}

$flattenedDialogue = (('MapleRoyals notice ' * 18) + 'Jack Put your game face on, ace! event starts soon')
$flattenedEvidence = $classify.Invoke($null, @($flattenedDialogue, $store, $true))
$flattenedKinds = [string]$flattenedEvidence.GetType().GetField('Kinds').GetValue($flattenedEvidence)
if (-not ($flattenedKinds -split ', ' -contains 'Dialogue')) {
    throw "长行中的完整 NPC 对话未被识别：$flattenedKinds"
}

foreach ($chatLine in @(
    'BeelsFad O : T> Int Bamboo Hat for Dex Bamboo Hat',
    'Tenz • S> Garnet/Aqua Ore 800ea Garnet/Aqua 1.2kea',
    'Sushi : fairy is 40/45',
    'Sushi : Put your game face on, ace!',
    'Toph O : Clicking NPC shows no dialogue window disables all controls.')) {
    if (-not [bool]$looksLikePlayerChat.Invoke($null, @($chatLine))) {
        throw "玩家聊天行未被隔离：$chatLine"
    }
    $evidence = $classify.Invoke($null, @($chatLine, $store, $true))
    $kinds = [string]$evidence.GetType().GetField('Kinds').GetValue($evidence)
    if ($kinds -split ', ' -contains 'Item') {
        throw "聊天中的物品名仍被误判成装备场景：$chatLine -> $kinds"
    }
}
foreach ($structuredLine in @('REQ LEV : 50 REQ STR : 0', 'Type: Hat')) {
    if ([bool]$looksLikePlayerChat.Invoke($null, @($structuredLine))) {
        throw "装备结构行被错误隔离成聊天：$structuredLine"
    }
}
$itemDescription = 'A legendary potion. Restores 35% of HP and MP, but due to its powerful effect, there is a cooldown before it can be used again.'
$itemEvidence = $classify.Invoke($null, @($itemDescription, $store, $true))
$itemKinds = [string]$itemEvidence.GetType().GetField('Kinds').GetValue($itemEvidence)
if (-not ($itemKinds -split ', ' -contains 'Item')) {
    throw "真实物品说明未进入装备物品场景：$itemKinds"
}

$rushFragment = 'Rushes rward a certain distance in the direction you are facing. It there are monsters within the rush range.'
$rushEvidence = $classify.Invoke($null, @($rushFragment, $store, $true))
if (-not [bool]$rushEvidence.GetType().GetField('SkillDetail').GetValue($rushEvidence)) {
    throw '技能正文片段没有进入第一优先级详情框'
}
$classify.Invoke($null, @($rushFragment, $store, $true)) | Out-Null
$cacheField = $storeType.GetField('skillClassificationCache',
    [Reflection.BindingFlags]'Instance,NonPublic')
$cache = $cacheField.GetValue($store)
if ($cache.Count -lt 1) { throw '技能场景分类缓存没有生效' }

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$sceneSource = Get-Content (Join-Path $repoRoot 'src\SceneRecognition.cs') -Raw -Encoding UTF8
foreach ($forbidden in @('ReadProcessMemory','WriteProcessMemory','CreateRemoteThread',
    'VirtualAllocEx','SetWindowsHookEx','SendInput')) {
    if (($source + $sceneSource).Contains($forbidden)) { throw "安全边界失败：$forbidden" }
}
if (-not $source.Contains('SceneClassifier.LooksLikePlayerChat(candidate.Text)')) {
    throw '玩家聊天隔离未接入覆盖标签生成入口'
}
Write-Output 'v3.0 场景分类、结构化 OCR 数值纠错与安全边界回归：通过'
