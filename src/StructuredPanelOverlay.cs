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

        private static bool HasLetterOrDigit(string value)
        {
            if(String.IsNullOrEmpty(value))return false;
            foreach(char c in value)if(Char.IsLetterOrDigit(c))return true;
            return false;
        }

        // Unlike the old whole-word span, this also separates INT+5 when OCR
        // emitted it as one word. A missing value is never supplied by a glossary.
        // The bool reports whether every intersected OCR word was covered whole.
        // A clipped word box is a proportional estimate, not a measured glyph edge.
        private static RectangleF PreciseTextSpan(List<OcrLine> lines,string text,int rawStart,int rawLength)
        {bool measured;return PreciseTextSpan(lines,text,rawStart,rawLength,out measured,false);}

        private static RectangleF PreciseTextSpan(List<OcrLine> lines,string text,int rawStart,int rawLength,
            out bool measured)
        {return PreciseTextSpan(lines,text,rawStart,rawLength,out measured,false);}

        private static RectangleF PreciseTextSpan(List<OcrLine> lines,string text,int rawStart,int rawLength,
            out bool measured,bool allowPunctuationTrim)
        {
            measured=false;
            if(lines==null||String.IsNullOrEmpty(text)||rawLength<=0)return RectangleF.Empty;
            int spanStart=Math.Max(0,Math.Min(rawStart,text.Length));
            int spanEnd=Math.Min(text.Length,spanStart+rawLength);
            if(spanEnd<=spanStart)return RectangleF.Empty;
            RectangleF bounds=RectangleF.Empty;
            bool foundAny=false,clean=true;
            int cursor=0;
            foreach(OcrLine line in lines) {
                if(line==null||String.IsNullOrEmpty(line.Text))continue;
                int lineStart=text.IndexOf(line.Text,cursor,StringComparison.Ordinal);
                if(lineStart<0){lineStart=cursor;clean=false;}
                int wordCursor=0;
                foreach(OcrWord word in line.Words) {
                    string raw=word.Text??"";if(raw.Length==0)continue;
                    int local=line.Text.IndexOf(raw,Math.Min(wordCursor,line.Text.Length),StringComparison.Ordinal);
                    if(local<0){local=wordCursor;clean=false;}
                    int absoluteStart=lineStart+local,absoluteEnd=absoluteStart+raw.Length;
                    wordCursor=Math.Min(line.Text.Length,local+raw.Length);
                    int left=Math.Max(spanStart,absoluteStart),right=Math.Min(spanEnd,absoluteEnd);
                    if(right<=left)continue;
                    foundAny=true;
                    if(left>absoluteStart||right<absoluteEnd) {
                        // A key word may keep an attached colon/period outside the
                        // replaced key; that punctuation-only trim is safe. A clipped
                        // value word is never safe, even when the clipped character is
                        // punctuation, because the sign/number edge would be estimated.
                        bool clippedValue=false;
                        if(left>absoluteStart)clippedValue=HasLetterOrDigit(raw.Substring(0,left-absoluteStart));
                        if(right<absoluteEnd)clippedValue=clippedValue||HasLetterOrDigit(raw.Substring(right-absoluteStart));
                        if(!allowPunctuationTrim||clippedValue)clean=false;
                    }
                    // Use the raw OCR word box, including sign/punctuation-only words
                    // that normalize to an empty string. Splitting inside a word is a
                    // proportional estimate and is reported as such.
                    RectangleF box=new RectangleF((float)word.BoundingRect.X+(float)word.BoundingRect.Width*(left-absoluteStart)/raw.Length,
                        (float)word.BoundingRect.Y,(float)word.BoundingRect.Width*(right-left)/raw.Length,(float)word.BoundingRect.Height);
                    if(allowPunctuationTrim && (left>absoluteStart||right<absoluteEnd)) {
                        bool lettersOutside=(left>absoluteStart&&HasLetterOrDigit(raw.Substring(0,left-absoluteStart)))||
                            (right<absoluteEnd&&HasLetterOrDigit(raw.Substring(right-absoluteStart)));
                        // Punctuation does not provide character geometry. Own the
                        // entire measured key word, including its colon/period,
                        // instead of proportionally cutting the last letter off.
                        if(!lettersOutside)box=word.BoundingRect;
                    }
                    bounds=bounds.IsEmpty?box:RectangleF.Union(bounds,box);
                }
                cursor=Math.Min(text.Length,lineStart+line.Text.Length);
            }
            measured=foundAny&&clean;
            return bounds;
        }

        private void AddStructuredFieldLabels(List<OverlayLabel> output,List<OcrLine> all,
            Dictionary<OcrLine,OcrPanelInfo> panels,float scale,HashSet<OcrLine> excluded=null)
        {
            int parsed=0,added=0,recognizedPanels=0,recognizedRows=0;
            int valuePixels=0,partialValues=0,mergedText=0,preservedSafety=0,rejected=0;
            int measuredValues=0,estimatedValues=0;
            string keys="",preservedReasons="";
            HashSet<OcrPanelInfo> used=new HashSet<OcrPanelInfo>();
            foreach(OcrLine origin in all) {
                OcrPanelInfo panel;panels.TryGetValue(origin,out panel);
                bool equipment=panel!=null&&panel.IsEquipmentDetail;
                if(panel!=null&&!used.Add(panel))continue;
                List<OcrLine> source=new List<OcrLine>(panel!=null?panel.Lines:new List<OcrLine>{origin});
                if(excluded!=null)source.RemoveAll(delegate(OcrLine line){return excluded.Contains(line);});
                if(source.Count==0)continue;
                List<List<OcrLine>> visualRows=BuildVisualRows(source);
                RectangleF rawPanel=panel!=null?GetCombinedLineBounds(source):GetOcrLineBounds(origin);
                RectangleF panelBounds=rawPanel.IsEmpty?RectangleF.Empty:OverlaySpan(rawPanel,scale);
                List<RectangleF> rowGeometry=new List<RectangleF>();
                foreach(List<OcrLine> geometryRow in visualRows) {
                    RectangleF raw=GetCombinedLineBounds(geometryRow);
                    if(!raw.IsEmpty)rowGeometry.Add(OverlaySpan(raw,scale));
                }
                EquipmentPanelTypography typography=EquipmentPanelTypography.ForRows(rowGeometry,panelBounds);
                EquipmentPanelPlan panelPlan=new EquipmentPanelPlan{PanelBounds=panelBounds,
                    Typography=typography,RecognitionReason=equipment?(panel.EquipmentReason??""):""};
                if(equipment)recognizedPanels++;
                foreach(List<OcrLine> row in visualRows) {
                    string text=CombineVisualRow(row);
                    bool rowEquipment=equipment||EquipmentFieldGrammar.LooksLikeEquipmentRow(text);
                    List<EquipmentField> fields=EquipmentFieldGrammar.Parse(text,rowEquipment);
                    if(fields.Count==0)continue;parsed+=fields.Count;
                    if(rowEquipment)recognizedRows++;
                    RectangleF rawRow=GetCombinedLineBounds(row),localRow=OverlaySpan(rawRow,scale);
                    List<OverlayLabel> replacements=new List<OverlayLabel>();
                    foreach(EquipmentField field in fields) {
                        bool nameMeasured,valueMeasured=true;
                        RectangleF name=PreciseTextSpan(row,text,field.Start,field.Length,out nameMeasured,true);
                        if(name.IsEmpty){rejected++;continue;}
                        RectangleF value=field.ValueLength>0?PreciseTextSpan(row,text,field.ValueStart,field.ValueLength,out valueMeasured,false):RectangleF.Empty;
                        // Both key and value boundaries must be measured. A value box
                        // sitting to the right must never upgrade a key box that clipped
                        // letters (for example INT inside POINT). Attached punctuation on
                        // a key word is already treated as a safe key trim by
                        // PreciseTextSpan(..., allowPunctuationTrim:true).
                        bool measuredBounds=nameMeasured&&valueMeasured;
                        EquipmentFieldPlan fieldPlan=EquipmentPanelLayout.BuildField(field,text,
                            OverlaySpan(name,scale),value.IsEmpty?RectangleF.Empty:OverlaySpan(value,scale),
                            localRow,panelPlan.PanelBounds,panelPlan.Typography,measuredBounds);
                        if(fieldPlan==null){rejected++;continue;}
                        panelPlan.Add(fieldPlan);
                        if(fieldPlan.BoundaryMode=="measured")measuredValues++;
                        else estimatedValues++;
                        if(fieldPlan.ValuePixelsPreserved)valuePixels++;
                        if(fieldPlan.PartialValue)partialValues++;
                        if(fieldPlan.MergedValueBaked)mergedText++;
                        if(keys.Length<=180)keys+=(keys.Length>0?",":"")+field.Key;
                        else if(!keys.EndsWith("...",StringComparison.Ordinal))keys+="...";
                        if(fieldPlan.Status!="prepared"){
                            preservedSafety++;
                            if(preservedReasons.Length<160){
                                if(preservedReasons.Length>0)preservedReasons+=",";
                                preservedReasons+=field.Key+":"+fieldPlan.PreservedReason;
                            }
                            continue;
                        }
                        RectangleF box=fieldPlan.KeySourceBounds;
                        replacements.Add(new OverlayLabel{Bounds=box,SourceBounds=box,HasSourceBounds=true,
                            SourceLineBounds=new List<RectangleF>{box},Text=fieldPlan.Translation,
                            SemanticRole="item-field",MatchSource=fieldPlan.MatchSource,
                            KeepClearBounds=fieldPlan.ValueGuardBounds,PanelBounds=fieldPlan.PanelBounds,
                            Confidence=LabelConfidence.High,EquipmentField=fieldPlan,EquipmentPanel=panelPlan});
                    }
                    // Even when every field is preserved (for example an unsafe
                    // merged value word), remove the old whole-row translation so
                    // the original value pixels stay exactly as captured.
                    output.RemoveAll(delegate(OverlayLabel label){return label.MatchSource!="stage-journal" &&
                        (label.SemanticRole=="item-field"||String.IsNullOrEmpty(label.SemanticRole)) &&
                        localRow.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                    if(replacements.Count>0){output.AddRange(replacements);added+=replacements.Count;}
                }
                int panelAdded=panelPlan.Prepared;
                if(panelAdded>0||equipment) {
                    string reason=panelPlan.RecognitionReason;
                    if(reason.Length==0&&panelAdded>0)reason="field-row-fallback";
                    Program.LogRuntimeEvent("equipment-panel",panelPlan.Describe()+
                        " reason="+(reason.Length==0?"unrecognized":reason)+
                        " sourceRows="+visualRows.Count);
                }
            }
            Program.LogRuntimeEvent("field-coverage","parsed="+parsed+" planned="+added);
            Program.LogRuntimeEvent("equipment-coverage","recognizedPanels="+recognizedPanels+
                " recognizedRows="+recognizedRows+" parsed="+parsed+" prepared="+added+
                " measuredValues="+measuredValues+" estimatedValues="+estimatedValues+
                " valuePixels="+valuePixels+" partialValues="+partialValues+
                " mergedText="+mergedText+" preserved="+preservedSafety+
                " preservedReason="+(preservedReasons.Length==0?"none":preservedReasons)+
                " rejected="+rejected+" keys="+keys);
        }

        private void AddSkillEffectSections(List<OverlayLabel> output,List<OcrLine> all,Dictionary<OcrLine,OcrPanelInfo> panels,float scale,HashSet<OcrPanelInfo> handled=null,HashSet<OcrLine> excluded=null)
        {
            List<OcrLine> sorted=new List<OcrLine>(all);sorted.Sort(delegate(OcrLine a,OcrLine b){return GetOcrLineBounds(a).Top.CompareTo(GetOcrLineBounds(b).Top);});
            int observed=0,translated=0;
            for(int i=0;i<sorted.Count;i++) {
                if(excluded!=null&&excluded.Contains(sorted[i]))continue;
                string key=TranslationStore.Normalize(sorted[i].Text);
                if(SkillSectionMarker(sorted[i].Text)!="current"&&SkillSectionMarker(sorted[i].Text)!="next")continue;
                observed++;OcrPanelInfo owner;if(handled!=null&&panels.TryGetValue(sorted[i],out owner)&&handled.Contains(owner))continue;string skillId=ResolveSkillContextId(all,sorted[i],scale);OcrPanelInfo parent;
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
