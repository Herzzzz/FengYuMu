param([string]$PixelFixtures='/workspace/work/release-v4.0/skill-screenshots/pixel-replay.json')
$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot;$work='/workspace/work/release-v4.0'
if(-not (Test-Path -LiteralPath $PixelFixtures)){throw 'Actual user screenshot pixel fixtures are required; they are intentionally excluded from source evidence. Regenerate from the original36 PNG files before replay.'}
. (Join-Path $PSScriptRoot 'logic-sections-r15.ps1') | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $work 'Sections.generated.cs')).Replace('namespace SectionsReplay','namespace ScreenshotReplay')
$source+=@'
namespace ScreenshotReplay {
 partial class OverlayForm {
  internal sealed class Shot {internal string Id,Name,Path;internal int Width,Height,Expected,Divider;internal List<OcrLine> Rows;}
  static OcrLine Measured(string text,float x,float y,float w,float h){var line=new OcrLine{Text=text};line.Words.Add(new OcrWord{Text=text,BoundingRect=new RectangleF(x,y,w,h)});return line;}
  static List<Shot> Shots(){return new List<Shot>{
'@
function Literal([string]$value){[Microsoft.CodeAnalysis.CSharp.SymbolDisplay]::FormatLiteral($value,$true)}
foreach($fixture in (Get-Content -LiteralPath $PixelFixtures -Raw|ConvertFrom-Json)){
 $source+='new Shot{Id='+(Literal $fixture.case)+',Name='+(Literal $fixture.name)+',Path='+(Literal $fixture.pixels)+',Width='+$fixture.width+',Height='+$fixture.height+',Expected='+$fixture.expected+',Divider='+$fixture.divider+',Rows=new List<OcrLine>{'
 foreach($row in $fixture.rows){$values=@($row.bounds|ForEach-Object{([single]$_).ToString('R',[Globalization.CultureInfo]::InvariantCulture)+'f'});$source+='Measured('+(Literal $row.text)+','+($values -join ',')+'),'}
 $source+='}},'
}
$source+=@'
  };}
  public static int RunScreenshots(object real,string report){var records=new List<string>();int n=0,total=0;var store=new TranslationStore(real);var failures=new List<string>();
   foreach(var shot in Shots())using(var frame=new Bitmap(shot.Width,shot.Height)){
    byte[] bytes=System.IO.File.ReadAllBytes(shot.Path);int[] pixels=new int[shot.Width*shot.Height];Buffer.BlockCopy(bytes,0,pixels,0,bytes.Length);frame.Set(pixels);
    var p=JournalForm(real);p.ClientRectangle=p.captureBounds=p.gameBounds=new Rectangle(0,0,shot.Width,shot.Height);var labels=new List<OverlayLabel>();string id=store.ResolveSkillTitle(shot.Name);Plan(p,labels,shot.Rows,id,frame);
    bool prepared=p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult();bool numbers=true;
    foreach(var label in labels){string original=label.SemanticRole=="skill-body"?shot.Rows[2].Text:label.SemanticRole=="skill-current-body"?shot.Rows.First(x=>x.Text.StartsWith("[Current")).Text:label.Text;
     if(label.SemanticRole=="skill-current-body"||label.SemanticRole=="skill-next-body"){int header=shot.Rows.FindIndex(x=>x.Text.StartsWith(label.SemanticRole=="skill-current-body"?"[Current":"[Next"));original=shot.Rows[header+1].Text;}
     var actual=real.GetType().Assembly.GetType("MapleOverlay.TranslationValueSafety").GetMethod("PreservesNumbers",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
     numbers&=(bool)actual.Invoke(null,new object[]{original,label.Text});}
    bool clear=prepared&&p.simpleLayer!=null&&p.simpleLayer.Pixels()[(shot.Divider+30)*shot.Width+Math.Min(shot.Width-1,230)]==0&&p.simpleLayer.Pixels()[(int)(shot.Rows[1].Words[0].BoundingRect.Bottom+10)*shot.Width+65]==0;
    string status=shot.Id+" "+shot.Name+" expected="+shot.Expected+" planned="+labels.Count+" ready="+p.simpleReady.Count+" numbers="+numbers+" icon/divider="+clear+" "+String.Join(";",labels.Select(x=>x.SemanticRole+"="+x.FusionFailureReason));records.Add(status);
    bool ok=prepared&&labels.Count==shot.Expected&&p.simpleReady.Count==shot.Expected&&numbers&&clear&&p.Captures==0&&p.OcrCalls==0;
    if(!ok)failures.Add(status);else{n++;total+=shot.Expected;Console.WriteLine("PASS: screenshot "+status);}p.ClearSimpleFusion();
   }
   System.IO.File.WriteAllLines(report,new[]{"36 original user screenshots /41 panels; manually checked text and measured OCR geometry; production planner/compositor; Windows OCR and actual GDI remain UNVERIFIED"}.Concat(records));
   if(failures.Count>0)throw new Exception(String.Join("\n",failures));if(n!=41||total!=104)throw new Exception("screenshot coverage count mismatch");Console.WriteLine("PASS: all104 visible sections across41 panels compose together; no missing levels invented");return n+1;
  }
 }
 public static class ShotRunner{public static int Run(object real,string report){return OverlayForm.RunScreenshots(real,report);}}
}
'@
[IO.File]::WriteAllText((Join-Path $work 'Screenshots.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[ScreenshotReplay.ShotRunner]::Run($real,(Join-Path $work 'skill-screenshot-coverage.txt'))
$quality=Get-Content (Join-Path $r 'data/skill-overview-quality-r15.json') -Raw|ConvertFrom-Json
foreach($entry in $quality.entries){$before=([regex]::Matches($entry.before,'[+\-]?\d+(?:\.\d+)?%?')|ForEach-Object{$_.Value}) -join '|';$after=([regex]::Matches($entry.after,'[+\-]?\d+(?:\.\d+)?%?')|ForEach-Object{$_.Value}) -join '|';if($before -ne $after){throw 'overview wording review changed numeric facts'}}
Write-Output "PASS: full-column wording review scanned $($quality.scannedSkillDescriptionRows) rows; $($quality.changedRows) ordinary data corrections preserve numeric facts"
