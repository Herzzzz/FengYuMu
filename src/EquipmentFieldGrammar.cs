using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class EquipmentField
    {
        internal int Start,Length,ValueStart,ValueLength;
        internal string Key,Chinese;
    }

    internal static class EquipmentFieldGrammar
    {
        private static readonly string[,] Fields={
            {"weapon-attack",@"(?:weapon\s*(?:attack|att|atk)|w\s*[.]?\s*(?:att|atk)|attack\s*power)","物理攻击力"},
            {"magic-attack",@"(?:mag[il1i]c\s*(?:attack|att|atk)|m\s*[.]?\s*(?:att|atk))","魔法攻击力"},
            {"weapon-defense",@"(?:weapon\s*def(?:en[cs]e)?|w\s*[.]?\s*def)","物理防御力"},
            {"magic-defense",@"(?:mag[il1i]c\s*def(?:en[cs]e)?|m\s*[.]?\s*def)","魔法防御力"},
            {"strength",@"(?:str(?:ength)?)","力量"},
            {"dexterity",@"(?:dex(?:terity)?|oex|0ex)","敏捷"},
            {"intelligence",@"(?:int(?:elligence)?|lnt|1nt)","智力"},
            {"luck",@"(?:luk|luck)","运气"},
            {"accuracy",@"accuracy","命中率"},
            {"avoidability",@"(?:avoidability|avoidance)","回避率"},
            {"speed",@"(?:movement\s*speed|speed)","移动速度"},
            {"jump",@"jump","跳跃力"},
            {"hp",@"(?:max(?:imum)?\s*hp|hp)","生命值"},
            {"mp",@"(?:max(?:imum)?\s*mp|mp)","魔法值"},
            {"level",@"(?:lev(?:el)?|lv)","等级"},
            {"fame",@"fam(?:e)?","人气"},
            {"upgrades",@"(?:remaining\s*enhancements|upgrades\s*available)","剩余强化次数"},
            {"critical-rate",@"cr[il1]t[il1]cal\s*(?:rate|chance)","暴击率"},
            {"critical-damage",@"cr[il1]t[il1]cal\s*(?:damage|dmg)","暴击伤害"},
            {"boss-damage",@"boss\s*(?:damage|dmg)","首领伤害"},
            {"damage",@"(?:total\s*)?damage", "伤害"},
            {"ignore-defense",@"(?:ignore\s*(?:enemy\s*)?def(?:en[cs]e)?)","无视防御"}
        };
        internal static List<EquipmentField> Parse(string text,bool equipment)
        {
            List<EquipmentField> result=new List<EquipmentField>();
            if(String.IsNullOrWhiteSpace(text))return result;
            foreach(string key in new string[]{"master level","current level","next level","physical damage","magic damage"})
                if(text.IndexOf(key,StringComparison.OrdinalIgnoreCase)>=0)return result;
            for(int i=0;i<Fields.GetLength(0);i++) {
                string expression=@"(?<![A-Za-z])(?<req>(?:req(?:uired)?\s*[.:]?\s*)?)(?<name>"+Fields[i,1]+@")(?![A-Za-z])";
                foreach(Match match in Regex.Matches(text,expression,RegexOptions.IgnoreCase)) {
                    int after=match.Index+match.Length;
                    Match value=Regex.Match(text.Substring(after),@"^\s*[.:：]*\s*(?<value>[+\-\u2212]?[\d]+(?:\.\d+)?\s*%?(?:\s*\([^\r\n)]*\))?)");
                    bool required=match.Groups["req"].Length>0;
                    bool explicitStat=i<=7 || required || i>=16;
                    if(!value.Success && (!equipment || Regex.IsMatch(text.Substring(after),@"[A-Za-z]")))continue;
                    if(!equipment && !explicitStat && !Regex.IsMatch(text.Substring(after),@"^\s*[:：]\s*[+\-]?\d"))continue;
                    if(i==14 && !required)continue;
                    // Non-tooltip sentences must not turn a short stat token into a field.
                    if(!equipment && value.Success && Regex.IsMatch(text.Substring(after+value.Length),@"[A-Za-z]"))continue;
                    result.Add(new EquipmentField{Start=match.Index,Length=match.Length,Key=Fields[i,0],
                        Chinese=(required?"需要":"")+Fields[i,2],
                        ValueStart=value.Success?after+value.Groups["value"].Index:after,
                        ValueLength=value.Success?value.Groups["value"].Length:0});
                }
            }
            result.Sort(delegate(EquipmentField a,EquipmentField b){return a.Start.CompareTo(b.Start);});
            // Longest aliases win over an inner HP/MP or Speed match.
            for(int i=result.Count-1;i>0;i--)if(result[i].Start<result[i-1].Start+result[i-1].Length)result.RemoveAt(i);
            return result;
        }
    }
}
