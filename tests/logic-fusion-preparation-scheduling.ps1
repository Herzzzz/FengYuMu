param([switch]$Before,[string]$BeforeRepository='/workspace/FengYuMu-option2-r2')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$sourceRoot=if($Before){$BeforeRepository}else{$repoRoot}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $sourceRoot 'src/FusionOverlay.cs'))).GetRoot()
$form=@($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'OverlayForm'})[0]
$names=@('FusionAsset','fusionAssets','fusionSourceFrame','fusionSourceScreen','paintedFusionAssets','ClearFusionAssets',$(if($Before){'PrepareFusionAssets'}else{'PrepareFusionAssetsAsync'}))
$members=foreach($member in $form.Members){
 $name=if($member -is [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax]){$member.Declaration.Variables[0].Identifier.ValueText}else{$member.Identifier.ValueText}
 if($names -contains $name){$member.ToFullString()}
}
if(@($members).Count -ne 7){throw 'Missing actual fusion preparation members'}
$prefix=@'
using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
namespace FusionScheduleProbe {
 enum PixelFormat{Format32bppArgb} enum ImageLockMode{ReadOnly}
 class BitmapData{public IntPtr Scan0;public int Stride;}
 class Bitmap:IDisposable{public int Width,Height;IntPtr pixels;public bool Disposed;public Bitmap(int w,int h){Width=w;Height=h;pixels=Marshal.AllocHGlobal(w*h*4);}public BitmapData LockBits(Rectangle r,ImageLockMode m,PixelFormat f){return new BitmapData{Scan0=pixels,Stride=Width*4};}public void UnlockBits(BitmapData data){}public void Dispose(){if(!Disposed){Disposed=true;Marshal.FreeHGlobal(pixels);}}}
 enum LabelConfidence{Low,Medium,High}enum LabelRenderMode{PreserveOriginal,Seamless}
 class OverlayLabel{public LabelConfidence Confidence=LabelConfidence.High;public bool HasSourceBounds=true;public bool Wrap;public RectangleF Bounds,SourceBounds,KeepClearBounds;public List<RectangleF> SourceLineBounds;public string Text="valid",FusionFailureReason,SemanticRole="task-body",MatchSource="exact";public LabelRenderMode RenderMode;}
 static class OverlayPaintSafety{public static bool IsSafeSeamlessGeometry(RectangleF a,RectangleF b,int w,int h){return b.Width>0&&b.Height>0&&b.Left>=0&&b.Top>=0&&b.Right<=w&&b.Bottom<=h;}}
 class ReconstructedTextSurface{public Rectangle Region;public int Ink,MaskedPixels;public float SourceHeight;public int[] Pixels;}
 static class GlyphBackgroundReconstruction{public static bool Accept;public static int Calls;public static bool TryReconstruct(int[] p,int w,int h,Rectangle r,List<Rectangle> rows,List<Rectangle> text,out ReconstructedTextSurface surface,out string reason){Calls++;surface=Accept?new ReconstructedTextSurface{Region=r}:null;reason="controlled-refusal";return Accept;}}
 static class GlyphMaskInpainting{public static bool TryReconstruct(int[] f,int w,int h,Rectangle r,List<Rectangle> rows,List<Rectangle> text,out ReconstructedTextSurface s,out string reason){s=null;reason="controlled-refusal";return false;}}
 static class TextSurfaceReconstruction{public static bool Reject;public static bool TryReconstruct(int[] p,int w,int h,Rectangle region,List<Rectangle> rows,out ReconstructedTextSurface surface,out string reason,List<Rectangle> protectedText=null){surface=new ReconstructedTextSurface{Region=region};reason="unsupported-texture";return !Reject;}}
 static class FusionBitmapRenderer{public static bool[] MakeTransparent(Bitmap b,int[] f,int w,int h,Rectangle r){var a=new bool[b.Width*b.Height];a[0]=true;return a;}public static int WantedExtra,RejectFirst;public static int RequiredSingleLineWidth(ReconstructedTextSurface surface,string text,float scale){return WantedExtra>0?surface.Region.Width+WantedExtra:0;}public static int Calls;public static bool SlowFirst,Reject;public static Bitmap TryRender(ReconstructedTextSurface surface,string text,float scale){Calls++;if(Calls==1&&SlowFirst)Thread.Sleep(100);if(RejectFirst>0){RejectFirst--;return null;}return Reject?null:new Bitmap(surface.Region.Width,surface.Region.Height);}}
 static class FusionTextSpace{public static bool Accept;public static int Calls,LastRemaining;public static bool TryExpandRight(ReconstructedTextSurface s,int[] f,int w,int h,int wanted,int maxExtra,int budget,List<Rectangle> guards,out ReconstructedTextSurface result,out string reason){Calls++;LastRemaining=budget;bool ok=Accept&&(wanted-s.Region.Width)*s.Region.Height<=budget;result=ok?new ReconstructedTextSurface{Region=new Rectangle(s.Region.X,s.Region.Y,wanted,s.Region.Height)}:null;reason="controlled-refusal";return ok;}}
 class MapleWindowInfo{public int Dpi=96;}class Tracker{public MapleWindowInfo Locked=new MapleWindowInfo();}
 static class Program{public static void LogRuntimeEvent(string a,string b){}}
 class Pump:SynchronizationContext{Queue<SendOrPostCallback> calls=new Queue<SendOrPostCallback>();Queue<object> args=new Queue<object>();public override void Post(SendOrPostCallback d,object s){calls.Enqueue(d);args.Enqueue(s);}public void Run(){while(calls.Count>0)calls.Dequeue()(args.Dequeue());}}
 public class Probe {
 int recognitionGeneration=1;bool shuttingDown=false;Tracker mapleWindows=new Tracker();Rectangle Bounds=Rectangle.Empty;RectangleF requestFocusBounds=RectangleF.Empty;
 List<RectangleF> captureTextRows=new List<RectangleF>();bool captureTextRowsOverflow=false;
'@
$call=if($Before){'p.PrepareFusionAssets(clean,Rectangle.Empty,Rows());'}else{'p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows()).GetAwaiter().GetResult();'}
$body=@'
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 static List<OverlayLabel> Rows(){var result=new List<OverlayLabel>();for(int i=0;i<3;i++)result.Add(new OverlayLabel{Bounds=new RectangleF(10,10+i*20,30,10),SourceBounds=new RectangleF(10,10+i*20,30,10)});return result;}
 static void Reset(){FusionTextSpace.Accept=false;FusionTextSpace.Calls=0;FusionTextSpace.LastRemaining=-1;FusionBitmapRenderer.WantedExtra=0;FusionBitmapRenderer.RejectFirst=0;FusionBitmapRenderer.Calls=0;FusionBitmapRenderer.SlowFirst=false;FusionBitmapRenderer.Reject=false;TextSurfaceReconstruction.Reject=false;GlyphBackgroundReconstruction.Accept=false;GlyphBackgroundReconstruction.Calls=0;}
'@
if($Before){$body += ' public static int Run(){var p=new Probe();Reset();FusionBitmapRenderer.SlowFirst=true;using(var clean=new Bitmap(100,100)){' + $call + ' Check(p.fusionAssets.Count==3,"slow first patch must not suppress all subsequent labels");}return count;}'}
else{$body+=@'
 public static int Run(){count=0;Reset();var p=new Probe();
  using(var clean=new Bitmap(100,100)){
   FusionBitmapRenderer.SlowFirst=true;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows()).GetAwaiter().GetResult();
   Check(p.fusionAssets.Count==3,"slow first patch yields rather than dropping later labels");p.ClearFusionAssets();
   Reset();var rows=Rows();foreach(var label in rows)label.Confidence=LabelConfidence.Medium;
   p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(FusionBitmapRenderer.Calls==0,"confidence threshold is preserved");
   Reset();rows=Rows();rows[1].KeepClearBounds=rows[0].SourceBounds;
   p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(p.fusionAssets.Count==2,"live values remain protected");p.ClearFusionAssets();
   Reset();TextSurfaceReconstruction.Reject=true;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows()).GetAwaiter().GetResult();Check(p.fusionAssets.Count==0,"unsupported texture remains original");
   Reset();TextSurfaceReconstruction.Reject=true;GlyphBackgroundReconstruction.Accept=true;
   p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows()).GetAwaiter().GetResult();Check(p.fusionAssets.Count==3&&GlyphBackgroundReconstruction.Calls==3,"independently validated local repair can publish after smooth repair refuses");p.ClearFusionAssets();
   Reset();TextSurfaceReconstruction.Reject=true;GlyphBackgroundReconstruction.Accept=true;rows=Rows();rows[0].Confidence=LabelConfidence.Low;
   p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(p.fusionAssets.Count==2&&GlyphBackgroundReconstruction.Calls==2,"local repair cannot bypass original confidence gate");p.ClearFusionAssets();
   Reset();FusionBitmapRenderer.Reject=true;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows()).GetAwaiter().GetResult();Check(p.fusionAssets.Count==0,"incomplete translation is never installed");
   Reset();var pump=new Pump();var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(pump);
   try{
    var old=new FusionAsset{Image=new Bitmap(10,10)};p.fusionAssets.Add(new OverlayLabel(),old);FusionBitmapRenderer.SlowFirst=true;
    Task<bool> work=p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows());
    Check(!work.IsCompleted&&p.fusionAssets.Count==1&&!old.Image.Disposed,"existing valid patches survive until complete replacement is ready");
    pump.Run();Check(work.GetAwaiter().GetResult()&&p.fusionAssets.Count==3&&old.Image==null,"complete set is installed atomically");p.ClearFusionAssets();
    Reset();FusionBitmapRenderer.SlowFirst=true;work=p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows());p.recognitionGeneration++;pump.Run();
    Check(!work.GetAwaiter().GetResult()&&p.fusionAssets.Count==0,"cancellation during yield cannot publish staged patches");
   }finally{SynchronizationContext.SetSynchronizationContext(previous);}
  }
  Reset();using(var clean=new Bitmap(1000,1000)){var rows=Rows();rows[0].SourceBounds=rows[0].Bounds=new RectangleF(10,10,600,500);rows[1].SourceBounds=rows[1].Bounds=new RectangleF(10,10,600,500);rows.RemoveAt(2);p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(FusionBitmapRenderer.Calls==1,"total 300000 source-pixel budget is unchanged");p.ClearFusionAssets();}
  Reset();using(var clean=new Bitmap(100,100)){var rows=Rows();rows.RemoveRange(1,2);FusionBitmapRenderer.RejectFirst=1;FusionBitmapRenderer.WantedExtra=20;FusionTextSpace.Accept=true;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(p.fusionAssets.Count==1&&FusionBitmapRenderer.Calls==2&&FusionTextSpace.Calls==1,"failed initial fit can retry only after independently accepted blank space");p.ClearFusionAssets();}
  Reset();using(var clean=new Bitmap(100,100)){var rows=Rows();rows.RemoveRange(1,2);rows[0].Wrap=true;FusionBitmapRenderer.Reject=true;FusionBitmapRenderer.WantedExtra=20;FusionTextSpace.Accept=true;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(p.fusionAssets.Count==0&&FusionTextSpace.Calls==0,"multiline prose cannot activate single-line widening");}
  Reset();using(var clean=new Bitmap(1000,1000)){var rows=Rows();rows.RemoveRange(1,2);rows[0].Bounds=rows[0].SourceBounds=new RectangleF(10,10,600,500);FusionBitmapRenderer.RejectFirst=1;FusionBitmapRenderer.WantedExtra=1;FusionTextSpace.Accept=true;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();Check(p.fusionAssets.Count==0&&FusionTextSpace.LastRemaining==0,"widening cannot spend beyond the shared 300000 source-pixel budget");}
  Reset();using(var clean=new Bitmap(100,100)){
   var rows=Rows();p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();
   var old=p.fusionAssets[rows[0]];int calls=FusionBitmapRenderer.Calls;
   var copies=Rows();p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,copies).GetAwaiter().GetResult();
   Check(p.fusionAssets[copies[0]]==old&&FusionBitmapRenderer.Calls==calls,"same generation content retains the confirmed patch across cloned snapshots");
   p.recognitionGeneration++;p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,Rows()).GetAwaiter().GetResult();
   Check(old.Image==null&&FusionBitmapRenderer.Calls>calls,"new generation cannot retain an earlier patch");p.ClearFusionAssets();
   Reset();rows=Rows();rows[0].RenderMode=LabelRenderMode.Seamless;rows[0].FusionFailureReason="previous";
   var pump=new Pump();var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(pump);
   try{FusionBitmapRenderer.SlowFirst=true;var pending=p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows);
    Check(!pending.IsCompleted&&rows[0].RenderMode==LabelRenderMode.Seamless&&rows[0].FusionFailureReason=="previous","preparation cannot mutate visible label state before commit");
    p.recognitionGeneration++;pump.Run();Check(!pending.GetAwaiter().GetResult()&&rows[0].FusionFailureReason=="previous","cancellation leaves visible label state intact");
   }finally{SynchronizationContext.SetSynchronizationContext(previous);}
  }
  Reset();using(var clean=new Bitmap(100,100)){
   var rows=Rows();p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();var old=p.fusionAssets[rows[0]];int calls=FusionBitmapRenderer.Calls;
   using(var different=new Bitmap(100,100)){p.PrepareFusionAssetsAsync(different,Rectangle.Empty,Rows()).GetAwaiter().GetResult();}
   Check(old.Image==null&&FusionBitmapRenderer.Calls>calls,"different frame cannot reuse patches even within the same generation");p.ClearFusionAssets();
   Reset();rows=Rows();p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();old=p.fusionAssets[rows[0]];
   var pendingRows=Rows();pendingRows.Add(new OverlayLabel{Bounds=new RectangleF(60,10,30,10),SourceBounds=new RectangleF(60,10,30,10)});
   pendingRows.Add(new OverlayLabel{Bounds=new RectangleF(60,30,30,10),SourceBounds=new RectangleF(60,30,30,10)});
   Reset();FusionBitmapRenderer.SlowFirst=true;var pump=new Pump();var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(pump);
   try{var work=p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,pendingRows);p.recognitionGeneration++;pump.Run();
    Check(!work.GetAwaiter().GetResult()&&old.Image!=null&&!old.Image.Disposed&&p.fusionAssets.Count==3,"cancelled preparation cannot dispose assets held by the committed snapshot");
   }finally{SynchronizationContext.SetSynchronizationContext(previous);}p.ClearFusionAssets();
   Reset();rows=Rows();for(int i=3;i<140;i++)rows.Add(new OverlayLabel{Bounds=new RectangleF(10,10,30,10),SourceBounds=new RectangleF(10,10,30,10)});
   p.PrepareFusionAssetsAsync(clean,Rectangle.Empty,rows).GetAwaiter().GetResult();
   Check(p.fusionAssets.Count==128,"128 accepted label ceiling is unchanged");p.ClearFusionAssets();
  }
  return count;
 }
'@}
$code=$prefix+"`n"+($members -join "`n")+"`n"+$body+"`n}}"
foreach($file in @('PanelFocusPriority.cs','FusionPatchMask.cs')){
 $policy=[IO.File]::ReadAllText((Join-Path $repoRoot ('src/'+$file))) -replace '(?m)^using .*;\r?\n','' -replace 'namespace MapleOverlay','namespace FusionScheduleProbe'
 $code+="`n"+$policy
}
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $code
$count=[FusionScheduleProbe.Probe]::Run()
if(-not $Before -and $count -ne 21){throw 'Unexpected schedule case count'}
Write-Output "Fusion preparation scheduling: $count passed; GDI reconstruction is a controlled boundary, not Windows/game validation."
