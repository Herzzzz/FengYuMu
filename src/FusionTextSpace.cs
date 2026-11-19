using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    // Expand drawing space only into corroborated blank pixels of this immutable
    // capture. This does not erase a larger source rectangle or relax ink repair.
    internal static class FusionTextSpace
    {
        internal static bool TryExpandRight(ReconstructedTextSurface source, int[] frame,
            int width, int height, int requestedWidth, int maximumExtraWidth, int remainingPixels,
            IList<Rectangle> protectedAreas, out ReconstructedTextSurface expanded, out string reason)
        {
            expanded = null; reason = "invalid-space-source";
            if (source == null || source.Pixels == null || frame == null || width <= 0 || height <= 0 ||
                (long)width * height != frame.Length || source.Region.Width < 2 || source.Region.Height < 1 ||
                source.Region.Left < 0 || source.Region.Top < 0 ||
                (long)source.Region.Left + source.Region.Width > width ||
                (long)source.Region.Top + source.Region.Height > height ||
                (long)source.Region.Width * source.Region.Height != source.Pixels.Length) return false;
            int extra = requestedWidth - source.Region.Width;
            if (requestedWidth <= source.Region.Width || extra < 1 || maximumExtraWidth < extra ||
                (long)source.Region.Left + requestedWidth > width)
            { reason = "blank-space-unavailable"; return false; }
            long cost = (long)extra * source.Region.Height;
            if (cost > remainingPixels || (long)requestedWidth * source.Region.Height > 300000)
            { reason = "local-work-budget"; return false; }
            Rectangle extension = new Rectangle(source.Region.Right, source.Region.Top, extra, source.Region.Height);
            if (protectedAreas != null) foreach (Rectangle guard in protectedAreas)
                if (guard.IntersectsWith(extension))
                { reason = "protected-space"; return false; }
            for (int y = source.Region.Top; y < source.Region.Bottom; y++)
            {
                int edge = frame[y * width + source.Region.Right - 1];
                int previous = frame[y * width + source.Region.Right - 2];
                if (Distance(edge, previous) > 18)
                { reason = "unsupported-space-edge"; return false; }
                for (int x = extension.Left; x < extension.Right; x++)
                {
                    int pixel = frame[y * width + x];
                    if (Distance(pixel, edge) > 18 || Distance(pixel, previous) > 18)
                    { reason = "space-has-content"; return false; }
                    previous = pixel;
                }
            }
            int[] pixels = new int[checked(requestedWidth * source.Region.Height)];
            for (int y = 0; y < source.Region.Height; y++)
            {
                Array.Copy(source.Pixels, y * source.Region.Width, pixels, y * requestedWidth, source.Region.Width);
                for (int x = 0; x < extra; x++)
                    pixels[y * requestedWidth + source.Region.Width + x] =
                        frame[(source.Region.Top + y) * width + extension.Left + x] | unchecked((int)0xff000000);
            }
            expanded = new ReconstructedTextSurface { Region = new Rectangle(source.Region.Left, source.Region.Top,
                requestedWidth, source.Region.Height), Pixels = pixels, Ink = source.Ink,
                SourceHeight = source.SourceHeight, MaskedPixels = source.MaskedPixels };
            reason = ""; return true;
        }

        private static int Distance(int a, int b)
        {
            return Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) +
                Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255));
        }
    }
}
