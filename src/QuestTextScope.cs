using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    // Bind selected quest prose to an observed panel, independently of whether
    // OCR happened to read every tab and the Forfeit button. Never use a guessed
    // percentage of the entire screen to authorize a task-specific translation.
    internal static class QuestTextScope
    {
        internal static RectangleF Measure(IList<PanelTextAnchor> anchors,
            IList<RectangleF> panels, float scale, Size frame)
        {
            RectangleF empty = RectangleF.Empty;
            if (anchors == null || panels == null || scale <= 0 || Single.IsNaN(scale) ||
                Single.IsInfinity(scale) || frame.Width <= 0 || frame.Height <= 0) return empty;
            RectangleF summary = empty;
            foreach (PanelTextAnchor anchor in anchors)
            {
                if (anchor == null || !QuestDetailGeometry.IsSummaryKey(anchor.Key) || !Valid(anchor.Bounds)) continue;
                if (!summary.IsEmpty && summary != anchor.Bounds) return empty;
                summary = anchor.Bounds;
            }
            if (summary.IsEmpty) return empty;
            RectangleF extent = new RectangleF(Point.Empty, frame), owner = empty;
            float best = Single.MaxValue;
            foreach (RectangleF panel in panels)
            {
                if (!Valid(panel) || !extent.Contains(panel) || !panel.Contains(summary)) continue;
                if (panel.Width * panel.Height >= extent.Width * extent.Height * 0.85f) continue;
                // A panel must provide observed space below its heading for real prose.
                if (panel.Bottom - summary.Bottom < 24 * scale || panel.Right - summary.Left < 80 * scale) continue;
                float area = panel.Width * panel.Height;
                if (area < best) { owner = panel; best = area; }
            }
            if (owner.IsEmpty) return empty;
            float left = Math.Max(owner.Left + 2 * scale, summary.Left - 24 * scale);
            float top = summary.Top, bottom = owner.Bottom - 2 * scale;
            foreach (PanelTextAnchor anchor in anchors)
            {
                if (anchor == null || anchor.Key != "forfeit" || !Valid(anchor.Bounds) ||
                    !owner.Contains(anchor.Bounds) || anchor.Bounds.Top <= summary.Bottom || anchor.Bounds.Left < left) continue;
                bottom = Math.Min(bottom, anchor.Bounds.Top - 2 * scale);
            }
            RectangleF detail = RectangleF.FromLTRB(left, top, owner.Right - 2 * scale, bottom);
            return Valid(detail) && detail.Contains(summary) && detail.Height >= 30 * scale ? detail : empty;
        }

        private static bool Valid(RectangleF r)
        {
            return !(Single.IsNaN(r.X) || Single.IsNaN(r.Y) || Single.IsNaN(r.Width) || Single.IsNaN(r.Height) ||
                Single.IsInfinity(r.X) || Single.IsInfinity(r.Y) || Single.IsInfinity(r.Width) || Single.IsInfinity(r.Height)) && r.Width > 0 && r.Height > 0;
        }
    }
}
