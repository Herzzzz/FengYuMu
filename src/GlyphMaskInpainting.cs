using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    // Recovery implementation, not the missing original r8/r9 file. It only
    // replaces a measured, sparse glyph mask. Clean context remains byte exact.
    // Smoothness and separated ink/context are required; uncertain pictures fail closed.
    internal static class GlyphMaskInpainting
    {
        internal static bool TryReconstruct(int[] frame, int width, int height, Rectangle source,
            IList<Rectangle> rows, IList<Rectangle> protectedText,
            out ReconstructedTextSurface result, out string reason)
        {
            result = null; reason = "inpaint-invalid-source";
            if (frame == null || width <= 0 || height <= 0 || (long)width*height != frame.Length ||
                source.Width < 24 || source.Height < 5 || source.X < 3 || source.Y < 3 ||
                (long)source.X+source.Width > width-3 || (long)source.Y+source.Height > height-3 ||
                rows == null || rows.Count == 0 || rows.Count > 40) return false;
            Rectangle region=source; region.Inflate(3,3);
            if ((long)region.Width*region.Height > 300000) return false;
            int[] pixels=new int[region.Width*region.Height];
            for(int y=0;y<region.Height;y++)
                Array.Copy(frame,(region.Top+y)*width+region.Left,pixels,y*region.Width,region.Width);
            bool[] mask=new bool[pixels.Length];
            List<int> heights=new List<int>();
            long red=0,green=0,blue=0; int inkCount=0;
            foreach(Rectangle original in rows)
            {
                Rectangle row=Rectangle.Intersect(original,source);
                if(row.Width<24 || row.Height<5 || row.Height>96)
                { reason="inpaint-invalid-row";return false; }
                heights.Add(row.Height);
                Rectangle area=row;area.Inflate(1,1);
                bool light=true,dark=true;int supports=0;
                // The clean band must independently support the foreground polarity.
                for(int y=row.Top-3;y<=row.Bottom+2;y++)
                for(int x=row.Left-2;x<=row.Right+1;x++)
                {
                    if(area.Contains(x,y)) continue;
                    if(Blocked(x,y,protectedText)) continue;
                    int c=frame[y*width+x];supports++;
                    light &= Max(c)<=180; dark &= Min(c)>=175;
                }
                if(supports<row.Width*2 || (!light&&!dark))
                {reason="inpaint-missing-context";return false;}
                bool[] core=new bool[area.Width*area.Height];int coreCount=0;
                for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)
                {
                    int c=frame[y*width+x];
                    if((light?Min(c)>=210:Max(c)<=70) && Max(c)-Min(c)<=60)
                    {
                        core[(y-area.Top)*area.Width+x-area.Left]=true;coreCount++;
                        red+=(c>>16)&255;green+=(c>>8)&255;blue+=c&255;inkCount++;
                    }
                }
                if(coreCount<6 || coreCount*100>core.Length*45 || !GlyphComponents(core,area.Width,area.Height,row.Height))
                {reason="inpaint-ambiguous-foreground";return false;}
                bool[] expanded=(bool[])core.Clone();
                for(int y=0;y<area.Height;y++)for(int x=0;x<area.Width;x++)if(core[y*area.Width+x])
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                        if(x+dx>=0&&x+dx<area.Width&&y+dy>=0&&y+dy<area.Height)
                            expanded[(y+dy)*area.Width+x+dx]=true;
                int count=0;
                for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)
                {
                    int i=(y-area.Top)*area.Width+x-area.Left;
                    if(!expanded[i]) continue;
                    // Clean dilated space is not erased or charged as foreground.
                    int top=frame[(row.Top-2)*width+x],bottom=frame[(row.Bottom+1)*width+x];
                    int expected=Mix(top,bottom,y-(row.Top-2),row.Height+3);
                    if(!core[i] && Distance(frame[y*width+x],expected)<=24) continue;
                    if(ProtectedOther(x,y,row,protectedText))
                    {reason="inpaint-protected-text";return false;}
                    mask[(y-region.Top)*region.Width+x-region.Left]=true;count++;
                }
                if(count*100>expanded.Length*45)
                {reason="inpaint-dense-mask";return false;}
            }
            if(inkCount==0)return false;
            bool[] known=new bool[mask.Length];int remaining=0;
            for(int i=0;i<mask.Length;i++){known[i]=!mask[i];if(mask[i])remaining++;}
            int masked=remaining;
            // Bounded propagation from clean neighbors. Reject sharp context edges
            // and over-thick masks rather than inventing a panel background.
            for(int round=0;round<24 && remaining>0;round++)
            {
                int[] next=(int[])pixels.Clone();bool[] ready=(bool[])known.Clone();int filled=0;
                for(int y=1;y<region.Height-1;y++)for(int x=1;x<region.Width-1;x++)
                {
                    int i=y*region.Width+x;if(known[i])continue;
                    int[] around={i-1,i+1,i-region.Width,i+region.Width};
                    int sumR=0,sumG=0,sumB=0,n=0,first=0;
                    foreach(int j in around)if(known[j])
                    {
                        int c=pixels[j];
                        if(n>0 && Distance(first,c)>120)
                        {reason="inpaint-context-edge";return false;}
                        if(n==0)first=c;
                        sumR+=(c>>16)&255;sumG+=(c>>8)&255;sumB+=c&255;n++;
                    }
                    if(n==0)continue;
                    next[i]=unchecked((int)0xff000000)|((sumR/n)<<16)|((sumG/n)<<8)|(sumB/n);
                    ready[i]=true;filled++;
                }
                if(filled==0)break;pixels=next;known=ready;remaining-=filled;
            }
            if(remaining!=0){reason="inpaint-unresolved-mask";return false;}
            // Fixed Jacobi smoothing; never write outside the proven glyph mask.
            for(int round=0;round<24;round++)
            {
                int[] next=(int[])pixels.Clone();
                for(int y=1;y<region.Height-1;y++)for(int x=1;x<region.Width-1;x++)
                {
                    int i=y*region.Width+x;if(!mask[i])continue;
                    next[i]=Mix(Mix(pixels[i-1],pixels[i+1],1,2),
                        Mix(pixels[i-region.Width],pixels[i+region.Width],1,2),1,2);
                }
                pixels=next;
            }
            heights.Sort();result=new ReconstructedTextSurface{Region=region,Pixels=pixels,MaskedPixels=masked,
                SourceHeight=heights[heights.Count/2],Ink=unchecked((int)0xff000000)|
                    ((int)(red/inkCount)<<16)|((int)(green/inkCount)<<8)|(int)(blue/inkCount)};
            reason="";return true;
        }
        private static bool Blocked(int x,int y,IList<Rectangle> text)
        {if(text!=null)foreach(Rectangle r in text)if(r.Contains(x,y))return true;return false;}
        private static bool ProtectedOther(int x,int y,Rectangle row,IList<Rectangle> text)
        {if(text!=null)foreach(Rectangle r in text)if(r.Contains(x,y)&&!row.Contains(r)&&r!=row)return true;return false;}
        private static bool GlyphComponents(bool[] core,int w,int h,int rowHeight)
        {
            bool[] seen=new bool[core.Length];Queue<int> queue=new Queue<int>();int groups=0;
            for(int start=0;start<core.Length;start++)
            {
                if(!core[start]||seen[start])continue;
                int left=w,right=0,top=h,bottom=0,count=0;queue.Enqueue(start);seen[start]=true;
                while(queue.Count>0)
                {
                    int i=queue.Dequeue(),x=i%w,y=i/w;count++;
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                    int[] ns={x>0?i-1:-1,x+1<w?i+1:-1,y>0?i-w:-1,y+1<h?i+w:-1};
                    foreach(int n in ns)if(n>=0&&core[n]&&!seen[n]){seen[n]=true;queue.Enqueue(n);}
                }
                if(right-left+1>rowHeight*2||count>rowHeight*rowHeight)return false;
                if(count>=2&&bottom-top>=2)groups++;
            }
            return groups>=3;
        }
        private static int Min(int c){return Math.Min((c>>16)&255,Math.Min((c>>8)&255,c&255));}
        private static int Max(int c){return Math.Max((c>>16)&255,Math.Max((c>>8)&255,c&255));}
        private static int Distance(int a,int b){return Math.Abs(((a>>16)&255)-((b>>16)&255))+Math.Abs(((a>>8)&255)-((b>>8)&255))+Math.Abs((a&255)-(b&255));}
        private static int Mix(int a,int b,int n,int d)
        {return unchecked((int)0xff000000)|(((((a>>16)&255)*(d-n)+((b>>16)&255)*n+d/2)/d)<<16)|(((((a>>8)&255)*(d-n)+((b>>8)&255)*n+d/2)/d)<<8)|(((a&255)*(d-n)+(b&255)*n+d/2)/d);}
    }
}
