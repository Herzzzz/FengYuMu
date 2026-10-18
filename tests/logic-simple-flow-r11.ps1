param([string]$OutputSource,[switch]$GenerateOnly)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
function Method([string]$file,[string]$name){
 $tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $root ('src/'+$file)))).GetRoot()
 @($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0].ToFullString()
}
$stub=@'
using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.Globalization;using System.Linq;using System.Runtime.InteropServices;using System.Text;using System.Text.RegularExpressions;using System.Threading;using System.Threading.Tasks;
namespace MapleOverlay {
 enum PixelFormat{Format32bppArgb}enum ImageLockMode{ReadOnly,WriteOnly,ReadWrite}
 enum TextRenderingHint{AntiAliasGridFit}enum FontStyle{Regular,Bold}enum GraphicsUnit{Pixel}enum StringTrimming{None}
 [Flags]enum StringFormatFlags{None=0,NoWrap=1}
 class StringFormat:IDisposable {public static StringFormat GenericTypographic=new StringFormat();public StringFormatFlags FormatFlags;public StringTrimming Trimming;public StringFormat(){}public StringFormat(StringFormat f){}public void Dispose(){}}
 class Font:IDisposable{public float Size;public GraphicsUnit Unit;public Font(string family,float size){Size=size;}public Font(string family,float size,FontStyle style,GraphicsUnit unit){Size=size;Unit=unit;}public void Dispose(){}}
 class SolidBrush:IDisposable{public Color Color;public SolidBrush(Color c){Color=c;}public void Dispose(){}}
 static class Brushes{public static SolidBrush White=new SolidBrush(Color.White);}
 class Image:IDisposable{public virtual Size Size{get{return new Size(120,100);}}public static Image FromFile(string f){return new Bitmap(120,100);}public virtual void Dispose(){}}
 class BitmapData{public IntPtr Scan0;public int Stride;}
 class Bitmap:Image{
  public int Width,Height;public bool Disposed;IntPtr memory;
  public override Size Size{get{return new Size(Width,Height);}}
  public Bitmap(int w,int h,PixelFormat f=PixelFormat.Format32bppArgb){Width=w;Height=h;memory=Marshal.AllocHGlobal(w*h*4);Marshal.Copy(new int[w*h],0,memory,w*h);}
  public BitmapData LockBits(Rectangle r,ImageLockMode m,PixelFormat f){if(Disposed)throw new Exception("disposed bitmap");return new BitmapData{Scan0=memory,Stride=Width*4};}
  public void UnlockBits(BitmapData d){}
  public int[] Pixels(){int[] p=new int[Width*Height];Marshal.Copy(memory,p,0,p.Length);return p;}
  public void Set(int[] p){Marshal.Copy(p,0,memory,p.Length);}
  public override void Dispose(){if(!Disposed){Disposed=true;Marshal.FreeHGlobal(memory);}}
 }
 class Graphics:IDisposable{
  Bitmap target;RectangleF clip;public TextRenderingHint TextRenderingHint;public int Draws;
  public static bool Slow;public static Graphics FromImage(Bitmap b){return new Graphics{target=b,clip=new RectangleF(0,0,b.Width,b.Height)};}
  public SizeF MeasureString(string text,Font font,SizeF area,StringFormat format){int fitted,lines;return MeasureString(text,font,area,format,out fitted,out lines);}
  public SizeF MeasureString(string text,Font font,PointF origin,StringFormat format){return new SizeF(text.Length*font.Size*.6f,font.Size);}
  public SizeF MeasureString(string text,Font font,SizeF area,StringFormat format,out int fitted,out int lines){
   if(Slow){Slow=false;Thread.Sleep(65);}float cw=font.Size*.6f,ch=font.Size;
   int cols=(int)(area.Width/cw),rows=(int)(area.Height/ch);fitted=Math.Min(text.Length,Math.Max(0,cols*rows));lines=cols==0?0:(text.Length+cols-1)/cols;
   return new SizeF(Math.Min(text.Length,cols)*cw,lines*ch);
  }
  public void SetClip(RectangleF r){clip=r;}public void ResetClip(){clip=new RectangleF(0,0,target.Width,target.Height);}
  public void DrawString(string t,Font f,SolidBrush brush,RectangleF area,StringFormat format){int[] p=target.Pixels();int cols=Math.Max(1,(int)(area.Width/(f.Size*.6f)));for(int i=0;i<t.Length;i++){int x=(int)area.X+(i%cols)*(int)(f.Size*.6f),y=(int)area.Y+(i/cols)*(int)f.Size;if(clip.Contains(x,y)&&x>=0&&x<target.Width&&y>=0&&y<target.Height)p[y*target.Width+x]=brush.Color.ToArgb();}target.Set(p);}
  public void DrawString(string t,Font f,SolidBrush brush,PointF origin,StringFormat format){DrawString(t,f,brush,new RectangleF(origin.X,origin.Y,100000,fontHeight(f)),format);}
  static float fontHeight(Font f){return f.Size*1.4f;}
  public void DrawString(string t,Font f,SolidBrush b,float x,float y){DrawString(t,f,b,new RectangleF(x,y,80,20),StringFormat.GenericTypographic);}
  public void DrawImageUnscaled(Image image,int x,int y){DrawImageUnscaled((Bitmap)image,new Point(x,y));}
  public void DrawImageUnscaled(Bitmap b,Point at){Draws++;int[] a=b.Pixels(),p=target.Pixels();for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if((uint)a[y*b.Width+x]>>24!=0&&x+at.X>=0&&x+at.X<target.Width&&y+at.Y>=0&&y+at.Y<target.Height)p[(y+at.Y)*target.Width+x+at.X]=a[y*b.Width+x];target.Set(p);}
  public void Clear(Color c){target.Set(Enumerable.Repeat(c.ToArgb(),target.Width*target.Height).ToArray());}public void Dispose(){}
 }
 enum LabelConfidence{Low,Medium,High}enum LabelRenderMode{CardFallback,Seamless,PreserveOriginal}
 class OcrWord{public string Text;public RectangleF BoundingRect;}
 class OcrLine{public string Text;public List<OcrWord> Words=new List<OcrWord>();}
 static class TranslationStore{public static string Normalize(string s){return (s??"").Trim().ToLowerInvariant();}}
 static class QuestDetailGeometry{public static bool IsSummaryKey(string s){return s=="quest summary"||s=="questsummary";}}
 static class QuestObjectiveSurface{public static Match Counter(string s){return Regex.Match(s??"",@"[0-9IlOo]+(?:\s*/\s*|\s+)[0-9IlOo]+\s*$");}}
 class ItemTextPart{public OverlayLabel Label;public string Status="",Reason="";}
 class ItemPanelPlan{public List<ItemTextPart> Parts=new List<ItemTextPart>();public int Prepared,Painted,Preserved;public void AddPreservedReason(string reason){}public string Describe(){return "fixture";}}
 class OverlayLabel{public RectangleF Bounds,SourceBounds,KeepClearBounds,PanelBounds;public List<RectangleF> SourceLineBounds;public QuestJournalDocument Journal;public EquipmentFieldPlan EquipmentField;public EquipmentPanelPlan EquipmentPanel;public SkillPanelPlan SkillPanel;public SkillSectionPlan SkillSection;public ItemPanelPlan ItemPanel;public ItemTextPart ItemTextPart;public float LayoutFontSize,LayoutMinimumFontSize;public string LayoutFontFamily="";public bool Wrap,HasSourceBounds=true,PaintedLastPass;public string MatchSource="";public string Text="中文",SemanticRole="task-name",FusionFailureReason="";public LabelConfidence Confidence=LabelConfidence.High;public LabelRenderMode RenderMode;}
 class SkillSectionPlan{public string Role="",Status="",PreservedReason="",HeaderStatus="",EffectStatus="";public float PreparedFontSize,PaintedFontSize;public OverlayLabel Label;}
 class SkillPanelPlan{public int Prepared,Painted,Preserved;public List<SkillSectionPlan> Sections=new List<SkillSectionPlan>();public void AddPreservedReason(string r){}public string DescribePaint(){return "";}}
 static class OverlayPaintSafety{
'@
$stub+=(Method 'MapleOverlay.cs' 'IsFinite')+(Method 'MapleOverlay.cs' 'IsFiniteRect')+(Method 'MapleOverlay.cs' 'IsSafeSeamlessGeometry')+@'
 }
 static class Program{public static bool Benchmark=false;public static string BenchmarkImagePath="",BenchmarkText="";public static List<string> Logs=new List<string>();public static void LogRuntimeEvent(string n,string s){Logs.Add(n+" "+s);}}
 class OcrResult{public string Text="known source";public List<int> Lines=new List<int>{1,2};}
 enum LabelBuildScope{Discovery}
 class WindowCaptureFrame:IDisposable{public Bitmap Bitmap;public Rectangle Bounds;public void Dispose(){Bitmap.Dispose();}}
 class Tray{public string Text;}class Timer{public int Interval;}class Primary{public string EngineLabel="Windows";}
 static class ContinuousTranslationPolicy{public static int NextInterval(bool a,int b,int c){return 260;}}
 partial class OverlayForm{
  public int recognitionGeneration=1;public bool shuttingDown=false,visibleTranslation,firstPaintRecorded,processing,IsDisposed,Disposing;
  public Rectangle Bounds=Rectangle.Empty,gameBounds=new Rectangle(0,0,120,100),captureBounds,ClientRectangle=new Rectangle(0,0,120,100);
  public RectangleF requestFocusBounds;public bool requestFocusIsTooltip,captureTextRowsOverflow,continuousTranslationEnabled=true;
  public List<RectangleF> captureTextRows=new List<RectangleF>();public List<OverlayLabel> labels=new List<OverlayLabel>(),earlyPaintedLabels=new List<OverlayLabel>();
  public List<int> displayedScene=new List<int>(),requestQuestMemoryIds=new List<int>();public int displayedSceneRevision,lastPaintedTranslationCount,continuousTranslationFailures,continuousTranslationMisses;
  public long benchmarkConvergedMs,benchmarkFirstVisibleMs;public Stopwatch visibleRequestWatch;public CancellationTokenSource panelAiCancellation;
  Primary primaryOcr=new Primary();Tray tray=new Tray();Timer continuousTranslationTimer=new Timer();public int Captures,OcrCalls,MatchCalls,Publishes,ComposeCalls;public TaskCompletionSource<OcrResult> OcrGate;
  public List<OverlayLabel> MatchFixture=Rows();
   void SetJournalPagingShortcuts(bool active){}void CancelPanelAiRequest(){}void SetInteractionStatus(string s){}void Invalidate(){}void Update(){}void ShowWindowScanIndicator(){}void HideWindowScanIndicator(){}
   void TraceItemStage(OverlayLabel label,int generation,string stage,string reason){}void MarkItemPlansPainted(string phase,bool readyIsPainted){}
  IntPtr GetForegroundWindow(){return new IntPtr(1);}bool ResolveCaptureBounds(out Rectangle r){r=new Rectangle(0,0,120,100);return true;}
  bool RecognitionWasCancelled(bool a,int id,int g){return shuttingDown||g!=recognitionGeneration;}
  void ClearFusionAssets(){ClearSimpleFusion();}
  Task<WindowCaptureFrame> CaptureSceneFrameWithoutOverlayAsync(CancellationToken token){Captures++;var b=Frame();return Task.FromResult(new WindowCaptureFrame{Bitmap=b,Bounds=new Rectangle(0,0,b.Width,b.Height)});}
  Bitmap PrepareForOcr(Bitmap b,out float scale,bool contrast,float max){scale=1;var copy=new Bitmap(b.Width,b.Height);copy.Set(b.Pixels());return copy;}
  Task<OcrResult> RecognizeAsync(Bitmap b,CancellationToken token){OcrCalls++;return OcrGate==null?Task.FromResult(new OcrResult()):OcrGate.Task;}
  List<OverlayLabel> BuildLabels(OcrResult r,float s,Bitmap b,LabelBuildScope scope){MatchCalls++;return MatchFixture;}
  void MergeAdjacentLongLabels(List<OverlayLabel> a){}void MergeOverlappingSameTextLabels(List<OverlayLabel> a){}void MergeSameTextLabels(List<OverlayLabel> a){}void RemoveContainedLabels(List<OverlayLabel> a){}
  Task<bool> RememberDisplayedSceneAsync(Bitmap b,Rectangle r,List<OverlayLabel> a){ComposeCalls++;return PrepareSimpleFusionAsync(b,r,a);}
  Task RetireChangedSceneAsync(bool immediate){return Task.FromResult(0);}
  void ShowCurrentTranslations(){if(visibleTranslation){Publishes++;using(var b=new Bitmap(120,100))using(var g=Graphics.FromImage(b))PaintSimpleFusion(g);}}
  void WriteBenchmarkResult(Stopwatch s,long a,long b,long c,long d,long e,long f,long g,string h,string i,string j){}
 }
}
'@
$source=$stub
foreach($file in @('FusionRowEvidence.cs','FusionPatchMask.cs','TextSurfaceReconstruction.cs','SimpleBackgroundFill.cs','EquipmentFieldGrammar.cs','EquipmentPanelLayout.cs','QuestJournalGeometry.cs','QuestJournalComposition.cs','SimpleFusionComposition.cs','SimpleTranslationFlow.cs')){
 $source+="`n"+([IO.File]::ReadAllText((Join-Path $root ('src/'+$file))) -replace '(?m)^using .*;\r?\n','')
}
$source+="`nnamespace MapleOverlay { static class FusionBitmapRenderer {"+(Method 'FusionBitmapRenderer.cs' 'MakeTransparent')+"`n"
# Both overloads are production code; select the six-parameter overload explicitly.
$tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $root 'src/FusionBitmapRenderer.cs'))).GetRoot()
$source+=@($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'MakeTransparent' -and $_.ParameterList.Parameters.Count -eq 6})[0].ToFullString()+"}}"
$source+=@'
namespace MapleOverlay {
 class Pump:SynchronizationContext{Queue<SendOrPostCallback> calls=new Queue<SendOrPostCallback>();Queue<object> args=new Queue<object>();public override void Post(SendOrPostCallback d,object a){calls.Enqueue(d);args.Enqueue(a);}public void Run(){while(calls.Count>0)calls.Dequeue()(args.Dequeue());}}
 partial class OverlayForm{
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
  static List<OverlayLabel> Rows(){return new List<OverlayLabel>{Label(10,20),Label(10,42)};}
  static OverlayLabel Label(int x,int y){return new OverlayLabel{Bounds=new RectangleF(x,y,40,12),SourceBounds=new RectangleF(x,y,40,12)};}
  static Bitmap Frame(bool textured=false){var b=new Bitmap(120,100);int[] p=Enumerable.Repeat(unchecked((int)0xff303030),120*100).ToArray();if(textured)for(int y=0;y<100;y++)for(int x=0;x<120;x++)p[y*120+x]=unchecked((int)(x%2==0?0xff202020:0xff909090));for(int y=20;y<54;y++)if(y<32||y>=42)for(int x=12;x<48;x+=4)p[y*120+x]=unchecked((int)0xffeeeeee);b.Set(p);return b;}
  static bool Prepare(OverlayForm p,Bitmap b,List<OverlayLabel> rows){return p.PrepareSimpleFusionAsync(b,Rectangle.Empty,rows).GetAwaiter().GetResult();}
  public static int Run(){count=0;
   var p=new OverlayForm();using(var b=Frame()){
    var rows=Rows();Check(Prepare(p,b,rows)&&p.simpleReady.Count==2,"two confirmed translations are prepared in a single composition");
    int[] pixels=p.simpleLayer.Pixels();Check((uint)pixels[20*120+10]>>24==255&&(uint)pixels[42*120+10]>>24==255,"both Chinese glyph positions survive all background writes");
    Check(pixels[0]==0&&pixels[35*120+10]==0,"unchanged background and inter-row gap are transparent");
    p.labels=rows;using(var dst=new Bitmap(120,100))using(var g=Graphics.FromImage(dst)){
     Check(p.PaintSimpleFusion(g)&&g.Draws==1&&p.lastPaintedTranslationCount==2,"one draw publishes all accepted translations without per-label pixel veto");
     Check(rows.All(r=>r.PaintedLastPass),"display counters refer to accepted plan labels");
     p.recognitionGeneration++;p.PaintSimpleFusion(g);Check(g.Draws==1&&p.lastPaintedTranslationCount==0,"stale generation cannot be drawn");
    }p.ClearSimpleFusion();
    rows=Rows();rows[0].Confidence=LabelConfidence.Low;Prepare(p,b,rows);Check(p.simpleReady.Count==1&&rows[0].FusionFailureReason=="unverified-dictionary-match","unverified fuzzy translation cannot erase source");p.ClearSimpleFusion();
    rows=Rows();rows[1].KeepClearBounds=rows[0].SourceBounds;Prepare(p,b,rows);Check(p.simpleReady.Count==1,"dynamic counter inside source remains protected");p.ClearSimpleFusion();
    rows=Rows();rows.Add(Label(10,20));Prepare(p,b,rows);Check(p.simpleReady.Count==2,"same source cannot be replaced twice");p.ClearSimpleFusion();
    rows=Rows();rows[0].SourceBounds=new RectangleF(float.NaN,20,40,12);Prepare(p,b,rows);Check(p.simpleReady.Count==1,"nonfinite source geometry is refused");p.ClearSimpleFusion();
    rows=Rows();rows[0].SourceBounds=new RectangleF(-5,20,40,12);Prepare(p,b,rows);Check(p.simpleReady.Count==1,"outside-frame source geometry is refused");p.ClearSimpleFusion();
    rows=Rows();foreach(var row in rows)row.Confidence=LabelConfidence.Low;Prepare(p,b,rows);Check(p.simpleLayer==null&&p.simpleReady.Count==0,"zero accepted translations cannot install an opaque frozen screenshot");
    rows=Rows();rows[0].Text=new string('字',4097);Prepare(p,b,rows);Check(p.simpleReady.Count==1,"overlong text cannot allocate an unbounded replacement");p.ClearSimpleFusion();
    rows=Rows();rows[0].Text=new string('字',100);Prepare(p,b,rows);Check(!p.simpleReady.Contains(rows[0])&&rows[0].FusionFailureReason=="complete-text-does-not-fit","text is never silently truncated to increase display counts");p.ClearSimpleFusion();
    rows=Rows();rows[0].SourceBounds=new RectangleF(10,20,40,12);rows[0].KeepClearBounds=new RectangleF(90,20,5,12);Prepare(p,b,rows);Check(p.simpleReady.Contains(rows[0])&&p.simpleLayer.Pixels()[20*120+90]==0,"nearby live value remains transparent after bounded text layout");p.ClearSimpleFusion();
   }
   using(var b=Frame()){
    p=new OverlayForm();var rows=Rows();rows[0].Bounds=new RectangleF(70,20,40,12);Prepare(p,b,rows);Check(p.simpleReady.Count==1&&rows[0].FusionFailureReason=="invalid-source-geometry","misaligned erase and layout boxes cannot recolour unrelated game text");p.ClearSimpleFusion();
    rows=Rows();rows[0].SourceLineBounds=new List<RectangleF>{new RectangleF(70,20,40,12)};Prepare(p,b,rows);Check(p.simpleReady.Count==1&&rows[0].FusionFailureReason=="invalid-source-rows","invalid line evidence cannot fall back to erasing the entire source union");p.ClearSimpleFusion();
    rows=Rows();rows[0].PanelBounds=new RectangleF(10,20,15,12);Prepare(p,b,rows);Check(p.simpleReady.Count==1&&rows[0].FusionFailureReason=="source-outside-panel","source must stay inside its measured panel");p.ClearSimpleFusion();
    var raw=b.Pixels();var area=new Rectangle(0,0,40,12);for(int y=0;y<12;y++)for(int x=2;x<38;x+=4)raw[y*120+x]=unchecked((int)0xffeeeeee);ReconstructedTextSurface rebuilt;
    Check(SimpleBackgroundFill.TryReconstruct(raw,120,100,area,new List<Rectangle>{area},null,out rebuilt)&&rebuilt.Region.Left==0&&rebuilt.Region.Top==0,"local fill handles measured text at the frame edge without outside sampling");
    var multi=new Rectangle(10,20,40,34);var multiRows=new List<Rectangle>{new Rectangle(10,20,40,12),new Rectangle(10,42,40,12)};
    SimpleBackgroundFill.TryReconstruct(raw,120,100,multi,multiRows,null,out rebuilt);int gap=(36-rebuilt.Region.Y)*rebuilt.Region.Width+15-rebuilt.Region.X;
    Check(rebuilt.Pixels[gap]==raw[36*120+15],"local multiline fill preserves original pixels between OCR rows");
    Check(!SimpleBackgroundFill.TryReconstruct(raw,120,100,new Rectangle(-1,0,40,12),new List<Rectangle>{area},null,out rebuilt),"background fallback rejects outside source geometry");
   }
   using(var b=Frame(true)){
    var rows=Rows();p=new OverlayForm();Program.Logs.Clear();Prepare(p,b,rows);
    Check(p.simpleReady.Count==2&&Program.Logs.Any(x=>x.Contains("background=local-fill")),"unsupported texture uses local background fill and retains confirmed translations");
    var output=p.simpleLayer.Pixels();Check(output[0]==0&&output[20*120+70]==0,"approximate fill stays within measured text and translated glyphs");p.ClearSimpleFusion();
   }
   using(var b=Frame()){
    p=new OverlayForm();var rows=Rows();rows.RemoveAt(1);Prepare(p,b,rows);var previousLayer=p.simpleLayer;
    var pump=new Pump();var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(pump);
    try{Graphics.Slow=true;var work=p.PrepareSimpleFusionAsync(b,Rectangle.Empty,Rows());Check(!work.IsCompleted&&p.simpleLayer==previousLayer,"pending composition cannot mutate the committed bitmap");p.recognitionGeneration++;pump.Run();Check(!work.GetAwaiter().GetResult()&&p.simpleLayer==previousLayer&&!previousLayer.Disposed,"cancelled composition cannot replace or dispose the committed bitmap");}
    finally{SynchronizationContext.SetSynchronizationContext(previous);p.ClearSimpleFusion();}
   }
   p=new OverlayForm();p.ShowSimpleTranslationAsync(false,1).GetAwaiter().GetResult();
   Check(p.Captures==1&&p.OcrCalls==1&&p.MatchCalls==1,"real request method captures, recognizes and matches exactly once");
   Check(p.ComposeCalls==1&&p.Publishes==1&&p.lastPaintedTranslationCount==2,"real request method composes and publishes exactly once");
   Check(!p.processing&&p.panelAiCancellation==null,"successful request releases busy and cancellation state");p.ClearSimpleFusion();
   p=new OverlayForm();p.OcrGate=new TaskCompletionSource<OcrResult>();var pending=p.ShowSimpleTranslationAsync(false,1);p.recognitionGeneration++;p.OcrGate.SetResult(new OcrResult());pending.GetAwaiter().GetResult();
   Check(p.MatchCalls==0&&p.ComposeCalls==0&&p.Publishes==0,"late OCR completion cannot match or publish after F8 cancellation");
   Check(!p.processing&&p.panelAiCancellation==null,"cancelled request releases busy and cancellation state");
   p=new OverlayForm();p.OcrGate=new TaskCompletionSource<OcrResult>();pending=p.ShowSimpleTranslationAsync(false,1);p.OcrGate.SetException(new InvalidOperationException("fixture"));pending.GetAwaiter().GetResult();Check(!p.processing&&!p.visibleTranslation&&p.Publishes==0,"OCR failure cannot leave stale output or busy state");
   p=new OverlayForm();p.processing=true;p.ShowSimpleTranslationAsync(false,1).GetAwaiter().GetResult();Check(p.OcrCalls==0&&p.Captures==0,"busy request cannot launch concurrent recognition");
   p=new OverlayForm();p.continuousTranslationEnabled=false;p.ShowSimpleTranslationAsync(true,0).GetAwaiter().GetResult();Check(p.Publishes==0&&p.simpleLayer==null,"disabled automatic mode cannot publish its late composition");
   Console.WriteLine("Simple flow: "+count+" checks passed; actual request/composition/background methods; GDI font/capture boundaries controlled; Windows game unverified.");return count;
  }
 }
 public static class Runner{public static int Run(){return OverlayForm.Run();}}
}
'@
if($OutputSource){[IO.File]::WriteAllText($OutputSource,$source)}
if($GenerateOnly){return}
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[MapleOverlay.Runner]::Run()
