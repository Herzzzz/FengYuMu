using System;
using System.Collections.Generic;
using System.Drawing;
using MapleOverlay;
public static class FusionSurfaceHarness
{
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 static int[] Frame(int w,int h,bool gradient){var p=new int[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)p[y*w+x]=gradient?Color.FromArgb(65+y/4,85+y/5,105+y/6).ToArgb():Color.FromArgb(225,225,225).ToArgb();return p;}
 static void Glyphs(int[] p,int width,Rectangle row,int ink){for(int x=row.Left+3;x<row.Right-3;x+=9)for(int y=row.Top+1;y<row.Bottom-1;y++){p[y*width+x]=ink;p[y*width+x+1]=ink;}}
 static bool Near(int a,int b){return Math.Abs(((a>>16)&255)-((b>>16)&255))<=3&&Math.Abs(((a>>8)&255)-((b>>8)&255))<=3&&Math.Abs((a&255)-(b&255))<=3;}
 public static int Run()
 {
  count=0;int w=200,h=100;Rectangle source=new Rectangle(20,30,120,12);var rows=new List<Rectangle>{source};ReconstructedTextSurface surface;string reason;
  var truth=Frame(w,h,false);var pixels=(int[])truth.Clone();Glyphs(pixels,w,source,Color.Black.ToArgb());var preserved=(int[])pixels.Clone();
  Check(TextSurfaceReconstruction.TryReconstruct(pixels,w,h,source,rows,out surface,out reason),"flat panel text is reconstructed");
  bool removed=true;for(int y=source.Top;y<source.Bottom;y++)for(int x=source.Left;x<source.Right;x++)removed&=Near(surface.Pixels[(y-surface.Region.Top)*surface.Region.Width+x-surface.Region.Left],truth[y*w+x]);
  Check(removed&&surface.MaskedPixels>0,"glyph strokes and their neighborhood return to supported background");
  Check(surface.Ink==Color.Black.ToArgb(),"dominant original ink is retained without averaging into panel color");
  bool same=true;for(int i=0;i<pixels.Length;i++)same&=pixels[i]==preserved[i];Check(same,"immutable source frame is never modified");
  truth=Frame(w,h,true);pixels=(int[])truth.Clone();Glyphs(pixels,w,source,Color.White.ToArgb());
  Check(TextSurfaceReconstruction.TryReconstruct(pixels,w,h,source,rows,out surface,out reason),"smooth gradient panel qualifies");
  removed=true;for(int y=source.Top;y<source.Bottom;y++)for(int x=source.Left;x<source.Right;x++)removed&=Near(surface.Pixels[(y-surface.Region.Top)*surface.Region.Width+x-surface.Region.Left],truth[y*w+x]);
  Check(removed&&surface.Ink==Color.White.ToArgb(),"gradient is reconstructed locally and white ink preserved");
  var multi=new Rectangle(20,20,120,46);rows=new List<Rectangle>{new Rectangle(20,20,120,12),new Rectangle(20,54,120,12)};pixels=Frame(w,h,false);foreach(var row in rows)Glyphs(pixels,w,row,Color.FromArgb(20,40,180).ToArgb());
  Check(TextSurfaceReconstruction.TryReconstruct(pixels,w,h,multi,rows,out surface,out reason),"wrapped body retains independent source rows");
  same=true;for(int y=36;y<50;y++)for(int x=surface.Region.Left;x<surface.Region.Right;x++)same&=surface.Pixels[(y-surface.Region.Top)*surface.Region.Width+x-surface.Region.Left]==pixels[y*w+x];Check(same,"gaps between source rows are untouched");
  rows=new List<Rectangle>{source};pixels=Frame(w,h,false);for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=(x%2==0?Color.Black:Color.White).ToArgb();
  Check(!TextSurfaceReconstruction.TryReconstruct(pixels,w,h,source,rows,out surface,out reason)&&reason=="unsupported-texture","high frequency texture is refused rather than flattened");
  pixels=Frame(w,h,false);Check(!TextSurfaceReconstruction.TryReconstruct(pixels,w,h,source,rows,out surface,out reason)&&reason=="no-supported-ink","blank OCR region cannot erase the game");
  for(int y=source.Top;y<source.Bottom;y++)for(int x=source.Left;x<source.Right;x++)pixels[y*w+x]=Color.Black.ToArgb();
  Check(!TextSurfaceReconstruction.TryReconstruct(pixels,w,h,source,rows,out surface,out reason)&&reason=="ambiguous-foreground","dense icon or filled surface is refused");
  Check(!TextSurfaceReconstruction.TryReconstruct(pixels,w,h,new Rectangle(1,1,20,10),rows,out surface,out reason),"missing clean boundary support is refused");
  Check(!TextSurfaceReconstruction.TryReconstruct(pixels,w,h,new Rectangle(Int32.MaxValue,20,100,10),rows,out surface,out reason),"overflowing coordinates fail closed");
  Check(!TextSurfaceReconstruction.TryReconstruct(new int[4],w,h,source,rows,out surface,out reason),"mismatched capture dimensions fail closed");
  pixels=Frame(w,h,false);Glyphs(pixels,w,source,Color.Black.ToArgb());for(int i=0;i<pixels.Length;i++)pixels[i]&=0xFFFFFF;
  bool opaque=TextSurfaceReconstruction.TryReconstruct(pixels,w,h,source,rows,out surface,out reason);if(opaque)foreach(int pixel in surface.Pixels)opaque&=((uint)pixel>>24)==255;
  Check(opaque,"undefined capture alpha never creates transparent repair holes");
  return count;
 }
}
