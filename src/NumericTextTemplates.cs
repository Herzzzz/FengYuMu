using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // One grammar for every skill/level. The dictionary supplies wording; only
    // the observed numeric tokens supply live costs, percentages and duration.
    internal sealed class NumericTextTemplates
    {
        private sealed class Pattern
        {
            internal string[] Parts;
            internal bool Ambiguous;
        }
        private readonly Dictionary<string,Pattern> patterns=new Dictionary<string,Pattern>(StringComparer.Ordinal);
        private static readonly Regex Numbers=new Regex(@"[+\-\u2212]?\s*\d+(?:\.\d+)?\s*%?",RegexOptions.Compiled);

        internal void Add(string english,string chinese)
        {
            MatchCollection source=Numbers.Matches(english ?? ""),target=Numbers.Matches(chinese ?? "");
            if(source.Count==0 || source.Count!=target.Count)return;
            for(int i=0;i<source.Count;i++)if(Value(source[i].Value)!=Value(target[i].Value))return;
            string key=Key(english);if(key.Length<8)return;
            string[] parts=new string[target.Count+1];int previous=0;
            for(int i=0;i<target.Count;i++){parts[i]=chinese.Substring(previous,target[i].Index-previous);previous=target[i].Index+target[i].Length;}
            parts[parts.Length-1]=chinese.Substring(previous);
            Pattern existing;
            if(patterns.TryGetValue(key,out existing)) {
                if(String.Join("\u001f",existing.Parts)!=String.Join("\u001f",parts))existing.Ambiguous=true;
            } else patterns.Add(key,new Pattern{Parts=parts});
        }

        internal bool TryTranslate(string observed,out string chinese)
        {
            chinese="";if(String.IsNullOrWhiteSpace(observed)||observed.Length>4096)return false;
            Pattern pattern;if(!patterns.TryGetValue(Key(observed),out pattern)||pattern.Ambiguous)return false;
            MatchCollection numbers=Numbers.Matches(observed);if(numbers.Count+1!=pattern.Parts.Length)return false;
            StringBuilder result=new StringBuilder(pattern.Parts[0]);
            for(int i=0;i<numbers.Count;i++)result.Append(numbers[i].Value.Trim()).Append(pattern.Parts[i+1]);
            chinese=result.ToString();return true;
        }

        private static string Key(string text)
        {return TranslationStore.Normalize(Numbers.Replace(text ?? ""," # ")).Replace(" ","");}
        private static string Value(string text)
        {return Regex.Replace(text,@"\s+","").Replace('\u2212','-');}
    }

    internal sealed partial class TranslationStore
    {
        private NumericTextTemplates skillEffectTemplates=new NumericTextTemplates();
        private readonly Dictionary<string,List<TranslationEntry>> skillEffectExact=new Dictionary<string,List<TranslationEntry>>(StringComparer.Ordinal);
        private void BuildSkillEffectTemplates()
        {
            skillEffectTemplates=new NumericTextTemplates();skillEffectExact.Clear();
            foreach(TranslationEntry entry in skillTextEntries) {
                skillEffectTemplates.Add(entry.English,entry.Chinese);List<TranslationEntry> exact;
                if(!skillEffectExact.TryGetValue(entry.Normalized,out exact)){exact=new List<TranslationEntry>();skillEffectExact.Add(entry.Normalized,exact);}
                exact.Add(entry);
            }
            BuildSkillOverviewEntries();
        }
        internal bool TryTranslateSkillEffect(string observed,string skillId,out string chinese)
        {
            if(skillEffectTemplates.TryTranslate(observed,out chinese))return true;
            string key=Normalize(observed);string found=null;List<TranslationEntry> entries;
            if(!skillEffectExact.TryGetValue(key,out entries))return TryTranslateEffectVocabulary(observed,out chinese);
            foreach(TranslationEntry entry in entries) {
                if(!String.IsNullOrEmpty(skillId)&&CategoryId(entry.Category)!=skillId)continue;
                if(!TranslationValueSafety.PreservesNumbers(observed,entry.English))continue;
                if(found!=null && found!=entry.Chinese){chinese="";return false;}
                found=entry.Chinese;
            }
            if(found!=null){chinese=found;return true;}
            return TryTranslateEffectVocabulary(observed,out chinese);
        }
    }
}
