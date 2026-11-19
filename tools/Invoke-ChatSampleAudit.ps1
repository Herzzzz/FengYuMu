param(
    [Parameter(Mandatory=$true)][string]$ImageFolder,
    [string]$OutputFolder = 'D:\GPT\temp\fengyumu-chat-samples'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "缺少程序：$exe" }
if (-not (Test-Path -LiteralPath $ImageFolder)) { throw "缺少样本目录：$ImageFolder" }
if (-not (Test-Path -LiteralPath $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}
if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) {
    . (Join-Path $repoRoot 'tests\Start-ProductExe.ps1')
}

$rows = @()
foreach ($image in @(Get-ChildItem -LiteralPath $ImageFolder -Filter '*.png' | Sort-Object Name)) {
    Start-ProductExe -FilePath $exe -ArgumentList ('--chat-style-benchmark=' + $image.FullName) `
        -WorkingDirectory $repoRoot -Wait
    $sourceReport = Join-Path $repoRoot 'chat_style_benchmark.txt'
    $report = Get-Content -LiteralPath $sourceReport -Encoding UTF8
    $destination = Join-Path $OutputFolder ($image.BaseName + '.txt')
    Set-Content -LiteralPath $destination -Value $report -Encoding UTF8
    $first = [string]($report | Select-Object -First 1)
    $bitmap = [Drawing.Image]::FromFile($image.FullName)
    $size = '{0}x{1}' -f $bitmap.Width, $bitmap.Height
    $bitmap.Dispose()
    $rows += [pscustomobject]@{
        Image = $image.Name
        Size = $size
        Lines = @($report | Where-Object { $_ -match '^(?:TEXT|BAND)/' }).Count
        Parsed = @($report | Where-Object { $_ -like 'PARSED | *' }).Count
        Elapsed = if ($first -match 'elapsed_ms=(\d+)') { [int]$Matches[1] } else { -1 }
        Report = $destination
    }
}
$rows | Format-Table -AutoSize
