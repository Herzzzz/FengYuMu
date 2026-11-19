using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MapleOverlay
{
    // The client prints its English UI with the Noto Sans KR Medium face that ships in
    // Data/UI/Font/Font_000.wz, scaled 16 -> 15 px. At that size Windows OCR confuses
    // glyph pairs (measured: O~Q 0.84, P~R 0.82, E~F 0.74), so "REQ LEV" arrives as
    // "AEQ LEV". This matcher re-reads one word from the captured pixels and compares it
    // with candidates rendered in that same face.
    //
    // Comparison is normalized cross-correlation on a grayscale, aspect-preserving grid:
    // binary IoU lost the anti-aliased stroke edges and stretched every word to a fixed
    // box, which measured 0.24-0.47 even for a correct candidate.
    internal static class GlyphTemplateMatcher
    {
        private const string ResourceName = "MapleOverlay.NotoSansKR.ttf";
        private const int GridWidth = 24;
        private const int GridHeight = 16;
        private static PrivateFontCollection collection;
        private static string family;
        private static readonly Dictionary<string, float[]> templates =
            new Dictionary<string, float[]>(StringComparer.Ordinal);

        internal static bool Available
        {
            get { EnsureLoaded(); return family != null; }
        }

        private static void EnsureLoaded()
        {
            if (collection != null) return;
            collection = new PrivateFontCollection();
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return;
                    byte[] bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int step = stream.Read(bytes, read, bytes.Length - read);
                        if (step <= 0) break;
                        read += step;
                    }
                    if (read <= 0) return;
                    IntPtr memory = Marshal.AllocCoTaskMem(read);
                    Marshal.Copy(bytes, 0, memory, read);
                    collection.AddMemoryFont(memory, read);
                    if (collection.Families.Length > 0) family = collection.Families[0].Name;
                }
            }
            catch (Exception) { family = null; }
        }

        internal static bool TryRepair(int[] pixels, int width, int height, RectangleF wordRect,
            string observedText, string[] candidates, out string repaired, out float confidence)
        {
            repaired = null;
            confidence = 0f;
            EnsureLoaded();
            if (family == null || pixels == null || candidates == null ||
                candidates.Length == 0 || width <= 0 || height <= 0) return false;
            Rectangle area = Rectangle.Ceiling(wordRect);
            area.Inflate(0, 1);
            area.Intersect(new Rectangle(0, 0, width, height));
            if (area.Width < 6 || area.Height < 5 || area.Width > 200 || area.Height > 48) return false;
            int inkWidth, inkHeight;
            float[] observed = ReadGrid(pixels, width, height, area, out inkWidth, out inkHeight);
            if (observed == null || inkWidth < 3 || inkHeight < 5) return false;
            float best = -1f, second = -1f;
            string bestText = null;
            int observedLength = (observedText ?? "").Trim().Length;
            foreach (string candidate in candidates)
            {
                // OCR rarely changes a word length by more than one character; without this
                // guard a short token was matched to a much longer key (PEO -> SPEED).
                if (observedLength > 0 && Math.Abs(candidate.Length - observedLength) > 1) continue;
                float[] template = Template(candidate, inkHeight);
                if (template == null) continue;
                float score = Correlation(observed, template);
                if (score > best) { second = best; best = score; bestText = candidate; }
                else if (score > second) second = score;
            }
            confidence = best;
            repaired = bestText;   // reported even when rejected, for calibration logs
            if (bestText == null || best < 0.76f) return false;
            if (second > 0f && best - second < 0.03f) return false;
            repaired = bestText;
            return true;
        }

        // Luminance grid of the measured word, cropped to its ink box and scaled to the
        // shared grid with its aspect ratio preserved (height drives the scale, width is
        // centred), so "REQ" and "AEQ" stay comparable instead of both being stretched.
        private static float[] ReadGrid(int[] pixels, int width, int height, Rectangle area,
            out int inkWidth, out int inkHeight)
        {
            inkWidth = 0; inkHeight = 0;
            float[] luma = new float[area.Width * area.Height];
            float max = 0f;
            for (int y = 0; y < area.Height; y++)
                for (int x = 0; x < area.Width; x++)
                {
                    int color = pixels[(area.Top + y) * width + area.Left + x];
                    float value = (((color >> 16) & 255) * 0.299f + ((color >> 8) & 255) * 0.587f +
                        (color & 255) * 0.114f) / 255f;
                    luma[y * area.Width + x] = value;
                    if (value > max) max = value;
                }
            if (max <= 0.05f) return null;
            float cutoff = max * 0.55f;
            int left = area.Width, right = -1, top = area.Height, bottom = -1;
            for (int y = 0; y < area.Height; y++)
                for (int x = 0; x < area.Width; x++)
                {
                    if (luma[y * area.Width + x] < cutoff) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            if (right < left || bottom < top) return null;
            inkWidth = right - left + 1;
            inkHeight = bottom - top + 1;
            float[] crop = new float[inkWidth * inkHeight];
            for (int y = 0; y < inkHeight; y++)
                for (int x = 0; x < inkWidth; x++)
                    crop[y * inkWidth + x] = luma[(top + y) * area.Width + left + x];
            return ToGrid(crop, inkWidth, inkHeight);
        }

        private static float[] ToGrid(float[] source, int sourceWidth, int sourceHeight)
        {
            float[] grid = new float[GridWidth * GridHeight];
            if (sourceWidth <= 0 || sourceHeight <= 0) return grid;
            float scale = (float)GridHeight / sourceHeight;
            float targetWidth = sourceWidth * scale;
            float offsetX = (GridWidth - targetWidth) / 2.0f;
            for (int gy = 0; gy < GridHeight; gy++)
                for (int gx = 0; gx < GridWidth; gx++)
                {
                    float sx = (gx + 0.5f - offsetX) / scale - 0.5f;
                    float sy = (gy + 0.5f) / scale - 0.5f;
                    if (sx < -0.5f || sy < -0.5f || sx > sourceWidth - 0.5f ||
                        sy > sourceHeight - 0.5f) continue;
                    int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                    float fx = sx - x0, fy = sy - y0;
                    float v00 = Sample(source, sourceWidth, sourceHeight, x0, y0);
                    float v10 = Sample(source, sourceWidth, sourceHeight, x0 + 1, y0);
                    float v01 = Sample(source, sourceWidth, sourceHeight, x0, y0 + 1);
                    float v11 = Sample(source, sourceWidth, sourceHeight, x0 + 1, y0 + 1);
                    grid[gy * GridWidth + gx] = (v00 * (1 - fx) + v10 * fx) * (1 - fy) +
                        (v01 * (1 - fx) + v11 * fx) * fy;
                }
            return grid;
        }

        private static float Sample(float[] source, int width, int height, int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return 0f;
            return source[y * width + x];
        }

        private static float[] Template(string text, int pixelHeight)
        {
            int size = Math.Max(6, Math.Min(28, pixelHeight));
            string key = size + "|" + text;
            float[] cached;
            if (templates.TryGetValue(key, out cached)) return cached;
            float[] result = null;
            try
            {
                using (Bitmap bitmap = new Bitmap(Math.Max(8, text.Length * size + 8),
                    size * 2 + 8, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Font font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (SolidBrush brush = new SolidBrush(Color.White))
                        graphics.DrawString(text, font, brush, 2, 2);
                    int[] pixels = ReadPixels(bitmap);
                    int inkWidth, inkHeight;
                    result = ReadGrid(pixels, bitmap.Width, bitmap.Height,
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height), out inkWidth, out inkHeight);
                }
            }
            catch (Exception) { result = null; }
            templates[key] = result;
            return result;
        }

        private static int[] ReadPixels(Bitmap bitmap)
        {
            int[] pixels = new int[bitmap.Width * bitmap.Height];
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width,
                        bitmap.Width);
            }
            finally { bitmap.UnlockBits(data); }
            return pixels;
        }

        // Normalized cross-correlation: insensitive to brightness/contrast differences
        // between the captured pixels and the rendered template.
        private static float Correlation(float[] left, float[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return 0f;
            double sumAB = 0, sumAA = 0, sumBB = 0;
            for (int i = 0; i < left.Length; i++)
            {
                sumAB += left[i] * right[i];
                sumAA += left[i] * left[i];
                sumBB += right[i] * right[i];
            }
            if (sumAA <= 1e-6 || sumBB <= 1e-6) return 0f;
            return (float)(sumAB / Math.Sqrt(sumAA * sumBB));
        }
    }
}
