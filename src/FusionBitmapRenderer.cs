using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MapleOverlay
{
    internal static class FusionBitmapRenderer
    {
internal static bool[] MakeTransparent(Bitmap patch,
    int[] frame,
    int width,
    int height,
    Rectangle region)
{
    int[] ignored;
    return MakeTransparent(patch,frame,width,height,region,out ignored);
}

internal static bool[] MakeTransparent(Bitmap patch,int[] frame,int width,int height,
    Rectangle region,out int[] patchPixels)
{
    patchPixels=null;
    BitmapData data = patch.LockBits(
        new Rectangle(0, 0, patch.Width, patch.Height),
        ImageLockMode.ReadWrite,
        PixelFormat.Format32bppArgb);

    try
    {
        int[] output = new int[patch.Width * patch.Height];

        for (int y = 0; y < patch.Height; y++)
        {
            Marshal.Copy(
                IntPtr.Add(data.Scan0, y * data.Stride),
                output,
                y * patch.Width,
                patch.Width);
        }

        bool[] mask = FusionPatchMask.Create(
            output, frame, width, height, region);

        if (mask == null) return null;
        patchPixels=output;

        for (int y = 0; y < patch.Height; y++)
        {
            Marshal.Copy(
                output,
                y * patch.Width,
                IntPtr.Add(data.Scan0, y * data.Stride),
                patch.Width);
        }

        return mask;
    }
    finally
    {
        patch.UnlockBits(data);
    }
}

        internal static int RequiredSingleLineWidth(ReconstructedTextSurface surface, string text, float dpiScale)
        {
            if (surface == null || String.IsNullOrWhiteSpace(text) || text.Length > 4096 ||
                text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0 ||
                dpiScale < 1 || Single.IsNaN(dpiScale) || Single.IsInfinity(dpiScale) ||
                surface.Region.Height > surface.SourceHeight + 6 || surface.Region.Height < 3) return 0;
            using (Bitmap sample = new Bitmap(1,1,PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(sample))
            using (Font font = new Font("SimSun", Math.Max(12 * dpiScale,
                Math.Min(26 * dpiScale, surface.SourceHeight)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                format.FormatFlags &= ~StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.None;
                int fitted, lines;
                SizeF measured = graphics.MeasureString(text, font,
                    new SizeF(65536, surface.Region.Height - 2), format, out fitted, out lines);
                if (fitted != text.Length || lines != 1 || Single.IsNaN(measured.Width) ||
                    Single.IsInfinity(measured.Width) || measured.Width > 65530) return 0;
                return (int)Math.Ceiling(measured.Width) + 5;
            }
        }

        internal static Bitmap TryRender(ReconstructedTextSurface surface, string text, float dpiScale)
        {
            if (surface == null || String.IsNullOrWhiteSpace(text) || text.Length > 4096 ||
                dpiScale < 1 || Single.IsNaN(dpiScale) || Single.IsInfinity(dpiScale)) return null;
            Bitmap patch = new Bitmap(surface.Region.Width, surface.Region.Height, PixelFormat.Format32bppArgb);
            bool completed = false;
            try
            {
                BitmapData data = patch.LockBits(new Rectangle(0, 0, patch.Width, patch.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < patch.Height; y++)
                        Marshal.Copy(surface.Pixels, y * patch.Width, IntPtr.Add(data.Scan0, y * data.Stride), patch.Width);
                }
                finally { patch.UnlockBits(data); }
                using (Bitmap mask = new Bitmap(patch.Width, patch.Height, PixelFormat.Format32bppArgb))
                {
                using (Graphics graphics = Graphics.FromImage(mask))
                using (Font font = new Font("SimSun", Math.Max(12 * dpiScale,
                    Math.Min(26 * dpiScale, surface.SourceHeight)), FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
                using (SolidBrush ink = new SolidBrush(Color.White))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    format.FormatFlags &= ~StringFormatFlags.NoWrap;
                    format.Trimming = StringTrimming.None;
                    RectangleF content = new RectangleF(3, 1, patch.Width - 5, patch.Height - 2);
                    int fitted, lines;
                    graphics.MeasureString(text, font, content.Size, format, out fitted, out lines);
                    if (fitted < text.Length) return null;
                    if (surface.Region.Height <= surface.SourceHeight + 6) format.LineAlignment = StringAlignment.Center;
                    graphics.SetClip(content);
                    graphics.DrawString(text, font, ink, content, format);
                }
                BitmapData maskData = mask.LockBits(new Rectangle(0, 0, mask.Width, mask.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    BitmapData target = patch.LockBits(new Rectangle(0, 0, patch.Width, patch.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int[] row = new int[patch.Width];
                        for (int y = 0; y < patch.Height; y++)
                        {
                            Marshal.Copy(IntPtr.Add(maskData.Scan0, y * maskData.Stride), row, 0, row.Length);
                            for (int x = 0; x < row.Length; x++)
                                row[x] = FusionInkWeight.Blend(surface.Pixels[y * patch.Width + x], surface.Ink, (int)((uint)row[x] >> 24));
                            Marshal.Copy(row, 0, IntPtr.Add(target.Scan0, y * target.Stride), row.Length);
                        }
                    }
                    finally { patch.UnlockBits(target); }
                }
                finally { mask.UnlockBits(maskData); }
                }
                completed = true; return patch;
            }
            finally { if (!completed) patch.Dispose(); }
        }
    }
}
