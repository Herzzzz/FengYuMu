$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$assembly = [Reflection.Assembly]::LoadFile($exe)
$chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$parse = $chatType.GetMethod('ParseChatLines', $flags)

function Invoke-Parse([string]$text) {
    $invokeArgs = [object[]]::new(1)
    $invokeArgs[0] = $text
    @($parse.Invoke($null, $invokeArgs))
}

$broadcast = Invoke-Parse "Friehd MisoSMELLS has0öq ed in`nCupidKillsNL O : Madpeset is scamming people mass defarne this game!`nMad eset O : SYBUA ou said that eo le farneu I eo le and ou look 0k"
if ($broadcast.Count -ne 3 -or $broadcast[0] -notlike 'MisoSMELLS:*' -or
    $broadcast[1] -notlike 'CupidKillsNL:*' -or $broadcast[2] -notlike 'Madeset:*' -or
    @($broadcast | Where-Object { $_ -like 'SYBUA:*' }).Count -ne 0) {
    throw "喇叭/好友行的人名边界错误：$($broadcast -join ' | ')"
}

$history = Invoke-Parse "'y'0Pl() thxs`n[Friendl Arrowshot has logged in.`nKEI-PYG"
if ($history.Count -ne 2 -or $history[0] -notlike 'y0Pl:*' -or
    $history[1] -notlike 'Arrowshot:*') {
    throw "聊天图标或好友上线行恢复失败：$($history -join ' | ')"
}

$overlap = Invoke-Parse "Levl`n• Weapon Def.: +48`nAccuracy: +14 (4+10)`n• Remaining Enhancements: O`nPobe() '\hit`n_lhiend) MisoSMELLS has loqaed In.`nAll`npearman`nHPaossh`ncnat`nMP(S629892'"
if ($overlap.Count -ne 2 -or $overlap[0] -notlike 'Pobe:*' -or
    $overlap[1] -notlike 'MisoSMELLS:*' -or
    @($overlap | Where-Object { $_ -match 'Weapon Def|Accuracy|Enhancements' }).Count -ne 0) {
    throw "装备面板文字混入AI聊天：$($overlap -join ' | ')"
}

$internalColon = @(Invoke-Parse 'Raea CH01: 3 words: Suck my D')
if ($internalColon.Count -ne 1 -or $internalColon[0] -ne 'Raea: 3 words: Suck my D') {
    throw "消息正文中的words冒号被误当成第二名玩家：$($internalColon -join ' | ')"
}
$itemNoise = @(Invoke-Parse "Lunar Pixie:s Moonpiece`nStar Pixie's Starpiece")
if ($itemNoise.Count -ne 0) {
    throw "物品名中的Pixie's被误当成玩家：$($itemNoise -join ' | ')"
}
$flattened = @(Invoke-Parse 'Chadson CH01: hello TugaStyle CH02: hi')
if ($flattened.Count -ne 2 -or $flattened[0] -notlike 'Chadson:*' -or
    $flattened[1] -notlike 'TugaStyle:*') {
    throw "同一OCR行里的两个带频道聊天没有拆开：$($flattened -join ' | ')"
}

$detectLanguage = $chatType.GetMethod('DetectChatSourceLanguage', $flags)
if ([string]$detectLanguage.Invoke($null, @('first it was the JR wraiths')) -ne '英语' -or
    [string]$detectLanguage.Invoke($null, @('今天组队吗')) -ne '中文') {
    throw 'AI聊天源语言快速判定失败'
}
$usefulLine = $chatType.GetMethod('IsUsefulChatLine', $flags)
if ([bool]$usefulLine.Invoke($null, @('wolfly: u t')) -or
    -not [bool]$usefulLine.Invoke($null, @('Player: gg')) -or
    -not [bool]$usefulLine.Invoke($null, @('WOIffy: first it was the JR wraiths'))) {
    throw '短OCR碎片过滤误伤有效聊天或放过无效碎片'
}

$fixtures = @(
    @{ Name='broadcast'; File='ai-chat-cloudpark-broadcast.png'; Required='PARSED \| CupidKillsNL:'; Forbidden='PARSED \| SYBUA:' },
    @{ Name='history'; File='ai-chat-cloudpark-history.png'; Required='PARSED \| Arrowshot:'; Forbidden='PARSED \| Arrow:' },
    @{ Name='equipment-overlap'; File='ai-chat-cloudpark-equipment-overlap.png'; Required='PARSED \| MisoSMELLS:'; Forbidden='PARSED \| (?:Accuracy|Enhancements|Remaining|Weapon Def):' },
    @{ Name='user-feedback-20260908'; File='ai-chat-user-feedback-20260908.png'; Required='PARSED \| WOIffy:'; Forbidden='PARSED \| (?:Pixie|words):' }
)
foreach ($fixture in $fixtures) {
    $image = Join-Path $repoRoot (Join-Path 'tests\fixtures' $fixture.File)
    if (-not (Test-Path -LiteralPath $image)) { throw "缺少AI聊天实图：$image" }
    Start-Process -FilePath $exe -ArgumentList "--chat-style-benchmark=$image" `
        -WorkingDirectory $repoRoot -Wait
    $report = Get-Content (Join-Path $repoRoot 'chat_style_benchmark.txt') -Raw -Encoding UTF8
    if ($report -notmatch $fixture.Required) { throw "$($fixture.Name) 实图未识别关键聊天行：$report" }
    if ($report -match $fixture.Forbidden) { throw "$($fixture.Name) 实图仍有错误聊天行：$report" }
}

$sweepFolder = Join-Path $repoRoot 'tests\fixtures\ai-chat-sweep'
$sweepImages = @(Get-ChildItem -LiteralPath $sweepFolder -Filter '*.png' | Sort-Object Name)
if ($sweepImages.Count -lt 17) { throw "AI聊天广覆盖实图不足：$($sweepImages.Count)/17" }
$sweepParsedFrames = 0
$sweepParsedLines = 0
$sweepResults = @{}
foreach ($image in $sweepImages) {
    Start-Process -FilePath $exe -ArgumentList "--chat-style-benchmark=$($image.FullName)" `
        -WorkingDirectory $repoRoot -Wait
    $report = Get-Content (Join-Path $repoRoot 'chat_style_benchmark.txt') -Encoding UTF8
    $parsedLines = @($report | Where-Object { $_ -like 'PARSED | *' })
    $parsedValues = [Collections.Generic.List[string]]::new()
    foreach ($parsedLine in $parsedLines) { $parsedValues.Add($parsedLine.Substring(9)) }
    $sweepResults[$image.BaseName] = $parsedValues
    if ($parsedLines.Count -gt 0) { $sweepParsedFrames++; $sweepParsedLines += $parsedLines.Count }
    if (@($parsedLines | Where-Object {
        $_ -match '^PARSED \| (?:Attack|Weapon Def|Magic Def|Accuracy|Enhancements|Remaining Enhancements|Type|Level|Job|STR|DEX|INT|LUK):'
    }).Count -ne 0) {
        throw "$($image.Name) 广覆盖实图混入结构化面板字段：$($parsedLines -join ' | ')"
    }
}
if ($sweepParsedFrames -lt 15 -or $sweepParsedLines -lt 30) {
    throw "AI聊天广覆盖识别量异常：帧=$sweepParsedFrames 行=$sweepParsedLines"
}

$getNew = $chatType.GetMethod('GetNewChatLines', $flags)
$suppressRecent = $chatType.GetMethod('SuppressRecentOcrReappearances', $flags)
$recentType = [Collections.Generic.List``1].MakeGenericType([Collections.Generic.List[string]])
$recent = [Activator]::CreateInstance($recentType)
$previous = [Collections.Generic.List[string]]::new()
$sequenceAdded = 0
foreach ($stamp in @('140.0','150.0','160.0','170.0','180.0','190.0','200.0','210.0','220.0','230.0','240.0')) {
    $current = $sweepResults["qq-$stamp"]
    $newArgs = [object[]]@($previous, $current)
    $candidates = $getNew.Invoke($null, $newArgs)
    $filterArgs = [object[]]@($recent, $previous, $current, $candidates)
    $filtered = @($suppressRecent.Invoke($null, $filterArgs))
    $sequenceAdded += $filtered.Count
    $recent.Add([Collections.Generic.List[string]]::new($current))
    while ($recent.Count -gt 3) { $recent.RemoveAt(0) }
    $previous = $current
}
if ($sequenceAdded -ne 5) {
    throw "连续视频帧防重复失效：11帧应产生5条真正新增，实际$sequenceAdded条"
}

Write-Output "AI聊天准确性：4张问题实图（含用户新反馈）+ $($sweepImages.Count)张跨视频实图通过，广覆盖命中${sweepParsedFrames}帧/${sweepParsedLines}行，连续11帧仅保留${sequenceAdded}条真正新增"
