param([switch]$Before,[string]$BeforeRepository='/workspace/FengYuMu-option2-r3')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=if($Before){$BeforeRepository}else{$root}
Add-Type -Path @((Join-Path $source 'src/TextSurfaceReconstruction.cs'),(Join-Path $PSScriptRoot 'FusionSupportHarness.cs'))
$count=[FusionSupportHarness]::Run()
if($count -ne 7){throw "Unexpected check count: $count"}
Write-Output 'Fusion support: 7/7 passed; synthetic pixels, native game backgrounds remain unverified.'
