using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal static class PanelFocusPriority
    {
        // Regions must come from the immutable captured frame.
        // The pointer alone never invents a panel or its semantic type.
        internal static Rectangle Select(
            Point pointer,
            Size frame,
            IList<Rectangle> panels,
            IList<Rectangle> tooltips,
            Rectangle excluded)
        {
            if (!new Rectangle(Point.Empty, frame).Contains(pointer) ||
                excluded.Contains(pointer))
                return Rectangle.Empty;

            Rectangle best = Rectangle.Empty;
            long bestDistance = Int64.MaxValue;
            long bestArea = Int64.MaxValue;

            if (tooltips != null)
            {
                foreach (Rectangle region in tooltips)
                {
                    if (!Valid(region, frame)) continue;

                    long dx = Math.Max(
                        region.Left - pointer.X,
                        Math.Max(0, pointer.X - region.Right));

                    long dy = Math.Max(
                        region.Top - pointer.Y,
                        Math.Max(0, pointer.Y - region.Bottom));

                    long distance = dx * dx + dy * dy;
                    long area = (long)region.Width * region.Height;

                    if (distance > 96 * 96 ||
                        distance > bestDistance ||
                        (distance == bestDistance && area >= bestArea))
                        continue;

                    best = region;
                    bestDistance = distance;
                    bestArea = area;
                }
            }

            if (!best.IsEmpty) return best;

            if (panels != null)
            {
                foreach (Rectangle region in panels)
                {
                    if (!Valid(region, frame) ||
                        !region.Contains(pointer))
                        continue;

                    long area = (long)region.Width * region.Height;
                    if (area >= bestArea) continue;

                    best = region;
                    bestArea = area;
                }
            }

            return best;
        }

        internal static List<T> Order<T>(
            IList<T> items,
            Func<T, RectangleF> bounds,
            RectangleF focus)
        {
            List<T> result = new List<T>();

            if (items == null) return result;

            if (focus.IsEmpty)
            {
                result.AddRange(items);
                return result;
            }

            // Stable partition. Do not lower resolution or confidence.
            for (int rank = 2; rank >= 0; rank--)
            {
                foreach (T item in items)
                {
                    RectangleF box = bounds(item);

                    int actual = focus.Contains(box)
                        ? 2
                        : focus.IntersectsWith(box) ? 1 : 0;

                    if (actual == rank) result.Add(item);
                }
            }

            return result;
        }

        private static bool Valid(Rectangle r, Size frame)
        {
            return r.Width >= 160 &&
                r.Height >= 70 &&
                new Rectangle(Point.Empty, frame).Contains(r) &&
                (long)r.Width * r.Height * 10 <
                    (long)frame.Width * frame.Height * 9;
        }
    }
}
