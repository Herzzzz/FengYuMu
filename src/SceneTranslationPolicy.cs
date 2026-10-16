using System;
using System.Drawing;
using System.Collections.Generic;

namespace MapleOverlay
{
    // Pixel evidence belongs to the clean source frame, never to painted translations.
    internal static class SceneTranslationPolicy
    {
        internal static double PulseOpacity(double elapsedMs)
        {
            if (Double.IsNaN(elapsedMs) || elapsedMs <= 0 || elapsedMs >= 320) return 0;
            return Math.Sin(Math.PI * elapsedMs / 320.0) * 0.055;
        }

        internal static bool PatchChanged(int[] original, int[] current)
        {
            if (original == null || current == null || original.Length < 16 ||
                original.Length != current.Length) return true;
            int changed = 0;
            for (int i = 0; i < original.Length; i++)
            {
                int a = original[i], b = current[i];
                int distance = Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) +
                    Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255));
                if (distance >= 96) changed++;
            }
            return changed * 5 >= original.Length;
        }

        internal static RectangleF CardBounds(RectangleF anchor, SizeF measured,
            SizeF wrapped, RectangleF client, float maximumWidth)
        {
            if (client.Width < 24 || client.Height < 24 || !Finite(measured.Width) ||
                !Finite(measured.Height) || !Finite(wrapped.Width) || !Finite(wrapped.Height) ||
                !Finite(maximumWidth) || maximumWidth < 24)
                return RectangleF.Empty;
            float width = Math.Min(client.Width, Math.Max(24, Math.Min(maximumWidth, (float)Math.Ceiling(measured.Width) + 20)));
            float height = (float)Math.Ceiling(measured.Width + 16 <= width ? measured.Height : wrapped.Height) + 16;
            if (height > client.Height || height < 1) return RectangleF.Empty;
            float x = Math.Max(client.Left, Math.Min(anchor.Left, client.Right - width));
            float y = Math.Max(client.Top, Math.Min(anchor.Top, client.Bottom - height));
            return new RectangleF(x, y, width, height);
        }

        internal static bool CanMergeSemanticBlocks(string leftRole, string rightRole,
            RectangleF left, RectangleF right)
        {
            if (String.IsNullOrEmpty(leftRole) || String.IsNullOrEmpty(rightRole))
            {
                RectangleF unknownOverlap = RectangleF.Intersect(left, right);
                float unknownArea = Math.Min(left.Width * left.Height, right.Width * right.Height);
                return unknownArea > 0 && unknownOverlap.Width * unknownOverlap.Height >= unknownArea * 0.35f;
            }
            if (leftRole != rightRole)
            {
                // A generic detail pass and a task-specific pass may recover the same body.
                // Only overlapping source evidence can identify them as one block.
                bool sameBody = (leftRole == "detail-body" && rightRole == "task-body") ||
                    (leftRole == "task-body" && rightRole == "detail-body");
                if (!sameBody) return false;
                RectangleF bodyOverlap = RectangleF.Intersect(left, right);
                float bodyArea = Math.Min(left.Width * left.Height, right.Width * right.Height);
                return bodyArea > 0 && bodyOverlap.Width * bodyOverlap.Height >= bodyArea * 0.35f;
            }
            if (leftRole.EndsWith("-body", StringComparison.Ordinal)) return true;
            RectangleF overlap = RectangleF.Intersect(left, right);
            float smaller = Math.Min(left.Width * left.Height, right.Width * right.Height);
            return smaller > 0 && overlap.Width * overlap.Height >= smaller * 0.35f;
        }

        internal static RectangleF PlaceCard(RectangleF card, IList<RectangleF> occupied, RectangleF client)
        {
            RectangleF best = RectangleF.Empty;
            float bestDistance = Single.MaxValue;
            List<PointF> candidates = new List<PointF> { card.Location };
            foreach (RectangleF previous in occupied)
            {
                candidates.Add(new PointF(previous.Right + 4, card.Top));
                candidates.Add(new PointF(previous.Left - card.Width - 4, card.Top));
                candidates.Add(new PointF(card.Left, previous.Bottom + 4));
                candidates.Add(new PointF(card.Left, previous.Top - card.Height - 4));
            }
            foreach (PointF point in candidates)
            {
                RectangleF candidate = new RectangleF(point.X, point.Y, card.Width, card.Height);
                if (!client.Contains(candidate)) continue;
                bool overlaps = false;
                foreach (RectangleF previous in occupied)
                    if (previous.IntersectsWith(candidate)) { overlaps = true; break; }
                if (overlaps) continue;
                float distance = Math.Abs(point.X - card.X) + Math.Abs(point.Y - card.Y);
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }

        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value) && value >= 0; }
    }
}
