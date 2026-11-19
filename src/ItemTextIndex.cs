using System;
using System.Collections.Generic;
using System.Text;

namespace MapleOverlay
{
    // One visible item-name row, resolved against the shipped name rows. The context
    // planner owns the final choice: a duplicate English name is allowed to share a
    // translation only when every variant has the same Chinese text, otherwise the row
    // stays untouched until the visible description selects one of the IDs.
    internal sealed class ItemNameMatch
    {
        internal string Normalized = "";
        internal string Chinese = "";
        internal string ItemId = "";
        internal int Start;
        internal int Length;
        internal bool Ambiguous;
        internal bool Shared;
        internal readonly List<TranslationEntry> Entries = new List<TranslationEntry>();
    }

    internal sealed partial class TranslationStore
    {
        private void BuildItemTextIndex(List<TranslationEntry> entries)
        {
            Dictionary<string, string> places = ItemTextSemantics.BuildPlaceMap(entries);
            foreach (KeyValuePair<string, string> place in places) itemPlaces[place.Key] = place.Value;

            foreach (TranslationEntry entry in entries)
            {
                if (entry == null) continue;
                if (entry.Category != null &&
                    (entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal) ||
                     entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal)))
                {
                    string id = CategoryId(entry.Category);
                    if (id.Length > 0)
                    {
                        itemNameEntries.Add(entry);
                        List<TranslationEntry> names;
                        if (!itemNamesByNormalized.TryGetValue(entry.Normalized, out names))
                        {
                            names = new List<TranslationEntry>();
                            itemNamesByNormalized.Add(entry.Normalized, names);
                        }
                        names.Add(entry);
                    }
                }
                if (!entry.IsItemText) continue;
                string descriptionId = CategoryId(entry.Category);
                if (descriptionId.Length > 0)
                {
                    List<TranslationEntry> rows;
                    if (!itemDescriptionsById.TryGetValue(descriptionId, out rows))
                    {
                        rows = new List<TranslationEntry>();
                        itemDescriptionsById.Add(descriptionId, rows);
                    }
                    rows.Add(entry);
                }
                if (entry.ItemStatus != ItemSemanticStatus.Trusted ||
                    !entry.IsOrdinaryItemText) continue;
                BuildItemSentenceReferences(entry);
            }
        }

        private void BuildItemSentenceReferences(TranslationEntry entry)
        {
            List<string> english = ItemTextSemantics.SplitSentences(entry.English);
            List<string> chinese = ItemTextSemantics.SplitChineseSentences(entry.Chinese);
            if (english.Count == 0) return;
            if (english.Count != chinese.Count)
            {
                // Some rows combine differently. Full-row matching remains available; only
                // sentence-level partial matches are withheld because the alignment is not
                // independently provable.
                if (english.Count == 1 && chinese.Count > 0)
                {
                    AddItemSentenceReference(entry, entry.English, entry.Chinese, 0);
                }
                return;
            }
            for (int i = 0; i < english.Count; i++)
                AddItemSentenceReference(entry, english[i], chinese[i], i);
        }

        private void AddItemSentenceReference(TranslationEntry entry, string english,
            string chinese, int index)
        {
            string canonical = ItemTextSemantics.CanonicalSentence(english);
            string cleanChinese = ItemTextSemantics.CleanChinese(chinese);
            if (canonical.Length == 0 || cleanChinese.Length == 0) return;
            ItemSentenceReference reference = new ItemSentenceReference {
                Entry = entry,
                English = english.Trim(),
                Normalized = canonical,
                Chinese = cleanChinese,
                Index = index
            };
            itemSentenceReferences.Add(reference);
            List<ItemSentenceReference> rows;
            if (!itemSentenceIndex.TryGetValue(canonical, out rows))
            {
                rows = new List<ItemSentenceReference>();
                itemSentenceIndex.Add(canonical, rows);
            }
            foreach (ItemSentenceReference existing in rows)
                if (Object.ReferenceEquals(existing.Entry, entry) &&
                    existing.Index == index) return;
            rows.Add(reference);
        }

        internal List<TranslationEntry> GetAllItemDescriptions()
        {
            return itemTextEntries;
        }

        internal List<TranslationEntry> GetItemDescriptionsById(string itemId)
        {
            List<TranslationEntry> rows;
            if (!String.IsNullOrEmpty(itemId) && itemDescriptionsById.TryGetValue(itemId, out rows))
                return rows;
            return new List<TranslationEntry>();
        }

        internal List<ItemSentenceReference> FindItemSentenceReferences(string canonical)
        {
            List<ItemSentenceReference> rows;
            if (!String.IsNullOrEmpty(canonical) && itemSentenceIndex.TryGetValue(canonical, out rows))
                // Callers apply item-id and trust filters with RemoveAll. Returning the
                // persistent bucket would make one scoped lookup corrupt every later
                // unscoped or differently scoped lookup in this process.
                return new List<ItemSentenceReference>(rows);
            return new List<ItemSentenceReference>();
        }

        internal List<TranslationEntry> GetItemNameEntriesById(string itemId)
        {
            List<TranslationEntry> result = new List<TranslationEntry>();
            if (String.IsNullOrEmpty(itemId)) return result;
            foreach (TranslationEntry entry in itemNameEntries)
                if (CategoryId(entry.Category) == itemId) result.Add(entry);
            return result;
        }

        internal string ResolvePlaceChinese(string destination)
        {
            return ItemTextSemantics.ResolvePlaceName(destination, itemPlaces);
        }

        // Match a complete visible line, or a visible line that starts with the item name
        // and continues with another row (Windows OCR can merge the tooltip title and body).
        // The caller decides whether the continuation is evidence for this panel; this
        // method only reports the longest exact name prefix and never a fuzzy one.
        internal ItemNameMatch MatchItemNameLine(string text, bool allowContinuation)
        {
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length < 1) return null;
            // An exact whole-line title wins outright. The scroll family shares a long
            // prefix ("Hat HP Scroll: Lesser/Greater/..."), and fuzzy scoring used to mark
            // even an exact title ambiguous, so the whole scroll tooltip stayed English.
            TranslationEntry exactBest = null;
            HashSet<string> exactChinese = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> exactIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in itemNameEntries)
            {
                if (!String.Equals(entry.Normalized, normalized, StringComparison.Ordinal)) continue;
                exactBest = entry;
                exactChinese.Add(entry.Chinese);
                exactIds.Add(CategoryId(entry.Category));
            }
            if (exactBest != null && exactChinese.Count == 1)
            {
                ItemNameMatch exact = new ItemNameMatch {
                    Normalized = exactBest.Normalized, Start = 0, Length = exactBest.Normalized.Length
                };
                foreach (TranslationEntry entry in itemNameEntries)
                    if (String.Equals(entry.Normalized, normalized, StringComparison.Ordinal))
                        exact.Entries.Add(entry);
                if (exactIds.Count == 1) foreach (string id in exactIds) exact.ItemId = id;
                foreach (string value in exactChinese) exact.Chinese = value;
                exact.Shared = exactIds.Count > 1;
                return exact;
            }
            TranslationEntry best = null;
            foreach (TranslationEntry entry in itemNameEntries)
            {
                if (entry.Normalized.Length == 0 || entry.Normalized.Length < normalized.Length &&
                    !allowContinuation) continue;
                if (normalized.Length < entry.Normalized.Length) continue;
                if (!normalized.StartsWith(entry.Normalized, StringComparison.Ordinal)) continue;
                if (normalized.Length > entry.Normalized.Length)
                {
                    if (!TranslationStore.IsBoundary(normalized, entry.Normalized.Length))
                        continue;
                    if (!allowContinuation) continue;
                    // An exact title with a stack quantity is still a title. Longer body
                    // continuations are accepted only through the panel planner.
                    string tail = normalized.Substring(entry.Normalized.Length).Trim();
                    bool quantity = tail.Length > 0 && IsDigitsOnly(tail);
                    if (!quantity && !allowContinuation) continue;
                }
                if (best == null || entry.Normalized.Length > best.Normalized.Length)
                    best = entry;
            }
            if (best == null) return null;
            ItemNameMatch match = new ItemNameMatch {
                Normalized = best.Normalized,
                Start = 0,
                Length = best.Normalized.Length
            };
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> chinese = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in itemNameEntries)
            {
                if (!String.Equals(entry.Normalized, best.Normalized, StringComparison.Ordinal))
                    continue;
                // A line with a quantity suffix ("Screw 250") still belongs to the same
                // name, but the suffix must be a complete trailing number only. An
                // upgraded equips tooltip appends the scroll count to the title
                // ("Black Salt Shoes (+5)"); that suffix is part of the title, not a
                // body continuation, so it must not discard every name entry below.
                string tail = normalized.Substring(entry.Normalized.Length).Trim();
                if (normalized.Length > entry.Normalized.Length && allowContinuation &&
                    tail.Length > 0 && !IsDigitsOnly(tail) &&
                    !IsEnhancementSuffix(tail) &&
                    !IsLikelyBodyContinuation(tail)) continue;
                match.Entries.Add(entry);
                ids.Add(CategoryId(entry.Category));
                chinese.Add(entry.Chinese);
            }
            if (ids.Count == 1) foreach (string id in ids) match.ItemId = id;
            if (chinese.Count == 1)
            {
                foreach (string value in chinese) match.Chinese = value;
            }
            else
            {
                match.Ambiguous = true;
            }
            match.Shared = ids.Count > 1 && chinese.Count == 1;
            return match;
        }

        // A tooltip row can merge several columns, so the item title is not always the
        // whole row: Windows OCR returns "Her zzzz's •Black Salt Shoes (+5)". MatchItemNameLine
        // only accepts a name at offset 0, and the row then falls through to the generic
        // structured translator, which sees the word "Shoes" and paints the entire row as
        // "类型：鞋子" - the title is lost. This lookup finds the longest known name that
        // starts on a word boundary anywhere in the row and reports the raw offsets.
        internal ItemNameMatch FindItemNameInLine(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string[] words = text.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 60) return null;
            List<int> starts = new List<int>();
            int cursor = 0;
            foreach (string word in words)
            {
                int index = text.IndexOf(word, Math.Min(cursor, text.Length), StringComparison.Ordinal);
                if (index < 0) index = Math.Min(cursor, text.Length);
                starts.Add(index);
                cursor = index + word.Length;
            }
            ItemNameMatch best = null;
            for (int i = 0; i < words.Length; i++)
            {
                StringBuilder phrase = new StringBuilder();
                for (int n = 1; n <= 6 && i + n <= words.Length; n++)
                {
                    if (n > 1) phrase.Append(' ');
                    phrase.Append(words[i + n - 1]);
                    string key = Normalize(phrase.ToString());
                    List<TranslationEntry> entries;
                    if (!itemNamesByNormalized.TryGetValue(key, out entries) || entries.Count == 0) continue;
                    int start = starts[i];
                    int end = starts[i + n - 1] + words[i + n - 1].Length;
                    if (best != null && end - start <= best.Length) continue;
                    ItemNameMatch match = new ItemNameMatch {
                        Normalized = key, Start = start, Length = end - start
                    };
                    HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                    HashSet<string> chinese = new HashSet<string>(StringComparer.Ordinal);
                    foreach (TranslationEntry entry in entries)
                    {
                        match.Entries.Add(entry);
                        ids.Add(CategoryId(entry.Category));
                        chinese.Add(entry.Chinese);
                    }
                    if (ids.Count == 1) foreach (string id in ids) match.ItemId = id;
                    if (chinese.Count == 1) foreach (string value in chinese) match.Chinese = value;
                    else match.Ambiguous = true;
                    match.Shared = ids.Count > 1 && chinese.Count == 1;
                    best = match;
                }
            }
            return best;
        }

        internal ItemNameMatch ResolveItemName(string normalizedName, string itemId)
        {
            List<TranslationEntry> entries;
            if (String.IsNullOrEmpty(normalizedName) ||
                !itemNamesByNormalized.TryGetValue(normalizedName, out entries) || entries.Count == 0)
                return null;
            ItemNameMatch match = new ItemNameMatch { Normalized = normalizedName,
                Start = 0, Length = normalizedName.Length };
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> chinese = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in entries)
            {
                string id = CategoryId(entry.Category);
                if (!String.IsNullOrEmpty(itemId) && id != itemId) continue;
                match.Entries.Add(entry);
                ids.Add(id);
                chinese.Add(entry.Chinese);
            }
            if (match.Entries.Count == 0 && !String.IsNullOrEmpty(itemId))
                return ResolveItemName(normalizedName, "");
            if (ids.Count == 1) foreach (string id in ids) match.ItemId = id;
            if (chinese.Count == 1)
            {
                foreach (string value in chinese) match.Chinese = value;
            }
            else match.Ambiguous = true;
            match.Shared = ids.Count > 1 && chinese.Count == 1;
            return match;
        }

        internal string ResolveItemNameChinese(string normalizedName, string itemId)
        {
            ItemNameMatch match = ResolveItemName(normalizedName, itemId);
            return match == null ? "" : match.Chinese;
        }

        private static bool IsDigitsOnly(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            foreach (char c in value) if (!Char.IsDigit(c)) return false;
            return true;
        }

        private static bool IsLikelyBodyContinuation(string tail)
        {
            if (String.IsNullOrEmpty(tail)) return false;
            if (tail.Length < 2) return false;
            return tail.IndexOf(' ') >= 0 || tail.IndexOf('.') >= 0 ||
                tail.IndexOf(':') >= 0 || tail.IndexOf('%') >= 0;
        }

        // The upgraded-equipment title carries the applied scroll count: "(+5)", "( +5 )"
        // or a bare "+5". Windows OCR keeps it attached to the name, so the title match
        // must treat it as decoration. Only a plus sign plus digits qualifies; anything
        // with letters is a body sentence and stays rejected.
        private static bool IsEnhancementSuffix(string tail)
        {
            if (String.IsNullOrEmpty(tail)) return false;
            string value = tail.Trim();
            if (value.Length >= 2 && value[0] == '(' && value[value.Length - 1] == ')')
                value = value.Substring(1, value.Length - 2).Trim();
            else if (value.Length > 0 && value[0] == '(') return false;
            if (value.Length < 2 || value[0] != '+') return false;
            for (int i = 1; i < value.Length; i++)
                if (!Char.IsDigit(value[i])) return false;
            return true;
        }
    }
}
