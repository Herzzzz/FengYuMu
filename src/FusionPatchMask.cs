using System;
using System.Drawing;

namespace MapleOverlay
{
    internal static class FusionPatchMask
    {
        internal static bool[] Create(
            int[] output,
            int[] frame,
            int width,
            int height,
            Rectangle region)
        {
            if (output == null ||
                frame == null ||
                (long)width * height != frame.Length ||
                region.Width <= 0 ||
                region.Height <= 0 ||
                (long)region.Width * region.Height != output.Length ||
                !new Rectangle(0, 0, width, height).Contains(region))
                return null;

            bool[] mask = new bool[output.Length];
            int changed = 0;

            for (int y = 0; y < region.Height; y++)
            {
                for (int x = 0; x < region.Width; x++)
                {
                    int i = y * region.Width + x;
                    int original = frame[
                        (region.Top + y) * width + region.Left + x];

                    mask[i] =
                        (output[i] & 0xffffff) !=
                        (original & 0xffffff);

                    if (mask[i])
                    {
                        output[i] |= unchecked((int)0xff000000);
                        changed++;
                    }
                    else
                    {
                        // Transparent original pixels cannot cover
                        // a neighboring row's translated text.
                        output[i] = 0;
                    }
                }
            }

            return changed == 0 ? null : mask;
        }

        internal static bool Touches(
            bool[] mask,
            Rectangle region,
            RectangleF protectedBounds)
        {
            Rectangle overlap = Rectangle.Intersect(
                region,
                Rectangle.Ceiling(protectedBounds));

            if (overlap.IsEmpty) return false;
            if (!Valid(mask, region)) return true;

            for (int y = overlap.Top; y < overlap.Bottom; y++)
            {
                for (int x = overlap.Left; x < overlap.Right; x++)
                {
                    if (mask[
                        (y - region.Top) * region.Width +
                        x - region.Left])
                        return true;
                }
            }

            return false;
        }

        internal static bool Overlaps(
            bool[] left,
            Rectangle a,
            bool[] right,
            Rectangle b)
        {
            Rectangle overlap = Rectangle.Intersect(a, b);

            if (overlap.IsEmpty) return false;
            if (!Valid(left, a) || !Valid(right, b)) return true;

            for (int y = overlap.Top; y < overlap.Bottom; y++)
            {
                for (int x = overlap.Left; x < overlap.Right; x++)
                {
                    if (left[
                            (y - a.Top) * a.Width + x - a.Left] &&
                        right[
                            (y - b.Top) * b.Width + x - b.Left])
                        return true;
                }
            }

            return false;
        }

        // Identical opaque writes commute. A shared background repair pixel must
        // not reject a neighbouring patch merely because both masks contain it.
        // Different colors (including text over another erase) still conflict.
        internal static bool Conflicts(bool[] left,Rectangle a,int[] leftPixels,
            bool[] right,Rectangle b,int[] rightPixels)
        {
            Rectangle overlap=Rectangle.Intersect(a,b);
            if(overlap.IsEmpty)return false;
            if(!Valid(left,a)||!Valid(right,b))return true;
            bool colors=leftPixels!=null&&rightPixels!=null&&leftPixels.Length==left.Length&&rightPixels.Length==right.Length;
            for(int y=overlap.Top;y<overlap.Bottom;y++)for(int x=overlap.Left;x<overlap.Right;x++)
            {
                int li=(y-a.Top)*a.Width+x-a.Left,ri=(y-b.Top)*b.Width+x-b.Left;
                if(left[li]&&right[ri]&&(!colors||leftPixels[li]!=rightPixels[ri]||
                    (uint)leftPixels[li]>>24!=255))return true;
            }
            return false;
        }

        private static bool Valid(bool[] mask, Rectangle region)
        {
            return mask != null &&
                region.Width > 0 &&
                region.Height > 0 &&
                (long)region.Width * region.Height == mask.Length;
        }
    }
}
