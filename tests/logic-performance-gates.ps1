$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'tools/Benchmark-Acceptance.ps1')
$good = "首次可见耗时=800`n最终收敛耗时=2500`n命中数量=1`n"
Assert-BenchmarkAcceptance $good 800 2500 1
Assert-BenchmarkAcceptance ($good.Replace('2500','1375')) 800 2500 1 1250
$cases = @(
    @{ Name='final timeout'; Raw=$good.Replace('2500','2501'); First=800; Final=2500; Hits=1 },
    @{ Name='first timeout'; Raw=$good.Replace('800','801'); First=800; Final=2500; Hits=1 },
    @{ Name='zero hits'; Raw=$good.Replace('命中数量=1','命中数量=0'); First=800; Final=2500; Hits=1 },
    @{ Name='missing metric'; Raw='命中数量=1'; First=800; Final=2500; Hits=1 },
    @{ Name='error report'; Raw='错误=failed'; First=800; Final=2500; Hits=1 },
    @{ Name='baseline worsening'; Raw=$good.Replace('2500','1376'); First=800; Final=2500; Hits=1; Baseline=1250 }
)
foreach ($case in $cases) {
    $rejected = $false
    try { Assert-BenchmarkAcceptance $case.Raw $case.First $case.Final $case.Hits ([int]$case.Baseline) } catch { $rejected = $true }
    if (-not $rejected) { throw "Gate incorrectly passed: $($case.Name)" }
}
# Check the underlying unhandled gate also exits nonzero, not just a return value.
$testRoot = Join-Path $repoRoot ('.bench/performance-gates-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
try {
    $fixture = Join-Path $testRoot 'fail.ps1'
    $helper = (Join-Path $repoRoot 'tools/Benchmark-Acceptance.ps1').Replace("'", "''")
    @"
`$ErrorActionPreference = 'Stop'
. '$helper'
Assert-BenchmarkAcceptance "首次可见耗时=800``n最终收敛耗时=2501``n命中数量=1``n" 800 2500 1
"@ | Set-Content -LiteralPath $fixture -Encoding UTF8
    $shell = (Get-Process -Id $PID).Path
    & $shell -NoLogo -NoProfile -File $fixture 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'Hard-gate failure did not produce nonzero exit' }
    Write-Output 'Performance gates: 9/9 passed; actual OCR timings and real first presentation unverified.'
} finally { Remove-Item -LiteralPath $testRoot -Recurse -Force }
