param([string]$ExePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $ExePath){$ExePath=Join-Path $repoRoot 'bin/candidate-r4/枫语幕.exe'}
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Static,Public,NonPublic'
$measure=$assembly.GetType('MapleOverlay.OverlayPaintSafety',$true).GetMethod('TryMeasureReadableCard',$flags)
if($null -eq $measure){throw 'Candidate lacks real readable-card measurement'}
$texts=@('任务概要','加工好的木材 0/10',('据说废弃都市有个叫伊卡路斯的人，想制作翅膀。请分别收集加工好的木材、蝙蝠翅膀和粗羽毛，再返回任务窗口查看进度。'*2))
$count=0
foreach($dpi in @(96,144,192)){
 foreach($width in @(240,560)){
  foreach($text in $texts){
   $bitmap=[Drawing.Bitmap]::new(1200,1200);$bitmap.SetResolution($dpi,$dpi)
   $graphics=[Drawing.Graphics]::FromImage($bitmap)
   $font=[Drawing.Font]::new('Microsoft YaHei UI',[single](15*$dpi/96),[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
   $format=[Drawing.StringFormat]::GenericTypographic.Clone()
   try {
    $format.FormatFlags=$format.FormatFlags -band (-bnot [int][Drawing.StringFormatFlags]::NoWrap)
    $client=[Drawing.RectangleF]::new(10,10,$width,1100)
    $anchor=[Drawing.RectangleF]::new(24,24,20,12)
    $args=[object[]]@($graphics,$text,$font,$format,$anchor,$client,[single]($width),[Drawing.RectangleF]::Empty,[Drawing.RectangleF]::Empty,[int]0,'')
    if(-not [bool]$measure.Invoke($null,$args)){throw "Native layout rejected: dpi=$dpi width=$width reason=$($args[10]) fitted=$($args[9])/$($text.Length)"}
    $card=[Drawing.RectangleF]$args[7];$textArea=[Drawing.RectangleF]$args[8]
    if([int]$args[9] -lt $text.Length -or -not $client.Contains($card)){throw 'Native proof or game boundary failed'}
    $graphics.Clear([Drawing.Color]::Black);$graphics.SetClip($card)
    $graphics.TextRenderingHint=[Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
    $graphics.DrawString($text,$font,[Drawing.Brushes]::White,$textArea,$format)
    $ink=$false
    :pixels for($y=[int]$card.Top;$y -lt [Math]::Ceiling($card.Bottom);$y++){
     for($x=[int]$card.Left;$x -lt [Math]::Ceiling($card.Right);$x++){
      if($bitmap.GetPixel($x,$y).ToArgb() -ne [Drawing.Color]::Black.ToArgb()){$ink=$true;break pixels}
     }
    }
    if(-not $ink){throw 'Native DrawString produced no visible ink'}
    $count++;Write-Output "PASS: native GDI text fits and renders: dpi=$dpi width=$width chars=$($text.Length)"
   } finally {$format.Dispose();$font.Dispose();$graphics.Dispose();$bitmap.Dispose()}
  }
 }
}
if($count -ne 18){throw "Unexpected native case count: $count"}
Write-Output 'Readable card Windows: 18/18 native GDI measurement+ink cases passed; actual game/F8 still require physical acceptance.'
