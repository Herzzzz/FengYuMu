using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        private QuestJournalRecord rememberedJournal;
        private string rememberedJournalHeader="",rememberedJournalObjectives="";
        private Rectangle rememberedJournalCapture;
        private long rememberedJournalAt;

        private RectangleF ResolveQuestJournalOwner(List<OcrLine> all,Size size,float scale,RectangleF measured)
        {
            float split=0,bottom=size.Height;
            foreach(OcrLine line in all) {
                string key=TranslationStore.Normalize(line.Text);RectangleF raw=GetOcrLineBounds(line);
                if(key=="available"||key=="in progress"||key=="completed"||key=="details"||key=="detail"||
                    Regex.IsMatch(key,@"^(?:available\s+)?in\s*progress\s+completed$"))split=Math.Max(split,raw.Right+12*scale);
                // OCR may combine the tab strip into one line. Word geometry,
                // rather than an icon or Summary card, locates its right edge.
                foreach(OcrWord word in line.Words)if(TranslationStore.Normalize(word.Text)=="completed")
                    split=Math.Max(split,word.BoundingRect.Right+12*scale);
            }
            if(split<=0&&!measured.IsEmpty)split=measured.Left;
            if(split<=0||split>=size.Width-100*scale)return RectangleF.Empty;
            foreach(OcrLine line in all) {
                string key=TranslationStore.Normalize(line.Text);RectangleF raw=GetOcrLineBounds(line);
                if((key=="forfeit"||key=="quest helper")&&raw.Left>=split)bottom=Math.Min(bottom,raw.Top-3*scale);
            }
            // A summary card is not the scroll viewport. Its height and its caption
            // disappear during scrolling; neither is allowed to clip the body scope.
            return RectangleF.FromLTRB(split,0,size.Width,bottom);
        }

        private bool TryAddQuestJournal(List<OverlayLabel> output,List<OcrLine> detail,
            List<OcrLine> all,Bitmap prepared,float scale,bool questWindow,RectangleF measured)
        {
            if(prepared==null||scale<=0||all==null)return false;
            RectangleF owner=ResolveQuestJournalOwner(all,prepared.Size,scale,measured);
            if(owner.IsEmpty)return false;
            int[] pixels=ReadSimplePixels(prepared);
            List<OcrLine> sorted=all.FindAll(delegate(OcrLine line){RectangleF raw=GetOcrLineBounds(line);return owner.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2));});
            sorted.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
            OcrLine heading=null;string stage="",reason="no-measured-stage";
            Rectangle area=Rectangle.Empty;Color background=Color.Empty;QuestJournalRecord record=null;
            foreach(OcrLine line in sorted) {
                string resolved=translations.ResolveJournalStage(line.Text);if(resolved.Length==0)continue;
                Rectangle candidate;Color paper;
                if(!QuestJournalGeometry.Measure(pixels,prepared.Width,prepared.Height,GetOcrLineBounds(line),owner,scale,out candidate,out paper)||!JournalPaper(paper))continue;
                if(heading!=null&&TranslationStore.Normalize(stage)!=TranslationStore.Normalize(resolved)) {
                    Program.LogRuntimeEvent("quest-journal","selected=false reason=multiple-stage-headings");return false;
                }
                heading=line;stage=resolved;area=candidate;background=paper;
            }
            if(heading!=null)record=translations.SelectJournal(stage,JournalProse(sorted,GetOcrLineBounds(heading),area,false),out reason);
            if(heading==null) {
                // One general path for heading-less, scrolled and server-variant
                // journals. No quest name or quantity is encoded in this procedure.
                foreach(OcrLine line in sorted) {
                    string key=TranslationStore.Normalize(line.Text);
                    if(key.Length<5||key.StartsWith("level ",StringComparison.Ordinal)||QuestDetailGeometry.IsSummaryKey(key)||Regex.IsMatch(line.Text,@"\d+\s*/\s*\d+"))continue;
                    Rectangle candidate;Color paper;
                    if(!QuestJournalGeometry.Measure(pixels,prepared.Width,prepared.Height,GetOcrLineBounds(line),owner,scale,out candidate,out paper)||!JournalPaper(paper))continue;
                    heading=line;area=candidate;background=paper;break;
                }
                if(heading!=null) {
                    string prose=JournalProse(sorted,GetOcrLineBounds(heading),area,true);
                    foreach(OcrLine line in sorted) {
                        if(GetOcrLineBounds(line).Bottom>=area.Top)continue;
                        string caption=translations.ResolveJournalCaption(line.Text);if(caption.Length==0)continue;
                        record=translations.SelectJournal(caption,prose,out reason);if(record!=null)break;
                    }
                    if(record==null)record=translations.SelectJournalForProse(prose,out reason);
                }
            }
            if(heading==null){Program.LogRuntimeEvent("quest-journal","selected=false reason=no-paper-viewport");return false;}
            string header=JournalHeader(sorted,pixels,prepared.Width,prepared.Height,area);
            string objectives=JournalObjectiveSignature(sorted,pixels,prepared.Width,prepared.Height,owner,scale,area.Top);
            if(record==null&&stage.Length==0&&rememberedJournal!=null&&header.Length>0&&header==rememberedJournalHeader&&
                objectives==rememberedJournalObjectives&&captureBounds==rememberedJournalCapture&&
                (Stopwatch.GetTimestamp()-rememberedJournalAt)/(double)Stopwatch.Frequency<600&&
                translations.JournalMemoryMatches(rememberedJournal,JournalProse(sorted,GetOcrLineBounds(heading),area,true)))
            {record=rememberedJournal;reason="same-task-scroll-context";}
            if(record==null){Program.LogRuntimeEvent("quest-journal","selected=false reason="+reason);return false;}
            RectangleF overlay=OverlaySpan(area,scale);
            output.RemoveAll(delegate(OverlayLabel label){return overlay.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
            output.Add(new OverlayLabel{Bounds=overlay,SourceBounds=overlay,HasSourceBounds=true,PanelBounds=overlay,
                Text=(String.IsNullOrEmpty(record.ChineseTitle)?"":record.ChineseTitle+"\n\n")+record.Chinese,Wrap=true,Confidence=LabelConfidence.High,SemanticRole="task-journal",
                MatchSource="stage-journal",Journal=new QuestJournalDocument{Title=record.ChineseTitle,Body=record.Chinese,Background=background,
                    FontHeight=Math.Max(14,Math.Min(20,GetOcrLineBounds(heading).Height/scale))}});
            if(header.Length>0){rememberedJournal=record;rememberedJournalHeader=header;rememberedJournalObjectives=objectives;rememberedJournalCapture=captureBounds;rememberedJournalAt=Stopwatch.GetTimestamp();}
            Program.LogRuntimeEvent("quest-journal","selected=true reason="+reason+" canonical="+(record.TaskId.Length>0)+" width="+area.Width+" height="+area.Height);
            return true;
        }
        private static bool JournalPaper(Color color)
        {return color.R>=245&&color.G>=245&&color.B>=240;}
        private static string JournalHeader(List<OcrLine> lines,int[] pixels,int width,int height,Rectangle body)
        {
            StringBuilder result=new StringBuilder();
            foreach(OcrLine line in lines) {
                RectangleF r=GetOcrLineBounds(line);string key=TranslationStore.Normalize(line.Text);
                if(r.Bottom>=body.Top||r.Left<body.Left||r.Left>body.Right||key.StartsWith("level ",StringComparison.Ordinal))continue;
                int blue=0,total=0;
                for(int i=0;i<12;i++) {
                    int x=Math.Max(0,Math.Min(width-1,(int)(r.Left+r.Width*i/12))),y=Math.Max(0,Math.Min(height-1,(int)r.Top-2));
                    int color=pixels[y*width+x],red=(color>>16)&255,green=(color>>8)&255,b=color&255;
                    total++;if(red<145&&green>red+15&&b>red+30)blue++;
                }
                if(blue*2>=total)result.Append(key).Append('|');
            }
            return result.ToString();
        }
        private static string JournalObjectiveSignature(List<OcrLine> lines,int[] pixels,int width,int height,RectangleF owner,float scale,int bodyTop)
        {
            List<RectangleF> cards=QuestObjectiveSurface.Find(pixels,width,height,owner,scale);List<string> values=new List<string>();
            foreach(OcrLine line in lines) {
                RectangleF raw=GetOcrLineBounds(line);if(raw.Bottom>bodyTop)continue;
                bool inside=false;foreach(RectangleF card in cards)if(card.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2))){inside=true;break;}
                if(!inside)continue;Match counter=QuestObjectiveSurface.Counter(line.Text);if(!counter.Success)continue;
                string required=Regex.Match(counter.Value,@"[0-9IlOo]+\s*$").Value.Trim();
                string key=TranslationStore.Normalize(line.Text.Substring(0,counter.Index))+"/"+required;values.Add(key);
            }
            values.Sort(StringComparer.Ordinal);return String.Join("|",values.ToArray());
        }
        private bool JournalOwnsLine(RectangleF owned,OcrLine line,float scale)
        {RectangleF raw=OverlaySpan(GetOcrLineBounds(line),scale);return owned.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2));}
        private static string JournalProse(List<OcrLine> lines,RectangleF heading,Rectangle area,bool includeHeading)
        {
            StringBuilder result=new StringBuilder();foreach(OcrLine line in lines){RectangleF raw=GetOcrLineBounds(line);
                if((includeHeading||raw!=heading)&&area.Contains(Rectangle.Ceiling(raw)))result.Append(line.Text).Append(' ');}
            return result.ToString();
        }
    }
}
