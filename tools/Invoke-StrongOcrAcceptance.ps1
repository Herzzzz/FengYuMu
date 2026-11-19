[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$ImagePath,
    [ValidateSet('minimum','balanced','maximum')][string]$Range = 'balanced',
    [string]$Cursor = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
. (Join-Path $repoRoot 'tests\Start-ProductExe.ps1')
$results = @()
foreach ($requested in $ImagePath) {
    $image = (Resolve-Path -LiteralPath $requested).Path
    $arguments = @('--benchmark', "--benchmark-image=$image", "--benchmark-range=$Range")
    if (-not [String]::IsNullOrWhiteSpace($Cursor)) {
        $arguments += "--benchmark-cursor=$Cursor"
    }
    Start-ProductExe -FilePath $exe -ArgumentList $arguments -WorkingDirectory $repoRoot -Wait
    $report = Get-Content -LiteralPath (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
    if ($report.StartsWith('错误=')) { throw "$image 执行失败：$report" }
    $elapsed = [int]([regex]::Match($report, '耗时毫秒=(\d+)').Groups[1].Value)
    $strong = [regex]::Match($report, '本地强OCR=([^\r\n]+)').Groups[1].Value
    $hits = [int]([regex]::Match($report, '命中数量=(\d+)').Groups[1].Value)
    $labels = [regex]::Match($report, '覆盖标签=([^\r\n]*)').Groups[1].Value
    $scenes = [regex]::Match($report, '识别场景=([^\r\n]*)').Groups[1].Value
    $results += [pscustomobject]@{
        图片 = [IO.Path]::GetFileName($image)
        耗时毫秒 = $elapsed
        强OCR = $strong
        命中 = $hits
        场景 = $scenes
        标签 = $labels
    }
}
$results | Format-List
