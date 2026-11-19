$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $root 'src/TextSurfaceReconstruction.cs'),(Join-Path $root 'src/FusionTextSpace.cs'),(Join-Path $PSScriptRoot 'FusionTextSpaceHarness.cs'))
$count=[FusionTextSpaceHarness]::Run()
if($count -ne 16){throw "Unexpected check count: $count"}
Write-Output 'Fusion text space: 16/16 passed; actual production pixel policy; native GDI font measurement remains unverified.'
