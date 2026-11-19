$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$generated = Join-Path $env:TEMP ('quest-font-layout-' + [Guid]::NewGuid().ToString('N') + '.cs')
& (Join-Path $PSScriptRoot 'logic-simple-flow-r11.ps1') -OutputSource $generated -GenerateOnly
$source = [IO.File]::ReadAllText($generated)
Remove-Item -LiteralPath $generated -Force

$source += @'
namespace MapleOverlay {
 partial class OverlayForm {
  static int fontCheckCount;
  static void FontCheck(bool ok,string name){if(!ok)throw new Exception(name);fontCheckCount++;Console.WriteLine("PASS: "+name);}
  static Bitmap FontFrame(out int markerFirst,out int markerRight,out int markerSummary,out int markerNeighbour){
   int w=1000,h=700;var b=new Bitmap(w,h);int[] p=Enumerable.Repeat(unchecked((int)0xff203030),w*h).ToArray();
   // Pale paper viewport and a distinct summary/objective card above it.
   for(int y=300;y<590;y++)for(int x=400;x<810;x++)p[y*w+x]=unchecked((int)0xfffafafa);
   for(int y=150;y<290;y++)for(int x=400;x<810;x++)p[y*w+x]=unchecked((int)0xffe1ded3);
   markerFirst=315*w+450;markerRight=330*w+805;markerSummary=160*w+430;markerNeighbour=160*w+820;
   p[markerFirst]=unchecked((int)0xff00ff00);p[markerRight]=unchecked((int)0xff00ffff);
   p[markerSummary]=unchecked((int)0xffff00ff);p[markerNeighbour]=unchecked((int)0xffffff00);
   b.Set(p);return b;
  }
  static OverlayLabel FontJournal(Rectangle area,string title,string body,float sourceHeight){
   return new OverlayLabel{Bounds=area,SourceBounds=area,PanelBounds=area,HasSourceBounds=true,
    Text=(String.IsNullOrEmpty(title)?"":title+"\n\n")+body,Wrap=true,Confidence=LabelConfidence.High,
    SemanticRole="task-journal",Journal=new QuestJournalDocument{Title=title,Body=body,
     Background=Color.FromArgb(250,248,241),FontHeight=sourceHeight}};
  }
  static OverlayLabel FontSummary(Rectangle area){
   return new OverlayLabel{Bounds=area,SourceBounds=area,PanelBounds=area,HasSourceBounds=true,
    Text="任务概要",Wrap=false,Confidence=LabelConfidence.High,SemanticRole="interface"};
  }
  static OverlayForm FontForm(Rectangle frame){
   var p=new OverlayForm();p.Bounds=Rectangle.Empty;p.gameBounds=p.captureBounds=frame;
   p.ClientRectangle=frame;p.recognitionGeneration=1;p.visibleTranslation=true;
   p.visibleRequestWatch=Stopwatch.StartNew();return p;
  }
  static List<OverlayLabel> FontRows(string text,int count){
   var rows=new List<OverlayLabel>();for(int i=0;i<count;i++)rows.Add(FontJournal(new Rectangle(400,320,400,260),i==0?"任务":"",text,18));return rows;
  }
  static string LongBody(int paragraphs){var b=new StringBuilder();for(int i=0;i<paragraphs;i++){if(i>0)b.Append("\n\n");b.Append("第").Append(i+1).Append("段：这是一段用于验证任务正文排版、自动换行和分页的完整中文内容，不能静默截断。");}return b.ToString();}
  static bool HasLoneSurrogate(string value){
   for(int i=0;i<value.Length;i++){
    if(Char.IsHighSurrogate(value[i])){if(i+1>=value.Length||!Char.IsLowSurrogate(value[i+1]))return true;i++;}
    else if(Char.IsLowSurrogate(value[i]))return true;
   }
   return false;
  }
  static void FontTimeout(Action action,string name){
   Exception error=null;
   Thread thread=new Thread(delegate(){try{action();}catch(Exception e){error=e;}});
   thread.IsBackground=true;thread.Start();
   if(!thread.Join(8000))throw new Exception(name+" timed out");
   if(error!=null)throw new Exception(name,error);
  }
  static void EmojiCase(string name,string title,string body,bool expectPages){
   FontTimeout(delegate(){
    using(var frame=new Bitmap(1000,700)){
     var p=FontForm(new Rectangle(0,0,1000,700));
     var journal=FontJournal(new Rectangle(400,300,410,290),title,body,20);
     FontCheck(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,new List<OverlayLabel>{journal}).GetAwaiter().GetResult(),name+" composes within 8s");
     var view=p.journalPageView;
     FontCheck(view!=null,name+" has a plan");
     FontCheck(String.Concat(view.Pages)==body,name+" pagination preserves every character");
     FontCheck(!HasLoneSurrogate(String.Concat(view.Pages)),name+" pages contain no lone surrogates");
     if(expectPages)FontCheck(view.Pages.Count>1,name+" long supplementary body paginates");
     if(!String.IsNullOrEmpty(title)){
      FontCheck(String.Concat(view.TitleLines)==title,name+" title preserves every character");
      FontCheck(!HasLoneSurrogate(String.Concat(view.TitleLines)),name+" title contains no lone surrogates");
     }
     p.ClearSimpleFusion();
    }
   },name);
  }
  public static int RunFontLayout(){
   fontCheckCount=0;
   int first,right,summary,neighbour;
   using(var frame=FontFrame(out first,out right,out summary,out neighbour)){
    var p=FontForm(new Rectangle(0,0,frame.Width,frame.Height));
    var journal=FontJournal(new Rectangle(400,300,410,290),"任务标题","这是一段较短的完整任务正文。",20);
    var summaryLabel=FontSummary(new Rectangle(410,154,180,22));
    var labels=new List<OverlayLabel>{journal,summaryLabel};
    FontCheck(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult(),"journal + summary compose in one pass");
    FontCheck(p.simpleReady.Contains(journal)&&p.simpleReady.Contains(summaryLabel),"journal and adjacent summary label are both published");
    var view=p.journalPageView;
    FontCheck(view!=null&&view.FontUnit==GraphicsUnit.Pixel,"journal records a real pixel font unit");
    FontCheck(view.FontSize>=15.0f,"journal body base size is readable, not 8px");
    FontCheck(view.TitleSize>view.FontSize,"title/body hierarchy is stable");
    FontCheck(view.Layout.MaxLineWidth<=view.Content.Width+0.01f,"every measured line fits the content width");
    FontCheck(view.Layout.Region.Right<=frame.Width&&view.Layout.Body.Right<=view.Layout.Region.Right,"content stays inside the replacement region");
    int[] px=p.simpleLayer.Pixels();
    FontCheck(px[first]!=unchecked((int)0xff00ff00)&&px[right]!=unchecked((int)0xff00ffff),"first-line and right-edge English markers are fully replaced");
    FontCheck(px[summary]!=unchecked((int)0xffff00ff),"adjacent Quest Summary marker is replaced by its own label, not left as residue");
    FontCheck(px[neighbour]==0,"adjacent panel pixels outside the journal region remain transparent");
    FontCheck(p.Captures==0&&p.OcrCalls==0,"journal layout adds no capture or OCR round");
    // Repeated F8 must be deterministic and must not duplicate pages/labels.
    FontCheck(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult(),"repeated F8 composition succeeds");
    FontCheck(p.journalPageView.Pages.Count==view.Pages.Count&&p.journalPageView.FontSize==view.FontSize,"repeated F8 keeps the same plan metrics");
    p.ClearSimpleFusion();
   }
   using(var frame=new Bitmap(1000,700)){
    int[] raw=Enumerable.Repeat(unchecked((int)0xff203030),700000).ToArray();for(int y=300;y<590;y++)for(int x=400;x<810;x++)raw[y*1000+x]=unchecked((int)0xfffafafa);frame.Set(raw);
    var p=FontForm(new Rectangle(0,0,1000,700));
    var body=LongBody(30);var journal=FontJournal(new Rectangle(400,300,410,290),"长任务",body,20);
    FontCheck(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,new List<OverlayLabel>{journal}).GetAwaiter().GetResult(),"long body composes through pagination");
    var view=p.journalPageView;
    FontCheck(view.Pages.Count>1,"long body produces multiple pages instead of shrinking or truncating");
    FontCheck(String.Concat(view.Pages)==body,"pagination preserves every character in order");
    FontCheck(view.PageLines.Count==view.Pages.Count&&view.PageLines[0].Count>0,"each page has measured draw lines");
    int before=view.Page;FontCheck(p.ChangeQuestJournalPage(1)&&view.Page==before+1,"page turn uses the cached layout without OCR");
    FontCheck(p.Captures==0&&p.OcrCalls==0,"page turn launches no capture or OCR");
    p.ClearSimpleFusion();
   }
   // Scrolled view: no summary and no known title; body is the complete replacement.
   using(var frame=FontFrame(out first,out right,out summary,out neighbour)){
    var p=FontForm(new Rectangle(0,0,frame.Width,frame.Height));
    var journal=FontJournal(new Rectangle(400,300,410,290),"",LongBody(2),20);
    FontCheck(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,new List<OverlayLabel>{journal}).GetAwaiter().GetResult(),"scrolled/heading-less body composes");
    FontCheck(p.journalPageView.TitleLines.Count==0&&p.journalPageView.FontSize>=15,"scrolled body keeps the shared body base size without inventing a title");
    p.ClearSimpleFusion();
   }
   // QUEST-R2: every wrap is bounded by an 8s watchdog. The old boundary search
   // could loop forever on a high surrogate ("A😀"); these cases must finish and
   // every page/title must preserve complete UTF-16 code points.
   EmojiCase("emoji A😀 title and body","A\uD83D\uDE00","A\uD83D\uDE00",false);
   EmojiCase("CJK long pagination","",LongBody(30),true);
   var supplementary=new StringBuilder();
   for(int i=0;i<30;i++){
    if(i>0)supplementary.Append("\n\n");
    supplementary.Append("第").Append(i+1).Append("段：汉字😀🚀𠀋与补充平面字符必须完整换行，不能丢字，也不能把代理对拆开。");
   }
   EmojiCase("supplementary title and long pagination","标题😀𠀋",supplementary.ToString(),true);
   // Scale matrix: 100/125/150/200%.
   float previous=0.0f;
   foreach(float factor in new float[]{1.0f,1.25f,1.5f,2.0f}){
    int w=(int)(1000*factor),h=(int)(700*factor);
    using(var frame=new Bitmap(w,h)){
     int[] raw=Enumerable.Repeat(unchecked((int)0xff203030),w*h).ToArray();
     for(int y=(int)(300*factor);y<(int)(590*factor);y++)for(int x=(int)(400*factor);x<(int)(810*factor);x++)raw[y*w+x]=unchecked((int)0xfffafafa);
     frame.Set(raw);
     var p=FontForm(new Rectangle(0,0,w,h));
     var journal=FontJournal(new Rectangle((int)(400*factor),(int)(300*factor),(int)(410*factor),(int)(290*factor)),"缩放任务",LongBody(3),20*factor);
     FontCheck(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,new List<OverlayLabel>{journal}).GetAwaiter().GetResult(),"scale "+factor.ToString("0.##")+" composes");
     var view=p.journalPageView;
     FontCheck(view.FontUnit==GraphicsUnit.Pixel&&view.FontSize>=15.0f,"scale "+factor.ToString("0.##")+" uses a readable pixel base size");
     FontCheck(view.FontSize>=previous,"scale "+factor.ToString("0.##")+" does not shrink the body base size");
     previous=view.FontSize;
     p.ClearSimpleFusion();
    }
   }
   Console.WriteLine("Quest font/layout composition: "+fontCheckCount+" checks passed; generated-source substitute Font/Graphics harness, not native GDI acceptance; Windows game unverified.");return fontCheckCount;
  }
 }
 public static class FontLayoutRunner{public static int Run(){return OverlayForm.RunFontLayout();}}
}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
$count = [MapleOverlay.FontLayoutRunner]::Run()
if ($count -lt 20) { throw "unexpected check count: $count" }
