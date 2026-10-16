param([string]$ExePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $ExePath){$ExePath=Join-Path $repoRoot 'bin/candidate-r3/枫语幕.exe'}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$store=$assembly.GetType('MapleOverlay.UserSettings',$true)
$pathField=$store.GetField('FilePath',$flags);$originalPath=$pathField.GetValue($null)
$temporary=Join-Path $repoRoot ('.bench/r3-ui-'+[Guid]::NewGuid().ToString('N'))
$pathField.SetValue($null,(Join-Path $temporary 'settings.dat'))
$main=$null;$selector=$null
try {
 if($null -ne $assembly.GetType('MapleOverlay.OfflineAiClient',$false) -or $null -ne $assembly.GetType('MapleOverlay.AiInstallForm',$false)){throw 'Offline AI implementation still in candidate'}
 Write-Output 'PASS: no offline inference client or installer'
 $mainType=$assembly.GetType('MapleOverlay.MainPanelForm',$true)
 $selectorType=$assembly.GetType('MapleOverlay.ChatRegionSelectorForm',$true)
 $main=$mainType.GetConstructors($flags)[0].Invoke([object[]]@($null))
 $main.Show();[Windows.Forms.Application]::DoEvents()
 $main.Size=[Drawing.Size]::new(600,800)
 $mainType.GetMethod('EndUserWindowInteraction',$flags).Invoke($main,@())|Out-Null
 $before=$main.Bounds;$beforeStyle=$main.FormBorderStyle
 $game=[Drawing.Rectangle]::new(40,50,1000,700)
 $initial=[Drawing.Rectangle]::new(140,550,600,30)
 $ctor=$selectorType.GetConstructor($flags,$null,[Type[]]@([Drawing.Rectangle],[Drawing.Rectangle],[Drawing.Bitmap]),$null)
 $selector=$ctor.Invoke([object[]]@($game,$initial,$null))
 $mainType.GetMethod('ShowGameRegionSelector',$flags).Invoke($main,[object[]]@($selector,$game,$null))|Out-Null
 [Windows.Forms.Application]::DoEvents()
 if($selector.TopLevel -or $selector.TopLevelControl -ne $main -or $main.Bounds -ne $game -or $main.ClientSize -ne $game.Size){throw 'Selection did not reuse main HWND at game client bounds'}
 Write-Output 'PASS: main form reused at game client bounds'
 if($main.TransparencyKey -ne [Drawing.Color]::Magenta -or $selector.BackColor -ne [Drawing.Color]::Magenta -or $null -ne $selectorType.GetField('snapshot',$flags)){throw 'Opaque selector or screenshot storage'}
 Write-Output 'PASS: transparent live surface without screenshot storage'
 $top=@([Windows.Forms.Application]::OpenForms|Where-Object {$_.TopLevel -and $_.Visible})
 if($top.Count -ne 1 -or $top[0] -ne $main){throw 'Extra top-level window'}
 Write-Output 'PASS: no extra top-level window'
 foreach($name in @('SelectorMouseDown','SelectorMouseMove','SelectorMouseUp')){
  $x=if($name -eq 'SelectorMouseDown'){400}else{440}
  $y=if($name -eq 'SelectorMouseDown'){515}else{535}
  $selectorType.GetMethod($name,$flags).Invoke($selector,[object[]]@($selector,[Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left,1,$x,$y,0)))|Out-Null
 }
 $after=$selectorType.GetProperty('SelectedScreenRegion',$flags).GetValue($selector,$null)
 if($after.X -ne $initial.X+40 -or $after.Y -ne $initial.Y+20 -or $after.Height -ne 30){throw 'Short region moved/scaled incorrectly'}
 Write-Output 'PASS: short region move and release preserve size'
 $selector.DialogResult=[Windows.Forms.DialogResult]::Cancel;$selector.Close();[Windows.Forms.Application]::DoEvents()
 if($main.Bounds -ne $before -or $main.FormBorderStyle -ne $beforeStyle -or $main.TransparencyKey -eq [Drawing.Color]::Magenta){throw 'Main form did not restore'}
 Write-Output 'PASS: cancel restores main form geometry and display'
 $mainType.GetMethod('ApplyDpiLayout',$flags).Invoke($main,[object[]]@(144,$true,$false))|Out-Null
 if($main.Bounds.Size -ne $before.Size){throw 'DPI layout reverted saved main size'}
 Write-Output 'PASS: manual sizing retained after selector exit and DPI layout'
 Write-Output 'R3 Windows UI: 7/7 passed; synthetic events and real WinForms only; physical input and game pixels remain unverified.'
} finally {
 if($null -ne $selector -and -not $selector.IsDisposed){$selector.Dispose()}
 if($null -ne $main){$main.Dispose()}
 $pathField.SetValue($null,$originalPath)
 if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Recurse -Force}
}
