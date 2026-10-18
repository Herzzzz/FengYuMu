using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MapleOverlay
{
    // One shared typography decision for every field in one equipment panel. This is
    // deliberately panel-scoped, not label-scoped: field keys keep the same family,
    // pixel unit and base size instead of shrinking independently down to 8px.
    internal sealed class EquipmentPanelTypography
    {
        internal string Family;
        internal GraphicsUnit Unit;
        internal float FieldSize;
        internal float MinimumSize;
        internal float SourceHeight;

        internal Font CreateFieldFont()
        {
            return new Font(Family, FieldSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        internal static EquipmentPanelTypography ForRows(IList<RectangleF> rows, RectangleF panel)
        {
            List<float> heights = new List<float>();
            if (rows != null)
                foreach (RectangleF row in rows)
                {
                    if (Single.IsNaN(row.Height) || Single.IsInfinity(row.Height)) continue;
                    if (row.Height < 5.0f || row.Height > 120.0f) continue;
                    heights.Add(row.Height);
                }
            float sourceHeight = 16.0f;
            if (heights.Count > 0)
            {
                heights.Sort();
                sourceHeight = heights[heights.Count / 2];
            }
            EquipmentPanelTypography type = new EquipmentPanelTypography();
            type.Family = "Microsoft YaHei";
            type.Unit = GraphicsUnit.Pixel;
            type.SourceHeight = sourceHeight;
            // Microsoft YaHei's line box is taller than SimSun's. 0.72 leaves room for
            // ascenders/descenders in the measured source row while the floor keeps the
            // field readable. The exact size is reused by every field in this panel.
            float size = (float)Math.Round(sourceHeight * 0.72f);
            if (size < 11.0f) size = 11.0f;
            if (size > 22.0f) size = 22.0f;
            if (panel.Height > 0.0f && size > panel.Height)
                size = Math.Max(11.0f, panel.Height);
            type.FieldSize = size;
            type.MinimumSize = 11.0f;
            return type;
        }

        internal string Describe()
        {
            return "family=" + Family + " unit=" + Unit +
                " size=" + FieldSize.ToString("0.#", CultureInfo.InvariantCulture) +
                " floor=" + MinimumSize.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class EquipmentFieldPlan
    {
        internal string Key;
        internal string Chinese;
        internal string MatchSource;
        internal string OriginalName;
        internal string OriginalValue;
        internal string Translation;
        internal RectangleF KeySourceBounds;
        internal RectangleF ValueGuardBounds;
        internal RectangleF PanelBounds;
        internal RectangleF RowBounds;
        internal EquipmentPanelTypography Typography;
        internal bool HasValue;
        internal bool MergedValueSplit;
        internal bool MergedValueBaked;
        internal bool PartialValue;
        internal string ValueMode;
        internal string Status;
        internal string PreservedReason;
        internal float PreparedFontSize;

        internal bool ValuePixelsPreserved
        {
            get { return ValueMode == "value-pixels-preserved"; }
        }

        internal string Describe()
        {
            return "key=" + Key +
                " value=" + (OriginalValue ?? "") +
                " valueMode=" + ValueMode +
                " status=" + Status +
                " source=" + KeySourceBounds.Left.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + KeySourceBounds.Top.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + KeySourceBounds.Width.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + KeySourceBounds.Height.ToString("0.#", CultureInfo.InvariantCulture) +
                " guard=" + ValueGuardBounds.Left.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + ValueGuardBounds.Top.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + ValueGuardBounds.Width.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + ValueGuardBounds.Height.ToString("0.#", CultureInfo.InvariantCulture) +
                (String.IsNullOrEmpty(PreservedReason) ? "" : " preservedReason=" + PreservedReason);
        }
    }

    internal sealed class EquipmentPanelPlan
    {
        internal RectangleF PanelBounds;
        internal EquipmentPanelTypography Typography;
        internal List<EquipmentFieldPlan> Fields = new List<EquipmentFieldPlan>();
        internal string RecognitionReason;
        internal int Matched, Prepared, Painted, Preserved;

        internal void Add(EquipmentFieldPlan field)
        {
            if (field == null) return;
            Fields.Add(field);
            Matched++;
            if (field.Status == "prepared") Prepared++;
        }

        internal string Describe()
        {
            string keys = "";
            foreach (EquipmentFieldPlan field in Fields)
            {
                if (keys.Length > 0) keys += ",";
                keys += field.Key;
                if (keys.Length > 160) { keys += "..."; break; }
            }
            return "recognized=" + (String.IsNullOrEmpty(RecognitionReason) ? 0 : 1) +
                " matched=" + Matched + " prepared=" + Prepared +
                " painted=" + Painted + " preserved=" + Preserved +
                " keys=" + keys +
                " panel=" + PanelBounds.Left.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + PanelBounds.Top.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + PanelBounds.Width.ToString("0.#", CultureInfo.InvariantCulture) +
                "," + PanelBounds.Height.ToString("0.#", CultureInfo.InvariantCulture) +
                (Typography == null ? "" : " typography=" + Typography.Describe());
        }
    }

    internal static class EquipmentPanelLayout
    {
        internal static EquipmentFieldPlan BuildField(EquipmentField field, string rowText,
            RectangleF keyBounds, RectangleF valueBounds, RectangleF rowBounds,
            RectangleF panelBounds, EquipmentPanelTypography typography)
        {
            if (field == null || String.IsNullOrEmpty(field.Key) || keyBounds.IsEmpty ||
                !IsFinite(keyBounds)) return null;
            if (typography == null)
                typography = EquipmentPanelTypography.ForRows(
                    new RectangleF[] { rowBounds.IsEmpty ? keyBounds : rowBounds }, panelBounds);

            EquipmentFieldPlan plan = new EquipmentFieldPlan();
            plan.Key = field.Key;
            plan.Chinese = field.Chinese ?? "";
            plan.MatchSource = "equipment-field:" + field.Key;
            plan.OriginalName = SafeSubstring(rowText, field.Start, field.Length);
            plan.OriginalValue = field.HasValue
                ? SafeSubstring(rowText, field.ValueStart, field.ValueLength) : "";
            plan.HasValue = field.HasValue && plan.OriginalValue.Length > 0;
            plan.Translation = plan.Chinese;
            plan.Typography = typography;
            plan.RowBounds = rowBounds;
            plan.Status = "matched";

            RectangleF key = keyBounds;
            RectangleF guard = RectangleF.Empty;
            bool valueBox = field.HasValue && !valueBounds.IsEmpty && IsFinite(valueBounds);
            // Touching boxes (a merged OCR word) are treated like overlap: trimming the
            // key edge is safer than letting the translation grow into the value.
            bool overlap = valueBox && valueBounds.Left < keyBounds.Right + 1.0f;
            if (overlap)
            {
                // The OCR word merged key and value ("INT+5"). Split at the measured
                // value edge and leave a two-pixel protection gap. Only when that would
                // erase most of the key do we preserve the complete raw value textually
                // inside the replacement, because no safe key-only split exists.
                float split = valueBounds.Left - 2.0f;
                if (split > keyBounds.Left + 6.0f)
                {
                    key = RectangleF.FromLTRB(keyBounds.Left, keyBounds.Top, split, keyBounds.Bottom);
                    plan.MergedValueSplit = true;
                }
                else
                {
                    key = RectangleF.Union(keyBounds, valueBounds);
                    plan.Translation = plan.Chinese + "：" + plan.OriginalValue;
                    plan.MergedValueBaked = true;
                }
            }
            if (key.Width < 1.0f) key.Width = 1.0f;

            if (plan.MergedValueBaked)
            {
                plan.ValueMode = "value-text-baked";
                plan.ValueGuardBounds = RectangleF.Empty;
            }
            else if (valueBox)
            {
                guard = valueBounds;
                plan.ValueMode = "value-pixels-preserved";
            }
            else if (field.HasValue)
            {
                // OCR produced a value string but no reliable value rectangle. Reserve
                // the rest of this visual row so painting cannot cover it accidentally.
                guard = RestOfRow(rowBounds, key);
                plan.PartialValue = true;
                plan.ValueMode = "value-bounds-unmeasured";
            }
            else
            {
                plan.ValueMode = "no-value";
                guard = RectangleF.Empty;
            }

            if (!plan.MergedValueBaked && !guard.IsEmpty)
            {
                // A defence-in-depth repair for proportionally-estimated word boxes:
                // never let a guard touch the key's own replacement region.
                if (guard.Left < key.Right + 1.0f)
                    guard = RectangleF.FromLTRB(key.Right + 2.0f, guard.Top,
                        Math.Max(key.Right + 3.0f, guard.Right), guard.Bottom);
                if (guard.Width < 1.0f) guard = RectangleF.Empty;
            }

            plan.KeySourceBounds = key;
            plan.ValueGuardBounds = guard;
            RectangleF panel = panelBounds.IsEmpty ? rowBounds : panelBounds;
            if (panel.IsEmpty || !IsFinite(panel)) panel = key;
            panel.Inflate(2.0f, 2.0f);
            if (!panel.Contains(key))
                panel = RectangleF.Union(panel, RectangleF.Inflate(key, 2.0f, 2.0f));
            plan.PanelBounds = panel;
            plan.Status = plan.Translation.Length > 0 ? "prepared" : "preserved";
            if (plan.Status != "prepared") plan.PreservedReason = "empty-translation";
            if (plan.MergedValueBaked)
            {
                // The value shares one OCR word with its key and no safe split exists.
                // Painting the whole merged word would replace the value's coloured
                // pixels, so keep the original row and report why.
                plan.Status = "preserved";
                plan.PreservedReason = "merged-value-region-unsafe";
            }
            return plan;
        }

        private static RectangleF RestOfRow(RectangleF row, RectangleF key)
        {
            if (row.IsEmpty || !IsFinite(row)) return RectangleF.Empty;
            float left = key.Right + 2.0f;
            if (left >= row.Right - 1.0f) return RectangleF.Empty;
            return RectangleF.FromLTRB(left, row.Top, row.Right, row.Bottom);
        }

        private static string SafeSubstring(string text, int start, int length)
        {
            if (String.IsNullOrEmpty(text) || start < 0 || length <= 0 || start >= text.Length)
                return "";
            return text.Substring(start, Math.Min(length, text.Length - start));
        }

        internal static bool IsFinite(RectangleF value)
        {
            return !Single.IsNaN(value.X) && !Single.IsInfinity(value.X) &&
                !Single.IsNaN(value.Y) && !Single.IsInfinity(value.Y) &&
                !Single.IsNaN(value.Width) && !Single.IsInfinity(value.Width) &&
                !Single.IsNaN(value.Height) && !Single.IsInfinity(value.Height);
        }
    }
}
