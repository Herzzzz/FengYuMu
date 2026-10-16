$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Add-Type -Path @(
    (Join-Path $root 'src/PanelFocusPriority.cs'),
    (Join-Path $root 'src/FusionPatchMask.cs'),
    (Join-Path $PSScriptRoot 'PanelFocusAndMaskHarness.cs')
)

$count = [PanelFocusAndMaskHarness]::Run()

if ($count -ne 26) {
    throw "Unexpected check count: $count"
}

Write-Output 'Panel focus/mask: 26/26 passed; controlled geometry/pixels, native GDI unverified.'
