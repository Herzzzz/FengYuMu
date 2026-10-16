using System;
using System.Collections.Generic;
namespace MapleOverlay
{
    // A full chat viewport can change without growing in row count. Confirm an
    // unanchored replacement across captures instead of treating it as no new text forever.
    internal sealed class ChatFrameContinuity
    {
        private List<string> pending = new List<string>();
        internal void Reset() { pending.Clear(); }
        internal List<string> Resolve(List<string> previous, List<string> current,
            List<string> differences, Func<string, string, bool> equivalent, out bool advance)
        {
            advance = false;
            if (current == null || current.Count == 0) return new List<string>();
            if (previous == null || previous.Count == 0 || SharesRow(previous, current, equivalent))
            { Reset(); advance = true; return differences ?? new List<string>(); }
            if (SharesRow(pending, current, equivalent))
            { Reset(); advance = true; return new List<string>(current); }
            pending = new List<string>(current);
            return new List<string>();
        }
        private static bool SharesRow(List<string> left, List<string> right, Func<string,string,bool> equivalent)
        {
            foreach (string a in left) foreach (string b in right) if (equivalent(a,b)) return true;
            return false;
        }
    }
}
