using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    // Expand chat abbreviations to plain English before the AI request.
    //
    // The prompt already explains the trade arrows, but short forms like "wts"/"lfp"
    // still get read backwards by the model on some frames. A table keeps each rule
    // in one editable place instead of scattered Regex calls, and a rule can be added
    // without touching code. Longer keys are applied first so "tyvm" is never eaten by
    // "ty". Only whole words match, and single-letter forms are deliberately absent.
    internal static class ChatSlangNormalizer
    {
        private sealed class Rule
        {
            internal string Short = "";
            internal string Expanded = "";
        }

        private static readonly List<Rule> rules = new List<Rule>();
        private static string loadedPath = "";
        private static bool loaded;

        internal static string Normalize(string text, string baseDirectory)
        {
            if (String.IsNullOrEmpty(text)) return text ?? "";
            EnsureLoaded(baseDirectory);
            if (rules.Count == 0) return text;
            string result = text;
            foreach (Rule rule in rules)
            {
                if (rule.Short.Length == 0) continue;
                string pattern = @"(?<![\p{L}\p{N}_])" + Regex.Escape(rule.Short) + @"(?![\p{L}\p{N}_])";
                result = Regex.Replace(result, pattern, rule.Expanded, RegexOptions.IgnoreCase);
            }
            return result;
        }

        internal static void Reset()
        {
            rules.Clear();
            loadedPath = "";
            loaded = false;
        }

        private static void EnsureLoaded(string baseDirectory)
        {
            string path = String.IsNullOrEmpty(baseDirectory)
                ? "" : Path.Combine(baseDirectory, "枫语幕简写.tsv");
            if (loaded && String.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase)) return;
            loaded = true;
            loadedPath = path;
            rules.Clear();
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                foreach (string raw in File.ReadLines(path, Encoding.UTF8))
                {
                    if (String.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] parts = raw.Split('\t');
                    if (parts.Length < 2) continue;
                    string shortForm = parts[0].Trim();
                    string expanded = parts[1].Trim();
                    if (shortForm.Length == 0 || expanded.Length == 0) continue;
                    rules.Add(new Rule { Short = shortForm, Expanded = expanded });
                }
            }
            catch (Exception)
            {
                rules.Clear();
            }
            // Longest short form first: "pm me" must win over "pm".
            rules.Sort(delegate(Rule left, Rule right)
            {
                return right.Short.Length.CompareTo(left.Short.Length);
            });
        }
    }
}
