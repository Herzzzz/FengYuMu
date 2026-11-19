using System;
using System.Collections.Generic;

namespace MapleOverlay
{
    // Majority vote over the colour judgement of one chat line across frames.
    // A translucent chat window lets the moving background tint a row on some frames,
    // and one wrong frame used to relabel the whole line (measured upstream: 18 of 63
    // samples flipped a plain line to whisper / broadcast). Recording every observation
    // and using the winner keeps a single noisy frame from deciding the category.
    internal sealed class ChatColorConsensus
    {
        private readonly int capacity;
        private readonly Dictionary<string, Dictionary<ChatSourceCategory, int>> votes =
            new Dictionary<string, Dictionary<ChatSourceCategory, int>>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();

        internal ChatColorConsensus(int capacity)
        {
            this.capacity = Math.Max(8, capacity);
        }

        internal void Record(string text, ChatSourceCategory category)
        {
            string key = TranslationStore.Normalize(text ?? "");
            if (key.Length == 0) return;
            Dictionary<ChatSourceCategory, int> counts;
            if (!votes.TryGetValue(key, out counts))
            {
                counts = new Dictionary<ChatSourceCategory, int>();
                votes[key] = counts;
                order.Add(key);
                while (order.Count > capacity)
                {
                    votes.Remove(order[0]);
                    order.RemoveAt(0);
                }
            }
            int count;
            counts.TryGetValue(category, out count);
            counts[category] = count + 1;
        }

        // Ties keep the earlier category (dictionary insertion order), which is a
        // deterministic fallback, not a stronger rule; real samples rarely tie.
        internal ChatSourceCategory Winner(string text)
        {
            string key = TranslationStore.Normalize(text ?? "");
            Dictionary<ChatSourceCategory, int> counts;
            if (key.Length == 0 || !votes.TryGetValue(key, out counts) || counts.Count == 0)
                return ChatSourceCategory.Unknown;
            ChatSourceCategory best = ChatSourceCategory.Unknown;
            int bestCount = -1;
            foreach (KeyValuePair<ChatSourceCategory, int> pair in counts)
                if (pair.Value > bestCount) { best = pair.Key; bestCount = pair.Value; }
            return best;
        }

        internal void Reset()
        {
            votes.Clear();
            order.Clear();
        }
    }
}
