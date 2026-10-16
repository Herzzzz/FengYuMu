$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $root 'src/PanelRecognitionPolicy.cs'),(Join-Path $root 'src/QuestDetailGeometry.cs'),(Join-Path $PSScriptRoot 'R6PanelGeometryHarness.cs'))
$count=[R6PanelGeometryHarness]::Run()
if($count -ne 33){throw "Unexpected check count: $count"}
Write-Output 'R6 panel geometry: 33/33 passed; actual production geometry policy, controlled rectangles; Windows/CW recognition remains unverified.'
