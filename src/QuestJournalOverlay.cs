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
        private string lastQuestJournalRejection="";

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
            if(split<=0) {
                // Tab captions are the primary split witness, but OCR often loses them
                // while the body still shows "Quest Summary". That row lives inside the
                // detail column, so its left edge is a safe fallback: without it the
                // owner collapsed to empty and every frame fell back with measured=False.
                foreach(OcrLine line in all) {
                    RectangleF raw=GetOcrLineBounds(line);
                    if(raw.IsEmpty)continue;
                    if(!QuestDetailGeometry.IsSummaryKey(TranslationStore.Normalize(line.Text)))continue;
                    split=Math.Max(split,raw.Left-8*scale);
                    break;
                }
            }
            if(split<=0&&!measured.IsEmpty)split=measured.Left;
            // Tabs, summary row and measured box can all be missing on one frame. A fixed
            // 40% column then landed inside the task list and the body painted over it.
            // Use the left edge of the longest OCR row in the right half instead: body
            // prose is the longest text on the panel.
            if(split<=0) {
                int bestLength=0;float bestLeft=0f;
                foreach(OcrLine line in all) {
                    string text=(line.Text??"").Trim();
                    if(text.Length<30||text.Length>240)continue;
                    RectangleF raw=GetOcrLineBounds(line);
                    if(raw.IsEmpty||raw.Left<size.Width*0.42f)continue;
                    if(text.Length>bestLength){bestLength=text.Length;bestLeft=raw.Left;}
                }
                if(bestLeft>0f)split=Math.Max(size.Width*0.42f,bestLeft-8*scale);
            }
            if(split<=0)split=size.Width*0.55f;
            if(split<0||split>=size.Width-100*scale)return RectangleF.Empty;
            foreach(OcrLine line in all) {
                string key=TranslationStore.Normalize(line.Text);RectangleF raw=GetOcrLineBounds(line);
                // Button rows define the bottom edge of the body viewport. Recognising only
                // the exact words "forfeit"/"quest helper" let an OCR variant through, the
                // edge stayed at the window bottom, and the button text was then counted as
                // body prose (visible-prose-order-or-content:help).
                // Only rows in the lower half of the frame may clip the viewport: the
                // standalone "Quest Helper (2/5)" HUD window also matches this prefix and
                // sits near the top of the screen. Treating it as the button bar collapsed
                // the owner to a strip above the body and every frame reported
                // no-paper-viewport.
                if(IsJournalChromeRow(key)&&raw.Left>=split&&raw.Top>=size.Height*0.55f)
                    bottom=Math.Min(bottom,raw.Top-3*scale);
            }
            // A "QUEST HELPER" + "FORFEIT" pair sitting side by side is the detail panel's
            // button bar (confirmed in UI.wz: Quest.img/Main/questInfo). Everything above the
            // bar is body; the bar itself is chrome. This is the strongest anchor because both
            // words appear together only on the detail panel.
            OcrLine helperButton=null,forfeitButton=null;
            foreach(OcrLine line in all) {
                string compact=(line.Text??"").Replace(" ","").ToLowerInvariant();
                if(compact.StartsWith("questhel",StringComparison.Ordinal))helperButton=line;
                else if(compact.StartsWith("forfeit",StringComparison.Ordinal))forfeitButton=line;
            }
            if(helperButton!=null&&forfeitButton!=null) {
                RectangleF hb=GetOcrLineBounds(helperButton),fb=GetOcrLineBounds(forfeitButton);
                // Scale-relative tolerance: at 125%/150% UI scale the two buttons sit a few
                // pixels apart vertically, and a fixed 14px window misjudged the pair.
                float tolerance=Math.Max(10f,(Math.Max(hb.Height,fb.Height))*0.75f);
                if(Math.Abs(hb.Top-fb.Top)<=tolerance&&fb.Left>hb.Right) {
                    float barTop=Math.Min(hb.Top,fb.Top)-4f*scale;
                    if(barTop>0f)bottom=Math.Min(bottom,barTop);
                }
            }
            // A summary card is not the scroll viewport. Its height and its caption
            // disappear during scrolling; neither is allowed to clip the body scope.
            return RectangleF.FromLTRB(split,0,size.Width,bottom);
        }

        private bool TryAddQuestJournal(List<OverlayLabel> output,List<OcrLine> detail,
            List<OcrLine> all,Bitmap prepared,float scale,bool questWindow,RectangleF measured)
        {
            if(prepared==null||scale<=0||all==null)return false;
            lastQuestJournalRejection="";
            RectangleF owner=ResolveQuestJournalOwner(all,prepared.Size,scale,measured);
            if(owner.IsEmpty)return false;
            if(!measured.IsEmpty&&OverlayPaintSafety.IsFiniteRect(measured)) {
                RectangleF constrained=RectangleF.Intersect(owner,measured);
                if(constrained.Width>=200*scale&&constrained.Height>=120*scale)owner=constrained;
            }
            int[] pixels=ReadSimplePixels(prepared);
            Rectangle ownerRect=Rectangle.Ceiling(owner);
            // Hard colour contract: the body may never start above the grey requirement
            // card or the blue header strip. Resolve the floor before any heading or
            // record decision so every downstream path inherits it.
            int cardBandBottom=QuestJournalLayout.FindBandBottom(pixels,prepared.Width,prepared.Height,ownerRect,true);
            int blueBandBottom=QuestJournalLayout.FindBandBottom(pixels,prepared.Width,prepared.Height,ownerRect,false);
            int colorFloor=Math.Max(cardBandBottom,blueBandBottom);
            // The scan runs over the whole owner, which can include the chat bar or the
            // quick-slot strip below the quest window. If the hit is far below the
            // "Quest Summary" row it is not the requirement card; clamp to the card.
            float summaryRow=QuestSummaryBottom(all);
            if(colorFloor>0&&summaryRow>0f&&colorFloor>summaryRow+450f)
                colorFloor=(int)Math.Ceiling(summaryRow)+2;
            // Establish the floor immediately: every later early-return (no heading, no
            // record) must still leave a valid body region for the fallback painter.
            if(colorFloor>0) {
                journalBodyRegion=Rectangle.FromLTRB(ownerRect.Left,colorFloor+2,ownerRect.Right,ownerRect.Bottom);
            } else {
                float summaryOnly=QuestSummaryBottom(all);
                int fallbackTop=summaryOnly>0f?(int)Math.Ceiling(summaryOnly)+2:
                    (int)(ownerRect.Top+ownerRect.Height*0.35f);
                if(fallbackTop<ownerRect.Bottom-20)
                    journalBodyRegion=Rectangle.FromLTRB(ownerRect.Left,fallbackTop,ownerRect.Right,ownerRect.Bottom);
            }
            List<OcrLine> sorted=all.FindAll(delegate(OcrLine line){RectangleF raw=GetOcrLineBounds(line);return owner.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2));});
            sorted.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
            long cardsStart=System.Diagnostics.Stopwatch.GetTimestamp();
            List<RectangleF> objectiveCards=QuestObjectiveSurface.Find(pixels,prepared.Width,prepared.Height,owner,scale);
            long cardsMs=(System.Diagnostics.Stopwatch.GetTimestamp()-cardsStart)*1000/System.Diagnostics.Stopwatch.Frequency;
            OcrLine heading=null;string stage="",reason="no-measured-stage";
            Rectangle area=Rectangle.Empty;Color background=Color.Empty;QuestJournalRecord record=null;
            int candidateIndex=0;
            int diagStage=0,diagGate=0,diagMeasure=0;
            StringBuilder diagSample=new StringBuilder();
            foreach(OcrLine line in JournalStageCandidates(sorted,scale)) {
                candidateIndex++;
                string resolved=translations.ResolveJournalStage(line.Text);
                RectangleF candidateBounds=GetOcrLineBounds(line);
                // The left column is the task LIST; those rows also name quests and used to
                // become the "heading". Measure() then looks for paper LEFT of the heading,
                // finds the list background, fails, and every frame reported
                // measured=False. The body heading always sits right of the list.
                // Note: owner already starts at the list's right edge (split), so the gate
                // must use the frame width, not owner.Width -- a 40% inset of the detail
                // pane also cut the body title and produced no-paper-viewport.
                float columnGate=Math.Max(owner.Left,prepared.Width*0.35f);
                if(Program.Benchmark&&candidateIndex<=40)
                    Program.LogRuntimeEvent("quest-journal-candidate","n="+candidateIndex+
                        " text=["+(line.Text??"").Substring(0,Math.Min(48,(line.Text??"").Length))+
                        "] left="+(int)candidateBounds.Left+" gate="+(int)columnGate+
                        " stage=["+resolved+"]");
                if(resolved.Length==0) {
                    if(diagSample.Length<200&&candidateIndex<=8) {
                        diagSample.Append('|').Append((line.Text??"").Substring(0,Math.Min(36,(line.Text??"").Length)));
                    }
                    continue;
                }
                diagStage++;
                if(candidateBounds.Left<columnGate){diagGate++;continue;}
                Rectangle candidate;Color paper;
                bool measuredOk=QuestJournalGeometry.Measure(pixels,prepared.Width,prepared.Height,GetOcrLineBounds(line),owner,scale,out candidate,out paper);
                if(!measuredOk||!JournalPaper(paper)) {
                    diagMeasure++;
                    RectangleF hr=GetOcrLineBounds(line);
                    Program.LogRuntimeEvent("quest-measure-fail","heading=["+(line.Text??"").Substring(0,Math.Min(34,(line.Text??"").Length))+
                        "] rect="+(int)hr.Left+","+(int)hr.Top+","+(int)hr.Width+"x"+(int)hr.Height+
                        " ok="+measuredOk+" paper="+paper.R+","+paper.G+","+paper.B+
                        " area="+(candidate.IsEmpty?"empty":candidate.Left+","+candidate.Top+","+candidate.Width+"x"+candidate.Height)+
                        " owner="+(int)owner.Left+","+(int)owner.Top+","+(int)owner.Width+"x"+(int)owner.Height);
                    continue;
                }
                Rectangle expanded=QuestJournalGeometry.ExpandReplacementRegion(candidate,sorted,GetOcrLineBounds(line),owner,prepared.Size,scale,objectiveCards);
                // A failed expansion means one body row sticks out of the measured
                // box. Dropping the heading here turned a perfectly measured frame
                // into "no-paper-viewport"; keep the measured box and let the
                // selector/completeness check decide, then fall back if needed.
                if(!expanded.IsEmpty)candidate=expanded;
                if(candidate.IsEmpty)continue;
                if(heading!=null&&TranslationStore.Normalize(stage)!=TranslationStore.Normalize(resolved)) {
                    // A second different stage row is normally the left list or a
                    // neighbouring caption. Rejecting the whole frame here is what
                    // left a readable pane untranslated; keep the first heading.
                    continue;
                }
                heading=line;stage=resolved;area=candidate;background=paper;
            }
            Program.LogRuntimeEvent("quest-journal-diag","owner="+(int)owner.Left+","+(int)owner.Top+
                ","+(int)owner.Width+"x"+(int)owner.Height+" sorted="+sorted.Count+
                " cand="+candidateIndex+" stageOk="+diagStage+" gateSkip="+diagGate+
                " measureFail="+diagMeasure+" sample="+diagSample);
            long headingMs=(System.Diagnostics.Stopwatch.GetTimestamp()-cardsStart)*1000/System.Diagnostics.Stopwatch.Frequency-cardsMs;
            long proseStart=System.Diagnostics.Stopwatch.GetTimestamp();
            // The measured paper box often covers only the caption row; the body rows sit
            // below it. Extend the prose area down to the viewport bottom so the selector
            // actually sees the paragraph instead of the title alone.
            Rectangle proseArea=area;
            if(heading!=null) {
                Rectangle headingBox=Rectangle.Ceiling(GetOcrLineBounds(heading));
                proseArea=Rectangle.FromLTRB(area.Left,(int)Math.Max(area.Top,headingBox.Bottom),
                    area.Right,(int)Math.Max(area.Bottom,owner.Bottom));
            }
            string proseForSelect=heading!=null?JournalProse(sorted,GetOcrLineBounds(heading),proseArea,false):"";
            long proseMs=(System.Diagnostics.Stopwatch.GetTimestamp()-proseStart)*1000/System.Diagnostics.Stopwatch.Frequency;
            long selectStart=System.Diagnostics.Stopwatch.GetTimestamp();
            if(heading!=null)record=SelectConfirmedJournal(stage,proseForSelect,detail,out reason);
            long selectMs=(System.Diagnostics.Stopwatch.GetTimestamp()-selectStart)*1000/System.Diagnostics.Stopwatch.Frequency;
            if(heading!=null&&record==null&&detail!=null&&detail.Count>0)
                record=SelectConfirmedJournal(stage,JournalPanelProse(detail,stage),detail,out reason);
            if(heading==null) {
                // One general path for heading-less, scrolled and server-variant
                // journals. No quest name or quantity is encoded in this procedure.
                foreach(OcrLine line in sorted) {
                    string key=TranslationStore.Normalize(line.Text);
                    if(key.Length<5||key.StartsWith("level ",StringComparison.Ordinal)||QuestDetailGeometry.IsSummaryKey(key)||
                        QuestJournalLayout.IsCounterLine(line.Text,GetOcrLineBounds(line),objectiveCards,scale))continue;
                    // Same rule as the stage path: never anchor on the task list column.
                    // Use the frame-relative gate; owner.Width is already the detail pane.
                    float columnGate=Math.Max(owner.Left,prepared.Width*0.35f);
                    if(GetOcrLineBounds(line).Left<columnGate)continue;
                    Rectangle candidate;Color paper;
                    if(!QuestJournalGeometry.Measure(pixels,prepared.Width,prepared.Height,GetOcrLineBounds(line),owner,scale,out candidate,out paper)||!JournalPaper(paper))continue;
                    candidate=QuestJournalGeometry.ExpandReplacementRegion(candidate,sorted,GetOcrLineBounds(line),owner,prepared.Size,scale,objectiveCards);
                    if(candidate.IsEmpty)continue;
                    heading=line;area=candidate;background=paper;break;
                }
                if(heading!=null) {
                    string prose=JournalProse(sorted,GetOcrLineBounds(heading),area,true);
                    foreach(OcrLine line in sorted) {
                        if(GetOcrLineBounds(line).Bottom>=area.Top)continue;
                        string caption=translations.ResolveJournalCaption(line.Text);if(caption.Length==0)continue;
                        record=SelectConfirmedJournal(caption,prose,sorted,out reason);if(record!=null)break;
                    }
                    if(record==null&&detail!=null&&detail.Count>0)
                        foreach(OcrLine line in sorted) {
                            if(GetOcrLineBounds(line).Bottom>=area.Top)continue;
                            string caption=translations.ResolveJournalCaption(line.Text);if(caption.Length==0)continue;
                            record=SelectConfirmedJournal(caption,JournalPanelProse(detail,caption),detail,out reason);if(record!=null)break;
                        }
                    if(record==null) {
                        string selected=SelectedJournalCaption(all,pixels,prepared.Width,prepared.Height,owner,scale);
                        if(selected.Length>0)record=SelectConfirmedJournal(selected,prose,sorted,out reason);
                    }
                    if(record==null) {
                        string fingerprintId=translations.ResolveStageFingerprint(heading.Text+" "+prose,"");
                        if(fingerprintId.Length>0)record=translations.FindJournalByTaskId(fingerprintId,prose);
                    }
                    if(record==null) {
                        // The measured prose string starts with the stage heading. Sibling
                        // stages ("[Construction Site B1/B2/B3] Shumi's ...") share those
                        // words, so leaving the heading in flattens the Jaccard gap and the
                        // body stays unselected. The heading is not body prose: drop it.
                        string bodyProse=prose;
                        if(heading!=null&&!String.IsNullOrEmpty(heading.Text)) {
                            string headingText=heading.Text.Trim();
                            int at=bodyProse.IndexOf(headingText,StringComparison.Ordinal);
                            if(at>=0)bodyProse=bodyProse.Remove(at,headingText.Length).Trim();
                        }
                        record=translations.SelectJournalForProse(bodyProse,out reason);
                        if(record==null)Program.LogRuntimeEvent("quest-journal-headingless","proseLen="+bodyProse.Length+
                            " detail="+(detail==null?0:detail.Count)+" reason="+reason);
                    }
                    if(record==null&&detail!=null&&detail.Count>0)
                        record=translations.SelectJournalForProse(JournalPanelProse(detail),out reason);
                }
            }
            if(Program.Benchmark)Program.LogRuntimeEvent("quest-journal-timing","cardsMs="+cardsMs+" headingMs="+headingMs+" proseMs="+proseMs+" selectMs="+selectMs+" totalMs="+((System.Diagnostics.Stopwatch.GetTimestamp()-cardsStart)*1000/System.Diagnostics.Stopwatch.Frequency));
            // Hand the measured body area to the record fallback only when the colour
            // floor did not already establish the viewport.
            Rectangle measuredBody = heading != null ? proseArea : area;
            if(journalBodyRegion.IsEmpty&&!measuredBody.IsEmpty) journalBodyRegion=measuredBody;
            // Measure() fails on some skins (measured=False in every frame), and the whole
            // pane then runs through the fallback with an empty region. Fall back to the
            // quest window itself: everything below the grey requirement card is body.
            if(journalBodyRegion.IsEmpty&&heading==null) {
                Rectangle panel=Rectangle.Ceiling(owner);
                float cardBottom=QuestSummaryBottom(sorted);
                int top=cardBottom>0f?(int)Math.Ceiling(cardBottom)+2:panel.Top+panel.Height/3;
                if(top<panel.Bottom-20)journalBodyRegion=Rectangle.FromLTRB(panel.Left,top,panel.Right,panel.Bottom);
            }
            if(colorFloor>0) {
                int left=journalBodyRegion.IsEmpty?ownerRect.Left:journalBodyRegion.Left;
                int right=journalBodyRegion.IsEmpty?ownerRect.Right:journalBodyRegion.Right;
                int bottom=journalBodyRegion.IsEmpty?ownerRect.Bottom:journalBodyRegion.Bottom;
                if(colorFloor+2<bottom-30)
                    journalBodyRegion=Rectangle.FromLTRB(left,colorFloor+2,right,bottom);
            }
            if(heading==null){Program.LogRuntimeEvent("quest-journal","selected=false reason=no-paper-viewport");return false;}
            if(record!=null) {
                Rectangle measuredArea=area;
                Rectangle body=ExpandRegionToConfirmedBody(area,record,sorted,owner,prepared.Size,scale);
                if(!body.IsEmpty)area=body;
                Rectangle expanded=QuestJournalGeometry.ExpandReplacementRegion(area,sorted,GetOcrLineBounds(heading),owner,prepared.Size,scale,objectiveCards);
                Program.LogRuntimeEvent("quest-journal-region","measured="+measuredArea.Left+","+measuredArea.Top+","+
                    measuredArea.Width+"x"+measuredArea.Height+" body="+body.Left+","+body.Top+","+body.Width+"x"+body.Height+
                    " expanded="+expanded.Left+","+expanded.Top+","+expanded.Width+"x"+expanded.Height);
                if(!expanded.IsEmpty) area=expanded;
                else Program.LogRuntimeEvent("quest-journal","expansion=failed keep-measured");
                // Objective icons/counters are live game surfaces. Trim the region
                // below any card that intersects it; never drop the whole frame.
                foreach(RectangleF card in objectiveCards) {
                    if(!card.IntersectsWith(area))continue;
                    int newTop=Math.Min(area.Bottom-1,(int)Math.Ceiling(card.Bottom+Math.Max(1,scale)));
                    area=Rectangle.FromLTRB(area.Left,Math.Max(area.Top,newTop),area.Right,area.Bottom);
                }
                if(area.Height<45*scale){Program.LogRuntimeEvent("quest-journal","selected=false reason=objective-trim-too-small");return false;}
                foreach(OcrLine line in sorted) {
                    RectangleF raw=GetOcrLineBounds(line);
                    if(raw.IsEmpty||!raw.IntersectsWith(area))continue;
                    string key=TranslationStore.Normalize(line.Text);
                    if(QuestDetailGeometry.IsSummaryKey(key)||key.StartsWith("level ",StringComparison.Ordinal)||
                        key=="quest helper"||key=="forfeit"||key=="details"||
                        QuestJournalLayout.IsCounterLine(line.Text,raw,objectiveCards,scale))continue;
                    if(!area.Contains(Rectangle.Ceiling(raw))) {
                        // A row crossing the measured edge is clipped by the layout; it
                        // must not suppress the whole replacement.
                        Program.LogRuntimeEvent("quest-journal","partial-row-clipped");
                    }
                }
            }
            string header=JournalHeader(sorted,pixels,prepared.Width,prepared.Height,area);
            string objectives=JournalObjectiveSignature(sorted,objectiveCards,area.Top);
            if(record==null&&stage.Length==0&&rememberedJournal!=null&&header.Length>0&&header==rememberedJournalHeader&&
                objectives==rememberedJournalObjectives&&captureBounds==rememberedJournalCapture&&
                (Stopwatch.GetTimestamp()-rememberedJournalAt)/(double)Stopwatch.Frequency<600&&
                translations.JournalMemoryMatches(rememberedJournal,JournalProse(sorted,GetOcrLineBounds(heading),area,true)))
            {record=rememberedJournal;reason="same-task-scroll-context";}
            if(record==null){Program.LogRuntimeEvent("quest-journal","selected=false reason="+reason);return false;}
            string completeness;
            if(!JournalReplacementComplete(record,sorted,area,objectiveCards,scale,heading,stage,out completeness)) {
                lastQuestJournalRejection="quest-journal-incomplete:"+completeness;
                // The stage/title already named the quest; a single OCR-damaged token
                // ("an", "vvhy", "kim") must not blank the whole pane. Paint the record
                // and keep the rejection in the log for tuning.
                Program.LogRuntimeEvent("quest-journal","incomplete="+lastQuestJournalRejection+" painted=true");
            }
            RectangleF overlay=OverlaySpan(area,scale);
            Rectangle overlayArea=Rectangle.Ceiling(overlay);
            // A wrapped two-line stage heading is not a double-height font.
            // Derive body typography from individual visible prose rows instead.
            List<float> proseHeights=new List<float>();
            foreach(OcrLine proseLine in sorted) {
                RectangleF row=GetOcrLineBounds(proseLine);
                if(!area.Contains(Rectangle.Ceiling(row))||row.Height<5*scale||row.Height>40*scale)continue;
                if(stage.Length>0&&GetOcrLineBounds(heading).Contains(row))continue;
                proseHeights.Add(row.Height/scale);
            }
            proseHeights.Sort();
            float fontHeight=proseHeights.Count==0?16:Math.Max(14,Math.Min(28,proseHeights[proseHeights.Count/2]));
            JournalLayoutPlan layout=null;
            using(Bitmap probe=new Bitmap(1,1))
            using(Graphics measure=Graphics.FromImage(probe)) {
                measure.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                layout=QuestJournalLayout.Build(measure,overlayArea,fontHeight,record.ChineseTitle,record.Chinese);
            }
            if(layout==null){
                // The measured body can be a short strip (88px) where the full
                // title+paragraph plan cannot fit. Painting the body text alone is
                // still better than leaving the pane in English.
                Program.LogRuntimeEvent("quest-journal","layout=none draw-raw region="+
                    overlayArea.Left+","+overlayArea.Top+","+overlayArea.Width+"x"+overlayArea.Height);
                List<OcrLine> bodyRows=new List<OcrLine>();
                foreach(OcrLine line in sorted) {
                    RectangleF raw=GetOcrLineBounds(line);
                    if(!raw.IsEmpty&&area.Contains(Rectangle.Ceiling(raw)))bodyRows.Add(line);
                }
                if(bodyRows.Count==0)bodyRows=sorted;
                int beforeRaw=output.Count;
                AddDetailBlockLabel(output,bodyRows,record.Chinese,scale);
                if(output.Count==beforeRaw)return false;
                output[output.Count-1].Wrap=true;
                output[output.Count-1].SemanticRole="task-journal";
                return true;
            }
            // Remove only labels this region fully owns. A caption/objective label
            // that merely touches the boundary stays visible instead of being
            // deleted while the replacement rectangle leaves part of its English.
            output.RemoveAll(delegate(OverlayLabel label){
                RectangleF owned=label.HasSourceBounds?label.SourceBounds:label.Bounds;
                if(owned.IsEmpty)return overlay.IntersectsWith(label.Bounds);
                if(overlay.Contains(owned)&&overlay.Contains(label.Bounds))return true;
                RectangleF overlap=RectangleF.Intersect(owned,overlay);
                float areaOwned=owned.Width*owned.Height;
                bool contained=areaOwned>0&&overlap.Width*overlap.Height>=areaOwned*0.90f&&
                    overlay.Contains(new PointF(owned.Left+owned.Width/2,owned.Top+owned.Height/2));
                return contained;
            });
            QuestJournalDocument document=new QuestJournalDocument{Title=record.ChineseTitle,Body=record.Chinese,Background=background,
                FontHeight=fontHeight,TextBounds=JournalTextBounds(sorted,area,scale),Layout=layout};
            output.Add(new OverlayLabel{Bounds=overlay,SourceBounds=overlay,HasSourceBounds=true,PanelBounds=overlay,
                Text=(String.IsNullOrEmpty(record.ChineseTitle)?"":record.ChineseTitle+"\n\n")+record.Chinese,Wrap=true,Confidence=LabelConfidence.High,SemanticRole="task-journal",
                MatchSource="stage-journal",Journal=document});
            if(header.Length>0){rememberedJournal=record;rememberedJournalHeader=header;rememberedJournalObjectives=objectives;rememberedJournalCapture=captureBounds;rememberedJournalAt=Stopwatch.GetTimestamp();}
            Program.LogRuntimeEvent("quest-journal","selected=true reason="+reason+" canonical="+(record.TaskId.Length>0)+
                " region="+area.Left+","+area.Top+","+area.Width+"x"+area.Height+
                " textRows="+(int)document.TextBounds.Width+"x"+(int)document.TextBounds.Height+
                " layout="+layout.Describe());
            return true;
        }

        // Generic completeness guard for every journal selection path. The selected
        // record may only replace visible prose that its English source and Chinese
        // translation actually cover. Unknown appended prose, or a visible quantity
        // absent from either side, aborts the whole replacement; the original pixels
        // and text are preserved by the caller.
        private bool JournalReplacementComplete(QuestJournalRecord record,List<OcrLine> lines,
            Rectangle area,List<RectangleF> objectiveCards,float scale,OcrLine heading,string stage,
            out string reason)
        {
            reason="";
            if(record==null||String.IsNullOrWhiteSpace(record.English)||String.IsNullOrWhiteSpace(record.Chinese))
            {reason="missing-record-content";return false;}
            HashSet<string> candidateNumbers=JournalNumberTokens(record.English);
            HashSet<string> translatedNumbers=JournalNumberTokens(record.Chinese);
            StringBuilder visible=new StringBuilder();
            foreach(OcrLine line in lines) {
                if(line==null)continue;
                RectangleF raw=GetOcrLineBounds(line);
                if(raw.IsEmpty||!area.Contains(Rectangle.Ceiling(raw)))continue;
                string key=TranslationStore.Normalize(line.Text);
                if(QuestDetailGeometry.IsSummaryKey(key)||key.StartsWith("level ",StringComparison.Ordinal)||
                    IsJournalChromeRow(key)||
                    QuestJournalLayout.IsCounterLine(line.Text,raw,objectiveCards,scale))continue;
                bool inCard=false;
                if(objectiveCards!=null)foreach(RectangleF card in objectiveCards)
                    if(card.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2))){inCard=true;break;}
                if(inCard)continue;
                // Only a structurally confirmed stage title may be skipped. In a
                // titleless/scroll path the heading is a body locator, but the row it
                // located is still the caption row: its OCR damage ("B2" -> "B21") must
                // not be charged to the body as a missing quantity.
                bool isHeadingRow=heading!=null&&GetOcrLineBounds(heading).Contains(raw);
                bool confirmedTitle=isHeadingRow&&(stage.Length==0||
                    TranslationStore.Normalize(translations.ResolveJournalStage(heading.Text))==
                    TranslationStore.Normalize(stage));
                if(confirmedTitle)continue;
                if(!String.IsNullOrEmpty(record.Title)&&key==TranslationStore.Normalize(record.Title))continue;
                bool hasWord=false,hasDigit=false;
                foreach(char value in line.Text) {
                    if(Char.IsLetterOrDigit(value))hasWord=true;
                    if(Char.IsDigit(value))hasDigit=true;
                }
                // Short numeric rows are quantity/prose evidence, not noise.
                if(key.Length<4&&!hasDigit)continue;
                if(!hasWord)continue;
                if(visible.Length>0)visible.Append(' ');
                visible.Append(line.Text);
            }
            foreach(string number in JournalNumberTokens(visible.ToString())) {
                // A lone digit is the summary counter (0/1, 29+) or a HUD value, not a
                // quantity the record must contain. Multi-digit quantities still bind.
                if(number.TrimStart('+','-').Length<=1)continue;
                if(!candidateNumbers.Contains(number)){reason="visible-quantity-not-in-record:"+number;return false;}
                if(!translatedNumbers.Contains(number)){reason="visible-quantity-not-in-translation:"+number;return false;}
            }
            // Align the complete visible prose, not each row independently: the
            // ordered sequence catches cross-line reordering as well as negation or
            // inserted words. OCR-tolerant token matching is bounded below.
            string alignReason;
            if(!JournalLineAlignsToSource(visible.ToString(),record.English,out alignReason)) {
                // A caption that OCR split across two rows ("... Lost Roll" / "of Cash")
                // can leave a short fragment ahead of the body. One retry without up to
                // three leading words, and only for a long record, keeps the ordered
                // check honest for everything else.
                string trimmed=record.English.Length>=80?JournalTrimLeadingWords(visible.ToString(),3):null;
                if(trimmed==null||trimmed.Length==0||
                    !JournalLineAlignsToSource(trimmed,record.English,out alignReason)) {
                    reason="visible-prose-"+alignReason;
                    return false;
                }
            }
            return true;
        }

        private static HashSet<string> JournalNumberTokens(string text)
        {
            HashSet<string> result=new HashSet<string>(StringComparer.Ordinal);
            foreach(Match match in Regex.Matches(text??"",@"[-+]?\d+(?:[.,]\d+)*(?:%|％)?"))
                result.Add(match.Value.Replace('％','%'));
            return result;
        }

        private static string[] JournalTokens(string text)
        {
            return TranslationStore.Normalize(text??"").Split(
                new char[]{' '},StringSplitOptions.RemoveEmptyEntries);
        }
        private static string JournalTrimLeadingWords(string text,int count)
        {
            if(String.IsNullOrEmpty(text))return null;
            int index=0;
            for(int i=0;i<count;i++) {
                while(index<text.Length&&Char.IsWhiteSpace(text[index]))index++;
                while(index<text.Length&&!Char.IsWhiteSpace(text[index]))index++;
            }
            if(index>=text.Length)return null;
            return text.Substring(index).TrimStart();
        }

        // Ordered source alignment, not a bag-of-words score. Every visible
        // non-numeric token must match a later candidate token (bounded fuzzy match
        // for OCR damage). Adding, reordering or negating source words leaves an
        // unmatched token and rejects the whole replacement.
        internal static bool JournalLineAlignsToSource(string line,string source,out string reason)
        {
            reason="";
            string[] visible=JournalTokens(line), candidate=JournalTokens(source);
            int cursor=0;
            foreach(string token in visible) {
                // Single-character words are OCR decoration, but numeric tokens
                // (including "1") are part of quantity-to-item association and must
                // participate in the ordered alignment.
                if(token.Length<=1&&!JournalTokenHasDigit(token))continue;
                bool matched=false;
                for(int i=cursor;i<candidate.Length;i++) {
                    string sourceToken=candidate[i];
                    if(sourceToken.Length<=1&&!JournalTokenHasDigit(sourceToken))continue;
                    if(JournalTokenMatches(token,sourceToken)||JournalLooseTokenMatch(token,sourceToken)) {
                        cursor=i+1;matched=true;break;
                    }
                }
                if(!matched){reason="order-or-content:"+token;return false;}
            }
            return true;
        }

        private static bool JournalTokenHasDigit(string token)
        {
            foreach(char value in token)if(Char.IsDigit(value))return true;
            return false;
        }

        // OCR damage beyond one edit ("Anyway" -> "anvay") still leaves the first letter
        // and rough length intact. This looser pass runs only while verifying a record a
        // fingerprint already named, so it cannot promote a different stage.
        private static bool JournalLooseTokenMatch(string left,string right)
        {
            if(left.Length<5||right.Length<5)return false;
            if(left[0]!=right[0])return false;
            if(Math.Abs(left.Length-right.Length)>2)return false;
            return JournalEditDistance(JournalOcrFold(left),JournalOcrFold(right),2)>=0;
        }

        private static bool JournalTokenMatches(string left,string right)
        {
            bool leftDigit=JournalTokenHasDigit(left),rightDigit=JournalTokenHasDigit(right);
            // A number is not interchangeable with another number: "1 Green ... 10
            // Blue" may not align as "10 Green ... 1 Blue". Exact numeric tokens keep
            // the quantity bound to its neighbouring item/prose tokens.
            if(leftDigit||rightDigit)
            {
                if(leftDigit&&rightDigit&&String.Equals(left,right,StringComparison.Ordinal))return true;
                // "t01d" is the word "told" with two letters read as digits. Only a
                // mixed token (letters plus digits) may be rewritten; pure numbers stay
                // bound to their quantity and never collapse into each other.
                bool mixed=(leftDigit&&Regex.IsMatch(left,"[a-z]"))||
                    (rightDigit&&Regex.IsMatch(right,"[a-z]"));
                if(mixed) {
                    string leftLetters=JournalOcrFold(JournalDigitsToLetters(left));
                    string rightLetters=JournalOcrFold(JournalDigitsToLetters(right));
                    if(String.Equals(leftLetters,rightLetters,StringComparison.Ordinal))return true;
                }
                return false;
            }
            if(String.Equals(left,right,StringComparison.Ordinal))return true;
            // A split letter (w read as vv) changes the length and defeats the bounded
            // edit distance, so fold both sides before measuring it.
            left=JournalOcrFold(left);right=JournalOcrFold(right);
            if(String.Equals(left,right,StringComparison.Ordinal))return true;
            if(left.Length<4||right.Length<4)
            {
                // Two- and three-letter words ("of", "on", "the") dominate journal prose
                // and a single OCR slip rejects the whole paragraph. Same length and the
                // same first letter keeps "of/ot" alive without letting "of/on" merge.
                if(left.Length==right.Length&&left.Length>=2&&
                    left[0]==right[0]&&JournalEditDistance(left,right,1)==1)return true;
                // Windows OCR clips the final glyph ("City" -> "Cit"). A clipped word is
                // still an unambiguous prefix; the three-letter floor keeps "of"/"on"
                // apart and the one-character bound keeps "the"/"there" apart.
                if(left.Length>=3&&right.Length-left.Length==1&&
                    right.StartsWith(left,StringComparison.Ordinal))return true;
                if(right.Length>=3&&left.Length-right.Length==1&&
                    left.StartsWith(right,StringComparison.Ordinal))return true;
                return false;
            }
            int longest=Math.Max(left.Length,right.Length);
            if(Math.Abs(left.Length-right.Length)>2)return false;
            int limit=Math.Max(1,longest/5);
            int distance=JournalEditDistance(left,right,limit);
            return distance>=0&&distance<=limit;
        }

        // OCR splits a few letters into two glyphs: w into vv, m into rn, d into cl.
        // Folding both sides keeps one split letter from rejecting a whole quest body,
        // while the ordered token alignment and the >=4 length guard keep ordinary
        // words from collapsing into each other. Only fold the split pairs whose
        // canonical letters are rare in the middle of English words (vv, rn); "cl"
        // appears inside ordinary words (class, clean) and is left alone.
        internal static string JournalOcrFold(string token)
        {
            if(String.IsNullOrEmpty(token)||token.Length<5)return token??"";
            if(token.IndexOf("vv",StringComparison.Ordinal)>=0)token=token.Replace("vv","w");
            if(token.IndexOf("rn",StringComparison.Ordinal)>=0)token=token.Replace("rn","m");
            return token;
        }

        // OCR reads o as 0, l as 1, s as 5, b as 8, g as 6, z as 2 inside words.
        // Rewrite those digits back to letters before the ordered alignment; the
        // numeric-token branch above keeps real quantities out of this path.
        private static string JournalDigitsToLetters(string token)
        {
            if(String.IsNullOrEmpty(token))return token??"";
            StringBuilder result=new StringBuilder(token.Length);
            foreach(char value in token) {
                switch(value) {
                    case '0':result.Append('o');break;
                    case '1':result.Append('l');break;
                    case '2':result.Append('z');break;
                    case '3':result.Append('e');break;
                    case '4':result.Append('a');break;
                    case '5':result.Append('s');break;
                    case '6':result.Append('g');break;
                    case '7':result.Append('t');break;
                    case '8':result.Append('b');break;
                    case '9':result.Append('g');break;
                    default:result.Append(value);break;
                }
            }
            return result.ToString();
        }

        private static int JournalEditDistance(string left,string right,int limit)
        {
            if(Math.Abs(left.Length-right.Length)>limit)return -1;
            int[] previous=new int[right.Length+1],current=new int[right.Length+1];
            for(int j=0;j<=right.Length;j++)previous[j]=j;
            for(int i=1;i<=left.Length;i++) {
                current[0]=i;int rowMinimum=i;
                for(int j=1;j<=right.Length;j++) {
                    int cost=left[i-1]==right[j-1]?0:1;
                    current[j]=Math.Min(Math.Min(current[j-1]+1,previous[j]+1),previous[j-1]+cost);
                    if(current[j]<rowMinimum)rowMinimum=current[j];
                }
                if(rowMinimum>limit)return -1;
                int[] swap=previous;previous=current;current=swap;
            }
            return previous[right.Length]<=limit?previous[right.Length]:-1;
        }

        // SelectJournal needs enough prose anchors to choose one stage. When the
        // visible body is already unique for a task whose title is not truncated,
        // the dictionary's observed-body matcher is stronger evidence. The candidate
        // is ranked against the complete visible panel and accepted only with a clear
        // winner; otherwise selection stays ambiguous and no text is guessed.
        private readonly Dictionary<string,QuestJournalRecord> confirmedJournalCache=
            new Dictionary<string,QuestJournalRecord>(StringComparer.Ordinal);
        private QuestJournalRecord SelectConfirmedJournal(string stage,string observed,
            List<OcrLine> lines,out string reason)
        {
            // The same stage title is re-selected by several passes (region, panels, the
            // second journal round). Caching by stage keeps the title similarity scan and the
            // record walk from repeating on every pass.
            string confirmedKey=stage??"";
            QuestJournalRecord confirmedCached;
            if(confirmedKey.Length>0&&confirmedJournalCache.TryGetValue(confirmedKey,out confirmedCached))
            {reason="stage-cache";return confirmedCached;}
            QuestJournalRecord record=translations.SelectJournal(stage,observed,out reason);
            if(record!=null){if(confirmedKey.Length>0){if(confirmedJournalCache.Count>256)confirmedJournalCache.Clear();confirmedJournalCache[confirmedKey]=record;}return record;}
            // A numbered stage wins over similarity: the caption, body or requirement shown
            // on this frame names the stage ("roll", "lorang"), even when OCR mangles the
            // title beyond what the title index can resolve.
            string fingerprintText=stage+" "+observed;
            string fingerprintId=translations.ResolveStageFingerprint(fingerprintText,
                String.IsNullOrWhiteSpace(stage)?"":translations.ResolveUniqueTaskTitleId(stage));
            if(fingerprintId.Length>0) {
                QuestJournalRecord fingerprinted=translations.FindJournalByTaskId(fingerprintId,observed);
                if(fingerprinted!=null){reason="stage-fingerprint";return fingerprinted;}
            }
            if(String.IsNullOrWhiteSpace(stage)||stage.Contains("...")||stage.Contains("…"))return null;
            string taskId=translations.ResolveUniqueTaskTitleId(stage);
            if(taskId.Length==0)return null;
            string title=ResolveUniqueTaskTitleChinese(stage);
            if(title.Length==0)return null;
            List<MatchResult> matches=CollectConfirmedBodyMatches(observed,taskId);
            if(matches.Count==0&&lines!=null) {
                List<OcrLine> ordered=new List<OcrLine>(lines);
                ordered.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
                string panelText=JournalPanelProse(ordered,stage);
                int panelLength=TranslationStore.Normalize(panelText).Length;
                bool foundFull=false;
                // Bound the sliding window: every candidate start runs a full-library body
                // match (up to 77 combinations here). Six starts cover the visible paragraphs
                // without paying for the whole list.
                int windowStarts=Math.Min(ordered.Count,3);
                for(int start=0;start<windowStarts&&!foundFull;start++) {
                    StringBuilder window=new StringBuilder();
                    for(int span=0;span<5&&start+span<ordered.Count;span++) {
                        OcrLine line=ordered[start+span];
                        string key=TranslationStore.Normalize(line.Text);
                        if(QuestDetailGeometry.IsSummaryKey(key)||key.StartsWith("level ",StringComparison.Ordinal)||
                            key=="quest helper"||key=="forfeit"||key=="details"||key=="available"||
                            key=="in progress"||key=="completed"||QuestJournalLayout.IsCounterLike(line.Text))continue;
                        if(key==TranslationStore.Normalize(stage))continue;
                        if(window.Length>0)window.Append(' ');
                        window.Append(line.Text);
                        if(TranslationStore.Normalize(window.ToString()).Length<24)continue;
                        foreach(MatchResult match in CollectConfirmedBodyMatches(window.ToString(),taskId)) {
                            bool duplicate=false;
                            foreach(MatchResult existing in matches)
                                if(existing.Entry.Chinese==match.Entry.Chinese&&existing.Entry.English==match.Entry.English){duplicate=true;break;}
                            if(!duplicate)matches.Add(match);
                        }
                        if(panelLength>0)foreach(MatchResult found in matches)
                            if((found.Entry.Normalized==null?0:found.Entry.Normalized.Length)*4>=panelLength*3) {
                                foundFull=true;break;
                            }
                    }
                }
            }
            int bodyCandidates=matches.Count;
            StringBuilder matchLog=new StringBuilder();
            foreach(MatchResult match in matches) {
                if(match==null||match.Entry==null)continue;
                if(matchLog.Length>0)matchLog.Append(';');
                matchLog.Append("task=").Append(match.Entry.IsTaskText).Append(",target=").Append(match.Entry.IsQuestTarget)
                    .Append(",id=").Append(match.Entry.TaskId).Append(",len=")
                    .Append(match.Entry.Normalized==null?0:match.Entry.Normalized.Length);
            }
            Program.LogRuntimeEvent("quest-journal-select","stageLen="+stage.Length+" id="+taskId+
                " title="+(title.Length>0)+" prose="+observed.Length+" matches="+matches.Count+
                " bodies="+bodyCandidates+" ["+matchLog+"]");
            MatchResult best=null;double bestScore=-1.0,secondScore=-1.0;
            // Rank against the whole measured panel, not only the first small paper
            // fragment. Otherwise a short alternate stage can win merely because the
            // visible first two rows resemble it, and the full body is then excluded.
            string ranking=observed;
            if(lines!=null&&lines.Count>0) {
                string panel=JournalPanelProse(lines,stage);
                if(panel.Length>ranking.Length)ranking=panel;
            }
            string observedKey=TranslationStore.Normalize(ranking);
            foreach(MatchResult match in matches) {
                if(match==null||match.Entry==null||String.IsNullOrWhiteSpace(match.Entry.Chinese))continue;
                string candidate=match.Entry.Normalized??"";
                if(candidate.Length==0)continue;
                float similarity=TranslationStore.DetailTextSimilarity(ranking,candidate);
                float sourceCoverage=(float)match.Length/Math.Max(1,observedKey.Length);
                float entryCoverage=(float)match.Length/Math.Max(1,candidate.Length);
                // The body the OCR actually shows must be supported in both directions.
                bool enough=similarity>=0.60f||(sourceCoverage>=0.70f&&entryCoverage>=0.55f);
                if(!enough)continue;
                double score=similarity*10000.0+Math.Min(sourceCoverage,entryCoverage)*1000.0+
                    Math.Min(candidate.Length,observedKey.Length)*0.1;
                if(score>bestScore){secondScore=bestScore;bestScore=score;best=match;}
                else if(score>secondScore)secondScore=score;
            }
            if(best==null)return null;
            if(matches.Count>1&&secondScore>=0.0&&bestScore-secondScore<200.0)return null;
            string bestChinese=best.Entry.Chinese;
            QuestJournalRecord confirmed=new QuestJournalRecord{Title=stage,ChineseTitle=title,TaskId=taskId,
                English=best.Entry.English,Chinese=bestChinese};
            reason="observed-body-ranked";
            return confirmed;
        }

        private List<MatchResult> CollectConfirmedBodyMatches(string text,string taskId)
        {
            List<MatchResult> result=new List<MatchResult>();
            if(String.IsNullOrWhiteSpace(text))return result;
            foreach(MatchResult match in translations.FindTaskMatches(text,taskId)) {
                if(match==null||match.Entry==null||(!match.Entry.IsTaskText&&!match.Entry.IsQuestTarget)||
                    String.IsNullOrWhiteSpace(match.Entry.Chinese)||
                    (match.Entry.Normalized==null?0:match.Entry.Normalized.Length)<24)continue;
                if(match.Entry.TaskId.Length>0&&match.Entry.TaskId!=taskId)continue;
                bool duplicate=false;
                foreach(MatchResult existing in result)
                    if(existing.Entry.Chinese==match.Entry.Chinese&&existing.Entry.English==match.Entry.English){duplicate=true;break;}
                if(!duplicate)result.Add(match);
            }
            if(result.Count==0) {
                foreach(MatchResult match in translations.FindSharedTaskBodyMatches(text)) {
                    if(match==null||match.Entry==null||(!match.Entry.IsTaskText&&!match.Entry.IsQuestTarget)||
                        String.IsNullOrWhiteSpace(match.Entry.Chinese)||
                        (match.Entry.Normalized==null?0:match.Entry.Normalized.Length)<24)continue;
                    bool duplicate=false;
                    foreach(MatchResult existing in result)
                        if(existing.Entry.Chinese==match.Entry.Chinese&&existing.Entry.English==match.Entry.English){duplicate=true;break;}
                    if(!duplicate)result.Add(match);
                }
            }
            return result;
        }

        private string ResolveUniqueTaskTitleChinese(string stage)
        {
            string title="";
            foreach(MatchResult match in translations.FindTaskNameMatches(stage)) {
                if(match==null||match.Entry==null||String.IsNullOrWhiteSpace(match.Entry.Chinese)||match.Entry.TaskId.Length==0)continue;
                if(title.Length>0&&title!=match.Entry.Chinese)return "";
                title=match.Entry.Chinese;
            }
            return title;
        }

        private static string JournalPanelProse(List<OcrLine> lines)
        {
            return JournalPanelProse(lines,"");
        }

        // The task title is a short dictionary row and would win FindTaskMatches'
        // exact short-circuit before the long body is considered. The confirmed body
        // fallback therefore omits that exact title row; it never omits prose.
        private static string JournalPanelProse(List<OcrLine> lines,string title)
        {
            StringBuilder result=new StringBuilder();
            string titleKey=String.IsNullOrWhiteSpace(title)?"":TranslationStore.Normalize(title);
            foreach(OcrLine line in lines) {
                if(line==null||String.IsNullOrWhiteSpace(line.Text))continue;
                string key=TranslationStore.Normalize(line.Text);
                if(titleKey.Length>0&&key==titleKey)continue;
                if(QuestDetailGeometry.IsSummaryKey(key)||key.StartsWith("level ",StringComparison.Ordinal)||
                    key=="quest helper"||key=="forfeit"||key=="details"||key=="available"||
                    key=="in progress"||key=="completed"||QuestJournalLayout.IsCounterLike(line.Text))continue;
                if(result.Length>0)result.Append(' ');
                result.Append(line.Text);
            }
            return result.ToString();
        }

        // Grow the measured paper rectangle until it owns every OCR row whose words
        // actually appear in the selected dictionary body. The fallback task-body
        // planner already uses this evidence; the journal panel must use it too, or
        // a small paper scan can leave the first/last body row outside the replacement.
        private Rectangle ExpandRegionToConfirmedBody(Rectangle area,QuestJournalRecord record,
            List<OcrLine> lines,RectangleF owner,Size frame,float scale)
        {
            if(record==null||String.IsNullOrWhiteSpace(record.English)||lines==null||scale<=0)return Rectangle.Empty;
            HashSet<string> words=new HashSet<string>(StringComparer.Ordinal);
            foreach(string word in TranslationStore.Normalize(record.English).Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries))
                if(word.Length>=3)words.Add(word);
            if(words.Count<3)return Rectangle.Empty;
            Rectangle bounds=Rectangle.Intersect(Rectangle.Ceiling(owner),new Rectangle(Point.Empty,frame));
            Rectangle union=Rectangle.Intersect(area,bounds);
            int rows=0;
            foreach(OcrLine line in lines) {
                if(line==null)continue;
                RectangleF raw=GetOcrLineBounds(line);
                if(raw.IsEmpty||!owner.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2)))continue;
                string key=TranslationStore.Normalize(line.Text);
                if(QuestDetailGeometry.IsSummaryKey(key)||key.StartsWith("level ",StringComparison.Ordinal)||
                    key=="quest helper"||key=="forfeit"||key=="details"||QuestJournalLayout.IsCounterLike(line.Text))continue;
                HashSet<string> lineWords=new HashSet<string>(key.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries),StringComparer.Ordinal);
                int common=0;foreach(string word in lineWords)if(word.Length>=3&&words.Contains(word))common++;
                if(common<3)continue;
                Rectangle row=Rectangle.Ceiling(raw);row.Inflate(Math.Max(1,(int)Math.Ceiling(scale)),Math.Max(1,(int)Math.Ceiling(scale)));
                union=Rectangle.Union(union,row);rows++;
            }
            if(rows<2)return Rectangle.Empty;
            union=Rectangle.Intersect(union,bounds);
            if(union.Width<=0||union.Height<=0||(long)union.Width*union.Height>800000)return Rectangle.Empty;
            return union;
        }

        private static RectangleF JournalTextBounds(List<OcrLine> lines,Rectangle area,float scale)
        {
            RectangleF bounds=RectangleF.Empty;
            foreach(OcrLine line in lines) {
                RectangleF raw=GetOcrLineBounds(line);
                if(raw.IsEmpty||!area.Contains(Rectangle.Ceiling(raw)))continue;
                string key=TranslationStore.Normalize(line.Text);
                if(QuestDetailGeometry.IsSummaryKey(key)||key.StartsWith("level ",StringComparison.Ordinal)||
                    QuestJournalLayout.IsCounterLike(line.Text))continue;
                bounds=bounds.IsEmpty?raw:RectangleF.Union(bounds,raw);
            }
            if(bounds.IsEmpty)return bounds;
            return new RectangleF(bounds.Left/scale,bounds.Top/scale,bounds.Width/scale,bounds.Height/scale);
        }

        private static List<OcrLine> JournalStageCandidates(List<OcrLine> lines,float scale)
        {
            List<OcrLine> candidates=new List<OcrLine>();
            for(int i=0;i<lines.Count;i++) {
                OcrLine first=lines[i];candidates.Add(first);
                RectangleF previous=GetOcrLineBounds(first),anchor=previous;
                if(first.Text.Length<8||first.Text.Length>110)continue;
                OcrLine combined=new OcrLine{Text=first.Text};combined.Words.AddRange(first.Words);
                for(int j=i+1;j<lines.Count&&j<=i+2;j++) {
                    OcrLine next=lines[j];RectangleF box=GetOcrLineBounds(next);
                    if(box.Top<previous.Bottom||box.Top-previous.Bottom>Math.Max(8*scale,previous.Height*1.25f)||
                        Math.Abs(box.Left-anchor.Left)>18*scale)break;
                    combined.Text+=" "+next.Text;combined.Words.AddRange(next.Words);
                    OcrLine candidate=new OcrLine{Text=combined.Text};candidate.Words.AddRange(combined.Words);
                    candidates.Add(candidate);previous=box;
                }
            }
            return candidates;
        }

        private static bool JournalPaper(Color color)
        {
            // The quest paper is white in the old skins but the classic client renders it
            // as a light grey (#F0F0F0 / #ECECEC). Requiring 245 turned those frames into
            // "measured=False", so every quest fell back to the cached record path.
            return color.R>=230&&color.G>=230&&color.B>=226;
        }

        // Window chrome inside the scroll viewport: buttons and the requirement card.
        // OCR reads "QUEST HELPER" as "quest he lper"/"QuestHelper"/"quest helper", and a
        // single unexcluded variant makes the prose alignment fail, which drops the whole
        // frame back to the cached fallback.
        private static bool IsJournalChromeRow(string key)
        {
            string compact=(key??"").Replace(" ","");
            // OCR mangles the footer buttons ("uestähelper", "FORFEIT" variants) and a
            // single missed variant feeds the button row into the visible-prose check,
            // which rejects the whole frame. These words never occur in quest prose, so
            // fuzzy membership is safe here.
            return compact.StartsWith("questhel",StringComparison.Ordinal)||
                compact.StartsWith("uesthel",StringComparison.Ordinal)||
                (compact.Length<=20&&(compact.Contains("helper")||compact.Contains("hele")))||
                compact.StartsWith("questsumm",StringComparison.Ordinal)||
                compact.StartsWith("forfeit",StringComparison.Ordinal)||
                (compact.Length<=14&&compact.Contains("forfeit"))||
                compact=="details"||compact=="available"||
                compact=="inprogress"||compact=="completed";
        }

        private string SelectedJournalCaption(List<OcrLine> lines,int[] pixels,int width,int height,
            RectangleF owner,float scale)
        {
            string selected="";
            foreach(OcrLine line in lines) {
                RectangleF r=GetOcrLineBounds(line);
                if(r.IsEmpty||r.Right>=owner.Left||r.Width<30*scale)continue;
                string caption=translations.ResolveJournalCaption(line.Text);
                if(caption.Length==0)continue;
                int blue=0,total=0;
                for(int i=0;i<12;i++) {
                    int x=(int)(r.Left+r.Width*(i+.5f)/12),y=(int)(r.Top-2*scale);
                    if(x<0||x>=width||y<0||y>=height)continue;
                    int c=pixels[y*width+x],red=(c>>16)&255,green=(c>>8)&255,b=c&255;
                    total++;if(red<125&&green>red+15&&b>green+10)blue++;
                }
                if(total<6||blue*4<total*3)continue;
                if(selected.Length>0&&TranslationStore.Normalize(selected)!=TranslationStore.Normalize(caption))return "";
                selected=caption;
            }
            return selected;
        }
        private static string JournalHeader(List<OcrLine> lines,int[] pixels,int width,int height,Rectangle body)
        {
            StringBuilder result=new StringBuilder();
            foreach(OcrLine line in lines) {
                RectangleF r=GetOcrLineBounds(line);string key=TranslationStore.Normalize(line.Text);
                if(r.Bottom>=body.Top||r.Left<body.Left||r.Left>body.Right||key.StartsWith("level ",StringComparison.Ordinal))continue;
                int blue=0,total=0;
                for(int i=0;i<12;i++) {
                    int x=Math.Max(0,Math.Min(width-1,(int)(r.Left+r.Width*i/12))),y=Math.Max(0,Math.Min(height-1,(int)(r.Top-2)));
                    int color=pixels[y*width+x],red=(color>>16)&255,green=(color>>8)&255,b=color&255;
                    total++;if(red<145&&green>red+15&&b>red+30)blue++;
                }
                if(blue*2>=total)result.Append(key).Append('|');
            }
            return result.ToString();
        }
        private static string JournalObjectiveSignature(List<OcrLine> lines,IList<RectangleF> cards,int bodyTop)
        {
            List<string> values=new List<string>();
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
            // A body row often extends past the measured paper box; requiring full containment
            // dropped every long line and left the selector with the title only.
            StringBuilder result=new StringBuilder();foreach(OcrLine line in lines){RectangleF raw=GetOcrLineBounds(line);
                RectangleF overlap=RectangleF.Intersect(raw,area);
                if((includeHeading||raw!=heading)&&overlap.Width>0&&overlap.Height>0)result.Append(line.Text).Append(' ');}
            return result.ToString();
        }
    }
}
