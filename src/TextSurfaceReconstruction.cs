using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal sealed class ReconstructedTextSurface
    {
        internal Rectangle Region;
        internal int[] Pixels;
        internal int Ink;
        internal int MaskedPixels;
        internal float SourceHeight;
    }

    // Local reconstruction from the immutable, clean game capture. No image/API service.
    // Only smooth, vertically supported text rows qualify; uncertain texture preserves the live original.
    internal static class TextSurfaceReconstruction
    {
        internal static bool TryReconstruct(int[] frame, int width, int height,
            Rectangle source, IList<Rectangle> rows, out ReconstructedTextSurface result, out string reason,
            IList<Rectangle> protectedText = null)
        {
            result = null; reason = "invalid-source";
            if (frame == null || width <= 0 || height <= 0 || (long)width * height != frame.Length ||
                source.Width < 4 || source.Height < 5 || rows == null || rows.Count == 0 || rows.Count > 40)
                return false;
            if (source.X < 3 || source.Y < 3 || (long)source.X + source.Width > width - 3 ||
                (long)source.Y + source.Height > height - 3) return false;
            Rectangle region = source; region.Inflate(3, 3);
            if (region.Left < 0 || region.Top < 0 || region.Right > width || region.Bottom > height ||
                (long)region.Width * region.Height > 300000) return false;
            int[] pixels = new int[region.Width * region.Height];
            for (int y = 0; y < region.Height; y++)
                Array.Copy(frame, (region.Top + y) * width + region.Left, pixels, y * region.Width, region.Width);
            for (int i = 0; i < pixels.Length; i++) pixels[i] |= unchecked((int)0xFF000000);
            bool[] mask = new bool[pixels.Length];
            int[] repair = new int[pixels.Length];
            int[] inkBins = new int[512]; long[] inkR = new long[512], inkG = new long[512], inkB = new long[512];
            long[] inkContrast = new long[512];
            List<int> heights = new List<int>();
            foreach (Rectangle original in rows)
            {
                Rectangle row = Rectangle.Intersect(original, source);
                if (row.Width < 4 || row.Height < 5 || row.Height > 96) { reason = "invalid-row"; return false; }
                heights.Add(row.Height);
                // Include the 1px outline/antialias fringe around OCR's ink bounds.
                Rectangle inkArea = row; inkArea.Inflate(1, 1);
                int[] top, bottom; int topY, bottomY;
                // OCR bounds can end one pixel from a neighboring text row. Find
                // corroborated clean support, never use another OCR row as background.
                if (!FindSupport(frame, width, height, row, rows, protectedText,
                    out top, out bottom, out topY, out bottomY))
                { reason = "unsupported-texture"; return false; }
                int changed = 0;
                for (int y = inkArea.Top; y < inkArea.Bottom; y++)
                for (int x = inkArea.Left; x < inkArea.Right; x++)
                {
                    int estimated = Lerp(top[x - inkArea.Left], bottom[x - inkArea.Left],
                        y - topY, bottomY - topY);
                    int index = (y - region.Top) * region.Width + x - region.Left;
                    repair[index] = estimated;
                    int originalPixel = pixels[index];
                    if (Distance(originalPixel, estimated) < 90) continue;
                    mask[index] = true; changed++;
                    int bin = ((originalPixel >> 21) & 7) * 64 + ((originalPixel >> 13) & 7) * 8 + ((originalPixel >> 5) & 7);
                    inkBins[bin]++; inkR[bin] += (originalPixel >> 16) & 255;
                    inkContrast[bin] += Distance(originalPixel, estimated);
                    inkG[bin] += (originalPixel >> 8) & 255; inkB[bin] += originalPixel & 255;
                }
                int area = inkArea.Width * inkArea.Height;
                if (changed < 2 || changed * 100 > area * 45)
                { reason = changed < 2 ? "no-supported-ink" : "ambiguous-foreground"; return false; }
            }
            // Dilate only inside the measured row; never paint neighboring counters or icons.
            bool[] expanded = (bool[])mask.Clone();
            foreach (Rectangle row in rows)
            {
                Rectangle allowed = Rectangle.Intersect(row, source); allowed.Inflate(1, 1);
                for (int y = allowed.Top; y < allowed.Bottom; y++)
                for (int x = allowed.Left; x < allowed.Right; x++)
                {
                    int index = (y - region.Top) * region.Width + x - region.Left;
                    if (!mask[index]) continue;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        if (allowed.Contains(x + dx, y + dy)) expanded[index + dy * region.Width + dx] = true;
                }
            }
            int masked = 0;
            for (int i = 0; i < pixels.Length; i++) if (expanded[i]) { pixels[i] = repair[i]; masked++; }
            int best = 0, modal = 0;
            for (int i = 1; i < inkBins.Length; i++) if (inkBins[i] > inkBins[modal]) modal = i;
            double bestScore = -1;
            for (int i = 0; i < inkBins.Length; i++)
            {
                // Repeated opaque strokes carry more contrast evidence than a
                // populous antialias fringe. Ignore isolated high-contrast specks.
                if (inkBins[i] < Math.Max(2, inkBins[modal] / 12)) continue;
                double score = (double)inkContrast[i] * inkContrast[i] / inkBins[i];
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (inkBins[best] == 0) { reason = "no-supported-ink"; return false; }
            int count = inkBins[best]; heights.Sort();
            result = new ReconstructedTextSurface { Region = region, Pixels = pixels,
                Ink = unchecked((int)0xFF000000) | ((int)(inkR[best] / count) << 16) | ((int)(inkG[best] / count) << 8) | (int)(inkB[best] / count),
                MaskedPixels = masked, SourceHeight = heights[heights.Count / 2] };
            reason = ""; return true;
        }
        private static bool FindSupport(int[] frame, int width, int height, Rectangle row,
            IList<Rectangle> rows, IList<Rectangle> protectedText,
            out int[] top, out int[] bottom, out int topY, out int bottomY)
        {
            top = null; bottom = null; topY = bottomY = 0;
            int left = row.Left - 1, count = row.Width + 2;
            long best = Int64.MaxValue;
            for (int above = 2; above <= 8; above++)
            for (int below = 1; below <= 7; below++)
            {
                int aY = row.Top - above, bY = row.Bottom + below;
                if (aY < 1 || bY + 1 >= height ||
                    Blocked(new Rectangle(left, aY - 1, count, 2), row, rows, protectedText) ||
                    Blocked(new Rectangle(left, bY, count, 2), row, rows, protectedText)) continue;
                int[] a = new int[count], b = new int[count];
                long difference = 0, roughness = 0, agreement = 0;
                for (int x = 0; x < count; x++)
                {
                    int a0 = frame[(aY - 1) * width + left + x], a1 = frame[aY * width + left + x];
                    int b0 = frame[bY * width + left + x], b1 = frame[(bY + 1) * width + left + x];
                    a[x] = Mean(a0, a1); b[x] = Mean(b0, b1);
                    agreement += Distance(a0, a1) + Distance(b0, b1);
                    difference += Distance(a[x], b[x]);
                    if (x > 0) roughness += Distance(a[x], a[x-1]) + Distance(b[x], b[x-1]);
                }
                // Original 60/18 thresholds remain; corroboration adds a stricter
                // adjacent-scanline check. Prefer nearby equally supported samples.
                if (difference > count * 60L || roughness > count * 18L || agreement > count * 18L) continue;
                bool gapSupported = true;
                for (int y = aY + 1; y < bY && gapSupported; y++)
                {
                    if (y >= row.Top - 1 && y <= row.Bottom) continue;
                    if (Blocked(new Rectangle(left, y, count, 1), row, rows, protectedText)) continue;
                    long error = 0;
                    for (int x = 0; x < count; x++)
                        error += Distance(frame[y * width + left + x], Lerp(a[x], b[x], y-aY, bY-aY));
                    if (error > count * 60L) gapSupported = false;
                }
                if (!gapSupported) continue;
                // A clean original support pair needs no expanded search. Most
                // panel rows take this path; only contaminated/blocked pairs pay
                // for the bounded alternatives, with the same validation gates.
                if (above == 2 && below == 1)
                { top = a; bottom = b; topY = aY; bottomY = bY; return true; }
                long score = roughness + agreement + difference + (above + below) * (long)count;
                if (score >= best) continue;
                best = score; top = a; bottom = b; topY = aY; bottomY = bY;
            }
            return top != null;
        }
        private static bool Blocked(Rectangle band, Rectangle current, IList<Rectangle> rows, IList<Rectangle> extra)
        {
            foreach (Rectangle text in rows)
                if (text != current && text.IntersectsWith(band)) return true;
            if (extra != null) foreach (Rectangle text in extra)
            {
                // Other OCR passes may measure this same row slightly differently.
                // Their bounds still protect samples; the current row itself lies
                // strictly between the candidate support bands.
                Rectangle guard = text; guard.Inflate(1, 1);
                if (guard.IntersectsWith(band)) return true;
            }
            return false;
        }
        private static int Distance(int a, int b)
        { return Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255)); }
        private static int Mean(int a, int b) { return Lerp(a, b, 1, 2); }
        private static int Lerp(int a, int b, int n, int d)
        {
            n = Math.Max(0, Math.Min(d, n));
            int r = ((((a >> 16) & 255) * (d - n) + ((b >> 16) & 255) * n) + d / 2) / d;
            int g = ((((a >> 8) & 255) * (d - n) + ((b >> 8) & 255) * n) + d / 2) / d;
            int blue = (((a & 255) * (d - n) + (b & 255) * n) + d / 2) / d;
            return unchecked((int)0xFF000000) | r << 16 | g << 8 | blue;
        }
    }
}
