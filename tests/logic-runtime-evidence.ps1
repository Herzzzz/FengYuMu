$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../tools/Runtime-Acceptance.ps1')
$good=@(
 'PID=1 hotkey-arrived id=1001',
 'PID=1 manual-request id=2 generation=3',
 'PID=1 ocr-start generation=3 automatic=False',
 'PID=1 overlay-first-paint generation=3 elapsedMs=125 labels=2',
 'PID=1 hotkey-arrived id=1001',
 'PID=1 overlay-hidden generation=3 manualRequest=2 processing=False'
)
$result=Assert-LiveToggleEvidence $good
if ($result.Generation -ne 3 -or $result.FirstPaintMs -ne 125 -or -not $result.Hidden) { throw 'Valid F8 evidence rejected' }
$cases=@(
 @($good | ForEach-Object { $_.Replace('id=1001','id=1002') }),
 @($good | ForEach-Object { $_.Replace('overlay-first-paint generation=3','overlay-first-paint generation=4') }),
 @($good | Where-Object { $_ -notmatch 'overlay-hidden' }),
 @($good | Where-Object { $_ -notmatch 'manual-request' }),
 @($good | ForEach-Object { $_.Replace('automatic=False','automatic=True') }),
 @($good + 'PID=1 overlay-published generation=3 elapsedMs=400 phase=final')
)
foreach ($case in $cases) {
    $rejected=$false
    try { Assert-LiveToggleEvidence $case | Out-Null } catch { $rejected=$true }
    if (-not $rejected) { throw 'Unrelated or incomplete runtime evidence accepted' }
}
Write-Output 'Runtime evidence: 7/7 passed; log decision rules only, physical inputs/Shell unverified.'
