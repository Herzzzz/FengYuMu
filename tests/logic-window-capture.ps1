param([string]$ExePath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
if ([String]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $root 'bin/candidate/枫语幕.exe' }
$assembly=Load-TestAssembly $ExePath
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$clientType=$assembly.GetType('MapleOverlay.WindowCaptureClient',$true)
$targetType=$assembly.GetType('MapleOverlay.MapleWindowInfo',$true)
$client=[Activator]::CreateInstance($clientType,$true)
$form=[Windows.Forms.Form]::new()
try {
    $form.ClientSize=[Drawing.Size]::new(400,300)
    $form.BackColor=[Drawing.Color]::CornflowerBlue
    $form.Show(); [Windows.Forms.Application]::DoEvents()
    $origin=$form.PointToScreen([Drawing.Point]::Empty)
    $bounds=[Drawing.Rectangle]::new($origin,$form.ClientSize)
    $target=[Activator]::CreateInstance($targetType,$true)
    $targetType.GetField('Handle',$flags).SetValue($target,$form.Handle)
    $targetType.GetField('ProcessId',$flags).SetValue($target,[uint32]$PID)
    $task=$clientType.GetMethod('CaptureAsync',$flags).Invoke($client,[object[]]@($target,$bounds,[Threading.CancellationToken]::None))
    $deadline=[DateTime]::UtcNow.AddSeconds(6)
    while (-not $task.IsCompleted -and [DateTime]::UtcNow -lt $deadline) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 }
    if (-not $task.IsCompleted) { throw 'Production worker capture did not meet bounded completion' }
    $bitmap=$task.GetAwaiter().GetResult()
    try {
        if ($bitmap.Size -ne $form.ClientSize -or $bitmap.GetPixel(200,150).ToArgb() -ne $form.BackColor.ToArgb()) {
            throw 'Production worker did not return the target client pixels'
        }
    } finally { $bitmap.Dispose() }
    Write-Output 'Production worker captured the real test window and returned its pixels. MapleStory/occlusion/physical keys remain unverified.'
} finally { $client.Dispose(); $form.Close(); $form.Dispose() }
