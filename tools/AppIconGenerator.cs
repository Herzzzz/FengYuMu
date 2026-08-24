using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

internal static class AppIconGenerator
{
    private static Bitmap Render(Bitmap source, int size)
    {
        Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(result))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            int maxHeight = Math.Max(1, size - Math.Max(2, size / 16));
            int width = Math.Max(1, (int)Math.Round(source.Width * (maxHeight / (double)source.Height)));
            int x = (size - width) / 2;
            int y = (size - maxHeight) / 2;
            g.DrawImage(source, new Rectangle(x, y, width, maxHeight));
        }
        return result;
    }

    private static byte[] PngBytes(Bitmap source, int size)
    {
        using (Bitmap image = Render(source, size))
        using (MemoryStream stream = new MemoryStream())
        {
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length != 3) return 2;
        string sourcePath = args[0], iconPath = args[1], previewPath = args[2];
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] images = new byte[sizes.Length][];
        using (Bitmap source = new Bitmap(sourcePath))
            for (int i = 0; i < sizes.Length; i++) images[i] = PngBytes(source, sizes[i]);

        using (FileStream file = File.Create(iconPath))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset);
                offset += images[i].Length;
            }
            for (int i = 0; i < images.Length; i++) writer.Write(images[i]);
        }

        using (Bitmap source = new Bitmap(sourcePath))
        using (Bitmap previewBase = Render(source, 256))
        using (Bitmap preview = new Bitmap(512, 512, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(preview))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(previewBase, new Rectangle(0, 0, 512, 512));
            preview.Save(previewPath, ImageFormat.Png);
        }
        return 0;
    }
}
