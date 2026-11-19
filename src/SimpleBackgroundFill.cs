using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    // A local appearance fallback. It never changes the translation or invents text.
    // Only the measured source rows are filled; gaps, icons and counters stay live.
    internal static class SimpleBackgroundFill
    {
        internal static bool TryReconstruct(int[] frame, int width, int height,
            Rectangle source, IList<Rectangle> rows, IList<Rectangle> guards,
            out ReconstructedTextSurface result)
        {
            result = null;
            Rectangle extent = new Rectangle(0, 0, width, height);
            if (frame == null || width <= 0 || height <= 0 || (long)width * height != frame.Length ||
                source.Width < 4 || source.Height < 5 || !extent.Contains(source) ||
                (long)source.Width * source.Height > 300000 || rows == null || rows.Count == 0 || rows.Count > 40)
                return false;
            Rectangle region = source; region.Inflate(1, 1); region.Intersect(extent);
            int[] output = new int[region.Width * region.Height];
            for (int y = 0; y < region.Height; y++)
                Array.Copy(frame, (region.Top + y) * width + region.Left, output, y * region.Width, region.Width);
            List<int> heights = new List<int>();
            long inkR = 0, inkG = 0, inkB = 0; int inkCount = 0, changed = 0;
            foreach (Rectangle raw in rows)
            {
                Rectangle row = Rectangle.Intersect(raw, source);
                if (row.Width < 4 || row.Height < 5 || row.Height > 96) return false;
                heights.Add(row.Height);
                Rectangle area = row; area.Inflate(1, 1); area.Intersect(region);
                Rectangle ring = row; ring.Inflate(5, 4); ring.Intersect(extent);
                int[] bins = new int[512]; long[] rr = new long[512], gg = new long[512], bb = new long[512];
                for (int y = ring.Top; y < ring.Bottom; y++)
                for (int x = ring.Left; x < ring.Right; x++)
                {
                    if (area.Contains(x, y) || Blocked(x, y, rows) || Blocked(x, y, guards)) continue;
                    Add(frame[y * width + x], bins, rr, gg, bb);
                }
                int best = Best(bins);
                // At a screen edge or between dense rows there may be no clean ring.
                // The dominant local colour is an explicit approximation, not texture proof.
                if (bins[best] < 4)
                {
                    Array.Clear(bins, 0, bins.Length); Array.Clear(rr, 0, rr.Length);
                    Array.Clear(gg, 0, gg.Length); Array.Clear(bb, 0, bb.Length);
                    for (int y = row.Top; y < row.Bottom; y++)
                    for (int x = row.Left; x < row.Right; x++) Add(frame[y * width + x], bins, rr, gg, bb);
                    best = Best(bins);
                }
                if (bins[best] == 0) return false;
                int background = unchecked((int)0xff000000) | ((int)(rr[best] / bins[best]) << 16) |
                    ((int)(gg[best] / bins[best]) << 8) | (int)(bb[best] / bins[best]);
                int strongest = 0, ink = background;
                for (int y = row.Top; y < row.Bottom; y++)
                for (int x = row.Left; x < row.Right; x++)
                {
                    int pixel = frame[y * width + x], d = Distance(pixel, background);
                    if (d > strongest) { strongest = d; ink = pixel; }
                }
                inkR += (ink >> 16) & 255; inkG += (ink >> 8) & 255; inkB += ink & 255; inkCount++;
                for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int index = (y - region.Top) * region.Width + x - region.Left;
                    if (output[index] != background) changed++;
                    output[index] = background;
                }
            }
            heights.Sort();
            result = new ReconstructedTextSurface { Region = region, Pixels = output,
                Ink = unchecked((int)0xff000000) | ((int)(inkR / inkCount) << 16) |
                    ((int)(inkG / inkCount) << 8) | (int)(inkB / inkCount),
                MaskedPixels = changed, SourceHeight = heights[heights.Count / 2] };
            return true;
        }

        private static bool Blocked(int x, int y, IList<Rectangle> guards)
        { if (guards != null) foreach (Rectangle guard in guards) if (guard.Contains(x, y)) return true; return false; }
        private static int Distance(int a, int b)
        { return Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255)); }
        private static void Add(int p, int[] bins, long[] r, long[] g, long[] b)
        { int i = ((p >> 21) & 7) * 64 + ((p >> 13) & 7) * 8 + ((p >> 5) & 7); bins[i]++; r[i] += (p >> 16) & 255; g[i] += (p >> 8) & 255; b[i] += p & 255; }
        private static int Best(int[] bins)
        { int best = 0; for (int i = 1; i < bins.Length; i++) if (bins[i] > bins[best]) best = i; return best; }
    }
}
