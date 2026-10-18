using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class ItemTextPart
    {
        internal string Role = "";
        internal string SourceText = "";
        internal string Translation = "";
        internal string Status = "recognized";
        internal string Reason = "";
        internal string MatchSource = "";
        internal bool Estimated;
        internal OverlayLabel Label;
        internal RectangleF SourceBounds;
    }

    internal sealed class ItemPanelPlan
    {
        internal string ItemId = "";
        internal string RecognitionReason = "";
        internal int Recognized;
        internal int Matched;
        internal int Planned;
        internal int Prepared;
        internal int Painted;
        internal int Preserved;
        internal string PreservedReason = "";
        internal float FontSize;
        internal RectangleF PanelBounds;
        internal readonly List<ItemTextPart> Parts = new List<ItemTextPart>();

        internal void AddPreservedReason(string reason)
        {
            if (String.IsNullOrEmpty(reason)) return;
            if (PreservedReason.IndexOf(reason, StringComparison.Ordinal) >= 0) return;
            if (PreservedReason.Length == 0) PreservedReason = reason;
            else if (PreservedReason.Length < 160) PreservedReason += "," + reason;
        }

        internal string Describe()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("id=").Append(ItemId.Length == 0 ? "shared" : ItemId);
            builder.Append(" recognized=").Append(Recognized);
            builder.Append(" matched=").Append(Matched);
            builder.Append(" planned=").Append(Planned);
            builder.Append(" prepared=").Append(Prepared);
            builder.Append(" painted=").Append(Painted);
            builder.Append(" preserved=").Append(Preserved);
            builder.Append(" font=").Append(FontSize.ToString("0.#"));
            builder.Append(" reason=").Append(RecognitionReason.Length == 0 ? "none" : RecognitionReason);
            builder.Append(" parts=");
            for (int i = 0; i < Parts.Count; i++)
            {
                if (i > 0) builder.Append(';');
                builder.Append(Parts[i].Role).Append(':').Append(Parts[i].Status);
                if (!String.IsNullOrEmpty(Parts[i].Reason))
                    builder.Append('/').Append(Parts[i].Reason);
            }
            if (PreservedReason.Length > 0) builder.Append(" preservedReason=").Append(PreservedReason);
            return builder.ToString();
        }
    }

    internal sealed partial class OverlayForm
    {
        private sealed class ItemBodyBlock
        {
            internal List<OcrLine> SourceLines = new List<OcrLine>();
            internal string MatchText = "";
            internal string LabelText = "";
            internal int LabelStart;
            internal bool MergedPrefix;
        }

        private void AddOrdinaryItemPanels(List<OverlayLabel> output, List<OcrLine> allLines,
            Dictionary<OcrLine, OcrPanelInfo> panelByLine, float ocrScale,
            HashSet<OcrLine> ownedLines, List<ItemPanelPlan> plans)
        {
            HashSet<OcrPanelInfo> seen = new HashSet<OcrPanelInfo>();
            foreach (OcrLine origin in allLines)
            {
                OcrPanelInfo panel;
                if (!panelByLine.TryGetValue(origin, out panel) || panel == null) continue;
                if (!seen.Add(panel)) continue;
                if (!LooksLikeOrdinaryItemPanel(panel)) continue;
                ItemNameMatch name = null;
                OcrLine nameLine = null;
                int nameIndex = -1;
                bool adjacentNames = false;
                int distinctNames = 0;
                string firstNormalized = "";
                List<OcrLine> sorted = new List<OcrLine>(panel.Lines);
                sorted.Sort(delegate(OcrLine left, OcrLine right) {
                    return GetOcrLineBounds(left).Top.CompareTo(GetOcrLineBounds(right).Top);
                });
                for (int i = 0; i < sorted.Count; i++)
                {
                    ItemNameMatch candidate = translations.MatchItemNameLine(sorted[i].Text, true);
                    if (candidate == null || candidate.Chinese.Length == 0 && !candidate.Ambiguous &&
                        candidate.Entries.Count == 0) continue;
                    if (name == null)
                    {
                        name = candidate;
                        nameLine = sorted[i];
                        nameIndex = i;
                        firstNormalized = candidate.Normalized;
                        distinctNames = 1;
                    }
                    else if (!String.Equals(candidate.Normalized, firstNormalized,
                        StringComparison.Ordinal))
                    {
                        adjacentNames = true;
                        distinctNames++;
                    }
                }
                if (adjacentNames)
                {
                    // Two adjacent tooltips cannot be separated from text alone. Preserve
                    // the local block instead of letting the generic path attach one item's
                    // body to the other. A large grid of names is an inventory list, not an
                    // ambiguous tooltip, and stays with its existing name-only path.
                    if (distinctNames <= 2 && sorted.Count <= 8)
                    {
                        foreach (OcrLine row in sorted) ownedLines.Add(row);
                        ItemPanelPlan ambiguous = new ItemPanelPlan {
                            RecognitionReason = "ambiguous-adjacent-item-names"
                        };
                        ambiguous.Recognized = sorted.Count;
                        ambiguous.AddPreservedReason("ambiguous-adjacent-item-names");
                        plans.Add(ambiguous);
                        Program.LogRuntimeEvent("item-context", ambiguous.Describe());
                    }
                    continue;
                }

                List<OcrLine> bodyRows = new List<OcrLine>();
                string continuation = "";
                if (nameLine != null)
                {
                    continuation = ItemContinuationText(nameLine, name.Normalized);
                    for (int i = nameIndex + 1; i < sorted.Count; i++)
                    {
                        OcrLine row = sorted[i];
                        if (bodyRows.Count > 0 && !CanJoinOcrLines(bodyRows[bodyRows.Count - 1], row))
                            break;
                        if (translations.MatchItemNameLine(row.Text, false) != null) break;
                        if (!IsCandidateItemBodyRow(row.Text)) break;
                        bodyRows.Add(row);
                    }
                }
                else
                {
                    // A body-only tooltip is accepted when the visible row is a complete
                    // trusted item description (one line is enough for return scrolls and
                    // short effect statements), or when a second local row confirms the
                    // block. A lone flavour sentence stays untouched.
                    if (sorted.Count < 1) continue;
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        OcrLine row = sorted[i];
                        if (bodyRows.Count > 0 && !CanJoinOcrLines(bodyRows[bodyRows.Count - 1], row))
                            break;
                        if (!IsCandidateItemBodyRow(row.Text)) break;
                        bodyRows.Add(row);
                    }
                    if (bodyRows.Count < 2)
                    {
                        if (bodyRows.Count != 1) continue;
                        ItemBodyTranslation single = ItemTextSemantics.TranslateVisibleBody(
                            translations, bodyRows[0].Text, "");
                        if (!single.Matched || single.Estimated ||
                            single.Source != "item-description") continue;
                    }
                }

                bool hasContinuation = continuation.Trim().Length > 0;
                if (bodyRows.Count == 0 && !hasContinuation)
                {
                    // A bare title without any local description/auxiliary context is
                    // indistinguishable from a list row; keep it untouched.
                    continue;
                }

                ItemPanelPlan plan = new ItemPanelPlan {
                    RecognitionReason = nameLine == null ? "body-only-context" : "name-and-local-body"
                };
                plans.Add(plan);
                if (nameLine != null) ownedLines.Add(nameLine);
                foreach (OcrLine row in bodyRows) ownedLines.Add(row);

                // A unique name ID scopes the description lookup; a duplicate name is
                // resolved later from the visible body. The name is planned after the body
                // so a successful name can never suppress the description (and vice versa).
                if (name != null && name.ItemId.Length > 0) plan.ItemId = name.ItemId;

                // Split the visible rows into auxiliary clauses and the actual
                // description so each owns its source region. Adjacent rows of the same
                // kind are translated as one block.
                List<OcrLine> descriptionRows = new List<OcrLine>();
                string pendingMatch = hasContinuation ? continuation : "";
                List<OcrLine> pendingSource = new List<OcrLine>();
                if (hasContinuation) pendingSource.Add(nameLine);
                for (int i = 0; i <= bodyRows.Count; i++)
                {
                    OcrLine row = i < bodyRows.Count ? bodyRows[i] : null;
                    bool aux = row != null && ItemTextSemantics.MatchAuxiliaryPhrase(row.Text).Length > 0;
                    if (row != null && aux)
                    {
                        FlushItemDescriptionBlock(output, plan, descriptionRows, pendingMatch,
                            pendingSource, ocrScale);
                        descriptionRows.Clear(); pendingMatch = ""; pendingSource.Clear();
                        AddItemAuxiliaryRow(output, plan, row, ocrScale);
                        continue;
                    }
                    if (row != null)
                    {
                        descriptionRows.Add(row);
                        if (pendingMatch.Length > 0) pendingMatch += " ";
                        pendingMatch += row.Text;
                    }
                    else
                    {
                        FlushItemDescriptionBlock(output, plan, descriptionRows, pendingMatch,
                            pendingSource, ocrScale);
                    }
                }

                ItemTextPart namePart = null;
                if (nameLine != null)
                {
                    namePart = new ItemTextPart {
                        Role = "item-name",
                        SourceText = name.Normalized,
                        MatchSource = "item-name"
                    };
                    plan.Parts.Insert(0, namePart);
                    string chinese = plan.ItemId.Length > 0
                        ? translations.ResolveItemNameChinese(name.Normalized, plan.ItemId)
                        : name.Chinese;
                    if (chinese.Length == 0)
                    {
                        namePart.Status = "preserved";
                        namePart.Reason = name.Ambiguous ? "ambiguous-name" : "name-not-translated";
                        plan.AddPreservedReason(namePart.Reason);
                    }
                    else
                    {
                        OverlayLabel label = BuildItemSpanLabel(
                            new List<OcrLine> { nameLine }, nameLine.Text, 0,
                            name.Normalized.Length, chinese, ocrScale, "item-name",
                            "item-name", LabelConfidence.High, false, plan);
                        if (label == null)
                        {
                            namePart.Status = "preserved";
                            namePart.Reason = "name-span-unmeasured";
                            plan.AddPreservedReason(namePart.Reason);
                        }
                        else
                        {
                            namePart.Translation = chinese;
                            namePart.SourceBounds = label.SourceBounds;
                            namePart.Label = label;
                            namePart.Status = "planned";
                            output.Add(label);
                        }
                    }
                }
                plan.Recognized = plan.Parts.Count;
                foreach (ItemTextPart part in plan.Parts)
                {
                    if (part.Status == "planned") plan.Planned++;
                    if (!String.IsNullOrEmpty(part.Translation)) plan.Matched++;
                }
                plan.FontSize = ComputeItemSharedFont(plan);
                foreach (ItemTextPart part in plan.Parts)
                    if (part.Label != null) part.Label.LayoutFontSize = plan.FontSize;
                plan.PanelBounds = ItemPlanBounds(plan);
                foreach (ItemTextPart part in plan.Parts)
                {
                    if (part.Label == null) continue;
                    part.Label.PanelBounds = plan.PanelBounds;
                    part.Label.ItemPanel = plan;
                    part.Label.ItemTextPart = part;
                }
                Program.LogRuntimeEvent("item-context", plan.Describe());
            }
        }

        private static bool LooksLikeOrdinaryItemPanel(OcrPanelInfo panel)
        {
            if (panel.IsQuest || panel.IsSkillDetail || panel.IsSettings ||
                panel.IsCharacterStats || panel.IsCharacterInfo || panel.IsEquipmentDetail)
                return false;
            string normalized = TranslationStore.Normalize(panel.Text);
            if (normalized.Contains("character stat") || normalized.Contains("character info") ||
                normalized.Contains("master level") || normalized.Contains("current level") ||
                normalized.Contains("next level") || normalized.Contains("quest helper") ||
                normalized.Contains("key bindings") || normalized.Contains("world map"))
                return false;
            foreach (OcrLine line in panel.Lines)
            {
                string key = TranslationStore.Normalize(line.Text);
                if (QuestDetailGeometry.IsSummaryKey(key) || key == "quest" ||
                    key == "available" || key == "in progress" || key == "completed" ||
                    key == "forfeit" || key == "details") return false;
            }
            return true;
        }

        private static bool IsCandidateItemBodyRow(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length < 4) return false;
            if (Regex.IsMatch(normalized, @"^\d[\d,]*$") ||
                Regex.IsMatch(normalized, @"^\d[\d,]* (?:mesos?|金币|枫币)$")) return false;
            if (Regex.IsMatch(normalized,
                @"^(?:req(?:uired)? (?:lev|lv|level|str|dex|int|luk|fame)|weapon def|magic def|" +
                @"attack speed|upgrade slots|remaining (?:upgrades|enhancements)|number of upgrades)\b"))
                return false;
            if (normalized.Contains("character stat") || normalized.Contains("character info") ||
                normalized.Contains("key bindings") || normalized.Contains("world map")) return false;
            if (ItemTextSemantics.MatchAuxiliaryPhrase(text).Length > 0) return true;
            string template;
            string source;
            if (ItemTextSemantics.TryTranslateTemplate(text, null, out template, out source))
                return true;
            int letters = 0;
            foreach (char c in normalized) if (Char.IsLetter(c)) letters++;
            return letters >= 3 && (normalized.IndexOf(' ') >= 0 ||
                normalized.IndexOf('%') >= 0 || normalized.IndexOf(':') >= 0);
        }

        private static string ItemContinuationText(OcrLine line, string normalizedName)
        {
            if (line == null || String.IsNullOrEmpty(normalizedName)) return "";
            string normalized = TranslationStore.Normalize(line.Text);
            if (normalized.Length <= normalizedName.Length) return "";
            if (!normalized.StartsWith(normalizedName, StringComparison.Ordinal)) return "";
            if (!TranslationStore.IsBoundary(normalized, normalizedName.Length)) return "";
            int rawCursor = 0;
            int normalizedCursor = 0;
            foreach (OcrWord word in line.Words)
            {
                string rawWord = word.Text ?? "";
                if (rawWord.Length == 0) continue;
                int rawIndex = line.Text.IndexOf(rawWord, rawCursor, StringComparison.Ordinal);
                if (rawIndex < 0) rawIndex = rawCursor;
                string wordNormalized = TranslationStore.Normalize(rawWord);
                if (wordNormalized.Length == 0) { rawCursor = rawIndex + rawWord.Length; continue; }
                if (normalizedCursor + wordNormalized.Length <= normalizedName.Length)
                {
                    normalizedCursor += wordNormalized.Length + 1;
                    rawCursor = rawIndex + rawWord.Length;
                    continue;
                }
                string suffix = line.Text.Substring(rawIndex).Trim();
                if (suffix.Length == 0) return "";
                string suffixNormalized = TranslationStore.Normalize(suffix);
                if (Regex.IsMatch(suffixNormalized, @"^\d+$")) return "";
                return suffix;
            }
            return "";
        }

        private void FlushItemDescriptionBlock(List<OverlayLabel> output, ItemPanelPlan plan,
            List<OcrLine> rows, string matchText, List<OcrLine> prefixSource, float ocrScale)
        {
            bool hasPrefix = prefixSource != null && prefixSource.Count > 0;
            if ((rows.Count == 0 && !hasPrefix) || String.IsNullOrWhiteSpace(matchText)) return;
            ItemTextPart part = new ItemTextPart {
                Role = "item-body",
                SourceText = matchText,
                MatchSource = "item-body"
            };
            plan.Parts.Add(part);
            ItemBodyTranslation body = ItemTextSemantics.TranslateVisibleBody(
                translations, matchText, plan.ItemId);
            if (body.ItemId.Length > 0) plan.ItemId = body.ItemId;
            if (!body.Matched)
            {
                part.Status = "preserved";
                part.Reason = body.Reason.Length == 0 ? "description-not-matched" : body.Reason;
                plan.AddPreservedReason(part.Reason);
                return;
            }
            if (body.Estimated)
            {
                part.Status = "preserved";
                part.Reason = "estimated-visible-source";
                part.Estimated = true;
                plan.AddPreservedReason(part.Reason);
                return;
            }
            List<OcrLine> sourceLines = new List<OcrLine>();
            if (prefixSource != null && prefixSource.Count > 0) sourceLines.AddRange(prefixSource);
            sourceLines.AddRange(rows);
            string labelText = JoinItemLineText(sourceLines);
            int start = TranslationStore.Normalize(labelText).IndexOf(
                TranslationStore.Normalize(matchText), StringComparison.Ordinal);
            if (start < 0) start = 0;
            OverlayLabel label = BuildItemSpanLabel(sourceLines, labelText, start,
                TranslationStore.Normalize(matchText).Length, body.Translation, ocrScale,
                "item-body", body.Source, LabelConfidence.High, sourceLines.Count > 1, plan);
            if (label == null)
            {
                part.Status = "preserved";
                part.Reason = "body-span-unmeasured";
                plan.AddPreservedReason(part.Reason);
                return;
            }
            part.Translation = body.Translation;
            part.SourceBounds = label.SourceBounds;
            part.Label = label;
            part.Status = "planned";
            output.Add(label);
        }

        private void AddItemAuxiliaryRow(List<OverlayLabel> output, ItemPanelPlan plan,
            OcrLine row, float ocrScale)
        {
            string chinese = ItemTextSemantics.MatchAuxiliaryPhrase(row.Text);
            ItemTextPart part = new ItemTextPart {
                Role = "item-aux",
                SourceText = TranslationStore.Normalize(row.Text),
                MatchSource = "item-aux"
            };
            plan.Parts.Add(part);
            if (chinese.Length == 0)
            {
                part.Status = "preserved";
                part.Reason = "aux-not-matched";
                plan.AddPreservedReason(part.Reason);
                return;
            }
            OverlayLabel label = BuildItemSpanLabel(new List<OcrLine> { row }, row.Text, 0,
                TranslationStore.Normalize(row.Text).Length, chinese, ocrScale, "item-aux",
                "item-aux", LabelConfidence.High, false, plan);
            if (label == null)
            {
                part.Status = "preserved";
                part.Reason = "aux-span-unmeasured";
                plan.AddPreservedReason(part.Reason);
                return;
            }
            part.Translation = chinese;
            part.SourceBounds = label.SourceBounds;
            part.Label = label;
            part.Status = "planned";
            output.Add(label);
        }

        private static string JoinItemLineText(List<OcrLine> lines)
        {
            StringBuilder text = new StringBuilder();
            foreach (OcrLine line in lines)
            {
                if (line == null || String.IsNullOrEmpty(line.Text)) continue;
                if (text.Length > 0) text.Append(' ');
                text.Append(line.Text);
            }
            return text.ToString();
        }

        private OverlayLabel BuildItemSpanLabel(List<OcrLine> lines, string combinedText,
            int normalizedStart, int normalizedLength, string translation, float ocrScale,
            string role, string source, LabelConfidence confidence, bool wrap,
            ItemPanelPlan plan)
        {
            if (lines == null || lines.Count == 0 || String.IsNullOrWhiteSpace(translation) ||
                normalizedLength <= 0 || ocrScale <= 0) return null;
            string normalizedLine = TranslationStore.Normalize(combinedText);
            int matchStart = Math.Max(0, Math.Min(normalizedStart, normalizedLine.Length));
            int matchLength = Math.Min(normalizedLength, normalizedLine.Length - matchStart);
            if (matchLength <= 0) return null;
            int matchEnd = matchStart + matchLength;
            float x0 = Single.MaxValue, y0 = Single.MaxValue;
            float x1 = Single.MinValue, y1 = Single.MinValue;
            List<RectangleF> sourceLines = new List<RectangleF>();
            bool clipped = false;
            int cursor = 0;
            foreach (OcrLine line in lines)
            {
                float lx0 = Single.MaxValue, ly0 = Single.MaxValue;
                float lx1 = Single.MinValue, ly1 = Single.MinValue;
                foreach (OcrWord word in line.Words)
                {
                    string nw = TranslationStore.Normalize(word.Text);
                    if (nw.Length == 0) continue;
                    int safeCursor = Math.Min(cursor, normalizedLine.Length);
                    int found = safeCursor < normalizedLine.Length
                        ? normalizedLine.IndexOf(nw, safeCursor, StringComparison.Ordinal) : -1;
                    if (found < 0) found = safeCursor;
                    int end = found + nw.Length;
                    if (end > matchStart && found < matchEnd)
                    {
                        if (found < matchStart || end > matchEnd) clipped = true;
                        float wx0 = ToOverlayX(word.BoundingRect.X, ocrScale);
                        float wy0 = ToOverlayY(word.BoundingRect.Y, ocrScale);
                        float wx1 = ToOverlayX(word.BoundingRect.X + word.BoundingRect.Width, ocrScale);
                        float wy1 = ToOverlayY(word.BoundingRect.Y + word.BoundingRect.Height, ocrScale);
                        x0 = Math.Min(x0, wx0); y0 = Math.Min(y0, wy0);
                        x1 = Math.Max(x1, wx1); y1 = Math.Max(y1, wy1);
                        lx0 = Math.Min(lx0, wx0); ly0 = Math.Min(ly0, wy0);
                        lx1 = Math.Max(lx1, wx1); ly1 = Math.Max(ly1, wy1);
                    }
                    cursor = Math.Min(normalizedLine.Length, end + 1);
                }
                if (lx0 != Single.MaxValue)
                    sourceLines.Add(new RectangleF(lx0, ly0,
                        Math.Max(1.0f, lx1 - lx0), Math.Max(1.0f, ly1 - ly0)));
            }
            if (x0 == Single.MaxValue) return null;
            // A partial OCR word would make the erase rectangle an estimate. Never
            // promote it: the caller preserves the original row instead.
            if (clipped) return null;
            RectangleF sourceBounds = new RectangleF(x0, y0,
                Math.Max(1.0f, x1 - x0), Math.Max(1.0f, y1 - y0));
            return new OverlayLabel {
                Bounds = new RectangleF(x0 - 3, y0 - 2,
                    Math.Max(28, x1 - x0 + 6), Math.Max(18, y1 - y0 + 4)),
                SourceBounds = sourceBounds,
                HasSourceBounds = true,
                SourceLineBounds = sourceLines,
                Text = translation,
                Wrap = wrap,
                SemanticRole = role,
                MatchSource = source,
                Confidence = confidence,
                ConfidenceReason = confidence == LabelConfidence.High ? 100.0f : 50.0f,
                SourceBoundaryEstimated = false
            };
        }

        private float ComputeItemSharedFont(ItemPanelPlan plan)
        {
            if (plan == null || plan.Parts.Count == 0) return 0.0f;
            float start = 14.0f;
            foreach (ItemTextPart part in plan.Parts)
            {
                if (part.Label == null) continue;
                start = Math.Min(start, Math.Max(11.0f, part.Label.SourceBounds.Height));
            }
            using (Bitmap sample = new Bitmap(1, 1))
            using (Graphics measure = Graphics.FromImage(sample))
            {
                measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (float size = start; size >= 11.0f; size -= 1.0f)
                {
                    bool allFit = true;
                    using (StringFormat format = ItemTextFormat())
                    {
                        foreach (ItemTextPart part in plan.Parts)
                        {
                            if (part.Label == null) continue;
                            using (Font font = new Font("SimSun", size,
                                FontStyle.Regular, GraphicsUnit.Pixel))
                            {
                                int fitted, lines;
                                SizeF area = new SizeF(Math.Max(1.0f, part.Label.SourceBounds.Width),
                                    Math.Max(1.0f, part.Label.SourceBounds.Height));
                                measure.MeasureString(part.Label.Text, font, area, format,
                                    out fitted, out lines);
                                if (fitted != part.Label.Text.Length)
                                { allFit = false; break; }
                            }
                        }
                    }
                    if (allFit) return size;
                }
            }
            return 11.0f;
        }

        private static StringFormat ItemTextFormat()
        {
            StringFormat format = new StringFormat(StringFormat.GenericTypographic);
            format.Trimming = StringTrimming.None;
            format.FormatFlags &= ~StringFormatFlags.NoWrap;
            return format;
        }

        private static RectangleF ItemPlanBounds(ItemPanelPlan plan)
        {
            RectangleF bounds = RectangleF.Empty;
            foreach (ItemTextPart part in plan.Parts)
            {
                if (part.Label == null) continue;
                RectangleF source = part.Label.SourceBounds;
                bounds = bounds.IsEmpty ? source : RectangleF.Union(bounds, source);
            }
            if (bounds.IsEmpty) return RectangleF.Empty;
            bounds.Inflate(4, 3);
            return bounds;
        }

        private static string ItemTraceId(OverlayLabel label)
        {
            if (label == null) return "none";
            string itemId = label.ItemPanel == null || String.IsNullOrEmpty(label.ItemPanel.ItemId)
                ? "shared" : label.ItemPanel.ItemId;
            string role = String.IsNullOrEmpty(label.SemanticRole) ? "unknown" : label.SemanticRole;
            return itemId + ":" + role + ":" +
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(label).ToString("X8");
        }

        private static void TraceItemStage(OverlayLabel label, int generation, string stage,
            string reason)
        {
            if (label == null || label.ItemTextPart == null || label.ItemPanel == null) return;
            RectangleF bounds = label.SourceBounds;
            Program.LogRuntimeEvent("item-draw-stage", "generation=" + generation +
                " label=" + ItemTraceId(label) + " stage=" + stage +
                " bounds=" + bounds.X.ToString("0.##") + "," + bounds.Y.ToString("0.##") +
                "," + bounds.Width.ToString("0.##") + "," + bounds.Height.ToString("0.##") +
                (String.IsNullOrEmpty(reason) ? "" : " reason=" + reason));
        }

        // Actual paint evidence is recorded after the immutable composition has been
        // drawn, not when it was merely prepared. A benchmark that never opens a window
        // therefore stays "prepared" and reports painted=0 instead of claiming success.
        private void MarkItemPlansPainted(string phase, bool readyIsPainted)
        {
            MarkItemPlansPainted(labels, phase, readyIsPainted);
        }

        private void MarkItemPlansPainted(List<OverlayLabel> source, string phase,
            bool readyIsPainted)
        {
            if (source == null) return;
            HashSet<ItemPanelPlan> plans = new HashSet<ItemPanelPlan>();
            foreach (OverlayLabel label in source)
            {
                if (label == null || label.ItemTextPart == null || label.ItemPanel == null)
                    continue;
                ItemPanelPlan plan = label.ItemPanel;
                if (plans.Add(plan)) plan.Painted = 0;
                bool painted = readyIsPainted ? simpleReady.Contains(label) : label.PaintedLastPass;
                if (painted)
                {
                    label.ItemTextPart.Status = "painted";
                    label.ItemTextPart.Reason = "";
                    plan.Painted++;
                }
                else if (simpleReady.Contains(label))
                {
                    label.ItemTextPart.Status = "prepared";
                }
            }
            if (plans.Count == 0 || lastItemPaintLogGeneration == recognitionGeneration) return;
            foreach (ItemPanelPlan plan in plans)
                Program.LogRuntimeEvent("item-painted", "generation=" + recognitionGeneration +
                    " phase=" + phase + " " + plan.Describe());
            lastItemPaintLogGeneration = recognitionGeneration;
        }
    }
}
