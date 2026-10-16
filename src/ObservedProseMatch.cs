using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // Compare one complete observed phrase. Only whitespace and a small number
    // of OCR-shaped characters may differ; no missing sentences or fuzzy nouns.
    internal static class ObservedProseMatch
    {
        internal static bool Equivalent(string source, string candidate)
        {
            if (String.IsNullOrWhiteSpace(source) || String.IsNullOrWhiteSpace(candidate) ||
                source.Length > 8192 || candidate.Length > 8192) return false;
            string a = TranslationStore.Normalize(source), b = TranslationStore.Normalize(candidate);
            if (a.Length < 28 || b.Length < 28 || Numbers(source) != Numbers(candidate)) return false;
            List<int> positions = new List<int>(); StringBuilder compact = new StringBuilder();
            for (int i = 0; i < b.Length; i++) if (b[i] != ' ') { positions.Add(i); compact.Append(b[i]); }
            string expected = compact.ToString(); compact.Length = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != ' ') compact.Append(a[i]);
            string actual = compact.ToString();
            if (actual.Length != expected.Length) return false;
            int changes = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] == expected[i]) continue;
                if (!OcrPair(actual[i], expected[i]) || !LetterWord(b, positions[i]) ||
                    ++changes > Math.Min(3, Math.Max(1, expected.Length / 60))) return false;
            }
            return true;
        }

        private static bool OcrPair(char source, char target)
        {
            return (source == '0' && target == 'o') || (source == '1' && (target == 'i' || target == 'l')) ||
                (source == '5' && target == 's') || (source == '8' && target == 'b') ||
                ((source == 'l' || source == 'i') && (target == 'l' || target == 'i'));
        }
        private static bool LetterWord(string text, int index)
        {
            int left = index, right = index + 1;
            while (left > 0 && text[left - 1] != ' ') left--;
            while (right < text.Length && text[right] != ' ') right++;
            if (right - left < 4) return false;
            for (int i = left; i < right; i++) if (!Char.IsLetter(text[i]) && text[i] != '\'') return false;
            return true;
        }
        private static string Numbers(string text)
        {
            StringBuilder result = new StringBuilder();
            // A digit inside an otherwise aligned word (rec0ver) is handled by
            // OcrPair; standalone quantities and percentages must remain exact.
            foreach (Match match in Regex.Matches(text, @"(?<![\p{L}\p{N}])[-+]?\d+(?:[.,]\d+)?(?:%|％)?"))
                result.Append(match.Value.Replace('％', '%')).Append('|');
            return result.ToString();
        }
    }
}
