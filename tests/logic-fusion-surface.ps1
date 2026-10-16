$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/TextSurfaceReconstruction.cs'),(Join-Path $PSScriptRoot 'FusionSurfaceHarness.cs'))
$count=[FusionSurfaceHarness]::Run()
if($count -ne 15){throw "Unexpected check count: $count"}
Write-Output 'Fusion surface: 15/15 passed; controlled pixel fixtures only, actual game reconstruction and GDI text rendering remain unverified.'
