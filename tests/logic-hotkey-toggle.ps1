$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$report = Join-Path $repoRoot 'hotkey_toggle_test.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }

$process = Start-Process -FilePath $exe -ArgumentList '--hotkey-toggle-test' `
    -WorkingDirectory $repoRoot -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "F8已编译程序自检进程失败：$($process.ExitCode)" }
if (-not (Test-Path -LiteralPath $report)) { throw 'F8已编译程序自检未生成报告' }
$text = Get-Content -LiteralPath $report -Raw -Encoding UTF8
if (-not $text.Contains('通过：首次显示、再次关闭、处理中取消、取消后重新截图')) {
    throw "F8已编译程序自检失败：$text"
}
Remove-Item -LiteralPath $report -Force
Write-Output "已编译程序F8实调用通过：首次显示、再次关闭、处理中取消、取消后重新截图"
