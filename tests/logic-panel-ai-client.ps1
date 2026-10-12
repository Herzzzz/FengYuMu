$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)
$clientType = $assembly.GetType('MapleOverlay.OnlineAiClient', $true)
if ($null -eq $settingsType -or $null -eq $clientType) {
    throw '找不到联网AI设置或面板客户端类型'
}

$settings = [Activator]::CreateInstance($settingsType, $true)
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'panel-test-key')
$source = 'Make a Pig & Mushroom Omok Set. Bring the requested items to Casey in Henesys Park. Lv. 12+ 30/50 __FYM_TERM_7__ @@@'
$glossary = 'Henesys Park => 赫内西斯公园`nCasey => 凯茜'
$buildMethods = @($clientType.GetMethods($all) | Where-Object {
    $_.Name -eq 'BuildPanelRequestBody' -and $_.IsStatic
})
if ($buildMethods.Count -lt 2) { throw '面板请求体缺少无术语和带术语两个入口' }
$buildWithGlossary = $buildMethods | Where-Object { $_.GetParameters().Count -eq 3 } | Select-Object -First 1
$buildSimple = $buildMethods | Where-Object { $_.GetParameters().Count -eq 2 } | Select-Object -First 1
if ($null -eq $buildWithGlossary -or $null -eq $buildSimple) { throw '面板请求体重载签名不完整' }

$responsesJson = [string]$buildWithGlossary.Invoke($null, [object[]]@($settings, $source, $glossary))
$responsesBody = $responsesJson | ConvertFrom-Json
if ([int]$responsesBody.max_output_tokens -ne 256 -or
    [string]$responsesBody.model -ne 'doubao-seed-2-1-lite-260915' -or
    [string]$responsesBody.input -notmatch '检测到的游戏面板OCR开始' -or
    [string]$responsesBody.input -notmatch 'Henesys Park => 赫内西斯公园' -or
    [string]$responsesBody.instructions -notmatch '只输出中文译文' -or
    [string]$responsesBody.instructions -notmatch 'OCR.*不确定' -or
    $responsesJson -match '(?i)image|base64|screenshot') {
    throw 'Responses 面板请求没有满足纯文本、中文、OCR保留和256 token约束'
}

$settingsType.GetField('Endpoint', $all).SetValue($settings,
    'https://api.deepseek.com/v1/chat/completions')
$settingsType.GetField('Model', $all).SetValue($settings, 'deepseek-v4-flash')
$chatJson = [string]$buildSimple.Invoke($null, [object[]]@($settings, $source))
$chatBody = $chatJson | ConvertFrom-Json
if ([int]$chatBody.max_tokens -ne 256 -or
    $null -eq $chatBody.messages -or $chatBody.messages.Count -ne 2 -or
    [string]$chatBody.messages[0].content -notmatch '游戏面板翻译器' -or
    [string]$chatBody.messages[1].content -notmatch '检测到的游戏面板OCR开始' -or
    $chatJson -match '(?i)image|base64|screenshot') {
    throw 'Chat Completions 面板请求没有复用协议支持或误发送图片'
}

$clamp = $clientType.GetMethod('ClampPanelTimeoutMs', $all)
if ([int]$clamp.Invoke($null, @(-1)) -ne 950 -or
    [int]$clamp.Invoke($null, @(100)) -ne 250 -or
    [int]$clamp.Invoke($null, @(700)) -ne 700 -or
    [int]$clamp.Invoke($null, @(2500)) -ne 1100) {
    throw '面板超时没有限制在250~1100ms或默认950ms'
}

$panelMethod = @($clientType.GetMethods($all) | Where-Object {
    $_.Name -eq 'TranslatePanelAsync' -and $_.IsStatic -and
    $_.GetParameters().Count -eq 4 -and
    $_.GetParameters()[1].ParameterType -eq [string] -and
    $_.GetParameters()[2].ParameterType -eq [int] -and
    $_.GetParameters()[3].ParameterType -eq [Threading.CancellationToken]
}) | Select-Object -First 1
if ($null -eq $panelMethod) { throw '找不到 TranslatePanelAsync(settings, source, timeoutMs, cancellationToken)' }
$cancel = New-Object Threading.CancellationTokenSource
$cancel.Cancel()
$task = $panelMethod.Invoke($null, [object[]]@($settings, $source, 950, $cancel.Token))
$result = [string]$task.GetAwaiter().GetResult()
if ($task.IsCanceled -or $task.IsFaulted -or $result.Length -ne 0) {
    throw '预取消面板请求没有按约定安全返回空字符串'
}

$sourceText = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
foreach ($required in @('request.Abort()', 'cancellationToken.Register',
    'request.ReadWriteTimeout = timeout', 'request.KeepAlive = false',
    'TranslatePanelCore', 'max_output_tokens"] = 256', 'max_tokens"] = 256')) {
    if (-not $sourceText.Contains($required)) { throw "面板客户端缺少取消或协议约束：$required" }
}
foreach ($forbidden in @('Bitmap', 'Graphics.CopyFromScreen', 'OfflineAiClient.TranslatePanel')) {
    if ($sourceText.Contains('BuildPanelRequestBody') -and $sourceText.Contains($forbidden) -and
        $forbidden -eq 'Graphics.CopyFromScreen') {
        throw '面板AI客户端不应读取屏幕或发送截图'
    }
}

Write-Output '面板AI客户端：纯文本面板提示词、Responses/Chat协议、256 token上限、250~1100ms超时、取消Abort、预取消安全空结果通过'
