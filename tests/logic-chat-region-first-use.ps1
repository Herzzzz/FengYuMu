$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
Add-Type -AssemblyName System.Drawing
$assembly = [Reflection.Assembly]::LoadFile($exe)

$settings = $assembly.GetType('MapleOverlay.ChatRegionSettings', $true)
$defaultMethod = $settings.GetMethod('DefaultForGame',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$game = New-Object System.Drawing.Rectangle -ArgumentList 100, 50, 1600, 900
$arguments = New-Object 'object[]' 1
$arguments[0] = $game
$region = [System.Drawing.Rectangle]$defaultMethod.Invoke($null, $arguments)
if ($region -ne (New-Object System.Drawing.Rectangle -ArgumentList 260, 734, 880, 189)) {
    throw "首次快捷键自动聊天区比例错误：$region"
}
$usableMethod = $settings.GetMethod('IsUsable',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$singleLine = New-Object System.Drawing.Rectangle -ArgumentList 172,600,391,24
$tooShort = New-Object System.Drawing.Rectangle -ArgumentList 172,600,391,19
$singleLineArguments = [object[]]@($singleLine.PSObject.BaseObject)
$tooShortArguments = [object[]]@($tooShort.PSObject.BaseObject)
if (-not [bool]$usableMethod.Invoke($null, $singleLineArguments) -or
    [bool]$usableMethod.Invoke($null, $tooShortArguments)) {
    throw '用户手动框选的单行24像素聊天区可用性判定错误'
}

$detector = $assembly.GetType('MapleOverlay.ChatRegionDetector', $true)
$detectMethod = $detector.GetMethod('Detect',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$lineList = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
$lineList.Add((New-Object System.Drawing.RectangleF -ArgumentList 430, 780, 650, 25))
$lineList.Add((New-Object System.Drawing.RectangleF -ArgumentList 430, 820, 700, 25))
$detectArguments = New-Object 'object[]' 2
$detectArguments[0] = $lineList.PSObject.BaseObject
$detectArguments[1] = $game
$detected = [System.Drawing.Rectangle]$detectMethod.Invoke($null, $detectArguments)
if ($detected -ne (New-Object System.Drawing.Rectangle -ArgumentList 411, 699, 751, 215)) {
    throw "聊天行自适应边界错误：$detected"
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$chatSource = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'ChatRegionOrigin.Automatic',
    'ChatRegionOrigin.Manual',
    'if (HasSelection()) return ResolveForGame(gameBounds);',
    'ChatRegionSettings.SaveAutomatic(',
    'ChatRegionDetector.Detect(chatLineBounds, gameBounds)',
    'FindPlayerChatLineBounds(result,',
    'ChatRegionSettings.ResaveForGame(resolved, gameBounds)',
    'Rectangle chatExclusion = GetChatExclusionBounds()',
    'SceneClassifier.LooksLikePlayerChat(candidate.Text)')) {
    if (-not $source.Contains($required)) { throw "首次聊天区锁定接入缺少：$required" }
}
foreach ($required in @(
    'ChatRegionSettings.SaveManual(chatRegion, game)',
    'await overlay.CaptureChatAsync(chatRegion)',
    '首次快捷键自动聊天区')) {
    if (-not $chatSource.Contains($required)) { throw "AI聊天区接入缺少：$required" }
}
if ($source.Contains('ChatRegionSettings.Save(resolved, gameBounds)')) {
    throw '游戏窗口缩放仍可能丢失聊天区来源'
}

Write-Output '聊天区：首次快捷键自动锁定、普通截图排除、AI复用、手动框选永久优先通过'
