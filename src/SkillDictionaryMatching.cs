using System;
using System.Collections.Generic;
using System.Text;
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
            // A visibly truncated name is not a title. Never let an ellipsis turn
            // into an arbitrary short-name match or a different skill.
            if (title.IndexOf("...", StringComparison.Ordinal) >= 0 ||
                title.IndexOf('\u2026') >= 0) return none;
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
            if (best.Count == 0)
            {
                // A resolved weapon-qualified name ("Arrow Bomb: Bow") may be shown
                // by the game with only its stable prefix ("Arrow Bomb"). This is
                // accepted only with a resolved identity and an explicit colon
                // qualifier; a bare prefix of a longer unrelated name never matches.
                if (!String.IsNullOrEmpty(skillId))
                {
                    foreach (TranslationEntry entry in skillNamesForMatching)
                    {
                        if (CategoryId(entry.Category) != skillId) continue;
                        string english = Regex.Replace((entry.English ?? "").Trim().ToLowerInvariant(),
                            @"\s+", " ");
                        if (!english.StartsWith(title + ":", StringComparison.Ordinal) &&
                            !english.StartsWith(title + " -", StringComparison.Ordinal)) continue;
                        string qualifiedChinese = entry.Chinese;
                        int cut = qualifiedChinese.IndexOfAny(new char[] { '：', ':' });
                        if (cut > 0) qualifiedChinese = qualifiedChinese.Substring(0, cut).Trim();
                        return new List<MatchResult> { new MatchResult { Entry = new TranslationEntry {
                            English = text, Chinese = qualifiedChinese, Normalized = title,
                            Category = entry.Category, TaskId = "",
                            DetailWords = entry.DetailWords }, Start = 0, Length = title.Length } };
                    }
                }
                return none;
            }
            if (distance > 0 && second <= distance + 1) return none;
            string chinese = best[0].Chinese;
            foreach (TranslationEntry entry in best)
                if (!String.Equals(chinese, entry.Chinese, StringComparison.Ordinal)) return none;
            // With a resolved panel identity, an exact canonical name wins over its
            // contained words. Title/copy geometry still comes from the observed span.
            TranslationEntry winner = best[0];
            foreach (TranslationEntry entry in best)
                if (!String.IsNullOrEmpty(skillId) && CategoryId(entry.Category) == skillId)
                { winner = entry; break; }
            return new List<MatchResult> { new MatchResult { Entry = new TranslationEntry {
                English = text, Chinese = chinese, Normalized = title, Category = winner.Category,
                TaskId = "", DetailWords = winner.DetailWords }, Start = 0, Length = title.Length } };
        }

        // Exact whole-name spans only. A shorter skill name contained inside a longer
        // visible title ("Recovery" in "Critical Recovery") must not create ambiguity;
        // only the longest exact spans on the line participate.
        private sealed class SkillNameOccurrence
        {
            internal int Start;
            internal int Length;
            internal string Id;
        }

        // Exact whole-name spans only. A short name contained in a longer span at
        // the same position ("Recovery" in "Critical Recovery") is removed; the
        // same short name at an independent position still participates and makes
        // the line ambiguous. No arbitrary first/longest id is selected.
        private HashSet<string> ExactSkillNameIds(string text)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            string normalized = Normalize(text);
            List<SkillNameOccurrence> occurrences = new List<SkillNameOccurrence>();
            foreach (TranslationEntry entry in skillNamesForMatching)
            {
                if (entry.Normalized.Length == 0) continue;
                int position = normalized.IndexOf(entry.Normalized, StringComparison.Ordinal);
                while (position >= 0)
                {
                    if (IsBoundary(normalized, position - 1) &&
                        IsBoundary(normalized, position + entry.Normalized.Length))
                        occurrences.Add(new SkillNameOccurrence {
                            Start = position, Length = entry.Normalized.Length,
                            Id = CategoryId(entry.Category)
                        });
                    position = normalized.IndexOf(entry.Normalized,
                        Math.Min(normalized.Length, position + 1), StringComparison.Ordinal);
                }
            }
            foreach (SkillNameOccurrence occurrence in occurrences)
            {
                bool contained = false;
                foreach (SkillNameOccurrence other in occurrences)
                {
                    if (other.Length <= occurrence.Length || other.Start > occurrence.Start) continue;
                    if (other.Start + other.Length < occurrence.Start + occurrence.Length) continue;
                    contained = true;
                    break;
                }
                if (!contained) ids.Add(occurrence.Id);
            }
            return ids;
        }

        private string DetectUniqueSkillNameId(string text)
        {
            HashSet<string> exact = ExactSkillNameIds(text);
            if (exact.Count == 1) foreach (string id in exact) return id;
            if (exact.Count > 1) return "";
            // OCR spelling repair may still resolve a unique canonical name. The
            // repaired name's own category set must be unique; a shared Chinese
            // translation is never grounds for choosing one job's identity.
            List<MatchResult> matches = FindSkillNameMatches(text);
            if (matches.Count != 1) return "";
            string canonicalChinese = matches[0].Entry.Chinese;
            HashSet<string> sameName = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in skillNamesForMatching)
                if (String.Equals(entry.Chinese, canonicalChinese, StringComparison.Ordinal))
                    sameName.Add(CategoryId(entry.Category));
            if (sameName.Count == 1) foreach (string id in sameName) return id;
            return "";
        }

        internal static string SkillContentBody(string text)
        {
            string source = text ?? "";
            source = Regex.Replace(source,
                @"^\s*\[?\s*master\s*level\s*[:：]?\s*[0-9lIoOgqSsB]+\s*\]?\s*", "",
                RegexOptions.IgnoreCase);
            Match required = Regex.Match(source, @"\bRequired\s+Skill\s*:", RegexOptions.IgnoreCase);
            if (required.Success) source = source.Substring(0, required.Index);
            Match level = Regex.Match(source, @"\b(?:Current|Next)\s+Level\b", RegexOptions.IgnoreCase);
            if (level.Success) source = source.Substring(0, level.Index);
            return source.Trim();
        }

        private static bool IsMeaningfulSkillContent(TranslationEntry entry)
        {
            if (entry == null || entry.Normalized.Length < 18) return false;
            if (entry.Normalized.StartsWith("master level ", StringComparison.Ordinal) &&
                entry.Normalized.Length < 34) return false;
            if (entry.Normalized.StartsWith("required skill", StringComparison.Ordinal)) return false;
            return true;
        }

        private static bool ContainsWholePhrase(string text, string phrase)
        {
            if (String.IsNullOrEmpty(text) || String.IsNullOrEmpty(phrase)) return false;
            int position = text.IndexOf(phrase, StringComparison.Ordinal);
            while (position >= 0)
            {
                if (IsBoundary(text, position - 1) &&
                    IsBoundary(text, position + phrase.Length)) return true;
                position = text.IndexOf(phrase, Math.Min(text.Length, position + 1),
                    StringComparison.Ordinal);
            }
            return false;
        }

        internal sealed class SkillContentMatch
        {
            internal string SkillId = "";
            internal string English = "";
            internal string Chinese = "";
            internal float Score;
            internal bool Exact;
            internal string Source = "";
        }

        // Candidate descriptions for the visible body. Exact rows are preferred.
        // Approximate rows are scored only to identify a unique category; a tie is
        // reported as ambiguous rather than borrowing the first matching skill.
        internal List<SkillContentMatch> FindSkillContentMatches(string text)
        {
            List<SkillContentMatch> result = new List<SkillContentMatch>();
            string body = SkillContentBody(text);
            string normalized = Normalize(body);
            if (normalized.Length < 12) return result;

            Dictionary<string, SkillContentMatch> exact = new Dictionary<string, SkillContentMatch>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in skillTextEntries)
            {
                if (!IsMeaningfulSkillContent(entry)) continue;
                if (!ContainsWholePhrase(normalized, entry.Normalized)) continue;
                string id = CategoryId(entry.Category);
                SkillContentMatch candidate;
                if (!exact.TryGetValue(id, out candidate) || entry.Normalized.Length > candidate.Score)
                {
                    exact[id] = new SkillContentMatch {
                        SkillId = id, English = SkillContentBody(entry.English),
                        Chinese = entry.Chinese, Score = entry.Normalized.Length,
                        Exact = true, Source = "dictionary-exact"
                    };
                }
            }
            if (exact.Count > 0)
            {
                foreach (SkillContentMatch candidate in exact.Values)
                {
                    candidate.Score = 1.0f;
                    result.Add(candidate);
                }
                return result;
            }

            string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 5) return result;
            HashSet<string> input = new HashSet<string>(words, StringComparer.Ordinal);
            Dictionary<string, SkillContentMatch> best = new Dictionary<string, SkillContentMatch>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in skillTextEntries)
            {
                if (!IsMeaningfulSkillContent(entry) || entry.DetailWords == null) continue;
                int common = 0;
                foreach (string word in input) if (entry.DetailWords.Contains(word)) common++;
                if (common < 5) continue;
                float precision = (float)common / Math.Max(1, input.Count);
                float coverage = (float)common / Math.Max(1, entry.DetailWords.Count);
                if (precision < 0.55f || coverage < 0.18f) continue;
                float similarity = DetailTextSimilarity(normalized, entry.Normalized);
                if (similarity < 0.66f) continue;
                float score = similarity * 0.70f + precision * 0.20f + coverage * 0.10f;
                if (entry.Normalized.IndexOf(normalized, StringComparison.Ordinal) >= 0) score += 0.08f;
                string id = CategoryId(entry.Category);
                SkillContentMatch candidate;
                if (!best.TryGetValue(id, out candidate) || score > candidate.Score)
                    best[id] = new SkillContentMatch {
                        SkillId = id, English = SkillContentBody(entry.English),
                        Chinese = entry.Chinese, Score = score,
                        Exact = false, Source = "dictionary-words"
                    };
            }
            float bestScore = 0.0f;
            foreach (SkillContentMatch candidate in best.Values)
                if (candidate.Score > bestScore) bestScore = candidate.Score;
            foreach (SkillContentMatch candidate in best.Values)
                if (candidate.Score >= 0.70f && candidate.Score >= bestScore - 0.035f)
                    result.Add(candidate);
            return result;
        }

        private string DetectUniqueSkillContentId(string text)
        {
            List<SkillContentMatch> candidates = FindSkillContentMatches(text);
            if (candidates.Count == 0) return "";
            int exactCount = 0;
            HashSet<string> exactIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkillContentMatch candidate in candidates)
                if (candidate.Exact) { exactCount++; exactIds.Add(candidate.SkillId); }
            if (exactCount > 0)
            {
                if (exactIds.Count == 1) foreach (string id in exactIds) return id;
                return "";
            }
            SkillContentMatch top = null;
            float second = 0.0f;
            foreach (SkillContentMatch candidate in candidates)
            {
                if (top == null || candidate.Score > top.Score)
                {
                    if (top != null) second = Math.Max(second, top.Score);
                    top = candidate;
                }
                else if (candidate.Score > second) second = candidate.Score;
            }
            if (top == null || top.Score < 0.76f) return "";
            if (second > 0 && top.Score - second < 0.06f) return "";
            return top.SkillId;
        }

        // Used by the panel plan only when every candidate category supplies the
        // exact same Chinese for this body. That is a shared translation, not an
        // arbitrary skill identity; no ID is claimed.
        internal bool TryResolveSharedSkillContent(string text, out string chinese)
        {
            chinese = "";
            List<SkillContentMatch> candidates = FindSkillContentMatches(text);
            if (candidates.Count < 2) return false;
            string observed = SkillContentBody(text);
            foreach (SkillContentMatch candidate in candidates)
            {
                if (String.IsNullOrEmpty(candidate.Chinese) ||
                    String.IsNullOrEmpty(candidate.English)) return false;
                string unsafeReason;
                if (!SkillOverviewSafety.IsSafeOverview(observed, candidate.English,
                    candidate.Chinese, false, out unsafeReason)) return false;
                if (chinese.Length == 0) chinese = candidate.Chinese;
                else if (!String.Equals(chinese, candidate.Chinese, StringComparison.Ordinal)) return false;
            }
            return chinese.Length > 0;
        }
    }
}
