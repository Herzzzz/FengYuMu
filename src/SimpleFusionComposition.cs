using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        private Bitmap simpleLayer;
        private Rectangle simpleLayerBounds;
        private int simpleLayerGeneration;
        private readonly HashSet<OverlayLabel> simpleReady = new HashSet<OverlayLabel>();

        private sealed class SimpleDraw : IDisposable
        {
            internal OverlayLabel Label;
            internal ReconstructedTextSurface Surface;
            internal RectangleF Content;
            internal List<Rectangle> Rows;
            internal Font Font;
            internal Font JournalTitleFont;
            internal JournalLayoutPlan JournalLayout;
            internal List<string> JournalPages;
            internal RectangleF JournalFooter;
            public void Dispose() { if (Font != null) Font.Dispose(); if (JournalTitleFont != null) JournalTitleFont.Dispose(); }
        }

        private void ClearSimpleFusion()
        {
            if (simpleLayer != null) simpleLayer.Dispose();
            simpleLayer = null; simpleReady.Clear();
            journalPageView = null; SetJournalPagingShortcuts(false);
        }

        // Returns true when a single immutable composition owns this paint pass.
        // No translation/appearance decisions occur in the window paint callback.
        private bool PaintSimpleFusion(Graphics graphics)
        {
            lastPaintedTranslationCount = 0;
            foreach (OverlayLabel label in labels) label.PaintedLastPass = false;
            if (simpleLayer == null) return false;
            if (simpleLayerGeneration != recognitionGeneration || !ClientRectangle.Contains(simpleLayerBounds)) return true;
            Rectangle game = gameBounds; game.Offset(-Bounds.Left, -Bounds.Top);
            if (!game.Contains(simpleLayerBounds)) return true;
            graphics.DrawImageUnscaled(simpleLayer, simpleLayerBounds.Location);
            foreach (OverlayLabel label in labels) label.PaintedLastPass = simpleReady.Contains(label);
            lastPaintedTranslationCount = simpleReady.Count;
            if (!firstPaintRecorded && simpleReady.Count > 0 && visibleRequestWatch != null)
            {
                firstPaintRecorded = true;
                Program.LogRuntimeEvent("overlay-first-paint", "generation=" + recognitionGeneration + " labels=" + simpleReady.Count + " elapsedMs=" + visibleRequestWatch.ElapsedMilliseconds);
            }
            return true;
        }

        private async Task<bool> PrepareSimpleFusionAsync(Bitmap clean, Rectangle screen, List<OverlayLabel> source)
        {
            int generation = recognitionGeneration;
            if (clean == null || source == null || captureTextRowsOverflow || (long)clean.Width * clean.Height > 16000000) return false;
            Stopwatch clock = Stopwatch.StartNew(), slice = Stopwatch.StartNew();
            int width = clean.Width, height = clean.Height;
            Rectangle extent = new Rectangle(0, 0, width, height);
            int dx = Bounds.Left - screen.Left, dy = Bounds.Top - screen.Top;
            int[] frame = ReadSimplePixels(clean), erased = (int[])frame.Clone();
            List<Rectangle> observed = new List<Rectangle>(), values = new List<Rectangle>();
            foreach (RectangleF raw in captureTextRows)
            { Rectangle row = Rectangle.Ceiling(raw); row.Offset(dx, dy); observed.Add(row); }
            foreach (OverlayLabel label in source)
                if (label != null && OverlayPaintSafety.IsFiniteRect(label.KeepClearBounds) && !label.KeepClearBounds.IsEmpty)
                { Rectangle value = Rectangle.Ceiling(label.KeepClearBounds); value.Offset(dx, dy); values.Add(value); }
            List<SimpleDraw> draws = new List<SimpleDraw>();
            Dictionary<OverlayLabel, string> failures = new Dictionary<OverlayLabel, string>();
            Bitmap staged = null;
            bool committed = false;
            int approximated = 0, work = 0;
            try
            {
                // Longer measured blocks win over a competing substring in the same source.
                List<OverlayLabel> ordered = new List<OverlayLabel>(source);
                ordered.Sort(delegate(OverlayLabel a, OverlayLabel b) {
                    if (a == null) return b == null ? 0 : 1; if (b == null) return -1;
                    if ((a.Journal != null) != (b.Journal != null)) return a.Journal != null ? -1 : 1;
                    int trusted = b.Confidence.CompareTo(a.Confidence);
                    return trusted != 0 ? trusted : ((b.Text ?? "").Length).CompareTo((a.Text ?? "").Length);
                });
                using (Bitmap sample = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                using (Graphics measure = Graphics.FromImage(sample))
                {
                    measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    foreach (OverlayLabel label in ordered)
                    {
                        if (slice.ElapsedMilliseconds >= 50) { await Task.Yield(); slice.Restart(); }
                        if (generation != recognitionGeneration || shuttingDown) return false;
                        string reason = "missing-source-geometry";
                        if (label == null) continue;
                        if (!label.HasSourceBounds || !OverlayPaintSafety.IsFiniteRect(label.SourceBounds) ||
                            !OverlayPaintSafety.IsFiniteRect(label.Bounds) || String.IsNullOrWhiteSpace(label.Text) || label.Text.Length > 4096)
                        { failures[label] = reason; continue; }
                        if (label.Confidence != LabelConfidence.High)
                        { failures[label] = "unverified-dictionary-match"; continue; }
                        if (label.Journal != null)
                        {
                            Rectangle journalArea = Rectangle.Ceiling(label.SourceBounds); journalArea.Offset(dx, dy);
                            SimpleDraw journalDraw;
                            if (TryPrepareJournalDraw(label, journalArea, extent, values, measure, out journalDraw, out reason))
                            {
                                draws.Add(journalDraw);
                                Program.LogRuntimeEvent("replacement-ready", "generation="+generation+" background=journal-paper role=task-journal pages="+journalDraw.JournalPages.Count+" "+(journalDraw.JournalLayout==null?"":journalDraw.JournalLayout.Describe()));
                            }
                            else failures[label] = reason;
                            continue;
                        }
                        RectangleF localSource = label.SourceBounds, localLayout = label.Bounds;
                        localSource.Offset(dx, dy); localLayout.Offset(dx, dy);
                        if (localSource.Left < 0 || localSource.Top < 0 || localSource.Right > width || localSource.Bottom > height ||
                            !OverlayPaintSafety.IsSafeSeamlessGeometry(localLayout, localSource, width, height))
                        { failures[label] = "invalid-source-geometry"; continue; }
                        Rectangle area = Rectangle.Ceiling(label.SourceBounds); area.Offset(dx, dy);
                        if (area.Width < 4 || area.Height < 5 || !extent.Contains(area))
                        { failures[label] = "source-outside-frame"; continue; }
                        List<Rectangle> rows = new List<Rectangle>();
                        bool invalidRows = label.SourceLineBounds != null && label.SourceLineBounds.Count > 40;
                        RectangleF rowEvidence = label.SourceBounds; rowEvidence.Inflate(0.5f, 0.5f);
                        if (label.SourceLineBounds != null) foreach (RectangleF raw in label.SourceLineBounds)
                        {
                            if (!OverlayPaintSafety.IsFiniteRect(raw) || raw.Width < 4 || raw.Height < 5 ||
                                !rowEvidence.Contains(raw)) { invalidRows = true; break; }
                            Rectangle row = Rectangle.Ceiling(raw); row.Offset(dx, dy); row.Intersect(area);
                            if (row.Width >= 4 && row.Height >= 5) rows.Add(row);
                        }
                        if (invalidRows) { failures[label] = "invalid-source-rows"; continue; }
                        if (rows.Count == 0) rows.Add(area);
                        bool blocked = false;
                        foreach (Rectangle row in rows)
                        {
                            Rectangle ink = row; ink.Inflate(1, 1);
                            foreach (Rectangle value in values) if (ink.IntersectsWith(value)) blocked = true;
                            foreach (SimpleDraw previous in draws) foreach (Rectangle used in previous.Rows)
                                if (row.IntersectsWith(used)) blocked = true;
                        }
                        if (blocked) { failures[label] = "source-or-live-value-conflict"; continue; }
                        long cost = (long)(area.Width + 6) * (area.Height + 6);
                        if (cost > 300000 || work + cost > 4000000 || draws.Count >= 256)
                        { failures[label] = "composition-work-budget"; continue; }
                        work += (int)cost;
                        List<Rectangle> guards = FusionRowEvidence.OtherGuards(observed, rows);
                        ReconstructedTextSurface surface;
                        bool smooth = TextSurfaceReconstruction.TryReconstruct(frame, width, height, area, rows, out surface, out reason, guards);
                        if (!smooth && !SimpleBackgroundFill.TryReconstruct(frame, width, height, area, rows, guards, out surface))
                        { failures[label] = "invalid-background-source"; continue; }
                        if(label.MatchSource=="skill-overview-slot")surface.SourceHeight=Math.Min(20,surface.SourceHeight);
                        if(label.MatchSource=="character-field-surface")surface.SourceHeight=Math.Min(18,surface.SourceHeight);
                        RectangleF content = area;
                        List<Rectangle> layoutGuards = new List<Rectangle>(values);
                        foreach (Rectangle row in observed)
                        {
                            // Full OCR lines may contain the measured substring. They are not
                            // neighbouring lines; source geometry and value guards constrain it.
                            bool own = false;
                            foreach (Rectangle owned in rows) if (owned.IntersectsWith(row)) { own = true; break; }
                            if (!own) layoutGuards.Add(row);
                        }
                        foreach (SimpleDraw previous in draws) layoutGuards.Add(Rectangle.Ceiling(previous.Content));
                        Rectangle limit = extent;
                        if (!label.PanelBounds.IsEmpty && OverlayPaintSafety.IsFiniteRect(label.PanelBounds))
                        { limit = Rectangle.Ceiling(label.PanelBounds); limit.Offset(dx, dy); limit.Intersect(extent); }
                        if (!limit.Contains(area)) { failures[label] = "source-outside-panel"; continue; }
                        // Permit only measured-row height and bounded blank space to the right.
                        float right = Math.Min(limit.Right, area.Right + 96);
                        foreach (Rectangle guard in layoutGuards)
                            if (guard.Bottom > area.Top && guard.Top < area.Bottom && guard.Left >= area.Right)
                                right = Math.Min(right, guard.Left - 1);
                        content.Width = Math.Max(area.Width, right - area.Left);
                        if (label.EquipmentField != null && label.EquipmentField.BoundaryEstimated)
                        { failures[label] = "equipment-field-boundary-estimated"; continue; }
                        Font font;
                        if (label.LayoutFontSize > 0.0f)
                            font = FitFixedFont(measure, label.Text, content.Size,
                                label.LayoutFontSize, label.LayoutFontFamily);
                        else if (label.EquipmentField != null)
                            font = FitEquipmentFieldFont(measure, label.EquipmentField, label.Text, content.Size);
                        else
                            font = FitSimpleFont(measure, label.Text, content.Size, surface.SourceHeight,
                                IsQuestPanelRole(label.SemanticRole) ? 14.0f : 8.0f);
                        if (font == null)
                        {
                            failures[label] = label.LayoutFontSize > 0.0f ?
                                "skill-section-does-not-fit" : label.EquipmentField != null ?
                                "equipment-field-does-not-fit" : "complete-text-does-not-fit";
                            continue;
                        }
                        // Shrink the reservation to actual measured text width for single rows.
                        if (rows.Count == 1)
                        {
                            using (StringFormat format = SimpleTextFormat())
                            { SizeF size = measure.MeasureString(label.Text, font, content.Size, format); content.Width = Math.Min(content.Width, size.Width + 1); }
                        }
                        Rectangle reservation = Rectangle.Ceiling(content);
                        foreach (Rectangle value in values) if (reservation.IntersectsWith(value)) blocked = true;
                        foreach (SimpleDraw previous in draws) if (reservation.IntersectsWith(Rectangle.Ceiling(previous.Content))) blocked = true;
                        if (blocked) { font.Dispose(); failures[label] = "translation-layout-conflict"; continue; }
                        draws.Add(new SimpleDraw { Label = label, Surface = surface, Rows = rows, Content = content, Font = font });
                        if (!smooth) approximated++;
                        Program.LogRuntimeEvent("replacement-ready", "generation=" + generation + " background=" + (smooth ? "smooth" : "local-fill") + " role=" + label.SemanticRole);
                    }
                }
                // Phase 1: apply every accepted erasure to the shared original frame.
                // Deliberately do not draw any Chinese until all background writes are done.
                foreach (SimpleDraw draw in draws)
                {
                    ReconstructedTextSurface surface = draw.Surface;
                    for (int y = 0; y < surface.Region.Height; y++)
                    for (int x = 0; x < surface.Region.Width; x++)
                    {
                        int target = (surface.Region.Top + y) * width + surface.Region.Left + x;
                        int pixel = surface.Pixels[y * surface.Region.Width + x];
                        if (pixel != frame[target]) erased[target] = pixel;
                    }
                }
                staged = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                WriteSimplePixels(staged, erased);
                // Phase 2: draw every accepted translation on the same cleaned canvas.
                using (Graphics graphics = Graphics.FromImage(staged))
                {
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    foreach (SimpleDraw draw in draws)
                    using (StringFormat format = SimpleTextFormat())
                    using (SolidBrush ink = new SolidBrush(Color.FromArgb(draw.Surface.Ink)))
                    {
                        if (draw.JournalPages != null)
                            DrawJournalPage(graphics, draw.JournalLayout, 0, draw.Font, draw.JournalTitleFont);
                        else
                        {
                            graphics.SetClip(draw.Content);
                            graphics.DrawString(draw.Label.Text, draw.Font, ink, draw.Content, format);
                            graphics.ResetClip();
                        }
                    }
                }
                int[] composed;
                if (draws.Count > 0)
                {
                    if (FusionBitmapRenderer.MakeTransparent(staged, frame, width, height, extent, out composed) == null) return false;

                }
                else { staged.Dispose(); staged = null; }
                if (generation != recognitionGeneration || shuttingDown) return false;
                ClearSimpleFusion();
                simpleLayer = staged; staged = null; simpleLayerGeneration = generation;
                simpleLayerBounds = new Rectangle(screen.Left - Bounds.Left, screen.Top - Bounds.Top, width, height);
                foreach (SimpleDraw draw in draws)
                {
                    simpleReady.Add(draw.Label);
                    if (draw.JournalPages != null)
                        journalPageView = new JournalPageView {Area=draw.Surface.Region, Content=draw.Content,
                            Footer=draw.JournalFooter, Pages=draw.JournalPages,
                            PageLines=draw.JournalLayout.PageLines, TitleLines=draw.JournalLayout.TitleLines,
                            Title=draw.JournalLayout.Title, FontSize=draw.JournalLayout.Type.BodySize,
                            TitleSize=draw.JournalLayout.TitleSize, FontUnit=draw.JournalLayout.Type.Unit,
                            FontFamily=draw.JournalLayout.Type.Family,
                            Layout=draw.JournalLayout, Background=draw.Label.Journal.Background.IsEmpty?
                                Color.FromArgb(250,248,241):draw.Label.Journal.Background};
                }
                SetJournalPagingShortcuts(journalPageView != null && journalPageView.Pages.Count > 1);
                foreach (OverlayLabel label in source)
                {
                    if (label == null) continue;
                    string failure;
                    label.RenderMode = simpleReady.Contains(label) ? LabelRenderMode.Seamless : LabelRenderMode.PreserveOriginal;
                    label.FusionFailureReason = simpleReady.Contains(label) ? "" : failures.TryGetValue(label, out failure) ? failure : "duplicate-source";
                    if (!simpleReady.Contains(label)) Program.LogRuntimeEvent("replacement-preserved", "generation=" + generation + " role="+label.SemanticRole+" source="+label.MatchSource+" reason=" + label.FusionFailureReason);
                }
                int equipmentPrepared = 0, equipmentPainted = 0, equipmentPreserved = 0;
                string equipmentReasons = "", equipmentPanelSizes = "";
                foreach (OverlayLabel equipmentLabel in source)
                {
                    if (equipmentLabel == null || equipmentLabel.EquipmentField == null) continue;
                    equipmentPrepared++;
                    string panelSize = equipmentLabel.EquipmentField.PanelFontSize.ToString("0.#");
                    if (equipmentPanelSizes.IndexOf(panelSize, StringComparison.Ordinal) < 0)
                        equipmentPanelSizes += (equipmentPanelSizes.Length > 0 ? "," : "") + panelSize;
                    if (simpleReady.Contains(equipmentLabel))
                    {
                        equipmentPainted++;
                        equipmentLabel.EquipmentField.Status = "painted";
                        equipmentLabel.EquipmentField.PreservedReason = "";
                        foreach (SimpleDraw draw in draws)
                            if (draw.Label == equipmentLabel)
                            { equipmentLabel.EquipmentField.PreparedFontSize = draw.Font.Size; break; }
                        if (equipmentLabel.EquipmentPanel != null)
                            equipmentLabel.EquipmentPanel.Painted++;
                    }
                    else
                    {
                        equipmentPreserved++;
                        equipmentLabel.EquipmentField.Status = "preserved";
                        equipmentLabel.EquipmentField.PreservedReason = equipmentLabel.FusionFailureReason;
                        if (equipmentLabel.EquipmentPanel != null)
                            equipmentLabel.EquipmentPanel.Preserved++;
                        string reason = equipmentLabel.FusionFailureReason ?? "";
                        if (reason.Length > 0 && equipmentReasons.IndexOf(reason, StringComparison.Ordinal) < 0)
                        {
                            if (equipmentReasons.Length > 0) equipmentReasons += ",";
                            if (equipmentReasons.Length < 160) equipmentReasons += reason;
                        }
                    }
                }
                if (equipmentPrepared > 0)
                    Program.LogRuntimeEvent("equipment-paint", "generation=" + generation +
                        " prepared=" + equipmentPrepared + " painted=" + equipmentPainted +
                        " preserved=" + equipmentPreserved +
                        " panelSize=" + (equipmentPanelSizes.Length == 0 ? "none" : equipmentPanelSizes) +
                        " preservedReason=" + (equipmentReasons.Length == 0 ? "none" : equipmentReasons));
                HashSet<SkillPanelPlan> skillPanels = new HashSet<SkillPanelPlan>();
                foreach (OverlayLabel skillLabel in source)
                {
                    if (skillLabel == null || skillLabel.SkillSection == null) continue;
                    SkillPanelPlan panel = skillLabel.SkillPanel;
                    if (panel != null) skillPanels.Add(panel);
                    SkillSectionPlan section = skillLabel.SkillSection;
                    if (simpleReady.Contains(skillLabel))
                    {
                        section.Status = "painted";
                        section.PreservedReason = "";
                        if (!String.IsNullOrEmpty(section.HeaderStatus))
                            section.HeaderStatus = "painted";
                        if (section.EffectStatus == "translated")
                            section.EffectStatus = "painted";
                        foreach (SimpleDraw draw in draws)
                            if (draw.Label == skillLabel)
                            {
                                section.PreparedFontSize = draw.Font.Size;
                                section.PaintedFontSize = draw.Font.Size;
                                break;
                            }
                    }
                    else
                    {
                        section.Status = "preserved";
                        section.PreservedReason = skillLabel.FusionFailureReason;
                        if (!String.IsNullOrEmpty(section.HeaderStatus))
                            section.HeaderStatus = "preserved";
                        if (section.EffectStatus == "translated")
                            section.EffectStatus = "preserved";
                        if (panel != null)
                        {
                            panel.Preserved++;
                            panel.AddPreservedReason(section.PreservedReason);
                        }
                    }
                }
                foreach (SkillPanelPlan panel in skillPanels)
                {
                    panel.Prepared = 0;
                    panel.Painted = 0;
                    foreach (SkillSectionPlan section in panel.Sections)
                    {
                        if (section.Label == null) continue;
                        panel.Prepared++;
                        if (section.Status == "painted") panel.Painted++;
                    }
                    Program.LogRuntimeEvent("skill-paint", "generation=" + generation +
                        " " + panel.DescribePaint());
                }
                committed = true;
                return true;
            }
            finally
            {
                if (staged != null) staged.Dispose();
                foreach (SimpleDraw draw in draws) draw.Dispose();
                Program.LogRuntimeEvent("composition-pass", "generation=" + generation + " committed=" + committed + " candidates=" + source.Count + " ready=" + draws.Count + " localFill=" + approximated + " elapsedMs=" + clock.ElapsedMilliseconds);
            }
        }

        private static StringFormat SimpleTextFormat()
        {
            StringFormat format = new StringFormat(StringFormat.GenericTypographic);
            format.Trimming = StringTrimming.None;
            format.FormatFlags &= ~StringFormatFlags.NoWrap;
            return format;
        }

        private static Font FitSimpleFont(Graphics graphics, string text, SizeF area, float sourceHeight)
        {
            return FitSimpleFont(graphics, text, area, sourceHeight, 8.0f);
        }

        // Quest panel prose keeps a shared readable floor. The journal path uses its
        // own 15px+ panel typography; this floor covers the preserved/fragment path
        // so a long task row is never squeezed to the historical 8px fallback. If it
        // cannot fit at the floor the caller preserves the original, not a truncation.
        private static bool IsQuestPanelRole(string role)
        {
            return role == "task-body" || role == "task-journal";
        }

        private static Font FitSimpleFont(Graphics graphics, string text, SizeF area, float sourceHeight,
            float minimumSize)
        {
            if (minimumSize < 8.0f) minimumSize = 8.0f;
            float start = Math.Max(minimumSize, Math.Min(26.0f, sourceHeight));
            using (StringFormat format = SimpleTextFormat())
            for (float size = start; size >= minimumSize; size -= 1)
            {
                Font font = new Font("SimSun", size, FontStyle.Regular, GraphicsUnit.Pixel);
                int fitted, lines;
                graphics.MeasureString(text, font, area, format, out fitted, out lines);
                if (fitted == text.Length) return font;
                font.Dispose();
            }
            return null;
        }

        // Equipment fields use one final panel font size. There is deliberately no
        // per-label shrink loop: a narrow key cannot drag a wide key to a smaller
        // size, and a key that does not fit at the panel size preserves the original.
        // Skill sections share one readable pixel size from the panel plan. There is
        // deliberately no shrink loop: a narrow section preserves its original text
        // instead of becoming an 8px decoration.
        private static Font FitFixedFont(Graphics graphics, string text, SizeF area,
            float size, string family)
        {
            if (size < 11.0f) size = 11.0f;
            if (size > 28.0f) size = 28.0f;
            if (String.IsNullOrEmpty(family)) family = "SimSun";
            Font font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            using (StringFormat format = SimpleTextFormat())
            {
                int fitted, lines;
                graphics.MeasureString(text, font, area, format, out fitted, out lines);
                if (fitted == text.Length) return font;
            }
            font.Dispose();
            return null;
        }

        private static Font FitEquipmentFieldFont(Graphics graphics, EquipmentFieldPlan plan,
            string text, SizeF area)
        {
            EquipmentPanelTypography type = plan == null ? null : plan.Typography;
            if (type == null || String.IsNullOrEmpty(type.Family) ||
                Single.IsNaN(type.FieldSize) || Single.IsInfinity(type.FieldSize))
                return FitSimpleFont(graphics, text, area, 14.0f, 11.0f);
            float size = Math.Max(11.0f, Math.Min(22.0f, type.FieldSize));
            Font font = new Font(type.Family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            using (StringFormat format = SimpleTextFormat())
            {
                int fitted, lines;
                graphics.MeasureString(text, font, area, format, out fitted, out lines);
                if (fitted == text.Length) return font;
            }
            font.Dispose();
            return null;
        }

        private static int[] ReadSimplePixels(Bitmap bitmap)
        {
            int[] pixels = new int[checked(bitmap.Width * bitmap.Height)];
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width, bitmap.Width); }
            finally { bitmap.UnlockBits(data); }
            return pixels;
        }

        private static void WriteSimplePixels(Bitmap bitmap, int[] pixels)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(pixels, y * bitmap.Width, IntPtr.Add(data.Scan0, y * data.Stride), bitmap.Width); }
            finally { bitmap.UnlockBits(data); }
        }
    }
}
