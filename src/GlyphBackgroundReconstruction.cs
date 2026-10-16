using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    // An independent local repair model for high-contrast glyphs on translucent
    // panels. A rough whole row is insufficient evidence to flatten its background.
    // Every removed pixel needs agreeing horizontal AND vertical clean supports.
    internal static class GlyphBackgroundReconstruction
    {
        internal static bool TryReconstruct(int[] frame, int width, int height, Rectangle source,
            IList<Rectangle> rows, IList<Rectangle> protectedText,
            out ReconstructedTextSurface result, out string reason)
        {
            result = null; reason = "glyph-invalid-source";
            if (frame == null || width <= 0 || height <= 0 || (long)width * height != frame.Length ||
                source.Width < 4 || source.Height < 5 || source.Left < 3 || source.Top < 3 ||
                (long)source.X+source.Width > width-3 || (long)source.Y+source.Height > height-3 ||
                rows == null || rows.Count < 1 || rows.Count > 40) return false;
            Rectangle region = source; region.Inflate(3, 3);
            if ((long)region.Width * region.Height > 300000) return false;
            int[] pixels = new int[region.Width*region.Height];
            for (int y=0;y<region.Height;y++)
                Array.Copy(frame,(region.Top+y)*width+region.Left,pixels,y*region.Width,region.Width);
            for (int i=0;i<pixels.Length;i++) pixels[i] |= unchecked((int)0xff000000);
            long red=0,green=0,blue=0;int inkCount=0,masked=0;
            List<int> heights = new List<int>();
            foreach (Rectangle original in rows)
            {
                Rectangle row = Rectangle.Intersect(original,source);
                if (row.Width < 24 || row.Height < 5 || row.Height > 96)
                { reason="glyph-insufficient-row"; return false; }
                heights.Add(row.Height);
                int minimum=255,maximum=0,samples=0;
                for (int y=row.Top-3;y<=row.Bottom+2;y++)
                {
                    if (y>=row.Top-1 && y<=row.Bottom) continue;
                    for (int x=row.Left-1;x<=row.Right;x++)
                    {
                        if (Blocked(x,y,protectedText)) continue;
                        int c=frame[y*width+x];minimum=Math.Min(minimum,Min(c));maximum=Math.Max(maximum,Max(c));samples++;
                    }
                }
                if (samples < row.Width*2) { reason="glyph-missing-support"; return false; }
                bool light = maximum <= 180, dark = minimum >= 175;
                if (!light && !dark) { reason="glyph-unseparated-background"; return false; }
                Rectangle area=row;area.Inflate(1,1);
                bool[] core=new bool[area.Width*area.Height];int coreCount=0;
                for(int y=area.Top;y<area.Bottom;y++) for(int x=area.Left;x<area.Right;x++)
                {
                    int c=frame[y*width+x];
                    if ((light && Min(c)>=210 || dark && Max(c)<=70) && Max(c)-Min(c)<=60)
                    {
                        core[(y-area.Top)*area.Width+x-area.Left]=true;coreCount++;
                        red+=(c>>16)&255;green+=(c>>8)&255;blue+=c&255;inkCount++;
                    }
                }
                if (coreCount<6 || coreCount*100>core.Length*45 || !HasGlyphComponents(core,area.Width,area.Height,row.Height))
                { reason="glyph-ambiguous-foreground"; return false; }
                bool[] mask=(bool[])core.Clone();
                for(int y=0;y<area.Height;y++) for(int x=0;x<area.Width;x++) if(core[y*area.Width+x])
                    for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
                        if(x+dx>=0 && x+dx<area.Width && y+dy>=0 && y+dy<area.Height)
                            mask[(y+dy)*area.Width+x+dx]=true;
                int maskCount=0;
                for(int y=area.Top;y<area.Bottom;y++) for(int x=area.Left;x<area.Right;x++)
                {
                    if(!mask[(y-area.Top)*area.Width+x-area.Left]) continue;
                    int horizontal,vertical;
                    if(!Estimate(frame,width,height,x,y,1,0,area,mask,light,protectedText,out horizontal) ||
                        !Estimate(frame,width,height,x,y,0,1,area,mask,light,protectedText,out vertical) ||
                        Distance(horizontal,vertical)>60)
                    { reason="glyph-local-disagreement";return false; }
                    int repaired=Mix(horizontal,vertical,1,2);
                    // Dilation is a search envelope, not proof that every pixel in
                    // it is ink. Counting already-clean whitespace as erased text
                    // rejected ordinary tightly spaced glyphs at the density gate.
                    // Every changed pixel still needs both agreeing support axes.
                    if(Distance(frame[y*width+x],repaired)<=18) continue;
                    pixels[(y-region.Top)*region.Width+x-region.Left]=repaired;masked++;maskCount++;
                }
                if(maskCount*100>mask.Length*45) { reason="glyph-dense-mask";return false; }
            }
            if(inkCount==0) return false;
            heights.Sort();
            result=new ReconstructedTextSurface{Region=region,Pixels=pixels,MaskedPixels=masked,
                SourceHeight=heights[heights.Count/2],Ink=unchecked((int)0xff000000)|
                    ((int)(red/inkCount)<<16)|((int)(green/inkCount)<<8)|(int)(blue/inkCount)};
            reason="";return true;
        }
        private static bool Estimate(int[] frame,int width,int height,int x,int y,int dx,int dy,
            Rectangle area,bool[] mask,bool light,IList<Rectangle> text,out int estimate)
        {
            estimate=0;int a,b,da,db;
            if(!Support(frame,width,height,x,y,-dx,-dy,area,mask,light,text,out a,out da) ||
                !Support(frame,width,height,x,y,dx,dy,area,mask,light,text,out b,out db)) return false;
            estimate=Mix(a,b,da,da+db);return true;
        }
        private static bool Support(int[] frame,int width,int height,int x,int y,int dx,int dy,
            Rectangle area,bool[] mask,bool light,IList<Rectangle> text,out int color,out int distance)
        {
            color=0;distance=0;bool found=false;
            Rectangle search=area;search.Inflate(2,2);
            for(int step=1;step<=16;step++)
            {
                int px=x+dx*step,py=y+dy*step;
                if(px<0||px>=width||py<0||py>=height||!search.Contains(px,py)) return false;
                if(area.Contains(px,py)) { if(mask[(py-area.Top)*area.Width+px-area.Left]) continue; }
                else if(Blocked(px,py,text)) return false;
                int sample=frame[py*width+px];
                if(light ? Max(sample)>180 : Min(sample)<175) return false;
                if(!found){color=sample;distance=step;found=true;}
                else return Distance(color,sample)<=18;
            }
            return false;
        }
        private static bool HasGlyphComponents(bool[] core,int width,int height,int rowHeight)
        {
            bool[] seen=new bool[core.Length];Queue<int> queue=new Queue<int>();int components=0;
            for(int start=0;start<core.Length;start++)
            {
                if(!core[start]||seen[start]) continue;
                int left=width,right=0,top=height,bottom=0,count=0;
                queue.Enqueue(start);seen[start]=true;
                while(queue.Count>0)
                {
                    int index=queue.Dequeue(),x=index%width,y=index/width;count++;
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                    for(int d=0;d<4;d++)
                    {
                        int nx=x+(d==0?-1:d==1?1:0),ny=y+(d==2?-1:d==3?1:0);
                        if(nx<0||nx>=width||ny<0||ny>=height) continue;
                        int next=ny*width+nx;if(!core[next]||seen[next])continue;seen[next]=true;queue.Enqueue(next);
                    }
                }
                if(right-left+1>rowHeight*2||count>rowHeight*rowHeight) return false;
                if(count>=2&&bottom-top>=2) components++;
            }
            return components>=3;
        }
        private static bool Blocked(int x,int y,IList<Rectangle> text)
        { if(text!=null)foreach(Rectangle r in text)if(r.Contains(x,y))return true;return false; }
        private static int Min(int c){return Math.Min((c>>16)&255,Math.Min((c>>8)&255,c&255));}
        private static int Max(int c){return Math.Max((c>>16)&255,Math.Max((c>>8)&255,c&255));}
        private static int Distance(int a,int b){return Math.Abs(((a>>16)&255)-((b>>16)&255))+Math.Abs(((a>>8)&255)-((b>>8)&255))+Math.Abs((a&255)-(b&255));}
        private static int Mix(int a,int b,int n,int d)
        {return unchecked((int)0xff000000)|(((((a>>16)&255)*(d-n)+((b>>16)&255)*n+d/2)/d)<<16)|(((((a>>8)&255)*(d-n)+((b>>8)&255)*n+d/2)/d)<<8)|(((a&255)*(d-n)+(b&255)*n+d/2)/d);}
    }
}
