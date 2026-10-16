$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/CaptureFramePolicy.cs'),(Join-Path $PSScriptRoot 'CaptureFramePolicyHarness.cs'))
if([CaptureFramePolicyHarness]::Run() -ne 13){throw 'Unexpected frame policy case count'}
Write-Output 'Capture frame policy: 13/13 passed; actual Windows Graphics Capture, GPU and game unverified.'
