using System;
using System.Drawing;
namespace MapleOverlay
{
    internal static class CaptureFramePolicy
    {
        internal static long CounterTicks(long counter, long frequency)
        {
            if (counter < 0 || frequency <= 0) throw new ArgumentOutOfRangeException();
            return checked(counter / frequency * TimeSpan.TicksPerSecond +
                counter % frequency * TimeSpan.TicksPerSecond / frequency);
        }
        internal static bool IsFresh(long frameTicks, long requestedTicks)
        { return requestedTicks > 0 && frameTicks >= requestedTicks; }
        internal static bool TryClientCrop(Size frame, Rectangle client, Rectangle extended, Rectangle outer, out Rectangle crop)
        {
            crop = Rectangle.Empty;
            if (frame.Width < 1 || frame.Height < 1 || (long)frame.Width * frame.Height > 64000000 ||
                client.Width < 1 || client.Height < 1) return false;
            Rectangle surface = extended.Size == frame && extended.Contains(client) ? extended :
                (outer.Size == frame && outer.Contains(client) ? outer : Rectangle.Empty);
            if (surface.IsEmpty) return false;
            crop = new Rectangle(client.Left - surface.Left, client.Top - surface.Top, client.Width, client.Height);
            return true;
        }
    }
}
