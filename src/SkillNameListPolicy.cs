using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal static class SkillNameListPolicy
    {
        internal static bool IsListContext(List<OcrLine> all, OcrLine line, float scale,
            TranslationStore translations)
        {
            if (all == null || line == null || translations == null || line.Words.Count == 0) return false;
            if (scale <= 0.0f) scale = 1.0f;
            string own = TranslationStore.Normalize(line.Text);
            if (own.Length < 3) return false;
            bool localHeader = false;
            int neighbors = 0;
            RectangleF ownBounds = BoundsOf(line);
            if (ownBounds.IsEmpty) return false;
            foreach (OcrLine candidate in all)
            {
                if (candidate == null || candidate == line) continue;
                string normalized = TranslationStore.Normalize(candidate.Text);
                RectangleF other = BoundsOf(candidate);
                if (other.IsEmpty) continue;
                bool headerText = normalized.Contains("skill inventory") ||
                    normalized.Contains("skill book") || normalized.Contains("skill point") ||
                    normalized == "skills" || normalized.EndsWith(" skills", StringComparison.Ordinal);
                if (headerText && LocallyRelated(other, ownBounds, scale))
                    localHeader = true;
                // Neighbour evidence must be in the same local column and close in
                // both axes. A name in another panel cannot make this row a list row.
                if (Math.Abs(other.Left - ownBounds.Left) >
                    Math.Max(28.0f, ownBounds.Width * 0.35f)) continue;
                if (Math.Abs(other.Height - ownBounds.Height) >
                    Math.Max(6.0f, ownBounds.Height * 0.6f)) continue;
                if (Math.Abs(other.Top - ownBounds.Top) >
                    Math.Max(80.0f, ownBounds.Height * 5.0f) * scale) continue;
                if (TranslationsOfListRow(candidate, translations)) neighbors++;
            }
            // A header is explicit list evidence only in the same local region as
            // the candidate. Without one, at least two nearby same-column exact
            // skill rows are required.
            return localHeader || neighbors >= 2;
        }

        private static bool LocallyRelated(RectangleF header, RectangleF row, float scale)
        {
            if (header.IsEmpty || row.IsEmpty) return false;
            float leftDrift = Math.Abs(row.Left - header.Left);
            if (leftDrift > Math.Max(80.0f, header.Width * 0.8f)) return false;
            if (row.Top < header.Top - 24.0f * scale) return false;
            if (row.Top - header.Bottom > 480.0f * scale) return false;
            return true;
        }

        private static bool TranslationsOfListRow(OcrLine line, TranslationStore translations)
        {
            string text = (line.Text ?? "").Trim();
            if (text.Length < 3) return false;
            List<MatchResult> full = translations.FindSkillNameMatches(text);
            if (full.Count == 1) return true;
            Match level = Regex.Match(text, @"\s+(?:(?:lv|lvl|level)\s*)?\d{1,3}\s*$",
                RegexOptions.IgnoreCase);
            if (!level.Success || level.Index < 3) return false;
            string name = text.Substring(0, level.Index).TrimEnd();
            return translations.FindSkillNameMatches(name).Count == 1;
        }

        internal static RectangleF BoundsOf(OcrLine line)
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
            return left == Single.MaxValue ? RectangleF.Empty :
                RectangleF.FromLTRB(left, top, right, bottom);
        }
    }

    internal sealed partial class OverlayForm
    {
        private bool TryAddSkillListNameLabel(List<OverlayLabel> output, OcrLine line,
            List<OcrLine> allLines, float ocrScale)
        {
            if (line == null || line.Words.Count == 0) return false;
            string text = (line.Text ?? "").Trim();
            if (text.Length < 4 || text.Length > 64) return false;
            if (text.IndexOf("...", StringComparison.Ordinal) >= 0 ||
                text.IndexOf('\u2026') >= 0) return false;
            if (SceneClassifier.LooksLikePlayerChat(text)) return false;
            if (!SkillNameListPolicy.IsListContext(allLines, line, ocrScale, translations)) return false;

            // Prefer the complete visible title (some real names end in a digit);
            // only then treat a trailing number as the optional list level.
            List<MatchResult> fullMatches = translations.FindSkillNameMatches(text);
            bool fullExact = fullMatches.Count == 1 &&
                String.Equals(TranslationStore.Normalize(text),
                    fullMatches[0].Entry.Normalized, StringComparison.Ordinal);
            List<MatchResult> matches = fullExact ? fullMatches : new List<MatchResult>();
            int nameLength = text.Length;
            RectangleF levelSpan = RectangleF.Empty;
            bool levelMeasured = false;
            bool trailingLevel = false;
            if (!fullExact)
            {
                Match level = Regex.Match(text, @"\s+(?:(?:lv|lvl|level)\s*)?(?<level>\d{1,3})\s*$",
                    RegexOptions.IgnoreCase);
                if (!level.Success || level.Index < 4) return false;
                trailingLevel = true;
                nameLength = level.Index;
                string name = text.Substring(0, nameLength).TrimEnd();
                matches = translations.FindSkillNameMatches(name);
                if (matches.Count != 1) return false;
                nameLength = text.LastIndexOf(name, StringComparison.Ordinal) + name.Length;
                levelSpan = PreciseTextSpan(new List<OcrLine> { line }, text, level.Index,
                    level.Length, out levelMeasured, false);
            }
            if (matches.Count != 1) return false;
            string nameText = text.Substring(0, nameLength).TrimEnd();
            string normalizedName = TranslationStore.Normalize(nameText);
            if (!String.Equals(normalizedName, matches[0].Entry.Normalized, StringComparison.Ordinal) &&
                translations.DetectSkillId(nameText).Length == 0)
                return false;
            if (nameText.Length < 4) return false;

            bool measured;
            RectangleF nameSpan = PreciseTextSpan(new List<OcrLine> { line }, text, 0,
                nameLength, out measured, true);
            if (nameSpan.IsEmpty) return false;
            if (!levelSpan.IsEmpty && levelSpan.Left > nameSpan.Left)
                nameSpan.Width = Math.Max(8.0f,
                    levelSpan.Left - nameSpan.Left - 2.0f * Math.Max(1.0f, ocrScale));
            RectangleF overlay = OverlaySpan(nameSpan, ocrScale);
            if (overlay.Width < 8 || overlay.Height < 5) return false;
            output.RemoveAll(delegate(OverlayLabel label) {
                if (label == null) return false;
                RectangleF bounds = label.HasSourceBounds ? label.SourceBounds : label.Bounds;
                return overlay.IntersectsWith(bounds);
            });
            int before = output.Count;
            AddDetailBlockLabelAt(output, new List<OcrLine> { line }, nameSpan,
                matches[0].Entry.Chinese, ocrScale, false);
            if (output.Count <= before) return false;
            OverlayLabel added = output[output.Count - 1];
            added.SemanticRole = "skill-name";
            added.MatchSource = "skill-list-name";
            added.Confidence = LabelConfidence.High;
            added.SourceBoundaryEstimated = !measured;
            added.PanelBounds = OverlaySpan(SkillNameListPolicy.BoundsOf(line), ocrScale);
            added.LayoutFontSize = Math.Max(11.0f, Math.Min(16.0f,
                nameSpan.Height / Math.Max(0.01f, ocrScale) - 3.0f));
            added.LayoutMinimumFontSize = added.LayoutFontSize;
            added.LayoutFontFamily = "SimSun";
            if (!levelSpan.IsEmpty)
                added.KeepClearBounds = OverlaySpan(levelSpan, ocrScale);
            else if (levelSpan.IsEmpty && trailingLevel)
            {
                RectangleF lineBounds = SkillNameListPolicy.BoundsOf(line);
                if (!lineBounds.IsEmpty && lineBounds.Right > nameSpan.Right)
                    added.KeepClearBounds = OverlaySpan(RectangleF.FromLTRB(nameSpan.Right,
                        nameSpan.Top, lineBounds.Right, nameSpan.Bottom), ocrScale);
            }
            return true;
        }
    }
}
