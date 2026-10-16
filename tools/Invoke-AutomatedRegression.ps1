[CmdletBinding()]
param(
    # Where the per-test report is written. Defaults to a file beside the repository.
    [string]$ReportPath,

    # Only run tests that never start the product GUI (fast, safe while playing).
    [switch]$SkipGuiTests
)

# Runs the repository's logic/regression suite with the automation gate enabled.
#
# Why this exists: the update-related tests drive the real updater, whose final step is to
# start the freshly installed program. With placeholder files in a sandbox that launch made
# Windows raise its "unknown publisher" security prompt on the desktop - once per test run,
# which is unacceptable while the player is in a game. FENGYUMU_AUTOMATION=1 makes exactly
# that step a no-op (see AutomationPolicy in src\ApplicationUpdater.cs) while every real
# assertion - verification, backup, replacement, rollback - still executes.
#
# Always use this entry point instead of launching tests by hand.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testsDir = Join-Path $repoRoot 'tests'
if (-not (Test-Path -LiteralPath $testsDir)) { throw "找不到测试目录：$testsDir" }

if ([String]::IsNullOrEmpty($ReportPath)) {
    $ReportPath = Join-Path (Split-Path -Parent $repoRoot) 'automated-regression-report.txt'
}

$env:FENGYUMU_AUTOMATION = '1'
Write-Host "已启用自动化闸门 FENGYUMU_AUTOMATION=1（更新流程不会启动任何 GUI）"

# Reflection tests load the built EXE, so the artefact is rebuilt from the current sources before
# anything runs, and the resulting hash is recorded in the report. Without this the suite can pass
# against a build that no longer matches the source it is supposed to verify.
$buildScript = Join-Path $repoRoot 'src\build.ps1'
if (Test-Path -LiteralPath $buildScript) {
    Write-Host '从当前源码重建产物……'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
}
$exePath = Join-Path $repoRoot '枫语幕.exe'
$exeHash = if (Test-Path -LiteralPath $exePath) {
    (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
} else { '<缺失>' }
Write-Host "产物 SHA-256：$exeHash"

$manifest = Get-ChildItem -LiteralPath $testsDir -Filter '*.ps1' |
    Where-Object { $_.Name -match '^(logic|regression|benchmark|compare|tray|gamepad)-' } |
    Sort-Object Name

$results = New-Object System.Collections.Generic.List[object]
foreach ($test in $manifest) {
    $text = Get-Content -LiteralPath $test.FullName -Raw -Encoding UTF8
    # Tests that need an external resource (real screenshots, a local model directory) declare
    # mandatory parameters. Running them without those inputs would report a configuration
    # failure as a product failure, so they are skipped and named as such.
    if ($text -match '(?m)^\s*\[Parameter\(Mandatory\s*=\s*\$true\)\]' -or
        $text -match 'AUTOMATION_REQUIRES_EXTERNAL_RESOURCE') {
        $results.Add([pscustomobject]@{ Test = $test.Name; Result = 'SKIPPED(需外部资源)'; Detail = '' })
        continue
    }
    # A GUI test launches the built EXE. They are correct but slow, and each launch can be
    # interrupted by desktop prompts, so they are opt-in via -SkipGuiTests.
    $isGuiTest = ($text -match 'Start-ProductExe' -or $text -match '--benchmark' -or $text -match 'Start-Process')
    if ($SkipGuiTests -and $isGuiTest) {
        $results.Add([pscustomobject]@{ Test = $test.Name; Result = 'SKIPPED(GUI)'; Detail = '' })
        continue
    }

    $stdout = Join-Path $env:TEMP ("autoreg-out-" + [guid]::NewGuid().ToString('N') + ".txt")
    $stderr = Join-Path $env:TEMP ("autoreg-err-" + [guid]::NewGuid().ToString('N') + ".txt")
    $process = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $test.FullName) `
        -WorkingDirectory $repoRoot -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $out = if (Test-Path $stdout) { Get-Content $stdout -Raw -Encoding UTF8 } else { '' }
    $err = if (Test-Path $stderr) { Get-Content $stderr -Raw -Encoding UTF8 } else { '' }
    Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue

    $detail = ''
    if ($process.ExitCode -eq 0) {
        $detail = (($out -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -Last 1)
    } else {
        $detail = (($err -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 1)
    }
    $results.Add([pscustomobject]@{
        Test = $test.Name
        Result = if ($process.ExitCode -eq 0) { 'PASS' } else { "FAIL($($process.ExitCode))" }
        Detail = ([string]$detail).Trim()
    })
    Write-Host ("{0,-48} {1}" -f $test.Name, $results[-1].Result)
}

Remove-Item Env:\FENGYUMU_AUTOMATION -ErrorAction SilentlyContinue

$pass = @($results | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($results | Where-Object { $_.Result -like 'FAIL*' }).Count
$skipped = @($results | Where-Object { $_.Result -like 'SKIPPED*' }).Count

$lines = @("枫语幕自动化回归  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    ("产物 SHA-256：{0}" -f $exeHash),
    ("通过 {0} / 失败 {1} / 跳过 {2} / 总计 {3}" -f $pass, $fail, $skipped, $results.Count), '')
foreach ($r in $results) { $lines += ("{0,-48} {1,-14} {2}" -f $r.Test, $r.Result, $r.Detail) }
Set-Content -LiteralPath $ReportPath -Value $lines -Encoding UTF8

Write-Host ''
Write-Host ("通过 {0} / 失败 {1} / 跳过 {2}" -f $pass, $fail, $skipped)
Write-Host ("报告：{0}" -f $ReportPath)
if ($fail -gt 0) { exit 1 }
