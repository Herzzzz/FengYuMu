using System;
using System.Drawing;
using System.Collections.Generic;

namespace MapleOverlay
{
    internal enum VerifiedPanelKind { None, Statistics, Information, Settings, WorldMap }

    internal static class PanelRecognitionPolicy
    {
        internal static VerifiedPanelKind CaptionKind(string normalized)
        {
            switch (normalized)
            {
                case "character stat": case "character stats": return VerifiedPanelKind.Statistics;
                case "character info": case "character information": return VerifiedPanelKind.Information;
                case "world map": return VerifiedPanelKind.WorldMap;
                case "options": case "system options": case "game options": case "sound options":
                case "graphics options": case "game settings": case "system settings":
                case "sound settings": case "graphics settings": case "key bindings":
                case "key einoings": return VerifiedPanelKind.Settings;
                default: return VerifiedPanelKind.None;
            }
        }

        internal static RectangleF MeasuredScope(RectangleF caption, float ocrScale,
            int captureWidth, int captureHeight, VerifiedPanelKind kind, IList<RectangleF> measuredPanels)
        {
            if (kind == VerifiedPanelKind.None || !Finite(caption.X) || !Finite(caption.Y) ||
                !Finite(caption.Width) || !Finite(caption.Height) || caption.Width <= 0 || caption.Height <= 0 ||
                !Finite(ocrScale) || ocrScale <= 0 || captureWidth <= 0 || captureHeight <= 0 ||
                measuredPanels == null) return RectangleF.Empty;
            RectangleF frame = new RectangleF(0,0,captureWidth * ocrScale,captureHeight * ocrScale);
            RectangleF selected = RectangleF.Empty;
            foreach (RectangleF measured in measuredPanels)
            {
                if (!Finite(measured.X) || !Finite(measured.Y) || !Finite(measured.Width) ||
                    !Finite(measured.Height) || measured.Width <= 0 || measured.Height <= 0) continue;
                RectangleF clipped = RectangleF.Intersect(frame, measured);
                if (!clipped.Contains(caption) || clipped.Height < caption.Height * 4) continue;
                if (selected.IsEmpty || (double)clipped.Width * clipped.Height < (double)selected.Width * selected.Height)
                    selected = clipped;
            }
            // A caption does not establish a window's extent. Never manufacture a
            // 760/960px rectangle that could lend panel semantics to nearby HUD/chat.
            return selected;
        }

        internal static bool Owns(RectangleF scope, RectangleF row)
        {
            return scope.Width > 0 && scope.Height > 0 && row.Width > 0 && row.Height > 0 &&
                scope.Contains(row);
        }

        internal static bool IsInformationField(string normalized)
        {
            switch (normalized)
            {
                case "character info": case "character information": case "citizenship":
                case "level": case "lv": case "job": case "fame": case "guild":
                case "alliance": case "party": case "trade": case "request party":
                case "request trade": case "pet info": case "pet information":
                case "see pet info": case "collection": case "collections":
                case "medal": case "medals": return true;
                default: return false;
            }
        }

        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
    }
}
