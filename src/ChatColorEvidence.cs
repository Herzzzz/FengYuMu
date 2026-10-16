using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal static class ChatColorEvidence
    {
        private sealed class Cluster {internal long R,G,B;internal int Count;}
        private static int Distance(int pixel,Color color){return Math.Abs(((pixel>>16)&255)-color.R)+Math.Abs(((pixel>>8)&255)-color.G)+Math.Abs((pixel&255)-color.B);}
        private static Color Mode(int[] pixels,int width,int height,Rectangle area,out double coverage)
        {
            Dictionary<int,Cluster> groups=new Dictionary<int,Cluster>();int total=0;
            for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++){int value=pixels[y*width+x],key=((value>>20)&15)*256+((value>>12)&15)*16+((value>>4)&15);Cluster group;if(!groups.TryGetValue(key,out group)){group=new Cluster();groups.Add(key,group);}group.R+=(value>>16)&255;group.G+=(value>>8)&255;group.B+=value&255;group.Count++;total++;}
            Cluster best=null;foreach(Cluster group in groups.Values)if(best==null||group.Count>best.Count)best=group;
            coverage=best==null||total==0?0:(double)best.Count/total;return best==null?Color.Empty:Color.FromArgb((int)(best.R/best.Count),(int)(best.G/best.Count),(int)(best.B/best.Count));
        }
        internal static void Sample(int[] pixels,int width,int height,Rectangle text,IList<RectangleF> words,out Color foreground,out Color background,out bool band)
        {
            foreground=background=Color.Empty;band=false;Rectangle frame=new Rectangle(0,0,width,height);text.Intersect(frame);if(pixels==null||(long)width*height!=pixels.Length||text.Width<2||text.Height<2)return;
            Rectangle ring=text;ring.Inflate(10,3);ring.Intersect(frame);double coverage;background=Mode(pixels,width,height,ring,out coverage);
            int max=Math.Max(background.R,Math.Max(background.G,background.B)),min=Math.Min(background.R,Math.Min(background.G,background.B));
            int outside=0,solid=0;
            for(int y=ring.Top;y<ring.Bottom;y++)for(int x=ring.Left;x<ring.Right;x++)if(!text.Contains(x,y)){outside++;if(Distance(pixels[y*width+x],background)<46)solid++;}
            // Foreground color is never evidence of a broadcast background. The
            // blank ring must itself carry one broad, nearly uniform colored band.
            band=max>=100&&max-min>=28&&coverage>=.60&&outside>=20&&solid>=outside*.78;
            if(band){bool boundary=false;for(int delta=1;delta<=12&&!boundary;delta++)foreach(int y in new int[]{text.Top-delta,text.Bottom+delta}){
                    if(y<0||y>=height)continue;int same=0,count=0;for(int x=ring.Left;x<ring.Right;x+=3){count++;if(Distance(pixels[y*width+x],background)<46)same++;}
                    if(count>0&&same<count*.45){boundary=true;break;}}
                band=boundary;}
            Dictionary<int,Cluster> ink=new Dictionary<int,Cluster>();
            IList<RectangleF> boxes=words!=null&&words.Count>0?words:new List<RectangleF>{text};
            foreach(RectangleF box in boxes){Rectangle area=Rectangle.Intersect(Rectangle.Ceiling(box),text);
                for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++){
                    int pixel=pixels[y*width+x],r=(pixel>>16)&255,g=(pixel>>8)&255,b=pixel&255,brightness=Math.Max(r,Math.Max(g,b));
                    if(brightness<125||Distance(pixel,background)<86)continue;
                    bool edge=false;for(int delta=1;delta<=2&&!edge;delta++)foreach(int shift in new int[]{-delta,delta,-delta*width,delta*width}){
                        int at=y*width+x+shift;if(at<0||at>=pixels.Length)continue;int other=pixels[at];int otherMax=Math.Max((other>>16)&255,Math.Max((other>>8)&255,other&255));if(otherMax<brightness-70){edge=true;break;}}
                    if(!edge)continue;int key=(r>>4)*256+(g>>4)*16+(b>>4);Cluster group;if(!ink.TryGetValue(key,out group)){group=new Cluster();ink.Add(key,group);}group.Count++;group.R+=r;group.G+=g;group.B+=b;
                }}
            Cluster winner=null;foreach(Cluster value in ink.Values)if(winner==null||value.Count>winner.Count)winner=value;
            if(winner!=null&&winner.Count>=2)foreground=Color.FromArgb((int)(winner.R/winner.Count),(int)(winner.G/winner.Count),(int)(winner.B/winner.Count));
        }
    }
}
