using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using MapleOverlay;

public static class RecoveryBudgetInpaintHarness
{
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
    static int[] Background(bool light){var p=new int[180*90];for(int i=0;i<p.Length;i++)p[i]=light?Color.White.ToArgb():Color.FromArgb(50,55,60).ToArgb();return p;}
    static void Glyphs(int[] p,int color){for(int x=23;x<135;x+=10)for(int y=31;y<41;y++)p[y*180+x]=p[y*180+x+1]=color;}
    static bool Repair(int[] p,Rectangle r,IList<Rectangle> guard,out ReconstructedTextSurface s)
    {string reason;return GlyphMaskInpainting.TryReconstruct(p,180,90,r,new List<Rectangle>{r},guard,out s,out reason);}
    public static int Run()
    {
        count=0;
        Check(SupplementalOcrReview.CanStart(false,1499,450),"short manual gate extends to 1500 on original clock");
        Check(!SupplementalOcrReview.CanStart(false,1500,450),"1500 gate cannot start at its boundary");
        Check(SupplementalOcrReview.CanStart(false,1499,780),"hover gate shares manual minimum");
        Check(SupplementalOcrReview.CanStart(false,1499,1320),"equipment gate shares manual minimum");
        Check(SupplementalOcrReview.CanStart(false,1749,1750),"existing longer panel gate is preserved");
        Check(!SupplementalOcrReview.CanStart(false,1750,1750),"panel end is not increased");
        Check(!SupplementalOcrReview.CanStart(false,2500,1750),"slow earlier work cannot restart the clock");
        Check(SupplementalOcrReview.CanStart(true,449,450),"automatic original gate permits before deadline");
        Check(!SupplementalOcrReview.CanStart(true,450,450),"automatic original gate unchanged at deadline");
        Check(!SupplementalOcrReview.CanStart(false,-1,450),"negative cumulative time rejected");
        Check(!SupplementalOcrReview.CanStart(false,0,0),"invalid gate rejected");
        Check(!SupplementalOcrReview.CanStart(false,4500,780),"no independent five second manual window remains");
        long clock=1499;int passes=0;
        bool ok=SupplementalOcrReview.RunWholeFrameAsync(false,()=>clock,()=>true,()=>false,
            ()=>{passes++;clock=1500;return Task.FromResult(true);},
            ()=>{passes++;return Task.FromResult(true);},(a,b)=>{}).GetAwaiter().GetResult();
        Check(ok&&passes==1,"completed contrast consumes the same budget before enlarged pass");
        clock=449;passes=0;
        ok=SupplementalOcrReview.RunWholeFrameAsync(true,()=>clock,()=>true,()=>false,
            ()=>{passes++;clock=500;return Task.FromResult(true);},
            ()=>{passes++;return Task.FromResult(true);},(a,b)=>{}).GetAwaiter().GetResult();
        Check(ok&&passes==2,"automatic passes retain distinct original cumulative deadlines");
        bool cancelled=false;passes=0;
        ok=SupplementalOcrReview.RunWholeFrameAsync(false,()=>0,()=>true,()=>cancelled,
            ()=>{passes++;cancelled=true;return Task.FromResult(true);},
            ()=>{passes++;return Task.FromResult(true);},(a,b)=>{}).GetAwaiter().GetResult();
        Check(!ok&&passes==1,"cancelled result stops subsequent review");
        passes=0;
        ok=SupplementalOcrReview.RunWholeFrameAsync(false,()=>0,()=>false,()=>false,
            ()=>{passes++;return Task.FromResult(true);},()=>{passes++;return Task.FromResult(true);},(a,b)=>{}).GetAwaiter().GetResult();
        Check(ok&&passes==0,"complete result does not invoke supplemental OCR");
        Rectangle r=new Rectangle(20,30,120,12);ReconstructedTextSurface surface;string reason;
        var p=Background(false);Glyphs(p,Color.White.ToArgb());var saved=(int[])p.Clone();
        Check(Repair(p,r,new List<Rectangle>{r},out surface),"sparse light glyphs repair against separated dark context");
        bool same=true;for(int i=0;i<p.Length;i++)same&=p[i]==saved[i];
        Check(same,"source frame remains immutable");
        same=true;for(int y=surface.Region.Top;y<surface.Region.Bottom;y++)for(int x=surface.Region.Left;x<surface.Region.Right;x++)
            if(saved[y*180+x]!=Color.White.ToArgb())same&=surface.Pixels[(y-surface.Region.Top)*surface.Region.Width+x-surface.Region.Left]==saved[y*180+x];
        Check(same,"all unmasked pixels remain byte exact");
        Check(surface.MaskedPixels>0&&surface.MaskedPixels*100<=(r.Width+2)*(r.Height+2)*45,"sparse mask obeys unchanged 45 percent limit");
        Check(surface.Ink==Color.White.ToArgb(),"opaque recurring glyphs determine white ink");
        p=Background(false);for(int y=0;y<90;y++)for(int x=0;x<180;x++)p[y*180+x]=Color.FromArgb(40+x/5,45+x/5,50+x/5).ToArgb();
        Glyphs(p,Color.White.ToArgb());Check(Repair(p,r,new List<Rectangle>{r},out surface),"bounded smooth gradient can support local inpainting");
        p=Background(true);Glyphs(p,Color.Black.ToArgb());
        Check(Repair(p,r,new List<Rectangle>{r},out surface)&&surface.Ink==Color.Black.ToArgb(),"dark glyphs require separated light context");
        Check(!GlyphMaskInpainting.TryReconstruct(null,180,90,r,new List<Rectangle>{r},null,out surface,out reason),"null frame rejected");
        Check(!GlyphMaskInpainting.TryReconstruct(new int[1],180,90,r,new List<Rectangle>{r},null,out surface,out reason),"incorrect frame length rejected");
        Check(!Repair(p,new Rectangle(Int32.MaxValue,30,120,12),null,out surface),"overflowing source geometry rejected before indexing");
        Check(!GlyphMaskInpainting.TryReconstruct(p,180,90,r,new List<Rectangle>(),null,out surface,out reason),"missing measured text rows rejected");
        Check(!Repair(p,new Rectangle(20,0,120,12),null,out surface),"missing context at frame boundary rejected");
        p=Background(false);for(int y=30;y<42;y++)for(int x=20;x<140;x++)p[y*180+x]=Color.White.ToArgb();
        Check(!Repair(p,r,null,out surface),"solid bright panel cannot masquerade as sparse glyphs");
        Check(!Repair(Background(false),r,null,out surface),"blank background is never erased");
        p=Background(false);Glyphs(p,Color.White.ToArgb());
        Check(!Repair(p,r,new List<Rectangle>{new Rectangle(0,0,180,90)},out surface),"protected context cannot seed repair");
        for(int y=0;y<90;y++)for(int x=0;x<180;x++)if(y<30||y>=42)p[y*180+x]=(x%2==0?Color.White:Color.Black).ToArgb();
        Check(!Repair(p,r,null,out surface),"unseparated checkerboard context is refused");
        return count;
    }
}
