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
            int top=Math.Max(bounds.Top,(int)Math.Floor(heading.Top-3*scale));
            int maxMargin=Math.Max(5,(int)Math.Ceiling(18*scale)), lane=-1, color=0;
            // Prefer the outermost continuous paper margin, not an interior gap in text.
            for(int x=Math.Max(bounds.Left+2,(int)heading.Left-maxMargin);x<(int)heading.Left;x++) {
                int sample=pixels[top*width+x];
                if(!Paper(sample))continue;
                int good=0,total=0;
                for(int y=top;y<Math.Min(bounds.Bottom,top+Math.Max(25,(int)(heading.Height*2)));y++)
                {total++;if(Similar(pixels[y*width+x],sample))good++;}
                if(total>0 && good*100>=total*95){lane=x;color=sample;break;}
            }
            if(lane<0)return false;
            int bottom=top;
            while(bottom<bounds.Bottom && Similar(pixels[bottom*width+lane],color))bottom++;
            // Find a horizontal paper run through the heading's top blank margin.
            int left=lane,right=lane;
            while(left>bounds.Left && Similar(pixels[top*width+left-1],color))left--;
            while(right<bounds.Right && Similar(pixels[top*width+right],color))right++;
            left++;right--;bottom--;
            // The entire paper region below the objective card belongs to the
            // description, including an OCR-missed heading or first prose row.
            // A row-wide boundary stops at the grey card even when a thin white
            // margin continues up beside its item icons.
            while(top>bounds.Top && PaperRow(pixels,width,top-1,left,right,color))top--;
            if(right<heading.Right || right-left<100*scale || bottom-top<45*scale)return false;
            // Both side margins must remain paper. This stops before footer chrome and
            // excludes the scrollbar without a fixed screen-percentage rectangle.
            int edge=Math.Max(left+1,lane);
            for(int y=top;y<bottom;y++)
                if(!Similar(pixels[y*width+edge],color) || !Similar(pixels[y*width+right-1],color))
                {bottom=y-1;break;}
            area=Rectangle.FromLTRB(left,top,right,bottom);
            if(area.Width<100*scale || area.Height<45*scale || !area.Contains(Rectangle.Ceiling(heading)) ||
                (long)area.Width*area.Height>800000){area=Rectangle.Empty;return false;}
            background=Color.FromArgb(color|unchecked((int)0xff000000));return true;
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
