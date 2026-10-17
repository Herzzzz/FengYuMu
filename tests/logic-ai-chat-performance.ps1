param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = if($ExePath){$ExePath}else{Join-Path $repoRoot 'bin/candidate-r3/枫语幕.exe'}
$reportPath = Join-Path $repoRoot 'chat_style_benchmark.txt'

function Test-ChatStyleFrame([string]$fileName, [string[]]$requiredPatterns) {
    $imagePath = Join-Path $PSScriptRoot (Join-Path 'fixtures' $fileName)
    if (-not (Test-Path -LiteralPath $imagePath)) { throw "聊天样式实帧不存在：$imagePath" }
if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Start-ProductExe.ps1') }
    $process = Start-ProductExe -FilePath $exe `
        -ArgumentList ('--chat-style-benchmark=' + $imagePath) `
        -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "聊天样式实帧测试失败：$fileName" }
    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8
    $elapsed = [regex]::Match($report, 'elapsed_ms=(\d+)')
    if (-not $elapsed.Success -or [int]$elapsed.Groups[1].Value -gt 2000) {
        throw "聊天样式识别耗时异常：$fileName / $report"
    }
    foreach ($pattern in $requiredPatterns) {
        if ($report -notmatch $pattern) { throw "聊天样式未命中 $pattern：$fileName / $report" }
    }
}

Test-ChatStyleFrame 'chat-blue-megaphone-video.png' @(
    'BAND/Megaphone[^\r\n]*event'
)
Test-ChatStyleFrame 'chat-pink-super-megaphone-video.png' @(
    'BAND/SuperMegaphone[^\r\n]*Toph',
    'BAND/SuperMegaphone[^\r\n]*BeelsFad',
    'BAND/Megaphone[^\r\n]*Kena'
)

if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1') }
$assembly = Load-TestAssembly $exe
$instance = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$offlineType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$constructor = $offlineType.GetConstructor($instance, $null,
    [Type[]]@($overlayType, [string]), $null)
$form = $constructor.Invoke([object[]]@($null, [string]$repoRoot))
try {
    $timer = $offlineType.GetField('timer', $instance).GetValue($form)
    if ($timer.Interval -ne 117) { throw "AI聊天轮询不是117ms：$($timer.Interval)" }

    $remember = $offlineType.GetMethod('RememberTranslation', $instance)
    foreach ($index in 0..299) {
        $arguments = [object[]]@([string]('cache-' + $index), [string]('译文-' + $index))
        $remember.Invoke($form, $arguments) | Out-Null
    }
    $cache = $offlineType.GetField('translationCache', $instance).GetValue($form)
    if ($cache.Count -ne 256 -or $cache.ContainsKey('cache-0') -or -not $cache.ContainsKey('cache-299')) {
        throw "AI翻译缓存淘汰异常：$($cache.Count)"
    }
} finally {
    $form.Dispose()
}

if($null -ne $assembly.GetType('MapleOverlay.OfflineAiClient',$false) -or
    $null -ne $assembly.GetType('MapleOverlay.AiInstallForm',$false)) {throw 'Offline inference/installer remains'}
$onlineType=$assembly.GetType('MapleOverlay.OnlineAiClient',$true)
$parameters=$onlineType.GetMethod('TranslateAsync',[Reflection.BindingFlags]'Static,Public,NonPublic').GetParameters()
if($parameters.Count -ne 5 -or $parameters[4].ParameterType -ne [Threading.CancellationToken]){throw 'Online cancellation contract missing'}
$source = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
$overlaySource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'timer.Interval = 117',
    'TranslateWithPreferredAiAsync',
    'firstLiveCapture = true',
    'int first = Math.Max(0, lines.Count - 2)',
    'DetectChatSourceLanguage(cleanedMessage)',
    'MaximumPendingChatLines = 60')) {
    if (-not $source.Contains($required)) { throw "AI实时翻译性能或隔离路径缺少：$required" }
}
if (-not $overlaySource.Contains('PrepareForOcr(bitmap, out scale, false, 1800.0f)')) {
    throw 'AI聊天OCR没有保留实图胜出的1800紧裁缩放'
}

Write-Output 'AI实时翻译：117ms监听、1800实图优选紧裁OCR、联网请求可取消、离线推理已移除、首帧仅最新2条、OCR变体短时去重、有界待处理队列、256条缓存、普通/超级喇叭实帧分类通过'
