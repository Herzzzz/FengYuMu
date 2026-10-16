$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot
$work='/workspace/work/release-v4.0'
$generated=Join-Path $work ('JournalBase-'+[Guid]::NewGuid().ToString('N')+'.cs')
& (Join-Path $PSScriptRoot 'logic-simple-flow-r11.ps1') -OutputSource $generated -GenerateOnly
$source=[IO.File]::ReadAllText($generated);Remove-Item -LiteralPath $generated
$main=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/MapleOverlay.cs'))).GetRoot()
function M([string]$name){@($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0].ToFullString()}
$source='using System.Text;using System.Text.RegularExpressions;using System.Reflection;'+$source
$source=$source.Replace('public QuestJournalDocument Journal;public bool HasSourceBounds=true,PaintedLastPass;','public QuestJournalDocument Journal;public bool Wrap;public QuestJournalDocument UnusedJournal;public bool HasSourceBounds=true,PaintedLastPass;')
$catalog=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/QuestJournalCatalog.cs'))).GetRoot()
$record=@($catalog.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'QuestJournalRecord'})[0].ToFullString()
$entry=@($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'TranslationEntry'})[0].ToFullString()
$panel=@($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'OcrPanelInfo'})[0].ToFullString()
$source+='namespace MapleOverlay {'+$entry+$panel+$record+@'
 class OcrWord{public string Text;public RectangleF BoundingRect;}
 class OcrLine{public string Text;public List<OcrWord> Words=new List<OcrWord>();}
 class TranslationStore {
  object store;Dictionary<QuestJournalRecord,object> originals=new Dictionary<QuestJournalRecord,object>();static Type actual;static readonly BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  public TranslationStore(object s){store=s;actual=s.GetType();}
  object Call(string method,params object[] args){return actual.GetMethod(method,flags).Invoke(store,args);}
  public static string Normalize(string s){return (string)actual.GetMethod("Normalize",flags).Invoke(null,new object[]{s});}
  public bool HasJournalTitle(string s){return (bool)Call("HasJournalTitle",s);}
  public string ResolveJournalStage(string s){return (string)Call("ResolveJournalStage",s);}
  public string ResolveJournalCaption(string s){return (string)Call("ResolveJournalCaption",s);}
  public string JournalTitleForTranslation(string s){return (string)Call("JournalTitleForTranslation",s);}
  QuestJournalRecord CopyRecord(object raw){if(raw==null)return null;var record=new QuestJournalRecord();foreach(var f in typeof(QuestJournalRecord).GetFields(flags)){var rf=raw.GetType().GetField(f.Name,flags);if(rf!=null)f.SetValue(record,rf.GetValue(raw));}originals[record]=raw;return record;}
  public QuestJournalRecord SelectJournal(string title,string observed,out string reason){object[] args={title,observed,""};object raw=actual.GetMethod("SelectJournal",flags).Invoke(store,args);reason=(string)args[2];return CopyRecord(raw);}
  public QuestJournalRecord SelectJournalForProse(string text,out string reason){object[] args={text,""};object raw=actual.GetMethod("SelectJournalForProse",flags).Invoke(store,args);reason=(string)args[1];return CopyRecord(raw);}
  public bool JournalMemoryMatches(QuestJournalRecord record,string text){return (bool)Call("JournalMemoryMatches",originals[record],text);}
  public bool TryTranslateSkillEffect(string text,string id,out string chinese){object[] args={text,id,""};bool ok=(bool)actual.GetMethod("TryTranslateSkillEffect",flags).Invoke(store,args);chinese=(string)args[2];return ok;}
  public string TranslateJobName(string s){return (string)Call("TranslateJobName",s);}
  public string ResolveSkillTitle(string s){return (string)Call("ResolveSkillTitle",s);}
  public string DetectSkillContentId(string s){return (string)Call("DetectSkillContentId",s);}
  public bool TryTranslateSkillOverview(string text,string id,out string chinese){object[] args={text,id,""};bool ok=(bool)actual.GetMethod("TryTranslateSkillOverview",flags).Invoke(store,args);chinese=(string)args[2];return ok;}
  public bool TryGetSkillOverviewBody(string id,out string chinese){object[] args={id,""};bool ok=(bool)actual.GetMethod("TryGetSkillOverviewBody",flags).Invoke(store,args);chinese=(string)args[1];return ok;}
  public TranslationEntry ResolveObjectiveName(string name){object raw=Call("ResolveObjectiveName",name);if(raw==null)return null;var entry=new TranslationEntry();foreach(var f in typeof(TranslationEntry).GetFields(flags)){var rf=raw.GetType().GetField(f.Name,flags);if(rf!=null)f.SetValue(entry,rf.GetValue(raw));}return entry;}
 }
 partial class OverlayForm {
  TranslationStore translations;
'@
foreach($name in @('ToOverlayX','ToOverlayY','GetOcrLineBounds','BuildVisualRows','CombineVisualRow','GetCombinedLineBounds','SkillLevelNumber','ExtractStructuredValue','LastNumber','SkillStructuredPrefixLength','StripSkillStructuredPrefix','SkillStructuredBodyStart','GetOcrTextSpanBounds','ToOverlayLineBounds')){$source+=M $name}
foreach($node in @($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'AddDetailBlockLabelAt'})){$source+=$node.ToFullString()}
$source=$source.Replace('static class OverlayPaintSafety{','static class OverlayPaintSafety{'+(M 'ClampRowsToSpan'))
$source+='}}'
$surfaceTree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/QuestObjectiveSurface.cs'))).GetRoot()
$surface=@($surfaceTree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'QuestObjectiveSurface'})[0].ToFullString()
$source+='namespace MapleOverlay {'+$surface+'}'
$characters=[IO.File]::ReadAllText((Join-Path $r 'src/CharacterFieldOverlay.cs'))
$charactersTree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($characters).GetRoot()
foreach($node in @($charactersTree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -ne 'TranslationStore'})){$source+='namespace MapleOverlay {'+$node.ToFullString()+'}'}
foreach($file in @('QuestDetailGeometry.cs','QuestJournalOverlay.cs','StructuredPanelOverlay.cs','SkillSectionOverlay.cs','EquipmentFieldGrammar.cs')){$source+="`n"+([IO.File]::ReadAllText((Join-Path $r ('src/'+$file))) -replace '(?m)^using .*;\r?\n','')}
$source+=@'
namespace MapleOverlay {
 partial class OverlayForm {
  static OcrLine Line(string text,int x,int y,int w=250,int h=18){var line=new OcrLine{Text=text};line.Words.Add(new OcrWord{Text=text,BoundingRect=new RectangleF(x,y,w,h)});return line;}
  static Bitmap JournalFrame(){var frame=new Bitmap(1000,700);int[] pixels=Enumerable.Repeat(unchecked((int)0xff203030),700000).ToArray();
   for(int y=150;y<590;y++)for(int x=400;x<810;x++)pixels[y*1000+x]=unchecked((int)0xfffafafa);
   for(int y=150;y<290;y++)for(int x=408;x<804;x++)pixels[y*1000+x]=unchecked((int)0xffe1ded3);
   // Unrecognized English above and below the recognized row, plus an icon/counter
   // in the objective card. They must not depend on OCR row rectangles.
   for(int y=300;y<570;y+=27)for(int x=410;x<785;x+=9)pixels[y*1000+x]=unchecked((int)0xff101010);
   pixels[220*1000+430]=unchecked((int)0xff00ff00);pixels[220*1000+650]=unchecked((int)0xff0000ff);
   frame.Set(pixels);return frame;
  }
  static OverlayForm JournalForm(object real){var p=new OverlayForm();p.translations=new TranslationStore(real);p.Bounds=Rectangle.Empty;p.gameBounds=p.ClientRectangle=p.captureBounds=new Rectangle(0,0,1000,700);p.visibleRequestWatch=Stopwatch.StartNew();return p;}
  public static int RunJournals(object real){int n=0;string reason;var store=new TranslationStore(real);
   Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS: "+name);};
   var maya=store.SelectJournal("Maya's First Collection","",out reason);
   check(maya!=null&&maya.TaskId=="10115"&&maya.Chinese.Contains("橙蘑菇盖")&&!maya.Chinese.Contains("报酬"),"stage title selects corrected Maya prose without any body OCR");
   check(store.SelectJournal("Maya's First Collection","She wants 30 of each",out reason)==null,"contradictory quantity cannot select the 20-item description");
   check(store.SelectJournal("Caretaker's Ledgers","Lyn asked for 25 Torn Ledger Pages",out reason)!=null,"observed server caption selects verified 25-page paragraph");
   check(store.SelectJournal("Caretaker's Ledgers","recover 50 Torn Ledger Pages",out reason)!=null,"50-page variant has its own distinct data record");
   check(store.SelectJournal("Caretaker's Ledgers","",out reason)==null,"unconfirmed server quantity is not invented from its group title");
   var omok=store.SelectJournal("Make a Pig & Mushroom Omok Set","I brought the requested items to Casey in Henesys Park and received a Pig & Mushroom Omok Set!",out reason);
   check(omok!=null&&omok.Chinese.StartsWith("我把所需物品交给"),"short completed-state anchors select completed Omok journal, excluding NPC dialogue");
   var initial=store.SelectJournal("Make a Pig & Mushroom Omok Set","He said I could get them by defeating Stump, Pig, and Orange Mushroom found around Henesys.",out reason);
   check(initial!=null&&initial.Chinese.Contains("橙蘑菇")&&!initial.Chinese.Contains("我把"),"initial Omok journal excludes completion text and corrects Orange Mushroom");
   check(store.SelectJournal("Make a Pig & Mushroom Omok Set","",out reason)==null,"same-title alternative states require short disambiguating anchors");
   check(store.ResolveJournalCaption("Arwen and the Glass Sh...").Length>0,"unique truncated right caption can identify its canonical journal");
   check(store.SelectJournal("Collecting For Maya","",out reason)==null,"group caption cannot guess Maya first stage when another stage may be selected");
   check(store.ResolveJournalCaption("Maya...")=="","short ambiguous caption cannot authorize a whole description");
   using(var frame=JournalFrame()){
    var p=JournalForm(real);var stage=Line("Maya's First Collection",410,315);var lines=new List<OcrLine>{Line("Quest Summary",410,160),stage};var output=new List<OverlayLabel>{new OverlayLabel{Bounds=new RectangleF(410,350,80,18),SourceBounds=new RectangleF(410,350,80,18),Text="零碎词"}};
    check(p.TryAddQuestJournal(output,lines,lines,frame,1,true,new RectangleF(385,30,445,555))&&output.Count==1&&output[0].Journal!=null,"production label builder replaces fragments with one title-owned document");
    var label=output[0];check(label.SourceBounds.Top<=300&&label.SourceBounds.Bottom>=570&&label.SourceBounds.Right<810&&label.SourceBounds.Top>=290,"pixel-derived paper boundary owns missing rows while excluding summary and scrollbar: "+label.SourceBounds);
    p.captureTextRows.Add(new RectangleF(410,350,200,18));
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==1,"whole journal enters production single compositor despite missing body OCR");
    int[] composed=p.simpleLayer.Pixels();int[] original=frame.Pixels();
    check(original[543*1000+779]==unchecked((int)0xff101010)&&composed[543*1000+779]==unchecked((int)0xfffafafa),"unrecognized last English row is erased, not just OCR-known glyphs");
    check(composed[220*1000+430]==0&&composed[220*1000+650]==0&&composed[560*1000+815]==0,"objective icon, live count and scrollbar remain transparent and untouched");
    check(p.journalPageView.FontSize>=14,"journal text keeps readable size rather than shrinking to eight pixels");
    p.labels=output;using(var dst=new Bitmap(1000,700))using(var g=Graphics.FromImage(dst)){p.PaintSimpleFusion(g);check(g.Draws==1&&p.lastPaintedTranslationCount==1,"one publication paints one complete journal document");}
    check(p.Captures==0&&p.OcrCalls==0,"title/document planning launches no additional capture or OCR");p.ClearSimpleFusion();
   }
   using(var frame=JournalFrame()){
    var p=JournalForm(real);var caption=Line("Caretaker's Ledgers",410,60);var lines=new List<OcrLine>{caption,Line("Quest Summary",410,160),Line("Torn Ledger Pages 0 / 25",450,220),Line("Filing Error",410,315),Line("Lyn the Librarian asked me to recover 25 Torn Ledger Pages",410,350,380)};var output=new List<OverlayLabel>();
    check(p.TryAddQuestJournal(output,lines,lines,frame,1,true,new RectangleF(385,30,445,555))&&output[0].Text.Contains("25页")&&!output[0].Text.Contains("50页"),"actual caption, stage position and incomplete prose produce corrected 25-page full journal");
    check(output[0].Text.Contains("树洞迷宫：北部森林 VI")&&output[0].Text.Contains("别人的家"),"25-page translation retains screenshot-confirmed second paragraph");
    output.Clear();lines[4]=Line("Lyn the Librarian asked me to recover 50 Torn Ledger Pages",410,350,380);
    check(p.TryAddQuestJournal(output,lines,lines,frame,1,true,new RectangleF(385,30,445,555))&&output[0].Text.Contains("50页"),"production builder selects the observed 50-page variant rather than 25");
   }
   using(var frame=JournalFrame()){
    var p=JournalForm(real);var caption=Line("Arwen and the Glass Sh...",410,60);var prose=Line("Arwen the Fairy from Ellinia says she lost her shoes",410,350,380);var lines=new List<OcrLine>{caption,Line("Quest Summary",410,160),prose};var output=new List<OverlayLabel>();
    check(p.TryAddQuestJournal(output,lines,lines,frame,1,true,new RectangleF(385,30,445,555))&&output[0].Text.Contains("艾温"),"caption fallback replaces a journal even when its separate lower stage heading is missing");
   }
   using(var frame=JournalFrame()){
    var p=JournalForm(real);var stage=Line("Make a Pig & Mushroom Omok Set",410,315,320);var lines=new List<OcrLine>{Line("Available",25,30,80),Line("In Progress",140,30,90),Line("Completed",250,30,100),stage,Line("I brought the requested items to Casey in Henesys Park and received a Pig & Mushroom Omok Set!",410,350,380)};var output=new List<OverlayLabel>();
    check(p.TryAddQuestJournal(output,lines,lines,frame,1,true,RectangleF.Empty)&&output[0].Text.Contains("我把所需物品"),"journal without Summary or footer OCR uses measured tab split and actual paper boundaries");
   }
   using(var frame=JournalFrame())using(var zoom=new Bitmap(2000,1400)){
    int[] small=frame.Pixels(),large=new int[2800000];for(int y=0;y<1400;y++)for(int x=0;x<2000;x++)large[y*2000+x]=small[(y/2)*1000+x/2];zoom.Set(large);
    var p=JournalForm(real);var lines=new List<OcrLine>{Line("Quest Summary",820,320,300,36),Line("Maya's First Collection",820,630,400,36)};var output=new List<OverlayLabel>();
    check(p.TryAddQuestJournal(output,lines,lines,zoom,2,true,new RectangleF(770,60,890,1110))&&Math.Abs(output[0].SourceBounds.Left-401)<2&&Math.Abs(output[0].SourceBounds.Top-290)<2,"upscaled OCR paper measurements map back to original capture coordinates");
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==1,"scaled title plan composes against the original native-size capture");p.ClearSimpleFusion();
   }
   using(var frame=JournalFrame()){
    var p=JournalForm(real);var label=new OverlayLabel{Bounds=new RectangleF(401,291,407,298),SourceBounds=new RectangleF(401,291,407,298),PanelBounds=new RectangleF(401,291,407,298),Text=new string('文',3000),SemanticRole="task-journal",Journal=new QuestJournalDocument{Background=Color.FromArgb(250,250,250),FontHeight=18}};var output=new List<OverlayLabel>{label};
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.journalPageView.Pages.Count>1,"long Chinese description paginates within the measured viewport");
    check(String.Concat(p.journalPageView.Pages)==label.Text,"pagination preserves every character in order without truncation");
    p.visibleTranslation=true;check(p.ChangeQuestJournalPage(1)&&p.journalPageView.Page==1&&p.Captures==0&&p.OcrCalls==0,"page turn uses cached Chinese without another recognition pass");
    p.recognitionGeneration++;check(!p.ChangeQuestJournalPage(1),"stale generation cannot page a previous task");p.ClearSimpleFusion();check(p.journalPageView==null,"clearing translation retires cached pages");
   }
   int[] dark=Enumerable.Repeat(unchecked((int)0xff101010),700000).ToArray();Rectangle area;Color bg;
   check(!QuestJournalGeometry.Measure(dark,1000,700,new RectangleF(410,315,200,18),new RectangleF(385,30,445,555),1,out area,out bg),"unknown paper geometry never erases an estimated whole screen region");
   Console.WriteLine("Quest journal: "+n+" checks passed; actual candidate catalog + production title planning/geometry/composition; OCR/GDI controlled, native Windows unverified.");return n;
  }
 }
 public static class JournalRunner{public static int Run(object store){return OverlayForm.RunJournals(store);}}
}
'@
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$a=Load-TestAssembly (Join-Path $r 'bin/windows-v4.0/枫语幕.exe')
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$t=$a.GetType('MapleOverlay.TranslationStore',$true)
$real=$t.GetConstructors($f)[0].Invoke([object[]]@([string](Join-Path $r '枫语幕词库.tsv')));$t.GetMethod('Load',$f).Invoke($real,@())|Out-Null
[IO.File]::WriteAllText((Join-Path $work 'QuestJournalR14.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[MapleOverlay.JournalRunner]::Run($real)
