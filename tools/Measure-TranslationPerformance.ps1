[CmdletBinding()]
param(
    # Comma- or semicolon-separated image list. A single string is used instead of string[]
    # because the array form binds unreliably when this script is invoked through
    # `powershell -File`, which silently skipped every image.
    [string]$Images,

    # Synthetic benchmark text, used when no images are supplied.
    [string]$Text = 'MapleStory Classic World Henesys Quest Complete',

    [string]$Range = 'balanced',
    [Parameter(Mandatory = $true)][string]$BaselinePath
)

# Measures the v4.0 acceptance numbers: time-to-first-visible-text and time-to-convergence.
#
# Two things matter here and both are easy to get wrong by hand:
#   1. FENGYUMU_AUTOMATION=1 must be set before the product starts, otherwise the updater
#      tests can pop a Windows security prompt (see tools\Invoke-AutomatedRegression.ps1).
#   2. The product must be started through CreateProcess rather than ShellExecute, because
#      this repository sits under a path Windows treats as untrusted, so ShellExecute adds
#      an "unknown publisher" confirmation for every launch.
#
# Results are read from last_run.txt, which now carries 首次可见耗时 and 最终收敛耗时.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
. (Join-Path $repoRoot 'tools/Build-Evidence.ps1')
Assert-BuildEvidence $exe $repoRoot
$baselines = Get-Content -LiteralPath $BaselinePath -Raw -Encoding UTF8 | ConvertFrom-Json
$runFile = Join-Path $repoRoot 'last_run.txt'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

. (Join-Path $repoRoot 'tests\Start-ProductExe.ps1')
. (Join-Path $PSScriptRoot 'Benchmark-Acceptance.ps1')
$env:FENGYUMU_AUTOMATION = '1'

if ([String]::IsNullOrWhiteSpace($Images)) {
    $Image = @(Get-ChildItem 'C:\Users\49480\Pictures\枫语幕实测截图' -Filter '*.png' -ErrorAction SilentlyContinue |
        Select-Object -First 2 -ExpandProperty FullName)
} else {
    $Image = @($Images -split '[,;]' | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
}

function Invoke-Case([string]$label, [string[]]$arguments) {
    $baseline = $baselines.PSObject.Properties[$label]
    if ($null -eq $baseline -or [int]$baseline.Value -le 0) { throw "$label 缺少有效收敛基线，不能验证不恶化 10%" }
    if (Test-Path -LiteralPath $runFile) { Remove-Item -LiteralPath $runFile -Force -ErrorAction SilentlyContinue }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-ProductExe -FilePath $exe -ArgumentList $arguments -WorkingDirectory $repoRoot -Wait -PassThru
    $watch.Stop()
    if ($null -eq $process -or $process.ExitCode -ne 0) { throw "$label 启动或进程失败" }
    if (-not (Test-Path -LiteralPath $runFile)) { throw "$label 未产生 last_run.txt" }
    $result = Get-Content -LiteralPath $runFile -Raw -Encoding UTF8
    if ($result.StartsWith('错误=')) {
        throw "$label 错误：$result"
    }
    $visible = [regex]::Match($result, '首次可见耗时=(\d+)').Groups[1].Value
    $converged = [regex]::Match($result, '最终收敛耗时=(\d+)').Groups[1].Value
    $hits = [regex]::Match($result, '命中数量=(\d+)').Groups[1].Value
    $scene = [regex]::Match($result, '识别场景=(.+)').Groups[1].Value.Trim()
    if ($visible.Length -eq 0 -or $converged.Length -eq 0 -or $hits.Length -eq 0) { throw "$label 报告缺少指标" }
    $firstBudget = if ($scene -eq '普通界面' -or $scene -eq '无面板') { 350 } else { 800 }
    $visibleMark = if ([int]$visible -le $firstBudget) { 'OK' } else { "超出 ${firstBudget}ms" }
    $convergedMark = if ([int]$converged -le 1600) { 'OK' } else { '超出 1600ms' }
    Write-Host ("{0,-34} 阶段估算首显={1,5}ms({2})  收敛={3,5}ms({4})  命中={5}  场景={6}" -f
        $label, $visible, $visibleMark, $converged, $convergedMark, $hits, $scene)
    Assert-BenchmarkAcceptance $result $firstBudget 1600 0 ([int]$baseline.Value)
}

if ($Image.Count -gt 0) {
    foreach ($path in $Image) {
        if (-not (Test-Path -LiteralPath $path)) { throw "缺少图片：$path" }
        Invoke-Case ([System.IO.Path]::GetFileName($path)) `
            @('--benchmark', "--benchmark-image=$path", "--benchmark-range=$Range")
    }
} else {
    Invoke-Case '合成文本' @('--benchmark', "--benchmark-text=$Text", "--benchmark-range=$Range")
}

Remove-Item Env:\FENGYUMU_AUTOMATION -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '验收目标：固定 UI 首次可见 <=800ms；最终收敛 <=1600ms 且不恶化基线 10%；无面板 <=350ms'
