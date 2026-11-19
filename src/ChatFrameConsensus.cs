using System;
using System.Collections.Generic;

namespace MapleOverlay
{
    // Collapse the same visible chat row to one spelling across frames.
    //
    // A translucent chat window makes every frame read one row slightly differently
    // ("You've been stunned" / "QYouve been stunned" / "Yowe bee(l stunnéd"). Downstream
    // duplicate detection compares text, so a row that never repeats verbatim is
    // translated again and again -- upstream measured 155 of 181 translated lines being
    // the same message. The output is always a string that was actually observed
    // (medoid of the variants), never a per-character blend: a blended word matches
    // neither the dictionary nor the previous observation.
    //
    // New lines do not wait: fewer than three votes pass through unchanged.
    internal sealed class ChatFrameConsensus
    {
        // Similarity threshold for "the same row read twice". Upstream measured
        // same-line variants at median 0.61 (min 0.33) and different lines at most 0.22,
        // so 0.55 sits inside that gap.
        private const float SameVariant = 0.55f;
        private const int HistoryFrames = 2;
        private const int Votes = 3;
        private const int SettledMemory = 40;

        private sealed class ObservedLine
        {
            internal string Text = "";
            internal string Key = "";

            internal static ObservedLine Of(string text)
            {
                return new ObservedLine { Text = text ?? "", Key = TranslationStore.Normalize(text) };
            }
        }

        private readonly List<List<ObservedLine>> history = new List<List<ObservedLine>>();
        private readonly List<ObservedLine> settled = new List<ObservedLine>();

        internal List<string> Accept(List<string> frameLines)
        {
            List<string> result = new List<string>();
            if (frameLines == null || frameLines.Count == 0) return result;
            List<ObservedLine> current = new List<ObservedLine>();
            foreach (string line in frameLines) current.Add(ObservedLine.Of(line));

            ObservedLine[] stuck = AssignSettled(current);
            for (int index = 0; index < current.Count; index++)
            {
                if (stuck[index] != null) { result.Add(stuck[index].Text); continue; }
                ObservedLine medoid = ChooseMedoid(current[index]);
                result.Add(medoid == null ? current[index].Text : medoid.Text);
            }

            history.Insert(0, current);
            while (history.Count > HistoryFrames) history.RemoveAt(history.Count - 1);
            return result;
        }

        internal void Reset()
        {
            history.Clear();
            settled.Clear();
        }

        // One settled representative per visible row. Two rows in the same frame are two
        // messages (a two-line broadcast differs only in its item name), so a settled
        // string is consumed by at most one row.
        private ObservedLine[] AssignSettled(List<ObservedLine> current)
        {
            ObservedLine[] result = new ObservedLine[current.Count];
            bool[] used = new bool[settled.Count];
            for (int index = 0; index < current.Count; index++)
            {
                int best = -1;
                float bestScore = SameVariant;
                for (int candidate = 0; candidate < settled.Count; candidate++)
                {
                    if (used[candidate]) continue;
                    float score = TranslationStore.TextSimilarity(current[index].Key, settled[candidate].Key);
                    if (score >= bestScore) { best = candidate; bestScore = score; }
                }
                if (best < 0) continue;
                used[best] = true;
                result[index] = settled[best];
            }
            return result;
        }

        private ObservedLine ChooseMedoid(ObservedLine target)
        {
            List<ObservedLine> candidates = new List<ObservedLine> { target };
            foreach (List<ObservedLine> frame in history)
            {
                ObservedLine best = null;
                float bestScore = 0f;
                foreach (ObservedLine line in frame)
                {
                    if (line.Key.Length == 0) continue;
                    float score = TranslationStore.TextSimilarity(target.Key, line.Key);
                    if (score >= SameVariant && score > bestScore) { best = line; bestScore = score; }
                }
                if (best != null && !ContainsText(candidates, best.Text)) candidates.Add(best);
            }
            if (candidates.Count < Votes) return null;

            ObservedLine medoid = candidates[0];
            float bestTotal = -1f;
            foreach (ObservedLine candidate in candidates)
            {
                float total = 0f;
                foreach (ObservedLine other in candidates)
                    if (!ReferenceEquals(other, candidate))
                        total += TranslationStore.TextSimilarity(candidate.Key, other.Key);
                if (total > bestTotal) { medoid = candidate; bestTotal = total; }
            }
            Remember(medoid);
            return medoid;
        }

        private static bool ContainsText(List<ObservedLine> lines, string text)
        {
            foreach (ObservedLine line in lines)
                if (String.Equals(line.Text, text, StringComparison.Ordinal)) return true;
            return false;
        }

        private void Remember(ObservedLine line)
        {
            foreach (ObservedLine existing in settled)
                if (String.Equals(existing.Text, line.Text, StringComparison.Ordinal)) return;
            settled.Insert(0, line);
            while (settled.Count > SettledMemory) settled.RemoveAt(settled.Count - 1);
        }
    }
}
