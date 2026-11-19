param([string]$ExePath = '', [switch]$ManualTrayVerification)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $repoRoot '枫语幕.exe' }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
. (Join-Path $repoRoot 'tools/Build-Evidence.ps1')
Assert-BuildEvidence $ExePath $repoRoot

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

if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Start-ProductExe.ps1') }
$first = Start-ProductExe -FilePath $ExePath -PassThru
try {
    if (-not (Wait-ForMainWindow $first.Id $true 8000)) { throw '主界面启动后未出现' }

    $mainWindow = Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible } | Select-Object -First 1
    [FengYuMuWindowProbe]::PostMessage($mainWindow.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 350
    if ($first.HasExited) { throw '关闭主界面不应退出后台进程' }
    if (-not (Wait-ForMainWindow $first.Id $false 5000) -or
        @(Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible }).Count -gt 0) {
        throw '关闭主界面后窗口没有隐藏'
    }
    $physicalTray = 'UNVERIFIED'
    $explorerRecovery = 'UNVERIFIED'
    if ($ManualTrayVerification) {
        $answer = Read-Host '请在真实托盘找到枫语幕图标，并用右键菜单或双击恢复主窗口；完成后输入 restored'
        if ($answer -ne 'restored' -or -not (Wait-ForMainWindow $first.Id $true 5000)) {
            throw '真实图标存在及托盘操作恢复未确认'
        }
        $physicalTray = 'MANUALLY_CONFIRMED'
    }

    if ($ManualTrayVerification) {
        $mainWindow = Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible } | Select-Object -First 1
        [FengYuMuWindowProbe]::PostMessage($mainWindow.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        if (-not (Wait-ForMainWindow $first.Id $false 5000) -or
            @(Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible }).Count -gt 0) {
            throw '二次启动验收前主窗口没有隐藏'
        }
    }
    $second = Start-ProductExe -FilePath $ExePath -PassThru
    if (-not $second.WaitForExit(4000)) { throw '第二次启动产生了重复驻留进程' }
    if (-not (Wait-ForMainWindow $first.Id $true 5000)) { throw '第二次启动未唤回已有主界面' }

    $taskbarCreated = [FengYuMuWindowProbe]::RegisterWindowMessage('TaskbarCreated')
    foreach ($window in Get-AppWindows $first.Id) {
        [FengYuMuWindowProbe]::PostMessage($window.Handle, $taskbarCreated, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    Start-Sleep -Milliseconds 350
    if ($first.HasExited) { throw '模拟任务栏重建后程序异常退出' }

    $samePath = @(Get-Process -Name $first.ProcessName -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $ExePath -and
            @(Get-AppWindows $_.Id | Where-Object { $_.Title -like '枫语幕*' }).Count -gt 0 } catch { $false }
    })
    if ($samePath.Count -ne 1) { throw "单实例数量错误：$($samePath.Count)" }

    if ($ManualTrayVerification) {
        $mainWindow = Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible } | Select-Object -First 1
        [FengYuMuWindowProbe]::PostMessage($mainWindow.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        if (-not (Wait-ForMainWindow $first.Id $false 5000) -or
            @(Get-AppWindows $first.Id | Where-Object { $_.Title -like '枫语幕*' -and $_.Visible }).Count -gt 0) {
            throw 'Explorer 重启验收前主窗口没有隐藏'
        }
        $answer = Read-Host '请手动重启 Explorer，确认图标重新出现并用托盘操作恢复主窗口；完成后输入 restored'
        if ($answer -ne 'restored' -or -not (Wait-ForMainWindow $first.Id $true 8000)) {
            throw 'Explorer 实际重启后的图标操作恢复未确认'
        }
        $explorerRecovery = 'MANUALLY_CONFIRMED'
    }
    [pscustomobject]@{ ProcessAlive = -not $first.HasExited; MainWindowHidden = $true;
        SecondLaunchRestored = $true; TaskbarMessageProcessSurvived = $true;
        PhysicalTrayInteraction = $physicalTray; RealExplorerRecovery = $explorerRecovery }
    Write-Output '自动检查只证明窗口、单实例和消息处理；图标存在与实际操作仅在人工确认后记录，不能由进程存活推导。'
}
finally {
    if (-not $first.HasExited) {
        foreach ($window in Get-AppWindows $first.Id) {
            [FengYuMuWindowProbe]::PostMessage($window.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        }
        $null = $first.WaitForExit(5000)
        if (-not $first.HasExited) { $first.Kill(); $null = $first.WaitForExit(5000) }
    }
}
