param([string]$ExePath)
$ErrorActionPreference = 'Stop'
# This test loads only pure numeric/filesystem types from the candidate. It does not
# start the Windows entry point or resolve WinForms/OCR types, and proves neither UI
# nor Windows CLR runtime compatibility.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $repoRoot '枫语幕.exe' }
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly = Load-TestAssembly $ExePath
$flags = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$safety = $assembly.GetType('MapleOverlay.TranslationValueSafety', $true)
$numbers = $safety.GetMethod('PreservesNumbers', $flags)
if ([bool]$numbers.Invoke($null, @('5','50'))) { throw 'Candidate accepted changed numeric token' }
if ([bool]$numbers.Invoke($null, @('5/12','12/5'))) { throw 'Candidate accepted reordered numeric tokens' }
if (-not [bool]$numbers.Invoke($null, @('5/12','进度5/12'))) { throw 'Candidate rejected unchanged numeric tokens' }
$transaction = $assembly.GetType('MapleOverlay.UpdateFileTransaction', $true)
$planType = $assembly.GetType('MapleOverlay.UpdateFilePlan', $true)
$hash = $transaction.GetMethod('Hash', $flags)
$execute = $transaction.GetMethod('Execute', $flags)
$root = Join-Path $repoRoot ('.bench/candidate-transaction-' + [Guid]::NewGuid().ToString('N'))
try {
    $install = Join-Path $root 'install'
    $stage = Join-Path $root 'stage'
    New-Item -ItemType Directory -Force -Path $install,$stage | Out-Null
    [IO.File]::WriteAllText((Join-Path $install 'app.exe'),'old')
    [IO.File]::WriteAllText((Join-Path $stage 'app.exe'),'new')
    $plan = [Activator]::CreateInstance($planType, $true)
    $planType.GetField('SourceName',$flags).SetValue($plan,'app.exe')
    $planType.GetField('TargetName',$flags).SetValue($plan,'app.exe')
    $sourcePath = [string](Join-Path $stage 'app.exe')
    $planType.GetField('Sha256',$flags).SetValue($plan,[string]$hash.Invoke($null,[object[]]@($sourcePath)))
    $plans = [Array]::CreateInstance($planType,1); $plans.SetValue($plan,0)
    $backup = Join-Path $root 'backup'
    $invokeArgs = [object[]]::new(4)
    $invokeArgs[0] = [string]$install; $invokeArgs[1] = [string]$stage; $invokeArgs[2] = [string]$backup; $invokeArgs[3] = $plans
    $execute.Invoke($null,$invokeArgs) | Out-Null
    if ([IO.File]::ReadAllText((Join-Path $install 'app.exe')) -ne 'new') { throw 'Candidate did not replace file' }
    if ([IO.File]::ReadAllText((Join-Path $backup 'app.exe')) -ne 'old') { throw 'Candidate did not preserve backup' }
    Write-Output "Candidate binary: 5/5 passed; EXE SHA-256=$((Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash)"
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
