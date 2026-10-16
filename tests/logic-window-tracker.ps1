$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

# This repository lives under a path that contains non-ASCII directory names. For such a
# path the CLR's Assembly.LoadFile refuses the assembly as a "remote" location even though
# the file has no Zone.Identifier and the identical bytes load from an ASCII path. The
# shared loader probes LoadFile first and falls back to UnsafeLoadFrom; no assertion below
# is relaxed by it.
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

$trackerType = $assembly.GetType('MapleOverlay.MapleWindowTracker', $true)
if ($null -eq $trackerType) { throw '程序集缺少 MapleOverlay.MapleWindowTracker' }

function Invoke-Static([string]$name, [object[]]$arguments) {
    $method = $trackerType.GetMethod($name, $all)
    if ($null -eq $method) { throw "缺少静态方法 $name" }
    return $method.Invoke($null, $arguments)
}

# ---------------------------------------------------------------------------
# 1. Process identity. The client must be recognised, and the tools that live
#    beside it must never be mistaken for the render surface.
# ---------------------------------------------------------------------------
foreach ($name in @('MapleStory', 'MapleStoryClassic', 'MapleStoryGlobal', 'MapleStory.exe',
    'maplestoryclassic')) {
    if (-not (Invoke-Static 'IsMapleProcessName' @([string]$name))) {
        throw "客户端进程名未被识别：$name"
    }
}
foreach ($name in @('MapleStory_Launcher', 'MapleStoryPatcher', 'MapleStoryUpdater',
    'MapleStorySetup', 'chrome', 'explorer', 'FengYuMu', '', $null)) {
    if (Invoke-Static 'IsMapleProcessName' @([string]$name)) {
        throw "非客户端进程名被误判为 MapleStory：$name"
    }
}

# ---------------------------------------------------------------------------
# 2. Title and class are corroboration only. A localised or renamed client must
#    still be lockable, so neither probe may be required on its own.
# ---------------------------------------------------------------------------
if (-not (Invoke-Static 'TitleLooksLikeMaple' @([string]'MapleStory Classic'))) {
    throw '窗口标题证据缺失：MapleStory Classic'
}
if (Invoke-Static 'TitleLooksLikeMaple' @([string]'日程表 - 记事本')) {
    throw '无关窗口标题被误判为 MapleStory'
}
if (-not (Invoke-Static 'ClassLooksLikeMaple' @([string]'MapleStoryClass'))) {
    throw '窗口类名证据缺失：MapleStoryClass'
}
if (Invoke-Static 'ClassLooksLikeMaple' @([string]'Notepad')) {
    throw '无关窗口类名被误判为 MapleStory'
}

# ---------------------------------------------------------------------------
# 3. Evidence scoring. Process identity is required; title and class only add
#    confidence. Hidden, minimised, owned and tiny windows are never the client.
# ---------------------------------------------------------------------------
$scoreMethod = $trackerType.GetMethod('ScoreCandidate', $all)
if ($null -eq $scoreMethod) { throw '缺少 ScoreCandidate' }
function Get-Score([bool]$process, [bool]$title, [bool]$class, [bool]$visible,
    [bool]$minimized, [bool]$owner, [int]$width, [int]$height) {
    return [int]$scoreMethod.Invoke($null, @($process, $title, $class, $visible, $minimized,
        $owner, $width, $height))
}

$plain = Get-Score $true $false $false $true $false $false 1280 720
$full = Get-Score $true $true $true $true $false $false 1280 720
if ($plain -le 0) { throw '仅凭进程名无法锁定客户端' }
if ($full -le $plain) { throw '标题与类名证据没有提高置信度' }
foreach ($case in @(
    @($false, $true, $true, $true, $false, $false, 1280, 720, '进程不匹配'),
    @($true, $false, $false, $false, $false, $false, 1280, 720, '窗口不可见'),
    @($true, $false, $false, $true, $true, $false, 1280, 720, '窗口最小化'),
    @($true, $false, $false, $true, $false, $true, 1280, 720, '被拥有的弹窗'),
    @($true, $false, $false, $true, $false, $false, 120, 90, '客户端区域过小'))) {
    $s = Get-Score $case[0] $case[1] $case[2] $case[3] $case[4] $case[5] $case[6] $case[7]
    if ($s -ne 0) { throw "证据规则应给出 0：$($case[8])（实际 $s）" }
}

# ---------------------------------------------------------------------------
# 4. Full-screen refusal. A client that covers its monitor must be rejected, but
#    an ordinary windowed client - including one larger than 1280x720 - must not.
# ---------------------------------------------------------------------------
Add-Type -AssemblyName System.Windows.Forms
$monitor = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$screenFull = New-Object System.Drawing.Rectangle(0, 0, $monitor.Width, $monitor.Height)
if (-not (Invoke-Static 'IsFullScreenClient' @($screenFull))) {
    throw "覆盖主显示器的客户端未被判为全屏：$($monitor.Width)x$($monitor.Height)"
}
$windowed = New-Object System.Drawing.Rectangle(60, 60, 1280, 720)
if (Invoke-Static 'IsFullScreenClient' @($windowed)) {
    throw '普通窗口化客户端被误判为全屏'
}
$largeWindowed = New-Object System.Drawing.Rectangle(8, 8, [Math]::Max(160, $monitor.Width - 16),
    [Math]::Max(120, $monitor.Height - 16))
if (Invoke-Static 'IsFullScreenClient' @($largeWindowed)) {
    throw '留出边框的大窗口被误判为全屏'
}

# ---------------------------------------------------------------------------
# 5. The layer must stay capture-only: no memory access, injection, hooking or
#    synthesised input may appear in its source.
# ---------------------------------------------------------------------------
$source = Get-Content (Join-Path $repoRoot 'src\MapleWindowTracker.cs') -Raw -Encoding UTF8
$allSource = (Get-ChildItem (Join-Path $repoRoot 'src') -Filter '*.cs' |
    ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$forbidden = @('ReadProcessMemory', 'WriteProcessMemory', 'SetWindowsHookEx',
    'SendInput', 'keybd_event', 'mouse_event', 'VirtualAllocEx', 'CreateRemoteThread')
foreach ($token in $forbidden) {
    if ($allSource.Contains($token)) { throw "源码越过了外部截图安全边界：$token" }
}
foreach ($right in @('PROCESS_VM_READ', 'PROCESS_VM_WRITE', 'PROCESS_VM_OPERATION',
    'PROCESS_ALL_ACCESS', 'PROCESS_CREATE_THREAD')) {
    if ($allSource.Contains($right)) { throw "源码申请了危险进程权限：$right" }
}

# OpenProcess is allowed only to read a process image name. The guard therefore pins the
# access mask instead of banning the API, because the dangerous part is the requested right,
# not the call: PROCESS_VM_* would permit reading or writing the game's memory, while
# PROCESS_QUERY_LIMITED_INFORMATION cannot.
if ($source.Contains('OpenProcess')) {
    if (-not $source.Contains('PROCESS_QUERY_LIMITED_INFORMATION')) {
        throw 'OpenProcess 被使用但未限定为只读查询权限'
    }
    foreach ($right in @('PROCESS_VM_READ', 'PROCESS_VM_WRITE', 'PROCESS_VM_OPERATION',
        'PROCESS_ALL_ACCESS', 'PROCESS_CREATE_THREAD')) {
        if ($source.Contains($right)) { throw "锁定层申请了危险进程权限：$right" }
    }
    if ($source -notmatch 'PROCESS_QUERY_LIMITED_INFORMATION\s*=\s*0x1000') {
        throw 'PROCESS_QUERY_LIMITED_INFORMATION 常量值不是只读查询权限'
    }
}

foreach ($required in @('EnumWindows', 'GetClientRect', 'ClientToScreen', 'IsIconic',
    'GetDpiForWindow')) {
    if (-not $source.Contains($required)) { throw "锁定层缺少必要的只读证据读取：$required" }
}
# The layer must never OCR or call a model.
foreach ($token in @('OcrEngine', 'PlaceholderText', 'OnlineAI', 'HttpWebRequest')) {
    if ($source.Contains($token)) { throw "锁定层不应包含 OCR 或联网逻辑：$token" }
}

# ---------------------------------------------------------------------------
# 6. Runtime behaviour. Whether a MapleStory client happens to be running on this
#    machine is not something the test may depend on, so both branches are checked
#    for their contract instead of asserting one particular state.
# ---------------------------------------------------------------------------
$tracker = [Activator]::CreateInstance($trackerType, $true)
if ($null -eq $tracker) { throw '无法实例化 MapleWindowTracker' }
$poll = $trackerType.GetMethod('Poll', $all)
$poll.Invoke($tracker, @())
$stateValue = $trackerType.GetProperty('State', $all).GetValue($tracker, @()).ToString()
$diag = [string]$trackerType.GetProperty('Diagnostic', $all).GetValue($tracker, @())
if ($diag.Length -eq 0) { throw '锁定时或未找到窗口时都必须给出中文状态说明' }

if ($stateValue -notin @('NotFound', 'FullScreen', 'Locked')) {
    throw "锁层返回了未知状态：$stateValue"
}

$outArgs = New-Object 'object[]' 2
$boundsMethod = $trackerType.GetMethod('TryGetCaptureBounds', $all)
$usable = [bool]$boundsMethod.Invoke($tracker, $outArgs)
$candidates = [int]$trackerType.GetProperty('CandidateCount', $all).GetValue($tracker, @())
$locked = $trackerType.GetProperty('Locked', $all).GetValue($tracker, @())

if ($stateValue -eq 'Locked') {
    # A lock must be backed by a real candidate and a usable client rectangle.
    if ($null -eq $locked) { throw '状态为已锁定但没有任何锁定对象' }
    if ($candidates -lt 1) { throw '状态为已锁定但候选列表为空' }
    if (-not $usable) { throw '状态为已锁定但无法给出截图区域' }
    $rect = $outArgs[0]
    if ($rect.Width -lt 160 -or $rect.Height -lt 120) {
        throw ("已锁定的截图区域过小：{0}x{1}" -f $rect.Width, $rect.Height)
    }

    # The lock must be justified by the same evidence rules the classifier uses. A window
    # that satisfies none of them would mean the tracker locked something unrelated.
    $lt = $locked.GetType()
    $processName = [string]$lt.GetField('ProcessName', $all).GetValue($locked)
    $className = [string]$lt.GetField('ClassName', $all).GetValue($locked)
    $title = [string]$lt.GetField('Title', $all).GetValue($locked)
    $evidence = [int]$lt.GetField('Evidence', $all).GetValue($locked)
    $processOk = [bool](Invoke-Static 'IsMapleProcessName' @($processName))
    $classOk = [bool](Invoke-Static 'ClassLooksLikeMaple' @($className))
    $titleOk = [bool](Invoke-Static 'TitleLooksLikeMaple' @($title))
    if (-not ($processOk -or $classOk -or $titleOk)) {
        throw ("锁定了一个没有任何 MapleStory 证据的窗口：{0} / {1} / {2}" -f $processName, $className, $title)
    }
    if ($evidence -le 0) { throw '锁定的证据分为 0，说明并非按评分规则选定' }
    if ([bool](Invoke-Static 'IsFullScreenClient' @($rect))) {
        throw '锁定的客户端被自己的全屏判定认为是全屏，状态自相矛盾'
    }
} else {
    # Not locked: the tracker must not invent capture bounds or a lock object.
    if ($usable) { throw '未锁定客户端时不应给出可用的截图区域（禁止回退全屏扫描）' }
    if ($null -ne $locked) { throw '未锁定时不应存在锁定对象' }
}

# The layer must stay passive: no timer of its own, and no OCR/network 类型 in its source.
foreach ($token in @('System.Windows.Forms.Timer', 'System.Timers.Timer', '.Start()')) {
    if ($source.Contains($token)) { throw "锁定层不应自行持有定时器：$token" }
}

Write-Output ("窗口锁定层：进程/标题/类名多证据识别可用，遮蔽/最小化/被拥有/过小窗口一律拒绝，" +
    "全屏客户端拒绝翻译，锁定层不含任何内存访问、注入或定时器；" +
    "当前环境状态={0}（{1}）" -f $stateValue, $diag)
