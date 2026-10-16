param([Parameter(Mandatory=$true)][string]$ExePath,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'UNVERIFIED: this component test requires native Windows GDI.'}
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
Add-Type -AssemblyName System.Drawing
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$surfaceType=$assembly.GetType('MapleOverlay.ReconstructedTextSurface',$true)
$renderer=$assembly.GetType('MapleOverlay.FusionBitmapRenderer',$true)
$space=$assembly.GetType('MapleOverlay.FusionTextSpace',$true)
$fonts=New-Object Drawing.Text.InstalledFontCollection
try{if(@($fonts.Families|Where-Object{$_.Name -eq 'SimSun'}).Count -eq 0){throw 'UNVERIFIED: SimSun is absent; install the normal Windows Chinese font capability before measuring the selected font.'}}finally{$fonts.Dispose()}
$surface=[Activator]::CreateInstance($surfaceType,$true)
function SetField([string]$name,$value){$surfaceType.GetField($name,$flags).SetValue($surface,$value)}
SetField 'Region' ([Drawing.Rectangle]::new(20,20,18,24))
SetField 'SourceHeight' ([single]18)
SetField 'Ink' ([Drawing.Color]::Black.ToArgb())
$pixels=New-Object int[] (18*24);for($i=0;$i -lt $pixels.Length;$i++){$pixels[$i]=[Drawing.Color]::White.ToArgb()};SetField 'Pixels' $pixels
$render=$renderer.GetMethod('TryRender',$flags)
$measure=$renderer.GetMethod('RequiredSingleLineWidth',$flags)
$text='生命值'
$small=$render.Invoke($null,@($surface,$text,[single]1))
if($null -ne $small){$small.Dispose();throw 'Expected the original narrow region to refuse incomplete text'}
$required=[int]$measure.Invoke($null,@($surface,$text,[single]1))
if($required -le 18){throw 'Native measurement did not obtain the complete single-line width'}
$frame=New-Object int[] (200*100);for($i=0;$i -lt $frame.Length;$i++){$frame[$i]=[Drawing.Color]::White.ToArgb()}
$guards=New-Object 'Collections.Generic.List[Drawing.Rectangle]'
$arguments=[object[]]@($surface,$frame,200,100,$required,256,300000,$guards,$null,$null)
if(-not $space.GetMethod('TryExpandRight',$flags).Invoke($null,$arguments)){throw ('Blank space rejected: '+$arguments[9])}
$expanded=$arguments[8];$patch=$render.Invoke($null,@($expanded,$text,[single]1))
if($null -eq $patch){throw 'The complete translation still did not fit the independently measured space'}
try{
 $changed=0;for($y=0;$y -lt $patch.Height;$y++){for($x=0;$x -lt $patch.Width;$x++){if($patch.GetPixel($x,$y).ToArgb() -ne [Drawing.Color]::White.ToArgb()){$changed++}}}
 if($changed -eq 0){throw 'Native GDI returned an empty patch'}
 if($surfaceType.GetField('SourceHeight',$flags).GetValue($expanded) -ne [single]18){throw 'Font height changed'}
}finally{$patch.Dispose()}
$guards.Add([Drawing.Rectangle]::new(38,20,100,24));$arguments[8]=$null;$arguments[9]=$null
if($space.GetMethod('TryExpandRight',$flags).Invoke($null,$arguments) -or $arguments[9] -ne 'protected-space'){throw 'Protected numeric area was not respected'}
[ordered]@{status='PASS';componentOnly=$true;exeSha256=(Get-FileHash $ExePath -Algorithm SHA256).Hash;
 font='SimSun';sourceHeight=18;requiredWidth=$required;drawnPixels=$changed;fullTextFit=$true;
 protectedValueBlocked=$true;gameLoginRequired=$false;physicalF8='UNVERIFIED';screenshotsExported=$false}|
 ConvertTo-Json|Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output 'Native GDI component check passed; no game login or paid API used; physical F8 and CW screenshot pipeline remain UNVERIFIED.'
