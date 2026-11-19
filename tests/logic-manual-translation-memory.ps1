$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/ManualTranslationMemory.cs'),(Join-Path $PSScriptRoot 'ManualMemoryHarness.cs'))
if([ManualMemoryHarness]::Run() -ne 12){throw 'Unexpected count'}
Write-Output 'Manual memory: 12/12 passed; controlled pixel evidence, actual game/UI unverified.'
