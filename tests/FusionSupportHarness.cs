using System;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using MapleOverlay;
public static class FusionSupportHarness
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
    static int[] Frame(int w, int h) { int[] p = new int[w*h]; for (int i=0;i<p.Length;i++) p[i]=Color.FromArgb(220,225,230).ToArgb(); return p; }
    static void Fill(int[] p,int w,Rectangle r) { for(int y=r.Top;y<r.Bottom;y++) for(int x=r.Left;x<r.Right;x++) p[y*w+x]=Color.Black.ToArgb(); }
    static bool Reconstruct(int[] p,int w,int h,Rectangle row,List<Rectangle> protectedRows,out ReconstructedTextSurface result,out string reason)
    {
        MethodInfo method=typeof(TextSurfaceReconstruction).GetMethod("TryReconstruct",BindingFlags.Static|BindingFlags.NonPublic);
        object[] args=method.GetParameters().Length==8 ? new object[]{p,w,h,row,new List<Rectangle>{row},null,null,protectedRows} : new object[]{p,w,h,row,new List<Rectangle>{row},null,null};
        bool ok=(bool)method.Invoke(null,args);result=(ReconstructedTextSurface)args[5];reason=(string)args[6];return ok;
    }
    public static int Run()
    {
        int w=180,h=90; Rectangle row=new Rectangle(20,30,120,12), neighbor=new Rectangle(19,26,122,3);
        int[] p=Frame(w,h); for(int x=24;x<136;x+=9) Fill(p,w,new Rectangle(x,31,2,10)); Fill(p,w,neighbor);
        int[] original=(int[])p.Clone(); ReconstructedTextSurface result; string reason;
        bool ok=Reconstruct(p,w,h,row,new List<Rectangle>{row,neighbor},out result,out reason);
        Check(ok,"neighboring OCR text does not poison clean support bands");
        bool clean=true; for(int y=row.Top;y<row.Bottom;y++) for(int x=row.Left;x<row.Right;x++) clean &= result.Pixels[(y-result.Region.Top)*result.Region.Width+x-result.Region.Left]==Color.FromArgb(220,225,230).ToArgb();
        Check(clean,"repaired glyphs return to the corroborated panel background");
        bool unchanged=true; for(int i=0;i<p.Length;i++) unchanged &= p[i]==original[i];
        Check(unchanged,"support search never mutates captured pixels");
        unchanged=true; for(int y=result.Region.Top;y<result.Region.Bottom;y++) for(int x=result.Region.Left;x<result.Region.Right;x++)
            if(y<row.Top-1||y>row.Bottom) unchanged &= result.Pixels[(y-result.Region.Top)*result.Region.Width+x-result.Region.Left]==p[y*w+x];
        Check(unchanged,"neighboring text and counters inside the patch remain byte exact");
        Check(!Reconstruct(p,w,h,row,new List<Rectangle>{row},out result,out reason)&&reason=="unsupported-texture","unexplained panel edge cannot be bypassed by distant smooth samples");
        p=Frame(w,h);for(int x=24;x<136;x+=9)Fill(p,w,new Rectangle(x,31,2,10));neighbor=new Rectangle(19,43,122,3);Fill(p,w,neighbor);
        Check(Reconstruct(p,w,h,row,new List<Rectangle>{row,neighbor},out result,out reason),"lower neighboring OCR text also uses independent clean support");
        var blocked=new List<Rectangle>{row,new Rectangle(19,20,122,10)};
        Check(!Reconstruct(p,w,h,row,blocked,out result,out reason),"absence of clean support still refuses reconstruction");
        return count;
    }
}
