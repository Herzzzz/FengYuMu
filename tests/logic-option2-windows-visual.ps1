param([string]$ExePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $ExePath){$ExePath=Join-Path $repoRoot 'bin/option2-fusion-r1/枫语幕.exe'}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$store=$assembly.GetType('MapleOverlay.UserSettings',$true)
$pathField=$store.GetField('FilePath',$flags);$oldPath=$pathField.GetValue($null)
$temp=Join-Path $repoRoot ('.bench/option2-visual-'+[Guid]::NewGuid().ToString('N'))
$pathField.SetValue($null,(Join-Path $temp 'settings.dat'))
$main=$null;$count=0
try {
 $mainType=$assembly.GetType('MapleOverlay.MainPanelForm',$true)
 $main=$mainType.GetConstructors($flags)[0].Invoke([object[]]@($null))
 $main.Show();$mainType.GetMethod('ShowDashboardForTest',$flags).Invoke($main,@())|Out-Null
 foreach($dpi in @(96,144,192)){
  $mainType.GetMethod('ApplyDpiLayout',$flags).Invoke($main,[object[]]@($dpi,$false,$true))|Out-Null
  [Windows.Forms.Application]::DoEvents()
  $range=$mainType.GetField('range',$flags).GetValue($main)
  $labels=$mainType.GetField('rangeLegendLabels',$flags).GetValue($main)
  if($range.AutoSize){throw 'Native TrackBar AutoSize may hide captions'}
  foreach($label in $labels){
   if(-not $label.Visible -or -not $label.Parent.ClientRectangle.Contains($label.Bounds) -or $label.Bounds.IntersectsWith($range.Bounds)){throw "Caption hidden or overlaps TrackBar at dpi=$dpi"}
  }
  $count++;Write-Output "PASS: all three range captions visible inside card at dpi=$dpi"
 }
 foreach($name in @('BlueGlassButton','BlueGlassPanel')){
  $parentType=$assembly.GetType('MapleOverlay.MainPanelForm+BlueSurfacePanel',$true)
  $parent=[Activator]::CreateInstance($parentType,$true);$child=$null;$before=$null;$after=$null
  try {
   $parent.Size=[Drawing.Size]::new(400,240)
   $before=[Drawing.Bitmap]::new(400,240);$parent.DrawToBitmap($before,$parent.ClientRectangle)
   $child=[Activator]::CreateInstance($assembly.GetType('MapleOverlay.'+$name,$true),$true)
   $child.Bounds=[Drawing.Rectangle]::new(60,80,160,70);$parent.Controls.Add($child)
   $after=[Drawing.Bitmap]::new(400,240);$parent.DrawToBitmap($after,$parent.ClientRectangle)
   foreach($offset in @(@(0,0),@(1,0),@(0,1))){
    $x=60+$offset[0];$y=80+$offset[1]
    if($before.GetPixel($x,$y).ToArgb() -ne $after.GetPixel($x,$y).ToArgb()){throw "$name leaves square parent-background pixels"}
   }
   $count++;Write-Output "PASS: $name rounded corners reproduce actual parent gradient"
  } finally {if($before){$before.Dispose()};if($after){$after.Dispose()};$parent.Dispose()}
 }
 $surfaceType=$assembly.GetType('MapleOverlay.ReconstructedTextSurface',$true)
 $surface=[Activator]::CreateInstance($surfaceType,$true)
 $surfaceType.GetField('Region',$flags).SetValue($surface,[Drawing.Rectangle]::new(10,10,160,26))
 $pixels=New-Object int[] (160*26);for($i=0;$i -lt $pixels.Length;$i++){$pixels[$i]=[Drawing.Color]::White.ToArgb()}
 $surfaceType.GetField('Pixels',$flags).SetValue($surface,$pixels)
 $surfaceType.GetField('Ink',$flags).SetValue($surface,[Drawing.Color]::Black.ToArgb())
 $surfaceType.GetField('SourceHeight',$flags).SetValue($surface,[single]14)
 $render=$assembly.GetType('MapleOverlay.FusionBitmapRenderer',$true).GetMethod('TryRender',$flags)
 $image=$render.Invoke($null,[object[]]@($surface,'任务详情',[single]1))
 if($null -eq $image){throw 'Actual GDI rejected short complete translation'}
 try {
  $ink=$false;for($y=0;$y -lt $image.Height;$y++){for($x=0;$x -lt $image.Width;$x++){$p=$image.GetPixel($x,$y);if($p.A -ne 255){throw 'Transparent repair patch'};if($p.R -lt 200){$ink=$true}}}
  if(-not $ink){throw 'Actual GDI draw produced no ink'}
  $count++;Write-Output 'PASS: complete short translation produces opaque native GDI patch and visible ink'
 } finally {$image.Dispose()}
 $overflow=$render.Invoke($null,[object[]]@($surface,('任务详情非常长不能被截掉'*100),[single]1))
 if($null -ne $overflow){$overflow.Dispose();throw 'Incomplete translation was accepted for fusion'}
 $count++;Write-Output 'PASS: incomplete text cannot replace original content'
 if($count -ne 7){throw "Unexpected native case count: $count"}
 Write-Output 'Option2 Windows visuals: 7/7 passed; native GDI and WinForms only, actual game and physical inputs remain unverified.'
} finally {
 if($null -ne $main){$main.Dispose()}
 $pathField.SetValue($null,$oldPath)
 if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}
}
