param([string]$ExePath = '')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $repoRoot '枫语幕.exe' }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path

if (-not ('FengYuMuWindowProbe' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public sealed class FengYuMuWindowInfo {
    public IntPtr Handle;
    public string Title;
    public bool Visible;
}

public static class FengYuMuWindowProbe {
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int RegisterWindowMessage(string message);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public static FengYuMuWindowInfo[] ForProcess(int processId) {
        List<FengYuMuWindowInfo> result = new List<FengYuMuWindowInfo>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused) {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != (uint)processId) return true;
            StringBuilder title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            result.Add(new FengYuMuWindowInfo { Handle = hwnd, Title = title.ToString(), Visible = IsWindowVisible(hwnd) });
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
}
'@
}

function Get-AppWindows([int]$ProcessId) {
    return @([FengYuMuWindowProbe]::ForProcess($ProcessId))
}

function Wait-ForMainWindow([int]$ProcessId, [bool]$Visible, [int]$TimeoutMs = 5000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    do {
        $found = @(Get-AppWindows $ProcessId | Where-Object {
            $_.Title -like '枫语幕*' -and $_.Visible -eq $Visible
        }).Count -gt 0
        if ($found) { return $true }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

$first = Start-Process -FilePath $ExePath -PassThru
try {
    if (-not (Wait-ForMainWindow $first.Id $true 8000)) { throw '主界面启动后未出现' }

    $mainWindow = Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible } | Select-Object -First 1
    [FengYuMuWindowProbe]::PostMessage($mainWindow.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 350
    if ($first.HasExited) { throw '关闭主界面不应退出后台进程' }

    $second = Start-Process -FilePath $ExePath -PassThru
    if (-not $second.WaitForExit(4000)) { throw '第二次启动产生了重复驻留进程' }
    if (-not (Wait-ForMainWindow $first.Id $true 5000)) { throw '第二次启动未唤回已有主界面' }

    $taskbarCreated = [FengYuMuWindowProbe]::RegisterWindowMessage('TaskbarCreated')
    foreach ($window in Get-AppWindows $first.Id) {
        [FengYuMuWindowProbe]::PostMessage($window.Handle, $taskbarCreated, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    Start-Sleep -Milliseconds 350
    if ($first.HasExited) { throw '模拟任务栏重建后程序异常退出' }

    $samePath = @(Get-Process -Name $first.ProcessName -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $ExePath } catch { $false }
    })
    if ($samePath.Count -ne 1) { throw "单实例数量错误：$($samePath.Count)" }

    Write-Output "托盘恢复集成：主界面隐藏、二次启动唤回、单实例和 TaskbarCreated 重建消息全部通过（PID $($first.Id)）"
}
finally {
    if (-not $first.HasExited) {
        foreach ($window in Get-AppWindows $first.Id) {
            [FengYuMuWindowProbe]::PostMessage($window.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        }
        $null = $first.WaitForExit(5000)
    }
}
