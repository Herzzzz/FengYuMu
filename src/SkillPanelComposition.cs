using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class SkillSectionPlan
    {
        internal string Role = "";
        internal string SourceKind = "visible";
        internal string Observed = "";
        internal string Translation = "";
        internal string MatchSource = "";
        internal string Status = "observed";
        internal string PreservedReason = "";
        internal string HeaderStatus = "";
        internal string EffectStatus = "";
        internal string EffectPreservedReason = "";
        internal string ObservedEffect = "";
        internal RectangleF RawBounds;
        internal List<RectangleF> RawRows = new List<RectangleF>();
        internal List<RectangleF> OverlayRows = new List<RectangleF>();
        internal RectangleF ProtectedRaw;
        internal bool Measured = true;
        internal bool FromDictionary;
        internal string NumericAssociations = "";
        internal OverlayLabel Label;
        internal float PreparedFontSize;
        internal float PaintedFontSize;
    }

    internal sealed class SkillPanelPlan
    {
        internal string SkillId = "";
        internal string IdentitySource = "none";
        internal string IdentityReason = "";
        internal bool IdentityAmbiguous;
        internal RectangleF RawBounds;
        internal RectangleF OverlayBounds;
        internal float Scale = 1.0f;
        internal string FontFamily = "SimSun";
        internal float TitleFontSize;
        internal float BodyFontSize;
        internal int Observed;
        internal int Matched;
        internal int Planned;
        internal int Prepared;
        internal int Painted;
        internal int Preserved;
        internal readonly List<SkillSectionPlan> Sections = new List<SkillSectionPlan>();
        internal string PreservedReasons = "";

        internal void AddPreservedReason(string reason)
        {
            if (String.IsNullOrEmpty(reason)) return;
            if (PreservedReasons.Length == 0) PreservedReasons = reason;
            else if (PreservedReasons.IndexOf(reason, StringComparison.Ordinal) < 0 &&
                PreservedReasons.Length < 200) PreservedReasons += "," + reason;
        }

        internal string SectionSummary()
        {
            StringBuilder text = new StringBuilder();
            foreach (SkillSectionPlan section in Sections)
            {
                if (text.Length > 0) text.Append(';');
                text.Append(section.Role).Append(':').Append(section.Status);
                if (!String.IsNullOrEmpty(section.EffectStatus))
                    text.Append("[header=").Append(section.HeaderStatus).Append(",effect=")
                        .Append(section.EffectStatus).Append(']');
                if (!String.IsNullOrEmpty(section.PreservedReason))
                    text.Append('/').Append(section.PreservedReason);
            }
            return text.Length == 0 ? "none" : text.ToString();
        }

        internal string Describe()
        {
            return "id=" + (SkillId.Length == 0 ? "none" : SkillId) +
                " identity=" + IdentitySource +
                " ambiguous=" + (IdentityAmbiguous ? "true" : "false") +
                " reason=" + (IdentityReason.Length == 0 ? "none" : IdentityReason) +
                " observed=" + Observed + " matched=" + Matched + " planned=" + Planned +
                " preserved=" + Preserved +
                " preservedReason=" + (PreservedReasons.Length == 0 ? "none" : PreservedReasons) +
                " font=" + FontFamily + " bodyPx=" + BodyFontSize.ToString("0.#",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " titlePx=" + TitleFontSize.ToString("0.#",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " unit=Pixel scale=" + Scale.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " sections=" + SectionSummary();
        }

        internal string DescribePaint()
        {
            return "id=" + (SkillId.Length == 0 ? "none" : SkillId) +
                " prepared=" + Prepared + " painted=" + Painted + " preserved=" + Preserved +
                " preservedReason=" + (PreservedReasons.Length == 0 ? "none" : PreservedReasons) +
                " sections=" + SectionSummary();
        }
    }

    internal sealed partial class OverlayForm
    {
        private sealed class SkillTextRow
        {
            internal int Index;
            internal readonly List<OcrLine> Lines = new List<OcrLine>();
            internal string Text = "";
            internal string Normalized = "";
            internal RectangleF Bounds;
            internal bool Measured = true;
            internal bool Synthetic;
        }

        private sealed class SkillTextSpan
        {
            internal SkillTextRow Row;
            internal int Start;
            internal int Length;
            internal RectangleF Bounds;
            internal bool Measured;
            internal string Text = "";
        }

        private static readonly Regex SkillMasterMarker = new Regex(
            @"[\[\(]?\s*master\s*level\s*[:：]?\s*(?<level>[0-9lIoOgqSsB]+)\s*[\]\)]?",
            RegexOptions.IgnoreCase);
        private static readonly Regex SkillCurrentMarker = new Regex(
            @"[\[\(]?\s*current\s*level\s*[:：]?\s*(?<level>[0-9lIoOgqSsB]+)\s*[\]\)]?",
            RegexOptions.IgnoreCase);
        private static readonly Regex SkillNextMarker = new Regex(
            @"[\[\(]?\s*next\s*level\s*[:：]?\s*(?<level>[0-9lIoOgqSsB]+)\s*[\]\)]?",
            RegexOptions.IgnoreCase);
        private static readonly Regex SkillRequiredMarker = new Regex(
            @"required\s*skill\s*[:：]?", RegexOptions.IgnoreCase);

        private static List<SkillTextRow> BuildSkillTextRows(List<OcrLine> source)
        {
            List<SkillTextRow> rows = new List<SkillTextRow>();
            foreach (List<OcrLine> visual in BuildVisualRows(source))
            {
                SkillTextRow row = new SkillTextRow();
                row.Index = rows.Count;
                row.Lines.AddRange(visual);
                row.Text = CombineVisualRow(visual);
                row.Normalized = TranslationStore.Normalize(row.Text);
                row.Bounds = GetCombinedLineBounds(visual);
                if (row.Bounds.IsEmpty || row.Text.Length == 0) continue;
                rows.Add(row);
            }
            return rows;
        }

        private static SkillTextSpan SkillSpan(SkillTextRow row, int start, int length)
        {
            if (row == null || length <= 0 || start < 0 || start >= row.Text.Length)
                return null;
            length = Math.Min(length, row.Text.Length - start);
            string text = row.Text.Substring(start, length);
            if (text.Trim().Length == 0) return null;
            bool measured;
            RectangleF bounds = PreciseTextSpan(row.Lines, row.Text, start, length, out measured, true);
            if (row.Synthetic && !row.Measured) measured = false;
            if (bounds.IsEmpty) bounds = row.Bounds;
            return new SkillTextSpan {
                Row = row, Start = start, Length = length, Bounds = bounds,
                Measured = measured, Text = text
            };
        }

        private static void AddSpanRows(SkillSectionPlan section, List<SkillTextSpan> spans)
        {
            foreach (SkillTextSpan span in spans)
            {
                if (span == null || span.Bounds.IsEmpty) continue;
                section.Measured = section.Measured && span.Measured;
                section.RawRows.Add(span.Bounds);
                if (section.RawBounds.IsEmpty) section.RawBounds = span.Bounds;
                else section.RawBounds = RectangleF.Union(section.RawBounds, span.Bounds);
                if (section.Observed.Length > 0) section.Observed += " ";
                section.Observed += span.Text.Trim();
            }
        }

        private static int FirstSkillMarker(SkillTextRow row, Regex pattern)
        {
            Match match = pattern.Match(row.Text);
            return match.Success ? match.Index : Int32.MaxValue;
        }

        private static int FirstAnyLevelMarker(SkillTextRow row)
        {
            int position = FirstSkillMarker(row, SkillCurrentMarker);
            position = Math.Min(position, FirstSkillMarker(row, SkillNextMarker));
            position = Math.Min(position, FirstSkillMarker(row, SkillRequiredMarker));
            return position;
        }

        private static string CleanSkillBodyChinese(string text)
        {
            string body = text ?? "";
            body = Regex.Replace(body,
                @"^\s*[\[【]?\s*(?:最高等级|最大等级)\s*[:：]?\s*\d+\s*[\]】]?\s*", "");
            body = Regex.Split(body, @"(?:前置技能|需要技能)\s*[:：]")[0].Trim();
            body = Regex.Replace(body, @"\s*冷却时间\s*[:：]\s*$", "").Trim();
            return body.Trim();
        }

        private static bool LooksLikeSkillEffectBody(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length < 12) return false;
            return normalized.Contains("mp ") || normalized.Contains("hp ") ||
                normalized.Contains("damage") || normalized.Contains("attack") ||
                normalized.Contains("recover") || normalized.Contains("speed") ||
                normalized.Contains("rate") || normalized.Contains("chance") ||
                normalized.Contains("range") || normalized.Contains("duration") ||
                normalized.Contains("sec") || normalized.Contains("mastery") ||
                normalized.Contains("accuracy") || normalized.Contains("def");
        }

        private void AddSkillPanelLabels(List<OverlayLabel> output, OcrPanelInfo panel, float ocrScale)
        {
            if (panel == null || panel.Lines.Count == 0) return;
            SkillPanelPlan plan = BuildSkillPanelPlan(panel, ocrScale);
            if (plan == null || plan.Sections.Count == 0) return;
            RectangleF panelOverlay = plan.OverlayBounds;
            output.RemoveAll(delegate(OverlayLabel label) {
                if (label == null) return false;
                string role = label.SemanticRole ?? "";
                if (!role.StartsWith("skill-", StringComparison.Ordinal)) return false;
                RectangleF bounds = label.HasSourceBounds ? label.SourceBounds : label.Bounds;
                return bounds.IntersectsWith(panelOverlay);
            });
            foreach (SkillSectionPlan section in plan.Sections)
            {
                if (section.Status != "planned" && section.Status != "header-only") continue;
                if (!section.Measured)
                {
                    section.Status = "preserved";
                    section.PreservedReason = "section-boundary-estimated";
                    plan.Preserved++;
                    plan.AddPreservedReason(section.PreservedReason);
                    continue;
                }
                if (AddSkillSectionLabel(output, plan, section, ocrScale)) plan.Planned++;
                else
                {
                    section.Status = "preserved";
                    section.PreservedReason = "section-layout-unavailable";
                    plan.Preserved++;
                    plan.AddPreservedReason(section.PreservedReason);
                }
            }
            Program.LogRuntimeEvent("skill-panel", plan.Describe());
        }

        private SkillPanelPlan BuildSkillPanelPlan(OcrPanelInfo panel, float ocrScale)
        {
            List<OcrLine> lines = SkillVisualLines(panel.Lines);
            List<SkillTextRow> rows = SplitSkillSemanticRows(BuildSkillTextRows(lines), translations);
            if (rows.Count == 0) return null;
            SkillPanelPlan plan = new SkillPanelPlan();
            plan.Scale = ocrScale <= 0.0f ? 1.0f : ocrScale;
            plan.RawBounds = GetCombinedLineBounds(lines);
            plan.OverlayBounds = OverlaySpan(plan.RawBounds, plan.Scale);
            plan.OverlayBounds.Inflate(2, 2);

            // Shared panel typography: one family and one pixel unit for all prose
            // sections. Title is a fixed, slightly larger level, never a per-section
            // 8px squeeze. A row that cannot fit at this size preserves its original.
            List<float> heights = new List<float>();
            foreach (SkillTextRow row in rows)
                if (row.Bounds.Height > 0) heights.Add(row.Bounds.Height / plan.Scale);
            heights.Sort();
            List<float> singleLine = new List<float>();
            foreach (float height in heights)
                if (height <= 26.0f) singleLine.Add(height);
            float rowHeight = singleLine.Count == 0 ? 14.0f :
                singleLine[singleLine.Count / 2];
            plan.BodyFontSize = Math.Max(11.0f, Math.Min(16.0f, rowHeight - 1.0f));
            plan.TitleFontSize = Math.Max(14.0f, Math.Min(24.0f, plan.BodyFontSize + 3.0f));
            plan.FontFamily = "SimSun";

            SkillTextRow masterRow = null, currentRow = null, nextRow = null, requiredRow = null;
            foreach (SkillTextRow row in rows)
            {
                if (masterRow == null && SkillMasterMarker.IsMatch(row.Text)) masterRow = row;
                if (currentRow == null && SkillCurrentMarker.IsMatch(row.Text)) currentRow = row;
                if (nextRow == null && SkillNextMarker.IsMatch(row.Text)) nextRow = row;
                if (requiredRow == null && SkillRequiredMarker.IsMatch(row.Text)) requiredRow = row;
            }
            int firstMarkerIndex = rows.Count;
            foreach (SkillTextRow marker in new SkillTextRow[] { masterRow, currentRow, nextRow, requiredRow })
                if (marker != null && marker.Index < firstMarkerIndex) firstMarkerIndex = marker.Index;

            SkillTextRow titleRow = null;
            for (int i = Math.Max(0, firstMarkerIndex - 1); i >= 0; i--)
            {
                if (FirstAnyLevelMarker(rows[i]) != Int32.MaxValue ||
                    SkillMasterMarker.IsMatch(rows[i].Text)) continue;
                if (translations.FindSkillNameMatches(rows[i].Text).Count == 1)
                { titleRow = rows[i]; break; }
            }
            if (titleRow == null)
            for (int i = Math.Max(0, firstMarkerIndex - 1); i >= 0; i--)
            {
                if (FirstAnyLevelMarker(rows[i]) != Int32.MaxValue ||
                    SkillMasterMarker.IsMatch(rows[i].Text)) continue;
                string candidateTitle = rows[i].Text.Trim();
                if (candidateTitle.Length < 4 || candidateTitle.Length > 55) continue;
                if (Regex.IsMatch(candidateTitle, @"[.!?]")) continue;
                if (TranslationStore.Normalize(candidateTitle).Split(
                    new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 6) continue;
                titleRow = rows[i]; break;
            }
            string titleText = titleRow == null ? "" : titleRow.Text.Trim();

            // Body spans: after the master prefix (or after the title when there is
            // no master row), up to the first prerequisite/current/next marker.
            List<SkillTextSpan> bodySpans = new List<SkillTextSpan>();
            for (int i = 0; i < rows.Count; i++)
            {
                SkillTextRow row = rows[i];
                if (row == titleRow) continue;
                if (row == masterRow)
                {
                    Match master = SkillMasterMarker.Match(row.Text);
                    int rest = master.Success ? master.Index + master.Length : row.Text.Length;
                    int marker = FirstAnyLevelMarker(row);
                    int end = marker == Int32.MaxValue ? row.Text.Length : Math.Min(row.Text.Length, marker);
                    if (rest < end) AddSkillSpan(bodySpans, SkillSpan(row, rest, end - rest));
                    if (marker != Int32.MaxValue) break;
                    continue;
                }
                if (row == requiredRow || row == currentRow || row == nextRow)
                {
                    int marker = FirstAnyLevelMarker(row);
                    if (marker > 0) AddSkillSpan(bodySpans, SkillSpan(row, 0, marker));
                    break;
                }
                if (row.Normalized.Length == 0 || IsSkillBodyNoise(row.Text)) continue;
                AddSkillSpan(bodySpans, SkillSpan(row, 0, row.Text.Length));
            }
            string bodyText = CombineSkillSpans(bodySpans);

            string titleId = titleText.Length == 0 ? "" : translations.DetectSkillId(titleText);
            string contentId = bodyText.Length < 12 ? "" : translations.DetectSkillContentId(bodyText);
            string panelId = panel.SkillId ?? "";
            if (titleId.Length > 0 && contentId.Length > 0 && titleId != contentId)
            {
                plan.IdentityAmbiguous = true;
                plan.IdentityReason = "name-content-conflict";
                plan.SkillId = "";
            }
            else if (titleId.Length > 0)
            {
                plan.SkillId = titleId;
                plan.IdentitySource = "unique-title";
            }
            else if (contentId.Length > 0)
            {
                plan.SkillId = contentId;
                plan.IdentitySource = "unique-content";
            }
            else if (panelId.Length > 0)
            {
                plan.SkillId = panelId;
                plan.IdentitySource = "panel-context";
            }
            else
            {
                plan.IdentityAmbiguous = true;
                plan.IdentityReason = titleText.Length > 0 ? "shared-or-ambiguous-name" : "no-visible-identity";
            }

            // Name: only an actually visible title row may produce a name label.
            if (titleRow != null)
            {
                List<MatchResult> names = translations.FindSkillNameMatches(titleRow.Text, plan.SkillId);
                if (names.Count == 0 && plan.SkillId.Length == 0)
                    names = translations.FindSkillNameMatches(titleRow.Text);
                if (names.Count == 1)
                {
                    SkillSectionPlan name = new SkillSectionPlan {
                        Role = "skill-name", MatchSource = "skill-name-from-panel",
                        Translation = names[0].Entry.Chinese, Status = "planned"
                    };
                    SkillTextSpan span = SkillSpan(titleRow, 0, titleRow.Text.Length);
                    if (span != null && span.Measured)
                    {
                        AddSpanRows(name, new List<SkillTextSpan> { span });
                        name.NumericAssociations = "none";
                        plan.Sections.Add(name);
                        plan.Observed++;
                        plan.Matched++;
                    }
                }
            }

            // Master level: visible header only. The number is never borrowed from
            // the dictionary or from another level.
            if (masterRow != null)
            {
                Match master = SkillMasterMarker.Match(masterRow.Text);
                string level = master.Success ? SkillLevelNumber(masterRow.Text,
                    masterRow.Normalized, "master level") : "";
                if (master.Success && level.Length > 0)
                {
                    SkillSectionPlan section = new SkillSectionPlan {
                        Role = "skill-master-level", MatchSource = "skill-master-header",
                        Translation = "最高等级：" + level, Status = "planned"
                    };
                    SkillTextSpan span = SkillSpan(masterRow, master.Index, master.Length);
                    if (span != null && span.Measured)
                    {
                        AddSpanRows(section, new List<SkillTextSpan> { span });
                        section.NumericAssociations = "max-level=" + level;
                        plan.Sections.Add(section);
                        plan.Observed++;
                        plan.Matched++;
                    }
                }
            }

            // Description: exactly the visible body; no dictionary completion of an
            // absent description. A verified complete known body may use the
            // dictionary's exact wording, otherwise the shared vocabulary is used.
            if (bodySpans.Count > 0 && TranslationStore.Normalize(bodyText).Length >= 12)
            {
                SkillSectionPlan body = new SkillSectionPlan {
                    Role = "skill-body", MatchSource = "skill-body-visible",
                    Status = "observed"
                };
                AddSpanRows(body, bodySpans);
                string bodySource;
                bool bodyFromDictionary;
                bool bodyUnsafe;
                string chinese = TranslateVisibleSkillBody(bodyText, plan.SkillId,
                    out bodySource, out bodyFromDictionary, out bodyUnsafe);
                bool matched = chinese.Length > 0;
                if (matched)
                {
                    body.Translation = chinese;
                    body.MatchSource = bodySource;
                    body.FromDictionary = bodyFromDictionary;
                    body.Status = "planned";
                    if (LooksLikeSkillEffectBody(bodyText))
                        body.NumericAssociations = "body-values-preserved";
                    plan.Sections.Add(body);
                    plan.Observed++;
                    plan.Matched++;
                }
                else
                {
                    body.Status = "preserved";
                    body.PreservedReason = bodyUnsafe ? "body-numeric-association-unsafe" :
                        "body-translation-unavailable";
                    plan.Sections.Add(body);
                    plan.Observed++;
                    plan.Preserved++;
                    plan.AddPreservedReason(body.PreservedReason);
                }
            }

            // Required skill: the visible name, relationship and level are kept as a
            // unit. An unresolved name preserves the original row rather than
            // inventing a prerequisite or borrowing another skill's name.
            if (requiredRow != null)
            {
                Match required = SkillRequiredMarker.Match(requiredRow.Text);
                string rest = required.Success ? requiredRow.Text.Substring(required.Index + required.Length).Trim() : "";
                string requiredName = "", requiredLevel = "", requiredRelation = "", requiredRelationChinese = "";
                bool requiredParsed = ParseRequiredSkillCondition(rest, out requiredName,
                    out requiredLevel, out requiredRelation, out requiredRelationChinese);
                string requiredChinese = "";
                List<MatchResult> nameMatches = requiredName.Length == 0 ? new List<MatchResult>() :
                    translations.FindSkillNameMatches(requiredName);
                if (nameMatches.Count == 1) requiredChinese = nameMatches[0].Entry.Chinese;
                SkillSectionPlan section = new SkillSectionPlan {
                    Role = "skill-required", Status = "observed",
                    MatchSource = "skill-required-visible"
                };
                SkillTextSpan span = SkillSpan(requiredRow, required.Index,
                    requiredRow.Text.Length - required.Index);
                if (span != null && span.Measured) AddSpanRows(section, new List<SkillTextSpan> { span });
                if (span != null && span.Measured && requiredParsed && requiredChinese.Length > 0)
                {
                    section.Translation = "需要技能：" + requiredChinese +
                        (String.Equals(requiredRelationChinese, "等级", StringComparison.Ordinal) ?
                            "等级" + requiredLevel : requiredRelationChinese + requiredLevel + "级");
                    section.NumericAssociations = requiredLevel.Length > 0 ?
                        "required-level=" + requiredLevel + ";relation=" +
                        (requiredRelation.Length == 0 ? "plain" : requiredRelation) :
                        "required-level-absent";
                    section.Status = "planned";
                    plan.Sections.Add(section);
                    plan.Observed++;
                    plan.Matched++;
                }
                else
                {
                    section.Status = "preserved";
                    section.PreservedReason = requiredName.Length == 0 ?
                        "required-skill-unparsed" : "required-skill-name-ambiguous";
                    if (requiredParsed && requiredChinese.Length == 0 && requiredName.Length > 0)
                        section.PreservedReason = "required-skill-name-ambiguous";
                    plan.Sections.Add(section);
                    plan.Observed++;
                    plan.Preserved++;
                    plan.AddPreservedReason(section.PreservedReason);
                }
            }

            AddSkillLevelSection(plan, rows, currentRow, false);
            AddSkillLevelSection(plan, rows, nextRow, true);
            ExpandSkillSectionSlots(plan);

            plan.Observed = 0;
            plan.Matched = 0;
            foreach (SkillSectionPlan section in plan.Sections)
            {
                plan.Observed++;
                if (section.Status == "planned") plan.Matched++;
            }
            return plan;
        }

        // A fixed pixel font needs a little more vertical room than a tightly
        // measured OCR row. Expand into blank space between sections, never over a
        // neighbour's measured source rows, so the font is not shrunk per section.
        private static void ExpandSkillSectionSlots(SkillPanelPlan plan)
        {
            for (int i = 0; i < plan.Sections.Count; i++)
            {
                SkillSectionPlan section = plan.Sections[i];
                if (section.RawBounds.IsEmpty || section.RawRows.Count == 0) continue;
                bool title = section.Role == "skill-name";
                float font = title ? plan.TitleFontSize : plan.BodyFontSize;
                float need = (font * 1.25f + 1.0f) * plan.Scale;
                if (section.Role == "skill-current-body" || section.Role == "skill-next-body")
                {
                    if (section.Translation.Length > 24) need = Math.Max(need,
                        (font * 2.35f + 1.0f) * plan.Scale);
                }
                if (section.RawBounds.Height >= need) continue;
                float top = section.RawBounds.Top, bottom = section.RawBounds.Bottom;
                float topLimit = plan.RawBounds.Top;
                float bottomLimit = plan.RawBounds.Bottom;
                if (i > 0 && !plan.Sections[i - 1].RawBounds.IsEmpty)
                    topLimit = Math.Max(topLimit, plan.Sections[i - 1].RawBounds.Bottom + 1.0f * plan.Scale);
                if (i + 1 < plan.Sections.Count && !plan.Sections[i + 1].RawBounds.IsEmpty)
                    bottomLimit = Math.Min(bottomLimit, plan.Sections[i + 1].RawBounds.Top - 1.0f * plan.Scale);
                float want = need - section.RawBounds.Height;
                float down = Math.Min(want, Math.Max(0.0f, bottomLimit - bottom));
                bottom += down; want -= down;
                float up = Math.Min(want, Math.Max(0.0f, top - topLimit));
                top -= up;
                section.RawBounds = RectangleF.FromLTRB(section.RawBounds.Left, top,
                    section.RawBounds.Right, bottom);
            }
        }

        private string TranslateVisibleSkillBody(string bodyText, string skillId,
            out string matchSource, out bool fromDictionary, out bool unsafeNumeric)
        {
            matchSource = "skill-body";
            fromDictionary = false;
            unsafeNumeric = false;
            string chinese = "";
            if (skillId.Length > 0)
            {
                float similarity;
                string overviewChinese;
                if (translations.TryGetVisibleSkillOverviewBody(bodyText, skillId,
                    out overviewChinese, out similarity) && similarity >= 0.62f)
                {
                    chinese = CleanSkillBodyChinese(overviewChinese);
                    if (chinese.Length > 0)
                    {
                        matchSource = "skill-overview-body";
                        fromDictionary = true;
                        return chinese;
                    }
                }
                string candidate;
                if (translations.TryTranslateSkillOverview(bodyText, skillId, out candidate))
                {
                    candidate = CleanSkillBodyChinese(candidate);
                    if (candidate.Length > 0)
                    {
                        matchSource = "skill-overview-template";
                        fromDictionary = true;
                        return candidate;
                    }
                }
            }
            string shared;
            if (translations.TryResolveSharedSkillContent(bodyText, out shared))
            {
                shared = CleanSkillBodyChinese(shared);
                if (shared.Length > 0)
                {
                    matchSource = "skill-shared-content";
                    fromDictionary = true;
                    return shared;
                }
            }
            string vocabulary;
            if (translations.TryTranslateEffectVocabulary(bodyText, out vocabulary))
            {
                vocabulary = CleanSkillBodyChinese(vocabulary);
                string fallbackReason;
                if (vocabulary.Length > 0 &&
                    SkillOverviewSafety.IsSafeFallback(bodyText, vocabulary, out fallbackReason))
                {
                    matchSource = "skill-body-vocabulary";
                    fromDictionary = false;
                    return vocabulary;
                }
                unsafeNumeric = true;
            }
            return "";
        }

        private static bool IsSkillBodyNoise(string text)
        {
            string raw = text ?? "";
            if (raw.IndexOf('\t') >= 0 && Regex.IsMatch(raw, @"\d")) return true;
            string normalized = TranslationStore.Normalize(raw);
            if (normalized.Length == 0) return true;
            string[] tokens = normalized.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            int numbers = 0, letters = 0;
            foreach (string token in tokens)
            {
                if (Regex.IsMatch(token, @"^\d+$")) numbers++;
                else if (Regex.IsMatch(token, @"^[a-z]+$")) letters++;
            }
            return tokens.Length >= 5 && numbers >= 3 && letters <= 2;
        }

        private static SkillTextRow NewSyntheticSkillRow(SkillTextRow source, int start,
            int length, bool measured)
        {
            int end = Math.Min(source.Text.Length, start + length);
            while (start < end && Char.IsWhiteSpace(source.Text[start])) start++;
            while (end > start && Char.IsWhiteSpace(source.Text[end - 1])) end--;
            string text = end > start ? source.Text.Substring(start, end - start) : "";
            SkillTextRow row = new SkillTextRow();
            row.Text = text;
            row.Normalized = TranslationStore.Normalize(text);
            row.Measured = measured;
            row.Synthetic = true;
            OcrLine line = new OcrLine();
            line.Text = text;
            int cursor = 0;
            bool found = false;
            RectangleF bounds = RectangleF.Empty;
            foreach (OcrLine sourceLine in source.Lines)
            {
                if (sourceLine == null || String.IsNullOrEmpty(sourceLine.Text)) continue;
                int lineStart = source.Text.IndexOf(sourceLine.Text, cursor, StringComparison.Ordinal);
                if (lineStart < 0) lineStart = cursor;
                int wordCursor = 0;
                foreach (OcrWord word in sourceLine.Words)
                {
                    string raw = word.Text ?? "";
                    if (raw.Length == 0) continue;
                    int local = sourceLine.Text.IndexOf(raw,
                        Math.Min(wordCursor, sourceLine.Text.Length), StringComparison.Ordinal);
                    if (local < 0) local = wordCursor;
                    int absoluteStart = lineStart + local;
                    int absoluteEnd = absoluteStart + raw.Length;
                    wordCursor = Math.Min(sourceLine.Text.Length, local + raw.Length);
                    int left = Math.Max(start, absoluteStart), right = Math.Min(end, absoluteEnd);
                    if (right <= left) continue;
                    string clipped = source.Text.Substring(left, right - left);
                    RectangleF box = word.BoundingRect;
                    if (left > absoluteStart || right < absoluteEnd)
                        box = new RectangleF(box.X + box.Width * (left - absoluteStart) / raw.Length,
                            box.Y, Math.Max(1.0f, box.Width * (right - left) / raw.Length), box.Height);
                    line.Words.Add(new OcrWord { Text = clipped, BoundingRect = box });
                    bounds = found ? RectangleF.Union(bounds, box) : box;
                    found = true;
                }
                cursor = lineStart + sourceLine.Text.Length;
            }
            row.Bounds = found ? bounds : RectangleF.Empty;
            if (line.Words.Count > 0) row.Lines.Add(line);
            return row;
        }

        // A single OCR line can contain a title, a structured header and prose
        // ("Skill [Master Level: 30] Description..."). Split only at measured
        // marker boundaries and at an exact whole-name prefix; never invent text.
        private static List<SkillTextRow> SplitSkillSemanticRows(List<SkillTextRow> source,
            TranslationStore translations)
        {
            List<SkillTextRow> split = new List<SkillTextRow>();
            foreach (SkillTextRow row in source)
            {
                List<int[]> markers = new List<int[]>();
                int type = 0;
                foreach (Regex pattern in new Regex[] { SkillMasterMarker, SkillCurrentMarker,
                    SkillNextMarker, SkillRequiredMarker })
                {
                    Match match = pattern.Match(row.Text);
                    if (match.Success) markers.Add(new int[] { match.Index, match.Length, type });
                    type++;
                }
                if (markers.Count == 0) { split.Add(row); continue; }
                markers.Sort(delegate(int[] a, int[] b) { return a[0].CompareTo(b[0]); });
                int cursor = 0;
                for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                {
                    int[] marker = markers[markerIndex];
                    if (marker[0] < cursor) continue;
                    if (marker[0] > cursor)
                    {
                        SkillTextSpan before = SkillSpan(row, cursor, marker[0] - cursor);
                        if (before != null && before.Text.Trim().Length > 0)
                            split.Add(NewSyntheticSkillRow(row, cursor, marker[0] - cursor, before.Measured));
                    }
                    int segmentEnd = marker[0] + marker[1];
                    if (marker[2] != 0)
                        segmentEnd = markerIndex + 1 < markers.Count ?
                            markers[markerIndex + 1][0] : row.Text.Length;
                    SkillTextSpan segment = SkillSpan(row, marker[0], segmentEnd - marker[0]);
                    if (segment != null && segment.Text.Trim().Length > 0)
                        split.Add(NewSyntheticSkillRow(row, marker[0], segmentEnd - marker[0], segment.Measured));
                    cursor = segmentEnd;
                }
                if (cursor < row.Text.Length)
                {
                    SkillTextSpan tail = SkillSpan(row, cursor, row.Text.Length - cursor);
                    if (tail != null && tail.Text.Trim().Length > 0)
                        split.Add(NewSyntheticSkillRow(row, cursor, row.Text.Length - cursor, tail.Measured));
                }
            }
            // The last plain row before the first marker is the title slot. If it
            // also contains prose on the same OCR line, split the exact name prefix.
            int firstMarker = split.Count;
            for (int i = 0; i < split.Count; i++)
                if (SkillMasterMarker.IsMatch(split[i].Text) ||
                    SkillCurrentMarker.IsMatch(split[i].Text) ||
                    SkillNextMarker.IsMatch(split[i].Text) ||
                    SkillRequiredMarker.IsMatch(split[i].Text)) { firstMarker = i; break; }
            int titleCandidate = -1;
            if (firstMarker >= split.Count && split.Count > 0)
                titleCandidate = 0;
            else
                for (int i = Math.Min(firstMarker, split.Count) - 1; i >= 0; i--)
                    if (SkillMasterMarker.IsMatch(split[i].Text) == false &&
                        SkillCurrentMarker.IsMatch(split[i].Text) == false &&
                        SkillNextMarker.IsMatch(split[i].Text) == false &&
                        SkillRequiredMarker.IsMatch(split[i].Text) == false)
                    { titleCandidate = i; break; }
            if (titleCandidate >= 0)
            {
                SkillTextRow row = split[titleCandidate];
                for (int at = row.Text.Length - 1; at >= 4; at--)
                {
                    if (row.Text[at] != ' ') continue;
                    string prefix = row.Text.Substring(0, at).Trim();
                    if (prefix.Length < 4 || prefix.Length > 55) continue;
                    if (translations.FindSkillNameMatches(prefix).Count != 1) continue;
                    SkillTextSpan title = SkillSpan(row, 0, at);
                    SkillTextSpan body = SkillSpan(row, at, row.Text.Length - at);
                    if (title == null || body == null) continue;
                    split.RemoveAt(titleCandidate);
                    split.Insert(titleCandidate, NewSyntheticSkillRow(row, at, row.Text.Length - at, body.Measured));
                    split.Insert(titleCandidate, NewSyntheticSkillRow(row, 0, at, title.Measured));
                    break;
                }
            }
            for (int i = 0; i < split.Count; i++) split[i].Index = i;
            return split;
        }

        private static void AddSkillSpan(List<SkillTextSpan> spans, SkillTextSpan span)
        {
            if (span != null && span.Bounds.Width > 0 && span.Bounds.Height > 0) spans.Add(span);
        }

        private static string CombineSkillSpans(List<SkillTextSpan> spans)
        {
            StringBuilder text = new StringBuilder();
            foreach (SkillTextSpan span in spans)
            {
                if (span == null || span.Text.Trim().Length == 0) continue;
                if (text.Length > 0) text.Append(' ');
                text.Append(span.Text.Trim());
            }
            return text.ToString();
        }

        private static bool ParseRequiredSkillCondition(string rest, out string name,
            out string level, out string relation, out string relationChinese)
        {
            name = ""; level = ""; relation = ""; relationChinese = "";
            string text = (rest ?? "").Trim().TrimEnd('#');
            if (text.Length == 0) return false;
            const string relationPattern = @"at\s*least|atleast|at\s*most|atmost|minimum|maximum|no\s+less\s+than|no\s+more\s+than";
            Match match = Regex.Match(text,
                @"^\s*(?<rel>" + relationPattern + @")?\s*(?:level|lv\.?)\s*(?<level>\d{1,3})\s*(?:on\s+)?(?<name>.+?)\s*$",
                RegexOptions.IgnoreCase);
            bool nameBeforeLevel = false;
            if (!match.Success)
            {
                match = Regex.Match(text,
                    @"^\s*(?<name>.+?)\s+(?<rel>" + relationPattern + @")\s*(?:level|lv\.?)\s*(?<level>\d{1,3})\s*$",
                    RegexOptions.IgnoreCase);
                nameBeforeLevel = true;
            }
            if (!match.Success) return false;
            relation = Regex.Replace(match.Groups["rel"].Value.Trim().ToLowerInvariant(),
                @"\s+", " ");
            level = match.Groups["level"].Value;
            name = match.Groups["name"].Value.Trim().TrimEnd('#');
            if (nameBeforeLevel && relation.Length == 0) return false;
            if (level.Length == 0 || name.Length == 0) return false;
            if (relation.Length == 0) { relationChinese = "等级"; return true; }
            if (relation == "at least" || relation == "atleast" ||
                relation == "minimum" || relation == "no less than")
            { relationChinese = "至少"; return true; }
            if (relation == "at most" || relation == "atmost" ||
                relation == "maximum" || relation == "no more than")
            { relationChinese = "至多"; return true; }
            relation = ""; relationChinese = "";
            return false;
        }

        private void AddSkillLevelSection(SkillPanelPlan plan, List<SkillTextRow> rows,
            SkillTextRow markerRow, bool next)
        {
            if (markerRow == null) return;
            Regex marker = next ? SkillNextMarker : SkillCurrentMarker;
            Match header = marker.Match(markerRow.Text);
            if (!header.Success) return;
            string level = SkillLevelNumber(markerRow.Text, markerRow.Normalized,
                next ? "next level" : "current level");
            if (level.Length == 0) return;
            string headerText = next ? "下一级：" : "当前等级：";

            // The effect starts on the header row and continues until the next level
            // marker, prerequisite, or the end of the panel. Nothing from the other
            // level is ever appended.
            List<SkillTextSpan> effectSpans = new List<SkillTextSpan>();
            int firstStart = header.Index + header.Length;
            if (firstStart < markerRow.Text.Length)
                AddSkillSpan(effectSpans, SkillSpan(markerRow, firstStart,
                    markerRow.Text.Length - firstStart));
            for (int i = markerRow.Index + 1; i < rows.Count; i++)
            {
                SkillTextRow row = rows[i];
                int stop = FirstAnyLevelMarker(row);
                if (stop != Int32.MaxValue)
                {
                    if (stop > 0) AddSkillSpan(effectSpans, SkillSpan(row, 0, stop));
                    break;
                }
                AddSkillSpan(effectSpans, SkillSpan(row, 0, row.Text.Length));
            }
            string effectText = CombineSkillSpans(effectSpans);
            string effectChinese = "";
            bool effectMatched = effectText.Length > 0 &&
                translations.TryTranslateSkillEffect(effectText, plan.SkillId, out effectChinese);
            if (effectMatched) effectChinese = effectChinese.Trim();

            SkillSectionPlan section = new SkillSectionPlan {
                Role = next ? "skill-next-body" : "skill-current-body",
                MatchSource = effectMatched ? "skill-effect-template" : "skill-level-header",
                Status = "observed",
                ObservedEffect = effectText,
                HeaderStatus = "observed",
                EffectStatus = effectText.Length == 0 ? "absent" :
                    (effectMatched ? "translated" : "preserved")
            };
            SkillTextSpan headerSpan = effectMatched ?
                SkillSpan(markerRow, 0, markerRow.Text.Length) :
                SkillSpan(markerRow, header.Index, header.Length);
            List<SkillTextSpan> spans = new List<SkillTextSpan>();
            if (headerSpan != null) spans.Add(headerSpan);
            // A failed effect keeps its own pixels. Only the measured header span is
            // replaced; the effect is never hidden behind a successful title. When
            // the effect is translated, the whole level row is replaced by its own
            // combined translation, so there is no estimated split inside that row.
            if (effectMatched) foreach (SkillTextSpan span in effectSpans)
                if (span.Row != markerRow) spans.Add(span);
            if (spans.Count > 0) AddSpanRows(section, spans);
            if (headerSpan == null)
            {
                section.Status = "preserved";
                section.PreservedReason = "level-header-unavailable";
                plan.Sections.Add(section);
                plan.Observed++;
                plan.Preserved++;
                plan.AddPreservedReason(section.PreservedReason);
                return;
            }
            section.Measured = section.Measured && headerSpan.Measured;
            foreach (SkillTextSpan span in spans)
                section.Measured = section.Measured && span.Measured;
            // Give a longer Chinese level row a bounded amount of the blank space
            // below the visible effect rows. SourceLineBounds still contains only the
            // measured OCR rows, so no value or neighbour is sampled as text. The
            // slot stops before the next level header, never over it.
            if (section.RawBounds.Height > 0 && effectMatched)
            {
                float rowHeight = section.RawBounds.Height;
                float extra = Math.Min(rowHeight * 1.2f, 24.0f * plan.Scale);
                float limitBottom = plan.RawBounds.Bottom;
                for (int i = markerRow.Index + 1; i < rows.Count; i++)
                    if (FirstAnyLevelMarker(rows[i]) != Int32.MaxValue)
                    { limitBottom = Math.Min(limitBottom, rows[i].Bounds.Top - 2.0f * plan.Scale); break; }
                if (section.RawBounds.Bottom + extra > limitBottom)
                    extra = Math.Max(0.0f, limitBottom - section.RawBounds.Bottom);
                if (extra > 0)
                    section.RawBounds = RectangleF.FromLTRB(section.RawBounds.Left,
                        section.RawBounds.Top, section.RawBounds.Right,
                        section.RawBounds.Bottom + extra);
            }
            section.Translation = headerText + level +
                (effectMatched && effectChinese.Length > 0 ? "　" + effectChinese : "");
            section.NumericAssociations = "level=" + level +
                (effectMatched ? ";effect-values-preserved" : ";effect-preserved");
            if (effectText.Length > 0 && !effectMatched)
            {
                section.Status = "header-only";
                section.HeaderStatus = "planned";
                section.EffectStatus = "preserved";
                section.EffectPreservedReason = "effect-translation-unavailable";
                section.PreservedReason = section.EffectPreservedReason;
                plan.AddPreservedReason(section.PreservedReason);
                plan.Sections.Add(section);
                plan.Observed++;
                plan.Matched++;
                plan.Preserved++;
                return;
            }
            section.Status = "planned";
            section.HeaderStatus = "planned";
            section.EffectStatus = effectText.Length == 0 ? "absent" : "translated";
            plan.Sections.Add(section);
            plan.Observed++;
            plan.Matched++;
        }

        private bool AddSkillSectionLabel(List<OverlayLabel> output, SkillPanelPlan plan,
            SkillSectionPlan section, float ocrScale)
        {
            if (section == null || section.RawBounds.IsEmpty ||
                String.IsNullOrEmpty(section.Translation)) return false;
            if (!section.Measured)
            {
                section.PreservedReason = "section-boundary-estimated";
                return false;
            }
            RectangleF overlay = OverlaySpan(section.RawBounds, ocrScale);
            if (overlay.IsEmpty || overlay.Width < 4 || overlay.Height < 4) return false;
            RectangleF panel = plan.OverlayBounds;
            if (!panel.IsEmpty)
            {
                overlay = RectangleF.Intersect(overlay, panel);
                if (overlay.IsEmpty) return false;
            }
            List<RectangleF> rowBounds = new List<RectangleF>();
            foreach (RectangleF raw in section.RawRows)
            {
                RectangleF row = OverlaySpan(raw, ocrScale);
                if (row.IsEmpty) continue;
                if (!panel.IsEmpty) row = RectangleF.Intersect(row, panel);
                if (row.IsEmpty) continue;
                // A single OCR block may span several visual text lines. Split a
                // tall erase row into horizontal bands so local background
                // reconstruction can still reason about each line-sized strip.
                float maximumBand = Math.Max(18.0f, plan.BodyFontSize * 1.8f);
                int bands = Math.Max(1, (int)Math.Ceiling(row.Height / maximumBand));
                if (bands <= 1) rowBounds.Add(row);
                else for (int band = 0; band < bands; band++)
                {
                    float top = row.Top + row.Height * band / bands;
                    float bottom = row.Top + row.Height * (band + 1) / bands;
                    rowBounds.Add(RectangleF.FromLTRB(row.Left, top, row.Right, bottom));
                }
            }
            if (rowBounds.Count == 0) rowBounds.Add(overlay);
            section.OverlayRows = rowBounds;
            bool title = section.Role == "skill-name";
            float layoutSize = title ? plan.TitleFontSize : plan.BodyFontSize;
            if (title)
            {
                // A combined OCR row can give the visible English name a very
                // narrow proportional span. Keep the title hierarchy when it fits;
                // otherwise use the smallest readable pixel size, never 8px.
                float widthFit = overlay.Width /
                    Math.Max(1, section.Translation.Length) * 0.95f;
                if (widthFit < layoutSize) layoutSize = Math.Max(11.0f, widthFit);
            }
            OverlayLabel label = new OverlayLabel {
                Bounds = new RectangleF(overlay.Left - 3, overlay.Top - 2,
                    Math.Max(40, overlay.Width + 6), Math.Max(18, overlay.Height + 5)),
                SourceBounds = overlay, HasSourceBounds = true,
                Text = section.Translation,
                Wrap = rowBounds.Count > 1 || section.Translation.Length > 24,
                SemanticRole = section.Role, MatchSource = section.MatchSource,
                Confidence = LabelConfidence.High, PanelBounds = panel,
                SourceLineBounds = rowBounds,
                LayoutFontSize = layoutSize,
                LayoutMinimumFontSize = layoutSize,
                LayoutFontFamily = plan.FontFamily,
                SkillPanel = plan, SkillSection = section,
                SourceBoundaryEstimated = !section.Measured
            };
            if (!section.ProtectedRaw.IsEmpty)
                label.KeepClearBounds = OverlaySpan(section.ProtectedRaw, ocrScale);
            output.Add(label);
            section.Label = label;
            if (section.Status != "header-only") section.Status = "planned";
            return true;
        }
    }
}
