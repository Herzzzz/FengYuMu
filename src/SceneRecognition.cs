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
        Inventory = 64,
        Interface = 128
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
        public bool MainPanelHeader;
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

    internal static class SettingsPanelPolicy
    {
        internal static bool IsSettingsWindow(string text)
        {
            return IsOptionsWindow(text) || IsKeyBindingsWindow(text);
        }

        internal static bool IsOptionsWindow(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            string padded = " " + normalized + " ";
            bool settingsCaption = normalized.Contains("game settings") || normalized.Contains("system settings") ||
                normalized.Contains("sound settings") || normalized.Contains("graphics settings");
            if (!padded.Contains(" options ") && !settingsCaption) return false;
            string[] tabs = new string[] {
                " graphics ", " sound ", " game ", " social ", " controller "
            };
            foreach (string tab in tabs)
                if (padded.Contains(tab)) return true;

            // A tight focused crop can lose the tab row while retaining the OPTIONS title.
            // Require two page-specific controls in that case so an isolated word such as
            // "Language" cannot turn an unrelated window into a settings panel.
            string[] controls = new string[] {
                " resolution ", " vertical sync ", " ui fade ", " master volume ",
                " hp warning ", " mp warning ", " enemy info ", " screenshot ",
                " language ", " whispers ", " chat font size ",
                " enable controller support ", " stick sensitivity ", " d pad slots "
            };
            int controlCount = 0;
            foreach (string control in controls)
                if (padded.Contains(control)) controlCount++;
            return controlCount >= 2;
        }

        internal static bool IsKeyBindingsWindow(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            string padded = " " + normalized + " ";
            // The classic pixel font frequently turns the B in BINDINGS into EI during
            // the full-window pass. The title plus either PRESET or QUICK SLOTS is still a
            // much stronger signature than an isolated skill name on a key cap.
            bool title = normalized.Contains("key bindings") ||
                normalized.Contains("key einoings") ||
                normalized.Contains("key bind");
            bool controls = padded.Contains(" preset ") ||
                normalized.Contains("quick slot") || normalized.Contains("quick sl keys") ||
                normalized.Contains("keyboard mouse") || padded.Contains(" controller ");
            return title && controls;
        }
    }

    internal static class SceneClassifier
    {
        internal static readonly SceneKind[] AtomicKinds = new SceneKind[] {
            SceneKind.Character, SceneKind.Skill, SceneKind.Quest, SceneKind.Item,
            SceneKind.Dialogue, SceneKind.Shop, SceneKind.Inventory, SceneKind.Interface
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
            if (kind == SceneKind.Interface) return "界面窗口";
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
                LooksLikeEquipmentStructure(text);
            if (evidence.ItemHeader || evidence.ItemDetail)
                evidence.Kinds |= SceneKind.Item;
            if (normalized.Contains("item inventory") || normalized.Contains("equipment inventory"))
                evidence.Kinds |= SceneKind.Inventory;

            if (normalized.Contains("leave store") || normalized.Contains("buy item") ||
                normalized.Contains("seller info"))
                evidence.Kinds |= SceneKind.Shop | SceneKind.Item;

            // These Classic World windows are neither dialogue nor item details, but they are
            // still foreground windows. Treating them as the fourth-tier background interface
            // made the task helper or HUD win the focused OCR pass. The four-tier planner keeps
            // its ordering; this flag merely puts a real open window into tier two.
            evidence.MainPanelHeader = normalized == "world map" ||
                normalized.Contains("crafting journal") ||
                normalized.Contains("maple user list") || normalized == "buddy list" ||
                normalized == "options" || normalized == "key bindings" ||
                normalized == "key einoings" ||
                SettingsPanelPolicy.IsSettingsWindow(text);
            if (evidence.MainPanelHeader) evidence.Kinds |= SceneKind.Interface;

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
            if (kind == SceneKind.Interface) return evidence.MainPanelHeader ? 126 : 80;
            return 0;
        }

        internal static SceneSnapshot Analyze(OcrResult result, TranslationStore translations,
            bool includeDialogueAnchors)
        {
            SceneSnapshot snapshot = new SceneSnapshot();
            if (result == null) return snapshot;
            bool keyBindingsWindow = SettingsPanelPolicy.IsKeyBindingsWindow(result.Text);
            foreach (OcrLine line in result.Lines)
            {
                SceneEvidence evidence = Classify(line.Text, translations, includeDialogueAnchors);
                // A key-cap can contain a real skill name (for example Heal), but the open
                // KEY BINDINGS window is not a skill tooltip. Once the whole-frame signature
                // is present, promote the settings window and ignore unrelated scene matches
                // from the game world behind it.
                if (keyBindingsWindow)
                {
                    if (evidence.MainPanelHeader) snapshot.Add(SceneKind.Interface, 126);
                    continue;
                }
                foreach (SceneKind kind in AtomicKinds)
                    if ((evidence.Kinds & kind) != 0)
                        snapshot.Add(kind, EvidenceScore(evidence, kind));
            }
            if (keyBindingsWindow && !snapshot.IsStrong(SceneKind.Interface))
                snapshot.Add(SceneKind.Interface, 126);
            // Windows OCR sometimes splits one NPC sentence or quest body into several OcrLine
            // objects even though OcrResult.Text reconstructs it correctly. Use the combined
            // text only for dictionary-backed signals; do not promote generic item/character
            // words from unrelated HUD regions into a panel scene.
            if (!snapshot.IsStrong(SceneKind.Quest) &&
                translations != null && !String.IsNullOrWhiteSpace(result.Text))
            {
                // Do not route the whole screen through LooksLikePlayerChat: an unrelated
                // quest title such as "Maple World's Unique Fun: Omok" contains a colon and
                // can make the merged screen text look like one giant chat line. A unique
                // dictionary-backed task body is sufficient evidence by itself.
                string combinedTaskId = translations.DetectTaskId(result.Text);
                if (combinedTaskId.Length > 0) snapshot.Add(SceneKind.Quest, 150);
            }
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
                " avoidability ", " usable until ", " current level ",
                " next level ", " master level "
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
            if (translations == null || normalized.Length < 18 || normalized.Length > 6000)
                return false;
            string padded = " " + normalized + " ";
            // Normalization deliberately preserves apostrophes. Recognize both OCR-flattened
            // contractions (dont/cant) and their correctly punctuated forms so a known NPC line
            // is not discarded merely because one OCR pass recovered the apostrophe. This still
            // requires a long dictionary match below, so ordinary player chat is not promoted.
            bool conversational = CountTokens(padded, new string[] {
                " i ", " i'm ", " i'll ", " i've ", " i'd ",
                " you ", " you're ", " you'll ", " you've ", " you'd ", " your ",
                " we ", " we're ", " we'll ", " we've ", " we'd ",
                " my ", " me ", " let's ", " ready ",
                " dont ", " don't ", " cant ", " can't ", " wont ", " won't ",
                " couldnt ", " couldn't ", " wouldnt ", " wouldn't ",
                " shouldnt ", " shouldn't ", " isnt ", " isn't ",
                " arent ", " aren't "
            }) > 0;
            if (!conversational) return false;
            List<MatchResult> known = translations.FindDialogueTextMatches(text);
            if (known.Count == 0) known = translations.FindInterfaceTextMatches(text);
            foreach (MatchResult match in known)
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

        private static bool LooksLikeEquipmentStructure(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return EquipmentFieldGrammar.LooksLikeEquipmentStructure(text) ||
                normalized.Contains("req lev") || normalized.Contains("required level") ||
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

    internal static class PanelContextPolicy
    {
        internal static bool UseEquipmentSemantics(bool localEquipmentPanel,
            bool explicitEquipmentStructure)
        {
            // A visible inventory or equipment tooltip is scene evidence, not permission to
            // parse every OCR line on the screen as equipment. Quest journals, chat and other
            // foreground windows may be open at the same time. Only the panel that actually
            // owns equipment structure (or a self-identifying REQ/DEF/etc. row) gets equipment
            // semantics, otherwise ordinary words such as Warrior/Thief can become a bogus
            // "allowed jobs" label in a neighbouring panel.
            return localEquipmentPanel || explicitEquipmentStructure;
        }

        internal static string ResolveTaskId(string focusedDetailText,
            IList<string> localLines, TranslationStore translations,
            bool focusedPass, bool mainQuestShell)
        {
            // The full-screen pass discovers panels only. It must never lend a task ID to
            // another region; focused crops establish their own semantic context afterwards.
            if (!focusedPass || translations == null) return "";

            if (mainQuestShell)
            {
                // A quest journal contains many titles in its left list. Only the selected
                // detail pane may choose the active task. Prefer its exact title. When the
                // game truncates that title, a sufficiently long right-pane description can
                // still identify one task, but conflicting evidence must fail closed.
                string titleId = String.IsNullOrWhiteSpace(focusedDetailText)
                    ? "" : translations.ResolveUniqueTaskTitleId(focusedDetailText);
                if (titleId.Length > 0) return titleId;

                HashSet<string> detailIds = new HashSet<string>(StringComparer.Ordinal);
                StringBuilder detailText = new StringBuilder();
                if (localLines != null)
                {
                    foreach (string line in localLines)
                    {
                        if (String.IsNullOrWhiteSpace(line) || line.Contains("...") ||
                            line.Contains("…") || TranslationStore.Normalize(line).Length < 24) continue;
                        if (detailText.Length > 0) detailText.AppendLine();
                        detailText.Append(line);
                        string lineId = translations.DetectTaskId(line);
                        if (lineId.Length > 0) detailIds.Add(lineId);
                    }
                }
                if (detailIds.Count > 1) return "";
                if (detailIds.Count == 1)
                    foreach (string id in detailIds) return id;
                return detailText.Length == 0 ? "" : translations.DetectTaskId(detailText.ToString());
            }

            HashSet<string> lineIds = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder combined = new StringBuilder();
            if (localLines != null)
            {
                foreach (string line in localLines)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    if (combined.Length > 0) combined.AppendLine();
                    combined.Append(line);
                    string lineId = translations.DetectTaskId(line);
                    if (lineId.Length > 0) lineIds.Add(lineId);
                }
            }

            // A side tracker commonly contains several quests. Choosing one of them as a
            // global context is always wrong, so short independent titles remain translatable
            // while task-specific prose is deliberately disabled for that crop.
            if (lineIds.Count > 1) return "";
            if (lineIds.Count == 1)
                foreach (string id in lineIds) return id;
            return combined.Length == 0 ? "" : translations.DetectTaskId(combined.ToString());
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

    internal enum VisualPanelSurfaceKind
    {
        Light = 1,
        DarkBlue = 2
    }

    internal sealed class VisualPanelRegion
    {
        public Rectangle Bounds;
        // Keep the measured colour surface separate from the padded discovery crop.
        // Only the former may assign OCR words to a tooltip owner.
        public Rectangle SurfaceBounds;
        public VisualPanelSurfaceKind Kind;
        public int Score;
        public double FillRatio;
        public double RowStrength;
        public double ColumnStrength;
    }

    internal static class ClassicSceneVision
    {
        internal static List<VisualPanelRegion> FindForegroundPanels(Bitmap bitmap)
        {
            List<VisualPanelRegion> result = new List<VisualPanelRegion>();
            if (bitmap == null || bitmap.Width < 320 || bitmap.Height < 240) return result;
            BitmapData data = null;
            try
            {
                Rectangle area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                FindPanelComponents(bitmap, data, pixels, VisualPanelSurfaceKind.Light, result);
                FindPanelComponents(bitmap, data, pixels, VisualPanelSurfaceKind.DarkBlue, result);
                // The player context menu is a compact cyan-blue stack rather than a large
                // tooltip. Its eight rows are separated by dark one-pixel gutters, so the
                // ordinary connected-component detector deliberately rejects it. Detect the
                // repeated row geometry here and return one expanded dark-blue region; the
                // focused OCR planner can then read the menu without treating the busy game
                // background as a panel.
                FindPlayerContextMenuComponents(bitmap, data, pixels, result);
                // Blue title cards inside a much taller light quest/options window are chrome,
                // not standalone tooltips. Treating them as a first-priority detail wastes one
                // of the focused passes and can delay the actual body. A real dark tooltip may
                // overlap a light inventory window, but it is not almost completely contained in
                // a shallow strip at that window's top.
                for (int i = result.Count - 1; i >= 0; i--)
                {
                    VisualPanelRegion dark = result[i];
                    if (dark.Kind != VisualPanelSurfaceKind.DarkBlue) continue;
                    foreach (VisualPanelRegion light in result)
                    {
                        if (light.Kind != VisualPanelSurfaceKind.Light ||
                            dark.Bounds.Height * 5 >= light.Bounds.Height * 2) continue;
                        Rectangle overlap = Rectangle.Intersect(dark.Bounds, light.Bounds);
                        long darkArea = (long)dark.Bounds.Width * dark.Bounds.Height;
                        if (darkArea > 0 && (long)overlap.Width * overlap.Height * 10 >= darkArea * 8)
                        { result.RemoveAt(i); break; }
                    }
                }
                result.Sort(delegate(VisualPanelRegion left, VisualPanelRegion right) {
                    int score = right.Score.CompareTo(left.Score);
                    if (score != 0) return score;
                    long leftArea = (long)left.Bounds.Width * left.Bounds.Height;
                    long rightArea = (long)right.Bounds.Width * right.Bounds.Height;
                    return rightArea.CompareTo(leftArea);
                });
                return result;
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "panel_debug.txt"), ex.ToString()); } catch { }
                return new List<VisualPanelRegion>();
            }
            finally
            {
                if (data != null) bitmap.UnlockBits(data);
            }
        }

        private static void FindPanelComponents(Bitmap bitmap, BitmapData data, byte[] pixels,
            VisualPanelSurfaceKind kind, List<VisualPanelRegion> output)
        {
            const int step = 4;
            int columns = (bitmap.Width + step - 1) / step;
            int rows = (bitmap.Height + step - 1) / step;
            bool[] mask = new bool[columns * rows];
            int stride = Math.Abs(data.Stride);
            for (int y = 0; y < rows; y++)
            {
                int sampleY = Math.Min(bitmap.Height - 1, y * step + step / 2);
                int row = data.Stride >= 0 ? sampleY * stride :
                    (bitmap.Height - 1 - sampleY) * stride;
                for (int x = 0; x < columns; x++)
                {
                    int sampleX = Math.Min(bitmap.Width - 1, x * step + step / 2);
                    int offset = row + sampleX * 4;
                    int blue = pixels[offset], green = pixels[offset + 1],
                        red = pixels[offset + 2], alpha = pixels[offset + 3];
                    mask[y * columns + x] = kind == VisualPanelSurfaceKind.Light
                        ? IsLightPanelPixel(alpha, red, green, blue)
                        : IsDarkBluePanelPixel(alpha, red, green, blue);
                }
            }

            bool[] visited = new bool[mask.Length];
            int[] queue = new int[mask.Length];
            int[] dx = new int[] { -1, 1, 0, 0 };
            int[] dy = new int[] { 0, 0, -1, 1 };
            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || visited[start]) continue;
                int head = 0, tail = 0, count = 0;
                int minX = columns, minY = rows, maxX = 0, maxY = 0;
                visited[start] = true;
                queue[tail++] = start;
                while (head < tail)
                {
                    int index = queue[head++];
                    count++;
                    int x = index % columns, y = index / columns;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int nextX = x + dx[direction], nextY = y + dy[direction];
                        if (nextX < 0 || nextY < 0 || nextX >= columns || nextY >= rows) continue;
                        int next = nextY * columns + nextX;
                        if (!mask[next] || visited[next]) continue;
                        visited[next] = true;
                        queue[tail++] = next;
                    }
                }
                int widthCells = maxX - minX + 1, heightCells = maxY - minY + 1;
                int width = widthCells * step, height = heightCells * step;
                int minimumCount = kind == VisualPanelSurfaceKind.Light ? 900 : 420;
                int minimumWidth = kind == VisualPanelSurfaceKind.Light ? 260 : 180;
                int minimumHeight = kind == VisualPanelSurfaceKind.Light ? 110 : 80;
                if (count < minimumCount || width < minimumWidth || height < minimumHeight)
                    continue;

                int[] rowCounts = new int[heightCells];
                int[] columnCounts = new int[widthCells];
                for (int i = 0; i < tail; i++)
                {
                    int index = queue[i];
                    int x = index % columns, y = index / columns;
                    rowCounts[y - minY]++;
                    columnCounts[x - minX]++;
                }
                int denseRows = 0, denseColumns = 0;
                double rowThreshold = kind == VisualPanelSurfaceKind.Light ? 0.42 : 0.30;
                double columnThreshold = kind == VisualPanelSurfaceKind.Light ? 0.32 : 0.24;
                foreach (int value in rowCounts)
                    if (value >= Math.Max(3, (int)Math.Ceiling(widthCells * rowThreshold))) denseRows++;
                foreach (int value in columnCounts)
                    if (value >= Math.Max(3, (int)Math.Ceiling(heightCells * columnThreshold))) denseColumns++;
                double fill = (double)count / Math.Max(1, widthCells * heightCells);
                double rowStrength = (double)denseRows / Math.Max(1, heightCells);
                double columnStrength = (double)denseColumns / Math.Max(1, widthCells);
                double minimumFill = kind == VisualPanelSurfaceKind.Light ? 0.34 : 0.16;
                double minimumRows = kind == VisualPanelSurfaceKind.Light ? 0.42 : 0.28;
                double minimumColumns = kind == VisualPanelSurfaceKind.Light ? 0.26 : 0.18;
                if (fill < minimumFill || rowStrength < minimumRows ||
                    columnStrength < minimumColumns) continue;

                Rectangle raw = Rectangle.FromLTRB(minX * step, minY * step,
                    Math.Min(bitmap.Width, (maxX + 1) * step),
                    Math.Min(bitmap.Height, (maxY + 1) * step));
                if (kind == VisualPanelSurfaceKind.Light && (raw.Left <= 12 || raw.Top <= 12 ||
                    raw.Right >= bitmap.Width - 12 || raw.Bottom >= bitmap.Height - 12)) continue;
                long rawArea = (long)raw.Width * raw.Height;
                long screenArea = (long)bitmap.Width * bitmap.Height;
                if (rawArea > screenArea * 58 / 100) continue;
                if (raw.Bottom > bitmap.Height * 92 / 100 && raw.Height < bitmap.Height * 35 / 100)
                    continue;
                double aspect = (double)raw.Width / Math.Max(1, raw.Height);
                if (aspect < 0.38 || aspect > 6.5) continue;

                Rectangle expanded = raw;
                expanded.Inflate(kind == VisualPanelSurfaceKind.Light ? 22 : 12,
                    kind == VisualPanelSurfaceKind.Light ? 42 : 16);
                expanded = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    expanded);
                int score = (kind == VisualPanelSurfaceKind.DarkBlue ? 172 : 132) +
                    (int)Math.Round(fill * 28.0 + rowStrength * 18.0 + columnStrength * 16.0);
                AddVisualPanelRegion(output, new VisualPanelRegion {
                    Bounds = expanded,
                    SurfaceBounds = raw,
                    Kind = kind,
                    Score = score,
                    FillRatio = fill,
                    RowStrength = rowStrength,
                    ColumnStrength = columnStrength
                });
            }
        }

        private static void FindPlayerContextMenuComponents(Bitmap bitmap, BitmapData data,
            byte[] pixels, List<VisualPanelRegion> output)
        {
            const int step = 2;
            int columns = (bitmap.Width + step - 1) / step;
            int rows = (bitmap.Height + step - 1) / step;
            int stride = Math.Abs(data.Stride);
            List<Rectangle> bands = new List<Rectangle>();
            for (int y = 0; y < rows; y++)
            {
                int sampleY = Math.Min(bitmap.Height - 1, y * step + step / 2);
                int row = data.Stride >= 0 ? sampleY * stride :
                    (bitmap.Height - 1 - sampleY) * stride;
                int start = -1, last = -1, active = 0;
                for (int x = 0; x <= columns; x++)
                {
                    bool match = false;
                    if (x < columns)
                    {
                        int sampleX = Math.Min(bitmap.Width - 1, x * step + step / 2);
                        int offset = row + sampleX * 4;
                        int blue = pixels[offset], green = pixels[offset + 1],
                            red = pixels[offset + 2], alpha = pixels[offset + 3];
                        match = IsPlayerMenuPixel(alpha, red, green, blue);
                    }
                    if (match)
                    {
                        if (start < 0) start = x;
                        last = x;
                        active++;
                        continue;
                    }
                    // White glyphs punch short holes through the cyan row. Close only those
                    // short holes; the old first/last-pixel approach joined unrelated blue
                    // effects from opposite sides of the entire game screen.
                    if (start < 0 || (x - last) <= 6) continue;
                    int sampledWidth = last - start + 1;
                    if (active >= 30 && sampledWidth >= 48 && sampledWidth <= 125)
                        bands.Add(Rectangle.FromLTRB(start * step, y * step,
                            Math.Min(bitmap.Width, (last + 1) * step),
                            Math.Min(bitmap.Height, (y + 1) * step)));
                    start = -1; last = -1; active = 0;
                }
            }
            if (bands.Count < 20) return;

            // Cluster aligned scan bands vertically. One component must survive many sampled
            // rows and the small gutters between all eight menu buttons; spell effects may
            // produce cyan streaks, but not this repeated compact column geometry.
            List<Rectangle> clusters = new List<Rectangle>();
            List<int> strengths = new List<int>();
            List<int> lastBottom = new List<int>();
            foreach (Rectangle band in bands)
            {
                int best = -1, bestOverlap = -1;
                for (int i = 0; i < clusters.Count; i++)
                {
                    if (band.Top - lastBottom[i] > 18) continue;
                    Rectangle cluster = clusters[i];
                    int overlap = Math.Min(cluster.Right, band.Right) -
                        Math.Max(cluster.Left, band.Left);
                    int smaller = Math.Min(cluster.Width, band.Width);
                    if (overlap <= 0 || overlap * 10 < Math.Max(1, smaller) * 6) continue;
                    int bandCenter = band.Left + band.Width / 2;
                    int clusterCenter = cluster.Left + cluster.Width / 2;
                    if (Math.Abs(bandCenter - clusterCenter) > 34) continue;
                    if (overlap > bestOverlap) { best = i; bestOverlap = overlap; }
                }
                if (best < 0)
                {
                    clusters.Add(band); strengths.Add(1); lastBottom.Add(band.Bottom);
                }
                else
                {
                    clusters[best] = Rectangle.Union(clusters[best], band);
                    strengths[best]++;
                    lastBottom[best] = Math.Max(lastBottom[best], band.Bottom);
                }
            }
            for (int i = 0; i < clusters.Count; i++)
            {
                Rectangle candidate = clusters[i];
                int count = strengths[i];
                if (count < 45 || candidate.Width < 96 || candidate.Width > 230 ||
                    candidate.Height < 145 || candidate.Height > 310) continue;
                candidate.Inflate(24, 20);
                candidate = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    candidate);
                AddVisualPanelRegion(output, new VisualPanelRegion {
                    Bounds = candidate,
                    Kind = VisualPanelSurfaceKind.DarkBlue,
                    Score = 218 + Math.Min(36, count / 2),
                    FillRatio = 0.34,
                    RowStrength = Math.Min(1.0, count / 80.0),
                    ColumnStrength = 0.55
                });
            }
        }

        private static bool IsPlayerMenuPixel(int alpha, int red, int green, int blue)
        {
            // Classic context-menu rows use a bright cyan fill, distinct from the dark navy
            // tooltip surface and from the white/grey quest windows.
            return alpha > 180 && red >= 18 && red <= 105 && green >= 112 && green <= 205 &&
                blue >= 156 && blue <= 242 && blue >= green + 22 && green >= red + 44;
        }

        private static void AddVisualPanelRegion(List<VisualPanelRegion> output,
            VisualPanelRegion candidate)
        {
            for (int i = 0; i < output.Count; i++)
            {
                VisualPanelRegion current = output[i];
                if (current.Kind != candidate.Kind) continue;
                Rectangle overlap = Rectangle.Intersect(current.Bounds, candidate.Bounds);
                long smaller = Math.Min((long)current.Bounds.Width * current.Bounds.Height,
                    (long)candidate.Bounds.Width * candidate.Bounds.Height);
                if (smaller <= 0 || (long)overlap.Width * overlap.Height * 10 < smaller * 7)
                    continue;
                if (candidate.Score > current.Score) output[i] = candidate;
                return;
            }
            output.Add(candidate);
        }

        private static bool IsLightPanelPixel(int alpha, int red, int green, int blue)
        {
            if (alpha < 180 || red < 174 || green < 174 || blue < 166) return false;
            int maximum = Math.Max(red, Math.Max(green, blue));
            int minimum = Math.Min(red, Math.Min(green, blue));
            return maximum - minimum <= 34;
        }

        private static bool IsDarkBluePanelPixel(int alpha, int red, int green, int blue)
        {
            return alpha > 180 && red >= 14 && red <= 138 && green >= 14 && green <= 156 &&
                blue >= 54 && blue <= 198 && blue >= red + 14 && blue >= green + 6;
        }

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
