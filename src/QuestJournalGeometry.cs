using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class QuestJournalDocument
    {
        internal string Title, Body;
        internal Color Background;
        internal float FontHeight;
        // Confirmed OCR rows for the selected prose, in prepared-frame coordinates.
        // Keeping them lets the composition pass prove that the single replacement
        // region covers every row instead of relying on the paper scan alone.
        internal RectangleF TextBounds;
        // The panel typography/wrap/page plan is built once while the label is
        // selected and reused by the compositor. This keeps title/body sizes and
        // line breaks identical across repeated F8 passes.
        internal JournalLayoutPlan Layout;
    }

    // Shared typography for one panel. Quest title and body use the same family and
    // pixel unit; only the title is larger/bold. Sizes are derived once from the
    // measured source line height, never per rendered row.
    internal sealed class PanelTypography
    {
        internal string Family;
        internal GraphicsUnit Unit;
        internal float BodySize, TitleSize;
        internal float LeftInset, RightGutter, TopInset, BottomInset, TitleGap;
        internal float LineHeight, TitleLineHeight;

        internal Font CreateBodyFont()
        {
            return new Font(Family, BodySize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        internal Font CreateTitleFont(float size)
        {
            return new Font(Family, size, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        internal static PanelTypography ForJournal(Graphics graphics, Rectangle region, float sourceHeight)
        {
            PanelTypography type = new PanelTypography();
            type.Family = "Microsoft YaHei";
            type.Unit = GraphicsUnit.Pixel;
            float hint = sourceHeight;
            if (Single.IsNaN(hint) || Single.IsInfinity(hint) || hint < 10.0f) hint = 16.0f;
            if (hint > 48.0f) hint = 48.0f;
            float body = (float)Math.Round(hint * 0.9f);
            if (body < 15.0f) body = 15.0f;
            if (body > 28.0f) body = 28.0f;
            // A very short replacement area cannot support a readable body line at
            // this size. The plan is rejected instead of silently shrinking to 8px.
            float maximum = region.Height / 7.0f;
            if (body > maximum) body = (float)Math.Floor(maximum);
            type.BodySize = body;
            type.TitleSize = Math.Min(36.0f, body + 4.0f);
            type.LeftInset = Math.Max(4.0f, (float)Math.Ceiling(body * 0.28f));
            // Reserve the rightmost strip for glyph overhang and the game's
            // scrollbar/border. Wrapped lines are measured against this exact width.
            type.RightGutter = Math.Max(6.0f, (float)Math.Ceiling(body * 0.45f) + 2.0f);
            type.TopInset = Math.Max(3.0f, (float)Math.Ceiling(body * 0.18f));
            type.BottomInset = Math.Max(3.0f, (float)Math.Ceiling(body * 0.18f));
            type.TitleGap = Math.Max(5.0f, (float)Math.Ceiling(body * 0.40f));
            using (StringFormat format = QuestJournalLayout.TextFormat())
            {
                using (Font bodyFont = type.CreateBodyFont())
                    type.LineHeight = QuestJournalLayout.MeasureLineHeight(graphics, bodyFont, format);
                using (Font titleFont = type.CreateTitleFont(type.TitleSize))
                    type.TitleLineHeight = QuestJournalLayout.MeasureLineHeight(graphics, titleFont, format);
            }
            return type;
        }
    }

    internal sealed class JournalLine
    {
        internal string Text;
        internal float Width;
        internal int Start, End;
    }

    internal sealed class JournalLayoutPlan
    {
        internal Rectangle Region;
        internal PanelTypography Type;
        internal RectangleF Content, Title, Body, Footer;
        internal float TitleSize, TitleLineHeight;
        internal List<string> TitleLines;
        internal List<string> Pages;
        internal List<List<string>> PageLines;
        internal List<JournalLine> Lines;
        internal int PageCount;
        internal float MaxLineWidth;
        internal string Overflow, TitleOverflow;

        internal string Describe()
        {
            return "body=" + Type.BodySize.ToString("0.#", CultureInfo.InvariantCulture) +
                "px unit=" + Type.Unit +
                " title=" + TitleSize.ToString("0.#", CultureInfo.InvariantCulture) + "px" +
                " wrap=" + Content.Width.ToString("0.#", CultureInfo.InvariantCulture) +
                " content=" + Content.Left.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                Content.Top.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                Content.Width.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                Content.Height.ToString("0.#", CultureInfo.InvariantCulture) +
                " pages=" + PageCount + " overflow=" + Overflow;
        }
    }

    internal static class QuestJournalLayout
    {
        internal const int MaximumCharacters = 4096;
        internal const int MaximumPages = 128;

        internal static JournalLayoutPlan Build(Graphics graphics, Rectangle region,
            float sourceHeight, string title, string body)
        {
            if (graphics == null || region.Width < 40 || region.Height < 30) return null;
            if (String.IsNullOrWhiteSpace(body) || body.Length > MaximumCharacters) return null;
            PanelTypography type = PanelTypography.ForJournal(graphics, region, sourceHeight);
            if (type.BodySize < 15.0f) return null;
            JournalLayoutPlan plan = new JournalLayoutPlan();
            plan.Region = region;
            plan.Type = type;
            plan.Content = new RectangleF(region.Left + type.LeftInset, region.Top + type.TopInset,
                region.Width - type.LeftInset - type.RightGutter,
                region.Height - type.TopInset - type.BottomInset);
            if (plan.Content.Width < type.BodySize * 1.5f ||
                plan.Content.Height < type.BodySize * 1.4f) return null;

            using (StringFormat format = TextFormat())
            {
                plan.MaxLineWidth = 0.0f;
                // 1. Stable title hierarchy. Wrap at the measured width first; only
                //    reduce the title, never the body base size, when it is too tall.
                float titleSize = type.TitleSize;
                List<JournalLine> titleLines = null;
                float titleLineHeight = type.TitleLineHeight;
                bool reduced = false;
                for (float size = type.TitleSize; size >= type.BodySize; size -= 1.0f)
                {
                    List<JournalLine> candidate;
                    float lineHeight;
                    using (Font font = type.CreateTitleFont(size))
                    {
                        candidate = WrapText(graphics, title, font, plan.Content.Width, format);
                        lineHeight = MeasureLineHeight(graphics, font, format);
                    }
                    if (candidate == null)
                    {
                        if (size <= type.BodySize) return null;
                        continue;
                    }
                    float height = candidate.Count * lineHeight;
                    if (candidate.Count <= 3 && height <= plan.Content.Height * 0.42f)
                    {
                        titleSize = size;
                        titleLines = candidate;
                        titleLineHeight = lineHeight;
                        reduced = size < type.TitleSize - 0.01f;
                        break;
                    }
                    if (size <= type.BodySize && candidate.Count <= 4 &&
                        height <= plan.Content.Height * 0.55f)
                    {
                        titleSize = size;
                        titleLines = candidate;
                        titleLineHeight = lineHeight;
                        reduced = true;
                        break;
                    }
                }
                if (titleLines == null)
                {
                    if (!String.IsNullOrWhiteSpace(title)) return null;
                    titleLines = new List<JournalLine>();
                }
                plan.TitleLines = new List<string>();
                foreach (JournalLine line in titleLines)
                {
                    if (line.Text.Length > 0) plan.TitleLines.Add(line.Text);
                    if (line.Width > plan.MaxLineWidth) plan.MaxLineWidth = line.Width;
                }
                plan.TitleSize = titleSize;
                plan.TitleLineHeight = titleLineHeight;
                plan.TitleOverflow = titleLines.Count == 0 ? "none" :
                    (reduced ? "title-reduced:" + titleSize.ToString("0", CultureInfo.InvariantCulture) :
                    (titleLines.Count > 1 ? "title-wrapped:" + titleLines.Count : "title-fit"));

                // 2. Wrap the complete body once, then paginate. A page is a substring
                //    of the original text, so every character survives page changes.
                List<JournalLine> lines;
                using (Font bodyFont = type.CreateBodyFont())
                    lines = WrapText(graphics, body, bodyFont, plan.Content.Width, format);
                if (lines == null || lines.Count == 0) return null;
                plan.Lines = lines;
                foreach (JournalLine line in lines)
                    if (line.Width > plan.MaxLineWidth) plan.MaxLineWidth = line.Width;

                float titleHeight = plan.TitleLines.Count == 0 ? 0.0f :
                    plan.TitleLines.Count * titleLineHeight;
                float bodyTop = plan.Content.Top + titleHeight +
                    (plan.TitleLines.Count > 0 ? type.TitleGap : 0.0f);
                float available = plan.Content.Bottom - bodyTop;
                float footerHeight = Math.Max(14.0f, type.BodySize * 0.9f) + 4.0f;
                int perPage = LinesPerPage(available, type.LineHeight);
                if (perPage < 1) return null;
                int pageCount = (lines.Count + perPage - 1) / perPage;
                if (pageCount > 1)
                {
                    available -= footerHeight + 2.0f;
                    perPage = LinesPerPage(available, type.LineHeight);
                    if (perPage < 1) return null;
                    pageCount = (lines.Count + perPage - 1) / perPage;
                    plan.Footer = new RectangleF(plan.Content.Left, plan.Content.Bottom - footerHeight,
                        plan.Content.Width, footerHeight);
                }
                if (pageCount < 1 || pageCount > MaximumPages) return null;
                if (plan.TitleLines.Count > 0)
                    plan.Title = new RectangleF(plan.Content.Left, plan.Content.Top,
                        plan.Content.Width, titleHeight);
                plan.Body = new RectangleF(plan.Content.Left, bodyTop, plan.Content.Width,
                    Math.Max(0.0f, available));
                if (plan.Body.Height < type.LineHeight) return null;
                plan.PageCount = pageCount;
                plan.Pages = new List<string>();
                plan.PageLines = new List<List<string>>();
                for (int page = 0; page < pageCount; page++)
                {
                    int first = page * perPage;
                    int last = Math.Min(lines.Count, first + perPage);
                    List<string> pageLines = new List<string>();
                    for (int i = first; i < last; i++) pageLines.Add(lines[i].Text);
                    plan.PageLines.Add(pageLines);
                    int start = lines[first].Start;
                    int end = last < lines.Count ? lines[last].Start : body.Length;
                    if (end < start) end = start;
                    plan.Pages.Add(body.Substring(start, end - start));
                }
                plan.Overflow = plan.TitleOverflow + ";body=" +
                    (pageCount > 1 ? "pages:" + pageCount : "single-page");
            }
            return plan;
        }

        private static int LinesPerPage(float height, float lineHeight)
        {
            if (lineHeight <= 0.5f || height < lineHeight) return 0;
            int count = (int)Math.Floor((height + 0.01f) / lineHeight);
            return count < 0 ? 0 : count;
        }

        internal static StringFormat TextFormat()
        {
            StringFormat format = new StringFormat(StringFormat.GenericTypographic);
            format.Trimming = StringTrimming.None;
            format.FormatFlags = StringFormatFlags.NoWrap;
            return format;
        }

        internal static float MeasureLineHeight(Graphics graphics, Font font, StringFormat format)
        {
            if (font == null) return 0.0f;
            if (graphics != null)
            {
                SizeF size = graphics.MeasureString("国", font,
                    new SizeF(100000.0f, 100000.0f), format);
                if (size.Height > 0.0f)
                    return Math.Max(font.Size * 1.1f, size.Height);
            }
            return Math.Max(font.Size * 1.25f, font.Size + 3.0f);
        }

        internal static float MeasureWidth(Graphics graphics, string text, Font font,
            StringFormat format)
        {
            if (String.IsNullOrEmpty(text) || font == null) return 0.0f;
            if (graphics == null)
                return text.Length * font.Size * 0.62f;
            SizeF size = graphics.MeasureString(text, font, new PointF(0.0f, 0.0f), format);
            return size.Width;
        }

        private static List<JournalLine> WrapText(Graphics graphics, string text, Font font,
            float width, StringFormat format)
        {
            List<JournalLine> lines = new List<JournalLine>();
            if (String.IsNullOrEmpty(text)) return lines;
            if (width < font.Size) return null;
            int position = 0;
            while (position < text.Length)
            {
                if (text[position] == '\r') { position++; continue; }
                if (text[position] == '\n')
                {
                    lines.Add(new JournalLine { Text = "", Width = 0.0f,
                        Start = position, End = position + 1 });
                    position++;
                    continue;
                }
                int lineStart = position;
                int hardEnd = text.IndexOf('\n', position);
                if (hardEnd < 0) hardEnd = text.Length;
                if (hardEnd == position)
                {
                    lines.Add(new JournalLine { Text = "", Width = 0.0f,
                        Start = position, End = position + 1 });
                    position++;
                    continue;
                }
                // Search over complete UTF-16 code points, not arbitrary code units.
                // The old binary search could decrement itself forever on "A😀" when
                // the midpoint landed on the high surrogate. A boundary list makes
                // every probe a valid substring and every accepted advance >= 1.
                List<int> boundaries = new List<int>();
                boundaries.Add(0);
                for (int scan = lineStart; scan < hardEnd; )
                {
                    scan += (scan + 1 < hardEnd && Char.IsHighSurrogate(text[scan]) &&
                        Char.IsLowSurrogate(text[scan + 1])) ? 2 : 1;
                    boundaries.Add(scan - lineStart);
                }
                int best = 0, low = 1, high = boundaries.Count - 1;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    int length = boundaries[middle];
                    string probe = text.Substring(position, length).TrimEnd();
                    float measured = probe.Length == 0 ? 0.0f :
                        MeasureWidth(graphics, probe, font, format);
                    if (measured <= width - 0.01f)
                    {
                        best = length;
                        low = middle + 1;
                    }
                    else high = middle - 1;
                }
                if (best <= 0) return null;
                int end = position + best;
                // Prefer the last whitespace in the second half of the fitted span;
                // CJK text simply breaks between measured characters.
                for (int i = end - 1; i > position + best / 2; i--)
                    if (Char.IsWhiteSpace(text[i])) { end = i + 1; break; }
                // Never end on a dangling high surrogate.
                if (end > position && Char.IsHighSurrogate(text[end - 1])) end--;
                if (end <= position)
                {
                    // Defensive safety net: consume exactly one complete code point.
                    end = position + 1;
                    if (end < text.Length && Char.IsHighSurrogate(text[position]) &&
                        Char.IsLowSurrogate(text[end])) end++;
                }
                int next = end;
                if (next < text.Length && text[next] == '\n') next++;
                if (next <= position)
                {
                    next = position + 1;
                    if (next < text.Length && Char.IsHighSurrogate(text[position]) &&
                        Char.IsLowSurrogate(text[next])) next++;
                }
                string line = text.Substring(position, end - position).TrimEnd();
                lines.Add(new JournalLine { Text = line,
                    Width = MeasureWidth(graphics, line, font, format),
                    Start = position, End = next });
                position = next;
            }
            return lines;
        }

        // The replacement surface is grown to every confirmed body row and clipped
        // away from partially covered chrome rows. This is what prevents a first line
        // or a "Quest Summary" caption from being half-erased and left as residue.
        internal static Rectangle ExpandReplacementRegion(Rectangle area, IList<OcrLine> lines,
            RectangleF heading, RectangleF owner, Size frame, float scale,
            IList<RectangleF> targetCards)
        {
            if (lines == null || area.Width <= 0 || area.Height <= 0 || scale <= 0.0f) return Rectangle.Empty;
            Rectangle bounds = Rectangle.Intersect(Rectangle.Ceiling(owner), new Rectangle(Point.Empty, frame));
            Rectangle region = Rectangle.Intersect(area, bounds);
            if (region.Width <= 0 || region.Height <= 0) return Rectangle.Empty;
            int padding = Math.Max(1, (int)Math.Ceiling(scale));
            // Shrink first: never keep a sliver of a caption, level gate, counter or
            // footer button inside the replacement rectangle.
            for (int pass = 0; pass < 2; pass++)
            {
                region = SnapRegionToChrome(region, lines, targetCards, scale, padding);
                if (region.Width <= 0 || region.Height <= 0) return Rectangle.Empty;
                // Grow to every confirmed non-chrome row that belongs to this body.
                // A row that lies partly outside is either fully included here or the
                // plan is abandoned - no half-line residue is accepted.
                foreach (OcrLine line in lines)
                {
                    if (line == null) continue;
                    RectangleF raw = GetJournalLineBounds(line);
                    if (raw.IsEmpty) continue;
                    Rectangle row = Rectangle.Ceiling(raw);
                    bool near = raw.IntersectsWith(region) ||
                        region.Contains(new Point((int)(raw.Left + raw.Width / 2),
                            (int)(raw.Top + raw.Height / 2)));
                    if (!near) continue;
                    if (IsJournalChrome(line.Text, raw, targetCards, scale)) continue;
                    row.Inflate(padding, padding);
                    region = Rectangle.Union(region, row);
                    region = Rectangle.Intersect(region, bounds);
                    if (region.Width <= 0 || region.Height <= 0) return Rectangle.Empty;
                }
            }
            // Final snap: growth must not leave a sliver of a chrome row inside.
            region = SnapRegionToChrome(region, lines, targetCards, scale, padding);
            if (region.Width <= 0 || region.Height <= 0) return Rectangle.Empty;
            // Every non-chrome row that touches the region must be fully covered.
            foreach (OcrLine line in lines)
            {
                if (line == null) continue;
                RectangleF raw = GetJournalLineBounds(line);
                if (raw.IsEmpty || IsJournalChrome(line.Text, raw, targetCards, scale)) continue;
                if (!raw.IntersectsWith(region)) continue;
                if (!region.Contains(Rectangle.Ceiling(raw))) return Rectangle.Empty;
            }
            if (!region.Contains(Rectangle.Ceiling(heading))) return Rectangle.Empty;
            if (region.Width < 100 * scale || region.Height < 45 * scale) return Rectangle.Empty;
            return region;
        }

        private static Rectangle SnapRegionToChrome(Rectangle region, IList<OcrLine> lines,
            IList<RectangleF> targetCards, float scale, int padding)
        {
            if (lines == null) return region;
            foreach (OcrLine line in lines)
            {
                if (line == null) continue;
                RectangleF raw = GetJournalLineBounds(line);
                if (raw.IsEmpty || !raw.IntersectsWith(region) ||
                    !IsJournalChrome(line.Text, raw, targetCards, scale)) continue;
                Rectangle row = Rectangle.Ceiling(raw);
                int overlapX = Math.Min(region.Right, row.Right) - Math.Max(region.Left, row.Left);
                int overlapY = Math.Min(region.Bottom, row.Bottom) - Math.Max(region.Top, row.Top);
                // A text row is primarily horizontal: move the top or bottom away.
                // Only a genuinely side-mounted control may trim a vertical edge.
                bool horizontalRow = overlapX >= overlapY;
                if (horizontalRow)
                {
                    if (row.Top <= region.Top + region.Height / 2)
                        region = Rectangle.FromLTRB(region.Left,
                            Math.Max(region.Top, row.Bottom + padding), region.Right, region.Bottom);
                    else
                        region = Rectangle.FromLTRB(region.Left, region.Top,
                            region.Right, Math.Min(region.Bottom, row.Top - padding));
                }
                else
                {
                    if (row.Left <= region.Left + region.Width / 2)
                        region = Rectangle.FromLTRB(Math.Max(region.Left, row.Right + padding),
                            region.Top, region.Right, region.Bottom);
                    else
                        region = Rectangle.FromLTRB(region.Left, region.Top,
                            Math.Min(region.Right, row.Left - padding), region.Bottom);
                }
                if (region.Width <= 0 || region.Height <= 0) return Rectangle.Empty;
            }
            return region;
        }

        private static bool IsJournalChrome(string text, RectangleF bounds,
            IList<RectangleF> targetCards, float scale)
        {
            string key = TranslationStore.Normalize(text);
            if (QuestDetailGeometry.IsSummaryKey(key)) return true;
            if (key.Replace(" ", "").Replace("•", "").Replace(".", "") == "questsummary") return true;
            if (key.StartsWith("level ", StringComparison.Ordinal)) return true;
            if (key == "forfeit" || key == "quest helper" || key == "details" ||
                key == "available" || key == "in progress" || key == "completed") return true;
            return IsCounterLine(text, bounds, targetCards, scale);
        }

        // Objective counters are live game values. A real pair (name + "x / y" or
        // the OCR-confused "x f y") is only a counter when the numeric tokens are
        // genuine tokens: the ambiguous f/t separator requires real digits on both
        // sides, and the pair must end at a word boundary. This prevents prose such
        // as "Please take care of 50" / "Please collect a total of 20" from being
        // treated as a counter just because "o" and "f" look like 0 and "/".
        internal static bool IsCounterLike(string text)
        {
            bool strong, leftDigit, rightDigit, boundary;
            if (!TryParseCounter(text, out strong, out leftDigit, out rightDigit, out boundary))
                return false;
            if (!boundary) return false;
            if (strong) return leftDigit || rightDigit;
            return leftDigit && rightDigit;
        }

        // Target-column-aware form used by the replacement-region geometry. A line
        // inside the measured objective card/list column may accept a strong slash
        // pair even when OCR substituted both digits with confusable letters; the
        // weak f/t form still requires a real digit on both sides. Location never
        // waives the word-boundary requirement.
        internal static bool IsCounterLine(string text, RectangleF bounds,
            IList<RectangleF> targetCards, float scale)
        {
            bool strong, leftDigit, rightDigit, boundary;
            if (!TryParseCounter(text, out strong, out leftDigit, out rightDigit, out boundary))
                return false;
            bool inTarget = false;
            if (targetCards != null && !bounds.IsEmpty)
            {
                PointF center = new PointF(bounds.Left + bounds.Width / 2.0f,
                    bounds.Top + bounds.Height / 2.0f);
                foreach (RectangleF card in targetCards)
                {
                    if (card.IsEmpty) continue;
                    RectangleF hit = card;
                    hit.Inflate(Math.Max(0.0f, scale), Math.Max(0.0f, scale));
                    if (hit.Contains(center)) { inTarget = true; break; }
                }
            }
            if (!boundary) return false;
            if (strong) return inTarget || leftDigit || rightDigit;
            // The f/t slash confusion is too weak to overcome the absence of a
            // real digit on either side, even inside the target column. Otherwise
            // "care of 50" in a misleading column would still be blacklisted.
            return leftDigit && rightDigit;
        }

        // Returns the suffix pair and whether both tokens contain a real ASCII digit.
        internal static bool TryParseCounter(string text, out bool strongSeparator,
            out bool leftRealDigit, out bool rightRealDigit, out bool startsAtWordBoundary)
        {
            strongSeparator = false;
            leftRealDigit = false;
            rightRealDigit = false;
            startsAtWordBoundary = false;
            if (String.IsNullOrEmpty(text)) return false;
            string value = text.TrimEnd();
            Match match = Regex.Match(value,
                @"(?<left>[0-9IlOo]+)[ \t]*(?<sep>[/\\]|[fFtT])[ \t]*(?<right>[0-9IlOo]+)$");
            if (!match.Success) return false;
            string separator = match.Groups["sep"].Value;
            strongSeparator = separator == "/" || separator == "\\";
            leftRealDigit = Regex.IsMatch(match.Groups["left"].Value, "[0-9]");
            rightRealDigit = Regex.IsMatch(match.Groups["right"].Value, "[0-9]");
            int start = match.Groups["left"].Index;
            startsAtWordBoundary = start == 0 || !IsCounterWordChar(value[start - 1]);
            return true;
        }

        private static bool IsCounterWordChar(char value)
        {
            return Char.IsLetterOrDigit(value) || value == '_';
        }

        private static RectangleF GetJournalLineBounds(OcrLine line)
        {
            RectangleF bounds = RectangleF.Empty;
            if (line == null) return bounds;
            if (line.Words != null && line.Words.Count > 0)
            {
                foreach (OcrWord word in line.Words)
                {
                    if (word == null) continue;
                    if (bounds.IsEmpty) bounds = word.BoundingRect;
                    else bounds = RectangleF.Union(bounds, word.BoundingRect);
                }
            }
            if (bounds.IsEmpty) return bounds;
            return bounds;
        }

        // Trace the actual pale description surface from a stage heading. The strip
        // left of the heading provides a continuous vertical boundary independent of
        // missing English rows; objective cards above it are never part of the erase.
        internal static bool Measure(int[] pixels,int width,int height,RectangleF heading,
            RectangleF owner,float scale,out Rectangle area,out Color background)
        {
            area=Rectangle.Empty;background=Color.Empty;
            if(pixels==null || (long)width*height!=pixels.Length || scale<=0 ||
                !OverlayPaintSafety.IsFiniteRect(heading) || !OverlayPaintSafety.IsFiniteRect(owner) ||
                !owner.Contains(heading) || heading.Width<12 || heading.Height<5) return false;
            Rectangle bounds=Rectangle.Intersect(Rectangle.Ceiling(owner),new Rectangle(0,0,width,height));
            Rectangle best=Rectangle.Empty;int bestColor=0;long bestScore=0;
            int margin=Math.Max(5,(int)Math.Ceiling(18*scale));
            // Every plausible margin is considered. A pale frame bevel can be
            // the first light column, but its narrow strip is not the text paper.
            for(int offset=2;offset<=5;offset++) {
                int seedTop=Math.Max(bounds.Top,(int)Math.Floor(heading.Top-offset*scale));
                for(int lane=Math.Max(bounds.Left+2,(int)heading.Left-margin);lane<(int)heading.Left;lane++) {
                    int color=pixels[seedTop*width+lane];if(!Paper(color))continue;
                    int left=lane,right=lane;
                    while(left>bounds.Left&&Similar(pixels[seedTop*width+left-1],color))left--;
                    while(right<bounds.Right&&Similar(pixels[seedTop*width+right],color))right++;
                    left++;right--;
                    if(right-left<100*scale||right<heading.Right)continue;
                    int top=seedTop,bottom=seedTop;
                    while(bottom<bounds.Bottom&&Similar(pixels[bottom*width+lane],color))bottom++;
                    bottom--;
                    while(top>bounds.Top&&PaperRow(pixels,width,top-1,left,right,color))top--;
                    Rectangle candidate=Rectangle.FromLTRB(left,top,right,bottom);
                    if(candidate.Height<45*scale||!candidate.Contains(Rectangle.Ceiling(heading))||(long)candidate.Width*candidate.Height>800000)continue;
                    long score=(long)candidate.Width*candidate.Height;
                    if(score>bestScore){best=candidate;bestColor=color;bestScore=score;}
                }
            }
            if(best.IsEmpty)return false;
            area=best;background=Color.FromArgb(bestColor|unchecked((int)0xff000000));return true;
        }

        private static bool Paper(int c)
        {int r=(c>>16)&255,g=(c>>8)&255,b=c&255;return r>=218&&g>=218&&b>=205&&Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=30;}
        private static bool Similar(int a,int b)
        {return Math.Abs(((a>>16)&255)-((b>>16)&255))<=12 && Math.Abs(((a>>8)&255)-((b>>8)&255))<=12 && Math.Abs((a&255)-(b&255))<=12;}
        private static bool PaperRow(int[] pixels,int width,int y,int left,int right,int color)
        {
            int good=0,total=0;
            for(int x=left+3;x<right-3;x+=2){total++;if(Similar(pixels[y*width+x],color))good++;}
            return total>0&&good*100>=total*78;
        }
    }

    // Compatibility façade for the measured-paper entry point used by the overlay.
    internal static class QuestJournalGeometry
    {
        internal static bool Measure(int[] pixels,int width,int height,RectangleF heading,
            RectangleF owner,float scale,out Rectangle area,out Color background)
        {
            return QuestJournalLayout.Measure(pixels,width,height,heading,owner,scale,
                out area,out background);
        }

        internal static Rectangle ExpandReplacementRegion(Rectangle area,IList<OcrLine> lines,
            RectangleF heading,RectangleF owner,Size frame,float scale,IList<RectangleF> targetCards)
        {
            return QuestJournalLayout.ExpandReplacementRegion(area,lines,heading,owner,frame,scale,targetCards);
        }
    }
}
