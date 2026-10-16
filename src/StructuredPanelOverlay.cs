using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        private RectangleF OverlaySpan(RectangleF raw,float scale)
        {return new RectangleF(ToOverlayX(raw.Left,scale),ToOverlayY(raw.Top,scale),raw.Width/scale,raw.Height/scale);}

        // Unlike the old whole-word span, this also separates INT+5 when OCR
        // emitted it as one word. A missing value is never supplied by a glossary.
        private static RectangleF PreciseTextSpan(List<OcrLine> lines,string text,int rawStart,int rawLength)
        {
            int start=TranslationStore.Normalize(text.Substring(0,rawStart)).Length;
            if(start>0)start=TranslationStore.Normalize(text).IndexOf(TranslationStore.Normalize(text.Substring(rawStart,rawLength)),Math.Min(start,TranslationStore.Normalize(text).Length),StringComparison.Ordinal);
            if(start<0)return RectangleF.Empty;
            int end=start+TranslationStore.Normalize(text.Substring(rawStart,rawLength)).Length;
            string all=TranslationStore.Normalize(text);int cursor=0;RectangleF bounds=RectangleF.Empty;
            foreach(OcrLine line in lines)foreach(OcrWord word in line.Words) {
                string value=TranslationStore.Normalize(word.Text);if(value.Length==0)continue;
                int found=all.IndexOf(value,Math.Min(cursor,all.Length),StringComparison.Ordinal);if(found<0)found=cursor;
                int left=Math.Max(start,found),right=Math.Min(end,found+value.Length);
                if(right>left) {
                    RectangleF box=new RectangleF((float)word.BoundingRect.X+(float)word.BoundingRect.Width*(left-found)/value.Length,
                        (float)word.BoundingRect.Y,(float)word.BoundingRect.Width*(right-left)/value.Length,(float)word.BoundingRect.Height);
                    bounds=bounds.IsEmpty?box:RectangleF.Union(bounds,box);
                }
                cursor=Math.Min(all.Length,found+value.Length);
            }
            return bounds;
        }

        private void AddStructuredFieldLabels(List<OverlayLabel> output,List<OcrLine> all,
            Dictionary<OcrLine,OcrPanelInfo> panels,float scale)
        {
            int parsed=0,added=0;HashSet<OcrPanelInfo> used=new HashSet<OcrPanelInfo>();
            foreach(OcrLine origin in all) {
                OcrPanelInfo panel;panels.TryGetValue(origin,out panel);
                bool equipment=panel!=null&&panel.IsEquipmentDetail;
                if(panel!=null&&!used.Add(panel))continue;
                List<OcrLine> source=panel!=null?panel.Lines:new List<OcrLine>{origin};
                foreach(List<OcrLine> row in BuildVisualRows(source)) {
                    string text=CombineVisualRow(row);List<EquipmentField> fields=EquipmentFieldGrammar.Parse(text,equipment);
                    if(fields.Count==0)continue;parsed+=fields.Count;
                    RectangleF rawRow=GetCombinedLineBounds(row),localRow=OverlaySpan(rawRow,scale);
                    List<OverlayLabel> replacements=new List<OverlayLabel>();
                    foreach(EquipmentField field in fields) {
                        RectangleF name=PreciseTextSpan(row,text,field.Start,field.Length);
                        if(name.IsEmpty)continue;
                        RectangleF value=field.ValueLength>0?PreciseTextSpan(row,text,field.ValueStart,field.ValueLength):RectangleF.Empty;
                        string chinese=field.Chinese;RectangleF sourceBox=name;RectangleF guard=value;
                        // A truly merged key/value word has no clean geometric gap. Render
                        // that complete observed field, including its unchanged raw number.
                        if(!value.IsEmpty&&value.Left-name.Right<2*scale) {
                            sourceBox=RectangleF.Union(name,value);guard=RectangleF.Empty;
                            chinese+="："+text.Substring(field.ValueStart,field.ValueLength);
                        }
                        RectangleF box=OverlaySpan(sourceBox,scale);
                        replacements.Add(new OverlayLabel{Bounds=box,SourceBounds=box,HasSourceBounds=true,
                            SourceLineBounds=new List<RectangleF>{box},Text=chinese,SemanticRole="item-field",
                            MatchSource="field:"+field.Key,KeepClearBounds=guard.IsEmpty?RectangleF.Empty:OverlaySpan(guard,scale),
                            Confidence=LabelConfidence.High});
                    }
                    if(replacements.Count==0)continue;
                    output.RemoveAll(delegate(OverlayLabel label){return label.MatchSource!="stage-journal" &&
                        (label.SemanticRole=="item-field"||String.IsNullOrEmpty(label.SemanticRole)) &&
                        localRow.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                    output.AddRange(replacements);added+=replacements.Count;
                }
            }
            Program.LogRuntimeEvent("field-coverage","parsed="+parsed+" planned="+added);
        }

        private void AddSkillEffectSections(List<OverlayLabel> output,List<OcrLine> all,Dictionary<OcrLine,OcrPanelInfo> panels,float scale)
        {
            List<OcrLine> sorted=new List<OcrLine>(all);sorted.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
            int observed=0,translated=0;
            for(int i=0;i<sorted.Count;i++) {
                string key=TranslationStore.Normalize(sorted[i].Text);
                if(SkillSectionMarker(sorted[i].Text)!="current"&&SkillSectionMarker(sorted[i].Text)!="next")continue;
                observed++;string skillId=ResolveSkillContextId(all,sorted[i],scale);OcrPanelInfo parent;
                if(skillId.Length==0&&panels.TryGetValue(sorted[i],out parent))skillId=parent.SkillId??"";
                if(skillId.Length==0)for(int previous=i-1;previous>=0;previous--) {
                    RectangleF prior=GetOcrLineBounds(sorted[previous]);
                    if(GetOcrLineBounds(sorted[i]).Top-prior.Bottom>600*scale)break;
                    if(Math.Abs(prior.Left-GetOcrLineBounds(sorted[i]).Left)>80*scale)continue;
                    if(panels.TryGetValue(sorted[previous],out parent)&&!String.IsNullOrEmpty(parent.SkillId)){skillId=parent.SkillId;break;}
                }
                List<OcrLine> rows=new List<OcrLine>();string best="";int bestRows=0;
                RectangleF header=GetOcrLineBounds(sorted[i]);StringBuilder text=new StringBuilder();
                string inline=StripSkillStructuredPrefix(sorted[i].Text);
                bool hasInline=inline!=sorted[i].Text.Trim()&&SkillStructuredBodyStart(sorted[i].Text)>0;
                if(hasInline){
                    rows.Add(sorted[i]);text.Append(inline);
                    string firstChinese;if(translations.TryTranslateSkillEffect(text.ToString(),skillId,out firstChinese)){best=firstChinese;bestRows=1;}
                }
                for(int j=i+1;j<sorted.Count&&rows.Count<16;j++) {
                    OcrLine line=sorted[j];RectangleF raw=GetOcrLineBounds(line);string lineKey=TranslationStore.Normalize(line.Text);
                    if(raw.Top<header.Bottom)continue;
                    if(raw.Left>header.Left+80*scale||raw.Right<header.Left-10*scale)continue;
                    if(SkillSectionMarker(line.Text).Length>0)break;
                    RectangleF last=rows.Count>0?GetOcrLineBounds(rows[rows.Count-1]):header;
                    if(raw.Top-last.Bottom>Math.Max(34*scale,last.Height*2.5f))break;
                    if(raw.Right<header.Left-10*scale||raw.Left>header.Right+300*scale)continue;
                    if(text.Length>0)text.Append(' ');text.Append(line.Text);rows.Add(line);
                    string chinese;if(translations.TryTranslateSkillEffect(text.ToString(),skillId,out chinese)){best=chinese;bestRows=rows.Count;}
                }
                if(bestRows==0)continue;
                List<OcrLine> accepted=rows.GetRange(0,bestRows);RectangleF rawBody=GetCombinedLineBounds(accepted);
                if(hasInline) {
                    string phrase=key.StartsWith("next",StringComparison.Ordinal)?"next level":"current level";
                    string level=SkillLevelNumber(sorted[i].Text,key,phrase);
                    best=(phrase=="next level"?"下一级":"当前等级")+"："+level+"　"+best;
                }
                RectangleF owned=OverlaySpan(rawBody,scale);
                output.RemoveAll(delegate(OverlayLabel label){return label.SemanticRole!="skill-name"&&label.SemanticRole!="skill-body"&&
                    (!label.SemanticRole.EndsWith("-level",StringComparison.Ordinal)||hasInline)&&owned.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                AddDetailBlockLabelAt(output,accepted,rawBody,best,scale,accepted.Count>1);
                OverlayLabel result=output[output.Count-1];result.SemanticRole=key.StartsWith("next",StringComparison.Ordinal)?"skill-next-body":"skill-current-body";
                result.MatchSource="skill-effect-template";result.Confidence=LabelConfidence.High;translated++;
            }
            Program.LogRuntimeEvent("skill-effect-coverage","observed="+observed+" planned="+translated);
        }

        private void AddObjectiveSurfaceLabels(List<OverlayLabel> output,List<OcrLine> all,Bitmap prepared,
            RectangleF owner,float scale)
        {
            if(prepared==null||owner.IsEmpty)return;
            List<RectangleF> cards=QuestObjectiveSurface.Find(ReadSimplePixels(prepared),prepared.Width,prepared.Height,owner,scale);
            int rowsSeen=0,names=0;
            foreach(RectangleF card in cards) {
                List<OcrLine> lines=all.FindAll(delegate(OcrLine line){RectangleF r=GetOcrLineBounds(line);return card.Contains(new PointF(r.Left+r.Width/2,r.Top+r.Height/2));});
                foreach(List<OcrLine> row in BuildVisualRows(lines)) {
                    string text=CombineVisualRow(row);
                    Match count=QuestObjectiveSurface.Counter(text);
                    rowsSeen++;
                    string source=(count.Success?text.Substring(0,count.Index):text).Trim();TranslationEntry entry=translations.ResolveObjectiveName(source);
                    if(entry==null)continue;
                    RectangleF raw=PreciseTextSpan(row,text,0,source.Length),value=count.Success?PreciseTextSpan(row,text,count.Index,count.Length):RectangleF.Empty;
                    if(raw.IsEmpty||(count.Success&&value.IsEmpty))continue;
                    // A no-icon row often has no geometric gap between the noun
                    // and counter inside one OCR word. Reserve the counter first.
                    if(count.Success&&raw.Right+2*scale>=value.Left) {
                        float right=Math.Min(raw.Right,value.Left-2*scale);
                        if(right-raw.Left<8*scale)continue;raw=RectangleF.FromLTRB(raw.Left,raw.Top,right,raw.Bottom);
                    }
                    RectangleF box=OverlaySpan(raw,scale);
                    output.RemoveAll(delegate(OverlayLabel label){return box.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                    output.Add(new OverlayLabel{Bounds=box,SourceBounds=box,HasSourceBounds=true,SourceLineBounds=new List<RectangleF>{box},
                        Text=entry.Chinese,SemanticRole="task-objective",KeepClearBounds=value.IsEmpty?RectangleF.Empty:OverlaySpan(value,scale),
                        MatchSource="objective-surface",Confidence=LabelConfidence.High,PanelBounds=count.Success?OverlaySpan(card,scale):box});names++;
                }
            }
            Program.LogRuntimeEvent("objective-coverage","cards="+cards.Count+" rows="+rowsSeen+" planned="+names);
        }
    }
}
