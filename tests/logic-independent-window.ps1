$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
Add-Type -AssemblyName System.Drawing
$assembly = [Reflection.Assembly]::LoadFile($exe)
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'

$contentType = $assembly.GetType('MapleOverlay.AiTranslationWindowContent', $true)
$append = $contentType.GetMethod('Append', $flags)
$text = [string]$append.Invoke($null, @('', '  Arthur： hello   world  '))
$text = [string]$append.Invoke($null, @($text, 'Maya： second line'))
$lines = @($text -split "`r?`n")
if ($lines.Count -ne 2 -or $lines[0] -ne 'Arthur： hello world' -or
    $lines[1] -ne 'Maya： second line') {
    throw "AI浮窗没有按到达顺序追加并规整译文：$text"
}

$styleType = $assembly.GetType('MapleOverlay.ChatVisualStylePolicy', $true)
$fromSample = $styleType.GetMethod('FromSample', $flags)
$green = [System.Drawing.Color]::FromArgb(82, 210, 112)
$blue = [System.Drawing.Color]::FromArgb(141, 170, 179)
$pink = [System.Drawing.Color]::FromArgb(184, 48, 106)
$normal = $fromSample.Invoke($null, @('PartyMember: hello', $green, [System.Drawing.Color]::Empty, $false))
$megaphone = $fromSample.Invoke($null, @('Joey: where do I bring the magic box?',
    [System.Drawing.Color]::FromArgb(33, 65, 91), $blue, $true))
$broadcast = $fromSample.Invoke($null, @("DunkChai's Gift-filled Message: hello", [System.Drawing.Color]::White, $pink, $true))
$instance = [Reflection.BindingFlags]'Instance,NonPublic,Public'
if ($normal.GetType().GetField('HasBackground', $instance).GetValue($normal)) {
    throw '普通聊天行被错误加上喇叭背景'
}
if (-not $broadcast.GetType().GetField('HasBackground', $instance).GetValue($broadcast)) {
    throw '喇叭行没有保留彩色文字背景'
}
if (-not $megaphone.GetType().GetField('HasBackground', $instance).GetValue($megaphone)) {
    throw '普通喇叭没有保留蓝色文字背景'
}
$megaphoneKind = [string]$megaphone.GetType().GetField('Kind', $instance).GetValue($megaphone)
$superKind = [string]$broadcast.GetType().GetField('Kind', $instance).GetValue($broadcast)
if ($megaphoneKind -ne 'Megaphone' -or $superKind -ne 'SuperMegaphone') {
    throw "蓝底普通喇叭与粉底超级喇叭未被分别识别：$megaphoneKind/$superKind"
}
$normalColor = [System.Drawing.Color]$normal.GetType().GetField('ForeColor', $instance).GetValue($normal)
if ($normalColor.G -le $normalColor.R -or $normalColor.G -le $normalColor.B) {
    throw "聊天来源颜色未保留：$normalColor"
}

$sourceColors = @(
    @{ Name='普通'; Text='Player: hello'; Color=[System.Drawing.Color]::FromArgb(235,235,235) },
    @{ Name='好友'; Text='Buddy: hello'; Color=[System.Drawing.Color]::FromArgb(235,151,62) },
    @{ Name='组队'; Text='Party: hello'; Color=[System.Drawing.Color]::FromArgb(194,143,235) },
    @{ Name='家族'; Text='Guild: hello'; Color=[System.Drawing.Color]::FromArgb(120,70,160) },
    @{ Name='联盟'; Text='Alliance: hello'; Color=[System.Drawing.Color]::FromArgb(126,205,126) },
    @{ Name='悄悄话'; Text='Whisper: hello'; Color=[System.Drawing.Color]::FromArgb(50,150,80) },
    @{ Name='系统'; Text='System: warning'; Color=[System.Drawing.Color]::FromArgb(225,115,145) },
    @{ Name='公告'; Text='[Notice] hello'; Color=[System.Drawing.Color]::FromArgb(220,190,90) }
)
$renderedColors = @()
foreach ($sample in $sourceColors) {
    $style = $fromSample.Invoke($null, @($sample.Text, $sample.Color,
        [System.Drawing.Color]::Empty, $false))
    if ($style.GetType().GetField('HasBackground', $instance).GetValue($style)) {
        throw "$($sample.Name)字体颜色被错误识别成喇叭背景"
    }
    $renderedColors += ([System.Drawing.Color]$style.GetType().GetField('ForeColor', $instance).GetValue($style)).ToArgb()
}
if (@($renderedColors | Select-Object -Unique).Count -ne $sourceColors.Count) {
    throw '普通/好友/组队/家族/联盟/悄悄话/系统/公告字体颜色被错误归并'
}

$overlaySource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$chatSource = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
$forms = Get-Content (Join-Path $repoRoot 'src\SimpleForms.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'internal async Task<ChatCaptureFrame> CaptureChatAsync',
    'SampleChatVisualStyle(bitmap, line, scale)',
    'if (shuttingDown || !visibleTranslation) return;')) {
    if (-not $overlaySource.Contains($required)) { throw "AI聊天样式采样接入缺少：$required" }
}
foreach ($required in @(
    'Queue<PendingChatLine>',
    'Style = capture.FindStyle(line)',
    'floatingWindow.AppendTranslation(translation, visualStyle)',
    'internal void ApplyFloatingWindow(bool enabled)')) {
    if (-not $chatSource.Contains($required)) { throw "AI浮窗链路缺少：$required" }
}
foreach ($required in @(
    'AI实时翻译独立浮窗（跟随聊天颜色）',
    'RichTextBox content',
    'FormBorderStyle = FormBorderStyle.None',
    'Opacity = 0.92d',
    'WmNcHitTest',
    'HtBottomRight',
    'AiChatWindowLayoutVersion',
    'SelectionBackColor',
    'ChatVisualKind.Megaphone',
    'ChatVisualKind.SuperMegaphone',
    'overlay.ApplyAiChatFloatingWindow')) {
    if (-not $forms.Contains($required)) { throw "AI浮窗界面缺少：$required" }
}
if ($overlaySource.Contains('!visibleTranslation || aiChatFloatingWindowEnabled')) {
    throw 'AI浮窗仍在抑制F8原位覆盖'
}

Add-Type -AssemblyName System.Windows.Forms
$offlineType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$floatingType = $assembly.GetType('MapleOverlay.AiTranslationWindowForm', $true)
$constructorFlags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$offlineConstructor = $offlineType.GetConstructor($constructorFlags, $null,
    [Type[]]@($overlayType, [string]), $null)
$floatingConstructor = $floatingType.GetConstructor($constructorFlags, $null,
    [Type[]]@($overlayType), $null)
$missingAiRoot = Join-Path $repoRoot 'tests\__ai_close_behavior_no_model__'
$chatWindow = $offlineConstructor.Invoke([object[]]@($null, [string]$missingAiRoot))
$floatingWindow = $floatingConstructor.Invoke([object[]]@($null))
try {
    $appendTranslation = $floatingType.GetMethod('AppendTranslation', $constructorFlags,
        $null, [Type[]]@([string]), $null)
    [void]$appendTranslation.Invoke($floatingWindow, @('Arthur：主窗口关闭后仍继续显示。'))
    $offlineType.GetField('floatingWindow', $constructorFlags).SetValue($chatWindow, $floatingWindow)
    $offlineType.GetField('floatingWindowEnabled', $constructorFlags).SetValue($chatWindow, $true)
    $offlineType.GetField('live', $constructorFlags).SetValue($chatWindow, $true)
    $liveTimer = $offlineType.GetField('timer', $constructorFlags).GetValue($chatWindow)
    $liveTimer.Start()
    $chatWindow.Show()
    $floatingWindow.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $chatWindow.Close()
    [System.Windows.Forms.Application]::DoEvents()
    if ($chatWindow.Visible -or
        -not [bool]$offlineType.GetField('live', $constructorFlags).GetValue($chatWindow) -or
        -not $liveTimer.Enabled -or -not $floatingWindow.Visible) {
        throw '关闭AI主窗口后，后台实时翻译或独立浮窗被错误停止'
    }
}
finally {
    $offlineType.GetMethod('StopService', $constructorFlags).Invoke($chatWindow, @()) | Out-Null
    $chatWindow.Dispose()
    if (-not $floatingWindow.IsDisposed) { $floatingWindow.Dispose() }
}

$errorPath = Join-Path $repoRoot 'translation_window_ui_test_error.txt'
$imagePath = Join-Path $repoRoot 'translation_window_ui_test.png'
Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList '--translation-window-ui-test' `
    -WorkingDirectory $repoRoot -Wait
if (Test-Path -LiteralPath $errorPath) {
    throw "AI浮窗界面自检失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
}
if (-not (Test-Path -LiteralPath $imagePath) -or (Get-Item -LiteralPath $imagePath).Length -lt 5000) {
    throw 'AI浮窗界面截图未生成或内容为空'
}

Write-Output 'AI实时浮窗：主窗口关闭不断流、自由缩放、半透明游戏风格、来源颜色、蓝底普通喇叭、粉底超级喇叭、置顶无焦点与F8隔离通过'
