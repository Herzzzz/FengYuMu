[CmdletBinding()]
param(
    # How long the translation overlay stays on screen before it is dismissed.
    [int]$HoldSeconds = 5,

    # Where the evidence screenshots are written. Defaults to a folder beside the repo.
    [string]$OutDir,

    # Send F8 automatically. Set to $false to press the key yourself when prompted.
    [bool]$AutoPressKey = $true
)

# One-click live verification against the running game.
#
# Why this is a separate, user-triggered tool: F8 paints the translation over the real game
# window. That is fine when the player asks for it, but it must never happen behind their
# back while they are playing. Running this script IS that request.
#
# What it does, in order:
#   1. refuses to run unless a MapleStory client is actually running;
#   2. starts 枫语幕 through CreateProcess (never ShellExecute, which would raise the
#      Windows "unknown publisher" prompt);
#   3. waits for the lock layer to identify the client;
#   4. presses F8, and captures the screen while the overlay is up;
#   5. dismisses the overlay and closes the product again;
#   6. prints where the screenshots went and what to look for in them.
#
# The screenshots are the point: they show whether Seamless or CardFallback was chosen on
# real UI, which no benchmark can tell us because the benchmark path never samples pixels.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

if ([String]::IsNullOrEmpty($OutDir)) {
    $OutDir = Join-Path (Split-Path -Parent $repoRoot) 'live-verification'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$client = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'Maple' }
if (-not $client) {
    throw '没有检测到运行中的 MapleStory 客户端。请先启动游戏（窗口模式，非全屏）再运行本脚本。'
}
Write-Host ("检测到客户端：{0}（PID {1}）" -f $client[0].ProcessName, $client[0].Id)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class LiveKey {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void Tap(byte vk) {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }
}
'@ -ErrorAction SilentlyContinue

function Save-Screen([string]$path) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size) }
        finally { $graphics.Dispose() }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) {
    . (Join-Path $repoRoot 'tests\Start-ProductExe.ps1')
}
$env:FENGYUMU_AUTOMATION = '1'

$stamp = Get-Date -Format 'HHmmss'
$beforePath = Join-Path $OutDir ("before-$stamp.png")
$duringPath = Join-Path $OutDir ("during-$stamp.png")
$afterPath = Join-Path $OutDir ("after-$stamp.png")

Write-Host '采集翻译前画面……'
Save-Screen $beforePath

Write-Host '启动 枫语幕……'
$product = Start-ProductExe -FilePath $exe -WorkingDirectory $repoRoot -PassThru
Start-Sleep -Seconds 4   # 启动 + 锁定层首次识别 + 托盘就绪

if ($AutoPressKey) {
    Write-Host '按下 F8（触发翻译）……'
    [LiveKey]::Tap(0x77)  # VK_F8
} else {
    Read-Host '请现在按 F8 触发翻译，然后按回车继续'
}
Start-Sleep -Seconds 3   # 等待首显与收敛

Write-Host '采集翻译中画面……'
Save-Screen $duringPath

Write-Host '关闭覆盖层并退出 枫语幕……'
if ($AutoPressKey) { [LiveKey]::Tap(0x77) }
Start-Sleep -Milliseconds 800
Save-Screen $afterPath
if ($product -and -not $product.HasExited) {
    try { $product.Kill() } catch { }
}
Remove-Item Env:\FENGYUMU_AUTOMATION -ErrorAction SilentlyContinue

Write-Host ''
Write-Host '实机验证完成。三张截图：'
Write-Host ("  翻译前：{0}" -f $beforePath)
Write-Host ("  翻译中：{0}   <-- 关键证据" -f $duringPath)
Write-Host ("  关闭后：{0}" -f $afterPath)
Write-Host ''
Write-Host '请检查「翻译中」截图：'
Write-Host '  1. 是否出现中文译文；'
Write-Host '  2. 译文是黑底卡片还是与背景同色的无缝替换；'
Write-Host '  3. 有无色块突兀、遮挡数值、文字溢出。'
Write-Host '把三张图发给开发者即可完成验收。'
