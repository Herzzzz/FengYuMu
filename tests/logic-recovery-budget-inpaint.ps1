$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $root 'src/SupplementalOcrReview.cs'),(Join-Path $root 'src/TextSurfaceReconstruction.cs'),(Join-Path $root 'src/GlyphMaskInpainting.cs'),(Join-Path $PSScriptRoot 'RecoveryBudgetInpaintHarness.cs'))
$count=[RecoveryBudgetInpaintHarness]::Run()
if($count -ne 32){throw "Unexpected check count $count"}
Write-Output "Budget/inpainting recovery: $count passed; controlled clocks and pixels, not original CW samples or native GDI."
