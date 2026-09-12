$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))

$snapshotType = $assembly.GetType('MapleOverlay.SceneSnapshot', $true)
$sceneKindType = $assembly.GetType('MapleOverlay.SceneKind', $true)
$policyType = $assembly.GetType('MapleOverlay.ContinuousTranslationPolicy', $true)
$snapshot = [Activator]::CreateInstance($snapshotType, $true)
$add = $snapshotType.GetMethod('Add', [Reflection.BindingFlags]'Instance,NonPublic,Public')
$shouldTranslate = $policyType.GetMethod('ShouldTranslate',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$nextInterval = $policyType.GetMethod('NextInterval',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$shouldRunProbe = $policyType.GetMethod('ShouldRunProbe',
    [Reflection.BindingFlags]'Static,NonPublic,Public')

if ([bool]$shouldTranslate.Invoke($null, @($snapshot, $false))) {
    throw '空画面不应触发持续翻译'
}
if (-not [bool]$shouldTranslate.Invoke($null, @($snapshot, $true))) {
    throw '人物属性视觉锚点应触发持续翻译'
}
$itemKind = [Enum]::Parse($sceneKindType, 'Item')
$add.Invoke($snapshot, @($itemKind, 99)) | Out-Null
if ([bool]$shouldTranslate.Invoke($null, @($snapshot, $false))) {
    throw '低置信度单锚点不应触发持续翻译'
}
$add.Invoke($snapshot, @($itemKind, 46)) | Out-Null
if (-not [bool]$shouldTranslate.Invoke($null, @($snapshot, $false))) {
    throw '装备详情高置信度锚点应触发持续翻译'
}

$visible = [int]$nextInterval.Invoke($null, @($true, 0, 0))
$idle0 = [int]$nextInterval.Invoke($null, @($false, 0, 0))
$idle2 = [int]$nextInterval.Invoke($null, @($false, 2, 0))
$idleCap = [int]$nextInterval.Invoke($null, @($false, 99, 0))
$failure1 = [int]$nextInterval.Invoke($null, @($false, 0, 1))
$failureCap = [int]$nextInterval.Invoke($null, @($false, 0, 99))
if ($visible -ne 240 -or $idle0 -ne 260 -or $idle2 -ne 580 -or
    $idleCap -ne 900 -or $failure1 -ne 1200 -or $failureCap -ne 2600) {
    throw "持续翻译轮询退避错误：$visible/$idle0/$idle2/$idleCap/$failure1/$failureCap"
}
if (-not [bool]$shouldRunProbe.Invoke($null, @($true, $false, $false)) -or
    [bool]$shouldRunProbe.Invoke($null, @($true, $true, $false)) -or
    -not [bool]$shouldRunProbe.Invoke($null, @($true, $true, $true))) {
    throw '持续模式快速探测复用策略错误'
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$forms = Get-Content (Join-Path $repoRoot 'src\SimpleForms.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'GetValue("ContinuousTranslationEnabled", 0)',
    'ShowTranslationAsync(true)',
    'GetWindowThreadProcessId',
    'continuousTranslationSuppressedUntilUtc',
    'manualTranslationPending',
    'while (processing && requestId == manualTranslationRequestId && !shuttingDown)',
    'RecognitionWasCancelled(automatic, requestId)',
    'internal bool ApplyHotkeys',
    'await Task.Delay(120)',
    '已恢复原来的可用设置',
    'return ShowTranslationAsync(false, 0)',
    'ContinuousTranslationPolicy.ShouldTranslate',
    '--benchmark-scene-probe',
    '快速探测[',
    'PrepareForOcr(bitmap, out probeScale, false, 1200.0f)')) {
    if (-not $source.Contains($required)) { throw "持续翻译安全路径缺少：$required" }
}
foreach ($required in @(
    '持续自动翻译（检测到游戏面板即显示）',
    '默认关闭；开启后智能降频',
    'overlay.ApplyContinuousTranslation')) {
    if (-not $forms.Contains($required)) { throw "持续翻译主界面缺少：$required" }
}
if (-not $forms.Contains('if (!overlay.ApplyHotkeys(sk, sm, hk, hm, fk, fm)) return;')) {
    throw '冲突快捷键注册失败后仍会保存无效设置'
}
foreach ($forbidden in @('ReadProcessMemory','WriteProcessMemory','CreateRemoteThread',
    'VirtualAllocEx','SetWindowsHookEx','SendInput')) {
    if (($source + $forms).Contains($forbidden)) { throw "安全边界失败：$forbidden" }
}

Write-Output '持续自动翻译：默认关闭、面板门控、快速轮询、空闲/失败退避、F8手动请求抢占持续扫描与安全边界通过'
