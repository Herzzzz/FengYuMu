[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$InputManifest,
 [Parameter(Mandatory=$true)][string]$ModelPackageDirectory,
 [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7 -or [Environment]::Version.Major -lt 10){
 throw 'Cloud runner needs PowerShell 7 on .NET 10; this does not test Windows OCR or GDI rendering.'
}
$references=@()
foreach($relative in @(
 'sdcb.simdpaddleocr.modelprovider.1.0.0/lib/netstandard2.0/Sdcb.SimdPaddleOCR.ModelProvider.dll',
 'sdcb.simdpaddleocr.models.textlineorientation.1.0.0/lib/netstandard2.0/Sdcb.SimdPaddleOCR.Models.TextLineOrientation.dll',
 'sdcb.simdpaddleocr.models.chinesev6small.1.0.0/lib/netstandard2.0/Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll',
 'sdcb.simdpaddleocr.2.0.0/lib/net10.0/Sdcb.SimdPaddleOCR.dll')){
 $file=(Resolve-Path -LiteralPath (Join-Path $ModelPackageDirectory $relative)).Path
 [Reflection.Assembly]::LoadFrom($file)|Out-Null
 $references+=$file
}
$references+=@(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll'|Select-Object -ExpandProperty FullName)
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'StrongOcrHelper/StrongOcrEngine.cs') -Raw -Encoding UTF8
$source+=@'

namespace FengYuMu.StrongOcr {
 public static class ScreenshotOcrRunner {
  public static void Run(string manifest, string output) {
   var input = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
   var watch = System.Diagnostics.Stopwatch.StartNew();
   using(var engine = new StrongOcrEngine()) {
    long loadMs = watch.ElapsedMilliseconds;
    using(var writer = new System.IO.StreamWriter(output,false,new System.Text.UTF8Encoding(false))) {
     foreach(var item in input.RootElement.EnumerateArray()) {
      string id=item.GetProperty("id").GetString();
      byte[] bytes=System.IO.File.ReadAllBytes(item.GetProperty("bgraPath").GetString());
      int width=item.GetProperty("width").GetInt32(), height=item.GetProperty("height").GetInt32();
      long start=watch.ElapsedMilliseconds;
      var result=engine.Recognize(bytes,width,height,width*4);
      long elapsed=watch.ElapsedMilliseconds-start;
      writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
       id=id,width=width,height=height,loadMs=loadMs,runMs=elapsed,detected=result.DetectedCount,
       runtime=System.Environment.Version.ToString(),assembly=typeof(Sdcb.SimdPaddleOCR.PaddleOcrAll).Assembly.FullName,
       lines=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(result.Lines,line=>new {
        text=line.Text,confidence=line.RecognitionScore,x1=line.Box.X1,y1=line.Box.Y1,
        x2=line.Box.X2,y2=line.Box.Y2,x3=line.Box.X3,y3=line.Box.Y3,x4=line.Box.X4,y4=line.Box.Y4
       }))
      }));
      writer.Flush();
      System.Console.WriteLine("OCR " + id + " lines=" + result.Lines.Length + " ms=" + elapsed);
     }
    }
   }
  }
 }
}
'@
Add-Type -TypeDefinition $source -ReferencedAssemblies $references
[FengYuMu.StrongOcr.ScreenshotOcrRunner]::Run((Resolve-Path -LiteralPath $InputManifest).Path,[IO.Path]::GetFullPath($OutputPath))
