$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot
$work='/workspace/work/release-v4.0'
# Generate the production planner/compositor harness, then run general scenarios
# against the candidate dictionary and never-seen-before task records.
& (Join-Path $PSScriptRoot 'logic-quest-journal-r13.ps1') | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $work 'QuestJournalR14.generated.cs'))
# The first invocation compiled the same internal names in this PowerShell
# process. A separate namespace keeps the general replay independent.
$source=$source.Replace('namespace MapleOverlay','namespace UniversalPanels')
$legacy=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/QuestJournalGeometry-r13.cs'))).GetRoot()
$old=@($legacy.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'QuestJournalGeometry'})[0].ToFullString().Replace('QuestJournalGeometry','LegacyQuestJournalGeometry')
$source+='namespace UniversalPanels {'+$old+'}'
$source+=@'
namespace UniversalPanels {
 partial class OverlayForm {
  static Bitmap GeneralFrame(bool bevel=true){var frame=JournalFrame();int[] px=frame.Pixels();
   if(bevel)for(int y=150;y<590;y++)for(int x=394;x<400;x++)px[y*1000+x]=unchecked((int)0xffe6e6e6);
   for(int y=30;y<140;y++)for(int x=400;x<810;x++)px[y*1000+x]=unchecked((int)0xff4182b4);
   frame.Set(px);return frame;
  }
  static OcrLine Cell(string key,string value,int y,int nameWidth=140,int valueX=650){
   var line=new OcrLine{Text=key+" "+value};line.Words.Add(new OcrWord{Text=key,BoundingRect=new RectangleF(450,y,nameWidth,16)});
   if(value.Length>0)line.Words.Add(new OcrWord{Text=value,BoundingRect=new RectangleF(valueX,y,60,16)});return line;
  }
  static Dictionary<OcrLine,OcrPanelInfo> FieldsPanel(List<OcrLine> lines,bool equipment,string skill=""){
   var panel=new OcrPanelInfo{IsEquipmentDetail=equipment,IsSkillDetail=skill.Length>0,SkillId=skill};panel.Lines.AddRange(lines);var map=new Dictionary<OcrLine,OcrPanelInfo>();foreach(var line in lines)map[line]=panel;return map;
  }
  public static int RunUniversal(object real,object synthetic){int n=0;
   Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS: "+name);};
   var store=new TranslationStore(real);string reason,chinese;
   using(var frame=GeneralFrame()){
    Rectangle oldArea,newArea;Color oldColor,newColor;int[] pixels=frame.Pixels();
    bool old=LegacyQuestJournalGeometry.Measure(pixels,1000,700,new RectangleF(410,315,200,18),new RectangleF(385,0,615,585),1,out oldArea,out oldColor);
    bool current=QuestJournalGeometry.Measure(pixels,1000,700,new RectangleF(410,315,200,18),new RectangleF(385,0,615,585),1,out newArea,out newColor);
    check(!old&&current,"same pale frame bevel reproduces r13 failure and is accepted by general paper geometry");
    check(newArea.Left>=400&&newArea.Top>=290&&newArea.Right<810,"bevel repair does not include the objective card or scrollbar");
   }
   using(var frame=GeneralFrame()){
    var p=JournalForm(synthetic);var caption=Line("Quartermaster Assignments",410,60,300);
    var stage=Line("Quartermaster's Supply Check",410,315,300);
    var first=new List<OcrLine>{caption,Line("Quest Summary",410,160),stage};var output=new List<OverlayLabel>();
    check(p.TryAddQuestJournal(output,new List<OcrLine>{stage},first,frame,1,true,new RectangleF(385,150,445,140))&&output[0].Text.Contains("琥珀"),"never-seen task title works even when legacy scope was only the objective card");
    var scrolled=new List<OcrLine>{caption,Line("beside the northern gate",410,315,280)};output.Clear();
    check(p.TryAddQuestJournal(output,scrolled,scrolled,frame,1,true,new RectangleF(385,0,445,585))&&output[0].Text.Contains("琥珀"),"same generic task survives scrolling without Summary or its stage heading");
    check(Program.Logs.Any(x=>x.Contains("same-task-scroll-context")),"scroll replay used verified task context rather than a named-task branch");
    var next=new List<OcrLine>{caption,Line("Quartermaster's Second Check",410,315,300)};output.Clear();
    check(p.TryAddQuestJournal(output,next,next,frame,1,true,new RectangleF(385,0,445,585))&&output[0].Text.Contains("苍蓝"),"a new stage overrides cached text despite sharing the same group caption");
    var unrelated=new List<OcrLine>{Line("Other Assignments",410,60,280),Line("beside the northern gate",410,315,280)};output.Clear();
    check(!p.TryAddQuestJournal(output,unrelated,unrelated,frame,1,true,new RectangleF(385,0,445,585)),"different group caption cannot reuse a cached stage from another task");
   }
   using(var frame=GeneralFrame()){
    var p=JournalForm(real);var rows=new List<OcrLine>{Cell("Pig's Ribbon","42 / 20",180,220,700),Cell("Bubbling's Huge Bubble","0 / 20",206,220,700),Cell("Fire Boar's Tooth","0 / 20",232,220,700),Cell("Tablecloth","0 / 20",258,220,700)};
    var output=new List<OverlayLabel>();p.AddObjectiveSurfaceLabels(output,rows,frame,new RectangleF(385,0,445,585),1);
    check(output.Count==4&&output.All(x=>x.SemanticRole=="task-objective"),"fixed grey surface translates every item name independently of any task body");
    check(output.All(x=>!System.Text.RegularExpressions.Regex.IsMatch(x.Text,@"\d")),"objective translation contains nouns only, never copied or translated quantities");
    check(output.All(x=>!x.KeepClearBounds.IsEmpty&&x.SourceBounds.Right<x.KeepClearBounds.Left),"all objective counter rectangles remain outside the name erasures");
    p.captureTextRows.AddRange(rows.Select(GetOcrLineBounds));
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==4,"all grey-card noun translations reach the production compositor");
    int[] layer=p.simpleLayer.Pixels();check(rows.All(x=>{var r=x.Words[1].BoundingRect;return layer[(int)r.Top*1000+(int)r.Left]==0;}),"every original counter pixel stays live and transparent in the output layer");p.ClearSimpleFusion();
   }
   using(var frame=GeneralFrame()){
    var p=JournalForm(real);var rows=new List<OcrLine>{Cell("Pig's Ribbon","42 20",180,220,700),Cell("Tablecloth","O / 2O",210,220,700)};
    var output=new List<OverlayLabel>();p.AddObjectiveSurfaceLabels(output,rows,frame,new RectangleF(385,0,445,585),1);
    check(output.Count==2&&output.All(x=>!System.Text.RegularExpressions.Regex.IsMatch(x.Text,@"[0-9]")),"lost slash and OCR counter letters still translate nouns only, without reconstructing a number");
    check(output.All(x=>!x.KeepClearBounds.IsEmpty&&x.SourceBounds.Right<x.KeepClearBounds.Left),"uncertain counter glyphs remain original protected pixels");
   }
   using(var frame=GeneralFrame()){
    var p=JournalForm(real);var rows=new List<OcrLine>{Cell("Tablecloth","",210,180)};var output=new List<OverlayLabel>();
    p.AddObjectiveSurfaceLabels(output,rows,frame,new RectangleF(385,0,445,585),1);
    check(output.Count==1&&output[0].Text=="桌布","a recognized objective noun does not depend on recognizing its counter at all");
    p.captureTextRows.AddRange(rows.Select(GetOcrLineBounds));
    int[] glyphs=frame.Pixels();glyphs[215*1000+460]=unchecked((int)0xff101010);frame.Set(glyphs);
    bool composed=p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult();
    check(composed&&p.simpleLayer!=null&&p.simpleLayer.Pixels()[220*1000+650]==0,"a counter missed by OCR still remains an original transparent pixel; composed="+composed+" ready="+p.simpleReady.Count+" bounds="+output[0].SourceBounds+" reason="+output[0].FusionFailureReason+(p.simpleLayer!=null?" pixel="+p.simpleLayer.Pixels()[220*1000+650]:" no-layer"));p.ClearSimpleFusion();
   }
   using(var frame=GeneralFrame()){
    var p=JournalForm(synthetic);var rows=new List<OcrLine>{Cell("MoonPearl","",210,40)};var output=new List<OverlayLabel>();
    int[] pixels=frame.Pixels();pixels[214*1000+460]=unchecked((int)0xff101010);pixels[210*1000+504]=unchecked((int)0xff0000ff);frame.Set(pixels);
    p.AddObjectiveSurfaceLabels(output,rows,frame,new RectangleF(385,0,445,585),1);p.captureTextRows.AddRange(rows.Select(GetOcrLineBounds));
    check(output.Count==1&&p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==1&&p.simpleLayer.Pixels()[210*1000+504]==0,"a long translated noun cannot grow into a nearby counter that OCR never detected");p.ClearSimpleFusion();
   }
   var shared=new TranslationStore(synthetic).SelectJournalForProse("The violet orchard needs fresh rainwater before the moon rises.",out reason);
   check(shared!=null&&shared.TaskId==""&&shared.Chinese.Contains("紫罗兰"),"shared chain prose is indexed without assigning an invented task ID or arbitrary title");
   string[,] grammar={
    {"Weapon ATT","+87","物理攻击力"},{"W.ATK","87","物理攻击力"},{"Magic Attack","+35","魔法攻击力"},
    {"Weapon DEF","42","物理防御力"},{"Magic Defense","28","魔法防御力"},
    {"STR","+7","力量"},{"DEX","-2","敏捷"},{"INT","+11","智力"},{"lNT","+11","智力"},{"LUK","+5","运气"},
    {"REQ. INT","40","需要智力"},{"Required Level","30","需要等级"},{"HP","+100","生命值"},{"MP","+80","魔法值"},
    {"Accuracy","12","命中率"},{"Avoidability","7","回避率"},{"Speed","+10%","移动速度"},{"Jump","+3","跳跃力"},
    {"Upgrades Available","7","剩余强化次数"}};
   bool grammarPass=true;foreach(int index in Enumerable.Range(0,grammar.GetLength(0))){var fields=EquipmentFieldGrammar.Parse(grammar[index,0]+" : "+grammar[index,1],true);grammarPass&=fields.Count==1&&fields[0].Chinese==grammar[index,2];}
   check(grammarPass,"one field grammar handles attack, defense, attributes, requirements and live modifiers for arbitrary items");
   check(EquipmentFieldGrammar.Parse("INTelligence is important for this skill.",false).Count==0,"ordinary prose cannot be treated as an equipment value row");
   using(var frame=GeneralFrame()){
    var p=JournalForm(real);var lines=new List<OcrLine>{Cell("Weapon ATT","+87",330),Cell("INT","+11",355,35),Cell("REQ. INT","40",380,90),Cell("Magic Defense","28",405)};
    var output=new List<OverlayLabel>();p.AddStructuredFieldLabels(output,lines,FieldsPanel(lines,true),1);
    check(output.Count==4&&output.Any(x=>x.Text=="智力"),"equipment fields do not require recognizing an item name or ID");
    p.captureTextRows.AddRange(lines.Select(GetOcrLineBounds));
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==4,"every planned equipment attribute is actually composed");
    int[] layer=p.simpleLayer.Pixels();check(lines.All(x=>{var r=x.Words[1].BoundingRect;return layer[(int)r.Top*1000+(int)r.Left]==0;}),"separate equipment values remain their original live pixels");p.ClearSimpleFusion();
   }
   check(store.TryTranslateSkillEffect("Chance to dodge attacks.","4120002",out chinese)&&chinese.Length>0,"short shared skill prose retains its exact identity without relying on whole-sentence fuzzy minimums");
   string original="MP 16; Physical damage -1%, Basic Attack 10 non-elemental magic damage for 100 sec";
   check(store.TryTranslateSkillEffect(original,null,out chinese)&&chinese.Contains("-1%")&&chinese.Contains("100"),"skill effect template retains observed balance values instead of requiring the old level sentence");
   string unseen="MP 73; Physical damage -22%, Basic Attack 37 non-elemental magic damage for 1439 sec";
   check(store.TryTranslateSkillEffect(unseen,null,out chinese)&&chinese.Contains("73")&&chinese.Contains("-22%")&&chinese.Contains("1439"),"unseen cost, damage and duration use the same numeric template without a skill-specific branch");
   using(var frame=GeneralFrame()){
    var p=JournalForm(real);var lines=new List<OcrLine>{Line("Next Level: 1",410,315,120),Line("MP 16; Physical damage -1%, Basic Attack 10 non-elemental magic",410,345,380),Line("damage for 100 sec",410,368,200)};
    var output=new List<OverlayLabel>();p.AddSkillEffectSections(output,lines,FieldsPanel(lines,false,"2301002"),1);
    check(output.Count==1&&output[0].SemanticRole=="skill-next-body"&&output[0].Text.Contains("-1%"),"wrapped consumption/effect footer is independent of the already translated skill overview");
    p.captureTextRows.AddRange(lines.Select(GetOcrLineBounds));
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==1,"complete skill cost and effects enter the compositor together");
    check(p.Captures==0&&p.OcrCalls==0,"generic field and effect supplements launch no extra capture or OCR");p.ClearSimpleFusion();
   }
   using(var frame=GeneralFrame()){
    var p=JournalForm(real);var lines=new List<OcrLine>{Line("Next Level: 1 "+original,410,315,380)};var output=new List<OverlayLabel>();
    p.AddSkillEffectSections(output,lines,FieldsPanel(lines,false,"2301002"),1);
    check(output.Count==1&&output[0].Text.StartsWith("下一级：1"),"one OCR row merging level header and effect is translated as one complete block");
   }
   Console.WriteLine("Universal panel replay: "+n+" checks passed; anonymous new quests and general fields, controlled OCR/GDI, native Windows unverified.");return n;
  }
 }
 public static class UniversalRunner{public static int Run(object real,object synthetic){return OverlayForm.RunUniversal(real,synthetic);}}
}
'@
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$a=Load-TestAssembly (Join-Path $r 'bin/windows-v4.0/枫语幕.exe')
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$t=$a.GetType('MapleOverlay.TranslationStore',$true)
$real=$t.GetConstructors($f)[0].Invoke([object[]]@([string](Join-Path $r '枫语幕词库.tsv')));$t.GetMethod('Load',$f).Invoke($real,@())|Out-Null
$fixture=Join-Path $work 'anonymous-tasks.tsv'
@"
Quartermaster's Supply Check`t军需官的补给核验`t怀旧服-任务#9100001
Collect 7 amber seals and return to the quartermaster before sunset. The sealed crates wait beside the northern gate.`t收集7枚琥珀印章，日落前交给军需官。封好的箱子放在北门旁。`t怀旧服-任务说明#9100001
Quartermaster's Second Check`t军需官的第二次核验`t怀旧服-任务#9100002
Collect 13 azure tokens and bring them to the outpost. The messenger waits beside the eastern bridge.`t收集13枚苍蓝令牌，送往前哨站。信使正在东桥旁等待。`t怀旧服-任务说明#9100002
MoonPearl`t测试用的月光宝珠`t怀旧服-道具#9100003
Anonymous chain instructions: The violet orchard needs fresh rainwater before the moon rises.`t紫罗兰果园需要新鲜雨水，请在月亮升起前送达。`t怀旧服-任务说明
"@ | Set-Content -LiteralPath $fixture -Encoding utf8
$synthetic=$t.GetConstructors($f)[0].Invoke([object[]]@([string]$fixture));$t.GetMethod('Load',$f).Invoke($synthetic,@())|Out-Null
[IO.File]::WriteAllText((Join-Path $work 'UniversalPanels.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[UniversalPanels.UniversalRunner]::Run($real,$synthetic)
