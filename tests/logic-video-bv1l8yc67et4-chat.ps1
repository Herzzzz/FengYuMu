$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$corpusPath = Join-Path $PSScriptRoot 'video-bv1l8yc67et4-chat-corpus.tsv'
$assembly = [Reflection.Assembly]::LoadFile($exe)
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$onlineType = $assembly.GetType('MapleOverlay.OnlineAiClient', $true)
$offlineType = $assembly.GetType('MapleOverlay.OfflineAiClient', $true)
$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)

$constructor = $chatType.GetConstructor($all, $null, [Type[]]@($overlayType, [string]), $null)
$form = $constructor.Invoke([object[]]@($null, [string]$repoRoot))
try {
    $exact = $chatType.GetMethod('TryExactGlossaryTranslation', $all)
    $checked = 0
    foreach ($row in Get-Content $corpusPath -Encoding UTF8) {
        if ([String]::IsNullOrWhiteSpace($row) -or $row.StartsWith('#')) { continue }
        $parts = $row.Split([char]9)
        if ($parts.Count -ne 3) { throw "视频语料格式错误：$row" }
        $args = [object[]]@($parts[1], '')
        if (-not [bool]$exact.Invoke($form, $args) -or $args[1] -ne $parts[2]) {
            throw "视频语料未稳定直出：$($parts[0]) $($parts[1]) => $($args[1])"
        }
        $checked++
    }
    if ($checked -lt 30) { throw "视频语料数量不足：$checked" }

    $lockable = $chatType.GetField('lockableGlossaryKeys', $all).GetValue($form)
    if ($lockable.Contains('any spot?') -or $lockable.Contains('low pots, gotta repot')) {
        throw '完整聊天短句仍被当作专有名词锁定，会污染中译英/西'
    }
    foreach ($term in @('KPQ', 'Pig''s Head', 'repot')) {
        if (-not $lockable.Contains($term)) { throw "应锁定的游戏术语缺失：$term" }
    }

    $protect = $chatType.GetMethod('ProtectOutboundTerms', $all)
    $protectArgs = [object[]]@('还有位置吗？', '拉美西班牙语', $null)
    $protected = [string]$protect.Invoke($form, $protectArgs)
    if ($protected.Contains('__FYM_TERM_') -or @($protectArgs[2].Values).Count -ne 0) {
        throw "普通聊天短句被错误锁成英文：$protected"
    }
} finally {
    $form.Dispose()
}

$known = $chatType.GetMethod('TryKnownChatIntentTranslationForTarget', $all)
$outbound = @(
    @{ Source='还有位置吗？'; Target='英语'; Expected='Any spot?' },
    @{ Source='还有位置吗？'; Target='拉美西班牙语'; Expected='¿Hay espacio?' },
    @{ Source='没事，我换频道'; Target='英语'; Expected="np, I'll cc." },
    @{ Source='药水不多了，我得补药'; Target='拉美西班牙语'; Expected='Me quedan pocas pociones; voy a comprar más.' },
    @{ Source='离30级还差多久？'; Target='英语'; Expected='How long till 30?' },
    @{ Source='50个多少钱？'; Target='拉美西班牙语'; Expected='¿Cuánto por 50?' },
    @{ Source='能8万卖吗？'; Target='英语'; Expected='Can u do 80k?' },
    @{ Source='抱歉，我英语不太好，在用翻译器'; Target='英语'; Expected="Sorry, my English isn't great. I'm using a translator." }
)
foreach ($case in $outbound) {
    $args = [object[]]@($case.Source, $case.Target, '')
    if (-not [bool]$known.Invoke($null, $args) -or $args[2] -ne $case.Expected) {
        throw "视频玩家口吻没有稳定直出：$($case.Source) => $($args[2])"
    }
}

$useful = $chatType.GetMethod('IsUsefulChatLine', $all)
foreach ($line in @('Jake: gj', 'Jake: ks', 'Jake: cc', 'Jake: pt', 'Jake: J>')) {
    if (-not [bool]$useful.Invoke($null, @($line))) { throw "短聊天仍会漏抓：$line" }
}

$settings = [Activator]::CreateInstance($settingsType, $true)
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'test-key')
$onlineBody = [string]$onlineType.GetMethod('BuildRequestBody', $all).Invoke($null,
    [object[]]@($settings, '药水不多了，我得补药', '英语', ''))
$offlinePrompt = [string]$offlineType.GetMethod('BuildSystemPrompt', $all).Invoke($null,
    [object[]]@('英语', '简体中文', ''))
foreach ($required in @('repot', 'rebuff', 'short on dps', 'cc')) {
    if (-not $onlineBody.Contains($required) -or -not $offlinePrompt.Contains($required)) {
        throw "在线或离线提示词缺少视频语境约束：$required"
    }
}

Write-Output "BV1L8Yc67Et4 玩家英语回归通过：$checked 条视频短句、短黑话抓取、英西直出和术语保护边界"
