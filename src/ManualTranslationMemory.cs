using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
namespace MapleOverlay
{
    internal sealed class ManualMemoryRecord
    {
        internal Rectangle Source, Anchor;
        internal byte[] SourceHash, AnchorHash;
        internal string Text;
        internal object Payload;
    }
    // One short-lived prior frame, exact RGB+context validation, never coordinate-only reuse.
    internal sealed class ManualTranslationMemory
    {
        private readonly List<ManualMemoryRecord> records = new List<ManualMemoryRecord>();
        private string context = ""; private DateTime capturedUtc;
        internal void Clear() { records.Clear(); context = ""; }
        internal void Store(string key, DateTime now, IList<ManualMemoryRecord> source)
        {
            Clear(); if (String.IsNullOrEmpty(key) || source == null) return;
            context = key; capturedUtc = now;
            for (int i = 0; i < source.Count && records.Count < 128; i++) if (source[i] != null) records.Add(source[i]);
        }
        internal List<ManualMemoryRecord> Match(string key, DateTime now, int[] frame, int width, int height)
        {
            List<ManualMemoryRecord> matches = new List<ManualMemoryRecord>();
            if (key != context || now < capturedUtc || now - capturedUtc > TimeSpan.FromSeconds(30)) { Clear(); return matches; }
            foreach (ManualMemoryRecord record in records)
                if (Equal(record.SourceHash, Fingerprint(frame, width, height, record.Source)) &&
                    Equal(record.AnchorHash, Fingerprint(frame, width, height, record.Anchor))) matches.Add(record);
            return matches;
        }
        internal static ManualMemoryRecord Create(int[] frame, int width, int height, Rectangle source, string text, object payload)
        {
            Rectangle anchor = source; anchor.Inflate(24, 20);
            anchor = Rectangle.Intersect(anchor, new Rectangle(0, 0, width, height));
            byte[] sourceHash = Fingerprint(frame, width, height, source), anchorHash = Fingerprint(frame, width, height, anchor);
            if (sourceHash == null || anchorHash == null) return null;
            return new ManualMemoryRecord { Source = source, Anchor = anchor, SourceHash = sourceHash, AnchorHash = anchorHash, Text = text, Payload = payload };
        }
        private static bool Equal(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0; for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i]; return difference == 0;
        }
        private static byte[] Fingerprint(int[] frame, int width, int height, Rectangle region)
        {
            if (frame == null || width < 1 || height < 1 || (long)width * height != frame.Length ||
                region.X < 0 || region.Y < 0 || region.Width < 1 || region.Height < 1 ||
                (long)region.X + region.Width > width || (long)region.Y + region.Height > height || (long)region.Width * region.Height > 131072) return null;
            byte[] bytes = new byte[region.Width * region.Height * 3]; int target = 0;
            for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
            { int value = frame[y * width + x]; bytes[target++] = (byte)value; bytes[target++] = (byte)(value >> 8); bytes[target++] = (byte)(value >> 16); }
            using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(bytes);
        }
    }
}
