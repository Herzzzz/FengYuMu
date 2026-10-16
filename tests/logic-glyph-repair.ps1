$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $root 'src/TextSurfaceReconstruction.cs'),(Join-Path $root 'src/GlyphBackgroundReconstruction.cs'),(Join-Path $PSScriptRoot 'GlyphRepairHarness.cs'))
$count=[GlyphRepairHarness]::Run()
if($count -ne 17){throw "Unexpected check count: $count"}
Write-Output 'Glyph repair: 17/17 passed; controlled pixel models, actual game rendering remains unverified.'
