using System;
using System.Drawing;
using System.Collections.Generic;
using MapleOverlay;
public static class GlyphRepairHarness
{
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
    static int[] Background(int w,int h,bool light){int[] p=new int[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){int phase=x%40,shade=35+Math.Min(phase,40-phase)*4+y/10;p[y*w+x]=light?Color.FromArgb(255-shade/3,255-shade/3,255-shade/3).ToArgb():Color.FromArgb(shade,shade+5,shade+10).ToArgb();}return p;}
    static void Glyphs(int[] p,int w,Rectangle r,int color){for(int x=r.Left+3;x<r.Right-5;x+=10)for(int y=r.Top+1;y<r.Bottom-1;y++)p[y*w+x]=p[y*w+x+1]=color;}
    static bool Local(int[] p,int w,int h,Rectangle r,out ReconstructedTextSurface result,out string reason){return GlyphBackgroundReconstruction.TryReconstruct(p,w,h,r,new List<Rectangle>{r},new List<Rectangle>{r},out result,out reason);}
    public static int Run()
    {
        count=0;int w=180,h=90;Rectangle r=new Rectangle(20,30,120,12);var truth=Background(w,h,false);var p=(int[])truth.Clone();Glyphs(p,w,r,Color.White.ToArgb());var saved=(int[])p.Clone();ReconstructedTextSurface result;string reason;
        Check(!TextSurfaceReconstruction.TryReconstruct(p,w,h,r,new List<Rectangle>{r},out result,out reason),"varying translucent panel refuses the whole-row smooth model");
        Check(Local(p,w,h,r,out result,out reason),"independent horizontal and vertical supports repair isolated light glyphs");
        bool same=true;for(int i=0;i<p.Length;i++)same&=p[i]==saved[i];Check(same,"local repair leaves immutable input untouched");
        int worst=0;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++){int actual=result.Pixels[(y-result.Region.Top)*result.Region.Width+x-result.Region.Left];int expected=truth[y*w+x];worst=Math.Max(worst,Math.Abs((actual&255)-(expected&255)));}
        Check(worst<=2,"two-axis affine local backgrounds reproduce hidden pixels without a flat rectangle");
        Check(result.Ink==Color.White.ToArgb(),"light text ink is opaque stroke color rather than gray fringe");
        same=true;for(int y=result.Region.Top;y<result.Region.Bottom;y++)for(int x=result.Region.Left;x<result.Region.Right;x++)if(!r.Contains(x,y)&&y!=r.Top-1&&y!=r.Bottom)same&=result.Pixels[(y-result.Region.Top)*result.Region.Width+x-result.Region.Left]==saved[y*w+x];
        Check(same,"unmasked neighboring pixels are byte exact");
        p=Background(w,h,true);Glyphs(p,w,r,Color.Black.ToArgb());Check(Local(p,w,h,r,out result,out reason)&&result.Ink==Color.Black.ToArgb(),"dark glyphs require the opposite independently separated background");
        p=Background(w,h,false);for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)p[y*w+x]=Color.White.ToArgb();
        Check(!Local(p,w,h,r,out result,out reason),"filled bright icon or surface is never accepted as text");
        p=Background(w,h,false);Glyphs(p,w,r,Color.White.ToArgb());for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(y<r.Top||y>=r.Bottom)p[y*w+x]=(x%2==0?Color.Black:Color.White).ToArgb();
        Check(!Local(p,w,h,r,out result,out reason),"checkerboard backgrounds cannot establish separated supports");
        p=Background(w,h,false);Check(!Local(p,w,h,r,out result,out reason),"blank region cannot be erased");
        p=Background(w,h,false);Glyphs(p,w,r,Color.White.ToArgb());for(int y=r.Top-3;y<r.Bottom+3;y++)for(int x=25;x<28;x++)p[y*w+x]=Color.FromArgb(165,170,175).ToArgb();
        Check(!Local(p,w,h,r,out result,out reason),"unexplained sharp edge near a glyph fails local corroboration");
        p=Background(w,h,false);Glyphs(p,w,r,Color.White.ToArgb());var blocked=new List<Rectangle>{new Rectangle(0,0,w,h)};
        Check(!GlyphBackgroundReconstruction.TryReconstruct(p,w,h,r,new List<Rectangle>{r},blocked,out result,out reason),"other text occupying support corridors fails closed");
        Check(!Local(p,w,h,new Rectangle(Int32.MaxValue,1,20,12),out result,out reason),"overflowing geometry is rejected before pixel access");
        // Many gray antialias pixels must not outweigh recurring solid strokes.
        p=new int[w*h];for(int i=0;i<p.Length;i++)p[i]=Color.White.ToArgb();
        for(int x=r.Left+3;x<r.Right-3;x+=9)for(int y=r.Top+1;y<r.Bottom-1;y++){p[y*w+x]=Color.Gray.ToArgb();p[y*w+x+1]=Color.Gray.ToArgb();p[y*w+x+2]=Color.Black.ToArgb();}
        Check(TextSurfaceReconstruction.TryReconstruct(p,w,h,r,new List<Rectangle>{r},out result,out reason)&&result.Ink==Color.Black.ToArgb(),"repeated opaque strokes outrank more numerous antialias fringes");
        p=new int[w*h];for(int i=0;i<p.Length;i++)p[i]=Color.FromArgb(50,55,60).ToArgb();
        for(int x=r.Left+2;x<r.Right-5;x+=5)for(int y=r.Top+1;y<r.Bottom-1;y++)p[y*w+x]=p[y*w+x+1]=Color.White.ToArgb();
        Check(Local(p,w,h,r,out result,out reason),"closely spaced glyphs do not count unchanged dilated whitespace as erased ink");
        same=true;foreach(int pixel in result.Pixels)same&=pixel==Color.FromArgb(50,55,60).ToArgb();
        Check(same&&result.MaskedPixels*100<=((r.Width+2)*(r.Height+2))*45,"only corroborated foreground is changed and the original 45 percent limit still holds");
        p=new int[w*h];for(int i=0;i<p.Length;i++)p[i]=Color.FromArgb(50,55,60).ToArgb();
        for(int x=r.Left+1;x<r.Right-3;x+=6)for(int y=r.Top+1;y<r.Bottom-1;y++){p[y*w+x]=p[y*w+x+1]=Color.White.ToArgb();p[y*w+x-1]=p[y*w+x+2]=Color.FromArgb(195,195,195).ToArgb();}
        Check(!Local(p,w,h,r,out result,out reason)&&reason=="glyph-dense-mask","actual foreground and antialias coverage above 45 percent is still refused");
        return count;
    }
}
