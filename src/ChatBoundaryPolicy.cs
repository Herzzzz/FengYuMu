using System;
using System.Text.RegularExpressions;
namespace MapleOverlay
{
    internal static class ChatBoundaryPolicy
    {
        internal static string RepairBadges(string value)
        {
            string line = value ?? "";
            // A measured OCR badge may include a comma/dash before its colon. Keep
            // actual player names, their digits, and message punctuation intact.
            line = Regex.Replace(line,
                @"(?<![A-Za-z0-9_])(?<name>[A-Za-z][A-Za-z0-9_]{2,23})\s+(?<badge>[0Oo@±]{1,4})[.,\-]{1,2}\s*[:：]",
                "${name} ${badge}:");
            // Lower-case channel glyphs are accepted only with a colon and an explicit
            // trade command; ordinary prose such as 'words: ...' remains message text.
            line = Regex.Replace(line,
                @"(?<![A-Za-z0-9_])(?<name>[A-Za-z][A-Za-z0-9_]{2,23})\s+(?<badge>[a-z]{1,3})\s*[:：]\s*(?=[BSR8]\s*>)",
                delegate(Match m) {
                    if (IsContinuationWord(m.Groups["name"].Value)) return m.Value;
                    return m.Groups["name"].Value + " " + m.Groups["badge"].Value.ToUpperInvariant() + ": ";
                });
            // A missing colon is not enough alone. A short numeric/O badge followed
            // immediately by an explicit trade command is independent boundary evidence.
            line = Regex.Replace(line,
                @"(?<![A-Za-z0-9_])(?<name>[A-Za-z][A-Za-z0-9_]{2,23})\s+(?:[oO0]|[·.]\d{1,3})\s+(?=[BSR8]\s*>)",
                delegate(Match m) { return IsContinuationWord(m.Groups["name"].Value) ? m.Value : m.Groups["name"].Value + " O: "; });
            return line;
        }
        private static bool IsContinuationWord(string value)
        {
            return Regex.IsMatch(value ?? "", @"^(?:coupons?|scrolls?|offers?|each|mesos|nx|chairs?|rings?|earrings?|spears?|polearms?|ons|words?)$", RegexOptions.IgnoreCase);
        }
        internal static bool HasUnresolvedSpeaker(string value)
        {
            // Do not guess a new player's name when the badge/colon is damaged and no
            // trade command proves the boundary. Surface this record as unconfirmed OCR.
            return Regex.IsMatch(value ?? "",
                @"(?:^|[.!?)]\s+)(?<name>[A-Z][A-Za-z0-9_]{2,23})\s+[oO0]\s+(?=[A-Za-z])");
        }
    }
}
