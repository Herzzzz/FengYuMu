using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // The ordinary-item pipeline must never trust a dictionary row merely because its
    // English side matched. Item prose carries gameplay quantities, effects, limits and
    // destinations, and the shipped rows contain known disagreements (for example an
    // English 500 HP row translated as 400). This guard compares the complete visible
    // semantics of both sides once, at dictionary load time, and the same guard gates the
    // exact, sentence, template, shared-description and vocabulary fallbacks at paint time.
    internal enum ItemSemanticStatus
    {
        Trusted = 0,
        Conflict = 1,
        Undecidable = 2
    }

    internal sealed class ItemSemanticAudit
    {
        internal ItemSemanticStatus Status = ItemSemanticStatus.Undecidable;
        internal string Reason = "unverified";
        internal string NumbersEnglish = "";
        internal string NumbersChinese = "";
        internal string BindingsEnglish = "";
        internal string BindingsChinese = "";
        internal string EffectsEnglish = "";
        internal string EffectsChinese = "";
        internal string LogicEnglish = "";
        internal string LogicChinese = "";

        internal string StatusText
        {
            get
            {
                if (Status == ItemSemanticStatus.Trusted) return "trusted";
                if (Status == ItemSemanticStatus.Conflict) return "conflict";
                return "undecidable";
            }
        }
    }

    internal sealed class ItemBodyTranslation
    {
        internal bool Matched;
        internal bool Estimated;
        internal bool Shared;
        internal string Translation = "";
        internal string Reason = "";
        internal string Source = "";
        internal string ItemId = "";
    }

    internal sealed class ItemSentenceReference
    {
        internal TranslationEntry Entry;
        internal string English = "";
        internal string Normalized = "";
        internal string Chinese = "";
        internal int Index;
    }

    internal static class ItemTextSemantics
    {
        private static readonly Regex NumberPattern = new Regex(
            @"[+\-\u2212]?\d(?:[\d,]*\d)?(?:\.\d+)?\s*%?", RegexOptions.Compiled);
        private static readonly Regex EnglishReturnPattern = new Regex(
            @"^\s*returns?\s+you\s+to\s+(?<dest>.+?)\s*\.?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChineseCjk = new Regex(@"[\u3400-\u9fff]",
            RegexOptions.Compiled);

        private sealed class EffectTerm
        {
            internal string Key;
            internal Regex Pattern;
            internal EffectTerm(string key, string pattern)
            {
                Key = key;
                Pattern = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
        }

        private sealed class TermHit
        {
            internal string Key;
            internal int Start;
            internal int Length;
            internal int End { get { return Start + Length; } }
        }

        private sealed class NumberSignal
        {
            internal decimal Value;
            internal string Raw = "";
            internal string Canonical = "";
            internal bool Percent;
            internal int Start;
            internal int Length;
            internal string Unit = "";
            internal string Binding = "";
            internal int End { get { return Start + Length; } }
        }

        private sealed class SignalSet
        {
            internal string Text = "";
            internal List<NumberSignal> Numbers = new List<NumberSignal>();
            internal List<TermHit> Effects = new List<TermHit>();
            internal List<string> Logic = new List<string>();
        }

        private sealed class LogicTerm
        {
            internal string Key;
            internal Regex English;
            internal Regex Chinese;
            internal LogicTerm(string key, string english, string chinese)
            {
                Key = key;
                English = new Regex(english, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                Chinese = new Regex(chinese, RegexOptions.Compiled);
            }
        }

        // Longest terms are listed first and overlapping hits are resolved by length below.
        private static readonly EffectTerm[] EnglishEffects = new EffectTerm[]
        {
            new EffectTerm("maxhp", @"\bmax(?:imum)?\s+hp\b"),
            new EffectTerm("maxmp", @"\bmax(?:imum)?\s+mp\b"),
            new EffectTerm("attackspeed", @"\battack\s+speed\b"),
            new EffectTerm("magicattack", @"\bmagic\s+attack(?:\s+power)?\b"),
            new EffectTerm("weaponattack", @"\bweapon\s+attack\b"),
            new EffectTerm("attack", @"\battack(?:\s+power)?\b"),
            new EffectTerm("magicdef", @"\bmagic\s+def(?:ense)?\.?\b"),
            new EffectTerm("weapondef", @"\bweapon\s+def(?:ense)?\.?\b"),
            new EffectTerm("weapondef", @"\bdef(?:ense)?\.?\b"),
            new EffectTerm("accuracy", @"\baccuracy\b|\bhit\s+rate\b"),
            new EffectTerm("avoidability", @"\bavoidability\b|\bavoid(?:ance)?\b"),
            new EffectTerm("jump", @"\bjump\b"),
            new EffectTerm("speed", @"\bspeed\b"),
            new EffectTerm("critdmg", @"\bcrit(?:ical)?\.?\s*damage\b"),
            new EffectTerm("critrate", @"\bcrit(?:ical)?\.?\s*(?:rate|chance)\b"),
            new EffectTerm("crit", @"\bcrit(?:ical)?\b"),
            new EffectTerm("success", @"\bsuccess\s+rate\b|\bsuccess\b"),
            new EffectTerm("exp", @"\bexp(?:erience)?\b"),
            new EffectTerm("meso", @"\bmesos?\b"),
            new EffectTerm("hp", @"(?<![a-z])hp(?![a-z])|\bhealth\b"),
            new EffectTerm("mp", @"(?<![a-z])mp(?![a-z])|\bmana\b"),
            new EffectTerm("str", @"\bstr(?:ength)?\b"),
            new EffectTerm("dex", @"\bdex(?:terity)?\b"),
            new EffectTerm("int", @"\bint(?:elligence)?\b"),
            new EffectTerm("luk", @"\bluk\b|\bluek\b|\bluck\b"),
            new EffectTerm("cooldown", @"\bcool(?:down|\s+down)\b"),
            new EffectTerm("duration", @"\bduration\b"),
            new EffectTerm("damage", @"\bdamage\b"),
            new EffectTerm("pet", @"\bpets?\b"),
            new EffectTerm("price", @"\bprice\b|\bcost\b"),
            new EffectTerm("quantity", @"\bquantity\b|\bamount\b")
        };

        private static readonly EffectTerm[] ChineseEffects = new EffectTerm[]
        {
            new EffectTerm("maxhp", @"最大(?:生命值|血量|体力|HP)"),
            new EffectTerm("maxmp", @"最大(?:魔法值|魔力|MP)"),
            new EffectTerm("attackspeed", @"攻击速度|攻速"),
            new EffectTerm("magicattack", @"魔法攻击力?|魔攻"),
            new EffectTerm("weaponattack", @"武器攻击力?"),
            new EffectTerm("attack", @"攻击力?|物攻"),
            new EffectTerm("magicdef", @"魔法防御力?|魔防"),
            new EffectTerm("weapondef", @"物理防御力?|防御力?|物防"),
            new EffectTerm("accuracy", @"命中率|命中"),
            new EffectTerm("avoidability", @"回避率|回避"),
            new EffectTerm("jump", @"跳跃力|跳跃"),
            new EffectTerm("speed", @"移动速度|速度"),
            new EffectTerm("critdmg", @"暴击伤害"),
            new EffectTerm("critrate", @"暴击率"),
            new EffectTerm("crit", @"暴击"),
            new EffectTerm("success", @"成功率|成功"),
            new EffectTerm("exp", @"经验值?|EXP"),
            new EffectTerm("meso", @"金币|枫币|楓幣|冒险币"),
            new EffectTerm("hp", @"生命值|血量|体力|HP"),
            new EffectTerm("mp", @"魔法值|魔力|MP"),
            new EffectTerm("str", @"力量|STR"),
            new EffectTerm("dex", @"敏捷|DEX"),
            new EffectTerm("int", @"智力|INT"),
            new EffectTerm("luk", @"运气|幸运|LUK"),
            new EffectTerm("cooldown", @"冷却"),
            new EffectTerm("duration", @"持续时间|持续"),
            new EffectTerm("damage", @"伤害"),
            new EffectTerm("pet", @"宠物"),
            new EffectTerm("price", @"价格|售价|价钱"),
            new EffectTerm("quantity", @"数量|个数")
        };

        private static readonly LogicTerm[] LogicTerms = new LogicTerm[]
        {
            new LogicTerm("approx", @"\b(?:around|about|approximately|roughly)\b", @"约|大约|大概|左右"),
            new LogicTerm("upto", @"\bup\s+to\b", @"最多|至多|上限|不超过"),
            new LogicTerm("atleast", @"\bat\s+least\b", @"至少|最少"),
            new LogicTerm("all", @"\ball\b", @"所有|全部|都|均|全"),
            new LogicTerm("some", @"\bsome\b", @"部分|一些|某些|少量"),
            new LogicTerm("only", @"\b(?:only|simply)\b", @"仅|只|唯一|只能"),
            new LogicTerm("not", @"\b(?:not|never|cannot|without)\b|n't\b", @"不|无|未|无法|不能|不可|没|非(?!常)"),
            new LogicTerm("unless", @"\bunless\b", @"除非"),
            new LogicTerm("if", @"\b(?:if|whenever)\b", @"如果|若|假如|一旦|当|时(?!间)"),
            new LogicTerm("before", @"\bbefore\b", @"之前|以前"),
            new LogicTerm("after", @"\bafter\b", @"之后|以后"),
            new LogicTerm("while", @"\b(?:while|when)\b", @"当|时候|期间"),
            new LogicTerm("until", @"\buntil\b", @"直到|为止"),
            new LogicTerm("failed", @"\bfail(?:ed|s|ure)?\b", @"失败"),
            new LogicTerm("destroyed", @"\bdestroy(?:ed|s|ing)?\b", @"摧毁|破坏|消失"),
            new LogicTerm("or", @"\bor\b", @"或"),
            new LogicTerm("both", @"\bboth\b", @"都|两者|二者"),
            new LogicTerm("cooldown", @"\bcool(?:down|\s+down)\b", @"冷却")
        };

        private static readonly string[] SignificantLogicKeys = new string[]
        {
            "approx", "upto", "atleast", "all", "some", "only", "not", "unless", "if",
            "before", "after", "while", "until", "failed", "destroyed", "or", "both",
            "cooldown"
        };

        private static readonly Dictionary<string, string> AuxiliaryPhrases =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Observed server tooltip wording (F064) plus standard trade-state vocabulary.
            // These are complete clauses: they can never be substituted inside a longer
            // sentence, so they cannot hide an effect or a number.
            { "account bound", "账号绑定" },
            { "accountbound", "账号绑定" },
            { "transferable within world", "可在世界内交易" },
            { "transferable within the world", "可在世界内交易" },
            { "untradable", "不可交易" },
            { "untradeable", "不可交易" },
            { "cannot be traded", "不可交易" },
            { "cannot be exchanged", "不可交换" },
            { "one of a kind item", "唯一物品" },
            { "oneofakind item", "唯一物品" }
        };

        private static readonly Dictionary<string, string> StatNames = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            { "attack power", "攻击力" }, { "attack", "攻击力" },
            { "magic attack", "魔法攻击力" }, { "weapon def", "物理防御力" },
            { "weapon defense", "物理防御力" }, { "magic def", "魔法防御力" },
            { "magic defense", "魔法防御力" }, { "defense", "防御力" },
            { "accuracy", "命中率" }, { "avoidability", "回避率" },
            { "speed", "移动速度" }, { "jump", "跳跃力" },
            { "max hp", "最大生命值" }, { "max mp", "最大魔法值" },
            { "hp", "生命值" }, { "mp", "魔法值" }, { "str", "力量" },
            { "strength", "力量" }, { "dex", "敏捷" }, { "dexterity", "敏捷" },
            { "int", "智力" }, { "intelligence", "智力" }, { "luk", "运气" },
            { "luck", "运气" }, { "exp", "经验值" }, { "damage", "伤害" },
            { "critical damage", "暴击伤害" }, { "crit damage", "暴击伤害" },
            { "critical rate", "暴击率" }, { "crit rate", "暴击率" }
        };

        private static readonly Dictionary<string, string> EquipmentNames = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            { "hats", "头盔" }, { "hat", "头盔" }, { "helmets", "头盔" },
            { "earrings", "耳环" }, { "earring", "耳环" },
            { "topwear", "上衣" }, { "top", "上衣" }, { "shirts", "上衣" },
            { "bottomwear", "下装" }, { "bottom", "下装" }, { "pants", "下装" },
            { "overall", "套装" }, { "overalls", "套装" },
            { "shoes", "鞋子" }, { "shoe", "鞋子" }, { "boots", "鞋子" },
            { "gloves", "手套" }, { "glove", "手套" },
            { "capes", "披风" }, { "cape", "披风" },
            { "weapons", "武器" }, { "weapon", "武器" },
            { "shields", "盾牌" }, { "shield", "盾牌" },
            { "accessories", "饰品" }, { "accessory", "饰品" }
        };

        private static readonly Regex ReturnNearestTown = new Regex(
            @"^(?:the\s+)?nearest\s+town$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DurationUnit = new Regex(
            @"^\s*(sec(?:ond)?s?|min(?:ute)?s?|hours?|hrs?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChineseDurationUnit = new Regex(
            @"^\s*(秒|秒钟|分钟|分|小时|个小时)",
            RegexOptions.Compiled);
        private static readonly Regex QuantityUnit = new Regex(
            @"^\s*(?:x|×)\s*$|^\s*(?:个|张|件|瓶|支)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TermHygiene = new Regex(
            @"\b(?:the|a|an|of|to|for|and|or|with|within|on|in|at|by|from|item|items)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static string DecodeLineBreaks(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? "";
            return value.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        }

        internal static string CleanChinese(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            string text = DecodeLineBreaks(value).Replace("\r", "\n");
            text = text.Replace("\n", " ");
            text = Regex.Replace(text, @"[ \t]+", " ");
            return text.Trim();
        }

        // The visible source may use either recorded verb. This is deliberately scoped to
        // item prose: chats, quests, skills and equipment grammar never pass through it.
        internal static string CanonicalSentence(string value)
        {
            string normalized = TranslationStore.Normalize(value);
            normalized = Regex.Replace(normalized, @"\brestores\b", "restore");
            normalized = Regex.Replace(normalized, @"\brestored\b", "restore");
            normalized = Regex.Replace(normalized, @"\brestoring\b", "restore");
            normalized = Regex.Replace(normalized, @"\brecovers\b", "restore");
            normalized = Regex.Replace(normalized, @"\brecovered\b", "restore");
            normalized = Regex.Replace(normalized, @"\brecovering\b", "restore");
            return normalized;
        }

        internal static ItemSemanticAudit Inspect(string english, string chinese)
        {
            return Inspect(english, chinese, null);
        }

        internal static ItemSemanticAudit Inspect(string english, string chinese,
            IDictionary<string, string> places)
        {
            ItemSemanticAudit audit = new ItemSemanticAudit();
            string en = DecodeLineBreaks(english ?? "").Trim();
            string zh = DecodeLineBreaks(chinese ?? "").Trim();
            if (en.Length == 0 || zh.Length == 0)
            {
                audit.Status = ItemSemanticStatus.Conflict;
                audit.Reason = "empty-side";
                return audit;
            }
            if (!ChineseCjk.IsMatch(zh) || String.Equals(
                TranslationStore.Normalize(en), TranslationStore.Normalize(zh),
                StringComparison.Ordinal))
            {
                audit.Status = ItemSemanticStatus.Conflict;
                audit.Reason = "not-translated";
                return audit;
            }

            // Destination-only scroll prose has no numeric/effect signal. Validate it
            // against the observed map vocabulary instead of inventing a town name.
            Match returnMatch = EnglishReturnPattern.Match(en);
            if (returnMatch.Success)
            {
                string destination = returnMatch.Groups["dest"].Value.Trim();
                if (ReturnNearestTown.IsMatch(destination))
                {
                    if (zh.Contains("最近") && (zh.Contains("城镇") || zh.Contains("城市")))
                    {
                        audit.Status = ItemSemanticStatus.Trusted;
                        audit.Reason = "destination-nearest-town";
                    }
                    else
                    {
                        audit.Status = ItemSemanticStatus.Conflict;
                        audit.Reason = "destination-nearest-town-mismatch";
                    }
                    return audit;
                }
                string placeChinese = ResolvePlaceName(destination, places);
                if (placeChinese.Length > 0)
                {
                    if (zh.IndexOf(placeChinese, StringComparison.Ordinal) >= 0)
                    {
                        audit.Status = ItemSemanticStatus.Trusted;
                        audit.Reason = "destination:" + placeChinese;
                    }
                    else
                    {
                        audit.Status = ItemSemanticStatus.Undecidable;
                        audit.Reason = "destination-name-differs";
                    }
                    return audit;
                }
                audit.Status = ItemSemanticStatus.Undecidable;
                audit.Reason = "destination-unverified";
                return audit;
            }

            SignalSet enSignals = ExtractSignals(en, false);
            SignalSet zhSignals = ExtractSignals(zh, true);
            audit.NumbersEnglish = DescribeNumbers(enSignals);
            audit.NumbersChinese = DescribeNumbers(zhSignals);
            audit.BindingsEnglish = String.Join(",", DescribeBindings(enSignals).ToArray());
            audit.BindingsChinese = String.Join(",", DescribeBindings(zhSignals).ToArray());
            audit.EffectsEnglish = DescribeEffects(enSignals);
            audit.EffectsChinese = DescribeEffects(zhSignals);
            audit.LogicEnglish = String.Join(",", enSignals.Logic.ToArray());
            audit.LogicChinese = String.Join(",", zhSignals.Logic.ToArray());

            if (!SameMultiset(DescribeNumberValues(enSignals), DescribeNumberValues(zhSignals)))
            {
                audit.Status = ItemSemanticStatus.Conflict;
                audit.Reason = "number-mismatch";
                return audit;
            }
            if (!SameMultiset(DescribeBindings(enSignals), DescribeBindings(zhSignals)))
            {
                audit.Status = ItemSemanticStatus.Conflict;
                audit.Reason = "number-binding-mismatch";
                return audit;
            }
            List<string> enEffects = SignificantEffects(enSignals);
            List<string> zhEffects = SignificantEffects(zhSignals);
            if (!SameMultiset(enEffects, zhEffects))
            {
                audit.Status = ItemSemanticStatus.Conflict;
                audit.Reason = "effect-mismatch";
                return audit;
            }
            string logicReason;
            if (!LogicCompatible(enSignals, zhSignals, out logicReason))
            {
                audit.Status = ItemSemanticStatus.Conflict;
                audit.Reason = logicReason;
                return audit;
            }

            bool hasNumbers = enSignals.Numbers.Count > 0 || zhSignals.Numbers.Count > 0;
            bool hasEffects = enEffects.Count > 0 || zhEffects.Count > 0;
            bool hasLogic = enSignals.Logic.Count > 0 || zhSignals.Logic.Count > 0;
            int unboundEn = CountBindings(enSignals, "unbound");
            int unboundZh = CountBindings(zhSignals, "unbound");
            if (unboundEn != unboundZh)
            {
                audit.Status = ItemSemanticStatus.Undecidable;
                audit.Reason = "unbound-numbers";
                return audit;
            }
            if (HasExtraChineseSentence(en, zh))
            {
                audit.Status = ItemSemanticStatus.Undecidable;
                audit.Reason = "extra-chinese-sentence";
                return audit;
            }
            if (!hasNumbers && !hasEffects && !hasLogic)
            {
                audit.Status = ItemSemanticStatus.Undecidable;
                audit.Reason = "no-verifiable-gameplay-signal";
                return audit;
            }
            if ((hasEffects && !hasNumbers) || (hasNumbers && !hasEffects))
            {
                // A one-sided numeric/effect cue can still be a pure flavour sentence
                // ("a cooldown before it can be used again"). Keep the verified pair only
                // when both sides agree on the signals that are actually present.
                if (enEffects.Count == 0 && zhEffects.Count == 0 &&
                    !HasMeaningfulLogic(enSignals, zhSignals))
                {
                    audit.Status = ItemSemanticStatus.Trusted;
                    audit.Reason = "numbers-only";
                    return audit;
                }
            }
            audit.Status = ItemSemanticStatus.Trusted;
            audit.Reason = "signals-match";
            return audit;
        }

        private static bool HasExtraChineseSentence(string english, string chinese)
        {
            int englishSentences = SplitSentences(english).Count;
            int chineseSentences = SplitChineseSentences(chinese).Count;
            // A Chinese row may join clauses with commas, but a genuinely extra Chinese
            // sentence has no counterpart in the visible source and must not be painted.
            return englishSentences > 0 && chineseSentences > englishSentences;
        }

        internal static void AuditEntries(List<TranslationEntry> entries)
        {
            if (entries == null) return;
            Dictionary<string, string> places = BuildPlaceMap(entries);
            foreach (TranslationEntry entry in entries)
            {
                if (entry == null || !entry.IsItemText) continue;
                ItemSemanticAudit audit = Inspect(entry.English, entry.Chinese, places);
                entry.ItemStatus = audit.Status;
                entry.ItemStatusReason = audit.Reason;
            }
        }

        internal static Dictionary<string, string> BuildPlaceMap(List<TranslationEntry> entries)
        {
            Dictionary<string, string> places = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> seen = new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);
            if (entries == null) return places;
            foreach (TranslationEntry entry in entries)
            {
                if (entry == null || entry.Category == null) continue;
                if (!entry.Category.StartsWith("怀旧服-地图#", StringComparison.Ordinal) &&
                    !entry.Category.StartsWith("怀旧服-地区#", StringComparison.Ordinal)) continue;
                string key = entry.Normalized;
                if (key.Length == 0 || entry.Chinese.Length == 0) continue;
                HashSet<string> values;
                if (!seen.TryGetValue(key, out values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    seen.Add(key, values);
                }
                values.Add(entry.Chinese);
            }
            foreach (KeyValuePair<string, HashSet<string>> pair in seen)
            {
                if (pair.Value.Count != 1) continue;
                foreach (string value in pair.Value) { places[pair.Key] = value; break; }
            }
            return places;
        }

        internal static string ResolvePlaceName(string destination, IDictionary<string, string> places)
        {
            if (String.IsNullOrEmpty(destination) || places == null) return "";
            string normalized = TranslationStore.Normalize(destination);
            if (normalized.Length == 0) return "";
            string direct;
            if (places.TryGetValue(normalized, out direct)) return direct;
            // Destinations such as "Sleepywood, a quiet and dark forest-town" or
            // "the dark Kerning City" contain the observed map name plus visible adjectives.
            // Accept only a whole-token map name and only when its translation also occurs in
            // the Chinese row; a bare substring such as "City" is never enough.
            string bestKey = "";
            string bestValue = "";
            foreach (KeyValuePair<string, string> pair in places)
            {
                if (pair.Key.Length < 4 || pair.Key.Length <= bestKey.Length) continue;
                int at = normalized.IndexOf(pair.Key, StringComparison.Ordinal);
                if (at < 0) continue;
                if (!TranslationStore.IsBoundary(normalized, at - 1) ||
                    !TranslationStore.IsBoundary(normalized, at + pair.Key.Length)) continue;
                bestKey = pair.Key;
                bestValue = pair.Value;
            }
            return bestValue;
        }

        internal static bool IsAuxiliaryPhrase(string text)
        {
            string key = TranslationStore.Normalize(text);
            return key.Length > 0 && AuxiliaryPhrases.ContainsKey(key);
        }

        internal static string TranslateAuxiliaryPhrase(string text)
        {
            string key = TranslationStore.Normalize(text);
            string value;
            return key.Length > 0 && AuxiliaryPhrases.TryGetValue(key, out value) ? value : "";
        }

        internal static string MatchAuxiliaryPhrase(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length == 0) return "";
            string exact;
            if (AuxiliaryPhrases.TryGetValue(normalized, out exact))
                return EnsureSentencePunctuation(exact);
            List<string> pieces = SplitSentences(text);
            if (pieces.Count <= 1) return "";
            StringBuilder result = new StringBuilder();
            foreach (string piece in pieces)
            {
                string key = TranslationStore.Normalize(piece);
                string value;
                if (key.Length == 0) continue;
                if (!AuxiliaryPhrases.TryGetValue(key, out value)) return "";
                result.Append(value).Append('。');
            }
            return result.ToString();
        }

        internal static List<string> SplitSentences(string text)
        {
            List<string> result = new List<string>();
            string decoded = DecodeLineBreaks(text ?? "").Replace("\r", "\n");
            foreach (string rawLine in decoded.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                int start = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c != '.' && c != '!' && c != '?') continue;
                    if (c == '.' && i > 0 && i + 1 < line.Length &&
                        Char.IsDigit(line[i - 1]) && Char.IsDigit(line[i + 1])) continue;
                    if (i + 1 < line.Length && !Char.IsWhiteSpace(line[i + 1])) continue;
                    if (c == '.' && IsAbbreviation(line, i)) continue;
                    string sentence = line.Substring(start, i - start + 1).Trim();
                    if (sentence.Length > 0) result.Add(sentence);
                    start = i + 1;
                }
                if (start < line.Length)
                {
                    string tail = line.Substring(start).Trim();
                    if (tail.Length > 0) result.Add(tail);
                }
            }
            if (result.Count == 0 && decoded.Trim().Length > 0)
                result.Add(decoded.Trim());
            return result;
        }

        internal static List<string> SplitChineseSentences(string text)
        {
            List<string> result = new List<string>();
            string decoded = DecodeLineBreaks(text ?? "").Replace("\r", "\n");
            foreach (string rawLine in decoded.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                int start = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c != '。' && c != '！' && c != '？' && c != '!' && c != '?') continue;
                    string sentence = line.Substring(start, i - start + 1).Trim();
                    if (sentence.Length > 0) result.Add(sentence);
                    start = i + 1;
                }
                if (start < line.Length)
                {
                    string tail = line.Substring(start).Trim();
                    if (tail.Length > 0) result.Add(tail);
                }
            }
            if (result.Count == 0 && decoded.Trim().Length > 0)
                result.Add(decoded.Trim());
            return result;
        }

        private static bool IsAbbreviation(string line, int period)
        {
            int start = period - 1;
            while (start >= 0 && Char.IsLetter(line[start])) start--;
            start++;
            string word = line.Substring(start, period - start).ToLowerInvariant();
            if (word == "def" || word == "max" || word == "min" || word == "sec" ||
                word == "approx" || word == "no" || word == "vs" || word == "req" ||
                word == "lv" || word == "hp" || word == "mp" || word == "str" ||
                word == "dex" || word == "int" || word == "luk" || word == "crit" ||
                word == "e" || word == "i") return true;
            int next = period + 1;
            while (next < line.Length && Char.IsWhiteSpace(line[next])) next++;
            if (next >= line.Length) return false;
            char nextChar = line[next];
            return nextChar == '+' || nextChar == '-' || nextChar == '%' ||
                Char.IsDigit(nextChar);
        }

        internal static bool TryTranslateTemplate(string text, TranslationStore store,
            out string chinese, out string source)
        {
            chinese = "";
            source = "";
            string visible = (text ?? "").Trim();
            if (visible.Length == 0) return false;
            string withoutPeriod = Regex.Replace(visible, @"\s*\.\s*$", "").Trim();

            string result;
            if (TryTranslateRestore(withoutPeriod, out result))
            {
                chinese = result; source = "item-template-restore"; return true;
            }
            if (TryTranslateReturn(withoutPeriod, store, out result))
            {
                chinese = result; source = "item-template-return"; return true;
            }
            if (TryTranslateStatDuration(withoutPeriod, out result))
            {
                chinese = result; source = "item-template-duration"; return true;
            }
            if (TryTranslateImprove(withoutPeriod, out result))
            {
                chinese = result; source = "item-template-improve"; return true;
            }
            if (TryTranslateSuccessRate(withoutPeriod, out result))
            {
                chinese = result; source = "item-template-success"; return true;
            }
            if (TryTranslateFailure(withoutPeriod, out result))
            {
                chinese = result; source = "item-template-failure"; return true;
            }
            return false;
        }

        private static bool TryTranslateRestore(string text, out string chinese)
        {
            chinese = "";
            Match dual = Regex.Match(text,
                @"^(restores?|recovers?)\s+(?:(around|about|approximately|roughly|up to)\s+)?" +
                @"([+\-]?\d[\d,]*(?:\.\d+)?)\s*(%?)\s*(hp|mp)\s+and\s+" +
                @"(?:(around|about|approximately|roughly|up to)\s+)?" +
                @"([+\-]?\d[\d,]*(?:\.\d+)?)\s*(%?)\s*(hp|mp)$",
                RegexOptions.IgnoreCase);
            if (dual.Success)
            {
                chinese = "恢复" + ApproxWord(dual.Groups[2].Value) +
                    dual.Groups[3].Value + dual.Groups[4].Value + " " +
                    dual.Groups[5].Value.ToUpperInvariant() + "和" +
                    ApproxWord(dual.Groups[6].Value) +
                    dual.Groups[7].Value + dual.Groups[8].Value + " " +
                    dual.Groups[9].Value.ToUpperInvariant() + "。";
                return true;
            }
            Match shared = Regex.Match(text,
                @"^(restores?|recovers?)\s+(?:(around|about|approximately|roughly|up to)\s+)?" +
                @"([+\-]?\d[\d,]*(?:\.\d+)?)\s*(%?)\s+(?:of\s+)?(?:for\s+)?(?:both\s+)?" +
                @"(hp|mp)(?:\s+and\s+(hp|mp))?$",
                RegexOptions.IgnoreCase);
            if (shared.Success)
            {
                string unit = shared.Groups[4].Value;
                string first = shared.Groups[5].Value.ToUpperInvariant();
                string second = shared.Groups[6].Value.ToUpperInvariant();
                bool both = second.Length > 0;
                if (both && !String.Equals(first, second, StringComparison.OrdinalIgnoreCase) &&
                    shared.Groups[3].Value.Length > 0)
                {
                    chinese = "恢复" + ApproxWord(shared.Groups[2].Value) +
                        shared.Groups[3].Value + unit + "点" + first + "和" + second + "。";
                }
                else
                {
                    string effects = both ? first + "和" + second : first;
                    string separator = unit.Length > 0 ? "" : " ";
                    chinese = "恢复" + ApproxWord(shared.Groups[2].Value) +
                        shared.Groups[3].Value + unit + separator + effects + "。";
                }
                return true;
            }
            Match reversed = Regex.Match(text,
                @"^(restores?|recovers?)\s+(?:(around|about|approximately|roughly|up to)\s+)?" +
                @"(hp|mp)\s*\+?\s*([+\-]?\d[\d,]*(?:\.\d+)?)\s*(%?)$",
                RegexOptions.IgnoreCase);
            if (reversed.Success)
            {
                chinese = "恢复" + ApproxWord(reversed.Groups[2].Value) +
                    reversed.Groups[4].Value + reversed.Groups[5].Value + " " +
                    reversed.Groups[3].Value.ToUpperInvariant() + "。";
                return true;
            }
            return false;
        }

        private static string ApproxWord(string value)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant();
            if (normalized == "up to") return "最多";
            if (normalized.Length > 0) return "约";
            return "";
        }

        private static bool TryTranslateReturn(string text, TranslationStore store,
            out string chinese)
        {
            chinese = "";
            Match match = Regex.Match(text, @"^returns?\s+you\s+to\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            string destination = match.Groups[1].Value.Trim();
            if (Regex.IsMatch(destination, @"^(?:the\s+)?nearest\s+town$",
                RegexOptions.IgnoreCase))
            {
                chinese = "返回最近的城镇。"; return true;
            }
            string place = store == null ? "" : store.ResolvePlaceChinese(destination);
            if (place.Length > 0)
            {
                chinese = "返回" + place + "。"; return true;
            }
            // Unknown destinations keep the observed proper noun; no map ID or Chinese
            // toponym is invented. The rest of the sentence is still translated.
            if (destination.Length > 0)
            {
                chinese = "返回" + destination + "。"; return true;
            }
            return false;
        }

        private static bool TryTranslateStatDuration(string text, out string chinese)
        {
            chinese = "";
            Match match = Regex.Match(text,
                @"^(attack power|magic attack|weapon def(?:ense)?|magic def(?:ense)?|" +
                @"defense|accuracy|avoidability|speed|jump|max hp|max mp|hp|mp|" +
                @"strength|dexterity|intelligence|luck|exp)\s*" +
                @"([+\-]\d[\d,]*(?:\.\d+)?)\s*(%?)\s+for\s+" +
                @"(\d[\d,]*(?:\.\d+)?)\s*(sec(?:ond)?s?|min(?:ute)?s?|hours?|hrs?)$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            string stat = match.Groups[1].Value.ToLowerInvariant();
            string statChinese;
            if (!StatNames.TryGetValue(stat, out statChinese)) return false;
            string unit = match.Groups[5].Value.ToLowerInvariant();
            string durationUnit = unit.StartsWith("sec") ? "秒" :
                unit.StartsWith("hour") || unit.StartsWith("hr") ? "小时" : "分钟";
            chinese = match.Groups[4].Value + durationUnit + "内" + statChinese + " " +
                match.Groups[2].Value + match.Groups[3].Value + "。";
            return true;
        }

        private static bool TryTranslateImprove(string text, out string chinese)
        {
            chinese = "";
            Match match = Regex.Match(text, @"^improves?\s+(.+?)\s+on\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            string stat;
            string equipment;
            if (!TryStatChinese(match.Groups[1].Value.Trim(), out stat)) return false;
            if (!TryEquipmentChinese(match.Groups[2].Value.Trim(), out equipment)) return false;
            chinese = "可提升" + equipment + "的" + stat + "。";
            return true;
        }

        private static bool TryTranslateSuccessRate(string text, out string chinese)
        {
            chinese = "";
            Match match = Regex.Match(text, @"^success\s+rate\s*:\s*(\d[\d,]*(?:\.\d+)?)\s*%\s*,\s*(.+)$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            List<string> pieces = new List<string>();
            string list = match.Groups[2].Value.Trim();
            string[] parts = list.Split(',');
            foreach (string raw in parts)
            {
                Match stat = Regex.Match(raw.Trim(),
                    @"^(.+?)\s*\+?\s*([+\-]?\d[\d,]*(?:\.\d+)?)\s*(%?)$",
                    RegexOptions.IgnoreCase);
                if (!stat.Success) return false;
                string statChinese;
                if (!TryStatChinese(stat.Groups[1].Value.Trim(), out statChinese)) return false;
                pieces.Add(statChinese + stat.Groups[2].Value + stat.Groups[3].Value);
            }
            if (pieces.Count == 0) return false;
            chinese = "成功率：" + match.Groups[1].Value + "%，" +
                String.Join("，", pieces.ToArray()) + "。";
            return true;
        }

        private static bool TryTranslateFailure(string text, out string chinese)
        {
            chinese = "";
            Match destroyed = Regex.Match(text,
                @"^if\s+fail(?:ed)?\s*,?\s+the\s+item\s+will\s+be\s+destroyed" +
                @"(?:\s+at\s+a\s+(\d[\d,]*(?:\.\d+)?)\s*%\s+rate)?$",
                RegexOptions.IgnoreCase);
            if (destroyed.Success)
            {
                string rate = destroyed.Groups[1].Value;
                chinese = rate.Length > 0
                    ? "失败时，道具将以" + rate + "%的概率被摧毁。"
                    : "失败时，道具将被摧毁。";
                return true;
            }
            Match safe = Regex.Match(text,
                @"^if\s+fail(?:ed)?\s*,?\s+the\s+item\s+will\s+not\s+be\s+destroyed$",
                RegexOptions.IgnoreCase);
            if (safe.Success)
            {
                chinese = "失败时，道具不会被摧毁。"; return true;
            }
            return false;
        }

        private static bool TryStatChinese(string raw, out string chinese)
        {
            chinese = "";
            string key = Regex.Replace((raw ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            key = key.Replace(".", "");
            string direct;
            if (StatNames.TryGetValue(key, out direct)) { chinese = direct; return true; }
            return false;
        }

        private static bool TryEquipmentChinese(string raw, out string chinese)
        {
            chinese = "";
            string key = Regex.Replace((raw ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            key = key.Replace(".", "");
            string direct;
            if (EquipmentNames.TryGetValue(key, out direct)) { chinese = direct; return true; }
            return false;
        }

        internal static string ExplainRejectedTemplate(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length == 0) return "empty-visible-body";
            if (normalized.Contains("returns you to")) return "unknown-destination-or-shape";
            if (normalized.Contains("restore") || normalized.Contains("recover"))
                return "restore-shape-not-fully-consumed";
            if (normalized.Contains("improve") || normalized.Contains("success rate"))
                return "scroll-shape-not-fully-consumed";
            return "no-template-for-visible-body";
        }

        // The one entry point used by ordinary item panels. It first refuses any visible
        // sentence (or an OCR-near copy of one) whose dictionary pair is conflicted or
        // unverifiable, then tries the trusted full row, the trusted sentence index, the
        // complete-semantics templates and finally the scoped auxiliary vocabulary.
        internal static ItemBodyTranslation TranslateVisibleBody(TranslationStore store,
            string visible, string itemId)
        {
            ItemBodyTranslation result = new ItemBodyTranslation();
            string text = CleanVisible(visible);
            if (text.Length == 0 || store == null)
            {
                result.Reason = "empty-visible-body";
                return result;
            }
            List<TranslationEntry> candidates = CandidateDescriptions(store, itemId);
            if (candidates.Count == 0 && String.IsNullOrEmpty(itemId))
                candidates = store.GetAllItemDescriptions();
            if (candidates.Count == 0)
            {
                result.Reason = itemId.Length == 0 ? "no-item-descriptions" : "no-description-for-item";
                return result;
            }

            List<string> pieces = SplitSentences(text);
            if (pieces.Count == 0)
            {
                result.Reason = "empty-visible-body";
                return result;
            }

            // First try the complete trusted row. CanonicalSentence treats
            // Restore/Recover as the same item verb, but nothing else is rewritten.
            string reason;
            List<TranslationEntry> full = new List<TranslationEntry>();
            string canonicalVisible = CanonicalSentence(text);
            foreach (TranslationEntry entry in candidates)
            {
                if (!String.Equals(CanonicalSentence(entry.English), canonicalVisible,
                    StringComparison.Ordinal)) continue;
                full.Add(entry);
            }
            if (full.Count > 0)
            {
                string chinese;
                string resolvedId;
                bool shared;
                bool allTrusted = true;
                bool identicalSharedProse = true;
                foreach (TranslationEntry entry in full)
                {
                    if (entry.ItemStatus != ItemSemanticStatus.Trusted) allTrusted = false;
                    if (entry.ItemStatus != ItemSemanticStatus.Undecidable ||
                        entry.ItemStatusReason != "no-verifiable-gameplay-signal")
                        identicalSharedProse = false;
                }
                if (ResolveDistinctChinese(full, out chinese, out resolvedId, out shared, out reason) &&
                    (allTrusted || identicalSharedProse))
                {
                    result.Matched = true;
                    result.Translation = EnsureSentencePunctuation(chinese);
                    result.Source = allTrusted ? "item-description" : "item-description-shared";
                    result.ItemId = resolvedId;
                    result.Shared = shared;
                    result.Reason = allTrusted ? "full-row-match" : shared ?
                        "shared-identical-description" : "exact-plain-description";
                    return result;
                }
                if (!allTrusted && !identicalSharedProse)
                {
                    bool conflict = false;
                    foreach (TranslationEntry entry in full)
                        if (entry.ItemStatus == ItemSemanticStatus.Conflict) conflict = true;
                    result.Reason = conflict ? "data-conflict" : "data-unverified";
                }
                else result.Reason = reason.Length == 0 ? "data-unverified" : reason;
                return result;
            }

            string conflictReason;
            if (HasConflictingVisible(text, candidates, out conflictReason))
            {
                result.Reason = conflictReason;
                return result;
            }

            // A small OCR deviation in a complete row may still identify a trusted row.
            // It returns an estimated result so the planner can preserve the original
            // pixels instead of upgrading an estimate into a measured replacement.
            TranslationEntry estimated = null;
            foreach (TranslationEntry entry in candidates)
            {
                if (entry.ItemStatus != ItemSemanticStatus.Trusted) continue;
                if (Math.Abs(entry.Normalized.Length - TranslationStore.Normalize(text).Length) >
                    Math.Max(4, entry.Normalized.Length / 12)) continue;
                float similarity = TranslationStore.ItemCanonicalSimilarity(text, entry.English);
                if (similarity >= 0.94f)
                {
                    if (estimated != null && estimated.Chinese != entry.Chinese)
                    {
                        result.Reason = "ambiguous-estimated-description";
                        return result;
                    }
                    estimated = entry;
                }
            }
            if (estimated != null)
            {
                result.Matched = true;
                result.Estimated = true;
                result.Translation = EnsureSentencePunctuation(CleanChinese(estimated.Chinese));
                result.Source = "item-description-estimated";
                result.ItemId = TranslationStore.CategoryId(estimated.Category);
                result.Reason = "estimated-ocr-match";
                return result;
            }

            List<string> translated = new List<string>();
            HashSet<string> matchedIds = new HashSet<string>(StringComparer.Ordinal);
            bool matchedShared = false;
            bool usedTemplate = false;
            bool usedDictionary = false;
            for (int i = 0; i < pieces.Count; i++)
            {
                string piece = pieces[i].Trim();
                if (piece.Length == 0) continue;
                string aux = MatchAuxiliaryPhrase(piece);
                if (aux.Length > 0)
                {
                    translated.Add(EnsureSentencePunctuation(aux));
                    continue;
                }
                string template;
                string templateSource;
                if (TryTranslateTemplate(piece, store, out template, out templateSource))
                {
                    translated.Add(EnsureSentencePunctuation(template));
                    usedTemplate = true;
                    continue;
                }
                string canonical = CanonicalSentence(piece);
                List<ItemSentenceReference> references = store.FindItemSentenceReferences(canonical);
                if (!String.IsNullOrEmpty(itemId))
                {
                    references.RemoveAll(delegate(ItemSentenceReference reference) {
                        return TranslationStore.CategoryId(reference.Entry.Category) != itemId;
                    });
                }
                references.RemoveAll(delegate(ItemSentenceReference reference) {
                    return reference.Entry.ItemStatus != ItemSemanticStatus.Trusted;
                });
                if (references.Count == 0 && !String.IsNullOrEmpty(itemId))
                {
                    // Some rows have no sentence alignment; fall back to a scoped exact
                    // full-entry match only when the item ID is already known.
                    foreach (TranslationEntry entry in candidates)
                    {
                        if (entry.ItemStatus != ItemSemanticStatus.Trusted) continue;
                        if (!String.Equals(CanonicalSentence(entry.English), canonical,
                            StringComparison.Ordinal)) continue;
                        references.Add(new ItemSentenceReference { Entry = entry,
                            English = entry.English, Normalized = canonical,
                            Chinese = CleanChinese(entry.Chinese), Index = 0 });
                    }
                }
                if (references.Count == 0)
                {
                    bool truncated = i == pieces.Count - 1 &&
                        !HasTerminalPunctuation(text) || text.Contains("...") || text.Contains("…");
                    result.Reason = truncated ? "truncated-visible-tail" :
                        "unmatched-visible-content";
                    return result;
                }
                string chinese;
                string resolvedId;
                bool shared;
                if (!ResolveDistinctChinese(references, out chinese, out resolvedId, out shared,
                    out reason))
                {
                    result.Reason = reason;
                    return result;
                }
                translated.Add(EnsureSentencePunctuation(chinese));
                if (shared) matchedShared = true;
                if (resolvedId.Length > 0) matchedIds.Add(resolvedId);
                usedDictionary = true;
            }
            if (translated.Count == 0)
            {
                result.Reason = "unmatched-visible-content";
                return result;
            }
            StringBuilder combined = new StringBuilder();
            foreach (string value in translated) combined.Append(value);
            result.Matched = true;
            result.Translation = combined.ToString();
            result.Source = usedDictionary && usedTemplate ? "item-mixed" :
                usedTemplate ? "item-template" : "item-sentence";
            result.ItemId = !matchedShared && matchedIds.Count == 1 ? First(matchedIds) : "";
            result.Shared = matchedShared || matchedIds.Count > 1;
            result.Reason = "visible-sentence-match";
            return result;
        }

        private static string CleanVisible(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            string text = DecodeLineBreaks(value).Replace("\r", "\n");
            text = text.Replace("\n", " ");
            text = Regex.Replace(text, @"[ \t]+", " ");
            return text.Trim();
        }

        private static bool HasTerminalPunctuation(string text)
        {
            string trimmed = (text ?? "").TrimEnd();
            if (trimmed.Length == 0) return false;
            char last = trimmed[trimmed.Length - 1];
            return last == '.' || last == '!' || last == '?' || last == '。' ||
                last == '！' || last == '？';
        }

        private static string EnsureSentencePunctuation(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return text;
            char last = text[text.Length - 1];
            if (last == '.' || last == '!' || last == '?' || last == '。' ||
                last == '！' || last == '？') return text;
            return text + "。";
        }

        private static List<TranslationEntry> CandidateDescriptions(TranslationStore store,
            string itemId)
        {
            List<TranslationEntry> candidates;
            if (!String.IsNullOrEmpty(itemId))
                candidates = new List<TranslationEntry>(store.GetItemDescriptionsById(itemId));
            else
                candidates = new List<TranslationEntry>(store.GetAllItemDescriptions());
            candidates.RemoveAll(delegate(TranslationEntry entry) {
                return entry == null || !entry.IsOrdinaryItemText;
            });
            return candidates;
        }

        private static bool HasConflictingVisible(string text, List<TranslationEntry> candidates,
            out string reason)
        {
            reason = "";
            string canonicalFull = CanonicalSentence(text);
            List<string> sentences = SplitSentences(text);
            foreach (TranslationEntry entry in candidates)
            {
                if (entry == null || !entry.IsOrdinaryItemText) continue;
                bool exact = String.Equals(CanonicalSentence(entry.English), canonicalFull,
                    StringComparison.Ordinal);
                bool exactSentence = false;
                if (!exact)
                {
                    List<string> entrySentences = SplitSentences(entry.English);
                    foreach (string visibleSentence in sentences)
                    {
                        string visibleCanonical = CanonicalSentence(visibleSentence);
                        foreach (string entrySentence in entrySentences)
                        {
                            if (!String.Equals(CanonicalSentence(entrySentence), visibleCanonical,
                                StringComparison.Ordinal)) continue;
                            exactSentence = true; break;
                        }
                        if (exactSentence) break;
                    }
                }
                float similarity = 0.0f;
                bool near = false;
                if (!exact && !exactSentence)
                {
                    if (Math.Abs(entry.Normalized.Length - TranslationStore.Normalize(text).Length) <=
                        Math.Max(6, entry.Normalized.Length / 10))
                    {
                        similarity = TranslationStore.ItemCanonicalSimilarity(text, entry.English);
                        near = similarity >= 0.94f;
                    }
                }
                if (!exact && !exactSentence && !near) continue;
                if (entry.ItemStatus == ItemSemanticStatus.Trusted) continue;
                reason = entry.ItemStatus == ItemSemanticStatus.Conflict
                    ? "data-conflict" : "data-unverified";
                if (exact || exactSentence) return true;
                reason = entry.ItemStatus == ItemSemanticStatus.Conflict
                    ? "data-conflict-near-match" : "data-unverified-near-match";
                return true;
            }
            return false;
        }

        private static bool ResolveDistinctChinese(List<TranslationEntry> entries,
            out string chinese, out string itemId, out bool shared, out string reason)
        {
            chinese = ""; itemId = ""; shared = false; reason = "";
            HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in entries)
            {
                if (entry == null) continue;
                values.Add(CleanChinese(entry.Chinese));
                ids.Add(TranslationStore.CategoryId(entry.Category));
            }
            if (values.Count == 0) { reason = "no-translation"; return false; }
            if (values.Count > 1) { reason = "ambiguous-description"; return false; }
            foreach (string value in values) { chinese = value; break; }
            if (ids.Count == 1) itemId = First(ids); else shared = true;
            if (chinese.Length == 0) { reason = "empty-translation"; return false; }
            return true;
        }

        private static bool ResolveDistinctChinese(List<ItemSentenceReference> entries,
            out string chinese, out string itemId, out bool shared, out string reason)
        {
            chinese = ""; itemId = ""; shared = false; reason = "";
            HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemSentenceReference reference in entries)
            {
                if (reference == null || reference.Entry == null) continue;
                values.Add(CleanChinese(reference.Chinese));
                ids.Add(TranslationStore.CategoryId(reference.Entry.Category));
            }
            if (values.Count == 0) { reason = "no-translation"; return false; }
            if (values.Count > 1) { reason = "ambiguous-description"; return false; }
            foreach (string value in values) { chinese = value; break; }
            if (ids.Count == 1) itemId = First(ids); else shared = true;
            if (chinese.Length == 0) { reason = "empty-translation"; return false; }
            return true;
        }

        private static string First(ICollection<string> values)
        {
            foreach (string value in values) return value;
            return "";
        }

        private static SignalSet ExtractSignals(string text, bool chinese)
        {
            SignalSet set = new SignalSet();
            set.Text = text ?? "";
            List<TermHit> rawEffects = new List<TermHit>();
            EffectTerm[] terms = chinese ? ChineseEffects : EnglishEffects;
            foreach (EffectTerm term in terms)
            {
                foreach (Match match in term.Pattern.Matches(set.Text))
                {
                    if (!match.Success || match.Length <= 0) continue;
                    rawEffects.Add(new TermHit {
                        Key = term.Key, Start = match.Index, Length = match.Length });
                }
            }
            rawEffects.Sort(delegate(TermHit left, TermHit right) {
                int byStart = left.Start.CompareTo(right.Start);
                if (byStart != 0) return byStart;
                return right.Length.CompareTo(left.Length);
            });
            foreach (TermHit candidate in rawEffects)
            {
                bool overlaps = false;
                foreach (TermHit accepted in set.Effects)
                {
                    if (candidate.Start < accepted.End && accepted.Start < candidate.End)
                    { overlaps = true; break; }
                }
                if (!overlaps) set.Effects.Add(candidate);
            }
            set.Effects.Sort(delegate(TermHit left, TermHit right) {
                return left.Start.CompareTo(right.Start);
            });

            foreach (Match match in NumberPattern.Matches(set.Text))
            {
                if (!match.Success || match.Length == 0) continue;
                string raw = match.Value.Trim();
                bool percent = raw.EndsWith("%", StringComparison.Ordinal);
                string numeric = raw.TrimEnd('%').Replace(",", "").Trim();
                numeric = numeric.Replace('\u2212', '-');
                decimal value;
                if (!Decimal.TryParse(numeric, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out value)) continue;
                NumberSignal signal = new NumberSignal {
                    Value = value,
                    Raw = match.Value,
                    Canonical = CanonicalNumber(value, percent),
                    Percent = percent,
                    Start = match.Index,
                    Length = match.Length
                };
                if (percent) signal.Unit = "percent";
                set.Numbers.Add(signal);
            }
            foreach (NumberSignal signal in set.Numbers)
                BindNumber(set, signal, chinese);
            foreach (LogicTerm term in LogicTerms)
            {
                Regex pattern = chinese ? term.Chinese : term.English;
                if (!pattern.IsMatch(set.Text)) continue;
                if (!set.Logic.Contains(term.Key)) set.Logic.Add(term.Key);
            }
            return set;
        }

        private static void BindNumber(SignalSet set, NumberSignal signal, bool chinese)
        {
            Match duration = chinese ? ChineseDurationUnit.Match(set.Text.Substring(signal.End))
                : DurationUnit.Match(set.Text.Substring(signal.End));
            if (duration.Success && duration.Groups.Count > 0 && duration.Groups[0].Length > 0)
            {
                string unit = duration.Groups[0].Value.Trim().ToLowerInvariant();
                signal.Unit = unit.StartsWith("hour") || unit.StartsWith("hr") || unit.StartsWith("小时")
                    ? "hour" : unit.StartsWith("min") || unit.StartsWith("分") ? "min" : "sec";
                signal.Binding = "duration:" + signal.Unit;
                return;
            }
            string after = set.Text.Substring(signal.End);
            if (QuantityUnit.IsMatch(after))
            {
                signal.Unit = "quantity";
                signal.Binding = "quantity";
                return;
            }
            if (signal.Percent)
            {
                signal.Unit = "percent";
            }
            List<TermHit> effects = set.Effects;
            TermHit nearest = null;
            int nearestDistance = Int32.MaxValue;
            int nearestAfterCount = 0;
            int numberBetween = 0;
            foreach (TermHit effect in effects)
            {
                if (effect.Start >= signal.End)
                {
                    // Numbers between this value and a later effect mean the later effect
                    // owns the later number (800 HP and 800 MP).
                    string gap = set.Text.Substring(signal.End, effect.Start - signal.End);
                    numberBetween = 0;
                    foreach (Match dot in NumberPattern.Matches(gap)) numberBetween++;
                    if (numberBetween > 0) continue;
                    int distance = effect.Start - signal.End;
                    if (distance < nearestDistance)
                    {
                        nearest = effect; nearestDistance = distance; nearestAfterCount++;
                    }
                }
                else if (effect.End <= signal.Start)
                {
                    int gapStart = effect.End;
                    string gap = set.Text.Substring(gapStart, signal.Start - gapStart);
                    numberBetween = 0;
                    foreach (Match dot in NumberPattern.Matches(gap)) numberBetween++;
                    if (numberBetween > 0) continue;
                    int distance = signal.Start - effect.End;
                    if (distance < nearestDistance)
                    {
                        nearest = effect; nearestDistance = distance; nearestAfterCount = 0;
                    }
                }
            }
            if (nearest != null)
            {
                string nearestKey = nearest.Key == "weaponattack" ? "attack" : nearest.Key;
                signal.Binding = nearestKey;
                int afterCount = 0;
                List<string> shared = new List<string>();
                if (nearest.Start >= signal.End)
                {
                    foreach (TermHit effect in effects)
                    {
                        if (effect.Start < signal.End) continue;
                        bool otherNumberFirst = false;
                        foreach (NumberSignal other in set.Numbers)
                        {
                            if (other == signal) continue;
                            if (other.Start >= signal.End && other.Start < effect.Start)
                            { otherNumberFirst = true; break; }
                        }
                        if (otherNumberFirst) continue;
                        afterCount++;
                        string effectKey = effect.Key == "weaponattack" ? "attack" : effect.Key;
                        if (!shared.Contains(effectKey)) shared.Add(effectKey);
                    }
                }
                if (afterCount >= 2 && shared.Count == 2)
                    signal.Binding = String.Join("+", shared.ToArray());
                if (signal.Unit.Length > 0 && signal.Unit != "percent" &&
                    signal.Unit != "quantity" && afterCount < 2)
                    signal.Binding = nearestKey + ":" + signal.Unit;
                return;
            }
            signal.Binding = signal.Unit.Length > 0 ? signal.Unit : "unbound";
        }

        private static string CanonicalNumber(decimal value, bool percent)
        {
            string text = value.ToString("0.################", CultureInfo.InvariantCulture);
            return percent ? text + "%" : text;
        }

        private static List<string> DescribeNumberValues(SignalSet set)
        {
            List<string> values = new List<string>();
            HashSet<string> durationValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (NumberSignal signal in set.Numbers)
            {
                if (signal.Binding.StartsWith("duration:", StringComparison.Ordinal))
                {
                    // Several effects may share one visible "for 5 min" phrase. Collapse
                    // identical duration values; differing values (5 min + 10 min) stay
                    // distinct and are still compared.
                    if (durationValues.Add(signal.Binding + "=" + signal.Canonical))
                        values.Add(signal.Canonical);
                    continue;
                }
                if (signal.Binding.IndexOf('+') >= 0)
                {
                    // A shared quantity ("100 for both HP and MP") is semantically the
                    // same as two equal values in the Chinese row. Expand only verified
                    // shared bindings, never arbitrary duplicate numbers.
                    foreach (string part in signal.Binding.Split('+'))
                        if (part.Length > 0) values.Add(signal.Canonical);
                }
                else values.Add(signal.Canonical);
            }
            values.Sort(StringComparer.Ordinal);
            return values;
        }

        private static List<string> DescribeBindings(SignalSet set)
        {
            List<string> values = new List<string>();
            HashSet<string> durationBindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (NumberSignal signal in set.Numbers)
            {
                if (signal.Binding.StartsWith("duration:", StringComparison.Ordinal))
                {
                    if (durationBindings.Add(signal.Binding + "=" + signal.Canonical))
                        values.Add(signal.Binding + "=" + signal.Canonical);
                    continue;
                }
                if (signal.Binding.IndexOf('+') >= 0)
                {
                    foreach (string part in signal.Binding.Split('+'))
                        if (part.Length > 0) values.Add(part + "=" + signal.Canonical);
                }
                else values.Add(signal.Binding + "=" + signal.Canonical);
            }
            values.Sort(StringComparer.Ordinal);
            return values;
        }

        private static string DescribeNumbers(SignalSet set)
        {
            return String.Join(",", DescribeNumberValues(set).ToArray());
        }

        private static string DescribeEffects(SignalSet set)
        {
            List<string> values = SignificantEffects(set);
            values.Sort(StringComparer.Ordinal);
            return String.Join(",", values.ToArray());
        }

        private static int CountBindings(SignalSet set, string binding)
        {
            int count = 0;
            foreach (NumberSignal signal in set.Numbers)
                if (String.Equals(signal.Binding, binding, StringComparison.Ordinal)) count++;
            return count;
        }

        private static bool HasMeaningfulLogic(SignalSet en, SignalSet zh)
        {
            return en.Logic.Count > 0 || zh.Logic.Count > 0;
        }

        private static bool SameMultiset(List<string> left, List<string> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (!String.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static List<string> SignificantEffects(SignalSet set)
        {
            List<string> values = new List<string>();
            foreach (TermHit hit in set.Effects)
            {
                if (hit.Key == "quantity" || hit.Key == "price") continue;
                if (hit.Key == "weaponattack") hit.Key = "attack";
                if (!values.Contains(hit.Key)) values.Add(hit.Key);
            }
            values.Sort(StringComparer.Ordinal);
            return values;
        }

        private static bool LogicCompatible(SignalSet en, SignalSet zh, out string reason)
        {
            reason = "";
            foreach (string key in SignificantLogicKeys)
            {
                bool inEnglish = en.Logic.Contains(key);
                bool inChinese = zh.Logic.Contains(key);
                if (inEnglish != inChinese)
                {
                    // Cooldown prose commonly paraphrases "before it can be used again"
                    // as "使用后需要等待冷却时间". Both sides still state the same
                    // cooldown limit, so before/after alone cannot contradict it.
                    if ((key == "before" || key == "after") && en.Logic.Contains("cooldown") &&
                        zh.Logic.Contains("cooldown")) continue;
                    // "and" may be rendered as a Chinese comma or 、. "both" is
                    // covered by the same conjunction vocabulary. The effect/value
                    // bindings already prove that shared HP+MP quantities did not swap.
                    if (key == "both" && inEnglish && (zh.Text.Contains("和") ||
                        zh.Text.Contains("与") || zh.Text.Contains("及") ||
                        zh.Text.Contains("、") || zh.Text.Contains("，")))
                        continue;
                    reason = "logic-mismatch:" + key;
                    return false;
                }
            }
            return true;
        }
    }
}






