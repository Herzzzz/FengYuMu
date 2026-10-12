using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // Canonical-name categories. Long prose (quest body, dialogue, objectives, chat) is deliberately
    // NOT a terminology source: locking whole sentences would freeze the very context we translate.
    internal enum TerminologyCategory
    {
        Unknown = 0,
        Map = 1,
        Job = 2,
        Equipment = 3,
        Item = 4,
        Monster = 5,
        Skill = 6,
        Npc = 7,
        QuestTitle = 8
    }

    internal sealed class TerminologyEntry
    {
        public string English = "";
        public string Chinese = "";
        public TerminologyCategory Category = TerminologyCategory.Unknown;
    }

    // One protected span: a locked canonical term or a value that must survive translation
    // byte for byte (counters, levels, percentages, standalone numbers).
    internal sealed class TerminologyPlaceholder
    {
        public string Token = "";
        public string Value = "";
        public string Source = "";
        public bool IsNumber;
    }

    internal sealed class ProtectedSegment
    {
        public string OriginalText = "";
        public string ProtectedText = "";
        public string TermHint = "";
        public bool HasTerminology;
        public readonly List<TerminologyPlaceholder> Placeholders =
            new List<TerminologyPlaceholder>();
    }

    internal sealed class RestoreResult
    {
        public bool Success;
        public string Text = "";
        public string Reason = "";
    }

    // Canonical terminology table. Only entries from the allowed categories become canonical names;
    // an English form that maps to more than one Chinese is ambiguous and is never locked.
    internal sealed class TerminologyTable
    {
        private readonly Dictionary<string, string> canonical =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // Permanent ambiguity: once a normalized English form has been seen with two different
        // Chinese names it is never added back, no matter how many more duplicates follow.
        private readonly HashSet<string> ambiguousKeys =
            new HashSet<string>(StringComparer.Ordinal);

        internal int Count { get { return canonical.Count; } }

        internal static bool IsCanonicalCategory(TerminologyCategory category)
        {
            return category == TerminologyCategory.Map || category == TerminologyCategory.Job ||
                category == TerminologyCategory.Equipment || category == TerminologyCategory.Item ||
                category == TerminologyCategory.Monster || category == TerminologyCategory.Skill ||
                category == TerminologyCategory.Npc || category == TerminologyCategory.QuestTitle;
        }

        internal static TerminologyTable Build(IEnumerable<TerminologyEntry> entries,
            List<string> ambiguous)
        {
            TerminologyTable table = new TerminologyTable();
            if (entries == null) return table;
            foreach (TerminologyEntry entry in entries)
            {
                if (entry == null || !IsCanonicalCategory(entry.Category)) continue;
                string normalized = TranslationTerminology.NormalizePhrase(entry.English);
                if (normalized.Length == 0) continue;
                string chinese = (entry.Chinese ?? "").Trim();
                if (chinese.Length == 0) continue;
                if (table.ambiguousKeys.Contains(normalized)) continue;
                string existing;
                if (table.canonical.TryGetValue(normalized, out existing))
                {
                    // Same normalized English, different Chinese: ambiguous, never locked again.
                    if (!String.Equals(existing, chinese, StringComparison.Ordinal))
                    {
                        if (ambiguous != null) ambiguous.Add(entry.English + " => " + existing +
                            " / " + chinese);
                        table.canonical.Remove(normalized);
                        table.ambiguousKeys.Add(normalized);
                    }
                    continue;
                }
                table.canonical[normalized] = chinese;
            }
            return table;
        }

        // Returns null when the phrase is absent or was dropped as ambiguous.
        internal string Lookup(string normalizedPhrase)
        {
            string chinese;
            return canonical.TryGetValue(normalizedPhrase ?? "", out chinese) ? chinese : null;
        }
    }

    // Pure terminology protection/restore. The sentence is never split: canonical terms and values
    // are replaced by non-colliding placeholders, the whole protected sentence goes to the
    // translator, and the restore is verified before any text is handed back.
    internal static class TranslationTerminology
    {
        // Private-use-area markers. Game and OCR text never produce them, and TryProtect refuses to
        // run when the source already contains one, so a placeholder can never collide with normal
        // text.
        internal const char OpenMarker = '\uE000';
        internal const char CloseMarker = '\uE001';
        internal const int MaximumTermWords = 5;

        private sealed class Token
        {
            public int Start;
            public int Length;
            public string Word = "";
        }

        private static readonly Regex ValuePattern = new Regex(
            @"(?<!\w)(?:\d+\s*/\s*\d+|\d+(?:\.\d+)?\s*%|[Ll]evel\s*\d+\s*\+?|\d+)(?!\w)",
            RegexOptions.CultureInvariant);

        // Lower-case, collapse separators, keep apostrophes inside names (Biggs's). Matching is done
        // on whole words, which is what stops "he" from hitting "helper".
        internal static string NormalizePhrase(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            StringBuilder builder = new StringBuilder(value.Length);
            bool space = false;
            foreach (char raw in value.Trim().ToLowerInvariant())
            {
                char c = (raw == '\u2019' || raw == '\u2018') ? '\'' : raw;
                if (Char.IsLetterOrDigit(c) || c == '\'')
                {
                    builder.Append(c);
                    space = false;
                }
                else if (!space && builder.Length > 0)
                {
                    builder.Append(' ');
                    space = true;
                }
            }
            return builder.ToString().Trim();
        }

        private static List<Token> Tokenize(string text)
        {
            List<Token> tokens = new List<Token>();
            int index = 0;
            while (index < text.Length)
            {
                if (!Char.IsLetterOrDigit(text[index])) { index++; continue; }
                int start = index;
                while (index < text.Length &&
                    (Char.IsLetterOrDigit(text[index]) || text[index] == '\'')) index++;
                string word = text.Substring(start, index - start);
                string normalized = NormalizePhrase(word);
                if (normalized.Length > 0)
                {
                    tokens.Add(new Token {
                        Start = start, Length = index - start, Word = normalized });
                }
            }
            return tokens;
        }

        // Protect canonical terms and values. `reason` explains a safe refusal (marker collision);
        // an ordinary sentence without any term simply comes back unchanged with HasTerminology
        // false, so callers can skip the terminology path entirely.
        internal static bool TryProtect(string text, TerminologyTable table,
            out ProtectedSegment segment, out string reason)
        {
            reason = "";
            segment = new ProtectedSegment();
            if (text == null) { reason = "空文本"; return false; }
            segment.OriginalText = text;
            segment.ProtectedText = text;
            if (text.Length == 0) return true;
            if (text.IndexOf(OpenMarker) >= 0 || text.IndexOf(CloseMarker) >= 0)
            {
                reason = "原文已含占位符标记，拒绝保护以免冲突";
                return false;
            }

            List<Token> tokens = Tokenize(text);
            bool[] consumed = new bool[tokens.Count];
            List<TerminologyPlaceholder> termSpans = new List<TerminologyPlaceholder>();
            List<int[]> termRanges = new List<int[]>();
            if (table != null && table.Count > 0)
            {
                for (int start = 0; start < tokens.Count; start++)
                {
                    if (consumed[start]) continue;
                    int bestEnd = -1;
                    string bestChinese = null;
                    for (int end = start; end < tokens.Count &&
                        end - start < MaximumTermWords; end++)
                    {
                        if (consumed[end]) break;
                        StringBuilder phrase = new StringBuilder();
                        for (int i = start; i <= end; i++)
                        {
                            if (phrase.Length > 0) phrase.Append(' ');
                            phrase.Append(tokens[i].Word);
                        }
                        string chinese = table.Lookup(phrase.ToString());
                        // Longest match wins: keep scanning and let a longer phrase override.
                        if (!String.IsNullOrEmpty(chinese)) { bestEnd = end; bestChinese = chinese; }
                    }
                    if (bestEnd < 0) continue;
                    int spanStart = tokens[start].Start;
                    int spanEnd = tokens[bestEnd].Start + tokens[bestEnd].Length;
                    for (int i = start; i <= bestEnd; i++) consumed[i] = true;
                    termSpans.Add(new TerminologyPlaceholder {
                        Source = text.Substring(spanStart, spanEnd - spanStart),
                        Value = bestChinese
                    });
                    termRanges.Add(new int[] { spanStart, spanEnd });
                }
            }

            // Values first, then terms, so the merge can sort everything by position.
            List<int[]> valueRanges = new List<int[]>();
            List<TerminologyPlaceholder> valueSpans = new List<TerminologyPlaceholder>();
            foreach (Match match in ValuePattern.Matches(text))
            {
                bool overlaps = false;
                foreach (int[] range in termRanges)
                {
                    if (match.Index < range[1] && range[0] < match.Index + match.Length)
                    { overlaps = true; break; }
                }
                if (overlaps) continue;
                bool nested = false;
                foreach (int[] range in valueRanges)
                {
                    if (match.Index < range[1] && range[0] < match.Index + match.Length)
                    { nested = true; break; }
                }
                if (nested) continue;
                valueRanges.Add(new int[] { match.Index, match.Index + match.Length });
                valueSpans.Add(new TerminologyPlaceholder {
                    Source = match.Value,
                    Value = match.Value,
                    IsNumber = true
                });
            }

            List<TerminologyPlaceholder> all = new List<TerminologyPlaceholder>();
            List<int[]> allRanges = new List<int[]>();
            for (int i = 0; i < termSpans.Count; i++)
            { all.Add(termSpans[i]); allRanges.Add(termRanges[i]); }
            for (int i = 0; i < valueSpans.Count; i++)
            { all.Add(valueSpans[i]); allRanges.Add(valueRanges[i]); }
            // Stable order by position so the protected text and the map line up.
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    if (allRanges[j][0] < allRanges[i][0])
                    {
                        TerminologyPlaceholder span = all[i]; all[i] = all[j]; all[j] = span;
                        int[] range = allRanges[i]; allRanges[i] = allRanges[j]; allRanges[j] = range;
                    }
                }
            }

            if (all.Count == 0)
            {
                // Nothing to protect: an ordinary sentence is not force-gamified.
                segment.HasTerminology = false;
                segment.ProtectedText = text;
                return true;
            }

            StringBuilder protectedText = new StringBuilder();
            StringBuilder hint = new StringBuilder();
            int cursor = 0;
            for (int i = 0; i < all.Count; i++)
            {
                TerminologyPlaceholder span = all[i];
                int start = allRanges[i][0];
                int end = allRanges[i][1];
                span.Token = OpenMarker + (span.IsNumber ? "N" : "T") + i + CloseMarker;
                span.Source = text.Substring(start, end - start);
                protectedText.Append(text, cursor, start - cursor);
                protectedText.Append(span.Token);
                cursor = end;
                segment.Placeholders.Add(span);
                if (!span.IsNumber)
                {
                    if (hint.Length > 0) hint.Append("; ");
                    hint.Append(span.Source).Append('=').Append(span.Value);
                    segment.HasTerminology = true;
                }
            }
            protectedText.Append(text, cursor, text.Length - cursor);
            segment.ProtectedText = protectedText.ToString();
            segment.TermHint = hint.ToString();
            return true;
        }

        // Verify and restore. Every placeholder must appear exactly once; a missing, duplicated or
        // tampered placeholder fails safely and no partially restored text is returned.
        internal static RestoreResult Restore(string translated, ProtectedSegment segment)
        {
            RestoreResult result = new RestoreResult();
            if (segment == null) { result.Reason = "缺少保护结果"; return result; }
            if (String.IsNullOrEmpty(translated)) { result.Reason = "空译文"; return result; }
            if (segment.Placeholders.Count == 0)
            {
                if (translated.IndexOf(OpenMarker) >= 0 || translated.IndexOf(CloseMarker) >= 0)
                { result.Reason = "出现未知占位符"; return result; }
                result.Success = true;
                result.Text = translated;
                return result;
            }
            string text = translated;
            foreach (TerminologyPlaceholder span in segment.Placeholders)
            {
                int first = text.IndexOf(span.Token, StringComparison.Ordinal);
                if (first < 0) { result.Reason = "占位符缺失：" + span.Source; return result; }
                if (text.IndexOf(span.Token, first + span.Token.Length,
                    StringComparison.Ordinal) >= 0)
                { result.Reason = "占位符重复：" + span.Source; return result; }
                text = text.Substring(0, first) + span.Value +
                    text.Substring(first + span.Token.Length);
            }
            if (text.IndexOf(OpenMarker) >= 0 || text.IndexOf(CloseMarker) >= 0)
            { result.Reason = "存在未还原或被篡改的占位符"; return result; }
            result.Success = true;
            result.Text = text;
            return result;
        }
    }
}
