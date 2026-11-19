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

        internal static long MetricPreciseTicks;
        private static RectangleF PreciseTextSpan(List<OcrLine> lines,string text,int rawStart,int rawLength,
            out bool measured,bool allowPunctuationTrim)
        {
            long metricStart=System.Diagnostics.Stopwatch.GetTimestamp();
            try { return PreciseTextSpanCore(lines,text,rawStart,rawLength,out measured,allowPunctuationTrim); }
            finally { MetricPreciseTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-metricStart; }
        }
        private static RectangleF PreciseTextSpanCore(List<OcrLine> lines,string text,int rawStart,int rawLength,
            out bool measured,bool allowPunctuationTrim)
        {
            measured=false;
            if(lines==null||String.IsNullOrEmpty(text)||rawLength<=0)return RectangleF.Empty;
            int spanStart=Math.Max(0,Math.Min(rawStart,text.Length));
            int spanEnd=Math.Min(text.Length,spanStart+rawLength);
            if(spanEnd<=spanStart)return RectangleF.Empty;
            RectangleF bounds=RectangleF.Empty;
            bool foundAny=false,clean=true;
            // RepairEquipmentTokens may rewrite a misread word in place without
            // changing its length ("AEQ" -> "REQ"), so the repaired text no longer
            // contains the raw line verbatim. When the concatenated raw length still
            // matches the repaired text, every row offset is unchanged and the word
            // geometry stays valid; only a length change is an unsafe estimate.
            int expectedLength=0,lineCount=0;
            foreach(OcrLine known in lines) {
                if(known==null||String.IsNullOrEmpty(known.Text))continue;
                expectedLength+=known.Text.Length;lineCount++;
            }
            if(lineCount>1)expectedLength+=lineCount-1;
            bool sameLengthRepair=expectedLength==text.Length;
            int cursor=0;
            foreach(OcrLine line in lines) {
                if(line==null||String.IsNullOrEmpty(line.Text))continue;
                int lineStart=text.IndexOf(line.Text,cursor,StringComparison.Ordinal);
                if(lineStart<0){lineStart=cursor;if(!sameLengthRepair)clean=false;}
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

        private static readonly string[] EquipmentTokenCandidates = new string[] {
            "REQ","STR","DEX","INT","LUK","LEV","LEVEL","FAME","TYPE","CATEGORY","CAPE",
            "SHOES","GLOVES","HAT","SHIELD","OVERALL","WEAPON","MAGIC","ATTACK","DEFENSE",
            "SPEED","JUMP","ACCURACY","EVASION","REMAINING","ENHANCEMENTS","UPGRADE",
            "SLOTS","CRITICAL","RATE","DAMAGE","BOSS","HP","MP"
        };

        private static readonly HashSet<string> KnownEquipmentTokens =
            new HashSet<string>(EquipmentTokenCandidates, StringComparer.Ordinal);

        // Windows OCR misreads the client's 15 px Latin face ("REQ" arrives as "AEQ");
        // every other row then parses while this one is dropped. Re-read only words that
        // are not already a known equipment word, using the game face rendered at the
        // measured ink height, and accept a winner only when it is clearly ahead.
        private string RepairEquipmentTokens(int[] pixels, int width, int height,
            List<OcrLine> row, string text, float scale)
        {
            if (pixels == null || row == null || row.Count == 0) return text;
            StringBuilder rebuilt = new StringBuilder();
            foreach (OcrLine line in row)
            {
                if (line == null || String.IsNullOrEmpty(line.Text)) continue;
                string lineText = line.Text;
                foreach (OcrWord word in line.Words)
                {
                    if (word == null || String.IsNullOrEmpty(word.Text)) continue;
                    string token = word.Text.Trim().ToUpperInvariant();
                    if (token.Length < 2 || token.Length > 13) continue;
                    bool lettersOnly = true;
                    foreach (char c in token)
                        if (!Char.IsLetter(c)) { lettersOnly = false; break; }
                    if (!lettersOnly || KnownEquipmentTokens.Contains(token)) continue;
                    string repaired;
                    float confidence;
                    bool matched = GlyphTemplateMatcher.TryRepair(pixels, width, height,
                        word.BoundingRect, token, EquipmentTokenCandidates, out repaired, out confidence);
                    if (Program.Benchmark)
                        Program.LogRuntimeEvent("glyph-try", "token=" + token + " ok=" + matched +
                            " best=" + confidence.ToString("0.00") + " pick=" + (repaired ?? ""));
                    if (!matched) continue;
                    if (String.Equals(repaired, token, StringComparison.Ordinal)) continue;
                    int at = lineText.IndexOf(word.Text, StringComparison.Ordinal);
                    if (at < 0) continue;
                    lineText = lineText.Substring(0, at) + repaired +
                        lineText.Substring(at + word.Text.Length);
                    Program.LogRuntimeEvent("glyph-repair", "from=" + token + " to=" + repaired +
                        " score=" + confidence.ToString("0.00"));
                }
                if (rebuilt.Length > 0) rebuilt.Append(' ');
                rebuilt.Append(lineText);
            }
            return rebuilt.Length > 0 ? rebuilt.ToString() : text;
        }
        private void AddStructuredFieldLabels(List<OverlayLabel> output,List<OcrLine> all,
            Dictionary<OcrLine,OcrPanelInfo> panels,float scale,HashSet<OcrLine> excluded=null,
            Bitmap frame=null)
        {
            int[] framePixels=frame==null?null:ReadSimplePixels(frame);
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
                    string combinedText=CombineVisualRow(row);
                    if(framePixels!=null) combinedText=RepairEquipmentTokens(framePixels,frame.Width,
                        frame.Height,row,combinedText,scale);
                    string text=combinedText;

                    // The visual row that owns the tooltip title also contains the type
                    // word ("Black Salt Shoes"), so the field grammar would replace the
                    // whole title with "类型：鞋子". The item-name planner owns that row.
                    if(equipment) {
                        ItemNameMatch title=translations.FindItemNameInLine(text);
                        if(title!=null&&title.Chinese.Length>0&&title.Length*2>=text.Length)continue;
                    }
                    bool rowEquipment=equipment||EquipmentFieldGrammar.LooksLikeEquipmentRow(text);
                    List<EquipmentField> fields=EquipmentFieldGrammar.Parse(text,rowEquipment);
                    if(equipment) {
                        // The class ribbon of an equips tooltip is a whole-row field, not an
                        // attribute/value pair, so it is parsed outside the grammar. It may
                        // share the merged OCR row with LUK/MaxMP fields, so it is added
                        // alongside them instead of only when they are absent.
                        string ribbonChinese;int ribbonStart,ribbonLength;
                        if(ItemClassRibbon.TryParse(text,equipment,BuildRibbonUsable(row,framePixels,frame==null?0:frame.Width,frame==null?0:frame.Height,text),out ribbonChinese,out ribbonStart,out ribbonLength))
                            fields.Add(new EquipmentField{Start=ribbonStart,Length=ribbonLength,
                                Key="jobs",Chinese=ribbonChinese});
                    }
                    if(fields.Count==0)continue;parsed+=fields.Count;
                    if(rowEquipment)recognizedRows++;
                    RectangleF rawRow=GetCombinedLineBounds(row),localRow=OverlaySpan(rawRow,scale);
                    List<OverlayLabel> replacements=new List<OverlayLabel>();
                    foreach(EquipmentField field in fields) {
                        // A stat word inside a sentence ("Press the left Alt key to jump.")
                        // is prose, not an attribute row. Only a key that actually carries
                        // a measured value may be repainted as an equipment field; the
                        // class ribbon is the one whole-row field without a value.
                        if(!field.HasValue&&field.Key!="jobs"){rejected++;continue;}
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
                        if(equipment&&Program.Benchmark)
                            Program.LogRuntimeEvent("equipment-field-probe","row=["+text+"] key="+field.Key+
                                " start="+field.Start+" len="+field.Length+" nameMeasured="+nameMeasured+
                                " valueMeasured="+valueMeasured+" name="+name.X.ToString("0.0")+","+
                                name.Y.ToString("0.0")+","+name.Width.ToString("0.0")+","+name.Height.ToString("0.0"));
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
                    string preview="";
                    if(equipment||panelAdded>0||recognizedRows>0) {
                        // The raw text is the only way to tell "OCR read three rows" from
                        // "OCR read the rows but the grammar rejected them".
                        foreach(OcrLine line in source) {
                            if(line==null||String.IsNullOrEmpty(line.Text))continue;
                            if(preview.Length>0)preview+=" | ";
                            preview+=line.Text;
                            if(preview.Length>320)break;
                        }
                        if(preview.Length>320)preview=preview.Substring(0,320);
                    }
                    Program.LogRuntimeEvent("equipment-panel",panelPlan.Describe()+
                        " reason="+(reason.Length==0?"unrecognized":reason)+
                        " sourceRows="+visualRows.Count+
                        (preview.Length==0?"":" text=["+preview+"]"));
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
            // A translucent tooltip over a bright map can lose its DarkBlue panel
            // (or have its rows absorbed by a neighbouring NPC bubble), while the
            // requirement rows themselves OCR cleanly. The panel pass then skips the
            // whole block. Re-read every unclaimed row with the same equipment grammar
            // and paint only keys whose measured glyph box is exact; values keep their
            // original pixels. This is a generic row-level fallback, not a per-item rule.
            bool skillPanelPresent=false;
            if(panels!=null)foreach(OcrPanelInfo info in panels.Values)if(info!=null&&info.IsSkillDetail){skillPanelPresent=true;break;}
            AddOrphanFieldLabels(output,all,excluded,scale,panels,skillPanelPresent);
        }


        // Usable classes are printed in orange, unusable ones in dull grey. Sample the
        // token's own character range so a mixed ribbon keeps only the usable names.
        private Func<int,int,bool> BuildRibbonUsable(List<OcrLine> row,int[] pixels,int width,int height,string text)
        {
            if(pixels==null||row==null||row.Count==0||width<=0||height<=0||String.IsNullOrEmpty(text))return null;
            RectangleF raw=GetCombinedLineBounds(row);
            if(raw.IsEmpty)return null;
            return delegate(int from,int to) {
                // Measure the exact glyphs of this class word; a proportional slice of the
                // merged row lands between words and sampled the panel instead.
                RectangleF span=PreciseTextSpan(row,text,from,Math.Max(0,to-from));
                RectangleF basis=span.IsEmpty?raw:span;
                int left=(int)Math.Max(0,basis.Left);
                int right=(int)Math.Min(width-1,basis.Right);
                int top=(int)Math.Max(0,basis.Top);
                int bottom=(int)Math.Min(height-1,basis.Bottom);
                if(right<=left||bottom<=top)return true;
                int orange=0,ink=0;
                // ReadSimplePixels returns one ARGB int per pixel, not four bytes.
                // Indexing it as bytes overran the array by 4x and threw
                // IndexOutOfRangeException for every equipment tooltip that reached
                // the class-ribbon check, discarding the whole frame.
                for(int y=top;y<=bottom;y+=2)
                    for(int x=left;x<=right;x+=2) {
                        int pixel=pixels[y*width+x];
                        int b=pixel&0xFF,g=(pixel>>8)&0xFF,r=(pixel>>16)&0xFF;
                        // The tooltip background is navy (blue clearly above red/green).
                        // Anything warmer than that is glyph ink; usable classes are orange.
                        if(b>r+20&&b>g+10)continue;
                        if(r+g+b<200)continue;
                        ink++;
                        if(r>=160&&b<=150&&r-b>=40)orange++;
                    }
                return ink<4||orange*100>=ink*22;
            };
        }

        private void AddOrphanFieldLabels(List<OverlayLabel> output,List<OcrLine> all,
            HashSet<OcrLine> excluded,float scale,Dictionary<OcrLine,OcrPanelInfo> panels,bool skillPanel=false)
        {
            if(output==null||all==null||all.Count==0)return;
            int added=0,rows=0;
            foreach(OcrLine line in all) {
                if(line==null||String.IsNullOrEmpty(line.Text))continue;
                if(excluded!=null&&excluded.Contains(line))continue;
                string text=line.Text;
                // Long prose rows are never equipment rows; this keeps quest and
                // skill bodies out of the orphan pass.
                if(text.Length>80)continue;
                bool hasRequirementPrefix=Regex.IsMatch(text,@"(?i)(?:req(?:uired)?|reo|aeq|aeo|re0)");
                if(!EquipmentFieldGrammar.LooksLikeEquipmentRow(text)&&!hasRequirementPrefix)continue;
                List<EquipmentField> fields=EquipmentFieldGrammar.Parse(text,true);
                if(fields.Count==0)continue;
                // One bare key in ordinary text is noise; require a requirement
                // prefix or at least two fields on the row.
                if(!hasRequirementPrefix&&fields.Count<2)continue;
                RectangleF rawLine=GetOcrLineBounds(line);
                if(rawLine.IsEmpty)continue;
                RectangleF lineBox=OverlaySpan(rawLine,scale);
                if(lineBox.Width<4||lineBox.Height<4)continue;
                bool covered=false;
                foreach(OverlayLabel existing in output) {
                    RectangleF bounds=existing.HasSourceBounds?existing.SourceBounds:existing.Bounds;
                    RectangleF inter=RectangleF.Intersect(bounds,lineBox);
                    // Any overlap means the structured pass already owned this row
                    // (it paints every field of a claimed row in one go). A looser
                    // area threshold duplicated "生命值" on partially claimed rows.
                    if(inter.Width>2&&inter.Height>2){covered=true;break;}
                }
                if(covered)continue;
                int rowAdded=0;
                foreach(EquipmentField field in fields) {
                    bool measured;
                    RectangleF name=PreciseTextSpan(new List<OcrLine>{line},text,field.Start,
                        field.Length,out measured,true);
                    if(name.IsEmpty||!measured)continue;
                    RectangleF box=OverlaySpan(name,scale);
                    output.Add(new OverlayLabel{Bounds=box,SourceBounds=box,HasSourceBounds=true,
                        SourceLineBounds=new List<RectangleF>{box},Text=field.Chinese,
                        SemanticRole="item-field",MatchSource="orphan-field-fallback",
                        Confidence=LabelConfidence.High});
                    rowAdded++;
                }
                if(rowAdded>0){added+=rowAdded;rows++;}
            }
            if(added>0)Program.LogRuntimeEvent("orphan-field-labels",
                "added="+added+" rows="+rows);
            AddAttributeRowLabels(output,all,excluded,scale,panels,skillPanel);
        }

        // An attribute row is just "KEY : value". The three-letter stats and the common
        // combat keys are translated from the row text alone - no panel claim, no glyph
        // measurement - because a measured box that failed used to leave the whole row
        // English. The value is redrawn from the same text, so numbers stay as read.
        private static readonly Regex AttributeRowPattern = new Regex(
            @"^\s*[•·]?\s*(?<key>S\s*T\s*R|D\s*E\s*X|I\s*N\s*T|L\s*U\s*K|M\s*ax\s*H\s*P|M\s*ax\s*M\s*P|H\s*P|M\s*P|" +
            @"Weapon\s*(?:Attack|Att|Atk)|Magic\s*(?:Attack|Att|Atk)|Weapon\s*Def(?:ense)?|Magic\s*Def(?:ense)?|" +
            @"W\s*A\s*T\s*K|M\s*A\s*T\s*K|A\s*T\s*T|W\s*D\s*E\s*F|M\s*D\s*E\s*F|D\s*E\s*F|" +
            @"H\s*a\s*n\s*d\s*s|A\s*c\s*c(?:\s*uracy)?|A\s*v\s*o\s*i\s*d(?:\s*ability)?|E\s*v\s*a(?:\s*sion)?|" +
            @"A\s*l\s*l\s*S\s*t\s*a\s*t\s*s|D\s*a\s*m\s*a\s*g\s*e|T\s*y\s*p\s*e|" +
            @"Accuracy|Avoidability|Evasion|Critical\s*(?:Rate|Damage)|Attack\s*Speed|Movement\s*Speed|Speed|Jump|Fame|Level|L\s*E\s*V)\s*(?:[.:：]\s*)*[.:：]?\s*(?<value>[+\-]?\s*\d[\d,.]*\s*%?.*|[A-Za-z][A-Za-z ]{2,20})$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Requirement rows ("REQ STR : 0"). OCR splits the three-letter keys ("LU K") and
        // the panel can be unconfirmed, so this row-level pass translates the key from the
        // text alone and leaves the counter as read.
        private static readonly Regex RequirementRowPattern = new Regex(
            @"^\s*(?<req>REQ(?:UIRED)?|REO|AEQ|AEO|RE0)\s*[.:：]?\s*(?<key>L\s*E\s*V|L\s*E\s*V\s*E\s*L|S\s*T\s*R|D\s*E\s*X|I\s*N\s*T|L\s*U\s*K|F\s*A\s*M|F\s*A\s*M\s*E)\s*[:：.]?\s*(?<value>[0-9OoIlSsBbZzGq][0-9OoIlSsBbZzGq,]*)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string RequirementKeyChinese(string key)
        {
            string compact=Regex.Replace(key??"",@"\s+","").ToUpperInvariant();
            switch(compact) {
                case "LEV":case "LEVEL":return "需要等级";
                case "STR":return "需要力量";
                case "DEX":return "需要敏捷";
                case "INT":return "需要智力";
                case "LUK":return "需要运气";
                case "FAM":case "FAME":return "需要人气";
                default:return "";
            }
        }

        // Windows OCR swallows several bonus rows of an equips tooltip ("• LUK: +2",
        // "• Critical Rate: +2") into one long line, so they never become OcrLine rows and
        // the row pass cannot place them. Recover them from the raw text and stack them
        // under the last translated row. Boxes are approximate by construction.
        private void AddMergedTooltipAttributes(List<OverlayLabel> output,string fullText,float scale)
        {
            if(output==null||String.IsNullOrEmpty(fullText)||scale<=0)return;
            if(fullText.IndexOf('•')<0&&fullText.IndexOf('·')<0)return;
            RectangleF anchor=RectangleF.Empty;
            foreach(OverlayLabel label in output) {
                if(label==null||String.IsNullOrEmpty(label.MatchSource))continue;
                if(label.MatchSource!="attribute-row"&&!label.MatchSource.StartsWith("equipment-field:",StringComparison.Ordinal))continue;
                RectangleF bounds=label.HasSourceBounds?label.SourceBounds:label.Bounds;
                if(bounds.IsEmpty)continue;
                if(anchor.IsEmpty||bounds.Bottom>anchor.Bottom)anchor=bounds;
            }
            if(anchor.IsEmpty)return;
            HashSet<string> known=new HashSet<string>(StringComparer.Ordinal);
            foreach(OverlayLabel label in output) {
                if(label==null||String.IsNullOrEmpty(label.Text))continue;
                int cut=label.Text.IndexOf('：');
                if(cut>0)known.Add(label.Text.Substring(0,cut));
            }
            float y=anchor.Bottom+2.0f;
            int added=0;
            foreach(Match segment in Regex.Matches(fullText,@"[•·]\s*([^•·]+)")) {
                string piece=segment.Groups[1].Value.Trim();
                if(piece.Length<3)continue;
                Match match=AttributeRowPattern.Match(piece);
                if(!match.Success)continue;
                string chinese=AttributeKeyChinese(match.Groups["key"].Value);
                if(chinese.Length==0||known.Contains(chinese))continue;
                string value=AttributeValueChinese(chinese,match.Groups["value"].Value.Trim());
                if(value.Length==0)continue;
                RectangleF box=new RectangleF(anchor.Left,y,anchor.Width,18.0f);
                output.Add(new OverlayLabel{Bounds=box,SourceBounds=box,HasSourceBounds=true,
                    SourceLineBounds=new List<RectangleF>{box},Text=chinese+"："+value,
                    SemanticRole="item-field",MatchSource="attribute-merged",
                    Confidence=LabelConfidence.High});
                known.Add(chinese);
                y+=19.0f;
                added++;
            }
            if(added>0)Program.LogRuntimeEvent("attribute-merged","added="+added);
        }

        private static string AttributeValueChinese(string chinese,string value)
        {
            if(String.IsNullOrEmpty(value))return "";
            if(String.Equals(chinese,"类型",StringComparison.Ordinal))
            {
                string kind=Regex.Replace(value,@"[^A-Za-z]","").ToLowerInvariant();
                switch(kind) {
                    case "gloves":return "手套";
                    case "shoes":return "鞋子";
                    case "hat":return "帽子";
                    case "overall":return "套服";
                    case "top":return "上衣";
                    case "bottom":return "裤裙";
                    case "cape":return "披风";
                    case "weapon":return "武器";
                    case "shield":return "盾牌";
                    case "ring":return "戒指";
                    case "pendant":return "项链";
                    case "earrings":return "耳环";
                    case "belt":return "腰带";
                }
            }
            if(String.Equals(chinese,"攻击速度",StringComparison.Ordinal)||
                String.Equals(chinese,"移动速度",StringComparison.Ordinal))
            {
                string word=Regex.Replace(value,@"[^A-Za-z]","");
                switch(word.ToLowerInvariant()) {
                    case "average":return "普通";
                    case "fast":case "faster":case "fastest":return "快";
                    case "slow":case "slower":case "slowest":return "慢";
                    case "normal":return "正常";
                }
            }
            return value;
        }

        private static string AttributeKeyChinese(string key)
        {
            string compact=Regex.Replace(key??"",@"\s+","").ToUpperInvariant();
            switch(compact) {
                case "STR":return "力量";
                case "DEX":return "敏捷";
                case "INT":return "智力";
                case "LUK":return "运气";
                case "MAXHP":case "HP":return "生命值";
                case "MAXMP":case "MP":return "魔法值";
                case "WEAPONATTACK":case "WEAPONATT":case "WEAPONATK":return "物理攻击力";
                case "MAGICATTACK":case "MAGICATT":case "MAGICATK":return "魔法攻击力";
                case "WEAPONDEF":case "WEAPONDEFENSE":return "物理防御力";
                case "MAGICDEF":case "MAGICDEFENSE":return "魔法防御力";
                case "WATK":case "ATT":case "ATK":return "物理攻击力";
                case "MATK":return "魔法攻击力";
                case "WDEF":return "物理防御力";
                case "MDEF":return "魔法防御力";
                case "DEF":return "防御力";
                case "HANDS":return "手技";
                case "ACC":case "ACCURACY":return "命中率";
                case "AVOID":case "AVOIDABILITY":case "EVA":case "EVASION":return "回避率";
                case "ALLSTATS":return "全属性";
                case "DAMAGE":return "伤害";
                case "TYPE":return "类型";
                case "CRITICALRATE":return "暴击率";
                case "CRITICALDAMAGE":return "暴击伤害";
                case "ATTACKSPEED":return "攻击速度";
                case "MOVEMENTSPEED":case "SPEED":return "移动速度";
                case "JUMP":return "跳跃力";
                case "FAME":return "人气";
                case "LEVEL":case "LEV":return "等级";
                default:return "";
            }
        }


        // Rows above the class ribbon are requirements, rows below are bonuses. The ribbon
        // is the stable visual divider of every equips tooltip; when it is missing the first
        // non-REQ row ends the requirement block.
        private HashSet<OcrLine> BuildRequirementZone(List<OcrLine> all,
            Dictionary<OcrLine,OcrPanelInfo> panels)
        {
            HashSet<OcrLine> zone=new HashSet<OcrLine>();
            if(all==null||panels==null)return zone;
            Dictionary<OcrPanelInfo,List<OcrLine>> groups=new Dictionary<OcrPanelInfo,List<OcrLine>>();
            foreach(OcrLine line in all) {
                OcrPanelInfo panel;
                if(!panels.TryGetValue(line,out panel)||panel==null||!panel.IsEquipmentDetail)continue;
                List<OcrLine> list;
                if(!groups.TryGetValue(panel,out list)){list=new List<OcrLine>();groups.Add(panel,list);}
                list.Add(line);
            }
            foreach(KeyValuePair<OcrPanelInfo,List<OcrLine>> group in groups) {
                List<OcrLine> rows=group.Value;
                rows.Sort(delegate(OcrLine left,OcrLine right){
                    return GetOcrLineBounds(left).Top.CompareTo(GetOcrLineBounds(right).Top);});
                int ribbon=-1;
                for(int i=0;i<rows.Count;i++) {
                    string chinese;int start,length;
                    if(ItemClassRibbon.TryParse(rows[i].Text,true,out chinese,out start,out length))
                    {ribbon=i;break;}
                }
                if(ribbon<0) {
                    for(int i=0;i<rows.Count;i++) {
                        // A requirement row starts with REQ (any OCR variant) or with one of
                        // the six requirement keys; attached bullets must not end the block.
                        bool req=Regex.IsMatch(rows[i].Text??"",
                            @"^\s*(?:(?:REQ|AEQ|REO|AEO|RE0)\s*[.:]?\s*)?(?:LEV|LEVEL|STR|DEX|INT|LUK|FAM)\b",
                            RegexOptions.IgnoreCase);
                        if(!req){ribbon=i;break;}
                    }
                }
                if(ribbon<=0)continue;
                for(int i=0;i<ribbon;i++)zone.Add(rows[i]);
            }
            return zone;
        }

        private void AddAttributeRowLabels(List<OverlayLabel> output,List<OcrLine> all,
            HashSet<OcrLine> excluded,float scale,Dictionary<OcrLine,OcrPanelInfo> panels,bool skillPanel=false)
        {
            if(output==null||all==null)return;
            // A skill tooltip is prose: its HP/MP/Damage words are part of a sentence, not
            // attribute rows. Rewriting them painted isolated Chinese words inside English.
            if(skillPanel)return;
            int added=0,scanned=0,skipped=0;
            // The tooltip is split by the class ribbon: requirement rows sit above it, the
            // bonus rows below. Zone membership decides whether a bare "STR : 0" means a
            // requirement ("需要力量") or a bonus row.
            HashSet<OcrLine> requirementZone=BuildRequirementZone(all,panels);
            foreach(OcrLine line in all) {
                if(line==null||String.IsNullOrEmpty(line.Text))continue;
                if(excluded!=null&&excluded.Contains(line))continue;
                // Skill and quest bodies contain sentences that begin with a stat word
                // ("MP is 0." arrived as "魔法值: is"). The attribute shortcut belongs to
                // equipment-like rows only; never rewrite skill/quest prose with it.
                OcrPanelInfo owner;
                if(panels!=null&&panels.TryGetValue(line,out owner)&&owner!=null&&
                    (owner.IsSkillDetail||owner.IsQuest))continue;
                string text=line.Text.Trim();
                if(String.IsNullOrEmpty(text))continue;
                if(Program.Benchmark&&(text.IndexOf('•')>=0||text.IndexOf('·')>=0))
                    Program.LogRuntimeEvent("attribute-scan","len="+text.Length+" text="+text);
                // A merged tooltip row is longer than the 70-char prose cap, so the bullet
                // split has to run before that cap rejects the row outright.
                if(text.Length<4)continue;
                // Windows OCR merges a whole equips tooltip into one row when the bullet
                // glyphs are the only separator: "• Type: Gloves • INT: +2 • LUK: +2 ...".
                // The bullet marks exactly the bonus block, so split on it and translate
                // each piece on its own row box.
                if(text.IndexOf('•')>0||text.IndexOf('·')>0)
                {
                    int pieces=0;
                    foreach(Match segment in Regex.Matches(text,@"[•·]\s*([^•·]+)"))
                    {
                        string piece=segment.Groups[1].Value.Trim();
                        if(piece.Length<3)continue;
                        Match item=AttributeRowPattern.Match(piece);
                        if(!item.Success)continue;
                        string pieceChinese=AttributeKeyChinese(item.Groups["key"].Value);
                        if(pieceChinese.Length==0)continue;
                        string pieceValue=AttributeValueChinese(pieceChinese,item.Groups["value"].Value.Trim());
                        if(pieceValue.Length==0)continue;
                        RectangleF pieceBounds=GetOcrLineBounds(line);
                        if(pieceBounds.IsEmpty)continue;
                        RectangleF pieceBox=OverlaySpan(pieceBounds,scale);
                        if(pieceBox.Width<4||pieceBox.Height<4)continue;
                        output.Add(new OverlayLabel{Bounds=pieceBox,SourceBounds=pieceBox,HasSourceBounds=true,
                            SourceLineBounds=new List<RectangleF>{pieceBox},Text=pieceChinese+"："+pieceValue,
                            SemanticRole="item-field",MatchSource="attribute-bullet",
                            Confidence=LabelConfidence.High});
                        pieces++;
                    }
                    if(pieces>0){scanned+=pieces;added+=pieces;continue;}
                }
                if(text.Length>70)continue;
                // "Torn Ledger Pages 0 / 25" is a quest requirement row, not an attribute row.
                // The objective surface owns it; translating it here painted the noun at the
                // wrong place (the card caption) instead of over the row.
                if(!text.Contains(":")&&!text.Contains("：")&&
                    Regex.IsMatch(text,@"\d+\s*[/／]?\s*\d+"))continue;
                Match match=AttributeRowPattern.Match(text);
                if(!match.Success)continue;
                scanned++;
                string chinese=AttributeKeyChinese(match.Groups["key"].Value);
                string value=match.Groups["value"].Value.Trim();
                // "Attack Speed : Average" carries a word, not a number. Leaving it in
                // English made the row read "攻击速度：Average".
                if(String.Equals(chinese,"攻击速度",StringComparison.Ordinal)||
                    String.Equals(chinese,"移动速度",StringComparison.Ordinal))
                {
                    string word=Regex.Replace(value,@"[^A-Za-z]","");
                    switch(word.ToLowerInvariant()) {
                        case "average":value="普通";break;
                        case "fast":case "faster":case "fastest":value="快";break;
                        case "slow":case "slower":case "slowest":value="慢";break;
                        case "normal":value="正常";break;
                    }
                }
                if(chinese.Length==0||value.Length==0)continue;
                RectangleF raw=GetOcrLineBounds(line);
                if(raw.IsEmpty)continue;
                RectangleF box=OverlaySpan(raw,scale);
                if(box.Width<4||box.Height<4)continue;
                // Only the key half proves the row is already handled; a value label on the
                // right used to make the whole row look covered and kept the key English.
                RectangleF keyZone=new RectangleF(box.Left,box.Top,Math.Max(8.0f,box.Width*0.6f),box.Height);
                bool covered=false;
                foreach(OverlayLabel existing in output) {
                    RectangleF bounds=existing.HasSourceBounds?existing.SourceBounds:existing.Bounds;
                    RectangleF inter=RectangleF.Intersect(bounds,keyZone);
                    if(inter.Width>2&&inter.Height>2){covered=true;break;}
                }
                // always paint: a label that covered this row earlier may be merged away
                output.RemoveAll(delegate(OverlayLabel label){return (label.SemanticRole=="item-field"||String.IsNullOrEmpty(label.SemanticRole)) &&
                    box.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                string prefix=requirementZone.Contains(line)?"需要":"";
                AddWholeLineLabel(output,line,prefix+chinese+"："+value,scale,false);
                if(output.Count>0){
                    output[output.Count-1].SemanticRole="item-field";
                    output[output.Count-1].MatchSource="attribute-row";
                    added++;
                }
            }
            // Requirement rows get the same treatment: translate the key from the text,
            // keep the counter as read. Measurement failures used to leave REQ STR/DEX/FAM
            // English while LEV/INT/LUK happened to pass.
            foreach(OcrLine line in all) {
                if(line==null||String.IsNullOrEmpty(line.Text))continue;
                if(excluded!=null&&excluded.Contains(line))continue;
                OcrPanelInfo owner;
                if(panels!=null&&panels.TryGetValue(line,out owner)&&owner!=null&&
                    (owner.IsSkillDetail||owner.IsQuest))continue;
                string text=line.Text.Trim();
                if(text.Length<5||text.Length>40)continue;
                Match match=RequirementRowPattern.Match(text);
                if(!match.Success)continue;
                scanned++;
                string chinese=RequirementKeyChinese(match.Groups["key"].Value);
                if(chinese.Length==0)continue;
                string value=match.Groups["value"].Success?match.Groups["value"].Value.Trim():"";
                RectangleF raw=GetOcrLineBounds(line);
                if(raw.IsEmpty)continue;
                RectangleF box=OverlaySpan(raw,scale);
                if(box.Width<4||box.Height<4)continue;
                RectangleF keyZone=new RectangleF(box.Left,box.Top,Math.Max(8.0f,box.Width*0.6f),box.Height);
                bool covered=false;
                foreach(OverlayLabel existing in output) {
                    RectangleF bounds=existing.HasSourceBounds?existing.SourceBounds:existing.Bounds;
                    RectangleF inter=RectangleF.Intersect(bounds,keyZone);
                    if(inter.Width>2&&inter.Height>2){covered=true;break;}
                }
                // always paint: a label that covered this row earlier may be merged away
                output.RemoveAll(delegate(OverlayLabel label){return (label.SemanticRole=="item-field"||String.IsNullOrEmpty(label.SemanticRole)) &&
                    box.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                AddWholeLineLabel(output,line,value.Length>0?chinese+"："+value:chinese,scale,false);
                if(output.Count>0){
                    output[output.Count-1].SemanticRole="item-field";
                    output[output.Count-1].MatchSource="requirement-row";
                    added++;
                }
            }
            if(scanned>0||added>0)Program.LogRuntimeEvent("attribute-row-labels","scanned="+scanned+" covered="+skipped+" added="+added);
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


        // The body already resolved a task; its stored English names the required items and
        // monsters in reading order. That order matches the grey requirement rows, so the
        // rows do not need their own name recognition.
        private List<string> ObjectiveNamesFromBody(string english)
        {
            List<string> names=new List<string>();
            if(String.IsNullOrEmpty(english))return names;
            List<MatchResult> matches=translations.FindObjectiveMatchesInText(english);
            foreach(MatchResult match in matches) {
                if(match==null||match.Entry==null||String.IsNullOrEmpty(match.Entry.Chinese))continue;
                if(!names.Contains(match.Entry.Chinese))names.Add(match.Entry.Chinese);
            }
            return names;
        }

        private void AddObjectiveSurfaceLabels(List<OverlayLabel> output,List<OcrLine> all,Bitmap prepared,
            RectangleF owner,float scale,string questEnglish)
        {
            if(prepared==null||owner.IsEmpty)return;
            long findStart=System.Diagnostics.Stopwatch.GetTimestamp();
            List<RectangleF> cards=QuestObjectiveSurface.Find(ReadSimplePixels(prepared),prepared.Width,prepared.Height,owner,scale);
            long findMs=(System.Diagnostics.Stopwatch.GetTimestamp()-findStart)*1000/
                System.Diagnostics.Stopwatch.Frequency;
            // Reverse assist: the body already matched a task, so the requirement names are
            // known from that task is own text. Assign them to the grey rows by order instead
            // of re-recognising each row is noun.
            List<string> bodyNames=ObjectiveNamesFromBody(questEnglish);
            int nameCursor=0;
            int rowsSeen=0,names=0;
            foreach(RectangleF card in cards) {
                List<OcrLine> lines=all.FindAll(delegate(OcrLine line){RectangleF r=GetOcrLineBounds(line);return card.Contains(new PointF(r.Left+r.Width/2,r.Top+r.Height/2));});
                foreach(List<OcrLine> row in BuildVisualRows(lines)) {
                    string text=CombineVisualRow(row);

                    Match count=QuestObjectiveSurface.Counter(text);
                    rowsSeen++;
                    string source=(count.Success?text.Substring(0,count.Index):text).Trim();
                    // OCR merges the card caption into the first requirement row.
                    Match captionHead=Regex.Match(source,@"^(任务概要|Quest\s*Summary|任务助手)\s*",RegexOptions.IgnoreCase);
                    int captionLength=captionHead.Success?captionHead.Length:0;
                    if(captionHead.Success)source=source.Substring(captionLength).Trim();
                    string chinese=null;
                    // Only a counted row consumes a body-derived name. The card is own caption
                    // row ("Quest Summary") has no counter and used to steal the first name,
                    // which painted the item noun on top of the caption.
                    if(count.Success&&nameCursor<bodyNames.Count)chinese=bodyNames[nameCursor++];
                    else if(!count.Success&&nameCursor<bodyNames.Count&&
                        !Regex.IsMatch(source,@"^(任务概要|任务助手|quest\s*summary)",RegexOptions.IgnoreCase))
                        chinese=bodyNames[nameCursor++];
                    if(chinese==null) {
                        TranslationEntry entry=translations.ResolveObjectiveName(source);
                        if(entry!=null)chinese=entry.Chinese;
                    }
                    if(chinese==null){if(Program.Benchmark)Program.LogRuntimeEvent("objective-row-miss","text="+text+" source="+source+" count="+count.Success);continue;}
                    RectangleF raw=PreciseTextSpan(row,text,captionLength,source.Length),value=count.Success?PreciseTextSpan(row,text,count.Index,count.Length):RectangleF.Empty;
                    // An icon merged into the row breaks the word boundary; fall back to the
                    // measured row box so the noun still gets translated. A missing counter
                    // box becomes the right-hand guard instead of dropping the whole row.
                    // A merged row starts with the card caption; measuring from the first
                    // line put the noun on top of that caption. Use the last line (the noun
                    // and counter row) as the fallback box.
                    RectangleF rowBox=GetOcrLineBounds(row[row.Count-1]);
                    if(raw.IsEmpty&&!rowBox.IsEmpty)
                        raw=RectangleF.FromLTRB(rowBox.Left,rowBox.Top,rowBox.Right,rowBox.Bottom);
                    if(count.Success&&value.IsEmpty&&!rowBox.IsEmpty)
                        value=RectangleF.FromLTRB(rowBox.Left+rowBox.Width*0.60f,rowBox.Top,
                            rowBox.Right,rowBox.Bottom);
                    if(raw.IsEmpty)continue;
                    // A no-icon row often has no geometric gap between the noun
                    // and counter inside one OCR word. Reserve the counter first.
                    if(count.Success&&raw.Right+2*scale>=value.Left) {
                        float right=Math.Min(raw.Right,value.Left-2*scale);
                        // A badly read noun ("ull") leaves almost no width; keeping the whole
                        // row is better than dropping the requirement translation entirely.
                        if(right-raw.Left>=8*scale)raw=RectangleF.FromLTRB(raw.Left,raw.Top,right,raw.Bottom);
                    }
                    // The Chinese noun is usually wider than the English one, and the narrow
                    // noun box then failed "complete-text-does-not-fit" and stayed English.
                    // Own the whole row (name + counter) so the replacement always fits.
                    RectangleF box=OverlaySpan(rowBox,scale);
                    // A summary card sits inside the journal viewport, so its objective rows
                    // overlap the body label that already owns the paragraph. The objective
                    // label paints its own row; it must not delete the journal body.
                    output.RemoveAll(delegate(OverlayLabel label){
                        if(label.SemanticRole=="task-body"||label.SemanticRole=="task-journal")return false;
                        return box.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds);});
                    output.Add(new OverlayLabel{Bounds=box,SourceBounds=box,HasSourceBounds=true,SourceLineBounds=new List<RectangleF>{box},
                        Text=chinese,SemanticRole="task-objective",KeepClearBounds=RectangleF.Empty,
                        MatchSource="objective-surface",Confidence=LabelConfidence.High,PanelBounds=OverlaySpan(card,scale)});names++;
                }
            }
            Program.LogRuntimeEvent("objective-coverage","cards="+cards.Count+" rows="+rowsSeen+" planned="+names+" bodyNames="+bodyNames.Count+" findMs="+findMs);
        }
    }
}
