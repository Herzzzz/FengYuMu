$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $repoRoot 'src/UserSettings.cs'),
    (Join-Path $repoRoot 'src/OnlineAiProtocol.cs'),
    (Join-Path $PSScriptRoot 'UserFeedbackSafetyHarness.cs'))
$root = Join-Path $repoRoot ('.bench/user-feedback-' + [Guid]::NewGuid().ToString('N'))
try {
    $count = [UserFeedbackSafetyHarness]::Run($root)
    if ($count -ne 20) { throw "Unexpected independent check count: $count" }
    Write-Output 'User feedback safety: 20/20 passed; actual Windows ACL/UI/game/provider paths remain unverified.'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
