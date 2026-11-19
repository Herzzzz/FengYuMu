using System;
using System.Collections.Generic;
using System.Drawing;
using MapleOverlay;
public static class FusionTextSpaceHarness
{
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 static int[] Frame(){var p=new int[160*80];for(int i=0;i<p.Length;i++)p[i]=Color.FromArgb(245,245,245).ToArgb();return p;}
 static ReconstructedTextSurface Surface(){var p=new int[30*18];for(int i=0;i<p.Length;i++)p[i]=Color.FromArgb(245,245,245).ToArgb();p[4]=Color.Black.ToArgb();return new ReconstructedTextSurface{Region=new Rectangle(20,20,30,18),Pixels=p,SourceHeight=12,Ink=Color.Black.ToArgb(),MaskedPixels=7};}
 static bool Expand(ReconstructedTextSurface s,int[] p,int width,List<Rectangle> guards,out ReconstructedTextSurface result,out string reason,int budget=3000){return FusionTextSpace.TryExpandRight(s,p,160,80,width,256,budget,guards,out result,out reason);}
 public static int Run(){count=0;var frame=Frame();var saved=(int[])frame.Clone();var source=Surface();var old=(int[])source.Pixels.Clone();ReconstructedTextSurface wider;string reason;
  Check(Expand(source,frame,70,null,out wider,out reason)&&wider.Region==new Rectangle(20,20,70,18),"flat blank space can fit an independently measured wider translation");
  bool same=true;for(int i=0;i<frame.Length;i++)same&=frame[i]==saved[i];for(int i=0;i<old.Length;i++)same&=source.Pixels[i]==old[i];Check(same,"capture and original reconstructed surface are immutable");
  same=true;for(int y=0;y<18;y++)for(int x=0;x<30;x++)same&=wider.Pixels[y*70+x]==old[y*30+x];Check(same,"repaired source pixels are copied with their original stride");
  Check(wider.SourceHeight==12&&wider.Ink==source.Ink&&wider.MaskedPixels==7,"font height ink and erase mask are not changed");
  Check(!Expand(source,frame,70,new List<Rectangle>{new Rectangle(64,20,15,18)},out wider,out reason)&&reason=="protected-space","live values block extension even on apparently blank pixels");
  frame=Frame();frame[30*160+65]=Color.Black.ToArgb();Check(!Expand(source,frame,70,null,out wider,out reason)&&reason=="space-has-content","unrecognized text is protected by pixel evidence");
  frame=Frame();frame[20*160+49]=Color.Black.ToArgb();Check(!Expand(source,frame,70,null,out wider,out reason)&&reason=="unsupported-space-edge","contaminated edge cannot serve as clean background");
  frame=Frame();Check(!Expand(source,frame,70,null,out wider,out reason,719)&&reason=="local-work-budget","additional drawing pixels remain inside the original total work budget");
  Check(Expand(source,frame,70,null,out wider,out reason,720),"exact remaining pixel budget is accounted for");
  Check(!Expand(source,frame,200,null,out wider,out reason),"extension cannot leave the captured game frame");
  Check(!Expand(source,frame,int.MaxValue,null,out wider,out reason),"overflowing requested width fails before allocation");
  Check(!Expand(source,frame,30,null,out wider,out reason),"no-op width does not create another asset");
  Check(!FusionTextSpace.TryExpandRight(source,new int[2],160,80,70,256,3000,null,out wider,out reason),"mismatched capture dimensions are rejected");
  var broken=Surface();broken.Pixels=new int[2];Check(!Expand(broken,frame,70,null,out wider,out reason),"mismatched reconstructed stride is rejected");
  Check(!FusionTextSpace.TryExpandRight(source,frame,160,80,70,39,3000,null,out wider,out reason),"bounded extension allowance is enforced");
  frame=Frame();for(int i=0;i<frame.Length;i++)frame[i]&=0xffffff;Check(Expand(source,frame,70,null,out wider,out reason)&&((uint)wider.Pixels[69]>>24)==255,"undefined frame alpha cannot produce transparent holes");
  return count;
 }
}
