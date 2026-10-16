$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
# Compile the exact production classes and the harness into one assembly. This exercises
# real filesystem transactions and component hashes, with no Windows/API/provider stubs.
Add-Type -Path @((Join-Path $repoRoot 'src/UpdateFileTransaction.cs'),
    (Join-Path $repoRoot 'src/StrongOcrComponent.cs'),
    (Join-Path $repoRoot 'src/TranslationTerminology.cs'),
    (Join-Path $PSScriptRoot 'ReleaseSafetyHarness.cs'))
$root = Join-Path $repoRoot ('.bench/release-safety-' + [Guid]::NewGuid().ToString('N'))
try {
    if ([ReleaseSafetyHarness]::Run($root) -ne 20) { throw 'Unexpected check count' }
    . (Join-Path $repoRoot 'tools/Build-Evidence.ps1')
    $fakeRepo = Join-Path $root 'provenance'
    New-Item -ItemType Directory -Force -Path (Join-Path $fakeRepo 'src') | Out-Null
    $source = Join-Path $fakeRepo 'src/Input.cs'
    Set-Content -LiteralPath $source -Value '// original input'
    $exe = Join-Path $fakeRepo 'candidate.exe'
    Set-Content -LiteralPath $exe -Value 'fixture artifact, not a runnable EXE'
    Write-BuildEvidence $exe $fakeRepo 'fixture for input identity checks' | Out-Null
    Assert-BuildEvidence $exe $fakeRepo
    Set-Content -LiteralPath $source -Value '// changed after build'
    $rejected = $false
    try { Assert-BuildEvidence $exe $fakeRepo } catch { $rejected = $true }
    if (-not $rejected) { throw 'Changed source was not rejected' }
    Write-Output 'PASS: source drift rejected by input hash (not timestamp)'
    Set-Content -LiteralPath $source -Value '// original input'
    Set-Content -LiteralPath $exe -Value 'changed artifact'
    $rejected = $false
    try { Assert-BuildEvidence $exe $fakeRepo } catch { $rejected = $true }
    if (-not $rejected) { throw 'Changed EXE was not rejected' }
    Write-Output 'PASS: changed EXE rejected'
    Write-Output 'Release safety: 22/22 passed; Windows UI/network/device checks not included.'
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
