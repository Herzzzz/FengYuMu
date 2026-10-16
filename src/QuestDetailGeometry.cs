using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal sealed class PanelTextAnchor
    {
        internal string Key;
        internal RectangleF Bounds;
    }

    internal static class QuestDetailGeometry
    {
        internal static bool IsSummaryKey(string key)
        {
            return key == "quest summary" ||
                key == "questsummary";
        }

        internal static Rectangle LinkedWindow(
            IList<PanelTextAnchor> anchors,
            float scale,
            Size frame,
            Point pointer)
        {
            RectangleF detail = Measure(anchors, scale, frame);
            if (detail.IsEmpty) return Rectangle.Empty;

            float left = detail.Left;
            float bottom = detail.Bottom;

            foreach (PanelTextAnchor anchor in anchors)
            {
                if (anchor == null || !Valid(anchor.Bounds)) continue;

                if (anchor.Key == "available" ||
                    anchor.Key == "in progress" ||
                    anchor.Key == "completed")
                {
                    left = Math.Min(
                        left,
                        anchor.Bounds.Left - 12 * scale);
                }

                if (anchor.Key == "forfeit")
                {
                    bottom = Math.Max(
                        bottom,
                        anchor.Bounds.Bottom + 8 * scale);
                }
            }

            Rectangle window = Rectangle.Intersect(
                new Rectangle(Point.Empty, frame),
                Rectangle.Ceiling(RectangleF.FromLTRB(
                    left,
                    detail.Top - 8 * scale,
                    detail.Right,
                    bottom)));

            return window.Contains(pointer)
                ? window
                : Rectangle.Empty;
        }

        internal static RectangleF Measure(
            IList<PanelTextAnchor> anchors,
            float scale,
            Size frame)
        {
            if (anchors == null ||
                Single.IsNaN(scale) ||
                Single.IsInfinity(scale) ||
                scale <= 0 ||
                frame.Width <= 0 ||
                frame.Height <= 0)
                return RectangleF.Empty;

            Dictionary<string, RectangleF> tabs =
                new Dictionary<string, RectangleF>(
                    StringComparer.Ordinal);

            RectangleF summary = RectangleF.Empty;
            RectangleF footer = RectangleF.Empty;

            foreach (PanelTextAnchor anchor in anchors)
            {
                if (anchor == null || !Valid(anchor.Bounds)) continue;

                string key = anchor.Key ?? "";

                if (key == "available" ||
                    key == "in progress" ||
                    key == "completed")
                {
                    RectangleF previous;

                    if (tabs.TryGetValue(key, out previous) &&
                        previous != anchor.Bounds)
                        return RectangleF.Empty;

                    tabs[key] = anchor.Bounds;
                }
                else if (IsSummaryKey(key))
                {
                    if (!summary.IsEmpty && summary != anchor.Bounds)
                        return RectangleF.Empty;

                    summary = anchor.Bounds;
                }
                else if (key == "forfeit")
                {
                    if (!footer.IsEmpty && footer != anchor.Bounds)
                        return RectangleF.Empty;

                    footer = anchor.Bounds;
                }
            }

            if (tabs.Count < 2 || summary.IsEmpty || footer.IsEmpty)
                return RectangleF.Empty;

            RectangleF tabBand = RectangleF.Empty;

            foreach (RectangleF tab in tabs.Values)
            {
                if (!tabBand.IsEmpty &&
                    Math.Abs(tab.Top - tabBand.Top) >
                        Math.Max(tab.Height, tabBand.Height))
                    return RectangleF.Empty;

                tabBand = tabBand.IsEmpty
                    ? tab
                    : RectangleF.Union(tabBand, tab);
            }

            float left = tabBand.Right + 12 * scale;

            if (summary.Left < left ||
                summary.Top <= tabBand.Bottom ||
                footer.Left < summary.Left ||
                footer.Top <= summary.Bottom)
                return RectangleF.Empty;

            RectangleF measured = RectangleF.FromLTRB(
                left,
                tabBand.Top,
                Math.Min(frame.Width, footer.Right + 12 * scale),
                footer.Top - 8 * scale);

            RectangleF capture =
                new RectangleF(0, 0, frame.Width, frame.Height);

            if (!Valid(measured) ||
                !capture.Contains(measured) ||
                !measured.Contains(summary))
                return RectangleF.Empty;

            return measured;
        }

        private static bool Valid(RectangleF rectangle)
        {
            return Finite(rectangle.Left) &&
                Finite(rectangle.Top) &&
                Finite(rectangle.Right) &&
                Finite(rectangle.Bottom) &&
                rectangle.Width > 0 &&
                rectangle.Height > 0;
        }

        private static bool Finite(float value)
        {
            return !Single.IsNaN(value) &&
                !Single.IsInfinity(value);
        }
    }
}
