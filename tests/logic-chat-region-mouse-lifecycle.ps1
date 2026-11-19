param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
if ([String]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $repoRoot 'bin/candidate/枫语幕.exe' }
$assembly = Load-TestAssembly $ExePath
$flags = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$type = $assembly.GetType('MapleOverlay.ChatRegionSelectorForm',$true)
$ctor = $type.GetConstructor($flags,$null,[Type[]]@([Drawing.Rectangle],[Drawing.Rectangle],[Drawing.Bitmap]),$null)
$getRegion = $type.GetProperty('SelectedScreenRegion',$flags)
$game = [Drawing.Rectangle]::new(0,0,1200,800)
$snapshot = [Drawing.Bitmap]::new(1200,800)
$settings=$assembly.GetType('MapleOverlay.ChatRegionSettings',$true)
$store=$assembly.GetType('MapleOverlay.UserSettings',$true)
$pathField=$store.GetField('FilePath',$flags)
$originalPath=$pathField.GetValue($null)
$testRoot=Join-Path $repoRoot ('.bench/selector-settings-'+[Guid]::NewGuid().ToString('N'))
$pathField.SetValue($null,(Join-Path $testRoot 'user-settings.dat'))
try {
    foreach ($height in @(24,30,36,39,40)) {
        $initial = [Drawing.Rectangle]::new(100,500,600,$height)
        $selector = $ctor.Invoke([object[]]@($game.PSObject.BaseObject,$initial.PSObject.BaseObject,$snapshot.PSObject.BaseObject))
        try {
            $before = $getRegion.GetValue($selector,$null)
            if ($before.Height -ne $height) { throw 'Usable short selection was unexpectedly replaced' }
            $middleY = 500 + [int]($height / 2)
            $type.GetMethod('SelectorMouseDown',$flags).Invoke($selector,@($selector,[Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left,1,400,$middleY,0))) | Out-Null
            $type.GetMethod('SelectorMouseMove',$flags).Invoke($selector,@($selector,[Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left,0,440,($middleY+20),0))) | Out-Null
            $type.GetMethod('SelectorMouseUp',$flags).Invoke($selector,@($selector,[Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left,1,440,($middleY+20),0))) | Out-Null
            $after = $getRegion.GetValue($selector,$null)
            if ($after.X -ne $before.X+40 -or $after.Y -ne $before.Y+20 -or $after.Height -ne $height) {
                throw "$height px mouse-down/move/up rolled back or resized unexpectedly"
            }
            $settings.GetMethod('SaveManual',$flags).Invoke($null,[object[]]@($after,$game)) | Out-Null
            # Simulate a new process: the next resolve must use the persisted canonical data.
            $settings.GetField('lastSavedRegion',$flags).SetValue($null,[Drawing.Rectangle]::Empty)
            $settings.GetField('lastSavedGame',$flags).SetValue($null,[Drawing.Rectangle]::Empty)
            $settings.GetField('lastSavedRegionSignature',$flags).SetValue($null,'')
            $resolved=$settings.GetMethod('ResolveForGame',$flags).Invoke($null,[object[]]@($game))
            if ($resolved -ne $after) { throw 'Saved short selection changed on persisted reopen' }
            $reopened = $ctor.Invoke([object[]]@($game.PSObject.BaseObject,$resolved.PSObject.BaseObject,$snapshot.PSObject.BaseObject))
            try { if ($getRegion.GetValue($reopened,$null) -ne $after) { throw 'Reopened selection differs' } }
            finally { $reopened.Dispose() }
        } finally { $selector.Dispose() }
    }
    Write-Output 'Selector event lifecycle: 24/30/36/39/40px down/move/up/persist/reopen checked. Synthetic snapshot/event calls; physical mouse/DPI acceptance remains unverified.'
} finally {
    $snapshot.Dispose()
    $pathField.SetValue($null,$originalPath)
    foreach ($fieldName in @('lastSavedRegion','lastSavedGame')) { $settings.GetField($fieldName,$flags).SetValue($null,[Drawing.Rectangle]::Empty) }
    $settings.GetField('lastSavedRegionSignature',$flags).SetValue($null,'')
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
