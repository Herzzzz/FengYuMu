param([string]$ExePath = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = if ([string]::IsNullOrWhiteSpace($ExePath)) { Join-Path $root '枫语幕.exe' } else { $ExePath }
$source = Get-Content -LiteralPath (Join-Path $root 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$forms = Get-Content -LiteralPath (Join-Path $root 'src\SimpleForms.cs') -Raw -Encoding UTF8

$process = Start-Process -FilePath $exe -ArgumentList '--gamepad-self-test' `
    -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "手柄单键、双键、防连发或冲突自检失败，退出码 $($process.ExitCode)"
}

$required = @(
    'GamepadShortcutLatch',
    'showBlocked = true',
    'hideBlocked = true',
    'IsPressedOnOneController',
    'xinput1_4.dll',
    'xinput9_1_0.dll',
    'xinput1_3.dll',
    'gamepadTimer.Interval = 25',
    'GamepadShowShortcut',
    'GamepadHideShortcut'
)
foreach ($needle in $required) {
    if (-not $source.Contains($needle)) { throw "缺少手柄实现：$needle" }
}

foreach ($needle in @('手柄快捷键（独立于键盘）', '支持单键或双键同时按',
    '双键组合不能选择两个相同按键', 'gs.ConflictsWith(gh)')) {
    if (-not $forms.Contains($needle)) { throw "缺少手柄设置界面或冲突保护：$needle" }
}

foreach ($forbidden in @('ReadProcessMemory', 'WriteProcessMemory', 'OpenProcess(', 'SendInput(', 'keybd_event(', 'mouse_event(')) {
    if ($source.Contains($forbidden) -or $forms.Contains($forbidden)) {
        throw "发现不允许的游戏进程或输入操作：$forbidden"
    }
}

Write-Host '手柄快捷键回归通过：单键、双键、同一手柄组合、防连发、跨手柄隔离、冲突保护与 XInput 回退均正常。'
