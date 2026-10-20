using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal enum ChatBandKind
    {
        None = 0,
        Colored = 1,
        PinkBroadcast = 2,
        BlueBroadcast = 3,
        YellowBroadcast = 4
    }

    internal sealed class ChatColorSample
    {
        internal Color Foreground = Color.Empty;
        internal Color Background = Color.Empty;
        internal Color Ink = Color.Empty;
        internal ChatBandKind Band = ChatBandKind.None;
        internal bool InkMatchesBand;
        internal bool Boundary;
        internal double Coverage;
        internal double RowCoverage;
        internal int InkPixels;
        internal bool HasBand { get { return Band != ChatBandKind.None; } }
        internal bool IsBroadcastBand
        {
            get { return Band == ChatBandKind.PinkBroadcast || Band == ChatBandKind.BlueBroadcast ||
                    Band == ChatBandKind.YellowBroadcast; }
        }
    }

    internal static class ChatColorEvidence
    {
        private sealed class Cluster
        {
            internal long R, G, B;
            internal int Count;
            internal Color Average { get { return Count <= 0 ? Color.Empty : Color.FromArgb((int)(R / Count), (int)(G / Count), (int)(B / Count)); } }
            internal void Add(int r, int g, int b) { R += r; G += g; B += b; Count++; }
        }

        private static int Distance(int pixel, Color color)
        {
            return Math.Abs(((pixel >> 16) & 255) - color.R) +
                Math.Abs(((pixel >> 8) & 255) - color.G) +
                Math.Abs((pixel & 255) - color.B);
        }

        private static int Distance(Color left, Color right)
        {
            return Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);
        }

        private static int Luminance(Color color)
        {
            return (int)Math.Round(color.R * 0.299 + color.G * 0.587 + color.B * 0.114);
        }

        private static int Luminance(int pixel)
        {
            int r = (pixel >> 16) & 255, g = (pixel >> 8) & 255, b = pixel & 255;
            return (int)Math.Round(r * 0.299 + g * 0.587 + b * 0.114);
        }

        private static int Chroma(Color color)
        {
            return Math.Max(color.R, Math.Max(color.G, color.B)) -
                Math.Min(color.R, Math.Min(color.G, color.B));
        }

        private static Color Mode(int[] pixels, int width, Rectangle area, out double coverage)
        {
            Dictionary<int, Cluster> groups = new Dictionary<int, Cluster>();
            int total = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int value = pixels[y * width + x];
                    int key = ((value >> 20) & 15) * 256 + ((value >> 12) & 15) * 16 + ((value >> 4) & 15);
                    Cluster group;
                    if (!groups.TryGetValue(key, out group)) { group = new Cluster(); groups.Add(key, group); }
                    group.Add((value >> 16) & 255, (value >> 8) & 255, value & 255);
                    total++;
                }
            Cluster best = null;
            foreach (Cluster group in groups.Values) if (best == null || group.Count > best.Count) best = group;
            if (best == null || total == 0) { coverage = 0; return Color.Empty; }
            // Scene texture and translucent capture scale split one band across adjacent
            // quantisation buckets. Merge every pixel close to the winning bucket and
            // average again until the centre is stable.
            Color center = best.Average;
            for (int pass = 0; pass < 3; pass++)
            {
                long r = 0, g = 0, b = 0; int count = 0;
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        int value = pixels[y * width + x];
                        if (Distance(value, center) > 46) continue;
                        r += (value >> 16) & 255; g += (value >> 8) & 255; b += value & 255; count++;
                    }
                if (count == 0) break;
                Color next = Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
                bool stable = Distance(next, center) <= 3;
                center = next;
                if (stable) break;
            }
            int matching = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (Distance(pixels[y * width + x], center) <= 52) matching++;
            coverage = (double)matching / total;
            return center;
        }

        private static bool IsPinkFamily(Color color)
        {
            return color.R - color.G >= 24 && color.R - color.B >= 5 &&
                color.B >= color.G - 4 && color.R >= 120;
        }

        private static bool IsBlueFamily(Color color)
        {
            return color.B - color.R >= 20 && color.B >= color.G - 4 &&
                color.G - color.R >= 3 && color.B >= 105;
        }

        private static bool IsYellowFamily(Color color)
        {
            return color.R >= 145 && color.G >= 115 && color.R >= color.G - 12 &&
                color.R - color.G <= 105 && Math.Min(color.R, color.G) - color.B >= 50;
        }

        private static bool IsYellowInk(Color ink, Color band)
        {
            bool warm = ink.R >= ink.G - 8 && ink.G - ink.B >= 10;
            bool neutralDark = Chroma(ink) < 18 && Math.Max(ink.R, Math.Max(ink.G, ink.B)) <= 105;
            return (warm || neutralDark) && Luminance(ink) <= Luminance(band) - 25;
        }

        private static bool IsPinkInk(Color ink, Color band)
        {
            return ink.R - ink.G >= 22 && ink.B - ink.G >= 4 &&
                ink.R - ink.B >= 4 && Luminance(ink) <= Luminance(band) - 18 &&
                Chroma(ink) >= 18;
        }

        private static bool IsBlueInk(Color ink, Color band)
        {
            return ink.B - ink.R >= 18 && ink.B - ink.G >= 3 &&
                ink.G - ink.R >= 3 && Luminance(ink) <= Luminance(band) - 10 &&
                Chroma(ink) >= 18;
        }

        private static double BandRowFraction(int[] pixels, int width, Rectangle strip, int y,
            Color background, int tolerance, out int count)
        {
            count = 0;
            if (y < 0) return 0;
            int matching = 0, total = 0;
            for (int x = strip.Left; x < strip.Right; x++)
            {
                total++;
                if (Distance(pixels[y * width + x], background) <= tolerance) matching++;
            }
            count = total;
            return total == 0 ? 0 : (double)matching / total;
        }

        private static bool HasVerticalBoundary(int[] pixels, int width, int height,
            Rectangle strip, Rectangle text, Color background)
        {
            int before = -1;
            for (int delta = 1; delta <= 8; delta++)
            {
                int y = text.Top - delta;
                if (y < 0) break;
                int count;
                double fraction = BandRowFraction(pixels, width, strip, y, background, 56, out count);
                if (fraction < 0.38) { before = delta; break; }
            }
            if (before >= 1 && before <= 4) return true;
            for (int delta = 1; delta <= 8; delta++)
            {
                int y = text.Bottom + delta - 1;
                if (y >= height) break;
                int count;
                double fraction = BandRowFraction(pixels, width, strip, y, background, 56, out count);
                if (fraction < 0.38) return delta <= 4;
            }
            return false;
        }

        private static void AddCluster(Dictionary<int, Cluster> clusters, int pixel, int quantize)
        {
            int r = (pixel >> 16) & 255, g = (pixel >> 8) & 255, b = pixel & 255;
            int key = (r / quantize) * 4096 + (g / quantize) * 64 + b / quantize;
            Cluster group;
            if (!clusters.TryGetValue(key, out group)) { group = new Cluster(); clusters.Add(key, group); }
            group.Add(r, g, b);
        }

        private static Cluster BestCluster(Dictionary<int, Cluster> clusters)
        {
            Cluster best = null;
            foreach (Cluster group in clusters.Values) if (best == null || group.Count > best.Count) best = group;
            return best;
        }

        internal static void Sample(int[] pixels, int width, int height, Rectangle text,
            IList<RectangleF> words, out Color foreground, out Color background, out bool band)
        {
            ChatColorSample sample = Analyze(pixels, width, height, text, words);
            foreground = sample.Foreground;
            background = sample.Background;
            band = sample.IsBroadcastBand;
        }

        internal static ChatColorSample Analyze(int[] pixels, int width, int height,
            Rectangle text, IList<RectangleF> words)
        {
            ChatColorSample result = new ChatColorSample();
            if (pixels == null || width < 1 || height < 1 || (long)width * height != pixels.Length) return result;
            Rectangle frame = new Rectangle(0, 0, width, height);
            text.Intersect(frame);
            if (text.Width < 2 || text.Height < 2) return result;

            int side = Math.Max(18, Math.Min(96, text.Height * 2));
            Rectangle strip = Rectangle.FromLTRB(Math.Max(0, text.Left - side), text.Top,
                Math.Min(width, text.Right + side), text.Bottom);
            if (strip.Width < 2 || strip.Height < 2) return result;

            double coverage;
            Color background = Mode(pixels, width, strip, out coverage);
            if (background.IsEmpty) return result;
            result.Background = background;
            result.Coverage = coverage;

            // Per-row coverage guards against a scene that merely contains the band
            // colour in one saturated patch. The text row itself must be covered.
            List<double> rows = new List<double>();
            int goodRows = 0;
            for (int y = text.Top; y < text.Bottom; y++)
            {
                int count;
                double fraction = BandRowFraction(pixels, width, strip, y, background, 56, out count);
                rows.Add(fraction);
                if (fraction >= 0.45) goodRows++;
            }
            rows.Sort();
            double rowMedian = rows.Count == 0 ? 0 : rows[rows.Count / 2];
            double rowGood = rows.Count == 0 ? 0 : (double)goodRows / rows.Count;
            result.RowCoverage = rowMedian;

            bool pink = IsPinkFamily(background);
            bool blue = IsBlueFamily(background);
            bool yellow = IsYellowFamily(background);
            Color ink = Color.Empty;
            int inkPixels = 0;
            bool pinkInk = false, blueInk = false, yellowInk = false;
            if (pink || blue || yellow)
            {
                Dictionary<int, Cluster> dark = new Dictionary<int, Cluster>();
                int textArea = Math.Max(1, text.Width * text.Height);
                // OCR word boxes can clip the darkest part of a scaled-down glyph.
                // The OCR line box is the reliable glyph area for broadcast ink; it
                // still contains mostly band pixels, so the dark-hue scan is safe.
                Rectangle area = text;
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        int pixel = pixels[y * width + x];
                        int r = (pixel >> 16) & 255, g = (pixel >> 8) & 255, b = pixel & 255;
                        Color value = Color.FromArgb(r, g, b);
                        if (Distance(pixel, background) < 52) continue;
                        if (Luminance(pixel) > Luminance(background) - 16) continue;
                        if (Chroma(value) < 14 && !yellow) continue;
                        // A glyph stroke is darker than at least one close neighbour;
                        // flat background/scene texture is not.
                        bool edge = false;
                        for (int delta = 1; delta <= 2 && !edge; delta++)
                            foreach (int shift in new int[] { -delta, delta, -delta * width, delta * width })
                            {
                                int at = y * width + x + shift;
                                if (at < 0 || at >= pixels.Length) continue;
                                if (Luminance(pixels[at]) >= Luminance(pixel) + 24) { edge = true; break; }
                            }
                        if (!edge) continue;
                        AddCluster(dark, pixel, 16);
                    }
                Cluster darkBest = BestCluster(dark);
                if (darkBest != null && darkBest.Count >= Math.Max(8, textArea / 400))
                {
                    ink = darkBest.Average;
                    inkPixels = darkBest.Count;
                    pinkInk = pink && IsPinkInk(ink, background);
                    blueInk = blue && IsBlueInk(ink, background);
                    yellowInk = yellow && IsYellowInk(ink, background);
                }
            }
            result.Ink = ink;
            result.InkPixels = inkPixels;
            result.InkMatchesBand = pinkInk || blueInk || yellowInk;
            bool boundary = (pink || blue || yellow) && HasVerticalBoundary(pixels, width, height, strip, text, background);
            result.Boundary = boundary;

            if ((pink || blue || yellow) && coverage >= 0.44 && rowMedian >= 0.46 && rowGood >= 0.55)
            {
                bool boundaryBand = boundary && coverage >= 0.50 && rowMedian >= 0.50;
                if (pink && (pinkInk || boundaryBand)) result.Band = ChatBandKind.PinkBroadcast;
                else if (blue && (blueInk || boundaryBand)) result.Band = ChatBandKind.BlueBroadcast;
                else if (yellow && (yellowInk || boundaryBand)) result.Band = ChatBandKind.YellowBroadcast;
                else result.Band = ChatBandKind.Colored;
            }
            else if (Chroma(background) >= 28 && Math.Max(background.R, Math.Max(background.G, background.B)) >= 90)
                result.Band = ChatBandKind.Colored;

            // The render colour is the sampled glyph ink, never a display-adjusted
            // version of the translated text. Real broadcast strokes are dark, so a
            // second pass keeps them instead of dropping every pixel below 125.
            Color foreground = result.InkMatchesBand ? ink : Color.Empty;
            if (foreground.IsEmpty || Luminance(foreground) < 70)
            {
                Color bright = SampleBrightInk(pixels, width, height, text, words, background);
                if (!bright.IsEmpty) foreground = bright;
                else if (!ink.IsEmpty && Chroma(ink) >= 18) foreground = ink;
            }
            result.Foreground = foreground;
            return result;
        }

        private static Color SampleBrightInk(int[] pixels, int width, int height, Rectangle text,
            IList<RectangleF> words, Color background)
        {
            Dictionary<int, Cluster> ink = new Dictionary<int, Cluster>();
            IList<RectangleF> boxes = words != null && words.Count > 0
                ? words : new List<RectangleF> { text };
            foreach (RectangleF box in boxes)
            {
                Rectangle area = Rectangle.Intersect(Rectangle.Ceiling(box), text);
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        int pixel = pixels[y * width + x];
                        int brightness = Math.Max((pixel >> 16) & 255, Math.Max((pixel >> 8) & 255, pixel & 255));
                        if (brightness < 112 || Distance(pixel, background) < 70) continue;
                        bool edge = false;
                        for (int delta = 1; delta <= 2 && !edge; delta++)
                            foreach (int shift in new int[] { -delta, delta, -delta * width, delta * width })
                            {
                                int at = y * width + x + shift;
                                if (at < 0 || at >= pixels.Length) continue;
                                int other = pixels[at];
                                int otherMax = Math.Max((other >> 16) & 255, Math.Max((other >> 8) & 255, other & 255));
                                if (otherMax < brightness - 55) { edge = true; break; }
                            }
                        if (!edge) continue;
                        AddCluster(ink, pixel, 16);
                    }
            }
            Cluster best = BestCluster(ink);
            return best != null && best.Count >= 2 ? best.Average : Color.Empty;
        }
    }
}
