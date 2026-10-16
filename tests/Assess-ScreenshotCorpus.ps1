[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$ExePath,
 [Parameter(Mandatory=$true)][string]$InputManifest,
 [Parameter(Mandatory=$true)][string]$OcrOutput,
 [Parameter(Mandatory=$true)][string]$OutputPath,
 [string]$ReconstructionSource
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $ReconstructionSource){$ReconstructionSource=$repo}
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$storeType=$assembly.GetType('MapleOverlay.TranslationStore',$true)
$store=$storeType.GetConstructors($flags)[0].Invoke([object[]]@([string](Join-Path $repo '枫语幕词库.tsv')))
$storeType.GetMethod('Load',$flags).Invoke($store,@())|Out-Null
$normalize=$storeType.GetMethod('Normalize',$flags)
$confidence=$assembly.GetType('MapleOverlay.StrongOcrClient',$true).GetMethod('NormalizeConfidence',$flags)
$minimum=$assembly.GetType('MapleOverlay.StrongOcrClient',$true).GetField('MinimumConfidence',$flags).GetRawConstantValue()
$source='using System;using System.Drawing;using System.Collections.Generic;'+[Environment]::NewLine
foreach($name in @('TextSurfaceReconstruction.cs','GlyphBackgroundReconstruction.cs','GlyphMaskInpainting.cs')){
 $source+=(Get-Content -LiteralPath (Join-Path $ReconstructionSource ('src/'+$name)) -Raw -Encoding UTF8) -replace '(?m)^using .*;\r?\n',''
}
$source+=@'

namespace MapleOverlay {
 public static class ScreenshotSurfaceProbe {
  public static string Assess(byte[] bytes,int width,int height,int[] selected,int[][] protectedBoxes) {
   int[] frame=new int[checked(width*height)];Buffer.BlockCopy(bytes,0,frame,0,bytes.Length);
   Rectangle region=new Rectangle(selected[0],selected[1],selected[2],selected[3]);
   var rows=new List<Rectangle>{region};var guard=new List<Rectangle>();
   foreach(var box in protectedBoxes)guard.Add(new Rectangle(box[0],box[1],box[2],box[3]));
   ReconstructedTextSurface surface;string reason;string method="vertical";
   bool ok=TextSurfaceReconstruction.TryReconstruct(frame,width,height,region,rows,out surface,out reason,guard);
   if(!ok&&(reason=="unsupported-texture"||reason=="ambiguous-foreground")) {
    method="glyph-local";
    ok=GlyphBackgroundReconstruction.TryReconstruct(frame,width,height,region,rows,guard,out surface,out reason);
   }
   if(!ok) {
    method="glyph-mask-recovery";
    ok=GlyphMaskInpainting.TryReconstruct(frame,width,height,region,rows,guard,out surface,out reason);
   }
   return System.Text.Json.JsonSerializer.Serialize(new {accepted=ok,method=method,reason=reason,
    masked=ok?surface.MaskedPixels:0,sourceHeight=ok?surface.SourceHeight:0});
  }
 }
}
'@
Add-Type -TypeDefinition $source
$inputs=@(Get-Content -LiteralPath $InputManifest -Raw -Encoding UTF8|ConvertFrom-Json)
$ocr=@(Get-Content -LiteralPath $OcrOutput -Encoding UTF8|ForEach-Object{$_|ConvertFrom-Json})
$expectations=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'screenshot-expectations.json') -Raw -Encoding UTF8|ConvertFrom-Json
function N([string]$text){[string]$normalize.Invoke($null,@($text))}
function Box($line){
 $xs=@($line.x1,$line.x2,$line.x3,$line.x4);$ys=@($line.y1,$line.y2,$line.y3,$line.y4)
 $x=[Math]::Floor(($xs|Measure-Object -Minimum).Minimum);$y=[Math]::Floor(($ys|Measure-Object -Minimum).Minimum)
 return ,[int[]]@($x,$y,([Math]::Ceiling(($xs|Measure-Object -Maximum).Maximum)-$x),([Math]::Ceiling(($ys|Measure-Object -Maximum).Maximum)-$y))
}
$results=@()
foreach($case in $expectations.cases){
 $sample=@($inputs|Where-Object{$_.id -eq $case.id})
 $record=@($ocr|Where-Object{$_.id -eq $case.id})
 if($sample.Count -ne 1 -or $record.Count -ne 1){throw ('Missing or duplicated screenshot record: '+$case.id)}
 $original=Join-Path $repo $sample[0].source
 if((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $sample[0].sha256){throw 'Fixture hash changed'}
 $raw=[IO.File]::ReadAllBytes($sample[0].bgraPath)
 if((Get-FileHash -LiteralPath $sample[0].bgraPath -Algorithm SHA256).Hash -ne $sample[0].rawSha256){throw 'Decoded pixels changed'}
 $trusted=@($record[0].lines|Where-Object{[double]$confidence.Invoke($null,@([double]$_.confidence)) -ge $minimum})
 $guards=[Collections.Generic.List[int[]]]::new()
 foreach($line in $trusted){$guards.Add((Box $line))}
 foreach($field in $case.fields){
  $rectangle=$field[2];$nearby=@()
  foreach($line in $trusted){
   $b=Box $line;$cx=$b[0]+$b[2]/2.0;$cy=$b[1]+$b[3]/2.0
   if($cx -ge $rectangle[0] -and $cx -lt ($rectangle[0]+$rectangle[2]) -and
      $cy -ge $rectangle[1] -and $cy -lt ($rectangle[1]+$rectangle[3])){$nearby+=$line}
  }
  $texts=($nearby|ForEach-Object{$_.text}) -join ' '
  $normalized=N $texts;$expected=N $field[0]
  $rawHit=(' '+$normalized+' ').Contains(' '+$expected+' ')
  $arguments=if($case.method -eq 'FindSkillNameMatches' -or $case.method -eq 'FindTaskMatches'){
   [object[]]@($texts,'')
  }else{[object[]]@($texts)}
  $matches=@($storeType.GetMethod($case.method,$flags).Invoke($store,$arguments))
  $translated=@($matches|ForEach-Object{
   $entry=$_.GetType().GetField('Entry',$flags).GetValue($_)
   [string]$entry.GetType().GetField('Chinese',$flags).GetValue($entry)
  })
  $hit=@($translated|Where-Object{$_.Contains([string]$field[1])}).Count -gt 0
  $reconstruction=@()
  foreach($line in $nearby){
   $reconstruction+=[MapleOverlay.ScreenshotSurfaceProbe]::Assess($raw,[int]$sample[0].width,[int]$sample[0].height,
    (Box $line),$guards.ToArray())|ConvertFrom-Json
  }
  $results+=[pscustomobject]@{screenshot=$case.id;expectedEnglish=$field[0];expectedChinese=$field[1];
   ocrText=$texts;exactOcrRead=$rawHit;dictionaryHit=$hit;dictionaryCandidates=$translated;
   surface=$reconstruction;finalWindowsRender='UNVERIFIED'}
 }
}
$summary=[ordered]@{generatedUtc=[DateTime]::UtcNow.ToString('o');
 exeSha256=(Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash;
 source='MapleRoyals public fixtures: auxiliary regression only; NOT CW acceptance';
 reconstructionSource=$ReconstructionSource;
 reconstructionInputHashes=@('TextSurfaceReconstruction.cs','GlyphBackgroundReconstruction.cs','GlyphMaskInpainting.cs'|ForEach-Object{[ordered]@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $ReconstructionSource ('src/'+$_)) -Algorithm SHA256).Hash}});
 fields=$results.Count;exactOcrRead=@($results|Where-Object{$_.exactOcrRead}).Count;
 dictionaryHit=@($results|Where-Object{$_.dictionaryHit}).Count;
 failedFields=@($results|Where-Object{-not $_.dictionaryHit}).Count;
 finalWindowsRender='UNVERIFIED';missingCwScenes=$expectations.missingScenes;results=$results}
$summary|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output ('Screenshot fields='+$results.Count+' exactOCR='+$summary.exactOcrRead+' dictionaryHit='+$summary.dictionaryHit+' failed='+$summary.failedFields)
if($summary.failedFields -gt 0){exit 1}
