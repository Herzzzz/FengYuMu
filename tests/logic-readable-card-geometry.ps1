$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/SceneTranslationPolicy.cs'),(Join-Path $PSScriptRoot 'ReadableCardGeometryHarness.cs'))
$count=[ReadableCardGeometryHarness]::Run()
if($count -ne 12){throw "Unexpected checks: $count"}
Write-Output 'Readable card geometry: 12/12 passed; numeric invariants only, native GDI remains unverified.'
