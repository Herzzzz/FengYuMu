using System;
namespace MapleOverlay
{
    internal static class FusionInkWeight
    {
        // Increase glyph coverage by 18%, rather than darkening light text on a
        // dark panel. Geometry, font size and repaired background stay unchanged.
        internal static int Blend(int background, int ink, int coverage)
        {
            int alpha = Math.Min(255, (Math.Max(0, Math.Min(255, coverage)) * 118 + 50) / 100);
            int inverse = 255 - alpha;
            int red = (((ink >> 16) & 255) * alpha + ((background >> 16) & 255) * inverse + 127) / 255;
            int green = (((ink >> 8) & 255) * alpha + ((background >> 8) & 255) * inverse + 127) / 255;
            int blue = ((ink & 255) * alpha + (background & 255) * inverse + 127) / 255;
            return unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
        }
    }
}
