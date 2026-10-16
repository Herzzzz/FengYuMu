using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal sealed class QuestJournalDocument
    {
        internal string Title, Body;
        internal Color Background;
        internal float FontHeight;
    }

    internal static class QuestJournalGeometry
    {
        // Trace the actual pale description surface from a stage heading. The strip
        // left of the heading provides a continuous vertical boundary independent of
        // missing English rows; objective cards above it are never part of the erase.
        internal static bool Measure(int[] pixels,int width,int height,RectangleF heading,
            RectangleF owner,float scale,out Rectangle area,out Color background)
        {
            area=Rectangle.Empty;background=Color.Empty;
            if(pixels==null || (long)width*height!=pixels.Length || scale<=0 ||
                !OverlayPaintSafety.IsFiniteRect(heading) || !OverlayPaintSafety.IsFiniteRect(owner) ||
                !owner.Contains(heading) || heading.Width<12 || heading.Height<5) return false;
            Rectangle bounds=Rectangle.Intersect(Rectangle.Ceiling(owner),new Rectangle(0,0,width,height));
            Rectangle best=Rectangle.Empty;int bestColor=0;long bestScore=0;
            int margin=Math.Max(5,(int)Math.Ceiling(18*scale));
            // Every plausible margin is considered. A pale frame bevel can be
            // the first light column, but its narrow strip is not the text paper.
            for(int offset=2;offset<=5;offset++) {
                int seedTop=Math.Max(bounds.Top,(int)Math.Floor(heading.Top-offset*scale));
                for(int lane=Math.Max(bounds.Left+2,(int)heading.Left-margin);lane<(int)heading.Left;lane++) {
                    int color=pixels[seedTop*width+lane];if(!Paper(color))continue;
                    int left=lane,right=lane;
                    while(left>bounds.Left&&Similar(pixels[seedTop*width+left-1],color))left--;
                    while(right<bounds.Right&&Similar(pixels[seedTop*width+right],color))right++;
                    left++;right--;
                    if(right-left<100*scale||right<heading.Right)continue;
                    int top=seedTop,bottom=seedTop;
                    while(bottom<bounds.Bottom&&Similar(pixels[bottom*width+lane],color))bottom++;
                    bottom--;
                    while(top>bounds.Top&&PaperRow(pixels,width,top-1,left,right,color))top--;
                    Rectangle candidate=Rectangle.FromLTRB(left,top,right,bottom);
                    if(candidate.Height<45*scale||!candidate.Contains(Rectangle.Ceiling(heading))||(long)candidate.Width*candidate.Height>800000)continue;
                    long score=(long)candidate.Width*candidate.Height;
                    if(score>bestScore){best=candidate;bestColor=color;bestScore=score;}
                }
            }
            if(best.IsEmpty)return false;
            area=best;background=Color.FromArgb(bestColor|unchecked((int)0xff000000));return true;
        }

        private static bool Paper(int c)
        {int r=(c>>16)&255,g=(c>>8)&255,b=c&255;return r>=218&&g>=218&&b>=205&&Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=30;}
        private static bool Similar(int a,int b)
        {return Math.Abs(((a>>16)&255)-((b>>16)&255))<=12 && Math.Abs(((a>>8)&255)-((b>>8)&255))<=12 && Math.Abs((a&255)-(b&255))<=12;}
        private static bool PaperRow(int[] pixels,int width,int y,int left,int right,int color)
        {
            int good=0,total=0;
            for(int x=left+3;x<right-3;x+=2){total++;if(Similar(pixels[y*width+x],color))good++;}
            return total>0&&good*100>=total*78;
        }
    }
}
