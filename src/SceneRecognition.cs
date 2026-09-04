using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Media.Ocr;

namespace MapleOverlay
{
    [Flags]
    internal enum SceneKind
    {
        None = 0,
        Character = 1,
        Skill = 2,
        Quest = 4,
        Item = 8,
        Dialogue = 16,
        Shop = 32,
        Inventory = 64
    }

    internal sealed class SceneEvidence
    {
        public SceneKind Kinds;
        public bool StrongCharacter;
        public bool SkillHeader;
        public bool SkillDetail;
        public bool QuestHeader;
        public bool ItemHeader;
        public bool ItemDetail;
        public bool DialogueAnchor;
        public bool DialogueText;
        public string TaskId = "";
    }

    internal sealed class SceneSnapshot
    {
        private readonly Dictionary<SceneKind, int> scores = new Dictionary<SceneKind, int>();
        private readonly Dictionary<SceneKind, int> anchors = new Dictionary<SceneKind, int>();

        public void Add(SceneKind kinds, int score)
        {
            foreach (SceneKind kind in SceneClassifier.AtomicKinds)
            {
                if ((kinds & kind) == 0) continue;
                int current;
                scores.TryGetValue(kind, out current);
                scores[kind] = current + score;
                anchors.TryGetValue(kind, out current);
                anchors[kind] = current + 1;
            }
        }

        public int Score(SceneKind kind)
        {
            int value;
            return scores.TryGetValue(kind, out value) ? value : 0;
        }

        public int Anchors(SceneKind kind)
        {
            int value;
            return anchors.TryGetValue(kind, out value) ? value : 0;
        }

        public bool IsStrong(SceneKind kind)
        {
            return Score(kind) >= 120 || Anchors(kind) >= 2;
        }

        public bool HasRecognizablePanel()
        {
            foreach (SceneKind kind in SceneClassifier.AtomicKinds)
                if (Score(kind) >= 100) return true;
            return false;
        }

        public string Describe()
        {
            StringBuilder result = new StringBuilder();
            foreach (SceneKind kind in SceneClassifier.AtomicKinds)
            {
                int score = Score(kind);
                if (score <= 0) continue;
                if (result.Length > 0) result.Append('>');
                result.Append(SceneClassifier.Name(kind)).Append(':').Append(score)
                    .Append('/').Append(Anchors(kind));
            }
            return result.Length == 0 ? "普通界面" : result.ToString();
        }
    }

    internal static class ContinuousTranslationPolicy
    {
        internal static bool ShouldTranslate(SceneSnapshot snapshot, bool visualCharacter)
        {
            return visualCharacter || (snapshot != null && snapshot.HasRecognizablePanel());
        }

        internal static int NextInterval(bool panelVisible, int consecutiveMisses,
            int consecutiveFailures)
        {
            if (consecutiveFailures > 0)
                return Math.Min(2600, 800 + consecutiveFailures * 400);
            if (panelVisible) return 240;
            return Math.Min(900, 260 + Math.Max(0, consecutiveMisses) * 160);
        }

        internal static bool ShouldRunProbe(bool automatic, bool restoringVisibleResult,
            bool forcedBenchmarkProbe)
        {
            // A visible panel was already confirmed on the preceding cycle. Re-running the
            // preliminary full-screen OCR only delayed updates; do one fresh precise pass and
            // let an empty result close the panel. Idle screens still use the cheap probe.
            return forcedBenchmarkProbe || (automatic && !restoringVisibleResult);
        }
    }

    internal static class CharacterPanelPolicy
    {
        internal static bool IsInformation(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("character info") ||
                normalized.Contains("citizenship") ||
                (normalized.Contains("request party") && normalized.Contains("request trade"));
        }

        internal static bool IsStatistics(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            if (IsInformation(normalized)) return false;
            if (normalized.Contains("character stat")) return true;
            string padded = " " + normalized + " ";
            string[] statWords = new string[] { " name ", " job ", " level ", " hp ", " mp ",
                " exp ", " fame ", " str ", " dex ", " int ", " luk ", " accuracy ", " evasion " };
            int anchors = 0;
            foreach (string word in statWords) if (padded.Contains(word)) anchors++;
            return anchors >= 5;
        }
    }

    internal static class SceneClassifier
    {
        internal static readonly SceneKind[] AtomicKinds = new SceneKind[] {
            SceneKind.Character, SceneKind.Skill, SceneKind.Quest, SceneKind.Item,
            SceneKind.Dialogue, SceneKind.Shop, SceneKind.Inventory
        };

        internal static string Name(SceneKind kind)
        {
            if (kind == SceneKind.Character) return "人物";
            if (kind == SceneKind.Skill) return "技能";
            if (kind == SceneKind.Quest) return "任务";
            if (kind == SceneKind.Item) return "装备物品";
            if (kind == SceneKind.Dialogue) return "NPC对话";
            if (kind == SceneKind.Shop) return "商店";
            if (kind == SceneKind.Inventory) return "背包";
            return "普通";
        }

        internal static SceneEvidence Classify(string text, TranslationStore translations,
            bool includeDialogueAnchors)
        {
            string normalized = TranslationStore.Normalize(text);
            string padded = " " + normalized + " ";
            SceneEvidence evidence = new SceneEvidence();
            bool playerChat = LooksLikePlayerChat(text);

            int characterAnchors = CountTokens(padded, new string[] {
                " character stat ", " character stats ", " character info ",
                " ability point ", " weapon def ", " magic def ", " accuracy ",
                " avoidability ", " crit rate ", " crit damage ", " str ", " dex ",
                " int ", " luk ", " hp ", " mp ", " fame "
            });
            evidence.StrongCharacter = normalized.Contains("character stat") ||
                normalized.Contains("character info") || characterAnchors >= 5;
            // HP/MP occur permanently in the bottom HUD and can also be duplicated by a
            // captured stream layout. They are not a character panel without a real header
            // or several independent character fields.
            if (evidence.StrongCharacter || characterAnchors >= 4)
                evidence.Kinds |= SceneKind.Character;

            evidence.SkillHeader = normalized.Contains("skill inventory") ||
                normalized.EndsWith(" skills", StringComparison.Ordinal) ||
                normalized.EndsWith(" techniques", StringComparison.Ordinal);
            bool knownSkillDetail = false;
            bool skillName = !playerChat && translations != null &&
                translations.ClassifySkillText(text, out knownSkillDetail);
            evidence.SkillDetail = normalized.Contains("master level") ||
                normalized.Contains("required skill") ||
                normalized.Contains("current level") || normalized.Contains("next level") ||
                knownSkillDetail;
            if (evidence.SkillHeader || evidence.SkillDetail || skillName)
                evidence.Kinds |= SceneKind.Skill;

            evidence.TaskId = playerChat || translations == null ? "" : translations.DetectTaskId(text);
            evidence.QuestHeader = normalized == "quest" || normalized == "quest log" ||
                normalized.Contains("quest helper") ||
                (normalized.Contains("quest") && (normalized.Contains("available") ||
                normalized.Contains("in progress") || normalized.Contains("completed") ||
                normalized.Contains("forfeit")));
            if (evidence.QuestHeader || evidence.TaskId.Length > 0)
                evidence.Kinds |= SceneKind.Quest;

            evidence.ItemHeader = normalized.Contains("item list") ||
                normalized.Contains("item inventory") || normalized.Contains("equipment inventory") ||
                normalized == "item" || normalized == "list";
            evidence.ItemDetail = (!playerChat && translations != null &&
                translations.LooksLikeItemDetailText(text)) ||
                LooksLikeEquipmentStructure(normalized);
            if (evidence.ItemHeader || evidence.ItemDetail)
                evidence.Kinds |= SceneKind.Item;
            if (normalized.Contains("item inventory") || normalized.Contains("equipment inventory"))
                evidence.Kinds |= SceneKind.Inventory;

            if (normalized.Contains("leave store") || normalized.Contains("buy item") ||
                normalized.Contains("seller info"))
                evidence.Kinds |= SceneKind.Shop | SceneKind.Item;

            evidence.DialogueAnchor = includeDialogueAnchors && IsDialogueAnchor(normalized);
            evidence.DialogueText = includeDialogueAnchors && !playerChat && LooksLikeKnownDialogue(text,
                normalized, translations);
            if (evidence.DialogueAnchor || evidence.DialogueText)
                evidence.Kinds |= SceneKind.Dialogue;
            return evidence;
        }

        internal static int EvidenceScore(SceneEvidence evidence, SceneKind kind)
        {
            if (kind == SceneKind.Character) return evidence.StrongCharacter ? 150 : 65;
            if (kind == SceneKind.Skill) return evidence.SkillDetail ? 150 :
                (evidence.SkillHeader ? 130 : 82);
            if (kind == SceneKind.Quest) return evidence.QuestHeader ? 128 :
                (evidence.TaskId.Length > 0 ? 112 : 70);
            if (kind == SceneKind.Item) return evidence.ItemDetail ? 145 :
                (evidence.ItemHeader ? 125 : 75);
            if (kind == SceneKind.Dialogue) return evidence.DialogueAnchor ? 120 :
                (evidence.DialogueText ? 116 : 65);
            if (kind == SceneKind.Shop) return 132;
            if (kind == SceneKind.Inventory) return 110;
            return 0;
        }

        internal static SceneSnapshot Analyze(OcrResult result, TranslationStore translations,
            bool includeDialogueAnchors)
        {
            SceneSnapshot snapshot = new SceneSnapshot();
            if (result == null) return snapshot;
            foreach (OcrLine line in result.Lines)
            {
                SceneEvidence evidence = Classify(line.Text, translations, includeDialogueAnchors);
                foreach (SceneKind kind in AtomicKinds)
                    if ((evidence.Kinds & kind) != 0)
                        snapshot.Add(kind, EvidenceScore(evidence, kind));
            }
            // Windows OCR sometimes splits one NPC sentence into several OcrLine objects even
            // though OcrResult.Text reconstructs it correctly. Use the combined text only to
            // recover a dictionary-backed dialogue signal; do not promote item/character words
            // from unrelated HUD regions into a panel scene.
            if (includeDialogueAnchors && !snapshot.IsStrong(SceneKind.Dialogue) &&
                !String.IsNullOrWhiteSpace(result.Text))
            {
                SceneEvidence combined = Classify(result.Text, translations, true);
                if (combined.DialogueAnchor || combined.DialogueText)
                    snapshot.Add(SceneKind.Dialogue, EvidenceScore(combined, SceneKind.Dialogue));
            }
            return snapshot;
        }

        internal static bool IsDialogueAnchor(string normalized)
        {
            return normalized == "next" || normalized == "back" || normalized == "accept" ||
                normalized == "decline" || normalized == "yes" || normalized == "no" ||
                normalized == "end chat" || normalized.Contains("end conversation");
        }

        internal static bool LooksLikePlayerChat(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            string raw = text.Trim();
            string lower = raw.ToLowerInvariant();
            string[] tradeMarkers = new string[] { "s>", "b>", "t>", "w>" };
            foreach (string marker in tradeMarkers)
            {
                int markerIndex = lower.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex >= 1 && markerIndex <= 36) return true;
            }
            int colon = raw.IndexOf(':');
            if (colon < 1 || colon > 32 || colon >= raw.Length - 1) return false;

            string prefix = TranslationStore.Normalize(raw.Substring(0, colon));
            if (prefix.Length < 1 || prefix.Length > 26) return false;
            string paddedPrefix = " " + prefix + " ";
            string[] structuredPrefixes = new string[] {
                " type ", " category ", " req ", " required ", " level ", " name ",
                " job ", " hp ", " mp ", " exp ", " fame ", " str ", " dex ",
                " int ", " luk ", " weapon attack ", " magic attack ",
                " weapon def ", " magic def ", " attack speed ", " accuracy ",
                " avoidability ", " current level ", " next level ", " master level "
            };
            foreach (string structured in structuredPrefixes)
                if (paddedPrefix == structured || paddedPrefix.StartsWith(structured,
                    StringComparison.Ordinal)) return false;

            int words = prefix.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries).Length;
            if (words > 5) return false;
            string body = raw.Substring(colon + 1).Trim();
            return body.Length >= 2;
        }

        private static bool LooksLikeKnownDialogue(string text, string normalized,
            TranslationStore translations)
        {
            if (translations == null || normalized.Length < 18 || normalized.Length > 800)
                return false;
            string padded = " " + normalized + " ";
            bool conversational = padded.Contains(" i ") || padded.Contains(" you ") ||
                padded.Contains(" your ") || padded.Contains(" we ") || padded.Contains(" my ") ||
                padded.Contains(" me ") || padded.Contains(" dont ") || padded.Contains(" cant ") ||
                padded.Contains(" ready ");
            if (!conversational) return false;
            foreach (MatchResult match in translations.FindInterfaceTextMatches(text))
            {
                int matched = match.Entry == null ? 0 : match.Entry.Normalized.Length;
                if (matched < 18) continue;
                // Windows OCR can flatten the dialogue sentence together with the minimap,
                // announcement and HUD into one long line. An exact long dictionary phrase is
                // still a valid dialogue anchor even when it covers less than 60% of that line.
                bool exactLongPhrase = matched >= 22 && normalized.IndexOf(
                    match.Entry.Normalized, StringComparison.Ordinal) >= 0;
                if (exactLongPhrase || matched * 10 >= normalized.Length * 6) return true;
            }
            return false;
        }

        private static bool LooksLikeEquipmentStructure(string normalized)
        {
            return normalized.Contains("req lev") || normalized.Contains("required level") ||
                normalized.Contains("remaining enhancements") ||
                normalized.Contains("number of upgrades") ||
                (normalized.Contains("weapon def") && normalized.Contains("magic def")) ||
                (normalized.Contains("category") && (normalized.Contains("weapon") ||
                normalized.Contains("accessory") || normalized.Contains("cape") ||
                normalized.Contains("shoes") || normalized.Contains("gloves")));
        }

        private static int CountTokens(string padded, string[] tokens)
        {
            int count = 0;
            foreach (string token in tokens) if (padded.Contains(token)) count++;
            return count;
        }
    }

    internal sealed class VisualColorBand
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
        public float CenterX { get { return (Left + Right) / 2.0f; } }
    }

    internal sealed class CharacterStatVisualLayout
    {
        public readonly List<VisualColorBand> UpperRows = new List<VisualColorBand>();
        public float Left;
        public float Top;
        public float Scale;
        public Rectangle Crop;
        public float MeasuredRightLeft = Single.NaN;
        public float MeasuredRightTop = Single.NaN;
        public float RightLeft { get { return Single.IsNaN(MeasuredRightLeft) ? Left + 360.0f * Scale : MeasuredRightLeft; } }
        public float RightTop { get { return Single.IsNaN(MeasuredRightTop) ? Top + 112.0f * Scale : MeasuredRightTop; } }
    }

    internal static class ClassicSceneVision
    {
        internal static CharacterStatVisualLayout FindCharacterStats(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 560 || bitmap.Height < 420) return null;
            BitmapData data = null;
            try
            {
                Rectangle area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                const int step = 3;
                List<VisualColorBand> magenta = FindBands(bitmap, data, pixels, step, true);
                List<VisualColorBand> blue = FindBands(bitmap, data, pixels, step, false);
                List<VisualColorBand> magentaRun = FindBestRun(magenta, 5);
                if (magentaRun == null) return null;

                float rowGap = TypicalGap(magentaRun);
                float scale = Math.Max(0.48f, Math.Min(1.75f, rowGap / 34.0f));
                float left = AverageLeft(magentaRun);
                float top = magentaRun[0].Top - 44.0f * scale;
                List<VisualColorBand> blueRun = FindMatchingBlueRun(blue, magentaRun, left, rowGap);

                // The old classic layout used a 360-unit gap. Other legitimate classic clients
                // use a narrower stat panel. Measure the blue column when it is visible, while
                // retaining the established formula as the fallback for noisy captures.
                float rightLeft = Single.NaN, rightTop = Single.NaN;
                if (blueRun != null)
                {
                    rightLeft = AverageLeft(blueRun);
                    rightTop = blueRun[0].Top;
                }
                else if (!HasExpectedBlueSurface(bitmap, data, pixels, left, top, scale))
                    return null;

                float predictedRight = Single.IsNaN(rightLeft) ? left + 360.0f * scale : rightLeft;
                int cropLeft = (int)Math.Max(0, left - 22.0f * scale);
                int cropTop = (int)Math.Max(0, top - 18.0f * scale);
                int cropRight = (int)Math.Min(bitmap.Width,
                    Math.Max(left + 170.0f * scale, predictedRight + 185.0f * scale));
                int cropBottom = (int)Math.Min(bitmap.Height,
                    Math.Max(magentaRun[magentaRun.Count - 1].Bottom + 140.0f * scale,
                        (Single.IsNaN(rightTop) ? top + 112.0f * scale : rightTop) + 350.0f * scale));
                Rectangle crop = Rectangle.FromLTRB(cropLeft, cropTop, cropRight, cropBottom);
                if (crop.Width < 240 || crop.Height < 210) return null;

                CharacterStatVisualLayout layout = new CharacterStatVisualLayout();
                layout.Scale = scale;
                layout.Left = left;
                layout.Top = top;
                layout.Crop = crop;
                layout.MeasuredRightLeft = rightLeft;
                layout.MeasuredRightTop = rightTop;
                foreach (VisualColorBand band in magentaRun) layout.UpperRows.Add(band);
                return layout;
            }
            catch { return null; }
            finally { if (data != null) bitmap.UnlockBits(data); }
        }

        private static List<VisualColorBand> FindBands(Bitmap bitmap, BitmapData data,
            byte[] pixels, int step, bool magenta)
        {
            int stride = Math.Abs(data.Stride);
            List<VisualColorBand> bands = new List<VisualColorBand>();
            List<VisualColorBand> active = new List<VisualColorBand>();
            for (int y = 0; y < bitmap.Height; y += step)
            {
                int row = data.Stride >= 0 ? y * stride : (bitmap.Height - 1 - y) * stride;
                List<VisualColorBand> segments = new List<VisualColorBand>();
                int count = 0, minimum = -1, maximum = -1, previous = -1000;
                for (int x = 0; x < bitmap.Width; x += step)
                {
                    int offset = row + x * 4;
                    int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                    bool match = magenta
                        ? r >= 150 && r <= 245 && g >= 34 && g <= 142 && b >= 65 && b <= 184 &&
                          r >= g + 42 && b >= g + 12
                        : b >= 110 && b <= 220 && g >= 62 && g <= 180 && r >= 32 && r <= 155 &&
                          b >= r + 20 && b >= g + 8;
                    if (!match) continue;
                    if (minimum >= 0 && x - previous > 18)
                    {
                        AddRowSegment(segments, minimum, maximum, y, count);
                        count = 0; minimum = -1; maximum = -1;
                    }
                    if (minimum < 0) minimum = x;
                    maximum = x; previous = x; count++;
                }
                AddRowSegment(segments, minimum, maximum, y, count);

                bool[] matched = new bool[active.Count];
                foreach (VisualColorBand segment in segments)
                {
                    int best = -1;
                    float bestDistance = Single.MaxValue;
                    for (int i = 0; i < active.Count; i++)
                    {
                        // Keep adjacent scan rows in one flat UI band, but never bridge an
                        // empty row: the narrow white separator between two stat rows is the
                        // only reliable boundary at low resolution.
                        if (matched[i] || y - active[i].Bottom > step) continue;
                        int overlap = Math.Min(active[i].Right, segment.Right) -
                            Math.Max(active[i].Left, segment.Left);
                        int smaller = Math.Min(active[i].Right - active[i].Left,
                            segment.Right - segment.Left);
                        float distance = Math.Abs(active[i].CenterX - segment.CenterX);
                        if (distance > 42 || (overlap < smaller * 0.35f && distance > 24)) continue;
                        if (distance < bestDistance) { bestDistance = distance; best = i; }
                    }
                    if (best < 0)
                    {
                        active.Add(segment);
                        Array.Resize(ref matched, active.Count);
                        matched[active.Count - 1] = true;
                    }
                    else
                    {
                        VisualColorBand target = active[best];
                        target.Bottom = y;
                        target.Left = Math.Min(target.Left, segment.Left);
                        target.Right = Math.Max(target.Right, segment.Right);
                        matched[best] = true;
                    }
                }

                for (int i = active.Count - 1; i >= 0; i--)
                {
                    if (matched[i] || y - active[i].Bottom <= step) continue;
                    bands.Add(active[i]);
                    active.RemoveAt(i);
                }
            }
            bands.AddRange(active);
            bands.RemoveAll(delegate(VisualColorBand band) {
                int width = band.Right - band.Left, height = band.Bottom - band.Top;
                return width < 38 || width > 220 || height > 72;
            });
            bands.Sort(delegate(VisualColorBand a, VisualColorBand b) {
                return a.Top.CompareTo(b.Top);
            });
            return bands;
        }

        private static void AddRowSegment(List<VisualColorBand> segments,
            int minimum, int maximum, int y, int count)
        {
            int width = maximum >= minimum ? maximum - minimum : 0;
            if (minimum < 0 || count < 7 || width < 38 || width > 220) return;
            segments.Add(new VisualColorBand { Left = minimum, Right = maximum,
                Top = y, Bottom = y });
        }

        private static List<VisualColorBand> FindBestRun(List<VisualColorBand> bands, int minimum)
        {
            List<List<VisualColorBand>> runs = BuildRuns(bands);
            List<VisualColorBand> best = null;
            double bestScore = 0;
            foreach (List<VisualColorBand> cluster in runs)
            {
                foreach (List<VisualColorBand> run in SplitVerticalSegments(cluster))
                {
                    if (run.Count < minimum) continue;
                    float gap = TypicalGap(run);
                    if (gap < 16 || gap > 58) continue;
                    double score = run.Count * 1000 - Math.Abs(34.0f - gap) * 8;
                    if (score > bestScore) { bestScore = score; best = run; }
                }
            }
            return best;
        }

        private static List<VisualColorBand> FindMatchingBlueRun(List<VisualColorBand> bands,
            List<VisualColorBand> magenta, float left, float gap)
        {
            List<List<VisualColorBand>> runs = BuildRuns(bands);
            List<VisualColorBand> best = null;
            double bestScore = Double.MinValue;
            foreach (List<VisualColorBand> cluster in runs)
            {
                foreach (List<VisualColorBand> run in SplitVerticalSegments(cluster))
                {
                    if (run.Count < 5) continue;
                    float runLeft = AverageLeft(run), runGap = TypicalGap(run);
                    float horizontal = runLeft - left;
                    if (horizontal < 110 || horizontal > 520) continue;
                    if (runGap < 16 || runGap > 58 ||
                        Math.Abs(runGap - gap) > Math.Max(9, gap * 0.36f)) continue;
                    float vertical = Math.Abs(run[0].Top - (magenta[0].Top + gap * 3.0f));
                    if (vertical > 170) continue;
                    double score = run.Count * 1000 - Math.Abs(runGap - gap) * 25 - vertical;
                    if (score > bestScore) { bestScore = score; best = run; }
                }
            }
            return best;
        }

        private static List<List<VisualColorBand>> SplitVerticalSegments(List<VisualColorBand> cluster)
        {
            List<List<VisualColorBand>> result = new List<List<VisualColorBand>>();
            List<VisualColorBand> current = null;
            VisualColorBand previous = null;
            foreach (VisualColorBand band in cluster)
            {
                if (previous != null)
                {
                    int gap = band.Top - previous.Top;
                    // A short gap is normally another fragment of the same colored row.
                    if (gap < 14) continue;
                    if (gap > 72) current = null;
                }
                if (current == null) { current = new List<VisualColorBand>(); result.Add(current); }
                current.Add(band);
                previous = band;
            }
            return result;
        }

        private static List<List<VisualColorBand>> BuildRuns(List<VisualColorBand> bands)
        {
            List<List<VisualColorBand>> runs = new List<List<VisualColorBand>>();
            foreach (VisualColorBand band in bands)
            {
                List<VisualColorBand> target = null;
                float bestDistance = Single.MaxValue;
                foreach (List<VisualColorBand> run in runs)
                {
                    float center = 0;
                    foreach (VisualColorBand existing in run) center += existing.CenterX;
                    center /= run.Count;
                    float distance = Math.Abs(band.CenterX - center);
                    if (distance > 38 || distance >= bestDistance) continue;
                    bestDistance = distance; target = run;
                }
                if (target == null) { target = new List<VisualColorBand>(); runs.Add(target); }
                target.Add(band);
            }
            foreach (List<VisualColorBand> run in runs)
                run.Sort(delegate(VisualColorBand a, VisualColorBand b) {
                    return a.Top.CompareTo(b.Top);
                });
            return runs;
        }

        private static float TypicalGap(List<VisualColorBand> run)
        {
            List<int> gaps = new List<int>();
            for (int i = 1; i < run.Count; i++)
            {
                int gap = run[i].Top - run[i - 1].Top;
                if (gap >= 16 && gap <= 58) gaps.Add(gap);
            }
            if (gaps.Count == 0) return 0;
            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private static float AverageLeft(List<VisualColorBand> run)
        {
            float result = 0;
            foreach (VisualColorBand band in run) result += band.Left;
            return result / Math.Max(1, run.Count);
        }

        private static bool HasExpectedBlueSurface(Bitmap bitmap, BitmapData data, byte[] pixels,
            float left, float top, float scale)
        {
            int stride = Math.Abs(data.Stride), samples = 0;
            float rightLeft = left + 360.0f * scale;
            float rightTop = top + 112.0f * scale;
            for (int y = Math.Max(0, (int)rightTop); y < Math.Min(bitmap.Height,
                (int)(rightTop + 10 * 34.0f * scale)); y += 5)
            {
                int row = data.Stride >= 0 ? y * stride : (bitmap.Height - 1 - y) * stride;
                for (int x = Math.Max(0, (int)rightLeft); x < Math.Min(bitmap.Width,
                    (int)(rightLeft + 112.0f * scale)); x += 5)
                {
                    int offset = row + x * 4;
                    int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                    if (b >= 125 && b <= 205 && g >= 82 && g <= 165 && r >= 48 && r <= 135 &&
                        b >= r + 28) samples++;
                }
            }
            return samples >= 28;
        }
    }
}
