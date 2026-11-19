using System;
using System.Collections.Generic;
using System.Drawing;
namespace MapleOverlay
{
    // Only near-identical measurements of the same row can share ownership.
    // Neighbouring rows, counters and broader text blocks remain protected.
    internal static class FusionRowEvidence
    {
        internal static bool SameBounds(RectangleF a,RectangleF b)
        {
            if(a.Width<4||b.Width<4||a.Height<5||b.Height<5)return false;
            float drift=Math.Min(2.0f,Math.Min(a.Height,b.Height)*0.20f);
            return Math.Abs(a.Left-b.Left)<=drift&&Math.Abs(a.Right-b.Right)<=drift&&
                Math.Abs(a.Top-b.Top)<=drift&&Math.Abs(a.Bottom-b.Bottom)<=drift;
        }
        internal static List<Rectangle> OtherGuards(IList<Rectangle> observed,IList<Rectangle> owned)
        {
            List<Rectangle> result=new List<Rectangle>();
            if(observed==null)return result;
            foreach(Rectangle guard in observed)
            {
                bool same=false;
                if(owned!=null)foreach(Rectangle row in owned)
                    if(SameBounds(guard,row)){same=true;break;}
                if(!same)result.Add(guard);
            }
            return result;
        }
    }
}
