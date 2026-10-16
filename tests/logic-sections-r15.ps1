$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot
$work='/workspace/work/release-v4.0'
& (Join-Path $PSScriptRoot 'logic-quest-journal-r13.ps1') | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $work 'QuestJournalR14.generated.cs')).Replace('namespace MapleOverlay','namespace SectionsReplay')
$source+=@'
namespace SectionsReplay {
 partial class OverlayForm {
  static Bitmap SkillFrame(int divider=340){var b=new Bitmap(1000,700);var px=Enumerable.Repeat(unchecked((int)0xff202544),700000).ToArray();
   for(int x=25;x<800;x++)px[divider*1000+x]=unchecked((int)0xffbfc0bf);
   for(int y=112;y<280;y+=24)for(int x=175;x<780;x+=11)px[y*1000+x]=unchecked((int)0xffededed);
   for(int y=390;y<425;y+=10)for(int x=30;x<790;x+=11)px[y*1000+x]=unchecked((int)0xffededed);
   for(int y=490;y<525;y+=10)for(int x=30;x<790;x+=11)px[y*1000+x]=unchecked((int)0xffededed);
   b.Set(px);return b;}
  static List<OcrLine> SkillRows(string title,string intro,string current,string next){return new List<OcrLine>{
   Line(title,40,25,180),Line("[Master Level: 30]",170,65,160),Line(intro,170,110,620,170),
   Line("[Current Level5]",30,360,150),Line(current,30,390,760,40),Line("Next Level: 6",30,460,150),Line(next,30,490,760,40)};}
  static Dictionary<OcrLine,OcrPanelInfo> Mapping(List<OcrLine> rows,string id,bool equipment=false){var panel=new OcrPanelInfo{SkillId=id,IsSkillDetail=!equipment,IsEquipmentDetail=equipment};panel.Lines.AddRange(rows);var map=new Dictionary<OcrLine,OcrPanelInfo>();foreach(var line in rows)map[line]=panel;return map;}
  static void Plan(OverlayForm p,List<OverlayLabel> labels,List<OcrLine> rows,string id,Bitmap frame){var map=Mapping(rows,id);p.AddSkillOverviewSections(labels,rows,map,frame,1);p.AddSkillEffectSections(labels,rows,map,1);p.captureTextRows.AddRange(rows.Select(GetOcrLineBounds));}
  public static int RunSections(object real,object synthetic){int n=0;Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS: "+name);};var store=new TranslationStore(real);string chinese;
   check(OverlayPaintSafety.IsSafeSeamlessGeometry(new RectangleF(30,100,766,45),new RectangleF(33,102,760,40),1000,700),"a long measured tooltip row is not rejected just for exceeding 60 percent of window width");
   check(!OverlayPaintSafety.IsSafeSeamlessGeometry(new RectangleF(0,0,940,400),new RectangleF(0,0,940,400),1000,700),"large screen-area replacements remain refused despite the long-row allowance");
   check(store.TryTranslateSkillEffect("MP -12; Recovery rate 40%; Range 100%","",out chinese)&&chinese.Contains("恢复率")&&chinese.Contains("范围")&&chinese.Contains("40%"),"effect clauses compose even when a known effect gains an additional range clause");
   check(store.TryTranslateSkillEffect("MP -73; Recovery rate 61%; Range 137%","",out chinese)&&chinese.Contains("-73")&&chinese.Contains("137%"),"new numeric values and combinations use the same vocabulary without selecting a level");
   check(store.TryTranslateSkillEffect("MP -20; All Damage taken is reduced by 5%, and HP recovery from the Heal skill is increased by 1% for 100 sec","",out chinese)&&chinese.Contains("伤害降低")&&chinese.Contains("群体治愈")&&chinese.Contains("100"),"generic mitigation and named-reference healing effects combine without a skill ID exception");
   check(!store.TryTranslateSkillEffect("MP -20; unknown secret effect 987","",out chinese),"an unknown effect clause cannot disappear behind a translated cost prefix");
   using(var frame=SkillFrame()){
    var p=JournalForm(real);var rows=SkillRows("Heal","Recovers the HP of all party members around the area. The amount of healing depends on the number of people being healed, and undead monsters around the area will receive damage.","MP -12; Recovery rate 40%; Range 100%","MP -12; Recovery rate 42%; Range 100%");
    check(p.ResolveSkillContextId(rows,rows[3],1)=="2301001","the observed header owns skill identity even when a cost row names another known skill");
    var labels=new List<OverlayLabel>();Plan(p,labels,rows,"1001",frame);
    check(labels.Count(x=>x.SemanticRole=="skill-body")==1&&labels.Count(x=>x.SemanticRole=="skill-current-body")==1&&labels.Count(x=>x.SemanticRole=="skill-next-body")==1,"overview and both level effects are all planned in the same frame despite a misleading legacy panel ID");
    check(labels.First(x=>x.SemanticRole=="skill-body").Text.Contains("不死")&&!labels.First(x=>x.SemanticRole=="skill-body").Text.Contains("30秒"),"overview cannot borrow the Recovery skill mentioned inside an effect");
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==3,"all three independent sections reach the actual compositor simultaneously: "+String.Join(";",labels.Select(x=>x.SemanticRole+"="+x.FusionFailureReason))+" logs="+String.Join("|",Program.Logs.Skip(Math.Max(0,Program.Logs.Count-6))));
    check(p.Captures==0&&p.OcrCalls==0,"independent sections reuse the existing frame and OCR");p.ClearSimpleFusion();
   }
   using(var frame=SkillFrame()){
    var p=JournalForm(synthetic);var rows=SkillRows("Cobalt Pulse","Sends a focused wave through nearby targets.","MP -19; Basic Attack 217, Mastery level 7","MP -23; Basic Attack 239, Mastery level 8");var labels=new List<OverlayLabel>();Plan(p,labels,rows,"9900001",frame);
    check(labels.Count==3&&p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==3,"an anonymous skill outside the shipped corpus has simultaneous introduction/current/next sections");p.ClearSimpleFusion();
   }
   using(var frame=SkillFrame()){
    var p=JournalForm(real);var rows=SkillRows("Holy Arrow","damage.","MP -15; Basic Attack 108, Mastery level 2","MP -15; Basic Attack 110, Mastery level 2");var labels=new List<OverlayLabel>();Plan(p,labels,rows,"2301004",frame);
    var intro=labels.FirstOrDefault(x=>x.SemanticRole=="skill-body");
    check(intro!=null&&intro.MatchSource=="skill-overview-slot"&&intro.SourceBounds.Top>80&&intro.SourceBounds.Right<800&&intro.SourceBounds.Bottom<340,"verified header and separator bound the full introduction slot when OCR captures only a short tail");
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==3,"incomplete introduction OCR cannot suppress complete level effects, or vice versa");
    check(p.simpleLayer.Pixels()[340*1000+500]==0&&p.simpleLayer.Pixels()[120*1000+80]==0,"the separator and icon column remain original pixels outside the introduction erase");p.ClearSimpleFusion();
   }
   using(var frame=SkillFrame(325)){
    var p=JournalForm(real);var rows=SkillRows("Holy Arrow","damage.","MP -15; Basic Attack 108, Mastery level 2","MP -15; Basic Attack 110, Mastery level 2");var labels=new List<OverlayLabel>();Plan(p,labels,rows,"2301004",frame);
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult()&&p.simpleReady.Count==3,"a short fractional final overview strip cannot invalidate an otherwise measured introduction");p.ClearSimpleFusion();
   }
   using(var frame=JournalFrame()){
    var p=JournalForm(real);var rows=new List<OcrLine>{Line("Available In Progress Completed",40,25,320),Line("A Clue to the Secret Book",410,315,300)};var output=new List<OverlayLabel>();
    check(p.TryAddQuestJournal(output,rows,rows,frame,1,true,RectangleF.Empty),"a task with no Summary or objective icons still resolves through a combined tab strip and stage title");
    check(output[0].Journal!=null&&output[0].Text.Contains("桑拿"),"a title-backed task translates complete known prose despite different visible English wording");
   }
   using(var frame=JournalFrame()){
    var px=frame.Pixels();for(int x=455;x<640;x+=9)px[207*1000+x]=unchecked((int)0xff101010);px[207*1000+670]=unchecked((int)0xff0000ff);frame.Set(px);
    var p=JournalForm(real);var line=Line("Green Mushroom0/25",450,200,260,16);var output=new List<OverlayLabel>();
    p.AddObjectiveSurfaceLabels(output,new List<OcrLine>{line},frame,new RectangleF(385,0,445,585),1);
    check(output.Count==1&&output[0].Text=="绿蘑菇"&&output[0].SourceBounds.Right<output[0].KeepClearBounds.Left,"a no-icon noun glued to a counter no longer loses the whole name");
    p.captureTextRows.Add(GetOcrLineBounds(line));var guard=output[0].KeepClearBounds;
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==1&&p.simpleLayer.Pixels()[(int)guard.Top*1000+(int)guard.Left]==0,"the glued counter remains transparent after the no-icon noun actually composes: "+output[0].FusionFailureReason+" source="+output[0].SourceBounds+" guard="+guard);p.ClearSimpleFusion();
   }
   using(var frame=SkillFrame()){
    var p=JournalForm(real);var rows=new List<OcrLine>{Line("Magic Attack:+55",30,390,220),Line("Critical Rate:+2",30,425,220),Line("Weapon Def.:+36 (40 -4)",30,460,250),Line("REQ INT:40",170,90,140)};var output=new List<OverlayLabel>();
    p.AddStructuredFieldLabels(output,rows,Mapping(rows,"1001"),1);
    check(output.Count==4&&output.Any(x=>x.Text.StartsWith("魔法攻击力"))&&output.Any(x=>x.Text.StartsWith("暴击率")),"explicit equipment fields survive a misleading skill classification and punctuation variations");
    p.captureTextRows.AddRange(rows.Select(GetOcrLineBounds));
    check(p.PrepareSimpleFusionAsync(frame,Rectangle.Empty,output).GetAwaiter().GetResult()&&p.simpleReady.Count==4,"all named equipment fields actually compose together");p.ClearSimpleFusion();
   }
   var anonymous=new TranslationStore(synthetic);
   check(anonymous.TryTranslateSkillEffect("Cobalt Pulse level 4, Critical Rate +6%, Speed +17","9900001",out chinese)&&chinese.Contains("钴蓝脉冲")&&chinese.Contains("+17"),"new named skills combine with generic level,critical and speed fields without new runtime rules");
   check(anonymous.TryTranslateSkillOverview("Strikes up to 9 targets for 37 seconds. Cooldown: 3 sec","9900001",out chinese)&&chinese.Contains("9")&&chinese.Contains("37")&&chinese.Contains("冷却时间")&&!chinese.Contains("12"),"anonymous prose numeric slots and separate cooldowns use observed values rather than a different server snapshot");
   var merged=new OcrLine{Text="Magic Attack +55 Critical Rate +2"};merged.Words.Add(new OcrWord{Text="Magic Attack +55",BoundingRect=new RectangleF(30,100,200,16)});merged.Words.Add(new OcrWord{Text="Critical Rate +2",BoundingRect=new RectangleF(30,130,200,16)});
   check(SkillVisualLines(new List<OcrLine>{merged}).Count==2,"one OCR line containing words on different visual rows is separated using real word geometry");
   Console.WriteLine("Section coexistence: "+n+" checks passed; controlled OCR/pixels/font boundary; Windows game remains unverified.");return n;
  }
 }
 public static class SectionRunner {public static int Run(object real,object synthetic){return OverlayForm.RunSections(real,synthetic);}}
}
'@
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$a=Load-TestAssembly (Join-Path $r 'bin/windows-v4.0/枫语幕.exe')
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$t=$a.GetType('MapleOverlay.TranslationStore',$true)
$real=$t.GetConstructors($f)[0].Invoke([object[]]@([string](Join-Path $r '枫语幕词库.tsv')));$t.GetMethod('Load',$f).Invoke($real,@())|Out-Null
$fixture=Join-Path $work 'anonymous-skill.tsv'
@"
Starweaver`t织星者`t怀旧服-职业#99000
Cobalt Pulse`t钴蓝脉冲`t怀旧服-技能#9900001
[Master Level: 30] Sends a focused wave through nearby targets.`t[最高等级：30] 向附近目标发射集中的冲击波。`t怀旧服-技能说明#9900001
[Master Level: 30] Strikes up to 4 targets for 12 seconds. Cooldown: 5 sec`t[最高等级：30] 最多攻击4个目标，持续12秒。冷却时间：5秒`t怀旧服-技能说明#9900001
"@|Set-Content -LiteralPath $fixture -Encoding utf8
$synthetic=$t.GetConstructors($f)[0].Invoke([object[]]@([string]$fixture));$t.GetMethod('Load',$f).Invoke($synthetic,@())|Out-Null
[IO.File]::WriteAllText((Join-Path $work 'Sections.generated.cs'),$source)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[SectionsReplay.SectionRunner]::Run($real,$synthetic)
