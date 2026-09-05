$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$reportPath = Join-Path $repoRoot 'chat_style_benchmark.txt'

function Test-ChatStyleFrame([string]$fileName, [string[]]$requiredPatterns) {
    $imagePath = Join-Path $PSScriptRoot (Join-Path 'fixtures' $fileName)
    if (-not (Test-Path -LiteralPath $imagePath)) { throw "聊天样式实帧不存在：$imagePath" }
    $process = Start-Process -FilePath $exe `
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

$assembly = [Reflection.Assembly]::LoadFile($exe)
$instance = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$offlineType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$constructor = $offlineType.GetConstructor($instance, $null,
    [Type[]]@($overlayType, [string]), $null)
$form = $constructor.Invoke([object[]]@($null, [string]$repoRoot))
try {
    $timer = $offlineType.GetField('timer', $instance).GetValue($form)
    if ($timer.Interval -ne 280) { throw "AI聊天轮询不是280ms：$($timer.Interval)" }

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

$aiType = $assembly.GetType('MapleOverlay.OfflineAiClient', $true)
if ($null -eq $aiType.GetField('startupLock', $instance) -or
    $null -eq $aiType.GetField('startupTask', $instance)) {
    throw 'AI模型启动缺少并发去重'
}

$source = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'timer.Interval = 280',
    '-c 1280 -b 512 -ub 256',
    '--parallel 1 --prio 1 --poll 80 --poll-batch 80',
    'Task<bool> warmup = ai.IsInstalled ? ai.EnsureStartedAsync() : null',
    'if (floatingWindowEnabled && live)')) {
    if (-not $source.Contains($required)) { throw "AI实时翻译性能或隔离路径缺少：$required" }
}

Write-Output 'AI实时翻译：280ms监听、模型预热、启动去重、256条缓存、普通/超级喇叭实帧分类通过'
