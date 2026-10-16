$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/SceneTranslationPolicy.cs'),
    (Join-Path $repoRoot 'src/OnlineAiProtocol.cs'),
    (Join-Path $repoRoot 'src/OnlineAiResponse.cs'),
    (Join-Path $PSScriptRoot 'SceneFeedbackSafetyHarness.cs'))
$count = [SceneFeedbackSafetyHarness]::Run()
if ($count -ne 32) { throw "Unexpected check count: $count" }
Write-Output "Scene feedback safety: $count/$count passed; actual GDI/game/mouse/hotkeys and provider HTTP remain unverified."
