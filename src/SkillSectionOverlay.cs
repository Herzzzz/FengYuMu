using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        private static string SkillSectionMarker(string text)
        {
            string key=Regex.Replace(TranslationStore.Normalize(text),@"\bleve[l1i]\s*(?=\d)","level ");
            if(Regex.IsMatch(key,@"^master\s*level\b"))return "master";
            if(Regex.IsMatch(key,@"^current\s*level\b"))return "current";
            if(Regex.IsMatch(key,@"^next\s*level\b"))return "next";
            return "";
        }
        private static List<OcrLine> SkillVisualLines(List<OcrLine> source)
        {
            List<OcrLine> result=new List<OcrLine>();
            foreach(OcrLine line in source) {
                List<OcrWord> sorted=new List<OcrWord>(line.Words);
                sorted.Sort(delegate(OcrWord a,OcrWord b){int y=a.BoundingRect.Top.CompareTo(b.BoundingRect.Top);return Math.Abs(a.BoundingRect.Top-b.BoundingRect.Top)<=Math.Max(a.BoundingRect.Height,b.BoundingRect.Height)*.45f?a.BoundingRect.Left.CompareTo(b.BoundingRect.Left):y;});
                List<OcrLine> rows=new List<OcrLine>();
                foreach(OcrWord word in sorted) {
                    OcrLine found=null;
                    foreach(OcrLine row in rows){RectangleF r=GetOcrLineBounds(row),b=word.BoundingRect;
                        if(Math.Abs(r.Top+r.Height/2-b.Top-b.Height/2)<=Math.Max(r.Height,b.Height)*.45f){found=row;break;}}
                    if(found==null){found=new OcrLine();rows.Add(found);}found.Words.Add(word);
                }
                if(rows.Count<=1){result.Add(line);continue;}
                foreach(OcrLine row in rows){StringBuilder text=new StringBuilder();foreach(OcrWord word in row.Words){if(text.Length>0)text.Append(' ');text.Append(word.Text);}row.Text=text.ToString();result.Add(row);}
            }
            result.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});return result;
        }
        private string ResolveSkillPanelId(List<OcrLine> lines)
        {
            List<OcrLine> sorted=SkillVisualLines(lines);float master=Single.MaxValue;
            foreach(OcrLine line in sorted)if(SkillSectionMarker(line.Text)=="master"){master=GetOcrLineBounds(line).Top;break;}
            string selected="";float nearest=Single.MaxValue;
            foreach(OcrLine line in sorted) {
                RectangleF r=GetOcrLineBounds(line);if(r.Bottom>master||master-r.Bottom>160||line.Text.Length>65)continue;
                string id=translations.ResolveSkillTitle(line.Text);if(id.Length==0)continue;
                float distance=master-r.Bottom;if(distance<nearest){selected=id;nearest=distance;}
            }
            if(selected.Length>0)return selected;
            StringBuilder prose=new StringBuilder();bool began=master==Single.MaxValue;
            foreach(OcrLine line in sorted){string marker=SkillSectionMarker(line.Text);if(marker=="current"||marker=="next")break;
                if(marker=="master")began=true;if(!began)continue;
                if(prose.Length>0)prose.Append(' ');prose.Append(line.Text);}
            return prose.Length>=24?translations.DetectSkillContentId(prose.ToString()):"";
        }
        private string ResolveSkillContextId(List<OcrLine> all,OcrLine marker,float scale)
        {
            OcrLine master=SkillSectionMarker(marker.Text)=="master"?marker:null;RectangleF at=GetOcrLineBounds(marker);
            foreach(OcrLine line in all)if(SkillSectionMarker(line.Text)=="master") {
                RectangleF r=GetOcrLineBounds(line);if(r.Top>at.Top||at.Top-r.Top>600*scale||r.Left>at.Right+240*scale||r.Right<at.Left-40*scale)continue;
                if(master==null||r.Top>GetOcrLineBounds(master).Top)master=line;
            }
            if(master==null)return "";RectangleF header=GetOcrLineBounds(master);string best="";float gap=Single.MaxValue;
            foreach(OcrLine line in all){RectangleF r=GetOcrLineBounds(line);
                if(r.Bottom>header.Top||header.Top-r.Bottom>160*scale||r.Left<header.Left-240*scale||r.Left>header.Right+40*scale)continue;
                string id=translations.ResolveSkillTitle(line.Text);if(id.Length>0&&header.Top-r.Bottom<gap){best=id;gap=header.Top-r.Bottom;}}
            return best;
        }
        private static bool RulePixel(int color)
        {int r=(color>>16)&255,g=(color>>8)&255,b=color&255;return Math.Min(r,Math.Min(g,b))>=140&&Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<45;}
        private static RectangleF SkillOverviewSlot(int[] pixels,int width,int height,RectangleF master,RectangleF level,float scale)
        {
            if(pixels==null||level.IsEmpty||level.Top-master.Bottom<18*scale)return RectangleF.Empty;
            for(int y=Math.Min(height-5,(int)(level.Top-3*scale));y>=Math.Max(4,(int)Math.Max(master.Bottom+15*scale,level.Top-65*scale));y--) {
                int x=Math.Max(0,Math.Min(width-1,(int)master.Left));if(!RulePixel(pixels[y*width+x]))continue;
                int left=x,right=x;while(left>0&&RulePixel(pixels[y*width+left-1]))left--;while(right+1<width&&RulePixel(pixels[y*width+right+1]))right++;
                if(right-left<200*scale||left>level.Left+10*scale||right<master.Right)continue;
                int dark=0,total=0;for(int sample=left+4;sample<right-4;sample+=4){total++;if(!RulePixel(pixels[(y-3)*width+sample])&&!RulePixel(pixels[(y+3)*width+sample]))dark++;}
                if(total==0||dark*4<total*3)continue;
                return RectangleF.FromLTRB(master.Left,master.Bottom+3*scale,right-3*scale,y-3*scale);
            }
            return RectangleF.Empty;
        }
        private void AddSkillOverviewSections(List<OverlayLabel> output,List<OcrLine> all,Dictionary<OcrLine,OcrPanelInfo> panels,Bitmap prepared,float scale,HashSet<OcrPanelInfo> handled=null)
        {
            int observed=0,planned=0;int[] pixels=null;
            foreach(OcrLine master in all) {
                if(SkillSectionMarker(master.Text)!="master")continue;observed++;
                OcrPanelInfo owner;if(handled!=null&&panels.TryGetValue(master,out owner)&&handled.Contains(owner))continue;
                if(pixels==null&&prepared!=null)pixels=ReadSimplePixels(prepared);
                RectangleF header=GetOcrLineBounds(master);OcrLine level=null;float levelTop=Single.MaxValue;
                foreach(OcrLine line in all){string marker=SkillSectionMarker(line.Text);RectangleF r=GetOcrLineBounds(line);
                    if((marker=="current"||marker=="next")&&r.Top>header.Bottom&&r.Top<header.Bottom+600*scale&&r.Top<levelTop&&r.Left>=header.Left-240*scale&&r.Left<header.Right+80*scale){level=line;levelTop=r.Top;}}
                string id=ResolveSkillContextId(all,master,scale);OcrPanelInfo parent;
                if(id.Length==0&&panels.TryGetValue(master,out parent))id=parent.SkillId;
                RectangleF slot=prepared==null?RectangleF.Empty:SkillOverviewSlot(pixels,prepared.Width,prepared.Height,header,level==null?RectangleF.Empty:GetOcrLineBounds(level),scale);
                List<OcrLine> overview=new List<OcrLine>();
                foreach(OcrLine line in all){if(line==master)continue;RectangleF r=GetOcrLineBounds(line);string key=TranslationStore.Normalize(line.Text);
                    if(r.Top<header.Bottom||r.Bottom>=levelTop||r.Left<header.Left-8*scale||r.Left>header.Right+400*scale)continue;
                    if(key.StartsWith("required skill",StringComparison.Ordinal)){if(!slot.IsEmpty&&r.Top>slot.Top)slot.Height=Math.Max(0,r.Top-slot.Top-3*scale);continue;}
                    if(SkillSectionMarker(line.Text).Length>0)continue;
                    if(slot.IsEmpty||slot.Contains(new PointF(r.Left+r.Width/2,r.Top+r.Height/2)))overview.Add(line);
                }
                overview.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
                string chinese;string english=CombineVisualRow(overview);
                bool matched=translations.TryTranslateSkillOverview(english,id,out chinese);
                if(!matched&&!slot.IsEmpty&&id.Length>0&&TranslationStore.Normalize(english).Length<24&&!Regex.IsMatch(english,@"\d"))matched=translations.TryGetSkillOverviewBody(id,out chinese);
                if(!matched)continue;
                RectangleF raw=!slot.IsEmpty?slot:GetCombinedLineBounds(overview);if(raw.Width<20||raw.Height<10)continue;RectangleF owned=OverlaySpan(raw,scale);
                output.RemoveAll(delegate(OverlayLabel label){return label.SemanticRole=="skill-body"&&owned.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                AddDetailBlockLabelAt(output,slot.IsEmpty?overview:null,raw,chinese,scale,true);
                OverlayLabel added=output[output.Count-1];added.SemanticRole="skill-body";added.MatchSource=slot.IsEmpty?"skill-overview-section":"skill-overview-slot";
                if(!slot.IsEmpty){added.SourceLineBounds=new List<RectangleF>();
                    float step=Math.Max(14,Math.Min(20,header.Height/scale));
                    int count=Math.Max(1,(int)Math.Ceiling(owned.Height/step));
                    for(int row=0;row<count;row++)added.SourceLineBounds.Add(RectangleF.FromLTRB(owned.Left,owned.Top+owned.Height*row/count,owned.Right,owned.Top+owned.Height*(row+1)/count));
                    added.PanelBounds=owned;}
                added.Confidence=LabelConfidence.High;planned++;
            }
            Program.LogRuntimeEvent("skill-overview-coverage","observed="+observed+" planned="+planned);
        }
    }
}
