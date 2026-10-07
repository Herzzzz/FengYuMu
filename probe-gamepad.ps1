$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$exe = Join-Path $PSScriptRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "找不到 枫语幕.exe，请确认本脚本和程序在同一个文件夹。" -ForegroundColor Red
    Read-Host "按回车退出"
    exit 1
}

Write-Host "================================================"
Write-Host "   枫语幕 手柄背键探测"
Write-Host "================================================"
Write-Host ""
Write-Host "  接下来采集 30 秒。"
Write-Host ""
Write-Host "  请在提示开始后，把八爪鱼3 的背键 M1 / M2 / M3 / M4"
Write-Host "  一个一个慢慢按几遍（每个按 3～5 次）。"
Write-Host ""
Write-Host "  按之前先把摇杆和正面按键都松开，避免干扰。"
Write-Host ""
Read-Host "准备好了就按回车开始"

$report = Join-Path $PSScriptRoot 'gamepad_probe.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }

Write-Host ""
Write-Host "开始采集，请现在按背键……" -ForegroundColor Yellow

# 程序自己采集并写文件；这里不等待它的消息框，按时间取结果即可。
$process = Start-Process -FilePath $exe `
    -ArgumentList @('--gamepad-probe', '--gamepad-probe-seconds=30', '--gamepad-probe-quiet') `
    -WorkingDirectory $PSScriptRoot -PassThru

$deadline = (Get-Date).AddSeconds(70)
$result = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if (-not (Test-Path -LiteralPath $report)) { continue }
    $text = Get-Content -LiteralPath $report -Raw -Encoding UTF8
    if ($text -notmatch '正在采集') { $result = $text; break }
}
if (-not $process.HasExited) { $process.Kill() }

Write-Host ""
if ($null -eq $result) {
    Write-Host "没有拿到探测结果。" -ForegroundColor Red
    Write-Host "请把当前窗口截图发给开发者。"
    Read-Host "按回车退出"
    exit 1
}

Write-Host "==================== 探测结果 ====================" -ForegroundColor Green
Write-Host $result
Write-Host "=================================================" -ForegroundColor Green
Write-Host ""
Write-Host "结果文件：$report"
Write-Host "请把上面的内容（或该文件）发给开发者。" -ForegroundColor Yellow
Read-Host "按回车退出"
