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
            lastQuestJournalRejection="";
            RectangleF owner=ResolveQuestJournalOwner(all,prepared.Size,scale,measured);
            if(owner.IsEmpty)return false;
            if(!measured.IsEmpty&&OverlayPaintSafety.IsFiniteRect(measured)) {
                RectangleF constrained=RectangleF.Intersect(owner,measured);
                if(constrained.Width>=200*scale&&constrained.Height>=120*scale)owner=constrained;
            }
            int[] pixels=ReadSimplePixels(prepared);
            List<OcrLine> sorted=all.FindAll(delegate(OcrLine line){RectangleF raw=GetOcrLineBounds(line);return owner.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2));});
            sorted.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
            List<RectangleF> objectiveCards=QuestObjectiveSurface.Find(pixels,prepared.Width,prepared.Height,owner,scale);
            OcrLine heading=null;string stage="",reason="no-measured-stage";
            Rectangle area=Rectangle.Empty;Color background=Color.Empty;QuestJournalRecord record=null;
            foreach(OcrLine line in sorted) {
                string resolved=translations.ResolveJournalStage(line.Text);if(resolved.Length==0)continue;
                Rectangle candidate;Color paper;
                if(!QuestJournalGeometry.Measure(pixels,prepared.Width,prepared.Height,GetOcrLineBounds(line),owner,scale,out candidate,out paper)||!JournalPaper(paper))continue;
                candidate=QuestJournalGeometry.ExpandReplacementRegion(candidate,sorted,GetOcrLineBounds(line),owner,prepared.Size,scale,objectiveCards);
                if(candidate.IsEmpty)continue;
                if(heading!=null&&TranslationStore.Normalize(stage)!=TranslationStore.Normalize(resolved)) {
                    Program.LogRuntimeEvent("quest-journal","selected=false reason=multiple-stage-headings");return false;
                }
                heading=line;stage=resolved;area=candidate;background=paper;
            }
            if(heading!=null)record=SelectConfirmedJournal(stage,JournalProse(sorted,GetOcrLineBounds(heading),area,false),detail,out reason);
            if(heading!=null&&record==null&&detail!=null&&detail.Count>0)
                record=SelectConfirmedJournal(stage,JournalPanelProse(detail,stage),detail,out reason);
            if(heading==null) {
                // One general path for heading-less, scrolled and server-variant
                // journals. No quest name or quantity is encoded in this procedure.
                foreach(OcrLine line in sorted) {
                    string key=TranslationStore.Normalize(line.Text);
                    if(key.Length<5||key.StartsWith("level ",StringComparison.Ordinal)||QuestDetailGeometry.IsSummaryKey(key)||
                        QuestJournalLayout.IsCounterLine(line.Text,GetOcrLineBounds(line),objectiveCards,scale))continue;
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
                    if(record==null)record=translations.SelectJournalForProse(prose,out reason);
                    if(record==null&&detail!=null&&detail.Count>0)
                        record=translations.SelectJournalForProse(JournalPanelProse(detail),out reason);
                }
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
                if(expanded.IsEmpty){Program.LogRuntimeEvent("quest-journal","selected=false reason=region-does-not-own-complete-body");return false;}
                area=expanded;
                // Objective icons/counters are live game surfaces. If one still sits
                // in the replacement region, trim the region below it when it is a
                // top card; anything deeper means the measured body is unsafe.
                foreach(RectangleF card in objectiveCards) {
                    if(!card.IntersectsWith(area))continue;
                    bool topCard=card.Bottom<=area.Top+area.Height*0.40f;
                    if(!topCard){Program.LogRuntimeEvent("quest-journal","selected=false reason=objective-surface-inside-body");return false;}
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
                        Program.LogRuntimeEvent("quest-journal","selected=false reason=partial-body-row-after-protection");
                        return false;
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
                Program.LogRuntimeEvent("quest-journal","selected=false reason="+lastQuestJournalRejection);
                return false;
            }
            RectangleF overlay=OverlaySpan(area,scale);
            Rectangle overlayArea=Rectangle.Ceiling(overlay);
            float fontHeight=Math.Max(14,Math.Min(40,GetOcrLineBounds(heading).Height/scale));
            JournalLayoutPlan layout=null;
            using(Bitmap probe=new Bitmap(1,1))
            using(Graphics measure=Graphics.FromImage(probe)) {
                measure.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                layout=QuestJournalLayout.Build(measure,overlayArea,fontHeight,record.ChineseTitle,record.Chinese);
            }
            if(layout==null){Program.LogRuntimeEvent("quest-journal","selected=false reason=journal-layout-does-not-fit region="+
                overlayArea.Left+","+overlayArea.Top+","+overlayArea.Width+"x"+overlayArea.Height);return false;}
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
                    key=="quest helper"||key=="forfeit"||key=="details"||key=="available"||
                    key=="in progress"||key=="completed"||
                    QuestJournalLayout.IsCounterLine(line.Text,raw,objectiveCards,scale))continue;
                bool inCard=false;
                if(objectiveCards!=null)foreach(RectangleF card in objectiveCards)
                    if(card.Contains(new PointF(raw.Left+raw.Width/2,raw.Top+raw.Height/2))){inCard=true;break;}
                if(inCard)continue;
                // Only a structurally confirmed stage title may be skipped. In a
                // titleless/scroll path the heading is a body locator and must be
                // checked like every other visible prose row.
                bool confirmedTitle=line==heading&&stage.Length>0&&
                    TranslationStore.Normalize(translations.ResolveJournalStage(line.Text))==
                    TranslationStore.Normalize(stage);
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
                if(!candidateNumbers.Contains(number)){reason="visible-quantity-not-in-record:"+number;return false;}
                if(!translatedNumbers.Contains(number)){reason="visible-quantity-not-in-translation:"+number;return false;}
            }
            // Align the complete visible prose, not each row independently: the
            // ordered sequence catches cross-line reordering as well as negation or
            // inserted words. OCR-tolerant token matching is bounded below.
            string alignReason;
            if(!JournalLineAlignsToSource(visible.ToString(),record.English,out alignReason)) {
                reason="visible-prose-"+alignReason;
                return false;
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
                    if(JournalTokenMatches(token,sourceToken)) {
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

        private static bool JournalTokenMatches(string left,string right)
        {
            bool leftDigit=JournalTokenHasDigit(left),rightDigit=JournalTokenHasDigit(right);
            // A number is not interchangeable with another number: "1 Green ... 10
            // Blue" may not align as "10 Green ... 1 Blue". Exact numeric tokens keep
            // the quantity bound to its neighbouring item/prose tokens.
            if(leftDigit||rightDigit)
                return leftDigit&&rightDigit&&String.Equals(left,right,StringComparison.Ordinal);
            if(String.Equals(left,right,StringComparison.Ordinal))return true;
            if(left.Length<4||right.Length<4)return false;
            int longest=Math.Max(left.Length,right.Length);
            if(Math.Abs(left.Length-right.Length)>2)return false;
            int limit=Math.Max(1,longest/5);
            int distance=JournalEditDistance(left,right,limit);
            return distance>=0&&distance<=limit;
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
        private QuestJournalRecord SelectConfirmedJournal(string stage,string observed,
            List<OcrLine> lines,out string reason)
        {
            QuestJournalRecord record=translations.SelectJournal(stage,observed,out reason);
            if(record!=null)return record;
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
                for(int start=0;start<ordered.Count&&!foundFull;start++) {
                    StringBuilder window=new StringBuilder();
                    for(int span=0;span<7&&start+span<ordered.Count;span++) {
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

        private static bool JournalPaper(Color color)
        {return color.R>=245&&color.G>=245&&color.B>=240;}

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
            StringBuilder result=new StringBuilder();foreach(OcrLine line in lines){RectangleF raw=GetOcrLineBounds(line);
                if((includeHeading||raw!=heading)&&area.Contains(Rectangle.Ceiling(raw)))result.Append(line.Text).Append(' ');}
            return result.ToString();
        }
    }
}
