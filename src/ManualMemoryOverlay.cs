using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        private readonly HashSet<string> requestQuestMemoryIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly ManualTranslationMemory manualMemory = new ManualTranslationMemory();
        private string ManualMemoryContext(SceneSnapshot scene, string focusedIdentity)
        {
            MapleWindowInfo target = mapleWindows.Locked; if (target == null) return "";
            int kinds = 0; foreach (SceneKind kind in SceneClassifier.AtomicKinds) if (scene.IsStrong(kind)) kinds |= (int)kind;
            if (kinds == 0) return "";
            string questIdentity = "";
            if (scene.IsStrong(SceneKind.Quest))
            {
                if (requestQuestMemoryIds.Count != 1) return "";
                foreach (string id in requestQuestMemoryIds) questIdentity = id;
            }
            if ((scene.IsStrong(SceneKind.Skill) || scene.IsStrong(SceneKind.Item)) && String.IsNullOrEmpty(focusedIdentity)) return "";
            return target.ProcessId + ":" + target.Handle.ToInt64() + ":" + mapleWindows.GeometryVersion +
                ":" + (int)translationRangeMode + ":" + translations.Revision + ":" + kinds + ":" + focusedIdentity + ":" + questIdentity;
        }
        private List<ManualMemoryRecord> MergeManualMemory(Bitmap clean, Rectangle screen, string context, List<OverlayLabel> current)
        {
            List<ManualMemoryRecord> pending = new List<ManualMemoryRecord>();
            if (String.IsNullOrEmpty(context)) { manualMemory.Clear(); return pending; }
            int[] pixels = new int[clean.Width * clean.Height];
            BitmapData data = clean.LockBits(new Rectangle(0, 0, clean.Width, clean.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < clean.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * clean.Width, clean.Width); }
            finally { clean.UnlockBits(data); }
            int reused = 0;
            foreach (ManualMemoryRecord record in manualMemory.Match(context, DateTime.UtcNow, pixels, clean.Width, clean.Height))
            {
                OverlayLabel previous = record.Payload as OverlayLabel; if (previous == null) continue;
                bool overlap = false;
                foreach (OverlayLabel fresh in current)
                {
                    RectangleF bounds = fresh.HasSourceBounds ? fresh.SourceBounds : fresh.Bounds;
                    if (bounds.IntersectsWith(previous.SourceBounds)) { overlap = true; break; }
                }
                // Any fresh overlapping result wins, including a changed or lower-confidence reading.
                if (overlap) continue;
                current.Add(CloneMemoryLabel(previous)); reused++;
            }
            int pixelsStored = 0;
            foreach (OverlayLabel label in current)
            {
                if (label.Confidence != LabelConfidence.High || !label.HasSourceBounds ||
                    (label.MatchSource == "ai-panel" || label.MatchSource == "panel-ai") || label.Text.Length > 4096 || !OverlayPaintSafety.IsFiniteRect(label.SourceBounds)) continue;
                Rectangle area = Rectangle.Ceiling(label.SourceBounds); area.Offset(Bounds.Left - screen.Left, Bounds.Top - screen.Top);
                long cost = (long)(area.Width + 48) * (area.Height + 40);
                if (cost > 262144 - pixelsStored || pending.Count >= 128) continue;
                ManualMemoryRecord record = ManualTranslationMemory.Create(pixels, clean.Width, clean.Height, area, label.Text, label);
                if (record != null) { pending.Add(record); pixelsStored += (int)cost; }
            }
            Program.LogRuntimeEvent("manual-memory", "reused=" + reused + " eligible=" + pending.Count);
            return pending;
        }
        private static OverlayLabel CloneMemoryLabel(OverlayLabel value)
        {
            return new OverlayLabel { Bounds=value.Bounds,Text=value.Text,Wrap=value.Wrap,StableLayout=value.StableLayout,
                SourceBounds=value.SourceBounds,HasSourceBounds=value.HasSourceBounds,Confidence=value.Confidence,
                MatchSource=value.MatchSource,ConfidenceReason=value.ConfidenceReason,RenderMode=value.RenderMode,
                SourceLineBounds=value.SourceLineBounds==null?null:new List<RectangleF>(value.SourceLineBounds),
                PanelBounds=value.PanelBounds,SemanticRole=value.SemanticRole,KeepClearBounds=value.KeepClearBounds };
        }
    }
}
