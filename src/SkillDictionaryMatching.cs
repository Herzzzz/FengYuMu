using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed partial class TranslationStore
    {
        // Per-level skill effect text ("MP -15; Enemy Speed -10 ...") keyed by skill id
        // and level. The dictionary already stores these rows with a "等级N" source
        // marker; indexing them lets a visible "Current Level: N" pick the exact line
        // instead of fuzzy-matching the whole effect block.
        private readonly Dictionary<string, Dictionary<int, KeyValuePair<string, string>>> skillLevels =
            new Dictionary<string, Dictionary<int, KeyValuePair<string, string>>>(StringComparer.Ordinal);
        private const string SkillLevelPrefix = "怀旧服-技能说明#";
        internal void LoadSkillLevels(string path)
        {
            skillLevels.Clear();
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return;
            foreach (string raw in File.ReadLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrEmpty(raw) || raw.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 4 || !parts[2].StartsWith(SkillLevelPrefix, StringComparison.Ordinal)) continue;
                Match levelMatch = Regex.Match(parts[3], @"等级\s*(\d{1,3})");
                if (!levelMatch.Success) continue;
                int level;
                if (!Int32.TryParse(levelMatch.Groups[1].Value, out level)) continue;
                string skillId = parts[2].Substring(SkillLevelPrefix.Length).Trim();
                if (skillId.Length == 0) continue;
                string english = (parts[0] ?? "").Trim(), chinese = (parts[1] ?? "").Trim();
                if (english.Length == 0 || chinese.Length == 0) continue;
                Dictionary<int, KeyValuePair<string, string>> byLevel;
                if (!skillLevels.TryGetValue(skillId, out byLevel))
                {
                    byLevel = new Dictionary<int, KeyValuePair<string, string>>();
                    skillLevels[skillId] = byLevel;
                }
                byLevel[level] = new KeyValuePair<string, string>(english, chinese);
            }
        }

        internal bool TryGetSkillLevelText(string skillId, int level, out string english, out string chinese)
        {
            english = ""; chinese = "";
            Dictionary<int, KeyValuePair<string, string>> byLevel;
            if (String.IsNullOrEmpty(skillId) || level <= 0 || !skillLevels.TryGetValue(skillId, out byLevel))
                return false;
            KeyValuePair<string, string> pair;
            if (!byLevel.TryGetValue(level, out pair)) return false;
            english = pair.Key; chinese = pair.Value;
            return english.Length > 0 && chinese.Length > 0;
        }

        // The visible effect line is one row of the per-level table. Matching it against
        // every stored level of the same skill is exact by construction, so it replaces
        // template guessing for the common "MP -15; Enemy Speed -10 ..." shape.
        internal bool TryMatchSkillLevelBody(string bodyText, string skillId, out string chinese, out float similarity)
        {
            chinese = ""; similarity = 0f;
            Dictionary<int, KeyValuePair<string, string>> byLevel;
            if (String.IsNullOrEmpty(skillId) || !skillLevels.TryGetValue(skillId, out byLevel)) return false;
            string normalized = Normalize(bodyText);
            if (normalized.Length < 8) return false;
            float best = 0f; string bestChinese = "";
            foreach (KeyValuePair<int, KeyValuePair<string, string>> pair in byLevel)
            {
                string levelEnglish = pair.Value.Key;
                if (levelEnglish.Length == 0) continue;
                float score = TextSimilarity(normalized, Normalize(levelEnglish));
                if (score > best) { best = score; bestChinese = pair.Value.Value; }
            }
            if (best < 0.86f || bestChinese.Length == 0) return false;
            chinese = bestChinese; similarity = best;
            return true;
        }

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
            if (exact.Count > 1)
            {
                // Three job variants of one skill (MP Eater 2100000/2200000/2300000)
                // share the Chinese name and the same description, so any of them paints
                // identical text. Treating that as ambiguous left the whole tooltip in
                // English; a genuinely different translation still returns "".
                string shared = null;
                foreach (string id in exact)
                {
                    string chinese = SkillNameChinese(id);
                    if (shared == null) shared = chinese;
                    else if (!String.Equals(shared, chinese, StringComparison.Ordinal)) return "";
                }
                foreach (string id in exact) return id;
            }
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

        private string SkillNameChinese(string skillId)
        {
            foreach (TranslationEntry entry in skillNamesForMatching)
                if (String.Equals(CategoryId(entry.Category), skillId, StringComparison.Ordinal))
                    return entry.Chinese ?? "";
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
        // Share of the stored body's whole words that are still visible. Used only as
        // the last tier of the shared-body matcher, where an injected OCR fragment
        // breaks the character-level similarity but leaves the paragraph recognisable.
        private static float WordCoverage(string candidateNormalized, string observedNormalized)
        {
            if (String.IsNullOrEmpty(candidateNormalized) || String.IsNullOrEmpty(observedNormalized))
                return 0.0f;
            HashSet<string> observed = new HashSet<string>(
                observedNormalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            int total = 0, hit = 0;
            foreach (string word in candidateNormalized.Split(
                new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 4) continue;
                total++;
                if (observed.Contains(word)) hit++;
            }
            return total == 0 ? 0.0f : (float)hit / total;
        }

        // Opposite direction: share of the VISIBLE words that the stored body contains.
        // Used when OCR clipped the paragraph, so the stored-body coverage above stays low
        // even though every visible word is genuinely from that row.
        private static float ObservedCoverage(string observedNormalized, string candidateNormalized)
        {
            if (String.IsNullOrEmpty(candidateNormalized) || String.IsNullOrEmpty(observedNormalized))
                return 0.0f;
            HashSet<string> candidate = new HashSet<string>(
                candidateNormalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            int total = 0, hit = 0;
            foreach (string word in observedNormalized.Split(
                new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 4) continue;
                total++;
                if (candidate.Contains(word)) hit++;
            }
            return total == 0 ? 0.0f : (float)hit / total;
        }

        // arbitrary skill identity; no ID is claimed.
        internal bool TryResolveSharedSkillContent(string text, out string chinese)
        {
            return TryResolveSharedSkillContent(text, "", out chinese);
        }

        // When the skill ID is already known the shared pass is restricted to that
        // skill's own rows: several buffs share the "Reduces physical damage taken ..."
        // wording, and comparing across them can only produce a conflict.
        internal bool TryResolveSharedSkillContent(string text, string skillId, out string chinese)
        {
            chinese = "";
            List<SkillContentMatch> candidates = FindSkillContentMatches(text);
            string observed = SkillContentBody(text);
            // The panel can name a job-branch id that the shipped rows do not use (MP Eater
            // is stored under 2100000/2200000/2300000). Restricting the scan to that exact
            // id then matches nothing and the body stays English on some frames only.
            // Fall back to the cross-id scan, which still requires identical Chinese.
            if (skillId.Length > 0)
            {
                bool hasEntry = false;
                foreach (TranslationEntry entry in skillTextEntries)
                    if (entry != null && CategoryId(entry.Category) == skillId) { hasEntry = true; break; }
                if (!hasEntry) skillId = "";
            }
            if (candidates.Count >= 2)
            {
                bool safeCandidates = true;
                foreach (SkillContentMatch candidate in candidates)
                {
                    if (String.IsNullOrEmpty(candidate.Chinese) ||
                        String.IsNullOrEmpty(candidate.English)) { safeCandidates = false; break; }
                    string unsafeReason;
                    if (!SkillOverviewSafety.IsSafeOverview(observed, candidate.English,
                        candidate.Chinese, false, out unsafeReason))
                    { safeCandidates = false; break; }
                    if (chinese.Length == 0) chinese = candidate.Chinese;
                    else if (!String.Equals(chinese, candidate.Chinese, StringComparison.Ordinal))
                    { safeCandidates = false; break; }
                }
                if (!safeCandidates) chinese = "";
            }
            if (chinese.Length > 0) return true;
            // Fallback for abilities shipped under several job-branch ids (MP Eater is
            // stored as 2100000/2200000/2300000 with an identical Chinese body). The
            // bucket search above is deliberately narrow and misses them, so the visible
            // body is compared with every stored skill text. A translation is accepted
            // only when every near-identical body carries exactly the same Chinese.
            string shared = "";
            int sharedHits = 0;
            string observedBody = Normalize(observed);
            if (observedBody.Length >= 18)
            {
                foreach (TranslationEntry entry in skillTextEntries)
                {
                    if (entry == null || String.IsNullOrEmpty(entry.Chinese)) continue;
                    if (skillId.Length > 0 && CategoryId(entry.Category) != skillId) continue;
                    string candidateBody = Normalize(SkillContentBody(entry.English));
                    if (candidateBody.Length < 18) continue;
                    // Windows OCR drops the tail of a wrapped tooltip paragraph, so a
                    // visible prefix of the stored body is still evidence for that body.
                    bool prefix = observedBody.Length >= 60 &&
                        candidateBody.StartsWith(observedBody, StringComparison.Ordinal);
                    bool similar = prefix || TextSimilarity(observedBody, candidateBody) >= 0.97f;
                    // OCR can also inject garbage mid-paragraph ("... any monster that
                    // rlCjn-elemental m contact damage ..."). When that happens neither the
                    // full similarity nor the prefix test survives, but nearly every whole
                    // word of the stored body is still visible. Require 85% word coverage
                    // and keep the identical-Chinese rule below as the safety net.
                    if (!similar && observedBody.Length >= 60)
                        // A smaller client window prints the body at ~11 px, so OCR drops a few
                // words. 0.78 missed those bodies entirely; 0.70 still requires the same
                // Chinese across every matching entry, so a wrong entry cannot win.
                similar = WordCoverage(candidateBody, observedBody) >= 0.78f;
                    // The visible body can also be clipped: only the first sentence survives
                    // OCR ("Absorbs the enemy's MP when attacking with Magic skills."), while
                    // the stored row keeps the whole paragraph. The stored-body coverage above
                    // can never pass then. Accept when nearly every VISIBLE word exists in the
                    // stored body; the identical-Chinese rule below still guards the match.
                    if (!similar && observedBody.Length >= 40)
                        similar = ObservedCoverage(observedBody, candidateBody) >= 0.80f;
                    if (!similar) continue;
                    // The same ability ships both with and without the "[最高等级：N]"
                    // header; strip it before comparing so the two rows agree.
                    string candidateChinese = Regex.Replace(entry.Chinese,
                        @"^\s*\[?\s*最高等级\s*[:：]?\s*[0-9OoIlSsBbZzGq]+\s*\]?\s*", "").Trim();
                    if (candidateChinese.Length == 0) continue;
                    sharedHits++;
                    if (shared.Length == 0) shared = candidateChinese;
                    else if (!String.Equals(shared, candidateChinese, StringComparison.Ordinal))
                    { shared = ""; break; }
                }
            }
            if (shared.Length > 0 && sharedHits > 0) { chinese = shared; return true; }
            return false;
        }
    }
}
