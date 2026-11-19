using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // The class ribbon of an equips tooltip names the jobs that may wear the item
    // ("MAGICIAN THIEF"). It is not an attribute/value pair, so it deliberately lives
    // outside EquipmentFieldGrammar: that grammar stays class-agnostic and the scope
    // test forbids class names inside it.
    //
    // Windows OCR returns the ribbon as one run of words, often with one letter-shape
    // error per class ("MRGICIRN THIEF"). A fuzzy token must therefore keep at least
    // 80% of a class name and be at least six characters long; every other letter in
    // the row disqualifies the row, so a quest title or chat line that happens to
    // contain "thief" cannot become an allowed-job label. Two distinct classes are
    // required because one word alone is not a ribbon.
    internal static class ItemClassRibbon
    {
        private static readonly string[] Tokens =
            { "beginner", "warrior", "magician", "bowman", "thief", "pirate" };
        private static readonly string[] Chinese =
            { "新手", "战士", "魔法师", "弓箭手", "飞侠", "海盗" };

        internal static bool TryParse(string text, bool trustedTooltip, out string chinese,
            out int start, out int length)
        {
            return TryParse(text, trustedTooltip, null, out chinese, out start, out length);
        }

        // The client prints usable classes in orange and unusable ones in dull grey; the
        // game itself is the only authority on that distinction. The optional predicate
        // receives a token's character range and decides whether that class is usable.
        internal static bool TryParse(string text, bool trustedTooltip,
            Func<int, int, bool> usable, out string chinese, out int start, out int length)
        {
            chinese = ""; start = 0; length = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] tokens = Regex.Split(text.Trim(), @"[\s,，、/／|•·]+");
            List<int> jobs = new List<int>();      // class index or -1
            List<int> tokenStart = new List<int>();
            List<int> tokenEnd = new List<int>();
            int letters = 0;
            foreach (string raw in tokens)
            {
                if (raw.Length == 0) continue;
                string token = raw.Trim('.', ':', '：', ';', '-', '_', '(', ')');
                if (token.Length == 0) continue;
                foreach (char c in token) if (Char.IsLetter(c)) letters++;
                string canonical = token.ToLowerInvariant().Replace('0', 'o').Replace('1', 'i')
                    .Replace('4', 'a');
                int offset = text.IndexOf(raw, StringComparison.Ordinal);
                if (offset < 0) offset = 0;
                jobs.Add(IndexOf(canonical));
                tokenStart.Add(offset);
                tokenEnd.Add(offset + raw.Length);
            }
            if (jobs.Count == 0 || letters == 0) return false;
            if (!trustedTooltip && letters > 60) return false;
            // Only a contiguous run of class words can be a ribbon. Chat lines such as
            // "S> magician thief items" contain the same words but do not put them in a
            // row of their own, and the coverage test below rejects those.
            int bestStart = -1, bestCount = 0, bestLetters = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                if (jobs[i] < 0) continue;
                int runLetters = 0, count = 0, j = i;
                for (; j < jobs.Count && jobs[j] >= 0; j++)
                {
                    runLetters += CountLetters(text, tokenStart[j], tokenEnd[j]);
                    count++;
                }
                if (count > bestCount || (count == bestCount && runLetters > bestLetters))
                {
                    bestStart = i; bestCount = count; bestLetters = runLetters;
                }
                i = j - 1;
            }
            if (bestStart < 0 || bestCount < 2) return false;
            HashSet<int> distinct = new HashSet<int>();
            for (int i = bestStart; i < bestStart + bestCount; i++) distinct.Add(jobs[i]);
            if (distinct.Count < 2) return false;
            // The ribbon must own most of the row: a title, a price line or a chat
            // sentence that merely mentions two classes keeps the rest of its words.
            // Inside a confirmed equips tooltip the surrounding fields legitimately share
            // the merged OCR row, so the contiguous run alone is the evidence there.
            if (!trustedTooltip && bestLetters * 10 < letters * 6) return false;
            List<string> names = new List<string>();
            for (int i = bestStart; i < bestStart + bestCount; i++)
            {
                // Only a class the client actually painted as usable (orange) may be listed.
                if (usable != null && !usable(tokenStart[i], tokenEnd[i])) continue;
                if (!names.Contains(Chinese[jobs[i]])) names.Add(Chinese[jobs[i]]);
            }
            if (names.Count == 0) return false;
            chinese = "可用职业：" + String.Join("／", names.ToArray());
            start = tokenStart[bestStart];
            length = tokenEnd[bestStart + bestCount - 1] - start;
            return true;
        }

        private static int CountLetters(string text, int from, int to)
        {
            int count = 0;
            for (int i = Math.Max(0, from); i < Math.Min(text.Length, to); i++)
                if (Char.IsLetter(text[i])) count++;
            return count;
        }

        private static int IndexOf(string token)
        {
            for (int i = 0; i < Tokens.Length; i++) if (token == Tokens[i]) return i;
            // OCR letter-shape slips only: "mrgicirn" keeps 6 of 8 letters, "urrrior"
            // keeps 5 of 7. The contiguous-run and coverage rules above are what keep
            // ordinary words out, so the per-token threshold can stay at 70%.
            if (token.Length < 5) return -1;
            for (int i = 0; i < Tokens.Length; i++)
            {
                string job = Tokens[i];
                if (Math.Abs(token.Length - job.Length) > 2) continue;
                int distance = Distance(token, job);
                if (1.0f - (float)distance / Math.Max(token.Length, job.Length) >= 0.7f) return i;
            }
            return -1;
        }

        private static int Distance(string left, string right)
        {
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }
    }
}
