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

        // Row-index form used by the live chat queue. It keeps the same pending
        // evidence state as Resolve but returns current row indices, so duplicate
        // text on different physical rows never has to be re-found by occurrence.
        internal List<int> ResolveIndices(List<string> previous, List<string> current,
            List<int> differences, Func<string, string, bool> equivalent, out bool advance)
        {
            advance = false;
            if (current == null || current.Count == 0) return new List<int>();
            if (previous == null || previous.Count == 0 || SharesRow(previous, current, equivalent))
            { Reset(); advance = true; return differences ?? new List<int>(); }
            if (SharesRow(pending, current, equivalent))
            {
                Reset(); advance = true;
                List<int> all = new List<int>();
                for (int index = 0; index < current.Count; index++) all.Add(index);
                return all;
            }
            pending = new List<string>(current);
            return new List<int>();
        }
        private static bool SharesRow(List<string> left, List<string> right, Func<string,string,bool> equivalent)
        {
            foreach (string a in left) foreach (string b in right) if (equivalent(a,b)) return true;
            return false;
        }
    }
}
