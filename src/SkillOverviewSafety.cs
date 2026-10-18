using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // Visible-overview safety is shared by every dictionary overview path and the
    // final fallback. Identity alone is never enough: the visible words, negation,
    // conditions, attributes and ordered numbers must all be represented by the
    // candidate that supplies the Chinese text.
    internal static class SkillOverviewSafety
    {
        private static readonly Regex NumberPattern = new Regex(
            @"[+\-\u2212]?\s*\d+(?:\.\d+)?\s*%?", RegexOptions.Compiled);
        private static readonly string[] NegationTokens = new string[] {
            "never", "not", "no", "cannot", "can't", "cant", "won't", "wont",
            "without", "unable", "isn't", "isnt", "doesn't", "doesnt", "don't",
            "dont", "nor", "neither", "except", "excluding", "immune", "prevents",
            "prevent", "stops", "stop"
        };
        private static readonly string[] ConditionalTokens = new string[] {
            "if", "when", "while", "unless", "after", "before", "during"
        };
        private static readonly string[] RestrictionTokens = new string[] {
            "only", "requires", "required", "equipped", "equip", "chance", "rate",
            "success", "trigger", "random", "cooldown", "duration"
        };
        private static readonly string[] AttributeTokens = new string[] {
            "hp", "mp", "damage", "attack", "defense", "defence", "accuracy",
            "evasion", "avoidability", "speed", "jump", "recovery", "recover",
            "heal", "critical", "mastery", "range", "stun", "poison", "freeze",
            "burn", "element", "weapon", "magic", "physical"
        };
        private static readonly HashSet<string> StopWords = new HashSet<string>(
            new string[] { "a", "an", "the", "of", "for", "to", "in", "on", "with",
                "and", "or", "is", "are", "be", "by", "at", "from", "this", "that",
                "it", "its", "as", "up", "down", "all", "any", "some", "user",
                "can", "will", "has", "have", "into", "their", "they", "them",
                "character", "skill", "effect", "level", "sec", "second", "seconds",
                "min", "minute", "minutes" }, StringComparer.Ordinal);

        internal static bool IsSafeOverview(string observed, string candidateEnglish,
            string translated, bool templateSubstitution, out string reason)
        {
            reason = "";
            string source = TranslationStore.Normalize(observed ?? "");
            string candidate = TranslationStore.Normalize(candidateEnglish ?? "");
            string target = TranslationStore.Normalize(translated ?? "");
            if (source.Length == 0 || candidate.Length == 0 || target.Length == 0)
            { reason = "empty-source"; return false; }

            // Negation and condition classes must survive unchanged. A missing or
            // extra marker changes the meaning even when the remaining words match.
            int sourceNegations = CountTokens(source, NegationTokens);
            int candidateNegations = CountTokens(candidate, NegationTokens);
            if (sourceNegations != candidateNegations)
            { reason = sourceNegations > candidateNegations ? "negation-missing" : "negation-extra"; return false; }
            if (sourceNegations > 0 && !ContainsChineseNegation(target))
            { reason = "negation-translation-missing"; return false; }

            bool sourceConditional = ContainsToken(source, ConditionalTokens);
            bool candidateConditional = ContainsToken(candidate, ConditionalTokens);
            if (sourceConditional != candidateConditional)
            { reason = sourceConditional ? "condition-missing" : "condition-extra"; return false; }
            bool sourceRestriction = ContainsToken(source, RestrictionTokens);
            bool candidateRestriction = ContainsToken(candidate, RestrictionTokens);
            if (sourceRestriction != candidateRestriction)
            { reason = sourceRestriction ? "restriction-missing" : "restriction-extra"; return false; }

            string sourceAttributes = AttributeSignature(source);
            string candidateAttributes = AttributeSignature(candidate);
            if (!String.Equals(sourceAttributes, candidateAttributes, StringComparison.Ordinal))
            { reason = "attribute-set-mismatch"; return false; }

            // Numeric checking is stricter for a borrowed sentence and more
            // tolerant for an exact dictionary sentence. An exact English line can
            // legitimately reorder clauses in Chinese; a template substitution or
            // approximate candidate cannot.
            bool exactCandidate = String.Equals(source, candidate, StringComparison.Ordinal);
            if (!PreservesVisibleNumbers(source, target, exactCandidate))
            { reason = "numeric-order-mismatch"; return false; }
            if (!templateSubstitution && !PreservesVisibleNumbers(source, candidate, exactCandidate))
            { reason = "numeric-candidate-mismatch"; return false; }

            string sourceWords = NumberPattern.Replace(source, " # ");
            string candidateWords = NumberPattern.Replace(candidate, " # ");
            HashSet<string> sourceSignificant = SignificantWords(sourceWords);
            HashSet<string> candidateSignificant = SignificantWords(candidateWords);
            if (sourceSignificant.Count == 0) { reason = "no-significant-source"; return false; }
            int common = 0;
            foreach (string word in sourceSignificant) if (candidateSignificant.Contains(word)) common++;
            float coverage = (float)common / sourceSignificant.Count;
            if (coverage < 0.72f) { reason = "source-coverage-low"; return false; }
            if (candidateSignificant.Count > 0)
            {
                int reverse = 0;
                foreach (string word in candidateSignificant) if (sourceSignificant.Contains(word)) reverse++;
                float reverseCoverage = (float)reverse / candidateSignificant.Count;
                if (reverseCoverage < 0.58f) { reason = "candidate-overreach"; return false; }
            }
            if (TranslationStore.DetailTextSimilarity(source, candidate) < 0.68f)
            { reason = "sequence-similarity-low"; return false; }
            return true;
        }

        // Ordinal words such as "4th" are routinely localized as 第四. They are
        // not numeric facts to compare mechanically. For the remaining numbers the
        // association with the nearest attribute is compared, so a correct Chinese
        // clause order (70% HP below, 30% chance) is not rejected merely because
        // the source listed the numbers in the opposite order.
        // The vocabulary fallback does not have a dictionary candidate. It must
        // still retain the visible negation/condition/restriction classes, the
        // numeric associations and a meaningful Chinese body before it may replace
        // the original sentence.
        internal static bool IsSafeFallback(string observed, string translated, out string reason)
        {
            reason = "";
            string source = TranslationStore.Normalize(observed ?? "");
            string target = TranslationStore.Normalize(translated ?? "");
            if (source.Length == 0 || target.Length == 0) { reason = "empty-source"; return false; }
            int negations = CountTokens(source, NegationTokens);
            if (negations > 0 && !ContainsChineseNegation(target))
            { reason = "negation-missing"; return false; }
            if (ContainsToken(source, ConditionalTokens) &&
                !Regex.IsMatch(target, @"(?:如果|若|当|时|只要|除非|直到)"))
            { reason = "condition-missing"; return false; }
            if (ContainsToken(source, RestrictionTokens) &&
                !Regex.IsMatch(target, @"(?:仅|只|限制|几率|概率|冷却|持续|最多|至少|不能|不会)"))
            { reason = "restriction-missing"; return false; }
            if (Regex.IsMatch(source, @"\bhp\b") && !Regex.IsMatch(target, @"(?:hp|生命|血量)"))
            { reason = "hp-missing"; return false; }
            if (Regex.IsMatch(source, @"\bmp\b") && !Regex.IsMatch(target, @"(?:mp|魔力|魔法)"))
            { reason = "mp-missing"; return false; }
            if (!PreservesVisibleNumbers(source, target))
            { reason = "numeric-association-mismatch"; return false; }
            int cjk = Regex.Matches(target, @"[\u4e00-\u9fff]").Count;
            if (cjk < Math.Max(2, source.Length / 12))
            { reason = "translated-body-too-short"; return false; }
            return true;
        }

        internal static bool PreservesVisibleNumbers(string source, string translated)
        {
            return PreservesVisibleNumbers(source, translated, false);
        }

        internal static bool PreservesVisibleNumbers(string source, string translated,
            bool exactDictionarySentence)
        {
            if (exactDictionarySentence)
            {
                List<string> expectedNumbers = VisibleNumberValues(source);
                List<string> actualNumbers = VisibleNumberValues(translated);
                expectedNumbers.Sort(StringComparer.Ordinal);
                actualNumbers.Sort(StringComparer.Ordinal);
                if (expectedNumbers.Count != actualNumbers.Count) return false;
                for (int i = 0; i < expectedNumbers.Count; i++)
                    if (!String.Equals(expectedNumbers[i], actualNumbers[i], StringComparison.Ordinal))
                        return false;
                return true;
            }
            List<string> expected = NumberAssociations(source);
            List<string> actual = NumberAssociations(translated);
            expected.Sort(StringComparer.Ordinal);
            actual.Sort(StringComparer.Ordinal);
            if (expected.Count != actual.Count) return false;
            for (int i = 0; i < expected.Count; i++)
                if (!String.Equals(expected[i], actual[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static List<string> VisibleNumberValues(string text)
        {
            List<string> values = new List<string>();
            foreach (Match match in NumberPattern.Matches(text ?? ""))
            {
                int next = match.Index + match.Length;
                string rest = (text ?? "").Substring(next, Math.Min(4, (text ?? "").Length - next));
                if (Regex.IsMatch(rest, @"^\s*(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase)) continue;
                values.Add(Regex.Replace(match.Value, @"\s+", "").Replace('\u2212', '-'));
            }
            return values;
        }

        private static readonly string[] AttributeKeys = new string[] {
            "max hp", "max mp", "hp", "mp", "生命值", "生命", "魔力值", "魔法值",
            "damage", "伤害", "chance", "rate", "probability", "几率", "概率",
            "success rate", "成功率", "level", "等级", "级",
            "seconds", "second", "sec", "秒", "minutes", "minute", "min", "分钟",
            "range", "范围", "duration", "持续", "cooldown", "冷却",
            "defense", "defence", "防御", "attack", "攻击", "speed", "速度",
            "accuracy", "命中", "targets", "target", "enemies", "enemy", "目标", "敌人",
            "times", "次", "mastery", "熟练度", "recovery", "recover", "恢复",
            "heal", "治疗", "critical", "暴击", "stun", "眩晕", "poison", "中毒",
            "burn", "burning", "燃烧", "freeze", "冻结", "jump", "跳跃",
            "combo", "combos", "连击", "orb", "orbs", "连击点",
            "stats", "stat", "属性"
        };

        private static List<string> NumberAssociations(string text)
        {
            List<string> pairs = new List<string>();
            string raw = text ?? "";
            string lower = raw.ToLowerInvariant();
            foreach (Match match in NumberPattern.Matches(raw))
            {
                int next = match.Index + match.Length;
                string rest = raw.Substring(next, Math.Min(4, raw.Length - next));
                if (Regex.IsMatch(rest, @"^\s*(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase)) continue;
                string number = Regex.Replace(match.Value, @"\s+", "").Replace('\u2212', '-');
                string category = "unbound";
                int followingDistance = Int32.MaxValue, precedingDistance = Int32.MaxValue;
                string followingCategory = "", precedingCategory = "";
                foreach (string key in AttributeKeys)
                {
                    int at = lower.IndexOf(key, StringComparison.Ordinal);
                    while (at >= 0)
                    {
                        int end = at + key.Length;
                        if (at >= next)
                        {
                            int distance = at - next;
                            if (distance < followingDistance)
                            {
                                followingDistance = distance;
                                followingCategory = AttributeCategory(key);
                            }
                        }
                        else if (end <= match.Index)
                        {
                            int distance = match.Index - end;
                            if (distance < precedingDistance)
                            {
                                precedingDistance = distance;
                                precedingCategory = AttributeCategory(key);
                            }
                        }
                        at = lower.IndexOf(key, at + 1, StringComparison.Ordinal);
                    }
                }
                // A percentage that follows a chance phrase belongs to that chance
                // even when a new clause starts immediately afterwards.
                string before = lower.Substring(Math.Max(0, match.Index - 28),
                    Math.Min(28, match.Index));
                bool isPercent = match.Value.IndexOf('%') >= 0 || match.Value.IndexOf('％') >= 0;
                string direct = "";
                if (isPercent && Regex.IsMatch(before, @"(?:chance|rate|probability|几率|概率)",
                    RegexOptions.IgnoreCase)) direct = "chance";
                else direct = DirectFollowingCategory(lower, next);
                if (direct.Length > 0) category = direct;
                // Otherwise choose the closest attribute; on an equal distance
                // prefer the attribute the number directly qualifies.
                else if (followingDistance <= 30 && followingDistance <= precedingDistance)
                    category = followingCategory;
                else if (precedingDistance <= 30) category = precedingCategory;
                pairs.Add(category + "#" + number);
            }
            return pairs;
        }

        private static string DirectFollowingCategory(string lower, int at)
        {
            if (at < 0 || at >= lower.Length) return "";
            string after = lower.Substring(at, Math.Min(32, lower.Length - at));
            if (Regex.IsMatch(after, @"^\s*(?:的)?\s*(?:chance|rate|probability|几率|概率|成功率)\b",
                RegexOptions.IgnoreCase)) return "chance";
            if (Regex.IsMatch(after, @"^\s*(?:of\s+the\s+)?(?:damage|伤害)\b",
                RegexOptions.IgnoreCase)) return "damage";
            if (Regex.IsMatch(after, @"^\s*(?:seconds?|sec|秒|minutes?|min|分钟)\b",
                RegexOptions.IgnoreCase)) return "time";
            if (Regex.IsMatch(after, @"^\s*(?:to\s+)?(?:all\s+)?(?:stats?|attributes?|属性)\b",
                RegexOptions.IgnoreCase)) return "stats";
            if (Regex.IsMatch(after, @"^\s*(?:of\s+the\s+enemy.{0,3}s?\s+)?(?:max\s+)?hp\b",
                RegexOptions.IgnoreCase)) return "hp";
            if (Regex.IsMatch(after, @"^\s*(?:of\s+)?(?:them|enemies|enemy|targets?|monsters?|目标|敌人|怪物)\b",
                RegexOptions.IgnoreCase)) return "target";
            if (Regex.IsMatch(after, @"^\s*(?:次)?\s*(?:组合攻击|combo|combos|连击|orb|orbs|连击点)\b",
                RegexOptions.IgnoreCase)) return "combo";
            if (Regex.IsMatch(after, @"^\s*(?:times|次)\b", RegexOptions.IgnoreCase)) return "times";
            if (Regex.IsMatch(after, @"^\s*(?:hp|生命值|生命)\b", RegexOptions.IgnoreCase)) return "hp";
            if (Regex.IsMatch(after, @"^\s*(?:mp|魔力值|魔法值)\b", RegexOptions.IgnoreCase)) return "mp";
            return "";
        }

        private static string AttributeCategory(string key)
        {
            switch (key)
            {
                case "max hp": case "hp": case "生命值": case "生命": return "hp";
                case "max mp": case "mp": case "魔力值": case "魔法值": return "mp";
                case "damage": case "伤害": return "damage";
                case "chance": case "rate": case "probability": case "几率": case "概率":
                case "success rate": case "成功率": return "chance";
                case "level": case "等级": case "级": return "level";
                case "seconds": case "second": case "sec": case "秒":
                case "minutes": case "minute": case "min": case "分钟":
                case "duration": case "持续": return "time";
                case "range": case "范围": return "range";
                case "cooldown": case "冷却": return "cooldown";
                case "defense": case "defence": case "防御": return "defense";
                case "attack": case "攻击": return "attack";
                case "speed": case "速度": return "speed";
                case "accuracy": case "命中": return "accuracy";
                case "targets": case "target": case "enemies": case "enemy":
                case "目标": case "敌人": return "target";
                case "times": case "次": return "times";
                case "mastery": case "熟练度": return "mastery";
                case "recovery": case "recover": case "恢复":
                case "heal": case "治疗": return "recovery";
                case "critical": case "暴击": return "critical";
                case "stun": case "眩晕": return "stun";
                case "poison": case "中毒": return "poison";
                case "burn": case "burning": case "燃烧": return "burn";
                case "freeze": case "冻结": return "freeze";
                case "jump": case "跳跃": return "jump";
                case "combo": case "combos": case "连击":
                case "orb": case "orbs": case "连击点": return "combo";
                case "stats": case "stat": case "属性": return "stats";
                default: return "unbound";
            }
        }

        private static int CountTokens(string text, string[] tokens)
        {
            int count = 0;
            string padded = " " + text + " ";
            foreach (string token in tokens)
            {
                string expression = Regex.Escape(token).Replace(@"\ ", @"\s+");
                count += Regex.Matches(padded, @"(?<![a-z0-9])" + expression + @"(?![a-z0-9])",
                    RegexOptions.IgnoreCase).Count;
            }
            return count;
        }

        private static bool ContainsToken(string text, string[] tokens)
        {
            string padded = " " + text + " ";
            foreach (string token in tokens)
            {
                string expression = Regex.Escape(token).Replace(@"\ ", @"\s+");
                if (Regex.IsMatch(padded, @"(?<![a-z0-9])" + expression + @"(?![a-z0-9])",
                    RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        private static bool ContainsChineseNegation(string translated)
        {
            return Regex.IsMatch(translated, @"[不没无未禁]|无法|不能|不可|绝不|从不|禁止");
        }

        private static string AttributeSignature(string text)
        {
            StringBuilder signature = new StringBuilder();
            foreach (string token in AttributeTokens)
            {
                string expression = Regex.Escape(token).Replace(@"\ ", @"\s+");
                signature.Append(Regex.IsMatch(" " + text + " ",
                    @"(?<![a-z0-9])" + expression + @"(?![a-z0-9])",
                    RegexOptions.IgnoreCase) ? '1' : '0');
            }
            return signature.ToString();
        }

        private static HashSet<string> SignificantWords(string normalized)
        {
            HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in (normalized ?? "").Split(
                new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "#" || StopWords.Contains(word)) continue;
                if (Regex.IsMatch(word, @"^\d+$")) continue;
                words.Add(word);
            }
            return words;
        }
    }
}
