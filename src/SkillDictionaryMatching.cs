using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed partial class TranslationStore
    {
        private readonly List<TranslationEntry> skillNamesForMatching = new List<TranslationEntry>();
        private void RefreshSkillNameIndex(List<TranslationEntry> entries)
        {
            skillNamesForMatching.Clear();
            foreach (TranslationEntry entry in entries)
                if (entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal)) skillNamesForMatching.Add(entry);
        }
        public List<MatchResult> FindSkillNameMatches(string text, string skillId = null)
        {
            List<MatchResult> none = new List<MatchResult>();
            string normalized = Normalize(text);
            string title = Regex.Replace(normalized, @"\s+(?:(?:lv|lvl|level)\s*)?\d{1,3}\s*$", "").Trim();
            if (title.Length < 4 || title.Length > 55) return none;
            List<TranslationEntry> best = new List<TranslationEntry>(); int distance = Int32.MaxValue, second = Int32.MaxValue;
            foreach (TranslationEntry entry in skillNamesForMatching)
            {
                if (!String.IsNullOrEmpty(skillId) && CategoryId(entry.Category) != skillId) continue;
                int allowed = title.Length >= 12 ? 2 : (title.Length >= 6 ? 1 : 0);
                int current = EditDistance(title, entry.Normalized, allowed);
                if (current > allowed) continue;
                if (current < distance) { second = distance; distance = current; best.Clear(); best.Add(entry); }
                else if (current == distance) best.Add(entry);
                else second = Math.Min(second, current);
            }
            if (best.Count == 0 || (distance > 0 && second <= distance + 1)) return none;
            string chinese = best[0].Chinese;
            foreach (TranslationEntry entry in best)
                if (!String.Equals(chinese, entry.Chinese, StringComparison.Ordinal)) return none;
            TranslationEntry winner = best[0];
            // An OCR spelling repair still uses the observed span for geometry, never the
            // longer canonical title. Scope/class identity is resolved separately and uniquely.
            return new List<MatchResult> { new MatchResult { Entry = new TranslationEntry {
                English = text, Chinese = chinese, Normalized = title, Category = winner.Category,
                TaskId = "", DetailWords = winner.DetailWords }, Start = 0, Length = title.Length } };
        }
        private string DetectUniqueSkillNameId(string text)
        {
            string normalized = Normalize(text); HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in skillNamesForMatching)
            {
                int position = normalized.IndexOf(entry.Normalized, StringComparison.Ordinal);
                if (position >= 0 && IsBoundary(normalized, position - 1) && IsBoundary(normalized, position + entry.Normalized.Length))
                    ids.Add(CategoryId(entry.Category));
            }
            if (ids.Count == 1) foreach (string id in ids) return id;
            if (ids.Count > 1) return "";
            List<MatchResult> matches = FindSkillNameMatches(text);
            if (matches.Count == 0) return "";
            string chinese = matches[0].Entry.Chinese;
            foreach (TranslationEntry entry in skillNamesForMatching)
                if (entry.Chinese == chinese) ids.Add(CategoryId(entry.Category));
            if (ids.Count == 1) foreach (string id in ids) return id;
            return "";
        }
    }
}
