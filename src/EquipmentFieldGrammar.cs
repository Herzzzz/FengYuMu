using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class EquipmentField
    {
        internal int Start,Length,ValueStart,ValueLength;
        internal string Key,Chinese;
        // The raw OCR substring is kept byte-for-byte. Painting replaces only the
        // translated key and leaves this value (and therefore sign, %, parenthesis
        // correction and coloured pixels) untouched.
        internal string RawValue;
        internal bool HasValue,Required,TextValue,StrongContext;
    }

    internal static class EquipmentFieldGrammar
    {
        private sealed class FieldDefinition
        {
            internal string Key,Pattern,Chinese;
            internal bool Explicit,TextValue;
            internal FieldDefinition(string key,string pattern,string chinese,bool explicitStat,bool textValue)
            {Key=key;Pattern=pattern;Chinese=chinese;Explicit=explicitStat;TextValue=textValue;}
        }

        // Every entry is a generic attribute shape. No item name, item id, class,
        // player or server value participates in matching.
        private static readonly FieldDefinition[] Fields={
            new FieldDefinition("weapon-attack",@"(?:weapon\s*(?:attack|att|atk)|w\s*[.]?\s*(?:att|atk|attack)|(?:attack|att|atk)\s*power|att|atk)","物理攻击力",true,false),
            new FieldDefinition("magic-attack",@"(?:mag[il1i]c\s*(?:attack|att|atk)|m\s*[.]?\s*(?:att|atk|attack))","魔法攻击力",true,false),
            new FieldDefinition("weapon-defense",@"(?:weapon\s*def(?:en[cs]e)?|w\s*[.]?\s*def(?:en[cs]e)?|wdef)","物理防御力",true,false),
            new FieldDefinition("magic-defense",@"(?:mag[il1i]c\s*def(?:en[cs]e)?|m\s*[.]?\s*def(?:en[cs]e)?|mdef)","魔法防御力",true,false),
            new FieldDefinition("strength",@"(?:str(?:ength)?)","力量",true,false),
            new FieldDefinition("dexterity",@"(?:dex(?:terity)?|oex|0ex)","敏捷",true,false),
            new FieldDefinition("intelligence",@"(?:int(?:elligence)?|lnt|1nt)","智力",true,false),
            new FieldDefinition("luck",@"(?:luk|luck)","运气",true,false),
            new FieldDefinition("accuracy",@"accuracy","命中率",false,false),
            // Evasion is the visible tooltip/stat wording for the same generic
            // avoidability attribute. It is an alias, not a new special case.
            new FieldDefinition("avoidability",@"(?:avoidability|avoidance|evasion|eva)","回避率",false,false),
            new FieldDefinition("attack-speed",@"(?:attack|weapon|atk|att)\s*speed","攻击速度",true,true),
            new FieldDefinition("speed",@"(?:movement\s*speed|move\s*speed|speed)","移动速度",false,false),
            new FieldDefinition("jump",@"jump","跳跃力",false,false),
            new FieldDefinition("hp",@"(?:max(?:imum)?\s*hp|hp)","生命值",false,false),
            new FieldDefinition("mp",@"(?:max(?:imum)?\s*mp|mp)","魔法值",false,false),
            new FieldDefinition("level",@"(?:lev(?:el)?|lv)","等级",false,false),
            new FieldDefinition("fame",@"fam(?:e)?","人气",false,false),
            new FieldDefinition("upgrades",@"(?:remaining\s*enhancements?|upgrades?\s*available|upgrade\s*slots?|remaining\s*upgrades?|slots?\s*remaining)","剩余强化次数",true,false),
            new FieldDefinition("critical-rate",@"(?:crit(?:ical)?(?:\s*(?:rate|chance))?)","暴击率",true,false),
            new FieldDefinition("critical-damage",@"(?:crit(?:ical)?\s*(?:damage|dmg))","暴击伤害",true,false),
            new FieldDefinition("boss-damage",@"boss\s*(?:damage|dmg)","首领伤害",true,false),
            new FieldDefinition("damage",@"(?:total\s*)?damage","伤害",true,false),
            new FieldDefinition("ignore-defense",@"(?:ignore\s*(?:enemy\s*)?def(?:en[cs]e)?)","无视防御",true,false)
        };

        private const string NumberToken=@"(?=[0-9OoIlSsBbZzGq,]*[0-9])[0-9OoIlSsBbZzGq][0-9OoIlSsBbZzGq,]*";
        private const string NumericValue=@"(?<value>[+\-\u2212\uFF0B\uFF0D]?\s*"+NumberToken+@"(?:\s*\.\s*[0-9]+)?\s*%?(?:\s*\([^()\r\n]*\))*(?:\s*%)?)";
        private const string TextValue=@"(?<value>(?:very\s+)?fast(?:est)?|(?:very\s+)?slow(?:est)?|normal|average|standard)";

        internal static List<EquipmentField> Parse(string text,bool equipment)
        {
            List<EquipmentField> result=new List<EquipmentField>();
            if(String.IsNullOrWhiteSpace(text))return result;
            foreach(string key in new string[]{"master level","current level","next level","physical damage","magic damage"})
                if(text.IndexOf(key,StringComparison.OrdinalIgnoreCase)>=0)return result;

            for(int i=0;i<Fields.Length;i++) {
                FieldDefinition definition=Fields[i];
                string expression=@"(?<![A-Za-z])(?<req>(?:req(?:uired)?\s*[.:]?\s*)?)(?<name>"+
                    definition.Pattern+@")(?![A-Za-z])";
                foreach(Match match in Regex.Matches(text,expression,RegexOptions.IgnoreCase)) {
                    int after=match.Index+match.Length;
                    bool required=match.Groups["req"].Length>0;
                    if(definition.Key=="level"&&!required)continue;
                    string tail=text.Substring(after);
                    Match value=MatchValue(tail,definition);
                    bool hasValue=value!=null&&value.Success;
                    if(!hasValue) {
                        // A key followed only by punctuation is a partial OCR row and
                        // still yields a safe key-only field. Letters/digits after the
                        // key are prose, not an equipment value, so those are skipped.
                        if(!equipment||Regex.IsMatch(tail,@"[A-Za-z0-9]"))continue;
                    }
                    bool hasPunctuation=hasValue&&Regex.IsMatch(tail.Substring(0,
                        value.Groups["value"].Index),@"[.:：]");
                    int leading=text.Length-text.TrimStart().Length;
                    bool leadingSigned=hasValue&&match.Index==leading&&Regex.IsMatch(
                        value.Groups["value"].Value.Trim(),@"^[+\-\u2212\uFF0B\uFF0D]");
                    EquipmentField field=new EquipmentField{
                        Start=match.Index,Length=match.Length,Key=definition.Key,
                        Chinese=(required?"需要":"")+definition.Chinese,
                        Required=required,TextValue=definition.TextValue,
                        StrongContext=definition.Explicit||required||hasPunctuation||leadingSigned};
                    if(hasValue) {
                        int start=after+value.Groups["value"].Index;
                        int length=value.Groups["value"].Length;
                        while(length>0&&Char.IsWhiteSpace(text[start+length-1]))length--;
                        if(length>0) {
                            field.ValueStart=start;field.ValueLength=length;
                            field.RawValue=text.Substring(start,length);field.HasValue=true;
                        }
                    }
                    result.Add(field);
                }
            }

            // Several aliases can overlap (Critical Damage/Damage, Attack Speed/Speed,
            // MaxHP/HP). Keep the longest field at a given position, never an inner
            // duplicate. This is a grammar rule, not an equipment-name exception.
            result.Sort(delegate(EquipmentField a,EquipmentField b){
                int start=a.Start.CompareTo(b.Start);
                if(start!=0)return start;
                return b.Length.CompareTo(a.Length);});
            List<EquipmentField> distinct=new List<EquipmentField>();
            foreach(EquipmentField candidate in result) {
                bool overlap=false;
                foreach(EquipmentField kept in distinct)
                    if(candidate.Start<kept.Start+kept.Length&&kept.Start<candidate.Start+candidate.Length)
                    {overlap=true;break;}
                if(!overlap)distinct.Add(candidate);
            }
            // A row made only of field spans may use signed no-colon forms such as
            // "Speed +3 Evasion +4". If prose remains outside the spans, only fields
            // with an explicit/colon/signed context survive this guard.
            if(!equipment&&!AllFieldsConsumed(text,distinct))
                distinct.RemoveAll(delegate(EquipmentField field){return !field.StrongContext;});
            // A field followed by ordinary prose is not a tooltip row. Another field
            // after a punctuation/space boundary is allowed: combined OCR rows carry
            // Weapon Def + Magic Def (or REQ LEV + REQ STR + ...) on one line.
            if(!equipment) {
                for(int i=distinct.Count-1;i>=0;i--) {
                    EquipmentField field=distinct[i];
                    if(!field.HasValue)continue;
                    string tail=text.Substring(field.ValueStart+field.ValueLength);
                    if(Regex.IsMatch(tail,@"[A-Za-z]")&&!HasKnownFieldStart(tail))distinct.RemoveAt(i);
                }
            }
            return distinct;
        }

        private static Match MatchValue(string tail,FieldDefinition definition)
        {
            if(definition.TextValue) {
                Match textValue=Regex.Match(tail,@"^\s*[.:：]*\s*"+TextValue,RegexOptions.IgnoreCase);
                if(textValue.Success)return textValue;
            }
            return Regex.Match(tail,@"^\s*[.:：]*\s*"+NumericValue,RegexOptions.IgnoreCase);
        }

        // True only when a complete known field starts immediately after separators.
        // Prose such as "and Strength +3" therefore cannot make a preceding sentence
        // look like two adjacent tooltip columns.
        private static bool HasKnownFieldStart(string text)
        {
            if(String.IsNullOrEmpty(text))return false;
            string trimmed=Regex.Replace(text,@"^[\s,;/|·•]+","");
            if(trimmed.Length==0)return false;
            for(int i=0;i<Fields.Length;i++) {
                string expression=@"^(?<req>(?:req(?:uired)?\s*[.:]?\s*)?)(?<name>"+
                    Fields[i].Pattern+@")(?![A-Za-z])";
                if(Regex.IsMatch(trimmed,expression,RegexOptions.IgnoreCase))return true;
            }
            return false;
        }

        // A single-line fallback for panels that OCR never grouped. It requires a
        // key/value shape or an explicit equipment anchor, so ordinary prose that
        // merely contains "intelligence" is not painted as a tooltip field.
        internal static bool LooksLikeEquipmentRow(string text)
        {
            if(String.IsNullOrWhiteSpace(text))return false;
            List<EquipmentField> fields=Parse(text,true);
            if(fields.Count==0)return false;
            bool required=false,upgrade=false,attackSpeed=false,colon=false,combatSigned=false,defense=false;
            bool leadingSigned=false,trailingProse=false;
            bool weaponDefense=false,weaponAttack=false,magicDefense=false,magicAttack=false;
            int leading=text.Length-text.TrimStart().Length;
            foreach(EquipmentField field in fields) {
                if(field.Required)required=true;
                if(field.Key=="upgrades")upgrade=true;
                if(field.Key=="attack-speed"&&field.HasValue)attackSpeed=true;
                if(field.Key=="weapon-defense")weaponDefense=true;
                if(field.Key=="magic-defense")magicDefense=true;
                if(field.Key=="weapon-attack")weaponAttack=true;
                if(field.Key=="magic-attack")magicAttack=true;
                if(field.Key=="weapon-defense"||field.Key=="magic-defense"||
                    field.Key=="weapon-attack"||field.Key=="magic-attack")defense=true;
                string afterName=text.Substring(field.Start+field.Length);
                if(Regex.IsMatch(afterName,@"^\s*[.:：]"))colon=true;
                bool signed=field.HasValue&&Regex.IsMatch(field.RawValue??"",
                    @"^[+\-\u2212\uFF0B\uFF0D]");
                if(signed)combatSigned=true;
                string tailAfterValue=field.HasValue
                    ? text.Substring(field.ValueStart+field.ValueLength) : "";
                bool tailIsKnown=!Regex.IsMatch(tailAfterValue,@"[A-Za-z]")||
                    HasKnownFieldStart(tailAfterValue);
                if(!tailIsKnown)trailingProse=true;
                if(signed&&field.Start==leading&&tailIsKnown)leadingSigned=true;
            }
            if(trailingProse)return false;
            bool type=Regex.IsMatch(text,@"(?i)\b(?:type|category)\s*[.:：]");
            if(required||upgrade||attackSpeed||(weaponDefense&&magicDefense)||
                (weaponAttack&&(weaponDefense||magicDefense))||
                (magicAttack&&(weaponDefense||magicDefense))||(type&&fields.Count>=1)||colon||
                leadingSigned)return true;
            if(combatSigned&&defense&&fields.Count==1&&
                Regex.IsMatch(text.TrimStart(),@"^(?:weapon|magic|w\s*[.]|m\s*[.]|att|atk|evasion|avoidability|speed)",
                    RegexOptions.IgnoreCase))return true;
            return false;
        }

        internal static bool LooksLikeEquipmentStructure(string text)
        {
            if(String.IsNullOrWhiteSpace(text))return false;
            string lower=text.ToLowerInvariant();
            if(lower.Contains("master level")||lower.Contains("current level")||
                lower.Contains("next level"))return false;
            List<EquipmentField> fields=Parse(text,true);
            if(fields.Count==0)return false;
            bool req=false,upgrade=false,attackSpeed=false,weaponDefense=false,magicDefense=false;
            bool weaponAttack=false,magicAttack=false;
            foreach(EquipmentField field in fields) {
                if(field.Required||field.Key=="level")req=req||field.Required;
                if(field.Key=="upgrades")upgrade=true;
                if(field.Key=="attack-speed"&&field.HasValue)attackSpeed=true;
                if(field.Key=="weapon-defense")weaponDefense=true;
                if(field.Key=="magic-defense")magicDefense=true;
                if(field.Key=="weapon-attack")weaponAttack=true;
                if(field.Key=="magic-attack")magicAttack=true;
            }
            bool type=Regex.IsMatch(text,@"(?i)\b(?:type|category)\s*[.:：]");
            return req||upgrade||attackSpeed||(weaponDefense&&magicDefense)||
                (weaponAttack&&(weaponDefense||magicDefense))||
                (magicAttack&&(weaponDefense||magicDefense))||(type&&fields.Count>=1)||
                AllFieldsConsumed(text,fields);
        }

        private static bool AllFieldsConsumed(string text,List<EquipmentField> fields)
        {
            if(String.IsNullOrEmpty(text)||fields==null||fields.Count==0)return false;
            char[] separators=new char[]{' ','\t','\r','\n','.',':','：','/','\\','|',',',';',
                '·','•','-','_'};
            int cursor=0;
            foreach(EquipmentField field in fields) {
                if(field.Start<cursor)return false;
                if(field.Start>cursor) {
                    string gap=text.Substring(cursor,field.Start-cursor);
                    if(gap.Trim(separators).Length>0)return false;
                }
                cursor=field.Start+field.Length;
                if(field.HasValue&&field.ValueLength>0)
                    cursor=field.ValueStart+field.ValueLength;
            }
            return text.Substring(cursor).Trim(separators).Length==0;
        }
    }
}
