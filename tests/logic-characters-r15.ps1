$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot;$work='/workspace/work/release-v4.0'
. (Join-Path $PSScriptRoot 'logic-sections-r15.ps1') | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $work 'Sections.generated.cs')).Replace('namespace SectionsReplay','namespace CharactersReplay')
$source+=@'

namespace CharactersReplay {
 partial class OverlayForm {
  static void Cell(int[] px,int left,int top,int height,int color){for(int y=top;y<top+height;y++)for(int x=left;x<left+90;x++)px[y*1000+x]=color;for(int x=left+10;x<left+65;x+=7)px[(top+9)*1000+x]=unchecked((int)0xffffffff);px[(top+10)*1000+left+100]=unchecked((int)0xff101010);}
  public static int RunCharacters(object real,object synthetic){int n=0;Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS: "+name);};
   using(var frame=new Bitmap(1000,700)){
    var px=Enumerable.Repeat(unchecked((int)0xffdddcd2),700000).ToArray();int top=60;var jobs=new RectangleF();for(int i=0;i<7;i++){int h=i==1?44:22;Cell(px,40,top,h,unchecked((int)0xffca4d83));if(i==1)jobs=new RectangleF(40,top,90,h);top+=h+4;}for(int i=0;i<4;i++)Cell(px,40,top+i*26,22,unchecked((int)0xffb2c146));for(int i=0;i<10;i++)Cell(px,340,110+i*26,22,unchecked((int)0xff588ab5));frame.Set(px);
    var rows=new List<OcrLine>{Line("CHARACTER STAT",38,20,160),Line("MAGICIAN",145,(int)jobs.Top+2,95,16),Line("CLERIC",145,(int)jobs.Top+23,65,16)};
    var p=JournalForm(real);var labels=new List<OverlayLabel>();p.AddCharacterFieldSurfaces(labels,rows,frame,1);
    check(labels.Count(x=>x.SemanticRole=="character-field")==21,"all 7 main,4 attribute,10 detail cells translate from verified character geometry despite missing label OCR");
    check(labels.Count(x=>x.SemanticRole=="character-job")==2,"both job group and specialization resolve through the complete job dictionary");
    check(labels.Any(x=>x.Text=="暴击伤害")&&labels.Any(x=>x.Text=="智力")&&labels.All(x=>x.HasSourceBounds&&x.SourceBounds.Width>8),"character labels have measured erase bounds and complete shared field vocabulary");
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==23,"all measured character fields and both job values reach the actual compositor together");
    check(p.simpleLayer.Pixels()[70*1000+140]==0&&p.simpleLayer.Pixels()[120*1000+440]==0,"original stats and numbers remain outside the translated label cells");p.ClearSimpleFusion();
   }
   using(var frame=new Bitmap(1000,700)){
    var px=Enumerable.Repeat(unchecked((int)0xffdddcd2),700000).ToArray();Cell(px,300,50,22,unchecked((int)0xffca4d83));for(int i=0;i<4;i++)Cell(px,300,100+i*26,22,unchecked((int)0xffca4d83));frame.Set(px);
    var rows=new List<OcrLine>{Line("CHARACTER INFO",40,20,160),Line("Cleric",410,128,80,16),Line("Cleric",60,240,80,16)};var p=JournalForm(real);var labels=new List<OverlayLabel>();p.AddCharacterFieldSurfaces(labels,rows,frame,1);
    check(labels.Count(x=>x.SemanticRole=="character-field")==4&&labels.Count(x=>x.SemanticRole=="character-job")==1,"Info uses its four field cells, skips its pink tab and translates only the job value rather than the same-named player");
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==5,"Info field and job overlays all compose together");p.ClearSimpleFusion();
   }
   var store=new TranslationStore(synthetic);check(store.TranslateJobName("Starweaver")=="织星者","an anonymous new dictionary job works without any profession branch");
   using(var frame=new Bitmap(1000,700)){var px=Enumerable.Repeat(unchecked((int)0xffdddcd2),700000).ToArray();Cell(px,40,60,22,unchecked((int)0xffca4d83));frame.Set(px);var p=JournalForm(real);var labels=new List<OverlayLabel>();p.AddCharacterFieldSurfaces(labels,new List<OcrLine>{Line("CHARACTER STAT",38,20,160)},frame,1);check(labels.Count==0,"an incomplete unrelated colored column cannot invent the entire character sheet");}
   Console.WriteLine("Character coverage: "+n+" checks passed; controlled geometry/compositor, physical Windows fonts unverified.");return n;
  }
 }
 public static class ExtraRunner {public static int Run(object real,object synthetic,string root,string work){return OverlayForm.RunCharacters(real,synthetic);}}
}

'@
[IO.File]::WriteAllText((Join-Path $work 'CharactersReplay.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[CharactersReplay.ExtraRunner]::Run($real,$synthetic,$r,$work)
