using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // Shared UI/effect vocabulary. Entire known templates win first. This path
    // translates new combinations without enumerating skills, jobs or levels.
    internal static class SkillEffectVocabulary
    {
        private static readonly string[,] Phrases={
            {"Using attack skills has a chance to enable the", "使用攻击技能时有概率启用"},
            {"While enabled, the skill can be used, dealing heavy damage to", "生效期间可发动该技能，造成高额伤害，攻击"},
            {"and absorbing some of the damage dealt as HP", "，并将部分造成的伤害转为HP"},
            {"The most one can absorb at once is", "单次最多吸收相当于自身最大HP的"},
            {"of the character's Max HP", ""},
            {"and can't absorb more than the Max HP of the enemy", "，且不能超过敌人的最大HP"},
            {"Consumes HP and MP to temporarily increase the Attack Speed of the equipped", "消耗HP和MP，暂时提升已装备武器的攻击速度："},
            {"Uses HP and MP to temporarily increase the Attack Speed of the equipped", "消耗HP和MP，暂时提升已装备武器的攻击速度："},
            {"Only applies when equipped with a", "仅在装备以下武器时生效："},
            {"This effect only applies when a", "仅在装备以下武器时生效："},
            {"It only applies when a", "仅在装备以下武器时生效："},
            {"and gives a chance to deal damage that ignores the target's", "，并有概率造成无视目标以下属性的伤害："},
            {"and gives a chance to deal damage that ignores the target’s", "，并有概率造成无视目标以下属性的伤害："},
            {"Temporarily slows down the movement speed and evasion of up to", "暂时降低移动速度和回避率，最多影响"},
            {"Cannot be used on monsters that are already affected", "无法对已受影响的怪物使用"},
            {"Final Attack with damage", "终极攻击伤害"},
            {"Final Attack", "终极攻击"},{"trigger rate", "触发概率"},
            {"chance to ignore enemy Weapon Def", "概率无视敌人物理防御"},
            {"of damage as HP", "伤害转为HP"},{"absorb", "吸收"},
            {"to up to", "，最多攻击"},{"a single enemy in front", "前方单个敌人"},
            {"a single enemy", "单个敌人"},{"in front", "前方"},
            {"one-handed or two-handed", "单手或双手"},
            {"is equipped", ""},{"equipped", ""},{"skill for", "技能，持续"},
            {"blunt weapon", "钝器"},{"crossbow", "弩"},{"polearm", "矛"},
            {"sword", "剑"},{"axe", "斧"},{"spear", "枪"},{"bow", "弓"},
            {"dagger", "短刀"},{"claw", "拳套"},{"knuckle", "指节"},{"gun", "枪械"},
            {"movement speed", "移动速度"},{"level", "等级"},
            {"enemies in front", "个前方敌人"},{"enemies", "个敌人"},{"monsters", "个怪物"},{"up to", "最多"},
            {"HP recovery from the", "HP恢复量，来自"},{"HP recovery from", "HP恢复量，来自"},
            {"all Damage taken is reduced by", "受到的全部伤害降低"},
            {"Damage taken is reduced by", "受到的伤害降低"},
            {"Temporarily reduces all Damage taken", "暂时降低受到的全部伤害"},
            {"for all nearby party members", "，对附近所有队员生效"},
            {"for nearby party members", "，对附近队员生效"},
            {"all nearby party members", "附近所有队员"},
            {"all party members", "所有队员"},
            {"around the area", "附近"},
            {"of", ""},
            {"non-elemental magic damage", "无属性魔法伤害"},
            {"Magic Attack Damage", "魔法攻击伤害"},{"Basic Attack", "基础攻击"},
            {"Physical damage", "物理伤害"},{"Magic damage", "魔法伤害"},
            {"Attack Power", "攻击力"},{"Magic Attack", "魔法攻击力"},
            {"Weapon Attack", "物理攻击力"},{"Weapon Def", "物理防御力"},
            {"Magic Def", "魔法防御力"},{"Mastery level", "熟练度等级"},
            {"Recovery rate", "恢复率"},{"Critical Rate", "暴击率"},
            {"Critical Damage", "暴击伤害"},{"MP consumption", "MP消耗"},
            {"MP cost", "MP消耗"},{"Max HP", "最大HP"},{"Max MP", "最大MP"},
            {"HP recovery", "HP恢复量"},{"HP Recovery", "HP恢复量"},
            {"is increased by", "增加"},{"is reduced by", "降低"},
            {"increased by", "增加"},{"reduced by", "降低"},
            {"Attack Range", "攻击范围"},{"Range", "范围"},
            {"in distance", "距离"},
            {"Cooldown", "冷却时间"},{"Duration", "持续时间"},
            {"Accuracy", "命中率"},{"Evasion", "回避率"},
            {"Avoidability", "回避率"},{"Mastery", "熟练度"},
            {"Damage", "伤害"},{"Speed", "速度"},{"Jump", "跳跃力"},
            {"stun chance", "眩晕概率"},{"success rate", "成功率"},
            {"chance to", "概率"},{"seconds", "秒"},{"second", "秒"},
            {"sec", "秒"},{"minutes", "分钟"},{"minute", "分钟"},{"min", "分钟"},
            {"temporarily", "暂时"},{"increases", "提高"},{"reduces", "降低"},
            {"recovers", "恢复"},{"restores", "恢复"},{"recovery", "恢复"},
            {"with", "，"},{"while active", "，生效期间"},
            {"and", "，"},{"for", "，持续"},{"by", ""},{"the", ""},
            {"skill", "技能"},{"HP", "HP"},{"MP", "MP"}
        };
        private sealed class Word { internal string English,Chinese; internal Regex Pattern; }
        private static readonly List<Word> Vocabulary=Build();
        private static List<Word> Build()
        {
            List<Word> rows=new List<Word>();HashSet<string> keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<Phrases.GetLength(0);i++)if(keys.Add(Phrases[i,0])) {
                string expression=Regex.Escape(Phrases[i,0]).Replace(@"\ ",@"\s*");
                rows.Add(new Word{English=Phrases[i,0],Chinese=Phrases[i,1],Pattern=new Regex(@"\G"+expression+@"(?![A-Za-z])",RegexOptions.IgnoreCase|RegexOptions.Compiled)});
            }
            rows.Sort(delegate(Word a,Word b){return b.English.Length.CompareTo(a.English.Length);});return rows;
        }
        internal static bool TryTranslate(string source,Func<string,string> skillName,out string chinese)
        {
            chinese="";if(String.IsNullOrWhiteSpace(source)||source.Length>4096)return false;
            StringBuilder output=new StringBuilder();int translated=0;
            for(int at=0;at<source.Length;) {
                char c=source[at];
                if(!((c>='A'&&c<='Z')||(c>='a'&&c<='z'))){output.Append(c);at++;continue;}
                Word best=null;int length=0;
                foreach(Word word in Vocabulary){Match match=word.Pattern.Match(source,at);if(match.Success){best=word;length=match.Length;break;}}
                if(best!=null){output.Append(best.Chinese);at+=length;translated++;continue;}
                // A named referenced skill uses the same complete-name index as
                // every other skill. No observed skill name is embedded here.
                string chosen="",name="";
                for(int end=at;end<source.Length&&end-at<60;end++)if(end==source.Length-1||!Char.IsLetter(source[end+1])) {
                    string text=source.Substring(at,end-at+1).Trim();if(!Regex.IsMatch(text,@"^[A-Za-z '’\-]+$"))continue;string value=skillName(text);
                    if(value.Length>0){chosen=text;name=value;}
                }
                if(chosen.Length==0)return false;
                output.Append(name);at+=chosen.Length;translated++;
            }
            if(translated==0)return false;
            chinese=Regex.Replace(output.ToString(),@"\s+"," ").Trim();
            chinese=chinese.Replace(',', '，').Replace(';','；').Replace(':','：');
            chinese=Regex.Replace(chinese,@"(?<!\d)\.(?!\d)","。");
            chinese=Regex.Replace(chinese,@"[，；：。]\s*([，；：。])","$1");
            chinese=Regex.Replace(chinese,@"(?<=[\u4e00-\u9fff])\s+(?=[\u4e00-\u9fff])","");
            return TranslationValueSafety.PreservesNumbers(source,chinese);
        }
    }

    internal sealed partial class TranslationStore
    {
        internal string ResolveSkillTitle(string title)
        {
            List<MatchResult> matches=FindSkillNameMatches(title);if(matches.Count==0)return "";
            // DetectUnique checks same-name cross-job ambiguity as well.
            return DetectSkillId(title);
        }
        private string SkillNameTranslation(string text)
        {
            List<MatchResult> matches=FindSkillNameMatches(text);return matches.Count>0?matches[0].Entry.Chinese:"";
        }
        internal bool TryTranslateEffectVocabulary(string text,out string chinese)
        {return SkillEffectVocabulary.TryTranslate(text,SkillNameTranslation,out chinese);}
        internal bool TryGetSkillOverviewBody(string skillId,out string chinese)
        {
            chinese="";TranslationEntry best=null;
            foreach(TranslationEntry entry in skillTextEntries)if(CategoryId(entry.Category)==skillId&&
                Regex.IsMatch(entry.English,@"^\s*\[?Master\s+Level",RegexOptions.IgnoreCase)&&
                (best==null||entry.English.Length>best.English.Length))best=entry;
            if(best==null)foreach(TranslationEntry entry in skillTextEntries)if(CategoryId(entry.Category)==skillId&&
                !Regex.IsMatch(entry.English,@"^\s*(?:HP|MP)\b",RegexOptions.IgnoreCase)&&
                entry.English.Length>=30&&(best==null||entry.English.Length>best.English.Length))best=entry;
            if(best==null)return false;
            chinese=Regex.Replace(best.Chinese,@"^\s*[\[【]?\s*(?:最高等级|最大等级)\s*[:：]?\s*\d+\s*[\]】]?\s*","");
            chinese=Regex.Split(chinese,@"(?:前置技能|需要技能)\s*[:：]")[0].Trim();return chinese.Length>0;
        }
        // Prefer the glossary's complete overview only when the visible body is
        // demonstrably the same wording and every visible number survives. This
        // keeps a known skill's polished description without allowing a partial
        // tail or another server's values to be substituted.
        internal bool TryGetVisibleSkillOverviewBody(string observed,string skillId,
            out string chinese,out float similarity)
        {
            chinese="";similarity=0;
            if(String.IsNullOrWhiteSpace(observed)||String.IsNullOrEmpty(skillId))return false;
            TranslationEntry overview=GetSkillOverview(skillId);
            if(overview==null)return false;
            string overviewEnglish=OverviewEnglish(overview.English);
            if(overviewEnglish.Length<18)return false;
            similarity=DetailTextSimilarity(observed,overviewEnglish);
            if(similarity<.55f)return false;
            chinese=OverviewChinese(overview.Chinese);
            if(chinese.Length==0)return false;
            string unsafeReason;
            if(!SkillOverviewSafety.IsSafeOverview(observed,overviewEnglish,chinese,false,
                out unsafeReason))return false;
            return true;
        }
        private static string OverviewEnglish(string text)
        {
            text=Regex.Replace(text??"",@"^\s*\[?Master\s+Level\s*:?\s*\d+\]?\s*","",RegexOptions.IgnoreCase);
            text=Regex.Split(text,@"\bRequired\s+Skill\s*:",RegexOptions.IgnoreCase)[0].Trim();
            text=Regex.Replace(text,@"\s*Cooldown\s*:\s*[+\-]?\d+(?:\.\d+)?\s*(?:sec(?:ond)?s?|min(?:ute)?s?)\.?\s*$","",RegexOptions.IgnoreCase).Trim();
            return Regex.Replace(text,@"\bcertain\s+trigger\s+rate\b","certain success rate",RegexOptions.IgnoreCase);
        }
        private static string OverviewChinese(string text)
        {
            text=Regex.Replace(text??"",@"^\s*[\[【]?\s*(?:最高等级|最大等级)\s*[:：]?\s*\d+\s*[\]】]?\s*","");
            text=Regex.Split(text,@"(?:前置技能|需要技能)\s*[:：]")[0].Trim();
            return Regex.Replace(text,@"\s*冷却时间\s*[:：]\s*\d+(?:\.\d+)?\s*(?:秒|分钟)\s*[。.]?\s*$","").Trim();
        }
        private static readonly string[] OverviewPropertyPatterns={@"critical\s*rate",@"critical\s*damage",@"accuracy",@"evasion|avoidability",@"(?:movement\s*)?speed",@"jump",@"attack\s*power",@"weapon\s*def(?:ense)?",@"magic\s*(?:attack|defense)",@"ignore|ignores",@"bleed|bleeding",@"recover|recovers|recovery|regenerate|regenerates",@"poison",@"burn|burning"};
        private static readonly Regex[] OverviewPropertyRules=MakeOverviewPropertyRules();
        private static Regex[] MakeOverviewPropertyRules(){List<Regex> rules=new List<Regex>();foreach(string pattern in OverviewPropertyPatterns)rules.Add(new Regex(@"\b(?:"+pattern+@")\b",RegexOptions.IgnoreCase|RegexOptions.Compiled));return rules.ToArray();}
        private static string OverviewProperties(string text)
        {
            StringBuilder result=new StringBuilder();foreach(Regex rule in OverviewPropertyRules)result.Append(rule.IsMatch(text??"")?'1':'0');return result.ToString();
        }
        private sealed class SkillOverviewEntry
        {internal string Id,English,Chinese,Key,Properties;internal NumericTextTemplates Template;}
        private readonly List<SkillOverviewEntry> skillOverviews=new List<SkillOverviewEntry>();
        private void BuildSkillOverviewEntries()
        {
            skillOverviews.Clear();HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(TranslationEntry entry in skillTextEntries){string english=OverviewEnglish(entry.English),body=OverviewChinese(entry.Chinese),key=Normalize(english);
                if(key.Length<8||Regex.IsMatch(english,@"^\s*(?:HP|MP)\s*[+\-−]?\s*\d",RegexOptions.IgnoreCase))continue;
                string id=CategoryId(entry.Category),unique=id+"\n"+key+"\n"+body;if(!seen.Add(unique))continue;
                NumericTextTemplates template=new NumericTextTemplates();template.Add(english,body);
                skillOverviews.Add(new SkillOverviewEntry{Id=id,English=english,Chinese=body,Key=key,Properties=OverviewProperties(english),Template=template});
            }
        }
        internal bool TryTranslateSkillOverview(string observed,string skillId,out string chinese)
        {
            chinese="";if(String.IsNullOrWhiteSpace(observed)||observed.Length>4096)return false;
            if(TryTranslateEffectVocabulary(observed,out chinese))return true;
            // A separate visible cooldown is an additional fact, not grounds
            // for dropping the whole known introduction or inventing a duration.
            Match cooldown=Regex.Match(observed,@"\s*Cooldown\s*:\s*[+\-]?\d+(?:\.\d+)?\s*(?:sec(?:ond)?s?|min(?:ute)?s?)\.?\s*$",RegexOptions.IgnoreCase);
            string suffix="",main=observed;
            if(cooldown.Success){if(!TryTranslateEffectVocabulary(cooldown.Value,out suffix))return false;main=observed.Substring(0,cooldown.Index).Trim();}
            string key=Normalize(OverviewEnglish(main));if(key.Length<8)return false;
            string best="";float highest=0;string properties=OverviewProperties(main);
            foreach(SkillOverviewEntry entry in skillOverviews) {
                if(!String.IsNullOrEmpty(skillId)&&entry.Id!=skillId)continue;
                string english=entry.English,body=entry.Chinese,candidate=entry.Key;
                string translated="";float score;bool templateUsed=false;
                if(key==candidate){translated=body;score=1;}
                else {
                    // Add requires identical source/target numbers in order.
                    // The nonnumeric wording must then match exactly. All new
                    // values come from the observed text, for all skill IDs.
                    if(entry.Template.TryTranslate(OverviewEnglish(main),out translated)){score=1;templateUsed=true;}
                    else {
                        if(key.Length<18||candidate.Length<18||properties!=entry.Properties)continue;
                        score=DetailTextSimilarity(main,candidate);if(score<.72f)continue;
                        if(Regex.IsMatch(main,@"\d")&&!SkillOverviewSafety.PreservesVisibleNumbers(main,english))continue;
                        translated=body;
                    }
                }
                // Identity and numbers alone are not enough. Negation, conditions,
                // attributes and visible source coverage must also match before a
                // dictionary sentence may replace the observed sentence.
                string unsafeReason;
                if(!SkillOverviewSafety.IsSafeOverview(OverviewEnglish(main),english,translated,
                    templateUsed,out unsafeReason))continue;
                if(score<=highest)continue;highest=score;best=translated;
            }
            if(best.Length==0)return false;
            chinese=best+(suffix.Length>0?"　"+suffix:"");return true;
        }
    }
}
