using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MapleOverlay
{
    internal static class Program
    {
        internal static bool Benchmark;
        internal static string BenchmarkIconPath;
        internal static string BenchmarkImagePath;
        internal static string BenchmarkText;
        internal static bool BenchmarkUi;
        internal static int BenchmarkBestIconDistance = 65;
        internal static string BenchmarkBestIcon = "";
        internal static string BenchmarkCurrentArea = "";
        internal static System.Drawing.Point BenchmarkCursor = System.Drawing.Point.Empty;
        [STAThread]
        private static void Main(string[] args)
        {
            // .NET Framework 4.x on older Windows may otherwise negotiate TLS 1.0,
            // which GitHub and Hugging Face no longer accept.
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 4;
            Benchmark = args != null && Array.IndexOf(args, "--benchmark") >= 0;
            BenchmarkUi = args != null && Array.IndexOf(args, "--benchmark-ui") >= 0;
            if (args != null) foreach (string arg in args)
                if (arg.StartsWith("--benchmark-icon=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkIconPath = arg.Substring("--benchmark-icon=".Length);
                else if (arg.StartsWith("--benchmark-image=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkImagePath = arg.Substring("--benchmark-image=".Length);
                else if (arg.StartsWith("--benchmark-text=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkText = arg.Substring("--benchmark-text=".Length);
                else if (arg.StartsWith("--benchmark-cursor=", StringComparison.OrdinalIgnoreCase))
                {
                    string[] xy = arg.Substring("--benchmark-cursor=".Length).Split(',');
                    int x, y;
                    if (xy.Length == 2 && Int32.TryParse(xy[0], out x) && Int32.TryParse(xy[1], out y))
                        BenchmarkCursor = new System.Drawing.Point(x, y);
                }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && Array.IndexOf(args, "--dictionary-ui-test") >= 0)
            {
                using (DictionaryOnlyForm editor = new DictionaryOnlyForm(null, AppDomain.CurrentDomain.BaseDirectory))
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dictionary_ui_test.txt"),
                        editor.RunCategorySelfTest(), Encoding.UTF8);
                return;
            }
            try { Application.Run(new OverlayForm()); }
            catch (Exception ex)
            {
                MessageBox.Show("程序发生错误：\n\n" + ex.Message,
                    "枫语幕", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class TranslationEntry
    {
        public string English;
        public string Chinese;
        public string Category;
        public string Normalized;
        public ulong IconHash;
        public bool HasIcon;
        public string TaskId;
        public bool IsTaskName;
        public bool IsTaskText;
        public bool IsSkillText;
        public bool IsItemText;
        public bool IsInterfaceText;
        public HashSet<string> DetailWords;
    }

    internal sealed class TranslationStore
    {
        private readonly string sourcePath;
        private readonly Dictionary<char, List<TranslationEntry>> buckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> taskBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> skillTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> itemTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> interfaceTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> iconTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly List<TranslationEntry> taskNames = new List<TranslationEntry>();
        private readonly List<TranslationEntry> taskEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> iconEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> skillTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> itemTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> interfaceTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> uiEntries = new List<TranslationEntry>();
        private string cachedIconText = "";
        private readonly List<KeyValuePair<TranslationEntry, float>> cachedIconCandidates =
            new List<KeyValuePair<TranslationEntry, float>>();
        public int Count { get; private set; }
        public int IconCount { get { return iconEntries.Count; } }
        public int TaskTextCount { get; private set; }
        public int SkillTextCount { get; private set; }
        public int ItemTextCount { get; private set; }
        public int TaskCount { get { return taskNames.Count; } }

        public TranslationStore(string source)
        {
            sourcePath = source;
        }

        public void Load()
        {
            List<TranslationEntry> entries = ReadTsv(sourcePath);

            // The classic data contains legitimate same-English-name variants (hair colours,
            // job-specific skills, duplicate transport NPCs, and so on).  If their Chinese
            // translations differ, plain OCR cannot choose safely. Keep every variant in the
            // icon index, but require icon + text matching instead of accepting an arbitrary
            // text-only translation.
            Dictionary<string, HashSet<string>> translationsByEnglish =
                new Dictionary<string, HashSet<string>>();
            foreach (TranslationEntry entry in entries)
            {
                if (entry.IsTaskName || entry.IsTaskText || entry.IsSkillText || entry.IsItemText) continue;
                HashSet<string> values;
                if (!translationsByEnglish.TryGetValue(entry.Normalized, out values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    translationsByEnglish.Add(entry.Normalized, values);
                }
                values.Add(entry.Chinese);
            }

            buckets.Clear();
            taskBuckets.Clear();
            skillTextBuckets.Clear();
            itemTextBuckets.Clear();
            interfaceTextBuckets.Clear();
            iconTextBuckets.Clear();
            taskNames.Clear();
            taskEntries.Clear();
            iconEntries.Clear();
            skillTextEntries.Clear();
            itemTextEntries.Clear();
            interfaceTextEntries.Clear();
            uiEntries.Clear();
            TaskTextCount = 0;
            SkillTextCount = 0;
            ItemTextCount = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (entry.Normalized.Length == 0) continue;
                if (entry.Category.StartsWith("怀旧服-界面", StringComparison.Ordinal))
                    uiEntries.Add(entry);
                if (entry.IsTaskName || entry.IsTaskText)
                {
                    AddToBucket(taskBuckets, entry);
                    taskEntries.Add(entry);
                    if (entry.IsTaskName) taskNames.Add(entry);
                    if (entry.IsTaskText) TaskTextCount++;
                }
                else if (entry.IsSkillText)
                {
                    AddToBucket(skillTextBuckets, entry); skillTextEntries.Add(entry); SkillTextCount++;
                }
                else if (entry.IsItemText)
                {
                    AddToBucket(itemTextBuckets, entry); itemTextEntries.Add(entry); ItemTextCount++;
                }
                else if (entry.IsInterfaceText)
                {
                    AddToBucket(interfaceTextBuckets, entry); interfaceTextEntries.Add(entry);
                }
                else
                {
                    // Player-chat slang belongs to the dedicated AI chat pipeline. Keeping it out
                    // of the F8 screen overlay prevents short fragments such as "wth" from being
                    // painted over unrelated UI or notification text.
                    if (entry.Category.StartsWith("怀旧服-聊天", StringComparison.Ordinal)) continue;
                    HashSet<string> values;
                    bool ambiguous = translationsByEnglish.TryGetValue(entry.Normalized, out values) &&
                        values.Count > 1;
                    if (!ambiguous) AddToBucket(buckets, entry);
                    if (entry.HasIcon)
                    {
                        iconEntries.Add(entry);
                        AddToBucket(iconTextBuckets, entry);
                    }
                }
            }
            SortBuckets(buckets);
            SortBuckets(taskBuckets);
            SortBuckets(skillTextBuckets);
            SortBuckets(itemTextBuckets);
            SortBuckets(interfaceTextBuckets);
            SortBuckets(iconTextBuckets);
            cachedIconText = "";
            cachedIconCandidates.Clear();
            Count = entries.Count;
        }

        private static void AddToBucket(Dictionary<char, List<TranslationEntry>> target, TranslationEntry entry)
        {
            char key = entry.Normalized[0];
            List<TranslationEntry> list;
            if (!target.TryGetValue(key, out list))
            {
                list = new List<TranslationEntry>();
                target.Add(key, list);
            }
            list.Add(entry);
        }

        private static void SortBuckets(Dictionary<char, List<TranslationEntry>> target)
        {
            foreach (List<TranslationEntry> list in target.Values)
                list.Sort(delegate(TranslationEntry a, TranslationEntry b) {
                    return b.Normalized.Length.CompareTo(a.Normalized.Length);
                });
        }

        public List<MatchResult> FindMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, buckets, null);
            return exact.Count > 0 ? exact : FindApproximateNamedMatch(text);
        }

        private List<MatchResult> FindApproximateNamedMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 5 || normalized.Length > 40 ||
                normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 5)
                return none;
            List<TranslationEntry> candidates;
            if (!iconTextBuckets.TryGetValue(normalized[0], out candidates)) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            HashSet<string> compared = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in candidates)
            {
                string identity = entry.Normalized + "\t" + entry.Chinese;
                if (!compared.Add(identity) || Math.Abs(entry.Normalized.Length - normalized.Length) > 7) continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                float required = Math.Min(normalized.Length, entry.Normalized.Length) <= 7 ? 0.84f : 0.77f;
                if (score < required) continue;
                if (score > bestScore) { secondScore = bestScore; bestScore = score; best = entry; }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.06f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        public List<MatchResult> FindTaskMatches(string text, string taskId)
        {
            List<MatchResult> exact = FindInBuckets(text, taskBuckets, taskId);
            if (exact.Count > 0) return exact;
            if (String.IsNullOrEmpty(taskId)) return FindApproximateTaskNameMatch(text);
            return FindApproximateTaskMatch(text, taskId);
        }

        public List<MatchResult> FindSkillTextMatches(string text, string detailId = null)
        {
            List<MatchResult> exact = FindInBuckets(text, skillTextBuckets, null);
            if (!String.IsNullOrEmpty(detailId)) exact.RemoveAll(delegate(MatchResult match) { return CategoryId(match.Entry.Category) != detailId; });
            return exact.Count > 0 ? exact : FindApproximateDetailMatch(text, skillTextEntries, detailId);
        }

        public List<MatchResult> FindItemTextMatches(string text, string detailId = null)
        {
            List<MatchResult> exact = FindInBuckets(text, itemTextBuckets, null);
            if (!String.IsNullOrEmpty(detailId)) exact.RemoveAll(delegate(MatchResult match) { return CategoryId(match.Entry.Category) != detailId; });
            return exact.Count > 0 ? exact : FindApproximateDetailMatch(text, itemTextEntries, detailId);
        }

        public List<MatchResult> FindInterfaceTextMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, interfaceTextBuckets, null);
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-界面", StringComparison.Ordinal))
                    exact.Add(match);
            string normalized = Normalize(text);
            int longestExact = 0;
            foreach (MatchResult match in exact)
                longestExact = Math.Max(longestExact, match.Entry.Normalized.Length);
            if (exact.Count > 0 && (normalized.Length <= 40 ||
                longestExact * 2 >= normalized.Length)) return exact;
            // A long dialogue may contain a short exact place/item token. That token must
            // not suppress a much better fuzzy match for the complete sentence.
            List<MatchResult> longPhrase = FindApproximateInterfaceMatch(text);
            if (longPhrase.Count > 0) return longPhrase;
            if (exact.Count > 0) return exact;
            List<MatchResult> shortUi = FindApproximateShortUiMatch(text);
            return shortUi.Count > 0 ? shortUi : longPhrase;
        }

        private List<MatchResult> FindApproximateShortUiMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 4 || normalized.Length > 34) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in uiEntries)
            {
                int length = entry.Normalized.Length;
                if (length < 4 || length > 30 || Math.Abs(length - normalized.Length) > 7) continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                float required = Math.Min(length, normalized.Length) <= 7 ? 0.70f : 0.76f;
                if (score < required) continue;
                if (score > bestScore)
                {
                    secondScore = bestScore; bestScore = score; best = entry;
                }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.08f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        public List<MatchResult> FindCharacterStatMatches(string text)
        {
            List<MatchResult> result = FindInBuckets(text, buckets, null);
            string normalized = Normalize(text);
            if (normalized.Length == 0) return result;
            string[] keys = new string[] { "character stat", "character info", "ability point",
                "weapon def", "magic def", "crit damage", "crit rate", "accuracy", "evasion",
                "attack", "magic", "speed", "jump", "level", "name", "job", "fame",
                "exp", "str", "dex", "int", "luk", "hp", "mp" };
            string[] chinese = new string[] { "角色属性", "角色信息", "能力值点数",
                "物理防御力", "魔法防御力", "暴击伤害", "暴击率", "命中率", "回避率",
                "攻击力", "魔法攻击力", "移动速度", "跳跃力", "等级", "名称", "职业", "人气",
                "经验", "力量", "敏捷", "智力", "运气", "生命值", "魔法值" };
            foreach (MatchResult existing in result)
                if (existing.Start == 0) return result;
            foreach (int index in CharacterStatKeyOrder(keys))
            {
                string key = keys[index];
                int take = Math.Min(normalized.Length, key.Length + 2);
                string prefix = normalized.Substring(0, take);
                int separator = prefix.IndexOf(' ');
                if (key.IndexOf(' ') < 0 && separator >= 0) prefix = prefix.Substring(0, separator);
                else if (key.IndexOf(' ') >= 0)
                {
                    string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int wanted = key.Split(' ').Length;
                    if (words.Length >= wanted) prefix = String.Join(" ", words, 0, wanted);
                }
                int allowed = key.Length <= 3 ? 1 : (key.Length <= 8 ? 2 : 3);
                if (EditDistance(prefix, key, allowed) > allowed) continue;
                TranslationEntry entry = new TranslationEntry {
                    English = prefix, Chinese = chinese[index], Category = "怀旧服-界面",
                    Normalized = prefix
                };
                result.Insert(0, new MatchResult { Entry = entry, Start = 0, Length = prefix.Length });
                break;
            }
            return result;
        }

        private static IEnumerable<int> CharacterStatKeyOrder(string[] keys)
        {
            List<int> order = new List<int>();
            for (int i = 0; i < keys.Length; i++) order.Add(i);
            order.Sort(delegate(int left, int right) { return keys[right].Length.CompareTo(keys[left].Length); });
            return order;
        }

        private static int EditDistance(string left, string right, int stopAfter)
        {
            if (Math.Abs(left.Length - right.Length) > stopAfter) return stopAfter + 1;
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i; int rowBest = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                    rowBest = Math.Min(rowBest, current[j]);
                }
                if (rowBest > stopAfter) return stopAfter + 1;
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }

        private List<MatchResult> FindApproximateInterfaceMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 12) return none;
            HashSet<string> inputWords = SignificantWords(normalized);
            if (inputWords.Count < 2) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in interfaceTextEntries)
            {
                HashSet<string> candidateWords = SignificantWords(entry.Normalized);
                if (candidateWords.Count == 0) continue;
                int common = 0;
                foreach (string word in inputWords) if (candidateWords.Contains(word)) common++;
                bool longPhrase = candidateWords.Count >= 5;
                if (common < (longPhrase ? 3 : Math.Max(2, candidateWords.Count - 1))) continue;
                float precision = (float)common / Math.Max(1, inputWords.Count);
                float coverage = (float)common / Math.Max(1, candidateWords.Count);
                if (longPhrase ? (coverage < 0.38f || precision < 0.28f) :
                    (coverage < 0.72f || precision < 0.45f)) continue;
                float score = coverage * 0.68f + precision * 0.32f;
                if (score > bestScore)
                {
                    if (best != null && best != entry) secondScore = Math.Max(secondScore, bestScore);
                    best = entry; bestScore = score;
                }
                else if (best != entry) secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.04f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        private List<MatchResult> FindApproximateTaskMatch(string text, string taskId)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 28) return none;
            HashSet<string> inputWords = SignificantWords(normalized);
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in taskEntries)
            {
                if (!entry.IsTaskText || entry.TaskId != taskId || entry.DetailWords == null) continue;
                HashSet<string> candidateWords = SignificantWords(entry.Normalized);
                int common = 0;
                foreach (string word in inputWords) if (candidateWords.Contains(word)) common++;
                if (common < 7) continue;
                float precision = (float)common / Math.Max(1, inputWords.Count);
                float coverage = (float)common / Math.Max(1, candidateWords.Count);
                if (precision < 0.35f || coverage < 0.45f) continue;
                float score = precision * 0.42f + coverage * 0.58f;
                if (score > bestScore)
                {
                    if (best != null && best != entry) secondScore = Math.Max(secondScore, bestScore);
                    best = entry; bestScore = score;
                }
                else if (best != entry) secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.035f)) return none;
            return new List<MatchResult> { new MatchResult { Entry = best, Start = 0, Length = normalized.Length } };
        }

        private List<MatchResult> FindApproximateTaskNameMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 5 || normalized.Length > 72) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in taskNames)
            {
                if (Math.Abs(entry.Normalized.Length - normalized.Length) > Math.Max(5, entry.Normalized.Length / 3))
                    continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                if (score < 0.72f) continue;
                if (score > bestScore) { secondScore = bestScore; bestScore = score; best = entry; }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.07f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        private static HashSet<string> SignificantWords(string normalized)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (word.Length >= 3) result.Add(word);
            return result;
        }

        private static List<MatchResult> FindApproximateDetailMatch(string text, List<TranslationEntry> entries, string detailId = null)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 18) return none;
            string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> input = new HashSet<string>(words, StringComparer.Ordinal);
            TranslationEntry best = null; float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (!String.IsNullOrEmpty(detailId) && CategoryId(entry.Category) != detailId) continue;
                HashSet<string> candidate = entry.DetailWords;
                if (candidate == null) continue;
                int common = 0;
                foreach (string word in input) if (candidate.Contains(word)) common++;
                if (common < 5) continue;
                float precision = (float)common / Math.Max(1, input.Count);
                float coverage = (float)common / Math.Max(1, candidate.Count);
                if (precision < 0.68f || coverage < 0.25f) continue;
                float score = precision * 0.72f + coverage * 0.28f;
                if (entry.Normalized.Contains(normalized)) score += 0.18f;
                if (score > bestScore)
                {
                    if (best != null && best.Category != entry.Category)
                        secondScore = Math.Max(secondScore, bestScore);
                    bestScore = score; best = entry;
                }
                else if ((best == null || best.Category != entry.Category) && score > secondScore)
                    secondScore = score;
            }
            if (best == null || bestScore < 0.68f || (secondScore > 0 && bestScore - secondScore < 0.035f)) return none;
            return new List<MatchResult> { new MatchResult { Entry = best, Start = 0, Length = normalized.Length } };
        }

        internal static string CategoryId(string category)
        {
            int marker = category == null ? -1 : category.LastIndexOf('#');
            return marker >= 0 && marker + 1 < category.Length ? category.Substring(marker + 1) : "";
        }

        public bool LooksLikeSkillTextStart(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length < 3) return false;
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal)) return true;
            if (normalized.Length < 8) return false;
            List<TranslationEntry> candidates;
            if (skillTextBuckets.TryGetValue(normalized[0], out candidates))
            {
                foreach (TranslationEntry candidate in candidates)
                {
                    if (candidate.Normalized.StartsWith(normalized, StringComparison.Ordinal) ||
                        normalized.StartsWith(candidate.Normalized, StringComparison.Ordinal)) return true;
                }
            }
            return HasStrongDetailFragment(text, skillTextEntries) ||
                FindApproximateDetailMatch(text, skillTextEntries).Count > 0;
        }


        public bool LooksLikeItemTextStart(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length < 3) return false;
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal)) return true;
            if (normalized.Length < 8) return false;
            List<TranslationEntry> candidates;
            if (itemTextBuckets.TryGetValue(normalized[0], out candidates))
            {
                foreach (TranslationEntry candidate in candidates)
                {
                    if (candidate.Normalized.StartsWith(normalized, StringComparison.Ordinal) ||
                        normalized.StartsWith(candidate.Normalized, StringComparison.Ordinal)) return true;
                }
            }
            return HasStrongDetailFragment(text, itemTextEntries) ||
                FindApproximateDetailMatch(text, itemTextEntries).Count > 0;
        }

        public string DetectSkillId(string text)
        {
            return DetectNamedDetailId(text, "怀旧服-技能#");
        }

        public string DetectSkillContentId(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, skillTextBuckets, null);
            MatchResult best = null;
            foreach (MatchResult match in exact)
                if (best == null || match.Entry.Normalized.Length > best.Entry.Normalized.Length)
                    best = match;
            if (best != null) return CategoryId(best.Entry.Category);

            List<MatchResult> approximate = FindApproximateDetailMatch(text, skillTextEntries);
            return approximate.Count > 0 ? CategoryId(approximate[0].Entry.Category) : "";
        }

        public string DetectItemId(string text)
        {
            string id = DetectNamedDetailId(text, "怀旧服-装备#");
            return id.Length > 0 ? id : DetectNamedDetailId(text, "怀旧服-道具#");
        }

        public TranslationEntry GetSkillOverview(string detailId)
        {
            return GetLongestDetailEntry(skillTextEntries, detailId);
        }

        public TranslationEntry GetItemOverview(string detailId)
        {
            return GetLongestDetailEntry(itemTextEntries, detailId);
        }

        private static TranslationEntry GetLongestDetailEntry(List<TranslationEntry> source,
            string detailId)
        {
            if (String.IsNullOrEmpty(detailId)) return null;
            TranslationEntry best = null;
            foreach (TranslationEntry entry in source)
            {
                if (CategoryId(entry.Category) != detailId) continue;
                // The overview is the longest prose entry for an ID. Level rows are
                // deliberately shorter and remain matched from their visible numbers.
                if (best == null || entry.Normalized.Length > best.Normalized.Length)
                    best = entry;
            }
            return best;
        }

        private string DetectNamedDetailId(string text, string categoryPrefix)
        {
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith(categoryPrefix, StringComparison.Ordinal))
                    return CategoryId(match.Entry.Category);
            return "";
        }

        private static bool HasStrongDetailFragment(string text, List<TranslationEntry> entries)
        {
            string normalized = Normalize(text);
            if (normalized.Length < 18) return false;
            HashSet<string> input = SignificantWords(normalized);
            if (input.Count < 5) return false;
            foreach (TranslationEntry entry in entries)
            {
                if (entry.DetailWords == null) continue;
                int common = 0;
                foreach (string word in input) if (entry.DetailWords.Contains(word)) common++;
                if (common >= 5 && (float)common / input.Count >= 0.58f) return true;
            }
            return false;
        }

        public bool HasDetailCandidate(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Contains("master level") || normalized.Contains("next level") ||
                normalized.Contains("enhancements") || normalized.Contains("req lev") ||
                normalized.Contains("required level")) return true;
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal)) return true;
            return false;
        }

        public string DetectTaskId(string text)
        {
            string normalized = Normalize(text);
            Dictionary<string, int> scores = new Dictionary<string, int>();
            foreach (TranslationEntry entry in taskEntries)
            {
                int position = normalized.IndexOf(entry.Normalized, StringComparison.Ordinal);
                if (position < 0) continue;
                if (!IsBoundary(normalized, position - 1) || !IsBoundary(normalized, position + entry.Normalized.Length)) continue;
                int score = entry.Normalized.Length * (entry.IsTaskText ? 4 : 1);
                int current;
                scores.TryGetValue(entry.TaskId, out current);
                scores[entry.TaskId] = current + score;
            }
            string bestId = "";
            int bestScore = 0;
            bool tied = false;
            foreach (KeyValuePair<string, int> pair in scores)
            {
                if (pair.Value > bestScore) { bestId = pair.Key; bestScore = pair.Value; tied = false; }
                else if (pair.Value == bestScore) tied = true;
            }
            if (!tied && bestId.Length > 0) return bestId;
            List<MatchResult> approximate = FindApproximateTaskNameMatch(text);
            return approximate.Count > 0 ? approximate[0].Entry.TaskId : "";
        }

        public bool LooksLikeQuestInterface(string text)
        {
            if (!String.IsNullOrEmpty(DetectTaskId(text))) return true;
            string normalized = Normalize(text);
            bool questAnchor = normalized.Contains("quest") &&
                (normalized.Contains("accept") || normalized.Contains("decline") ||
                 normalized.Contains("reward") || normalized.Contains("complete") ||
                 normalized.Contains("in progress"));
            if (questAnchor) return true;
            List<MatchResult> matches = FindTaskMatches(text, null);
            foreach (MatchResult match in matches) if (match.Length >= 20) return true;
            return false;
        }

        private static List<MatchResult> FindInBuckets(string text,
            Dictionary<char, List<TranslationEntry>> source, string taskId)
        {
            string normalized = Normalize(text);
            List<MatchResult> results = new List<MatchResult>();
            int position = 0;
            while (position < normalized.Length)
            {
                List<TranslationEntry> candidates;
                TranslationEntry best = null;
                if (source.TryGetValue(normalized[position], out candidates))
                {
                    foreach (TranslationEntry candidate in candidates)
                    {
                        if (!String.IsNullOrEmpty(taskId) && candidate.TaskId != taskId) continue;
                        if (position + candidate.Normalized.Length > normalized.Length) continue;
                        if (string.CompareOrdinal(normalized, position, candidate.Normalized, 0,
                            candidate.Normalized.Length) != 0) continue;
                        if (!IsBoundary(normalized, position - 1) ||
                            !IsBoundary(normalized, position + candidate.Normalized.Length)) continue;
                        best = candidate;
                        break;
                    }
                }
                if (best == null) { position++; continue; }
                results.Add(new MatchResult { Entry = best, Start = position, Length = best.Normalized.Length });
                position += best.Normalized.Length;
            }
            return results;
        }

        public IconMatchResult FindIconAssistedMatch(ulong hash, string recognizedText)
        {
            string normalizedText = Normalize(recognizedText);
            PrepareIconCandidates(normalizedText);
            if (cachedIconCandidates.Count == 0) return null;
            IconMatchResult best = null;
            float secondScore = Single.MinValue;
            foreach (KeyValuePair<TranslationEntry, float> pair in cachedIconCandidates)
            {
                TranslationEntry entry = pair.Key;
                int distance = HammingDistance(hash, entry.IconHash);
                float similarity = pair.Value;
                if (Program.Benchmark && similarity >= 0.75f)
                {
                    if (distance < Program.BenchmarkBestIconDistance)
                    {
                        Program.BenchmarkBestIconDistance = distance;
                        Program.BenchmarkBestIcon = entry.English + "@" + Program.BenchmarkCurrentArea;
                    }
                }
                if (distance > 24) continue;
                if (similarity < 0.58f) continue;
                // A noisy icon is accepted only when the OCR text is already a strong fuzzy match.
                if (distance > 9 && similarity < 0.78f) continue;
                float score = similarity * 22.0f - distance * 0.45f;
                if (best == null || score > best.Score)
                {
                    if (best != null) secondScore = Math.Max(secondScore, best.Score);
                    best = new IconMatchResult { Entry = entry, Score = score, IconDistance = distance };
                }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || best.Score < 9.0f) return null;
            if (secondScore != Single.MinValue && best.Score - secondScore < 1.0f) return null;
            return best;
        }

        public bool HasPlausibleIconText(string recognizedText)
        {
            PrepareIconCandidates(Normalize(recognizedText));
            return cachedIconCandidates.Count > 0;
        }

        private void PrepareIconCandidates(string normalizedText)
        {
            if (normalizedText == cachedIconText) return;
            cachedIconText = normalizedText;
            cachedIconCandidates.Clear();
            if (normalizedText.Length < 3) return;
            List<TranslationEntry> candidates;
            if (!iconTextBuckets.TryGetValue(normalizedText[0], out candidates)) return;
            foreach (TranslationEntry entry in candidates)
            {
                float lengthRatio = (float)Math.Min(normalizedText.Length, entry.Normalized.Length) /
                    Math.Max(normalizedText.Length, entry.Normalized.Length);
                if (lengthRatio < 0.45f) continue;
                float similarity = TextSimilarity(normalizedText, entry.Normalized);
                if (similarity >= 0.58f)
                    cachedIconCandidates.Add(new KeyValuePair<TranslationEntry, float>(entry, similarity));
            }
        }

        private static int HammingDistance(ulong left, ulong right)
        {
            ulong value = left ^ right;
            int count = 0;
            while (value != 0) { value &= value - 1; count++; }
            return count;
        }

        private static float TextSimilarity(string left, string right)
        {
            if (left == right) return 1.0f;
            if (left.Length == 0 || right.Length == 0) return 0.0f;
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous; previous = current; current = swap;
            }
            return 1.0f - (float)previous[right.Length] / Math.Max(left.Length, right.Length);
        }

        public static string Normalize(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder b = new StringBuilder(value.Length);
            bool space = false;
            foreach (char raw in value.Trim().ToLowerInvariant())
            {
                char c = raw == '\u2019' || raw == '\u2018' ? '\'' : raw;
                bool keep = Char.IsLetterOrDigit(c) || c == '\'' || c == '+' || c == '%' || c == '#';
                if (!keep)
                {
                    if (!space && b.Length > 0) b.Append(' ');
                    space = true;
                }
                else
                {
                    b.Append(c);
                    space = false;
                }
            }
            string normalized = b.ToString().Trim();
            // Keep apostrophes inside names (Biggs's), but discard quote-like OCR
            // decoration at token edges ('LERVE STORE) before whole-token repair.
            normalized = Regex.Replace(normalized,
                @"(?<![a-z0-9])'+|'+(?![a-z0-9])", " ");
            if (normalized.Length == 0) return normalized;
            string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                // Conservative whole-token fixes for the recurring Classic UI OCR shapes.
                // They improve panel discovery without changing player names or free-form chat.
                if (words[i] == "ouest" || words[i] == "ojest") words[i] = "quest";
                else if (words[i] == "lerve") words[i] = "leave";
                else if (words[i] == "pethils") words[i] = "details";
                else if (words[i] == "reo" || words[i] == "aeq") words[i] = "req";
                else if (words[i] == "tor") words[i] = "for";
                else if (words[i] == "tun") words[i] = "fun";
            }
            normalized = String.Join(" ", words);
            normalized = normalized.Replace("jest helper", "quest helper")
                .Replace("uest helper", "quest helper");
            return normalized;
        }

        private static bool IsBoundary(string text, int at)
        {
            if (at < 0 || at >= text.Length) return true;
            char c = text[at];
            return !Char.IsLetterOrDigit(c) && c != '\'';
        }

        private static List<TranslationEntry> ReadTsv(string path)
        {
            List<TranslationEntry> result = new List<TranslationEntry>();
            if (!File.Exists(path)) throw new FileNotFoundException("找不到词库", path);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 2) continue;
                string english = parts[0].Trim();
                string chinese = PolishChinese(parts[1].Trim());
                string normalized = Normalize(english);
                string category = parts.Length > 2 ? parts[2].Trim() : "";
                bool isTaskName = category.StartsWith("怀旧服-任务#", StringComparison.Ordinal);
                bool isTaskText = category.StartsWith("怀旧服-任务说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-任务对白#", StringComparison.Ordinal);
                bool isSkillText = category.StartsWith("怀旧服-技能说明#", StringComparison.Ordinal);
                bool isItemText = category.StartsWith("怀旧服-装备说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-物品说明#", StringComparison.Ordinal);
                bool isInterfaceText = category.StartsWith("怀旧服-界面长句", StringComparison.Ordinal);
                string taskId = (isTaskName || isTaskText) && category.LastIndexOf('#') >= 0
                    ? category.Substring(category.LastIndexOf('#') + 1) : "";
                ulong iconHash = 0;
                bool hasIcon = parts.Length > 3 && UInt64.TryParse(parts[3].Trim(),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iconHash);
                // Icon-backed entries are distinct by source category/ID even when their English
                // names are identical. This preserves colour, appearance, job and duplicate-NPC
                // variants for icon-assisted disambiguation.
                string dedupeScope = (isTaskName || isTaskText) ? taskId :
                    ((hasIcon || isSkillText || isItemText || isInterfaceText) && category.Length > 0 ? category : "general");
                string dedupeKey = normalized + "\t" + dedupeScope;
                if (normalized.Length == 0 || chinese.Length == 0 || !seen.Add(dedupeKey)) continue;
                result.Add(new TranslationEntry {
                    English = english, Chinese = chinese,
                    Category = category, Normalized = normalized,
                    IconHash = hasIcon ? iconHash : 0, HasIcon = hasIcon,
                    TaskId = taskId, IsTaskName = isTaskName, IsTaskText = isTaskText,
                    IsSkillText = isSkillText, IsItemText = isItemText, IsInterfaceText = isInterfaceText,
                    DetailWords = (isTaskText || isSkillText || isItemText || isInterfaceText)
                        ? new HashSet<string>(normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
                        : null
                });
            }
            return result;
        }

        private static string PolishChinese(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            string text = value.Replace("Meso", "金币").Replace("meso", "金币")
                .Replace("DEX", "敏捷").Replace("STR", "力量")
                .Replace("INT", "智力").Replace("LUK", "运气")
                .Replace("8x伤害", "8倍伤害");
            text = Regex.Replace(text, "向(\\d+)投掷金币造成(\\d+)倍伤害",
                "投掷$1金币，造成$2倍伤害");
            text = Regex.Replace(text, "基于敏捷减少金币消耗量的(\\d+)%",
                "根据敏捷，有几率使金币消耗减少$1%");
            return text;
        }

        private static void WriteBinary(string path, List<TranslationEntry> entries)
        {
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(new byte[] { 77, 83, 68, 49 }); // MSD1
                writer.Write(entries.Count);
                foreach (TranslationEntry e in entries)
                {
                    writer.Write(e.English);
                    writer.Write(e.Chinese);
                    writer.Write(e.Category ?? "");
                }
            }
        }

        private static List<TranslationEntry> ReadBinary(string path)
        {
            List<TranslationEntry> result = new List<TranslationEntry>();
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                byte[] magic = reader.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 77 || magic[1] != 83 || magic[2] != 68 || magic[3] != 49)
                    throw new InvalidDataException("词库缓存版本不兼容");
                int count = reader.ReadInt32();
                if (count < 0 || count > 1000000) throw new InvalidDataException("词库缓存损坏");
                for (int i = 0; i < count; i++)
                {
                    string en = reader.ReadString();
                    result.Add(new TranslationEntry {
                        English = en, Chinese = reader.ReadString(), Category = reader.ReadString(),
                        Normalized = Normalize(en)
                    });
                }
            }
            return result;
        }
    }

    internal sealed class MatchResult
    {
        public TranslationEntry Entry;
        public int Start;
        public int Length;
    }

    internal sealed class IconMatchResult
    {
        public TranslationEntry Entry;
        public float Score;
        public int IconDistance;
    }

    internal sealed class OverlayLabel
    {
        public RectangleF Bounds;
        public string Text;
        public bool Wrap;
    }

    internal sealed class OcrPanelInfo
    {
        public readonly List<OcrLine> Lines = new List<OcrLine>();
        public string Text = "";
        public bool IsQuest;
        public bool IsSkillDetail;
        public bool IsCharacterStats;
        public bool IsEquipmentDetail;
        public string TaskId = "";
        public string SkillId = "";
    }

    internal sealed class SkillPanelCandidate
    {
        public List<OcrLine> Lines;
        public string Text;
        public MatchResult Match;
        public int Score;
    }

    internal sealed class PanelCropCandidate
    {
        public Rectangle Bounds;
        public int Score;
        public string Kind;
    }

    internal static class ChatRegionSettings
    {
        private const int Scale = 10000;

        public static void Save(Rectangle region, Rectangle gameBounds)
        {
            if (region.Width < 40 || region.Height < 20 || gameBounds.Width < 1 || gameBounds.Height < 1) return;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
            {
                key.SetValue("ChatX", region.X); key.SetValue("ChatY", region.Y);
                key.SetValue("ChatW", region.Width); key.SetValue("ChatH", region.Height);
                key.SetValue("ChatRelX", (region.X - gameBounds.X) * Scale / gameBounds.Width);
                key.SetValue("ChatRelY", (region.Y - gameBounds.Y) * Scale / gameBounds.Height);
                key.SetValue("ChatRelW", region.Width * Scale / gameBounds.Width);
                key.SetValue("ChatRelH", region.Height * Scale / gameBounds.Height);
                key.SetValue("ChatGameX", gameBounds.X); key.SetValue("ChatGameY", gameBounds.Y);
                key.SetValue("ChatGameW", gameBounds.Width); key.SetValue("ChatGameH", gameBounds.Height);
            }
        }

        public static Rectangle LoadAbsolute()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
                {
                    if (key == null) return Rectangle.Empty;
                    return new Rectangle(Convert.ToInt32(key.GetValue("ChatX", 0)),
                        Convert.ToInt32(key.GetValue("ChatY", 0)),
                        Convert.ToInt32(key.GetValue("ChatW", 0)),
                        Convert.ToInt32(key.GetValue("ChatH", 0)));
                }
            }
            catch { return Rectangle.Empty; }
        }

        public static bool HasSelection()
        {
            Rectangle value = LoadAbsolute();
            return value.Width >= 80 && value.Height >= 30;
        }

        public static Rectangle ResolveForGame(Rectangle gameBounds)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
                {
                    if (key != null)
                    {
                        int relX = Convert.ToInt32(key.GetValue("ChatRelX", -1));
                        int relY = Convert.ToInt32(key.GetValue("ChatRelY", -1));
                        int relW = Convert.ToInt32(key.GetValue("ChatRelW", -1));
                        int relH = Convert.ToInt32(key.GetValue("ChatRelH", -1));
                        if (relX >= 0 && relY >= 0 && relW >= 100 && relH >= 100 &&
                            relX + relW <= Scale && relY + relH <= Scale)
                        {
                            Rectangle relative = new Rectangle(gameBounds.X + relX * gameBounds.Width / Scale,
                                gameBounds.Y + relY * gameBounds.Height / Scale,
                                Math.Max(40, relW * gameBounds.Width / Scale),
                                Math.Max(20, relH * gameBounds.Height / Scale));
                            return Rectangle.Intersect(relative, gameBounds);
                        }
                    }
                }
            }
            catch { }

            Rectangle absolute = LoadAbsolute();
            if (absolute.Width >= 80 && absolute.Height >= 30)
            {
                Rectangle overlap = Rectangle.Intersect(absolute, gameBounds);
                long sourceArea = (long)absolute.Width * absolute.Height;
                long overlapArea = (long)overlap.Width * overlap.Height;
                if (sourceArea > 0 && overlapArea * 10 >= sourceArea * 7) return overlap;
            }
            return DefaultForGame(gameBounds);
        }

        public static Rectangle DefaultForGame(Rectangle gameBounds)
        {
            return new Rectangle(gameBounds.Left + gameBounds.Width * 10 / 100,
                gameBounds.Top + gameBounds.Height * 76 / 100,
                gameBounds.Width * 55 / 100, gameBounds.Height * 21 / 100);
        }
    }

    internal sealed class OverlayForm : Form
    {
        private const int HOTKEY_SHOW = 1001;
        private const int HOTKEY_HIDE = 1002;
        private const int WM_HOTKEY = 0x0312;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly TranslationStore translations;
        private readonly List<OverlayLabel> labels = new List<OverlayLabel>();
        private readonly NotifyIcon tray = new NotifyIcon();
        private bool processing;
        private bool visibleTranslation;
        private OcrEngine ocr;
        private Keys showKey = Keys.F8;
        private Keys hideKey = Keys.F9;
        private uint showModifiers;
        private uint hideModifiers;
        private Rectangle captureBounds;
        private Rectangle gameBounds;
        private DictionaryOnlyForm dictionaryEditor;
        private HotkeyForm hotkeyEditor;
        private OfflineChatForm chatTranslator;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        public OverlayForm()
        {
            translations = new TranslationStore(Path.Combine(baseDir, "枫语幕词库.tsv"));
            translations.Load();
            // The international client and the dictionary use English source text.
            // Pin OCR to English first so a Chinese Windows profile does not misclassify Latin glyphs.
            ocr = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if (ocr == null) ocr = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocr == null) throw new InvalidOperationException("Windows OCR 不可用，请在系统语言设置中安装英语 OCR。 ");

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            DoubleBuffered = true;

            LoadSettings();

            BuildTray();
            Shown += async delegate {
                SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) |
                    WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                bool h1 = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                bool h2 = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                tray.ShowBalloonTip(2500, "枫语幕已启动",
                    "常规/任务共 " + translations.Count + " 条，任务 " + translations.TaskCount + " 个/文本 " +
                    translations.TaskTextCount + " 条，图标指纹 " + translations.IconCount + " 条，已载入内存。" +
                    HotkeyText(showKey, showModifiers) + " 呼出，" +
                    HotkeyText(hideKey, hideModifiers) + " 缩回后台。双击托盘图标打开AI实时聊天翻译。" +
                    ((!h1 || !h2) ? "（有快捷键注册失败）" : ""), ToolTipIcon.Info);
                SyncAiKnowledge(false);
                if (Program.Benchmark)
                {
                    await Task.Delay(350);
                    await ShowTranslationAsync();
                    Close();
                }
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private void LoadSettings()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
            {
                Keys parsed;
                if (key == null) return;
                string sk = Convert.ToString(key.GetValue("ShowKey", "F8"));
                string hk = Convert.ToString(key.GetValue("HideKey", "F9"));
                if (Enum.TryParse<Keys>(sk, true, out parsed)) showKey = parsed;
                if (Enum.TryParse<Keys>(hk, true, out parsed)) hideKey = parsed;
                showModifiers = ParseModifiers(Convert.ToString(key.GetValue("ShowModifiers", "")));
                hideModifiers = ParseModifiers(Convert.ToString(key.GetValue("HideModifiers", "")));
            }
        }

        internal static uint ParseModifiers(string value)
        {
            uint result = 0;
            foreach (string part in value.Split('+'))
            {
                string m = part.Trim().ToUpperInvariant();
                if (m == "ALT") result |= 0x0001;
                else if (m == "CTRL" || m == "CONTROL") result |= 0x0002;
                else if (m == "SHIFT") result |= 0x0004;
                else if (m == "WIN" || m == "WINDOWS") result |= 0x0008;
            }
            return result;
        }

        internal static string ModifiersText(uint modifiers)
        {
            List<string> items = new List<string>();
            if ((modifiers & 0x0002) != 0) items.Add("CTRL");
            if ((modifiers & 0x0001) != 0) items.Add("ALT");
            if ((modifiers & 0x0004) != 0) items.Add("SHIFT");
            if ((modifiers & 0x0008) != 0) items.Add("WIN");
            return String.Join("+", items.ToArray());
        }

        private string HotkeyText(Keys key, uint modifiers)
        {
            string p = ModifiersText(modifiers);
            if (p.Length > 0) p += "+";
            return p + key;
        }

        internal Keys ShowKey { get { return showKey; } }
        internal Keys HideKey { get { return hideKey; } }
        internal uint ShowModifiers { get { return showModifiers; } }
        internal uint HideModifiers { get { return hideModifiers; } }

        internal void ApplyHotkeys(Keys newShowKey, uint newShowModifiers, Keys newHideKey, uint newHideModifiers)
        {
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            showKey = newShowKey; showModifiers = newShowModifiers;
            hideKey = newHideKey; hideModifiers = newHideModifiers;
            bool ok1 = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
            bool ok2 = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
            if (!ok1 || !ok2) MessageBox.Show("快捷键被其他程序占用，请换一个组合。", "快捷键设置");
        }

        internal void ReloadDictionary()
        {
            translations.Load();
            SyncAiKnowledge(false);
            tray.ShowBalloonTip(1200, "词库与AI知识已同步", translations.Count + " 条", ToolTipIcon.Info);
        }

        internal void SyncAiKnowledge(bool showNotice)
        {
            try
            {
                string aiRoot = Path.Combine(baseDir, "模型");
                bool hasModel = Directory.Exists(aiRoot) &&
                    Directory.GetFiles(aiRoot, "Qwen3-*.gguf", SearchOption.AllDirectories).Length > 0;
                if (!hasModel)
                {
                    if (showNotice) tray.ShowBalloonTip(1800, "AI词库同步", "尚未安装AI模型；正式词库已经载入。", ToolTipIcon.Info);
                    return;
                }
                KnowledgeInitializationResult result = MapleKnowledgeInitializer.Initialize(aiRoot,
                    Path.Combine(baseDir, "枫语幕词库.tsv"));
                if (chatTranslator != null && !chatTranslator.IsDisposed) chatTranslator.SyncKnowledge();
                if (showNotice) tray.ShowBalloonTip(2200, "AI词库同步完成",
                    result.Entries + " 条、" + result.Categories + " 类" +
                    (result.Changed ? "，已切换到新词库。" : "，当前已经是最新版。"), ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                if (showNotice) MessageBox.Show("AI词库同步失败：\n\n" + ex.Message, "枫语幕");
            }
        }

        private void ShowDictionaryEditor()
        {
            HideTranslation();
            if (dictionaryEditor == null || dictionaryEditor.IsDisposed) dictionaryEditor = new DictionaryOnlyForm(this, baseDir);
            dictionaryEditor.Show();
            dictionaryEditor.Activate();
        }

        private void ShowHotkeyEditor()
        {
            HideTranslation();
            if (hotkeyEditor == null || hotkeyEditor.IsDisposed) hotkeyEditor = new HotkeyForm(this);
            hotkeyEditor.Show();
            hotkeyEditor.Activate();
        }

        private void ShowChatTranslator()
        {
            HideTranslation();
            if (chatTranslator == null || chatTranslator.IsDisposed)
                chatTranslator = new OfflineChatForm(this, baseDir);
            chatTranslator.Show();
            chatTranslator.Activate();
        }

        private void BuildTray()
        {
            tray.Icon = SystemIcons.Information;
            tray.Text = "枫语幕 v2.1.1";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem dictionary = new ToolStripMenuItem("打开并更改词库");
            dictionary.Click += delegate { ShowDictionaryEditor(); };
            ToolStripMenuItem hotkeys = new ToolStripMenuItem("更改快捷键");
            hotkeys.Click += delegate { ShowHotkeyEditor(); };
            ToolStripMenuItem chat = new ToolStripMenuItem("AI实时聊天翻译");
            chat.Font = new Font(chat.Font, FontStyle.Bold);
            chat.Click += delegate { ShowChatTranslator(); };
            ToolStripMenuItem syncAi = new ToolStripMenuItem("同步AI词库");
            syncAi.Click += delegate { SyncAiKnowledge(true); };
            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Close(); };
            menu.Items.Add(dictionary);
            menu.Items.Add(hotkeys);
            menu.Items.Add(chat);
            menu.Items.Add(syncAi);
            menu.Items.Add(exit);
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowChatTranslator(); };
        }

        private async Task ToggleAsync()
        {
            if (visibleTranslation)
            {
                visibleTranslation = false;
                labels.Clear();
                Invalidate();
                tray.Text = "枫语幕 v2.1.1（内存待机）";
            }
            else await ShowTranslationAsync();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_SHOW) { Task ignored = ShowTranslationAsync(); }
                else if (id == HOTKEY_HIDE) HideTranslation();
            }
            base.WndProc(ref m);
        }

        private void HideTranslation()
        {
            visibleTranslation = false;
            labels.Clear();
            Invalidate();
            tray.Text = "枫语幕 v2.1.1（低配置优化）";
        }

        private async Task ShowTranslationAsync()
        {
            if (processing) return;
            processing = true;
            Stopwatch stopwatch = Stopwatch.StartNew();
            // Screen results are never reused. Every F8 starts from an empty overlay and a fresh capture.
            visibleTranslation = false;
            labels.Clear();
            Invalidate();
            // Invalidate only queues a repaint. Force that repaint through the desktop compositor
            // before CopyFromScreen so a second F8 can never OCR its own previous black labels.
            Update();
            if (!Program.Benchmark)
            {
                try { DwmFlush(); }
                catch { }
            }
            string benchmarkOcrText = "";
            try
            {
                Rectangle screen;
                if (Program.Benchmark && !String.IsNullOrEmpty(Program.BenchmarkImagePath) && File.Exists(Program.BenchmarkImagePath))
                {
                    using (Image source = Image.FromFile(Program.BenchmarkImagePath))
                        screen = new Rectangle(0, 0, source.Width, source.Height);
                }
                else screen = Program.Benchmark ? new Rectangle(0, 0, 1280, 720) : GetForegroundCaptureBounds();
                captureBounds = screen;
                gameBounds = screen;
                using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        if (Program.Benchmark)
                        {
                            if (!String.IsNullOrEmpty(Program.BenchmarkImagePath) && File.Exists(Program.BenchmarkImagePath))
                            {
                                using (Image source = Image.FromFile(Program.BenchmarkImagePath))
                                    g.DrawImageUnscaled(source, 0, 0);
                            }
                            else if (!String.IsNullOrEmpty(Program.BenchmarkText))
                            {
                                g.Clear(Color.FromArgb(28, 28, 32));
                                using (Font titleFont = new Font("Arial", 15, FontStyle.Bold))
                                using (Font textFont = new Font("Arial", 14, FontStyle.Regular))
                                {
                                    if (!Program.BenchmarkUi)
                                        g.DrawString("Quest   Accept", titleFont, Brushes.White, 40, 35);
                                    g.DrawString(Program.BenchmarkUi ? Program.BenchmarkText.Replace("|", Environment.NewLine) : Program.BenchmarkText,
                                        textFont, Brushes.White,
                                        Program.BenchmarkUi ? new RectangleF(40, 78, 700, 600) : new RectangleF(40, 78, 430, 220));
                                }
                            }
                            else if (!String.IsNullOrEmpty(Program.BenchmarkIconPath) && File.Exists(Program.BenchmarkIconPath))
                            {
                                g.Clear(Color.FromArgb(18, 18, 20));
                                using (Image icon = Image.FromFile(Program.BenchmarkIconPath))
                                    g.DrawImage(icon, new Rectangle(40, 40, 32, 32));
                                using (Font f = new Font("Arial", 14, FontStyle.Regular))
                                    g.DrawString("Blue Potlon", f, Brushes.White, 82, 47);
                            }
                            else
                            {
                                g.Clear(Color.White);
                                using (Font f = new Font("Arial", 14, FontStyle.Regular))
                                    g.DrawString("MapleStory Classic World   Henesys   Orange Mushroom   Quest Complete", f, Brushes.Black, 40, 40);
                            }
                        }
                        else g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
                    }
                    float ocrScale;
                    // A 2200px first pass keeps classic-client tooltip and quest text legible.
                    // The indexed matchers below are now cheap enough that this remains well inside
                    // the two-second budget without falling back to repeated full-screen OCR passes.
                    using (Bitmap prepared = PrepareForOcr(bitmap, out ocrScale, false, screen.Width >= 1900 ? 2200.0f : 2000.0f))
                    {
                        OcrResult result = await RecognizeAsync(prepared);
                        List<OverlayLabel> next = BuildLabels(result, ocrScale, prepared);
                        benchmarkOcrText = result.Text;
                        if (Program.Benchmark)
                        {
                            StringBuilder benchmarkLines = new StringBuilder();
                            foreach (OcrLine benchmarkLine in result.Lines)
                            {
                                if (benchmarkLines.Length > 0) benchmarkLines.Append(" <> ");
                                benchmarkLines.Append(benchmarkLine.Text);
                            }
                            benchmarkOcrText += " || OCR行=" + benchmarkLines;
                        }
                        bool hasImageCapture = !String.IsNullOrEmpty(Program.BenchmarkImagePath) && File.Exists(Program.BenchmarkImagePath);
                        bool useHoverPass = !Program.Benchmark ||
                            (hasImageCapture && Program.BenchmarkCursor != System.Drawing.Point.Empty);
                        Rectangle hoverCapture = Rectangle.Empty;
                        if (useHoverPass && stopwatch.ElapsedMilliseconds < 780)
                        {
                            System.Drawing.Point pointer = Program.Benchmark && Program.BenchmarkCursor != System.Drawing.Point.Empty ? Program.BenchmarkCursor : Cursor.Position;
                            Rectangle chat = GetChatExclusionBounds();
                            Rectangle tooltipLocal = FindClassicTooltipCrop(bitmap, pointer, screen);
                            Rectangle hover = tooltipLocal.IsEmpty
                                ? Rectangle.Intersect(screen,
                                    new Rectangle(pointer.X - 500, pointer.Y - 360, 1000, 720))
                                : new Rectangle(screen.Left + tooltipLocal.Left,
                                    screen.Top + tooltipLocal.Top, tooltipLocal.Width, tooltipLocal.Height);
                            if (!chat.Contains(pointer) && hover.Width >= 300 && hover.Height >= 220 &&
                                hover.Width * hover.Height < screen.Width * screen.Height * 0.90)
                            {
                                hoverCapture = hover;
                                Rectangle local = new Rectangle(hover.Left - screen.Left, hover.Top - screen.Top, hover.Width, hover.Height);
                                using (Bitmap hoverBitmap = bitmap.Clone(local, PixelFormat.Format32bppArgb))
                                {
                                    float hoverScale;
                                    using (Bitmap hoverPrepared = tooltipLocal.IsEmpty
                                        ? PrepareForOcr(hoverBitmap, out hoverScale, false, 1750.0f)
                                        : PrepareTooltipForOcr(hoverBitmap, out hoverScale,
                                            Math.Min(1900.0f, Math.Max(1450.0f, hover.Width * 3.0f))))
                                    {
                                        captureBounds = hover;
                                        OcrResult hoverResult = await RecognizeAsync(hoverPrepared);
                                        MergeLabels(next, BuildLabels(hoverResult, hoverScale, hoverPrepared));
                                        if (Program.Benchmark) benchmarkOcrText += " || 光标详情=" + hoverResult.Text;
                                    }
                                }
                                captureBounds = screen;
                            }
                        }
                        // Small classic UI labels (NAME/STR/DEX, quest objectives, etc.) are
                        // often only 9-12 pixels high. Once the first pass locates a panel header,
                        // re-read that panel alone at higher effective resolution instead of
                        // slowing every full-screen capture or guessing individual words.
                        if (stopwatch.ElapsedMilliseconds < 1120)
                        {
                            List<Rectangle> panelCrops = FindPanelCrops(result, ocrScale, screen);
                            foreach (Rectangle panelCrop in panelCrops)
                            {
                                if (stopwatch.ElapsedMilliseconds >= 1680) break;
                                if (IsCharacterPanelAlreadyCovered(panelCrop, next)) continue;
                                if (!hoverCapture.IsEmpty)
                                {
                                    Rectangle overlap = Rectangle.Intersect(hoverCapture, panelCrop);
                                    long smaller = Math.Min((long)hoverCapture.Width * hoverCapture.Height,
                                        (long)panelCrop.Width * panelCrop.Height);
                                    if (smaller > 0 && (long)overlap.Width * overlap.Height * 10 >= smaller * 6)
                                        continue;
                                }
                                Rectangle local = new Rectangle(panelCrop.Left - screen.Left,
                                    panelCrop.Top - screen.Top, panelCrop.Width, panelCrop.Height);
                                using (Bitmap panelBitmap = bitmap.Clone(local, PixelFormat.Format32bppArgb))
                                {
                                    float panelScale;
                                    using (Bitmap panelPrepared = PrepareForOcr(panelBitmap, out panelScale,
                                        false, Math.Min(2200.0f, Math.Max(1600.0f, panelCrop.Width * 2.8f))))
                                    {
                                        captureBounds = panelCrop;
                                        OcrResult panelResult = await RecognizeAsync(panelPrepared);
                                        MergeLabels(next, BuildLabels(panelResult, panelScale, panelPrepared));
                                        if (Program.Benchmark) benchmarkOcrText += " || 面板复核=" + panelResult.Text;

                                        // Classic equipment tooltips use red requirements and white
                                        // attributes on a dark blue background. A normal luminance
                                        // conversion makes the red glyphs almost as dark as the panel.
                                        // Re-read only an item/equipment crop with a red-aware channel;
                                        // this keeps the ordinary full-screen path fast while recovering
                                        // REQ STR/DEX, item type and the complete hover-detail block.
                                        if (LooksLikeItemPanelText(panelResult.Text) &&
                                            stopwatch.ElapsedMilliseconds < 1320)
                                        {
                                            Rectangle tooltipLocal = FindEquipmentTooltipCrop(panelResult,
                                                panelScale, panelBitmap.Size);
                                            using (Bitmap tooltipBitmap = panelBitmap.Clone(tooltipLocal,
                                                PixelFormat.Format32bppArgb))
                                            {
                                                float tooltipScale;
                                                using (Bitmap tooltipPrepared = PrepareTooltipForOcr(
                                                    tooltipBitmap, out tooltipScale,
                                                    Math.Min(2150.0f, Math.Max(1650.0f,
                                                        tooltipLocal.Width * 3.25f))))
                                                {
                                                    captureBounds = new Rectangle(panelCrop.Left + tooltipLocal.Left,
                                                        panelCrop.Top + tooltipLocal.Top,
                                                        tooltipLocal.Width, tooltipLocal.Height);
                                                    OcrResult tooltipResult = await RecognizeAsync(tooltipPrepared);
                                                    MergeLabels(next, BuildLabels(tooltipResult, tooltipScale,
                                                        tooltipPrepared));
                                                    if (Program.Benchmark)
                                                        benchmarkOcrText += " || 装备红字复核=" + tooltipResult.Text;
                                                    captureBounds = panelCrop;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            captureBounds = screen;
                        }
                        bool needsMoreOcr = !useHoverPass && (next.Count < 3 || result.Lines.Count > next.Count + 1);

                        // Use the spare time budget for a second, grayscale/high-contrast OCR pass.
                        // Exact dictionary hits unique to either pass are merged; overlapping results
                        // keep the colour pass. Slow machines skip the second pass before one second.
                        if (needsMoreOcr && stopwatch.ElapsedMilliseconds < 450)
                        {
                            float secondScale;
                            using (Bitmap contrastPrepared = PrepareForOcr(bitmap, out secondScale, true))
                            {
                                OcrResult secondResult = await RecognizeAsync(contrastPrepared);
                                List<OverlayLabel> second = BuildLabels(secondResult, secondScale, prepared);
                                MergeLabels(next, second);
                                needsMoreOcr = next.Count < 3 || secondResult.Lines.Count > next.Count + 1;
                                if (Program.Benchmark)
                                    benchmarkOcrText += " || 二次=" + secondResult.Text;
                            }
                        }
                        // A higher-resolution colour pass improves very small item and quest text.
                        if (needsMoreOcr && stopwatch.ElapsedMilliseconds < 650)
                        {
                            float thirdScale;
                            using (Bitmap largePrepared = PrepareForOcr(bitmap, out thirdScale, false, 3000.0f))
                            {
                                OcrResult thirdResult = await RecognizeAsync(largePrepared);
                                MergeLabels(next, BuildLabels(thirdResult, thirdScale, largePrepared));
                                if (Program.Benchmark)
                                    benchmarkOcrText += " || 三次=" + thirdResult.Text;
                            }
                        }
                        labels.Clear(); labels.AddRange(next);
                    }
                }
                visibleTranslation = true;
                Invalidate();
                stopwatch.Stop();
                tray.Text = "枫语幕 v2.1.1（已显示，" + stopwatch.ElapsedMilliseconds + "ms）";
                if (Program.Benchmark)
                {
                    StringBuilder benchmarkLabels = new StringBuilder();
                    foreach (OverlayLabel label in labels)
                    {
                        if (benchmarkLabels.Length > 0) benchmarkLabels.Append(" | ");
                        benchmarkLabels.Append(label.Text).Append('@').Append(label.Bounds.ToString());
                    }
                    File.WriteAllText(Path.Combine(baseDir, "last_run.txt"),
                        "耗时毫秒=" + stopwatch.ElapsedMilliseconds + Environment.NewLine +
                        "命中数量=" + labels.Count + Environment.NewLine +
                        "图标指纹数量=" + translations.IconCount + Environment.NewLine +
                        "任务数量=" + translations.TaskCount + Environment.NewLine +
                        "任务文本数量=" + translations.TaskTextCount + Environment.NewLine +
                        "技能说明数量=" + translations.SkillTextCount + Environment.NewLine +
                        "装备物品说明数量=" + translations.ItemTextCount + Environment.NewLine +
                        "覆盖标签=" + benchmarkLabels + Environment.NewLine +
                        "OCR文本=" + benchmarkOcrText.Replace("\r", " ").Replace("\n", " | ") + Environment.NewLine +
                        "最佳图标距离=" + Program.BenchmarkBestIconDistance + Environment.NewLine +
                        "最佳图标候选=" + Program.BenchmarkBestIcon + Environment.NewLine +
                        "时间=" + DateTime.Now.ToString("s") + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                visibleTranslation = false;
                labels.Clear();
                Invalidate();
                if (Program.Benchmark)
                    File.WriteAllText(Path.Combine(baseDir, "last_run.txt"),
                        "错误=" + ex + Environment.NewLine +
                        "时间=" + DateTime.Now.ToString("s") + Environment.NewLine, Encoding.UTF8);
                tray.ShowBalloonTip(3000, "识别失败", ex.Message, ToolTipIcon.Error);
            }
            finally { processing = false; }
        }

        internal static Rectangle GetForegroundCaptureBounds()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && !IsIconic(foreground))
            {
                RECT client;
                POINT origin = new POINT();
                if (GetClientRect(foreground, out client) && ClientToScreen(foreground, ref origin))
                {
                    Rectangle window = new Rectangle(origin.X, origin.Y,
                        client.Right - client.Left, client.Bottom - client.Top);
                    Rectangle clipped = Rectangle.Intersect(window, SystemInformation.VirtualScreen);
                    if (clipped.Width >= 160 && clipped.Height >= 120) return clipped;
                }
            }

            IntPtr monitor = MonitorFromWindow(foreground, 2); // nearest monitor
            MONITORINFO info = new MONITORINFO();
            info.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
            return Screen.PrimaryScreen.Bounds;
        }

        internal async Task<string> CaptureTextAsync(Rectangle screen)
        {
            if (screen.Width < 80 || screen.Height < 40) return "";
            using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
                float scale;
                using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false, 1800.0f))
                {
                    OcrResult result = await RecognizeAsync(prepared);
                    return result.Text ?? "";
                }
            }
        }

        private static Bitmap PrepareForOcr(Bitmap source, out float scale, bool grayscale,
            float targetLongEdge = 2400.0f)
        {
            int longEdge = Math.Max(source.Width, source.Height);
            // 1440p/4K full-screen captures must be allowed to shrink. Keeping them at native
            // size made Windows OCR spend over two seconds even before a hover-detail pass.
            float minimumScale = longEdge >= 1900 ? 0.58f : 1.0f;
            scale = Math.Min(2.5f, Math.Max(minimumScale, targetLongEdge / Math.Max(1, longEdge)));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            result.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Slight contrast boost helps outlined game text while preserving coloured item names.
                float contrast = grayscale ? 1.38f : 1.18f;
                float offset = (1.0f - contrast) / 2.0f;
                float red = grayscale ? 0.299f * contrast : contrast;
                float green = grayscale ? 0.587f * contrast : contrast;
                float blue = grayscale ? 0.114f * contrast : contrast;
                ColorMatrix matrix = new ColorMatrix(new float[][] {
                    new float[] { red, grayscale ? red : 0, grayscale ? red : 0, 0, 0 },
                    new float[] { grayscale ? green : 0, green, grayscale ? green : 0, 0, 0 },
                    new float[] { grayscale ? blue : 0, grayscale ? blue : 0, blue, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { offset, offset, offset, 0, 1 }
                });
                attributes.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, width, height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private static Bitmap PrepareTooltipForOcr(Bitmap source, out float scale,
            float targetLongEdge)
        {
            int longEdge = Math.Max(source.Width, source.Height);
            float minimumScale = longEdge >= 1900 ? 0.58f : 1.0f;
            scale = Math.Min(2.6f, Math.Max(minimumScale,
                targetLongEdge / Math.Max(1, longEdge)));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            result.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Output brightness = 82% red + 18% green. White and red text
                // stay bright, while the blue/purple tooltip background becomes dark.
                const float contrast = 1.48f;
                float offset = (1.0f - contrast) / 2.0f;
                float red = 0.82f * contrast, green = 0.18f * contrast;
                ColorMatrix matrix = new ColorMatrix(new float[][] {
                    new float[] { red, red, red, 0, 0 },
                    new float[] { green, green, green, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { offset, offset, offset, 0, 1 }
                });
                attributes.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, width, height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private static bool IsClassicTooltipPixel(Color color)
        {
            // Classic tooltips are a translucent navy/purple rectangle. Detecting the
            // surface itself is much more reliable than assuming a fixed tooltip size or
            // position around the cursor, and excludes bright sky/snow backgrounds.
            return color.A > 180 && color.R >= 18 && color.R <= 122 &&
                color.G >= 18 && color.G <= 138 && color.B >= 64 && color.B <= 184 &&
                color.B >= color.R + 18 && color.B >= color.G + 8;
        }

        private static Rectangle FindClassicTooltipCrop(Bitmap bitmap,
            System.Drawing.Point screenPointer, Rectangle screen)
        {
            if (bitmap == null || bitmap.Width < 160 || bitmap.Height < 120)
                return Rectangle.Empty;
            System.Drawing.Point pointer = new System.Drawing.Point(
                screenPointer.X - screen.Left, screenPointer.Y - screen.Top);
            const int step = 4;
            int columns = (bitmap.Width + step - 1) / step;
            int rows = (bitmap.Height + step - 1) / step;
            bool[] mask = new bool[columns * rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int sampleX = Math.Min(bitmap.Width - 1, x * step + step / 2);
                    int sampleY = Math.Min(bitmap.Height - 1, y * step + step / 2);
                    mask[y * columns + x] = IsClassicTooltipPixel(bitmap.GetPixel(sampleX, sampleY));
                }

            bool[] visited = new bool[mask.Length];
            int[] queue = new int[mask.Length];
            Rectangle best = Rectangle.Empty;
            double bestScore = 0;
            int[] dx = new int[] { -1, 1, 0, 0 };
            int[] dy = new int[] { 0, 0, -1, 1 };
            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || visited[start]) continue;
                int head = 0, tail = 0, count = 0;
                int minX = columns, minY = rows, maxX = 0, maxY = 0;
                visited[start] = true; queue[tail++] = start;
                while (head < tail)
                {
                    int index = queue[head++]; count++;
                    int x = index % columns, y = index / columns;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int nextX = x + dx[direction], nextY = y + dy[direction];
                        if (nextX < 0 || nextY < 0 || nextX >= columns || nextY >= rows) continue;
                        int next = nextY * columns + nextX;
                        if (!mask[next] || visited[next]) continue;
                        visited[next] = true; queue[tail++] = next;
                    }
                }
                int width = (maxX - minX + 1) * step;
                int height = (maxY - minY + 1) * step;
                if (width < 190 || height < 90 || count < 800) continue;
                double density = (double)count / Math.Max(1, (maxX - minX + 1) * (maxY - minY + 1));
                if (density < 0.20) continue;
                Rectangle candidate = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    Rectangle.FromLTRB(minX * step - 14, minY * step - 14,
                        Math.Min(bitmap.Width, (maxX + 1) * step + 14),
                        Math.Min(bitmap.Height, (maxY + 1) * step + 14)));
                int nearestX = Math.Max(candidate.Left, Math.Min(pointer.X, candidate.Right));
                int nearestY = Math.Max(candidate.Top, Math.Min(pointer.Y, candidate.Bottom));
                double distance = Math.Sqrt((pointer.X - nearestX) * (pointer.X - nearestX) +
                    (pointer.Y - nearestY) * (pointer.Y - nearestY));
                if (distance > Math.Max(520, bitmap.Width * 0.28)) continue;
                double score = candidate.Width * candidate.Height * density / (1.0 + distance / 180.0);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }

        private static void MergeLabels(List<OverlayLabel> primary, List<OverlayLabel> secondary)
        {
            foreach (OverlayLabel candidate in secondary)
            {
                bool duplicate = false;
                for (int i = 0; i < primary.Count; i++)
                {
                    OverlayLabel existing = primary[i];
                    RectangleF intersection = RectangleF.Intersect(existing.Bounds, candidate.Bounds);
                    float smallerArea = Math.Min(existing.Bounds.Width * existing.Bounds.Height,
                        candidate.Bounds.Width * candidate.Bounds.Height);
                    if ((smallerArea > 0 && intersection.Width * intersection.Height / smallerArea >= 0.45f) ||
                        (SameOverlayText(existing.Text, candidate.Text) &&
                         Math.Abs(existing.Bounds.Y - candidate.Bounds.Y) < 20))
                    {
                        // A focused panel pass usually turns a one-word fallback into a complete
                        // structured row. Prefer that richer result instead of letting the noisy
                        // full-screen pass permanently mask it.
                        if (!String.Equals(existing.Text, candidate.Text, StringComparison.Ordinal) &&
                            candidate.Text.Length >= existing.Text.Length + 4)
                            primary[i] = candidate;
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) primary.Add(candidate);
            }
        }

        private static bool SameOverlayText(string left, string right)
        {
            if (String.Equals(left, right, StringComparison.Ordinal)) return true;
            if (String.IsNullOrEmpty(left) || String.IsNullOrEmpty(right)) return false;
            return String.Equals(Regex.Replace(left, @"\s+", ""),
                Regex.Replace(right, @"\s+", ""), StringComparison.Ordinal);
        }

        private async Task<OcrResult> RecognizeAsync(Bitmap bitmap)
        {
            using (MemoryStream png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png);
                byte[] bytes = png.ToArray();
                using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
                {
                    using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await ToTask<uint>((IAsyncOperation<uint>)writer.StoreAsync());
                        await ToTask<bool>(writer.FlushAsync());
                    }
                    BitmapDecoder decoder = await ToTask<BitmapDecoder>(BitmapDecoder.CreateAsync(stream));
                    SoftwareBitmap software = await ToTask<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied));
                    using (software) { return await ToTask<OcrResult>(ocr.RecognizeAsync(software)); }
                }
            }
        }

        private static Task<T> ToTask<T>(IAsyncOperation<T> operation)
        {
            TaskCompletionSource<T> source = new TaskCompletionSource<T>();
            operation.Completed = delegate(IAsyncOperation<T> info, AsyncStatus status)
            {
                try
                {
                    if (status == AsyncStatus.Completed) source.TrySetResult(info.GetResults());
                    else if (status == AsyncStatus.Canceled) source.TrySetCanceled();
                    else source.TrySetException(info.ErrorCode ?? new InvalidOperationException("Windows OCR 异步操作失败"));
                }
                catch (Exception ex) { source.TrySetException(ex); }
            };
            return source.Task;
        }

        private List<Rectangle> FindPanelCrops(OcrResult result, float ocrScale, Rectangle screen)
        {
            List<PanelCropCandidate> candidates = new List<PanelCropCandidate>();
            foreach (OcrLine line in result.Lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                bool character = normalized.Contains("character stat") || normalized.Contains("character info");
                bool skillHeader = normalized.Contains("skill inventory") || normalized.EndsWith(" skills", StringComparison.Ordinal) ||
                    normalized.EndsWith(" techniques", StringComparison.Ordinal);
                bool skill = skillHeader || translations.LooksLikeSkillTextStart(line.Text) ||
                    normalized.Contains("master level") || normalized.Contains("required skill");
                string taskId = translations.DetectTaskId(line.Text);
                bool questHeader = normalized == "quest" || normalized == "quest log" ||
                    normalized.Contains("quest helper");
                bool quest = questHeader || taskId.Length > 0;
                bool itemHeader = normalized.Contains("item list") || normalized.Contains("item inventory") ||
                    normalized == "item" || normalized == "list";
                bool item = normalized.Contains("item list") || normalized.Contains("item inventory") ||
                    normalized == "item" || normalized == "list" ||
                    translations.LooksLikeItemTextStart(line.Text) || LooksLikeEquipmentStatText(line.Text) ||
                    normalized.Contains("leave store") || normalized.Contains("buy item") ||
                    normalized.Contains("seller info");
                if (!character && !skill && !quest && !item) continue;
                RectangleF source = GetOcrLineBounds(line);
                int sourceLeft = screen.Left + (int)(source.Left / ocrScale);
                int sourceTop = screen.Top + (int)(source.Top / ocrScale);
                int left = sourceLeft - 28;
                int top = sourceTop - 24;
                int width, height;
                if (character)
                {
                    width = Math.Max(620, screen.Width * 29 / 100);
                    height = Math.Max(500, screen.Height * 43 / 100);
                }
                else if (skill)
                {
                    if (!skillHeader) { left = sourceLeft - 210; top = sourceTop - 180; }
                    width = Math.Max(950, screen.Width * 48 / 100);
                    height = Math.Max(700, screen.Height * 72 / 100);
                }
                else if (quest)
                {
                    if (!questHeader) { left = sourceLeft - 120; top = sourceTop - 180; }
                    bool sideHelper = sourceLeft > screen.Left + screen.Width * 70 / 100;
                    width = sideHelper ? Math.Max(430, screen.Width * 23 / 100) : Math.Max(900, screen.Width * 48 / 100);
                    height = sideHelper ? Math.Max(420, screen.Height * 45 / 100) : Math.Max(680, screen.Height * 68 / 100);
                }
                else
                {
                    if (!itemHeader) { left = sourceLeft - 260; top = sourceTop - 320; }
                    width = Math.Max(900, screen.Width * 48 / 100);
                    height = Math.Max(680, screen.Height * 74 / 100);
                }
                Rectangle crop = Rectangle.Intersect(screen, new Rectangle(left, top, width, height));
                if (crop.Width < 300 || crop.Height < 250) continue;
                int score = character ? 130 : (skillHeader ? 120 : (questHeader ? 115 :
                    (itemHeader ? 110 : (taskId.Length > 0 ? 88 :
                    (LooksLikeEquipmentStatText(line.Text) ? 82 : 60)))));
                // A main quest/equipment/skill window is more useful than the small tracker
                // at the right edge. Sort all proposals before de-duplication so OCR line order
                // can never consume the three high-resolution passes on low-value fragments.
                bool atRightEdge = sourceLeft > screen.Left + screen.Width * 72 / 100;
                if (atRightEdge) score -= 40;
                else if (quest && taskId.Length > 0) score += 34;
                candidates.Add(new PanelCropCandidate { Bounds = crop, Score = score,
                    Kind = character ? "character" : (skill ? "skill" : (quest ? "quest" : "item")) });
            }

            candidates.Sort(delegate(PanelCropCandidate left, PanelCropCandidate right) {
                int score = right.Score.CompareTo(left.Score);
                if (score != 0) return score;
                long leftArea = (long)left.Bounds.Width * left.Bounds.Height;
                long rightArea = (long)right.Bounds.Width * right.Bounds.Height;
                return rightArea.CompareTo(leftArea);
            });
            List<PanelCropCandidate> accepted = new List<PanelCropCandidate>();
            HashSet<string> acceptedKinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelCropCandidate candidate in candidates)
            {
                if (acceptedKinds.Contains(candidate.Kind)) continue;
                bool duplicate = false;
                foreach (PanelCropCandidate existing in accepted)
                {
                    Rectangle overlap = Rectangle.Intersect(existing.Bounds, candidate.Bounds);
                    long smaller = Math.Min((long)existing.Bounds.Width * existing.Bounds.Height,
                        (long)candidate.Bounds.Width * candidate.Bounds.Height);
                    long overlapArea = (long)overlap.Width * overlap.Height;
                    bool samePanelFamily = String.Equals(existing.Kind, candidate.Kind,
                        StringComparison.Ordinal);
                    if (smaller > 0 && (overlapArea * 10 >= smaller * 7 ||
                        (samePanelFamily && overlapArea * 10 >= smaller * 3)))
                    { duplicate = true; break; }
                }
                if (!duplicate)
                {
                    accepted.Add(candidate);
                    acceptedKinds.Add(candidate.Kind);
                }
                if (accepted.Count >= 2) break;
            }
            List<Rectangle> crops = new List<Rectangle>();
            foreach (PanelCropCandidate candidate in accepted) crops.Add(candidate.Bounds);
            return crops;
        }

        private bool IsCharacterPanelAlreadyCovered(Rectangle panelCrop,
            List<OverlayLabel> currentLabels)
        {
            string[] markers = new string[] { "名称", "职业", "生命值", "魔法值",
                "能力值点数", "物理防御力", "暴击伤害", "跳跃力" };
            int count = 0;
            foreach (OverlayLabel label in currentLabels)
            {
                bool marker = false;
                foreach (string value in markers)
                    if (String.Equals(value, label.Text, StringComparison.Ordinal))
                    { marker = true; break; }
                if (!marker) continue;
                RectangleF screenBounds = new RectangleF(Bounds.Left + label.Bounds.Left,
                    Bounds.Top + label.Bounds.Top, label.Bounds.Width, label.Bounds.Height);
                if (screenBounds.IntersectsWith(panelCrop)) count++;
            }
            // Once the fixed-layout pass has covered almost the whole stat panel, another
            // high-resolution OCR pass only repeats the same labels and adds latency.
            return count >= 6;
        }

        private Dictionary<OcrLine, OcrPanelInfo> BuildOcrPanels(List<OcrLine> lines)
        {
            Dictionary<OcrLine, OcrPanelInfo> byLine = new Dictionary<OcrLine, OcrPanelInfo>();
            int count = lines.Count;
            int[] parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (BelongToSamePanel(lines[i], lines[j])) UnionPanel(parent, i, j);

            Dictionary<int, OcrPanelInfo> groups = new Dictionary<int, OcrPanelInfo>();
            for (int i = 0; i < count; i++)
            {
                int root = FindPanelRoot(parent, i);
                OcrPanelInfo panel;
                if (!groups.TryGetValue(root, out panel))
                {
                    panel = new OcrPanelInfo(); groups.Add(root, panel);
                }
                panel.Lines.Add(lines[i]);
                byLine[lines[i]] = panel;
            }

            foreach (OcrPanelInfo panel in groups.Values)
            {
                panel.Lines.Sort(delegate(OcrLine left, OcrLine right) {
                    RectangleF a = GetOcrLineBounds(left), b = GetOcrLineBounds(right);
                    int vertical = a.Top.CompareTo(b.Top);
                    return Math.Abs(a.Top - b.Top) <= Math.Max(a.Height, b.Height) * 0.45f
                        ? a.Left.CompareTo(b.Left) : vertical;
                });
                StringBuilder text = new StringBuilder();
                foreach (OcrLine line in panel.Lines)
                {
                    if (text.Length > 0) text.Append(' ');
                    text.Append(line.Text);
                }
                panel.Text = text.ToString();
                string normalized = TranslationStore.Normalize(panel.Text);
                panel.TaskId = translations.DetectTaskId(panel.Text);
                panel.IsQuest = panel.TaskId.Length > 0 ||
                    (normalized.Contains("quest") &&
                     (normalized.Contains("available") || normalized.Contains("in progress") ||
                      normalized.Contains("completed") || normalized.Contains("forfeit") ||
                      normalized.Contains("quest helper") || normalized.Contains("accept") ||
                      normalized.Contains("decline")));
                panel.SkillId = translations.DetectSkillId(panel.Text);
                string skillContentId = translations.DetectSkillContentId(panel.Text);
                if (panel.SkillId.Length == 0) panel.SkillId = skillContentId;
                panel.IsSkillDetail = normalized.Contains("master level") ||
                    (normalized.Contains("current level") && normalized.Contains("next level")) ||
                    normalized.Contains("required skill") ||
                    (skillContentId.Length > 0 && normalized.Length >= 24) ||
                    (panel.SkillId.Length > 0 && normalized.Length >= 35 &&
                     (normalized.Contains("damage") || normalized.Contains("duration") ||
                      normalized.Contains("seconds") || normalized.Contains(" mp ")));
                int statAnchors = 0;
                string[] statWords = new string[] { " name ", " job ", " level ", " hp ", " mp ",
                    " exp ", " fame ", " str ", " dex ", " int ", " luk ", " accuracy ", " evasion " };
                string padded = " " + normalized + " ";
                foreach (string word in statWords) if (padded.Contains(word)) statAnchors++;
                panel.IsCharacterStats = normalized.Contains("character stat") ||
                    normalized.Contains("character info") || statAnchors >= 5;
                panel.IsEquipmentDetail = normalized.Contains("req lev") ||
                    normalized.Contains("required level") || normalized.Contains("remaining enhancements") ||
                    (normalized.Contains("weapon def") && normalized.Contains("magic def"));
            }
            return byLine;
        }

        private static int FindPanelRoot(int[] parent, int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        private static void UnionPanel(int[] parent, int left, int right)
        {
            int a = FindPanelRoot(parent, left), b = FindPanelRoot(parent, right);
            if (a != b) parent[b] = a;
        }

        private static bool BelongToSamePanel(OcrLine first, OcrLine second)
        {
            RectangleF a = GetOcrLineBounds(first), b = GetOcrLineBounds(second);
            if (a.IsEmpty || b.IsEmpty) return false;
            float height = Math.Max(8.0f, Math.Max(a.Height, b.Height));
            float verticalGap = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
            if (verticalGap > Math.Max(34.0f, height * 2.5f)) return false;
            float overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            float narrow = Math.Max(1.0f, Math.Min(a.Width, b.Width));
            if (overlap >= Math.Min(24.0f, narrow * 0.18f)) return true;
            float leftDrift = Math.Abs(a.Left - b.Left);
            return leftDrift <= Math.Max(72.0f, narrow * 0.52f);
        }

        private List<OverlayLabel> BuildLabels(OcrResult result, float ocrScale, Bitmap prepared)
        {
            List<OverlayLabel> output = new List<OverlayLabel>();
            Rectangle chatExclusion = GetChatExclusionBounds();
            List<OcrLine> allLines = new List<OcrLine>();
            StringBuilder visibleText = new StringBuilder();
            foreach (OcrLine candidate in result.Lines)
            {
                if (IsChatLine(candidate, ocrScale, chatExclusion) &&
                    !translations.HasDetailCandidate(candidate.Text) &&
                    !LooksLikeEquipmentStatText(candidate.Text)) continue;
                allLines.Add(candidate);
                if (visibleText.Length > 0) visibleText.AppendLine();
                visibleText.Append(candidate.Text);
            }
            Dictionary<OcrLine, OcrPanelInfo> panelByLine = BuildOcrPanels(allLines);
            AddStablePanelLayouts(output, allLines, panelByLine, ocrScale);
            string globalTaskId = translations.DetectTaskId(visibleText.ToString());
            if (globalTaskId.Length == 0)
            {
                // DetectTaskId intentionally rejects fuzzy matching on one giant page string.
                // A focused quest crop can still contain a slightly damaged title such as
                // "Mars Training"; recover context from individual visual lines.
                foreach (OcrLine candidate in allLines)
                {
                    globalTaskId = translations.DetectTaskId(candidate.Text);
                    if (globalTaskId.Length > 0) break;
                }
            }
            if (globalTaskId.Length > 0)
                AddQuestPanelTextLabels(output, allLines, globalTaskId, ocrScale);
            HashSet<OcrPanelInfo> handledSkillPanels = new HashSet<OcrPanelInfo>();
            HashSet<OcrPanelInfo> handledEquipmentPanels = new HashSet<OcrPanelInfo>();
            for (int lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
            {
                OcrLine line = allLines[lineIndex];
                OcrPanelInfo currentPanel;
                panelByLine.TryGetValue(line, out currentPanel);
                bool questInterface = currentPanel != null && currentPanel.IsQuest;
                string activeTaskId = questInterface && currentPanel.TaskId.Length > 0
                    ? currentPanel.TaskId : globalTaskId;
                if (currentPanel != null && currentPanel.IsSkillDetail &&
                    handledSkillPanels.Add(currentPanel))
                {
                    AddSkillPanelLabels(output, currentPanel, ocrScale);
                }
                if (currentPanel != null && currentPanel.IsSkillDetail) continue;

                bool equipmentPanel = currentPanel != null && currentPanel.IsEquipmentDetail;
                if (equipmentPanel && handledEquipmentPanels.Add(currentPanel))
                    AddEquipmentPanelLabels(output, currentPanel, ocrScale);

                string structuredLine;
                if (!equipmentPanel && TryTranslateStructuredLine(line.Text, false, false, out structuredLine))
                {
                    AddWholeLineLabel(output, line, structuredLine, ocrScale,
                        structuredLine.Length > 24);
                    continue;
                }

                List<MatchResult> characterStatMatches = translations.FindCharacterStatMatches(line.Text);
                bool characterStatLine = currentPanel != null && currentPanel.IsCharacterStats;
                bool earlyEquipmentStats = LooksLikeEquipmentStatText(line.Text) || characterStatLine;
                if (equipmentPanel && LooksLikeEquipmentStatText(line.Text)) continue;
                if (earlyEquipmentStats)
                {
                    List<MatchResult> statMatches = characterStatLine
                        ? characterStatMatches : translations.FindMatches(line.Text);
                    if (statMatches.Count > 0)
                    {
                        AddExactLabels(output, new List<OcrLine> { line }, line.Text, statMatches, ocrScale);
                        continue;
                    }
                }
                // Dialogue, shop and crafting descriptions are often wrapped over several OCR
                // lines without a quest ID. Prefer one high-coverage interface-text match over
                // several unrelated short dictionary hits inside the sentence.
                List<OcrLine> interfaceLines = new List<OcrLine>();
                StringBuilder interfaceText = new StringBuilder();
                List<MatchResult> bestInterfaceMatches = null;
                string bestInterfaceText = ""; int bestInterfaceSpan = 0; int bestInterfaceLength = 0;
                for (int span = 0; span < 8 && lineIndex + span < allLines.Count; span++)
                {
                    OcrLine candidateLine = allLines[lineIndex + span];
                    if (span > 0 && !CanJoinOcrLines(allLines[lineIndex + span - 1], candidateLine)) break;
                    interfaceLines.Add(candidateLine);
                    if (interfaceText.Length > 0) interfaceText.Append(' ');
                    interfaceText.Append(candidateLine.Text);
                    List<MatchResult> interfaceMatches = translations.FindInterfaceTextMatches(interfaceText.ToString());
                    int longest = 0;
                    foreach (MatchResult match in interfaceMatches) longest = Math.Max(longest, match.Entry.Normalized.Length);
                    if (longest > bestInterfaceLength)
                    {
                        bestInterfaceLength = longest; bestInterfaceMatches = interfaceMatches;
                        bestInterfaceText = interfaceText.ToString(); bestInterfaceSpan = span + 1;
                    }
                }
                if (bestInterfaceMatches != null && bestInterfaceMatches.Count > 0)
                {
                    AddExactLabels(output, interfaceLines.GetRange(0, bestInterfaceSpan),
                        bestInterfaceText, bestInterfaceMatches, ocrScale);
                    // Windows OCR can flatten several nearby UI blocks into one very long line.
                    // Keep non-overlapping equipment attributes from the same OCR line instead
                    // of dropping everything after the matched dialogue/crafting sentence.
                    List<MatchResult> secondaryMatches = translations.FindMatches(bestInterfaceText);
                    secondaryMatches.RemoveAll(delegate(MatchResult secondary) {
                        foreach (MatchResult primary in bestInterfaceMatches)
                            if (secondary.Start < primary.Start + primary.Length &&
                                primary.Start < secondary.Start + secondary.Length) return true;
                        return false;
                    });
                    string normalizedSecondaryLine = TranslationStore.Normalize(bestInterfaceText);
                    bool secondaryEquipmentStats = LooksLikeEquipmentStatText(bestInterfaceText);
                    if (normalizedSecondaryLine.Length > 28 && !secondaryEquipmentStats)
                        secondaryMatches.RemoveAll(delegate(MatchResult match) {
                            return match.Length < 12 || match.Length * 3 < normalizedSecondaryLine.Length;
                        });
                    if (secondaryMatches.Count > 0)
                        AddExactLabels(output, interfaceLines.GetRange(0, bestInterfaceSpan),
                            bestInterfaceText, secondaryMatches, ocrScale);
                    lineIndex += bestInterfaceSpan - 1; continue;
                }
                bool skillDetailStart = !questInterface && translations.LooksLikeSkillTextStart(line.Text);
                bool itemDetailStart = !questInterface && !skillDetailStart && translations.LooksLikeItemTextStart(line.Text);
                if (skillDetailStart || itemDetailStart)
                {
                    string detailId = skillDetailStart ? translations.DetectSkillId(line.Text) :
                        translations.DetectItemId(line.Text);
                    List<OcrLine> detailLines = new List<OcrLine>();
                    StringBuilder detailText = new StringBuilder();
                    List<MatchResult> bestDetailMatches = null;
                    string bestDetailText = ""; int bestDetailLineCount = 0; int bestDetailAdvance = 0;
                    int bestDetailLength = 0;
                    int bestDetailCoverage = 0;
                    int skippedDetailNoise = 0;
                    for (int scan = 0; scan < 9 && lineIndex + scan < allLines.Count; scan++)
                    {
                        OcrLine candidateLine = allLines[lineIndex + scan];
                        if (detailLines.Count > 0 && !CanJoinOcrLines(detailLines[detailLines.Count - 1], candidateLine))
                        {
                            // OCR ordering can interleave a small notification or watermark that is
                            // spatially outside the tooltip. Skip at most two such lines, then resume
                            // only if the next line continues the same tooltip block.
                            if (skippedDetailNoise < 2) { skippedDetailNoise++; continue; }
                            break;
                        }
                        detailLines.Add(candidateLine);
                        if (detailText.Length > 0) detailText.Append(' ');
                        detailText.Append(candidateLine.Text);
                        List<MatchResult> detailMatches = skillDetailStart
                            ? translations.FindSkillTextMatches(detailText.ToString(), detailId)
                            : translations.FindItemTextMatches(detailText.ToString(), detailId);
                        detailMatches.RemoveAll(delegate(MatchResult match) { return match.Length < 8; });
                        int longest = 0;
                        // Approximate matches cover the OCR fragment, so ranking by Match.Length
                        // would reward appending unrelated lines. Rank by the dictionary phrase
                        // instead: the first tight fragment wins unless a genuinely fuller entry appears.
                        foreach (MatchResult match in detailMatches) longest = Math.Max(longest, match.Entry.Normalized.Length);
                        int coverage = detailMatches.Count > 0 ? TranslationStore.Normalize(detailText.ToString()).Length : 0;
                        if (longest > bestDetailLength || (longest == bestDetailLength && coverage > bestDetailCoverage))
                        {
                            bestDetailLength = longest; bestDetailMatches = detailMatches;
                            bestDetailText = detailText.ToString(); bestDetailLineCount = detailLines.Count;
                            bestDetailAdvance = scan + 1;
                            bestDetailCoverage = coverage;
                        }
                    }
                    if (bestDetailMatches != null && bestDetailMatches.Count > 0)
                    {
                        List<OcrLine> matchedLines = detailLines.GetRange(0, bestDetailLineCount);
                        AddExactLabels(output, matchedLines, bestDetailText, bestDetailMatches, ocrScale);
                        lineIndex += bestDetailAdvance - 1; continue;
                    }
                }
                string normalizedCurrentLine = TranslationStore.Normalize(line.Text);
                bool equipmentStatLine = LooksLikeEquipmentStatText(line.Text);
                bool taskTextContext = questInterface || (globalTaskId.Length > 0 &&
                    normalizedCurrentLine.Length >= 18 &&
                    (currentPanel == null || (!currentPanel.IsSkillDetail && !currentPanel.IsEquipmentDetail &&
                     !currentPanel.IsCharacterStats)));
                List<MatchResult> matches;
                if (taskTextContext && !equipmentStatLine)
                {
                    // A quest list and the helper can show several different quest titles
                    // around one selected quest body. Never constrain a short title row to
                    // the selected task ID; only prose/dialogue uses that context.
                    List<MatchResult> independentTaskNames =
                        translations.FindTaskMatches(line.Text, null);
                    independentTaskNames.RemoveAll(delegate(MatchResult match) {
                        return !match.Entry.IsTaskName;
                    });
                    matches = independentTaskNames.Count > 0
                        ? independentTaskNames
                        : translations.FindTaskMatches(line.Text, activeTaskId);
                }
                else matches = translations.FindMatches(line.Text);
                if (!questInterface && normalizedCurrentLine.Length > 28 && !equipmentStatLine)
                    matches.RemoveAll(delegate(MatchResult match) {
                        int lineLength = normalizedCurrentLine.Length;
                        return match.Length < 12 || match.Length * 3 < lineLength;
                    });
                if (taskTextContext && matches.Count == 0 && TranslationStore.Normalize(line.Text).Length <= 18)
                    matches = translations.FindMatches(line.Text); // buttons, rewards and short item names only
                if (matches.Count == 0)
                {
                    // Quest dialogue is commonly wrapped into several visual OCR lines.
                    // Combine only nearby consecutive lines and accept long quest-text entries.
                    List<OcrLine> combinedLines = new List<OcrLine>();
                    StringBuilder combinedText = new StringBuilder();
                    List<MatchResult> bestTaskMatches = null;
                    string bestTaskText = ""; int bestTaskSpan = 0; int bestTaskCoverage = 0;
                    for (int span = 0; span < 7 && lineIndex + span < allLines.Count; span++)
                    {
                        OcrLine candidateLine = allLines[lineIndex + span];
                        if (span > 0 && !CanJoinOcrLines(allLines[lineIndex + span - 1], candidateLine)) break;
                        combinedLines.Add(candidateLine);
                        if (combinedText.Length > 0) combinedText.Append(' ');
                        combinedText.Append(candidateLine.Text);
                        if (span == 0) continue;
                        List<MatchResult> combinedMatches = taskTextContext
                            ? translations.FindTaskMatches(combinedText.ToString(), activeTaskId)
                            : new List<MatchResult>();
                        combinedMatches.RemoveAll(delegate(MatchResult match) {
                            return match.Length < 20 || !match.Entry.Category.StartsWith("怀旧服-任务", StringComparison.Ordinal);
                        });
                        int coverage = combinedMatches.Count > 0 ? TranslationStore.Normalize(combinedText.ToString()).Length : 0;
                        if (coverage > bestTaskCoverage)
                        {
                            bestTaskMatches = combinedMatches; bestTaskText = combinedText.ToString();
                            bestTaskSpan = span + 1; bestTaskCoverage = coverage;
                        }
                    }
                    if (bestTaskMatches != null && bestTaskMatches.Count > 0)
                    {
                        AddExactLabels(output, combinedLines.GetRange(0, bestTaskSpan), bestTaskText, bestTaskMatches, ocrScale);
                        lineIndex += bestTaskSpan - 1;
                        continue;
                    }
                    OverlayLabel assisted = BuildIconAssistedLabel(line, ocrScale, prepared);
                    if (assisted != null) output.Add(assisted);
                    continue;
                }
                bool iconBackedName = false;
                foreach (MatchResult match in matches)
                    if (match.Entry.HasIcon) { iconBackedName = true; break; }
                if (iconBackedName && normalizedCurrentLine.Length <= 42)
                {
                    // Text-only OCR commonly confuses visually similar names (for example
                    // Kitty/Nitty). Resolve a short icon-backed row with both signals before
                    // committing the textual match; otherwise a perfect OCR typo can select
                    // the wrong item even though the adjacent icon is unambiguous.
                    OverlayLabel resolved = BuildIconAssistedLabel(line, ocrScale, prepared);
                    if (resolved != null) { output.Add(resolved); continue; }
                }
                AddExactLabels(output, new List<OcrLine> { line }, line.Text, matches, ocrScale);
            }
            MergeAdjacentLongLabels(output);
            MergeOverlappingSameTextLabels(output);
            MergeSameTextLabels(output);
            RemoveContainedLabels(output);
            return output;
        }

        private void AddStablePanelLayouts(List<OverlayLabel> output, List<OcrLine> lines,
            Dictionary<OcrLine, OcrPanelInfo> panelByLine, float ocrScale)
        {
            bool quest = false, characterStat = false, characterInfo = false, playerShop = false;
            bool allowQuestLayout = captureBounds == gameBounds;
            foreach (OcrLine line in lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                OcrPanelInfo panel;
                panelByLine.TryGetValue(line, out panel);
                if (allowQuestLayout && !quest && translations.DetectTaskId(line.Text).Length > 0 &&
                    IsMainQuestWindowTaskLine(line, ocrScale))
                {
                    AddQuestWindowLayoutLabels(output, line, ocrScale); quest = true;
                }
                if (!characterStat && normalized.Contains("character stat"))
                {
                    AddCharacterStatLayoutLabels(output, line, ocrScale); characterStat = true;
                }
                else if (!characterStat && panel != null && panel.IsCharacterStats &&
                    normalized.StartsWith("name", StringComparison.Ordinal))
                {
                    AddCharacterStatLayoutFromName(output, line, ocrScale); characterStat = true;
                }
                if (!characterInfo && normalized.Contains("character info"))
                {
                    AddCharacterInfoLayoutLabels(output, line, ocrScale); characterInfo = true;
                }
                else if (!characterInfo && normalized.Contains("citizenship"))
                {
                    AddCharacterInfoLayoutFromCitizenship(output, line, ocrScale); characterInfo = true;
                }
                if (!playerShop && normalized.Contains("leave store"))
                {
                    AddPlayerShopLayoutLabels(output, line, ocrScale); playerShop = true;
                }
                if (quest && characterStat && characterInfo && playerShop) break;
            }
        }

        private void AddQuestPanelTextLabels(List<OverlayLabel> output,
            List<OcrLine> lines, string taskId, float ocrScale)
        {
            Dictionary<string, SkillPanelCandidate> candidates =
                new Dictionary<string, SkillPanelCandidate>(StringComparer.Ordinal);
            for (int start = 0; start < lines.Count; start++)
            {
                List<OcrLine> window = new List<OcrLine>();
                StringBuilder combined = new StringBuilder();
                for (int span = 0; span < 10 && start + span < lines.Count; span++)
                {
                    OcrLine current = lines[start + span];
                    if (span > 0 && !CanJoinOcrLines(lines[start + span - 1], current)) break;
                    window.Add(current);
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(current.Text);
                    string text = combined.ToString();
                    if (TranslationStore.Normalize(text).Length < 24) continue;
                    foreach (MatchResult match in translations.FindTaskMatches(text, taskId))
                    {
                        if (!match.Entry.IsTaskText || match.Entry.Normalized.Length < 24) continue;
                        int score = match.Entry.Normalized.Length * 1000 -
                            Math.Abs(TranslationStore.Normalize(text).Length - match.Entry.Normalized.Length);
                        string key = match.Entry.Category + "\t" + match.Entry.English;
                        SkillPanelCandidate previous;
                        if (!candidates.TryGetValue(key, out previous) || score > previous.Score)
                            candidates[key] = new SkillPanelCandidate {
                                Lines = new List<OcrLine>(window), Text = text,
                                Match = match, Score = score
                            };
                    }
                }
            }
            List<SkillPanelCandidate> ranked = new List<SkillPanelCandidate>(candidates.Values);
            ranked.Sort(delegate(SkillPanelCandidate left, SkillPanelCandidate right) {
                return right.Score.CompareTo(left.Score);
            });
            List<RectangleF> used = new List<RectangleF>();
            int added = 0;
            foreach (SkillPanelCandidate candidate in ranked)
            {
                List<OcrLine> tight = SelectBestDetailLines(candidate.Lines, candidate.Match.Entry);
                RectangleF raw = GetCombinedLineBounds(tight);
                if (raw.IsEmpty) continue;
                bool overlaps = false;
                foreach (RectangleF previous in used)
                {
                    RectangleF intersection = RectangleF.Intersect(previous, raw);
                    float smaller = Math.Min(previous.Width * previous.Height, raw.Width * raw.Height);
                    if (smaller > 0 && intersection.Width * intersection.Height / smaller > 0.35f)
                    { overlaps = true; break; }
                }
                if (overlaps) continue;
                AddDetailBlockLabel(output, tight, candidate.Match.Entry.Chinese, ocrScale);
                used.Add(raw);
                if (++added >= 3) break;
            }
        }

        private void AddCharacterStatLayoutLabels(List<OverlayLabel> output, OcrLine header, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(header);
            if (raw.IsEmpty) return;
            float left = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float top = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float headerHeight = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, headerHeight / 14.0f));
            AddCharacterStatLayoutLabelsAt(output, left, top, scale);
        }

        private void AddCharacterStatLayoutFromName(List<OverlayLabel> output,
            OcrLine nameLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(nameLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, height / 14.0f));
            float nameLeft = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float nameTop = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            AddCharacterStatLayoutLabelsAt(output, nameLeft + 2.0f * scale,
                nameTop - 44.0f * scale, scale);
        }

        private static void AddCharacterStatLayoutLabelsAt(List<OverlayLabel> output,
            float left, float top, float scale)
        {
            float row = 34.0f * scale, labelHeight = Math.Max(18.0f, 20.0f * scale);
            float firstTop = top + 44.0f * scale;
            string[] leftUpper = new string[] { "名称", "职业", "等级", "生命值", "魔法值", "经验", "人气" };
            for (int i = 0; i < leftUpper.Length; i++)
                AddLayoutLabel(output, leftUpper[i], new RectangleF(left - 2, firstTop + row * i,
                    104.0f * scale, labelHeight));
            float attributesTop = firstTop + row * leftUpper.Length + 27.0f * scale;
            string[] attributes = new string[] { "力量", "敏捷", "智力", "运气" };
            for (int i = 0; i < attributes.Length; i++)
                AddLayoutLabel(output, attributes[i], new RectangleF(left - 2, attributesTop + row * i,
                    104.0f * scale, labelHeight));
            AddLayoutLabel(output, "能力值点数", new RectangleF(left - 2,
                attributesTop + row * attributes.Length + 18.0f * scale, 150.0f * scale, labelHeight));

            float rightLeft = left + 360.0f * scale;
            float rightTop = top + 112.0f * scale;
            string[] right = new string[] { "攻击力", "物理防御力", "魔法攻击力", "魔法防御力", "命中率",
                "回避率", "暴击率", "暴击伤害", "移动速度", "跳跃力" };
            for (int i = 0; i < right.Length; i++)
                AddLayoutLabel(output, right[i], new RectangleF(rightLeft, rightTop + row * i,
                    118.0f * scale, labelHeight));
        }

        private void AddCharacterInfoLayoutLabels(List<OverlayLabel> output, OcrLine header, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(header);
            if (raw.IsEmpty) return;
            float left = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float top = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float headerHeight = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, headerHeight / 14.0f));
            AddCharacterInfoLayoutLabelsAt(output, left, top, scale);
        }

        private void AddCharacterInfoLayoutFromCitizenship(List<OverlayLabel> output,
            OcrLine citizenshipLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(citizenshipLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, height / 14.0f));
            float citizenshipLeft = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float citizenshipTop = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            AddCharacterInfoLayoutLabelsAt(output, citizenshipLeft - 228.0f * scale,
                citizenshipTop - 42.0f * scale, scale);
        }

        private static void AddCharacterInfoLayoutLabelsAt(List<OverlayLabel> output,
            float left, float top, float scale)
        {
            float labelHeight = Math.Max(18.0f, 20.0f * scale);

            AddLayoutLabel(output, "角色信息", new RectangleF(left - 2, top - 2,
                150.0f * scale, labelHeight));
            AddLayoutLabel(output, "公民身份", new RectangleF(left + 228.0f * scale,
                top + 42.0f * scale, 150.0f * scale, labelHeight));
            string[] fields = new string[] { "等级", "职业", "人气", "家族" };
            for (int i = 0; i < fields.Length; i++)
                AddLayoutLabel(output, fields[i], new RectangleF(left + 230.0f * scale,
                    top + (105.0f + i * 35.0f) * scale, 130.0f * scale, labelHeight));
            AddLayoutLabel(output, "邀请组队", new RectangleF(left + 232.0f * scale,
                top + 240.0f * scale, 160.0f * scale, labelHeight));
            AddLayoutLabel(output, "申请交易", new RectangleF(left + 410.0f * scale,
                top + 240.0f * scale, 160.0f * scale, labelHeight));
            AddLayoutLabel(output, "查看宠物信息", new RectangleF(left + 405.0f * scale,
                top + 298.0f * scale, 180.0f * scale, labelHeight));
        }

        private void AddPlayerShopLayoutLabels(List<OverlayLabel> output,
            OcrLine leaveStoreLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(leaveStoreLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.82f, Math.Min(1.55f, height / 16.0f));
            float left = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float top = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float labelHeight = Math.Max(18.0f, 20.0f * scale);
            AddLayoutLabel(output, "卖家信息", new RectangleF(left - 2.0f * scale,
                top - 92.0f * scale, 142.0f * scale, labelHeight));
            AddLayoutLabel(output, "购买道具", new RectangleF(left - 2.0f * scale,
                top - 46.0f * scale, 142.0f * scale, labelHeight));
            AddLayoutLabel(output, "离开商店", new RectangleF(left - 2.0f * scale,
                top - 2.0f * scale, 142.0f * scale, labelHeight));
        }

        private bool IsMainQuestWindowTaskLine(OcrLine line, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty) return false;
            float x = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float y = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            return x < Bounds.Width * 0.68f && y > Bounds.Height * 0.18f;
        }

        private void AddQuestWindowLayoutLabels(List<OverlayLabel> output,
            OcrLine taskLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(taskLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(15.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.82f, Math.Min(1.45f, height / 20.0f));
            float taskLeft = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float taskTop = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float left = taskLeft - 78.0f * scale;
            float top = taskTop - 126.0f * scale;
            float labelHeight = Math.Max(18.0f, 21.0f * scale);

            AddLayoutLabel(output, "任务", new RectangleF(left + 4.0f * scale,
                top + 3.0f * scale, 90.0f * scale, labelHeight));
            AddLayoutLabel(output, "可接取", new RectangleF(left + 18.0f * scale,
                top + 36.0f * scale, 112.0f * scale, labelHeight));
            AddLayoutLabel(output, "进行中", new RectangleF(left + 145.0f * scale,
                top + 36.0f * scale, 124.0f * scale, labelHeight));
            AddLayoutLabel(output, "已完成", new RectangleF(left + 282.0f * scale,
                top + 36.0f * scale, 112.0f * scale, labelHeight));
            AddLayoutLabel(output, "详情", new RectangleF(left + 330.0f * scale,
                top + 620.0f * scale, 112.0f * scale, labelHeight));
            AddLayoutLabel(output, "任务助手", new RectangleF(left + 742.0f * scale,
                top + 620.0f * scale, 148.0f * scale, labelHeight));
            AddLayoutLabel(output, "放弃任务", new RectangleF(left + 900.0f * scale,
                top + 620.0f * scale, 122.0f * scale, labelHeight));
        }

        private static void AddLayoutLabel(List<OverlayLabel> output, string text, RectangleF bounds)
        {
            foreach (OverlayLabel existing in output)
            {
                if (!String.Equals(existing.Text, text, StringComparison.Ordinal)) continue;
                RectangleF overlap = RectangleF.Intersect(existing.Bounds, bounds);
                if (overlap.Width > 0 && overlap.Height > 0) return;
            }
            output.Add(new OverlayLabel { Bounds = bounds, Text = text, Wrap = false });
        }

        private static string ExtractStructuredValue(string normalized, string expression)
        {
            Match match = Regex.Match(normalized, expression, RegexOptions.IgnoreCase);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : "";
        }

        private static string LastNumber(string text)
        {
            MatchCollection values = Regex.Matches(text ?? "", @"[+-]?\d+(?:\.\d+)?%?");
            return values.Count == 0 ? "" : values[values.Count - 1].Value;
        }

        private static string RepairOcrNumber(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return value.Replace('l', '1').Replace('I', '1').Replace('g', '9')
                .Replace('q', '9').Replace('O', '0').Replace('o', '0');
        }

        private static string TranslateEquipmentType(string normalized)
        {
            string[,] values = new string[,] {
                { "two handed blunt weapon", "双手钝器" }, { "one handed blunt weapon", "单手钝器" },
                { "two handed sword", "双手剑" }, { "one handed sword", "单手剑" },
                { "crossbow", "弩" }, { "polearm", "战戟" }, { "knuckle", "指虎" },
                { "earrings", "耳环" }, { "overall", "套服" }, { "shield", "盾牌" },
                { "staff", "长杖" }, { "wand", "短杖" }, { "dagger", "短刀" },
                { "claw", "拳套" }, { "spear", "枪" }, { "shoes", "鞋子" },
                { "gloves", "手套" }, { "bottom", "下装" }, { "cape", "披风" },
                { "bow", "弓" }, { "gun", "枪械" }, { "hat", "帽子" },
                { "top", "上衣" }
            };
            for (int i = 0; i < values.GetLength(0); i++)
                if (normalized.Contains(values[i, 0])) return values[i, 1];
            return "";
        }

        private static int StructuredEditDistance(string left, string right)
        {
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1,
                        previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }

        private static string CanonicalizeStructuredToken(string token)
        {
            if (String.IsNullOrEmpty(token)) return "";
            return token.Replace("0", "o").Replace("1", "i").Replace("4", "a")
                .Replace("+", "in");
        }

        private static bool ContainsApproximateStructuredToken(string normalized,
            string expected, float minimumSimilarity)
        {
            foreach (string raw in normalized.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string token = CanonicalizeStructuredToken(raw);
                if (token == expected) return true;
                if (token.Length < Math.Max(2, expected.Length / 2) ||
                    Math.Abs(token.Length - expected.Length) > Math.Max(3, expected.Length / 2)) continue;
                int distance = StructuredEditDistance(token, expected);
                float similarity = 1.0f - (float)distance / Math.Max(token.Length, expected.Length);
                if (similarity >= minimumSimilarity) return true;
            }
            return false;
        }

        private bool TryTranslateStructuredLine(string text, bool equipmentContext,
            bool skillContext, out string translated)
        {
            translated = "";
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length == 0) return false;
            List<string> fields = new List<string>();
            string value;

            bool requirementLine = equipmentContext || normalized.Contains("req ") ||
                normalized.Contains("required ");
            if (requirementLine)
            {
                value = ExtractStructuredValue(normalized,
                    @"(?:req|required)?\s*(?:lev|level)\s*([+-]?\d+)");
                bool hasLevel = value.Length > 0 || normalized.Contains("req lev") ||
                    normalized.Contains("required level") ||
                    ContainsApproximateStructuredToken(normalized, "lev", 0.66f);
                if (hasLevel) fields.Add("需要等级" + (value.Length > 0 ? "：" + value : ""));
                string[] keys = new string[] { "str", "dex", "int", "luk", "fam|fame" };
                string[] names = new string[] { "力量", "敏捷", "智力", "运气", "人气" };
                for (int i = 0; i < keys.Length; i++)
                {
                    value = ExtractStructuredValue(normalized,
                        @"(?:req|required)?\s*(?:" + keys[i] + @")\s*([+-]?\d+)");
                    string approximateKey = i == 4 ? "fame" : keys[i];
                    bool hasField = value.Length > 0 ||
                        ContainsApproximateStructuredToken(normalized, approximateKey,
                            approximateKey.Length <= 3 ? 0.66f : 0.56f) ||
                        (i == 4 && ContainsApproximateStructuredToken(normalized, "fam", 0.66f));
                    if (hasField) fields.Add("需要" + names[i] +
                        (value.Length > 0 ? "：" + value : ""));
                }
            }

            int jobCount = 0;
            string[] jobs = new string[] { "beginner", "warrior", "magician", "bowman", "thief", "pirate" };
            string[] jobNames = new string[] { "新手", "战士", "魔法师", "弓箭手", "飞侠", "海盗" };
            List<string> allowedJobs = new List<string>();
            for (int i = 0; i < jobs.Length; i++)
                if (normalized.Contains(jobs[i]) ||
                    ContainsApproximateStructuredToken(normalized, jobs[i], 0.52f))
                { jobCount++; allowedJobs.Add(jobNames[i]); }
            if (jobCount >= 2) fields.Add("可用职业：" + String.Join("／", allowedJobs.ToArray()));

            string type = TranslateEquipmentType(normalized);
            if ((equipmentContext || normalized.Contains("type ")) && type.Length > 0)
                fields.Add("类型：" + type);

            if (normalized.Contains("attack speed"))
            {
                string speed = normalized.Contains("fast") ? "快" :
                    (normalized.Contains("slow") ? "慢" : "普通");
                fields.Add("攻击速度：" + speed);
            }
            string[,] stats = new string[,] {
                { "weapon attack", "物理攻击力" }, { "magic attack", "魔法攻击力" },
                { "weapon def", "物理防御力" }, { "magic def", "魔法防御力" },
                { "avoidability", "回避率" }, { "accuracy", "命中率" },
                { "movement speed", "移动速度" }, { "jump", "跳跃力" }
            };
            for (int i = 0; i < stats.GetLength(0); i++)
            {
                if (!normalized.Contains(stats[i, 0])) continue;
                value = LastNumber(normalized.Substring(normalized.IndexOf(stats[i, 0], StringComparison.Ordinal)));
                fields.Add(stats[i, 1] + (value.Length > 0 ? "：" + value : ""));
            }
            if (normalized.Contains("remaining enhancements"))
            {
                value = LastNumber(normalized);
                fields.Add("剩余强化次数" + (value.Length > 0 ? "：" + value : ""));
            }

            if (skillContext || normalized.Contains("master level") ||
                normalized.Contains("current level") || normalized.Contains("next level") ||
                normalized.Contains("required skill"))
            {
                if (normalized.Contains("master level"))
                {
                    value = LastNumber(normalized);
                    fields.Add("最高等级" + (value.Length > 0 ? "：" + value : ""));
                }
                if (normalized.Contains("current level"))
                {
                    value = LastNumber(normalized);
                    fields.Add("当前等级" + (value.Length > 0 ? "：" + value : ""));
                }
                if (normalized.Contains("next level"))
                {
                    value = LastNumber(normalized);
                    fields.Add("下一级" + (value.Length > 0 ? "：" + value : ""));
                }
                if (normalized.Contains("required skill"))
                {
                    string level = ExtractStructuredValue(normalized, @"(?:level)\s*(\d+)");
                    string skillName = "";
                    foreach (MatchResult match in translations.FindMatches(text))
                        if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal))
                        { skillName = match.Entry.Chinese; break; }
                    fields.Add("需要技能：" + (skillName.Length > 0 ? skillName : "前置技能") +
                        (level.Length > 0 ? "至少" + level + "级" : ""));
                }
            }

            Match price = Regex.Match(text ?? "", @"([\dlI]+)\s+(?:for|tor)\s+([\dlIgqOo,]+)(?:\s+mesos?)?",
                RegexOptions.IgnoreCase);
            if (price.Success) fields.Add(RepairOcrNumber(price.Groups[1].Value) + "个，售价" +
                RepairOcrNumber(price.Groups[2].Value) + "金币");

            Match shopPresence = Regex.Match((text ?? "").Trim(),
                @"^([A-Za-z0-9_]{2,24})\s+has\s+(entered|left)\.?$",
                RegexOptions.IgnoreCase);
            if (shopPresence.Success)
                fields.Add(shopPresence.Groups[1].Value +
                    (shopPresence.Groups[2].Value.Equals("entered",
                        StringComparison.OrdinalIgnoreCase) ? " 已进入" : " 已离开"));

            if (fields.Count == 0) return false;
            translated = String.Join("　", fields.ToArray());
            return true;
        }

        private void AddWholeLineLabel(List<OverlayLabel> output, OcrLine line,
            string text, float ocrScale, bool wrap)
        {
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            RectangleF bounds = new RectangleF(raw.Left / ocrScale + captureBounds.Left - Bounds.Left - 3,
                raw.Top / ocrScale + captureBounds.Top - Bounds.Top - 2,
                Math.Max(34, raw.Width / ocrScale + 6), Math.Max(18, raw.Height / ocrScale + 5));
            output.Add(new OverlayLabel { Bounds = bounds, Text = text, Wrap = wrap });
        }

        private static List<List<OcrLine>> BuildVisualRows(List<OcrLine> source)
        {
            List<OcrLine> sorted = new List<OcrLine>(source);
            sorted.Sort(delegate(OcrLine left, OcrLine right) {
                RectangleF a = GetOcrLineBounds(left), b = GetOcrLineBounds(right);
                int top = a.Top.CompareTo(b.Top);
                return Math.Abs(a.Top - b.Top) <= Math.Max(a.Height, b.Height) * 0.55f
                    ? a.Left.CompareTo(b.Left) : top;
            });
            List<List<OcrLine>> rows = new List<List<OcrLine>>();
            foreach (OcrLine line in sorted)
            {
                RectangleF current = GetOcrLineBounds(line);
                List<OcrLine> target = null;
                foreach (List<OcrLine> row in rows)
                {
                    RectangleF bounds = GetCombinedLineBounds(row);
                    float centerDistance = Math.Abs((bounds.Top + bounds.Bottom) / 2.0f -
                        (current.Top + current.Bottom) / 2.0f);
                    if (centerDistance <= Math.Max(bounds.Height, current.Height) * 0.62f)
                    { target = row; break; }
                }
                if (target == null) { target = new List<OcrLine>(); rows.Add(target); }
                target.Add(line);
                target.Sort(delegate(OcrLine left, OcrLine right) {
                    return GetOcrLineBounds(left).Left.CompareTo(GetOcrLineBounds(right).Left);
                });
            }
            return rows;
        }

        private static string CombineVisualRow(List<OcrLine> row)
        {
            StringBuilder text = new StringBuilder();
            foreach (OcrLine line in row)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(line.Text);
            }
            return text.ToString();
        }

        private void AddEquipmentPanelLabels(List<OverlayLabel> output,
            OcrPanelInfo panel, float ocrScale)
        {
            int before = output.Count;
            foreach (List<OcrLine> row in BuildVisualRows(panel.Lines))
            {
                string translated;
                if (!TryTranslateStructuredLine(CombineVisualRow(row), true, false, out translated)) continue;
                AddDetailBlockLabel(output, row, translated, ocrScale);
            }
            string itemId = translations.DetectItemId(panel.Text);
            TranslationEntry overview = translations.GetItemOverview(itemId);
            List<OcrLine> prose = FindBestPanelProseBlock(panel.Lines);
            if (overview != null && prose.Count > 0 && output.Count == before)
                AddDetailBlockLabel(output, prose, overview.Chinese, ocrScale);
        }

        private void AddSkillPanelLabels(List<OverlayLabel> output, OcrPanelInfo panel, float ocrScale)
        {
            if (panel == null || panel.Lines.Count == 0) return;
            string skillId = panel.SkillId;
            if (skillId.Length == 0) skillId = translations.DetectSkillContentId(panel.Text);

            foreach (List<OcrLine> row in BuildVisualRows(panel.Lines))
            {
                string structured;
                if (TryTranslateStructuredLine(CombineVisualRow(row), false, true, out structured))
                    AddDetailBlockLabel(output, row, structured, ocrScale);
            }

            // Names and fixed labels (Master/Current/Next Level) are
            // resolved independently from the long description so one OCR wobble cannot
            // hide the entire tooltip.
            List<MatchResult> fixedMatches = translations.FindMatches(panel.Text);
            fixedMatches.RemoveAll(delegate(MatchResult match) {
                if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal)) return false;
                string key = match.Entry.Normalized;
                return key != "skill point" && key != "skill points" && key != "skill inventory";
            });
            if (fixedMatches.Count > 0)
                AddExactLabels(output, panel.Lines, panel.Text, fixedMatches, ocrScale);

            Dictionary<string, SkillPanelCandidate> candidates =
                new Dictionary<string, SkillPanelCandidate>(StringComparer.Ordinal);
            int lineCount = panel.Lines.Count;
            for (int start = 0; start < lineCount; start++)
            {
                StringBuilder combined = new StringBuilder();
                List<OcrLine> window = new List<OcrLine>();
                RectangleF firstBounds = GetOcrLineBounds(panel.Lines[start]);
                for (int span = 0; span < 16 && start + span < lineCount; span++)
                {
                    OcrLine current = panel.Lines[start + span];
                    RectangleF currentBounds = GetOcrLineBounds(current);
                    if (span > 0 && currentBounds.Top - firstBounds.Top > 620.0f) break;
                    window.Add(current);
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(current.Text);
                    string text = combined.ToString();
                    string normalized = TranslationStore.Normalize(text);
                    if (normalized.Length < 18) continue;
                    List<MatchResult> detailMatches = translations.FindSkillTextMatches(text, skillId);
                    foreach (MatchResult match in detailMatches)
                    {
                        if (!match.Entry.IsSkillText || match.Entry.Normalized.Length < 18) continue;
                        int distance = Math.Abs(normalized.Length - match.Entry.Normalized.Length);
                        int score = match.Entry.Normalized.Length * 1000 - distance;
                        string key = match.Entry.Category + "\t" + match.Entry.English;
                        SkillPanelCandidate previous;
                        if (!candidates.TryGetValue(key, out previous) || score > previous.Score)
                        {
                            candidates[key] = new SkillPanelCandidate {
                                Lines = new List<OcrLine>(window), Text = text,
                                Match = match, Score = score
                            };
                        }
                    }
                }
            }

            List<SkillPanelCandidate> ranked = new List<SkillPanelCandidate>(candidates.Values);
            ranked.Sort(delegate(SkillPanelCandidate left, SkillPanelCandidate right) {
                return right.Score.CompareTo(left.Score);
            });
            List<RectangleF> used = new List<RectangleF>();
            int added = 0;
            foreach (SkillPanelCandidate candidate in ranked)
            {
                List<OcrLine> tight = SelectBestDetailLines(candidate.Lines, candidate.Match.Entry);
                RectangleF raw = GetCombinedLineBounds(tight);
                if (raw.IsEmpty) continue;
                bool overlaps = false;
                foreach (RectangleF previous in used)
                {
                    RectangleF intersection = RectangleF.Intersect(previous, raw);
                    float smaller = Math.Min(previous.Width * previous.Height, raw.Width * raw.Height);
                    if (smaller > 0 && intersection.Width * intersection.Height / smaller > 0.38f)
                    { overlaps = true; break; }
                }
                if (overlaps) continue;
                AddDetailBlockLabel(output, tight, candidate.Match.Entry.Chinese, ocrScale);
                used.Add(raw);
                if (++added >= 3) break; // description + current level + next level
            }
            if (added == 0)
            {
                TranslationEntry overview = translations.GetSkillOverview(skillId);
                List<OcrLine> prose = FindBestPanelProseBlock(panel.Lines);
                if (overview != null && prose.Count > 0)
                    AddDetailBlockLabel(output, prose, overview.Chinese, ocrScale);
            }
        }

        private static List<OcrLine> FindBestPanelProseBlock(List<OcrLine> source)
        {
            List<OcrLine> sorted = new List<OcrLine>(source);
            sorted.Sort(delegate(OcrLine left, OcrLine right) {
                return GetOcrLineBounds(left).Top.CompareTo(GetOcrLineBounds(right).Top);
            });
            List<OcrLine> best = new List<OcrLine>(), current = new List<OcrLine>();
            int bestScore = 0, currentScore = 0;
            foreach (OcrLine line in sorted)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                bool structural = normalized.Contains("master level") ||
                    normalized.Contains("current level") || normalized.Contains("next level") ||
                    normalized.Contains("required skill") || normalized.Contains("skill point") ||
                    normalized.Contains("remaining enhancements") || normalized.Contains("req lev") ||
                    normalized.Contains("beginner warrior") || normalized.Contains("character stat") ||
                    normalized.Contains("character info");
                int words = normalized.Split(new char[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries).Length;
                bool prose = !structural && normalized.Length >= 18 && words >= 4;
                if (prose && (current.Count == 0 || CanJoinOcrLines(current[current.Count - 1], line)))
                {
                    current.Add(line);
                    int digits = 0;
                    foreach (char c in normalized) if (Char.IsDigit(c)) digits++;
                    currentScore += normalized.Length - digits * 3;
                    if (currentScore > bestScore)
                    { bestScore = currentScore; best = new List<OcrLine>(current); }
                }
                else
                {
                    current.Clear(); currentScore = 0;
                    if (prose)
                    {
                        current.Add(line); currentScore = normalized.Length;
                        if (currentScore > bestScore)
                        { bestScore = currentScore; best = new List<OcrLine>(current); }
                    }
                }
            }
            return best;
        }

        private static RectangleF GetCombinedLineBounds(List<OcrLine> lines)
        {
            RectangleF bounds = RectangleF.Empty;
            int count = 0;
            foreach (OcrLine line in lines)
            {
                RectangleF current = GetOcrLineBounds(line);
                if (current.IsEmpty) continue;
                bounds = count++ == 0 ? current : RectangleF.Union(bounds, current);
            }
            return bounds;
        }

        private static List<OcrLine> SelectBestDetailLines(List<OcrLine> source, TranslationEntry entry)
        {
            if (source == null || source.Count <= 1 || entry == null || entry.DetailWords == null)
                return source ?? new List<OcrLine>();
            int bestStart = 0, bestSpan = source.Count, bestScore = Int32.MinValue;
            for (int start = 0; start < source.Count; start++)
            {
                StringBuilder combined = new StringBuilder();
                for (int span = 1; span <= 7 && start + span <= source.Count; span++)
                {
                    if (span > 1 && !CanJoinOcrLines(source[start + span - 2], source[start + span - 1])) break;
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(source[start + span - 1].Text);
                    string normalized = TranslationStore.Normalize(combined.ToString());
                    HashSet<string> words = new HashSet<string>(normalized.Split(
                        new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                    int common = 0;
                    foreach (string word in words) if (entry.DetailWords.Contains(word)) common++;
                    int score = common * 1000 - Math.Abs(words.Count - entry.DetailWords.Count) * 8 - span;
                    if (common >= 3 && score > bestScore)
                    { bestScore = score; bestStart = start; bestSpan = span; }
                }
            }
            return bestScore == Int32.MinValue ? source : source.GetRange(bestStart, bestSpan);
        }

        private void AddDetailBlockLabel(List<OverlayLabel> output, List<OcrLine> lines,
            string text, float ocrScale)
        {
            RectangleF raw = GetCombinedLineBounds(lines);
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            output.Add(new OverlayLabel {
                Bounds = new RectangleF(raw.Left / ocrScale + captureBounds.Left - Bounds.Left - 3,
                    raw.Top / ocrScale + captureBounds.Top - Bounds.Top - 2,
                    Math.Max(100, raw.Width / ocrScale + 6), Math.Max(20, raw.Height / ocrScale + 5)),
                Text = text, Wrap = true
            });
        }

        private static void MergeAdjacentLongLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i].Text.Length < 16) continue;
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!SameOverlayText(labels[j].Text, labels[i].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    float horizontalOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    float verticalGap = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
                    if (horizontalOverlap < Math.Min(a.Width, b.Width) * 0.35f || verticalGap > 12) continue;
                    labels[i].Bounds = RectangleF.Union(a, b);
                    labels[i].Wrap = true;
                    labels.RemoveAt(j);
                }
            }
        }

        private static void MergeSameTextLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i].Text.Length < 16) continue;
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!SameOverlayText(labels[j].Text, labels[i].Text)) continue;
                    labels[i].Bounds = RectangleF.Union(labels[i].Bounds, labels[j].Bounds);
                    labels[i].Wrap = true;
                    labels.RemoveAt(j);
                }
            }
        }

        private static void MergeOverlappingSameTextLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!SameOverlayText(labels[i].Text, labels[j].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    RectangleF overlap = RectangleF.Intersect(a, b);
                    float smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
                    if (smaller <= 0 || overlap.Width * overlap.Height / smaller < 0.35f) continue;
                    labels[i].Bounds = RectangleF.Union(a, b);
                    labels[i].Wrap = labels[i].Wrap || labels[j].Wrap;
                    labels.RemoveAt(j);
                }
            }
        }

        private static void RemoveContainedLabels(List<OverlayLabel> labels)
        {
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < labels.Count; j++)
                {
                    if (i == j || labels[i].Text.Length >= labels[j].Text.Length) continue;
                    string shorter = Regex.Replace(labels[i].Text, @"\s+", "");
                    string longer = Regex.Replace(labels[j].Text, @"\s+", "");
                    if (!longer.Contains(shorter)) continue;
                    RectangleF overlap = RectangleF.Intersect(labels[i].Bounds, labels[j].Bounds);
                    float area = labels[i].Bounds.Width * labels[i].Bounds.Height;
                    if (area <= 0 || overlap.Width * overlap.Height / area < 0.62f) continue;
                    labels.RemoveAt(i); break;
                }
            }
        }

        private Rectangle GetChatExclusionBounds()
        {
            Rectangle resolved = ChatRegionSettings.ResolveForGame(gameBounds);
            // If the game moved or changed resolution, an F8 capture is the reliable moment
            // when its current client bounds are known. Refresh the same shared absolute box
            // so the AI reader and the F8 exclusion continue to point at one region.
            if (ChatRegionSettings.HasSelection() && ChatRegionSettings.LoadAbsolute() != resolved)
            {
                ChatRegionSettings.Save(resolved, gameBounds);
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.UpdateChatRegion(resolved);
            }
            return resolved;
        }

        private bool IsChatLine(OcrLine line, float ocrScale, Rectangle exclusion)
        {
            if (exclusion.Width <= 0 || exclusion.Height <= 0 || line.Words.Count == 0) return false;
            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, (float)word.BoundingRect.X / ocrScale + captureBounds.Left);
                top = Math.Min(top, (float)word.BoundingRect.Y / ocrScale + captureBounds.Top);
                right = Math.Max(right, (float)(word.BoundingRect.X + word.BoundingRect.Width) / ocrScale + captureBounds.Left);
                bottom = Math.Max(bottom, (float)(word.BoundingRect.Y + word.BoundingRect.Height) / ocrScale + captureBounds.Top);
            }
            if (left == Single.MaxValue) return false;
            RectangleF lineBounds = RectangleF.FromLTRB(left, top, right, bottom);
            return IsInsideChatBounds(lineBounds, exclusion);
        }

        private static bool IsInsideChatBounds(RectangleF lineBounds, Rectangle exclusion)
        {
            float centerX = lineBounds.Left + lineBounds.Width / 2.0f;
            float centerY = lineBounds.Top + lineBounds.Height / 2.0f;
            if (centerX >= exclusion.Left && centerX <= exclusion.Right &&
                centerY >= exclusion.Top && centerY <= exclusion.Bottom) return true;
            RectangleF overlap = RectangleF.Intersect(lineBounds, exclusion);
            float area = lineBounds.Width * lineBounds.Height;
            return area > 0 && overlap.Width * overlap.Height / area >= 0.35f;
        }

        private void AddExactLabels(List<OverlayLabel> output, List<OcrLine> lines,
            string combinedText, List<MatchResult> matches, float ocrScale)
        {
            string normalizedLine = TranslationStore.Normalize(combinedText);
            foreach (MatchResult match in matches)
            {
                // OCR exposes word boxes, not character boxes. Approximate the phrase span by character ratio.
                float x0 = Single.MaxValue, y0 = Single.MaxValue, x1 = Single.MinValue, y1 = Single.MinValue;
                int cursor = 0;
                foreach (OcrLine line in lines)
                {
                    foreach (OcrWord word in line.Words)
                    {
                        string nw = TranslationStore.Normalize(word.Text);
                        if (nw.Length == 0) continue;
                        int safeCursor = Math.Min(cursor, normalizedLine.Length);
                        int found = safeCursor < normalizedLine.Length
                            ? normalizedLine.IndexOf(nw, safeCursor, StringComparison.Ordinal) : -1;
                        if (found < 0) found = safeCursor;
                        int end = found + nw.Length;
                        if (end > match.Start && found < match.Start + match.Length)
                        {
                            x0 = Math.Min(x0, (float)word.BoundingRect.X / ocrScale + captureBounds.Left - Bounds.Left);
                            y0 = Math.Min(y0, (float)word.BoundingRect.Y / ocrScale + captureBounds.Top - Bounds.Top);
                            x1 = Math.Max(x1, (float)(word.BoundingRect.X + word.BoundingRect.Width) / ocrScale + captureBounds.Left - Bounds.Left);
                            y1 = Math.Max(y1, (float)(word.BoundingRect.Y + word.BoundingRect.Height) / ocrScale + captureBounds.Top - Bounds.Top);
                        }
                        cursor = Math.Min(normalizedLine.Length, end + 1);
                    }
                }
                if (x0 == Single.MaxValue) continue;
                output.Add(new OverlayLabel {
                    Bounds = new RectangleF(x0 - 3, y0 - 2, Math.Max(28, x1 - x0 + 6), Math.Max(18, y1 - y0 + 4)),
                    Text = match.Entry.Chinese,
                    Wrap = lines.Count > 1 || match.Entry.IsTaskText || match.Entry.IsSkillText ||
                        match.Entry.IsItemText || match.Entry.IsInterfaceText
                });
            }
        }

        private static bool CanJoinOcrLines(OcrLine first, OcrLine second)
        {
            return AreOcrBlocksContinuous(GetOcrLineBounds(first), GetOcrLineBounds(second));
        }

        private static bool LooksLikeEquipmentStatText(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("attack speed") || normalized.Contains("weapon attack") ||
                normalized.Contains("enhancements") || normalized.Contains("required level") ||
                normalized.Contains("req lev") || normalized.Contains("req str") ||
                normalized.Contains("req dex") || normalized.Contains("req int") ||
                normalized.Contains("req luk") || normalized.Contains("req fame") ||
                normalized.Contains("weapon def") || normalized.Contains("magic def") ||
                normalized.Contains("avoidability") || normalized.StartsWith("str ", StringComparison.Ordinal) ||
                normalized.StartsWith("dex ", StringComparison.Ordinal) ||
                normalized.StartsWith("int ", StringComparison.Ordinal) ||
                normalized.StartsWith("luk ", StringComparison.Ordinal) ||
                normalized.StartsWith("type:", StringComparison.Ordinal) ||
                normalized.Contains(" type:");
        }

        private static bool LooksLikeItemPanelText(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("req lv") || normalized.Contains("req lev") ||
                normalized.Contains("required level") || normalized.Contains("remaining enhancements") ||
                normalized.Contains("beginner warrior") || normalized.Contains("weapon def") ||
                normalized.Contains("item list") || normalized.Contains("item inventory");
        }

        private static Rectangle FindEquipmentTooltipCrop(OcrResult result, float ocrScale,
            System.Drawing.Size sourceSize)
        {
            RectangleF anchors = RectangleF.Empty;
            int count = 0;
            foreach (OcrLine line in result.Lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                bool anchor = normalized.Contains("remaining enhancements") ||
                    normalized.Contains("weapon def") || normalized.Contains("magic def") ||
                    normalized.Contains("beginner warrior") || normalized == "shoes" ||
                    normalized == "gloves" || normalized.Contains("attack speed");
                if (!anchor) continue;
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                RectangleF source = new RectangleF(raw.Left / ocrScale, raw.Top / ocrScale,
                    raw.Width / ocrScale, raw.Height / ocrScale);
                anchors = count == 0 ? source : RectangleF.Union(anchors, source);
                count++;
            }
            if (count < 2) return new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
            int left = Math.Max(0, (int)Math.Floor(anchors.Left - 220));
            int top = Math.Max(0, (int)Math.Floor(anchors.Top - 300));
            int right = Math.Min(sourceSize.Width, (int)Math.Ceiling(anchors.Right + 170));
            int bottom = Math.Min(sourceSize.Height, (int)Math.Ceiling(anchors.Bottom + 65));
            Rectangle crop = Rectangle.FromLTRB(left, top, right, bottom);
            return crop.Width >= 280 && crop.Height >= 300
                ? crop : new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
        }

        private static RectangleF GetOcrLineBounds(OcrLine line)
        {
            if (line == null || line.Words.Count == 0) return RectangleF.Empty;
            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, (float)word.BoundingRect.X);
                top = Math.Min(top, (float)word.BoundingRect.Y);
                right = Math.Max(right, (float)(word.BoundingRect.X + word.BoundingRect.Width));
                bottom = Math.Max(bottom, (float)(word.BoundingRect.Y + word.BoundingRect.Height));
            }
            return left == Single.MaxValue ? RectangleF.Empty : RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static bool AreOcrBlocksContinuous(RectangleF first, RectangleF second)
        {
            if (first.IsEmpty || second.IsEmpty) return false;
            float lineHeight = Math.Max(8.0f, Math.Max(first.Height, second.Height));
            if (second.Top < first.Top - lineHeight * 0.4f) return false;
            float verticalGap = second.Top - first.Bottom;
            if (verticalGap > Math.Max(28.0f, lineHeight * 2.2f)) return false;
            float horizontalOverlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
            float leftDrift = Math.Abs(first.Left - second.Left);
            float allowedDrift = Math.Max(90.0f, Math.Max(first.Width, second.Width) * 0.35f);
            return horizontalOverlap > 0 || leftDrift <= allowedDrift;
        }

        private OverlayLabel BuildIconAssistedLabel(OcrLine line, float ocrScale, Bitmap prepared)
        {
            string normalized = TranslationStore.Normalize(line.Text);
            if (normalized.Length < 3 || normalized.Length > 64 || line.Words.Count == 0) return null;
            if (!translations.HasPlausibleIconText(line.Text)) return null;

            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, (float)word.BoundingRect.X);
                top = Math.Min(top, (float)word.BoundingRect.Y);
                right = Math.Max(right, (float)(word.BoundingRect.X + word.BoundingRect.Width));
                bottom = Math.Max(bottom, (float)(word.BoundingRect.Y + word.BoundingRect.Height));
            }
            if (left == Single.MaxValue) return null;

            float baseSize = Math.Max(22.0f * ocrScale, 32.0f * ocrScale);
            float[] sizeFactors = new float[] { 0.78f, 1.0f, 1.25f, 1.48f };
            float[] gaps = new float[] { 2.0f * ocrScale, 10.0f * ocrScale,
                22.0f * ocrScale, 36.0f * ocrScale };
            // Tooltips often place a 32px icon above the text baseline; skill lists centre it.
            float[] verticalFactors = new float[] { -0.85f, -0.45f, -0.10f, 0.25f };
            IconMatchResult best = null;

            foreach (float sizeFactor in sizeFactors)
            {
                float size = baseSize * sizeFactor;
                foreach (float gap in gaps)
                {
                    foreach (float verticalFactor in verticalFactors)
                    {
                        RectangleF iconArea = new RectangleF(left - gap - size,
                            (top + bottom - size) / 2.0f + verticalFactor * size, size, size);
                        ulong hash;
                        if (!TryComputeDHash(prepared, iconArea, out hash)) continue;
                        if (Program.Benchmark) Program.BenchmarkCurrentArea = iconArea.ToString();
                        IconMatchResult candidate = translations.FindIconAssistedMatch(hash, line.Text);
                        if (candidate != null && (best == null || candidate.Score > best.Score)) best = candidate;
                    }
                }
            }
            if (best == null) return null;

            return new OverlayLabel {
                Bounds = new RectangleF(left / ocrScale + captureBounds.Left - Bounds.Left - 3,
                    top / ocrScale + captureBounds.Top - Bounds.Top - 2,
                    Math.Max(28, (right - left) / ocrScale + 6),
                    Math.Max(18, (bottom - top) / ocrScale + 4)),
                Text = best.Entry.Chinese
            };
        }

        private static bool TryComputeDHash(Bitmap image, RectangleF area, out ulong hash)
        {
            hash = 0;
            if (area.Width < 8 || area.Height < 8 || area.Left < 0 || area.Top < 0 ||
                area.Right >= image.Width || area.Bottom >= image.Height) return false;
            int[,] luminance = new int[8, 9];
            using (Bitmap reduced = new Bitmap(9, 8, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(reduced))
            {
                graphics.Clear(Color.FromArgb(18, 18, 20));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, 9, 8), area.X, area.Y,
                    area.Width, area.Height, GraphicsUnit.Pixel);
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 9; x++)
                    {
                        Color color = reduced.GetPixel(x, y);
                        luminance[y, x] = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    }
                }
            }
            int bit = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++, bit++)
                    if (luminance[y, x] > luminance[y, x + 1]) hash |= 1UL << bit;
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush background = new SolidBrush(Color.FromArgb(238, 18, 18, 20)))
            using (Pen border = new Pen(Color.FromArgb(220, 255, 190, 45), 1.0f))
            using (SolidBrush text = new SolidBrush(Color.White))
            using (Font font = new Font("Microsoft YaHei UI", 12.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                foreach (OverlayLabel label in labels)
                {
                    RectangleF r = label.Bounds;
                    SizeF size;
                    if (label.Wrap)
                    {
                        r.Width = Math.Max(90, r.Width);
                        size = e.Graphics.MeasureString(label.Text, font,
                            new SizeF(Math.Max(20, r.Width - 8), 1000), StringFormat.GenericTypographic);
                        r.Height = Math.Max(r.Height, size.Height + 7);
                    }
                    else
                    {
                        size = e.Graphics.MeasureString(label.Text, font);
                        r.Width = Math.Max(r.Width, size.Width + 8);
                        r.Height = Math.Max(r.Height, size.Height + 5);
                    }
                    using (GraphicsPath path = RoundedRect(r, 4.0f))
                    {
                        e.Graphics.FillPath(background, path);
                        e.Graphics.DrawPath(border, path);
                    }
                    if (label.Wrap)
                        e.Graphics.DrawString(label.Text, font, text,
                            new RectangleF(r.X + 4, r.Y + 3, r.Width - 8, r.Height - 5), StringFormat.GenericTypographic);
                    else
                        e.Graphics.DrawString(label.Text, font, text, r.X + 4, r.Y + (r.Height - size.Height) / 2 - 1);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            tray.Visible = false;
            tray.Dispose();
            if (chatTranslator != null && !chatTranslator.IsDisposed) chatTranslator.StopService();
            base.OnFormClosed(e);
        }
    }
}
