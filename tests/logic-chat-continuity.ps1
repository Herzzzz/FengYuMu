$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/ChatFrameContinuity.cs'),(Join-Path $PSScriptRoot 'ChatContinuityHarness.cs'))
if([ChatContinuityHarness]::Run() -ne 9){throw 'Unexpected check count'}
Write-Output 'Chat continuity: 9/9 passed; actual policy state, capture/OCR/HTTP unverified.'
