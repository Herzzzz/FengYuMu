param([Parameter(Mandatory=$true)][string]$ExePath)
$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'Native Windows is required; do not count a non-Windows skip as a pass.'}
Add-Type -AssemblyName System.Drawing
$exe=(Resolve-Path -LiteralPath $ExePath).Path
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance'
$renderer=$assembly.GetType('MapleOverlay.FusionBitmapRenderer',$true)
$transparent=$renderer.GetMethod('MakeTransparent',$flags)
$render=$renderer.GetMethod('TryRender',$flags)
$surfaceType=$assembly.GetType('MapleOverlay.ReconstructedTextSurface',$true)
$script:count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output ('PASS: '+$name)}
$background=[Drawing.Color]::FromArgb(50,55,60)
$frame=New-Object int[] (80*80)
for($i=0;$i -lt $frame.Length;$i++){$frame[$i]=$background.ToArgb()}
$a=New-Object Drawing.Bitmap 20,16,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$b=New-Object Drawing.Bitmap 20,16,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$canvas=New-Object Drawing.Bitmap 80,80,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
try{
 foreach($patch in @($a,$b)){
  $g=[Drawing.Graphics]::FromImage($patch)
  try{$g.Clear($background)}finally{$g.Dispose()}
  $patch.SetPixel(5,5,[Drawing.Color]::White)
 }
 $ma=[bool[]]$transparent.Invoke($null,[object[]]@($a,$frame,80,80,([Drawing.Rectangle]::new(10,10,20,16))))
 $mb=[bool[]]$transparent.Invoke($null,[object[]]@($b,$frame,80,80,([Drawing.Rectangle]::new(10,22,20,16))))
 Check ($ma.Length -eq 320 -and $ma[105]) 'actual native renderer returns the changed-pixel mask'
 Check ($a.GetPixel(0,0).A -eq 0 -and $a.GetPixel(5,5).A -eq 255) 'LockBits/Marshal retain only changed pixels'
 $g=[Drawing.Graphics]::FromImage($canvas)
 try{
  $g.Clear($background);$g.DrawImageUnscaled($a,10,10);$g.DrawImageUnscaled($b,10,22)
 }finally{$g.Dispose()}
 Check ($canvas.GetPixel(15,15).ToArgb() -eq [Drawing.Color]::White.ToArgb() -and $canvas.GetPixel(15,27).ToArgb() -eq [Drawing.Color]::White.ToArgb()) 'native alpha composition preserves both adjacent patches'
 $surface=[Activator]::CreateInstance($surfaceType,$true)
 $region=[Drawing.Rectangle]::new(0,0,240,32)
 $pixels=New-Object int[] (240*32)
 for($i=0;$i -lt $pixels.Length;$i++){$pixels[$i]=$background.ToArgb()}
 $surfaceType.GetField('Region',$flags).SetValue($surface,$region)
 $surfaceType.GetField('Pixels',$flags).SetValue($surface,$pixels)
 $surfaceType.GetField('SourceHeight',$flags).SetValue($surface,[single]18)
 $surfaceType.GetField('Ink',$flags).SetValue($surface,[Drawing.Color]::White.ToArgb())
 $patch=$render.Invoke($null,[object[]]@($surface,'属性 10',[single]1))
 try{Check ($null -ne $patch -and $patch.Width -eq 240) 'actual native GDI font layout accepts a complete Chinese label'}finally{if($patch){$patch.Dispose()}}
 Write-Output ('Native Windows GDI: '+$count+' passed; EXE_SHA256='+(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)
 Write-Output 'These checks do not establish real CW OCR, F8, window switching, or optional Paddle installation.'
}finally{$a.Dispose();$b.Dispose();$canvas.Dispose()}
