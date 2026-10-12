$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$overlayPath = Join-Path $repoRoot 'src\MapleOverlay.cs'
$chatPath = Join-Path $repoRoot 'src\OfflineChat.cs'
$overlay = Get-Content -LiteralPath $overlayPath -Raw -Encoding UTF8
$chat = Get-Content -LiteralPath $chatPath -Raw -Encoding UTF8

function Require-Text([string]$haystack, [string]$needle, [string]$message) {
    if (-not $haystack.Contains($needle)) { throw $message + '：' + $needle }
}

# Restrict the network fallback to a deliberate manual F8 pass.  The benchmark and continuous
# paths must remain deterministic/offline, and the candidate is attempted at most once per frame.
Require-Text $overlay '!panelAiAttempted && !automatic && !Program.Benchmark' '面板AI未限定手动F8'
Require-Text $overlay 'panelAiAttempted = true' '面板AI没有单次调用闸门'
Require-Text $overlay 'FindBestPanelProseBlock' '面板AI没有复用正文候选提取'
Require-Text $overlay 'CombinePanelAiSource(panelProse)' '面板AI没有从OCR正文构造文本'

$aiStart = $overlay.IndexOf('private async Task<OverlayLabel> BuildPanelAiFallbackLabelAsync',
    [StringComparison]::Ordinal)
$aiEnd = $overlay.IndexOf('private static string BuildPanelAiCacheKey', $aiStart,
    [StringComparison]::Ordinal)
if ($aiStart -lt 0 -or $aiEnd -le $aiStart) { throw '找不到面板AI兜底方法范围' }
$aiMethod = $overlay.Substring($aiStart, $aiEnd - $aiStart)

# One invocation only, OCR text only.  No Bitmap, HWND, CopyFromScreen, or process/memory value
# is passed to the panel client; the client itself is separately guarded by logic-panel-ai-client.
if (([regex]::Matches($aiMethod, 'OnlineAiClient\.TranslatePanelAsync')).Count -ne 1) {
    throw '一个F8面板兜底不应产生多次联网调用'
}
Require-Text $aiMethod 'settings == null || !settings.IsReady' '配置未就绪时没有完全跳过联网'
Require-Text $aiMethod 'CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)' '面板AI未绑定F8取消令牌'
Require-Text $aiMethod 'timeout.CancelAfter' '面板AI没有独立超时取消'
Require-Text $aiMethod 'TranslatePanelAsync(settings, source' '面板AI没有使用纯OCR文本'
Require-Text $aiMethod 'if (cancellationToken.IsCancellationRequested ||' '面板AI返回后缺少取消闸门'
Require-Text $aiMethod 'IsSafePanelAiTranslation(source, translated)' '面板AI没有安全结果校验'
Require-Text $aiMethod 'WritePanelAiCache(cacheKey, translated)' '面板AI成功结果没有缓存'
 $safePosition = $aiMethod.IndexOf('IsSafePanelAiTranslation(source, translated)', [StringComparison]::Ordinal)
 $cachePosition = $aiMethod.IndexOf('WritePanelAiCache(cacheKey, translated)', [StringComparison]::Ordinal)
if ($safePosition -lt 0 -or $cachePosition -lt 0 -or $safePosition -gt $cachePosition) {
    throw '面板AI缓存写入没有位于结果安全校验之后'
}
Require-Text $overlay 'CancelPanelAiRequest();' '隐藏/F8取消没有中止面板AI'
Require-Text $overlay 'panelAiRequestCancellation.Dispose();' '面板AI取消源没有释放'
Require-Text $overlay 'result.MatchSource = "ai-panel"' 'AI标签缺少ai-panel来源标记'
Require-Text $overlay 'result.Confidence = LabelConfidence.Medium' 'AI标签没有降为Medium置信度'
Require-Text $overlay 'private const int PanelAiBudgetMs = 2420' '面板AI没有为2.5秒目标预留安全余量'
Require-Text $overlay 'OnlineAiClient.PanelTimeoutMaxMs' '面板AI没有复用1100ms上限'
Require-Text $overlay 'PanelAiCacheCapacity = 128' '面板AI缓存没有小容量上限'
Require-Text $overlay 'settings.Endpoint ?? ""' '面板AI缓存键缺少endpoint'
Require-Text $overlay 'settings.Model ?? ""' '面板AI缓存键缺少model'
Require-Text $overlay '(source ?? "").Trim()' '面板AI缓存键缺少source'
Require-Text $overlay 'Regex.Matches(source ?? "", @"\d+(?:\.\d+)?")' '面板AI没有提取源文本数字'
Require-Text $overlay 'Regex.IsMatch(translated, "[\\u4e00-\\u9fff]")' '面板AI没有要求中文返回'

# Compile the product first in the caller, then exercise only pure safety helpers through
# reflection.  This verifies Chinese output and source-number preservation without making a real
# provider request or reading a screenshot.
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw '找不到已编译枫语幕.exe' }
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$safe = $overlayType.GetMethod('IsSafePanelAiTranslation', $all)
if ($null -eq $safe) { throw '找不到面板AI数字/中文校验方法' }
$sourceText = 'Make a Pig & Mushroom Omok Set. Level 12+ requires 30/50 items.'
$good = [bool]$safe.Invoke($null, [object[]]@($sourceText, '制作猪蘑菇五子棋套装，等级 12+，需要 30/50 件物品。'))
$missingNumberRejected = -not [bool]$safe.Invoke($null, [object[]]@($sourceText, '制作猪蘑菇五子棋套装，等级 12+，需要 30 件物品。'))
$noChinese = [bool]$safe.Invoke($null, [object[]]@($sourceText, 'Make a Pig & Mushroom Omok Set. Level 12+ requires 30/50 items.'))
if (-not $good -or -not $missingNumberRejected -or $noChinese) {
    throw '面板AI中文或数字校验结果不符合安全策略'
}

$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)
$settings = [Activator]::CreateInstance($settingsType, $true)
$ready = $settingsType.GetProperty('IsReady', $all)
if ([bool]$ready.GetValue($settings, $null)) { throw '默认无密钥配置不应被视为已就绪' }
$settingsType.GetField('Endpoint', $all).SetValue($settings, 'https://example.invalid/panel')
$settingsType.GetField('Model', $all).SetValue($settings, 'panel-model-test')
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'test-key')
$keyMethod = $overlayType.GetMethod('BuildPanelAiCacheKey', $all)
$cacheKey = [string]$keyMethod.Invoke($null, [object[]]@($settings, $sourceText))
if ($cacheKey -notmatch 'example\.invalid' -or $cacheKey -notmatch 'panel-model-test' -or
    $cacheKey -notmatch 'Pig.*Mushroom') { throw '面板AI缓存键没有包含endpoint/model/source' }

Write-Output '面板AI兜底：手动F8限定、连续/基准跳过、一次调用、文本-only、配置门控、2.42秒预算、取消/超时、成功缓存失败不缓存、中文与数字校验通过'
