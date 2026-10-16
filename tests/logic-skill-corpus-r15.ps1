$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot;$work='/workspace/work/release-v4.0'
. (Join-Path $PSScriptRoot 'logic-sections-r15.ps1') | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $work 'Sections.generated.cs')).Replace('namespace SectionsReplay','namespace SkillCorpusReplay')
$source+=@'

namespace SkillCorpusReplay {
 partial class OverlayForm {
  public static int RunSkillCorpus(object real,string root,string work){var names=new Dictionary<string,string>();var overviews=new Dictionary<string,string>();var levels=new Dictionary<string,List<string>>();
   foreach(string raw in System.IO.File.ReadLines(System.IO.Path.Combine(root,"枫语幕词库.tsv"))){var row=raw.Split('\t');if(row.Length<3)continue;if(row[0].Length>=2&&row[0][0]=='"'&&row[0][row[0].Length-1]=='"')row[0]=row[0].Substring(1,row[0].Length-2).Replace("\"\"","\"");var at=row[2].LastIndexOf('#');if(at<0)continue;string id=row[2].Substring(at+1);
    if(row[2].StartsWith("怀旧服-技能#"))names[id]=row[0];if(!row[2].StartsWith("怀旧服-技能说明#"))continue;
    if(row.Length>3&&row[3].StartsWith("Henesys.gg等级")){if(!levels.ContainsKey(id))levels[id]=new List<string>();if(!levels[id].Contains(row[0]))levels[id].Add(row[0]);}
    else if(!overviews.ContainsKey(id)||row[0].Length>overviews[id].Length)overviews[id]=row[0];
   }
   int overviewCount=0,sectionCount=0;var failures=new List<string>();var store=new TranslationStore(real);var covered=new List<string>();
   foreach(var entry in overviews){string text=Regex.Replace(entry.Value,@"^\s*\[?Master\s+Level\s*:?\s*\d+\]?\s*","",RegexOptions.IgnoreCase);text=Regex.Split(text,@"\bRequired\s+Skill\s*:",RegexOptions.IgnoreCase)[0].Trim();string cn;
    if(!store.TryTranslateSkillOverview(text,entry.Key,out cn))failures.Add("overview "+entry.Key);else overviewCount++;
    if(!levels.ContainsKey(entry.Key)||levels[entry.Key].Count<2||!names.ContainsKey(entry.Key))continue;
    using(var frame=SkillFrame()){var p=JournalForm(real);var rows=SkillRows(names[entry.Key],text,levels[entry.Key][0],levels[entry.Key][1]);var labels=new List<OverlayLabel>();Plan(p,labels,rows,entry.Key,frame);
     bool planned=labels.Count(x=>x.SemanticRole=="skill-body")==1&&labels.Count(x=>x.SemanticRole=="skill-current-body")==1&&labels.Count(x=>x.SemanticRole=="skill-next-body")==1;
     bool composed=planned&&p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==3;if(!composed)failures.Add("sections "+entry.Key+" labels="+String.Join(",",labels.Select(x=>x.SemanticRole))+" ready="+p.simpleReady.Count);else{sectionCount++;covered.Add(entry.Key);}p.ClearSimpleFusion();}
   }
   System.IO.File.WriteAllLines(System.IO.Path.Combine(work,"skill-sections-coverage.txt"),new[]{"overviewTested="+overviews.Count,"overviewPassed="+overviewCount,"skillsWithTwoAvailableEffects="+sectionCount,"unverified=Windows OCR,pixels,fonts,provider; absent source level rows are not tested"}.Concat(covered.Select(x=>"PASS "+x)).Concat(failures));
   if(failures.Count>0)throw new Exception(String.Join(";",failures));if(overviewCount<250||sectionCount<150)throw new Exception("unexpected corpus coverage shrink");
   Console.WriteLine("PASS: every corpus skill overview uses the same overview path ("+overviewCount+")");Console.WriteLine("PASS: all available two-level skill records plan simultaneous overview/current/next ("+sectionCount+")");Console.WriteLine("PASS: those three sections actually compose together across the available corpus");return 3;
  }
 }
 public static class ExtraRunner {public static int Run(object real,object synthetic,string root,string work){return OverlayForm.RunSkillCorpus(real,root,work);}}
}

'@
[IO.File]::WriteAllText((Join-Path $work 'SkillCorpusReplay.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[SkillCorpusReplay.ExtraRunner]::Run($real,$synthetic,$r,$work)
