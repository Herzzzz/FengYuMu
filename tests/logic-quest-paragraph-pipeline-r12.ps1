param([switch]$Before)
$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot
$work='/workspace/work/release-v4.0'
$generated=Join-Path $work ('ParagraphBase-'+[Guid]::NewGuid().ToString('N')+'.cs')
& (Join-Path $PSScriptRoot 'logic-simple-flow-r11.ps1') -OutputSource $generated -GenerateOnly
$source=[IO.File]::ReadAllText($generated)
Remove-Item -LiteralPath $generated
$main=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/MapleOverlay.cs'))).GetRoot()
function C([string]$name){@($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0]}
function M([string]$name){@($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0].ToFullString()}
$source='using System.Text;using System.Text.RegularExpressions;using System.Reflection;'+$source
$source=$source.Replace('public bool HasSourceBounds=true,PaintedLastPass;','public bool HasSourceBounds=true,PaintedLastPass,Wrap;')
$source=$source.Replace('static class OverlayPaintSafety{','static class OverlayPaintSafety{'+(M 'ClampRowsToSpan'))
$source+="namespace MapleOverlay {"+(C 'TranslationEntry').ToFullString()+(C 'MatchResult').ToFullString()+@'
 class OcrWord{public string Text;public RectangleF BoundingRect;}
 class OcrLine{public string Text;public List<OcrWord> Words=new List<OcrWord>();}
 class TranslationStore {
  object store;static Type actual;static readonly BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  public TranslationStore(object value){store=value;actual=value.GetType();}
  public static string Normalize(string s){return (string)actual.GetMethod("Normalize",flags).Invoke(null,new object[]{s});}
  public static float DetailTextSimilarity(string a,string b){return (float)actual.GetMethod("DetailTextSimilarity",flags).Invoke(null,new object[]{a,b});}
  List<MatchResult> Matches(string method,object[] args){var result=new List<MatchResult>();foreach(object hit in (System.Collections.IEnumerable)actual.GetMethod(method,flags).Invoke(store,args)){
   object raw=hit.GetType().GetField("Entry",flags).GetValue(hit);var entry=new TranslationEntry();foreach(var f in typeof(TranslationEntry).GetFields(flags)){var rf=raw.GetType().GetField(f.Name,flags);if(rf!=null)f.SetValue(entry,rf.GetValue(raw));}
   result.Add(new MatchResult{Entry=entry,Start=(int)hit.GetType().GetField("Start",flags).GetValue(hit),Length=(int)hit.GetType().GetField("Length",flags).GetValue(hit)});
  }return result;}
  public List<MatchResult> FindSharedTaskBodyMatches(string s){return Matches("FindSharedTaskBodyMatches",new object[]{s});}
  public List<MatchResult> FindTaskMatches(string s,string id){return Matches("FindTaskMatches",new object[]{s,id});}
 }
 partial class OverlayForm {
  TranslationStore translations;string benchmarkQuestDebug="";const int DetailTailLength=24;
'@
$names=@('ApplyTaskBodyScope','AddQuestPanelTextLabels','SelectBestDetailLines','ScoreStrongLongTextCandidate','GetCombinedLineBounds','AddDetailBlockLabel','ToOverlayLineBounds','ToOverlayX','ToOverlayY','GetEntrySemanticRole','CanJoinOcrLines','AreOcrBlocksContinuous','GetOcrLineBounds','ContinuesDetailProse','IsPanelControlWord','DetailTailText')
foreach($name in $names){$source+=M $name}
$source+=(C 'SkillPanelCandidate').ToFullString()
$source+=@'
  public static int RunParagraphs(object real){
   int n=0;string body="Lyn the Librarian asked me to recover 50 Torn Ledger Pages the fairies took, then have Chief Stan in Henesys stamp the cover.";
   foreach(string text in new string[]{body,body.Replace("recover","rec over"),body.Replace("Torn Ledger","TornLedger"),body.Replace("recover","rec0ver")}){
    var p=new OverlayForm();p.translations=new TranslationStore(real);p.Bounds=Rectangle.Empty;p.gameBounds=p.ClientRectangle=p.captureBounds=new Rectangle(0,0,1000,800);p.visibleRequestWatch=Stopwatch.StartNew();
    var lines=new List<OcrLine>();string[] words=text.Split(' ');int row=0;
    for(int i=0;i<words.Length;i+=6){string s=String.Join(" ",words.Skip(i).Take(6));var line=new OcrLine{Text=s};line.Words.Add(new OcrWord{Text=s,BoundingRect=new RectangleF(400,220+row*18,300,12)});lines.Add(line);row++;}
    var found=new List<OverlayLabel>();p.AddQuestPanelTextLabels(found,lines,"",1,true);
    if(found.Count!=1||found[0].SemanticRole!="task-body"||found[0].SourceLineBounds.Count!=lines.Count)throw new Exception("full observed paragraph must generate one body with all OCR rows: variant="+text.IndexOf("rec0ver"));
    n++;Console.WriteLine("PASS: actual paragraph builder retains the complete matched body and each source row");
    p.ApplyTaskBodyScope(found,new RectangleF(380,200,360,160),1);
    if(found[0].PanelBounds.Right!=740)throw new Exception("selected task panel must bound translated text layout");
    if(text==body){n++;Console.WriteLine("PASS: measured selected-panel boundary constrains the actual full-paragraph compositor");}
    using(var frame=new Bitmap(1000,800)){
     int[] px=Enumerable.Repeat(unchecked((int)0xff303030),800000).ToArray();foreach(var line in lines){var box=line.Words[0].BoundingRect;for(int y=(int)box.Top;y<box.Bottom;y++)for(int x=(int)box.Left+3;x<box.Right;x+=5)px[y*1000+x]=unchecked((int)0xffeeeeee);}frame.Set(px);
     if(!p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,found).GetAwaiter().GetResult()||p.simpleReady.Count!=1)throw new Exception("matched full paragraph must enter shared compositor");
     n++;Console.WriteLine("PASS: actual full-paragraph geometry enters unified erasure and text composition");
     p.labels=found;using(var dst=new Bitmap(1000,800))using(var g=Graphics.FromImage(dst)){p.PaintSimpleFusion(g);if(g.Draws!=1||p.lastPaintedTranslationCount!=1)throw new Exception("one complete body must be published once");}
     n++;Console.WriteLine("PASS: actual one-layer paint publishes one complete paragraph once");
     p.ClearSimpleFusion();
    }
   }
   Console.WriteLine("Quest paragraph pipeline: "+n+" checks passed; production paragraph builder + actual candidate dictionary + production compositor; OCR/GDI boundaries controlled, not user frame.");return n;
  }
 }
 public static class ParagraphRunner{public static int Run(object store){return OverlayForm.RunParagraphs(store);}}
}
'@
$testedRoot=if($Before){'/workspace/FengYuMu-option2-matching-r11'}else{$r}
. (Join-Path $testedRoot 'tests/Load-TestAssembly.ps1')
$a=Load-TestAssembly (Join-Path $testedRoot $(if($Before){'bin/option2-matching-r11/枫语幕.exe'}else{'bin/windows-v4.0/枫语幕.exe'}))
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$t=$a.GetType('MapleOverlay.TranslationStore',$true)
$real=$t.GetConstructors($f)[0].Invoke([object[]]@([string](Join-Path $testedRoot '枫语幕词库.tsv')))
$t.GetMethod('Load',$f).Invoke($real,@())|Out-Null
[IO.File]::WriteAllText((Join-Path $work 'QuestParagraph.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[MapleOverlay.ParagraphRunner]::Run($real)
