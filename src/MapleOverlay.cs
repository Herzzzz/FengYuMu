using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

[assembly: AssemblyTitle("枫语幕")]
[assembly: AssemblyProduct("枫语幕")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace MapleOverlay
{
    internal static class Program
    {
        private const string InstanceMutexName = @"Local\FengYuMu.SingleInstance.v2";
        private const string ActivationEventName = @"Local\FengYuMu.Activate.v2";
        private static Mutex instanceMutex;
        private static bool ownsInstanceMutex;
        internal static readonly Icon AppIcon = LoadAppIcon();
        internal static EventWaitHandle ActivationEvent;
        internal static bool Benchmark;
        internal static string BenchmarkIconPath;
        internal static string BenchmarkImagePath;
        internal static string BenchmarkChatImagePath;
        internal static string BenchmarkText;
        internal static bool BenchmarkUi;
        internal static int BenchmarkBestIconDistance = 65;
        internal static string BenchmarkBestIcon = "";
        internal static string BenchmarkCurrentArea = "";
        internal static System.Drawing.Point BenchmarkCursor = System.Drawing.Point.Empty;
        internal static int BenchmarkRangeMode;
        internal static bool BenchmarkSceneProbe;
        internal static bool BenchmarkHotkeyToggle;
        internal static int BenchmarkContinuousCycles;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProgramWindowRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr handle, out ProgramWindowRect rectangle);

        private static Icon LoadAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private static bool AcquirePrimaryInstance()
        {
            try
            {
                bool createdNew;
                instanceMutex = new Mutex(true, InstanceMutexName, out createdNew);
                if (createdNew)
                {
                    ownsInstanceMutex = true;
                    ActivationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
                    return true;
                }

                // A second launch is the recovery entry for a lost tray icon.
                // The short retry also covers the tiny gap between the first process creating
                // its mutex and creating the activation event.
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        using (EventWaitHandle activation = EventWaitHandle.OpenExisting(ActivationEventName))
                        {
                            activation.Set();
                            break;
                        }
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        if (attempt < 3) Thread.Sleep(50);
                    }
                }
                instanceMutex.Dispose();
                instanceMutex = null;
                return false;
            }
            catch
            {
                // Named wait handles can be unavailable under unusual account policies.
                // In that case preserve the legacy startup path instead of blocking the app.
                if (instanceMutex != null) instanceMutex.Dispose();
                instanceMutex = null;
                return true;
            }
        }

        private static void ReleasePrimaryInstance()
        {
            if (ActivationEvent != null)
            {
                ActivationEvent.Dispose();
                ActivationEvent = null;
            }
            if (instanceMutex == null) return;
            if (ownsInstanceMutex)
            {
                try { instanceMutex.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            instanceMutex.Dispose();
            instanceMutex = null;
            ownsInstanceMutex = false;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            // .NET Framework 4.x on older Windows may otherwise negotiate TLS 1.0,
            // which GitHub and Hugging Face no longer accept.
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 4;
            if (args != null) foreach (string arg in args)
                if (arg.StartsWith("--apply-update=", StringComparison.OrdinalIgnoreCase))
                {
                    Environment.ExitCode = ApplicationUpdater.ApplyPreparedUpdate(
                        arg.Substring("--apply-update=".Length).Trim('"'));
                    return;
                }
            Benchmark = args != null && Array.IndexOf(args, "--benchmark") >= 0;
            BenchmarkUi = args != null && Array.IndexOf(args, "--benchmark-ui") >= 0;
            BenchmarkSceneProbe = args != null && Array.IndexOf(args, "--benchmark-scene-probe") >= 0;
            BenchmarkHotkeyToggle = args != null && Array.IndexOf(args, "--hotkey-toggle-test") >= 0;
            if (BenchmarkHotkeyToggle) Benchmark = true;
            if (args != null) foreach (string arg in args)
                if (arg.StartsWith("--benchmark-icon=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkIconPath = arg.Substring("--benchmark-icon=".Length);
                else if (arg.StartsWith("--benchmark-image=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkImagePath = arg.Substring("--benchmark-image=".Length);
                else if (arg.StartsWith("--chat-style-benchmark=", StringComparison.OrdinalIgnoreCase))
                {
                    Benchmark = true;
                    BenchmarkChatImagePath = arg.Substring("--chat-style-benchmark=".Length);
                }
                else if (arg.StartsWith("--benchmark-text=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkText = arg.Substring("--benchmark-text=".Length);
                else if (arg.StartsWith("--benchmark-cursor=", StringComparison.OrdinalIgnoreCase))
                {
                    string[] xy = arg.Substring("--benchmark-cursor=".Length).Split(',');
                    int x, y;
                    if (xy.Length == 2 && Int32.TryParse(xy[0], out x) && Int32.TryParse(xy[1], out y))
                        BenchmarkCursor = new System.Drawing.Point(x, y);
                }
                else if (arg.StartsWith("--benchmark-range=", StringComparison.OrdinalIgnoreCase))
                {
                    string mode = arg.Substring("--benchmark-range=".Length);
                    BenchmarkRangeMode = String.Equals(mode, "balanced", StringComparison.OrdinalIgnoreCase) ? 2 :
                        (String.Equals(mode, "minimum", StringComparison.OrdinalIgnoreCase) ? 3 : 1);
                }
                else if (arg.StartsWith("--benchmark-continuous-cycles=", StringComparison.OrdinalIgnoreCase))
                {
                    int cycles;
                    if (Int32.TryParse(arg.Substring("--benchmark-continuous-cycles=".Length),
                        out cycles)) BenchmarkContinuousCycles = Math.Max(0, Math.Min(30, cycles));
                }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && Array.IndexOf(args, "--main-ui-test") >= 0)
            {
                string uiErrorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "main_ui_test_error.txt");
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    if (File.Exists(uiErrorPath)) File.Delete(uiErrorPath);
                    using (MainPanelForm panel = new MainPanelForm(null))
                    {
                        panel.RefreshStatus();
                        panel.Show();
                        Application.DoEvents();
                        // PerMonitorV2 can resize the form when its handle is first shown. Allocate
                        // the QA bitmap afterwards so high-DPI screenshots are not clipped or scaled.
                        using (Bitmap bitmap = new Bitmap(panel.Width, panel.Height, PixelFormat.Format32bppArgb))
                        {
                            panel.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                            panel.Hide();
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                "main_ui_test.png"), ImageFormat.Png);
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(uiErrorPath, ex.ToString(), Encoding.UTF8);
                    Environment.ExitCode = 3;
                }
                return;
            }
            if (args != null && Array.IndexOf(args, "--online-ai-ui-test") >= 0)
            {
                string uiErrorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "online_ai_ui_test_error.txt");
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    if (File.Exists(uiErrorPath)) File.Delete(uiErrorPath);
                    using (OnlineAiForm panel = new OnlineAiForm())
                    {
                        panel.Show();
                        Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(panel.Width, panel.Height,
                            PixelFormat.Format32bppArgb))
                        {
                            panel.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty,
                                bitmap.Size));
                            panel.Hide();
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                "online_ai_ui_test.png"), ImageFormat.Png);
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(uiErrorPath, ex.ToString(), Encoding.UTF8);
                    Environment.ExitCode = 3;
                }
                return;
            }
            if (args != null && Array.IndexOf(args, "--translation-window-ui-test") >= 0)
            {
                string uiErrorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "translation_window_ui_test_error.txt");
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    if (File.Exists(uiErrorPath)) File.Delete(uiErrorPath);
                    using (AiTranslationWindowForm window = new AiTranslationWindowForm(null))
                    {
                        window.Size = new System.Drawing.Size(620, 300);
                        Rectangle work = Screen.FromControl(window).WorkingArea;
                        window.Location = new System.Drawing.Point(work.Right - window.Width - 24,
                            work.Top + Math.Max(24, (work.Height - window.Height) / 2));
                        window.AppendTranslation("Arthur：欢迎来到射手村。",
                            ChatVisualStylePolicy.FromSample("Arthur: hello", Color.FromArgb(95, 225, 125),
                                Color.Empty, false));
                        window.AppendTranslation("Joey：活动魔盒要交到哪里？",
                            ChatVisualStylePolicy.FromSample("Joey: where do I bring the magic box?",
                                Color.FromArgb(81, 120, 149), Color.FromArgb(141, 170, 179), true));
                        window.AppendTranslation("DunkChai：你手机里有骨头吗？",
                            ChatVisualStylePolicy.FromSample("DunkChai's Gift-filled Message:",
                                Color.FromArgb(156, 77, 113),
                                Color.FromArgb(205, 159, 173), true));
                        window.Show();
                        Application.DoEvents();
                        window.Refresh();
                        Thread.Sleep(180);
                        Application.DoEvents();
                        ProgramWindowRect physical;
                        if (!GetWindowRect(window.Handle, out physical))
                            throw new InvalidOperationException("无法读取AI浮窗实际屏幕边界");
                        int physicalWidth = Math.Max(1, physical.Right - physical.Left);
                        int physicalHeight = Math.Max(1, physical.Bottom - physical.Top);
                        using (Bitmap bitmap = new Bitmap(physicalWidth, physicalHeight,
                            PixelFormat.Format32bppArgb))
                        {
                            using (Graphics graphics = Graphics.FromImage(bitmap))
                                graphics.CopyFromScreen(physical.Left, physical.Top, 0, 0,
                                    bitmap.Size, CopyPixelOperation.SourceCopy);
                            window.Hide();
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                "translation_window_ui_test.png"), ImageFormat.Png);
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(uiErrorPath, ex.ToString(), Encoding.UTF8);
                    Environment.ExitCode = 3;
                }
                return;
            }
            if (!String.IsNullOrEmpty(BenchmarkChatImagePath))
            {
                try
                {
                    Application.Run(new OverlayForm());
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "chat_style_benchmark.txt"), "ERROR " + ex, Encoding.UTF8);
                    Environment.ExitCode = 4;
                }
                return;
            }
            if (args != null && Array.IndexOf(args, "--dictionary-ui-test") >= 0)
            {
                using (DictionaryOnlyForm editor = new DictionaryOnlyForm(null, AppDomain.CurrentDomain.BaseDirectory))
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dictionary_ui_test.txt"),
                        editor.RunCategorySelfTest(), Encoding.UTF8);
                return;
            }
            if (args != null && Array.IndexOf(args, "--hotkey-ui-test") >= 0)
            {
                using (HotkeyForm form = new HotkeyForm(null))
                {
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                        form.Hide();
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkey_ui_test.png"), ImageFormat.Png);
                    }
                }
                return;
            }
            if (args != null && Array.IndexOf(args, "--gamepad-self-test") >= 0)
            {
                Environment.ExitCode = GamepadShortcutLatch.RunSelfTest() ? 0 : 2;
                return;
            }
            if (!Benchmark && !BenchmarkUi && !AcquirePrimaryInstance()) return;
            try { Application.Run(new OverlayForm()); }
            catch (Exception ex)
            {
                MessageBox.Show("程序发生错误：\n\n" + ex.Message,
                    "枫语幕", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!Benchmark && !BenchmarkUi) ReleasePrimaryInstance();
            }
        }
    }

    internal sealed class TranslationEntry
    {
        public string English;
        public string Chinese;
        public string Category;
        public string Normalized;
        public ulong IconHash;
        public bool HasIcon;
        public string TaskId;
        public bool IsTaskName;
        public bool IsTaskText;
        public bool IsSkillText;
        public bool IsItemText;
        public bool IsInterfaceText;
        public HashSet<string> DetailWords;
    }

    internal sealed class TranslationStore
    {
        private readonly string sourcePath;
        private readonly Dictionary<char, List<TranslationEntry>> buckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> taskBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> skillTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> itemTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> interfaceTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> iconTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly List<TranslationEntry> taskNames = new List<TranslationEntry>();
        private readonly List<TranslationEntry> taskEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> iconEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> skillTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> itemTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> interfaceTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> uiEntries = new List<TranslationEntry>();
        private readonly Dictionary<string, byte> skillClassificationCache =
            new Dictionary<string, byte>(StringComparer.Ordinal);
        private readonly object skillClassificationLock = new object();
        private string cachedIconText = "";
        private readonly List<KeyValuePair<TranslationEntry, float>> cachedIconCandidates =
            new List<KeyValuePair<TranslationEntry, float>>();
        public int Count { get; private set; }
        public int IconCount { get { return iconEntries.Count; } }
        public int TaskTextCount { get; private set; }
        public int SkillTextCount { get; private set; }
        public int ItemTextCount { get; private set; }
        public int TaskCount { get { return taskNames.Count; } }

        public TranslationStore(string source)
        {
            sourcePath = source;
        }

        public void Load()
        {
            List<TranslationEntry> entries = ReadTsv(sourcePath);

            // The classic data contains legitimate same-English-name variants (hair colours,
            // job-specific skills, duplicate transport NPCs, and so on).  If their Chinese
            // translations differ, plain OCR cannot choose safely. Keep every variant in the
            // icon index, but require icon + text matching instead of accepting an arbitrary
            // text-only translation.
            Dictionary<string, HashSet<string>> translationsByEnglish =
                new Dictionary<string, HashSet<string>>();
            foreach (TranslationEntry entry in entries)
            {
                if (entry.IsTaskName || entry.IsTaskText || entry.IsSkillText || entry.IsItemText) continue;
                HashSet<string> values;
                if (!translationsByEnglish.TryGetValue(entry.Normalized, out values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    translationsByEnglish.Add(entry.Normalized, values);
                }
                values.Add(entry.Chinese);
            }

            buckets.Clear();
            taskBuckets.Clear();
            skillTextBuckets.Clear();
            itemTextBuckets.Clear();
            interfaceTextBuckets.Clear();
            iconTextBuckets.Clear();
            taskNames.Clear();
            taskEntries.Clear();
            iconEntries.Clear();
            skillTextEntries.Clear();
            itemTextEntries.Clear();
            interfaceTextEntries.Clear();
            uiEntries.Clear();
            TaskTextCount = 0;
            SkillTextCount = 0;
            ItemTextCount = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (entry.Normalized.Length == 0) continue;
                if (entry.Category.StartsWith("怀旧服-界面", StringComparison.Ordinal))
                    uiEntries.Add(entry);
                if (entry.IsTaskName || entry.IsTaskText)
                {
                    AddToBucket(taskBuckets, entry);
                    taskEntries.Add(entry);
                    if (entry.IsTaskName) taskNames.Add(entry);
                    if (entry.IsTaskText) TaskTextCount++;
                }
                else if (entry.IsSkillText)
                {
                    AddToBucket(skillTextBuckets, entry); skillTextEntries.Add(entry); SkillTextCount++;
                }
                else if (entry.IsItemText)
                {
                    AddToBucket(itemTextBuckets, entry); itemTextEntries.Add(entry); ItemTextCount++;
                }
                else if (entry.IsInterfaceText)
                {
                    AddToBucket(interfaceTextBuckets, entry); interfaceTextEntries.Add(entry);
                }
                else
                {
                    // Player-chat slang belongs to the dedicated AI chat pipeline. Keeping it out
                    // of the F8 screen overlay prevents short fragments such as "wth" from being
                    // painted over unrelated UI or notification text.
                    if (entry.Category.StartsWith("怀旧服-聊天", StringComparison.Ordinal)) continue;
                    HashSet<string> values;
                    bool ambiguous = translationsByEnglish.TryGetValue(entry.Normalized, out values) &&
                        values.Count > 1;
                    if (!ambiguous) AddToBucket(buckets, entry);
                    if (entry.HasIcon)
                    {
                        iconEntries.Add(entry);
                        AddToBucket(iconTextBuckets, entry);
                    }
                }
            }
            SortBuckets(buckets);
            SortBuckets(taskBuckets);
            SortBuckets(skillTextBuckets);
            SortBuckets(itemTextBuckets);
            SortBuckets(interfaceTextBuckets);
            SortBuckets(iconTextBuckets);
            cachedIconText = "";
            cachedIconCandidates.Clear();
            lock (skillClassificationLock) skillClassificationCache.Clear();
            Count = entries.Count;
        }

        private static void AddToBucket(Dictionary<char, List<TranslationEntry>> target, TranslationEntry entry)
        {
            char key = entry.Normalized[0];
            List<TranslationEntry> list;
            if (!target.TryGetValue(key, out list))
            {
                list = new List<TranslationEntry>();
                target.Add(key, list);
            }
            list.Add(entry);
        }

        private static void SortBuckets(Dictionary<char, List<TranslationEntry>> target)
        {
            foreach (List<TranslationEntry> list in target.Values)
                list.Sort(delegate(TranslationEntry a, TranslationEntry b) {
                    return b.Normalized.Length.CompareTo(a.Normalized.Length);
                });
        }

        public List<MatchResult> FindMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, buckets, null);
            return exact.Count > 0 ? exact : FindApproximateNamedMatch(text);
        }

        private List<MatchResult> FindApproximateNamedMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 5 || normalized.Length > 40 ||
                normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 5)
                return none;
            List<TranslationEntry> candidates;
            if (!iconTextBuckets.TryGetValue(normalized[0], out candidates)) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            HashSet<string> compared = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in candidates)
            {
                string identity = entry.Normalized + "\t" + entry.Chinese;
                if (!compared.Add(identity) || Math.Abs(entry.Normalized.Length - normalized.Length) > 7) continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                float required = Math.Min(normalized.Length, entry.Normalized.Length) <= 7 ? 0.84f : 0.77f;
                if (score < required) continue;
                if (score > bestScore) { secondScore = bestScore; bestScore = score; best = entry; }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.06f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        public List<MatchResult> FindTaskMatches(string text, string taskId)
        {
            List<MatchResult> exact = FindInBuckets(text, taskBuckets, taskId);
            if (exact.Count > 0)
            {
                string normalized = Normalize(text);
                int longestExact = 0;
                foreach (MatchResult match in exact)
                    longestExact = Math.Max(longestExact, match.Entry.Normalized.Length);
                // A selected-task crop often starts with the exact short title and then the
                // OCR-damaged description. The title must not suppress the much more useful
                // long description match for that same task.
                if (!String.IsNullOrEmpty(taskId) && normalized.Length >= 28 &&
                    longestExact * 2 < normalized.Length)
                {
                    List<MatchResult> longText = FindApproximateTaskMatch(text, taskId);
                    if (longText.Count > 0) return longText;
                }
                return exact;
            }
            if (String.IsNullOrEmpty(taskId)) return FindApproximateTaskNameMatch(text);
            return FindApproximateTaskMatch(text, taskId);
        }

        public List<MatchResult> FindSkillTextMatches(string text, string detailId = null)
        {
            List<MatchResult> exact = FindInBuckets(text, skillTextBuckets, null);
            if (!String.IsNullOrEmpty(detailId)) exact.RemoveAll(delegate(MatchResult match) { return CategoryId(match.Entry.Category) != detailId; });
            return exact.Count > 0 ? exact : FindApproximateDetailMatch(text, skillTextEntries, detailId);
        }

        public List<MatchResult> FindItemTextMatches(string text, string detailId = null)
        {
            List<MatchResult> exact = FindInBuckets(text, itemTextBuckets, null);
            if (!String.IsNullOrEmpty(detailId)) exact.RemoveAll(delegate(MatchResult match) { return CategoryId(match.Entry.Category) != detailId; });
            return exact.Count > 0 ? exact : FindApproximateDetailMatch(text, itemTextEntries, detailId);
        }

        public List<MatchResult> FindInterfaceTextMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, interfaceTextBuckets, null);
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-界面", StringComparison.Ordinal))
                    exact.Add(match);
            string normalized = Normalize(text);
            int longestExact = 0;
            foreach (MatchResult match in exact)
                longestExact = Math.Max(longestExact, match.Entry.Normalized.Length);
            if (exact.Count > 0 && (normalized.Length <= 40 ||
                longestExact * 2 >= normalized.Length)) return exact;
            // A long dialogue may contain a short exact place/item token. That token must
            // not suppress a much better fuzzy match for the complete sentence.
            List<MatchResult> longPhrase = FindApproximateInterfaceMatch(text);
            if (longPhrase.Count > 0) return longPhrase;
            if (exact.Count > 0) return exact;
            List<MatchResult> shortUi = FindApproximateShortUiMatch(text);
            return shortUi.Count > 0 ? shortUi : longPhrase;
        }

        private List<MatchResult> FindApproximateShortUiMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 4 || normalized.Length > 34) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in uiEntries)
            {
                int length = entry.Normalized.Length;
                if (length < 4 || length > 30 || Math.Abs(length - normalized.Length) > 7) continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                float required = Math.Min(length, normalized.Length) <= 7 ? 0.70f : 0.76f;
                if (score < required) continue;
                if (score > bestScore)
                {
                    secondScore = bestScore; bestScore = score; best = entry;
                }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.08f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        public List<MatchResult> FindCharacterStatMatches(string text)
        {
            List<MatchResult> result = FindInBuckets(text, buckets, null);
            string normalized = Normalize(text);
            if (normalized.Length == 0) return result;
            string[] keys = new string[] { "character stat", "character info", "ability point",
                "weapon def", "magic def", "crit damage", "crit rate", "accuracy", "evasion",
                "attack", "magic", "speed", "jump", "level", "name", "job", "fame",
                "exp", "str", "dex", "int", "luk", "hp", "mp" };
            string[] chinese = new string[] { "角色属性", "角色信息", "能力值点数",
                "物理防御力", "魔法防御力", "暴击伤害", "暴击率", "命中率", "回避率",
                "攻击力", "魔法攻击力", "移动速度", "跳跃力", "等级", "名称", "职业", "人气",
                "经验", "力量", "敏捷", "智力", "运气", "生命值", "魔法值" };
            foreach (MatchResult existing in result)
                if (existing.Start == 0) return result;
            foreach (int index in CharacterStatKeyOrder(keys))
            {
                string key = keys[index];
                int take = Math.Min(normalized.Length, key.Length + 2);
                string prefix = normalized.Substring(0, take);
                int separator = prefix.IndexOf(' ');
                if (key.IndexOf(' ') < 0 && separator >= 0) prefix = prefix.Substring(0, separator);
                else if (key.IndexOf(' ') >= 0)
                {
                    string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int wanted = key.Split(' ').Length;
                    if (words.Length >= wanted) prefix = String.Join(" ", words, 0, wanted);
                }
                int allowed = key.Length <= 3 ? 1 : (key.Length <= 8 ? 2 : 3);
                if (EditDistance(prefix, key, allowed) > allowed) continue;
                TranslationEntry entry = new TranslationEntry {
                    English = prefix, Chinese = chinese[index], Category = "怀旧服-界面",
                    Normalized = prefix
                };
                result.Insert(0, new MatchResult { Entry = entry, Start = 0, Length = prefix.Length });
                break;
            }
            return result;
        }

        private static IEnumerable<int> CharacterStatKeyOrder(string[] keys)
        {
            List<int> order = new List<int>();
            for (int i = 0; i < keys.Length; i++) order.Add(i);
            order.Sort(delegate(int left, int right) { return keys[right].Length.CompareTo(keys[left].Length); });
            return order;
        }

        private static int EditDistance(string left, string right, int stopAfter)
        {
            if (Math.Abs(left.Length - right.Length) > stopAfter) return stopAfter + 1;
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i; int rowBest = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                    rowBest = Math.Min(rowBest, current[j]);
                }
                if (rowBest > stopAfter) return stopAfter + 1;
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }

        private List<MatchResult> FindApproximateInterfaceMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 12) return none;
            HashSet<string> inputWords = SignificantWords(normalized);
            if (inputWords.Count < 2) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in interfaceTextEntries)
            {
                HashSet<string> candidateWords = SignificantWords(entry.Normalized);
                if (candidateWords.Count == 0) continue;
                int common = 0;
                foreach (string word in inputWords) if (candidateWords.Contains(word)) common++;
                bool longPhrase = candidateWords.Count >= 5;
                if (common < (longPhrase ? 3 : Math.Max(2, candidateWords.Count - 1))) continue;
                float precision = (float)common / Math.Max(1, inputWords.Count);
                float coverage = (float)common / Math.Max(1, candidateWords.Count);
                if (longPhrase ? (coverage < 0.38f || precision < 0.28f) :
                    (coverage < 0.72f || precision < 0.45f)) continue;
                float score = coverage * 0.68f + precision * 0.32f;
                if (score > bestScore)
                {
                    if (best != null && best != entry) secondScore = Math.Max(secondScore, bestScore);
                    best = entry; bestScore = score;
                }
                else if (best != entry) secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.04f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        private List<MatchResult> FindApproximateTaskMatch(string text, string taskId)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 28) return none;
            HashSet<string> inputWords = SignificantWords(normalized);
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in taskEntries)
            {
                if (!entry.IsTaskText || entry.TaskId != taskId || entry.DetailWords == null) continue;
                HashSet<string> candidateWords = SignificantWords(entry.Normalized);
                int common = 0;
                foreach (string word in inputWords) if (candidateWords.Contains(word)) common++;
                if (common < 7) continue;
                float precision = (float)common / Math.Max(1, inputWords.Count);
                float coverage = (float)common / Math.Max(1, candidateWords.Count);
                if (precision < 0.35f || coverage < 0.45f) continue;
                float score = precision * 0.42f + coverage * 0.58f;
                if (score > bestScore)
                {
                    if (best != null && best != entry) secondScore = Math.Max(secondScore, bestScore);
                    best = entry; bestScore = score;
                }
                else if (best != entry) secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.035f)) return none;
            return new List<MatchResult> { new MatchResult { Entry = best, Start = 0, Length = normalized.Length } };
        }

        private List<MatchResult> FindApproximateTaskNameMatch(string text)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 5 || normalized.Length > 72) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in taskNames)
            {
                if (Math.Abs(entry.Normalized.Length - normalized.Length) > Math.Max(5, entry.Normalized.Length / 3))
                    continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                if (score < 0.72f) continue;
                if (score > bestScore) { secondScore = bestScore; bestScore = score; best = entry; }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.07f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        private static HashSet<string> SignificantWords(string normalized)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (word.Length >= 3) result.Add(word);
            return result;
        }

        private static List<MatchResult> FindApproximateDetailMatch(string text, List<TranslationEntry> entries, string detailId = null)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 18) return none;
            string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> input = new HashSet<string>(words, StringComparer.Ordinal);
            TranslationEntry best = null; float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (!String.IsNullOrEmpty(detailId) && CategoryId(entry.Category) != detailId) continue;
                HashSet<string> candidate = entry.DetailWords;
                if (candidate == null) continue;
                int common = 0;
                foreach (string word in input) if (candidate.Contains(word)) common++;
                if (common < 5) continue;
                float precision = (float)common / Math.Max(1, input.Count);
                float coverage = (float)common / Math.Max(1, candidate.Count);
                if (precision < 0.68f || coverage < 0.25f) continue;
                float score = precision * 0.72f + coverage * 0.28f;
                if (entry.Normalized.Contains(normalized)) score += 0.18f;
                if (score > bestScore)
                {
                    if (best != null && best.Category != entry.Category)
                        secondScore = Math.Max(secondScore, bestScore);
                    bestScore = score; best = entry;
                }
                else if ((best == null || best.Category != entry.Category) && score > secondScore)
                    secondScore = score;
            }
            if (best == null || bestScore < 0.68f || (secondScore > 0 && bestScore - secondScore < 0.035f)) return none;
            return new List<MatchResult> { new MatchResult { Entry = best, Start = 0, Length = normalized.Length } };
        }

        internal static string CategoryId(string category)
        {
            int marker = category == null ? -1 : category.LastIndexOf('#');
            return marker >= 0 && marker + 1 < category.Length ? category.Substring(marker + 1) : "";
        }

        public bool LooksLikeSkillTextStart(string text)
        {
            bool detail;
            return ClassifySkillText(text, out detail);
        }

        public bool ClassifySkillText(string text, out bool detail)
        {
            detail = false;
            string normalized = Normalize(text);
            if (normalized.Length < 3) return false;
            byte cached;
            lock (skillClassificationLock)
                if (skillClassificationCache.TryGetValue(normalized, out cached))
                {
                    detail = (cached & 2) != 0;
                    return (cached & 1) != 0;
                }
            bool skillName = false;
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal))
                { skillName = true; break; }
            if (normalized.Length >= 8)
            {
                List<TranslationEntry> candidates;
                if (skillTextBuckets.TryGetValue(normalized[0], out candidates))
                {
                    foreach (TranslationEntry candidate in candidates)
                    {
                        if (candidate.Normalized.StartsWith(normalized, StringComparison.Ordinal) ||
                            normalized.StartsWith(candidate.Normalized, StringComparison.Ordinal))
                        {
                            detail = normalized.Length >= 18;
                            break;
                        }
                    }
                }
                if (!detail)
                    detail = HasStrongDetailFragment(text, skillTextEntries) ||
                        FindApproximateDetailMatch(text, skillTextEntries).Count > 0;
            }
            bool result = skillName || detail;
            byte value = (byte)((result ? 1 : 0) | (detail ? 2 : 0));
            lock (skillClassificationLock)
            {
                if (skillClassificationCache.Count >= 2048)
                    skillClassificationCache.Clear();
                skillClassificationCache[normalized] = value;
            }
            return result;
        }

        public bool LooksLikeSkillDetailText(string text)
        {
            bool detail;
            ClassifySkillText(text, out detail);
            return detail;
        }


        public bool LooksLikeItemTextStart(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length < 3) return false;
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal)) return true;
            if (normalized.Length < 8) return false;
            List<TranslationEntry> candidates;
            if (itemTextBuckets.TryGetValue(normalized[0], out candidates))
            {
                foreach (TranslationEntry candidate in candidates)
                {
                    if (candidate.Normalized.StartsWith(normalized, StringComparison.Ordinal) ||
                        normalized.StartsWith(candidate.Normalized, StringComparison.Ordinal)) return true;
                }
            }
            return HasStrongDetailFragment(text, itemTextEntries) ||
                FindApproximateDetailMatch(text, itemTextEntries).Count > 0;
        }

        public bool LooksLikeItemDetailText(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length < 12) return false;
            foreach (MatchResult match in FindItemTextMatches(text))
            {
                if (match.Entry == null || !match.Entry.IsItemText || match.Length < 10) continue;
                if (match.Entry.Normalized.Length >= 16 ||
                    match.Length * 10 >= normalized.Length * 6) return true;
            }
            return false;
        }

        public string DetectSkillId(string text)
        {
            return DetectNamedDetailId(text, "怀旧服-技能#");
        }

        public string DetectSkillContentId(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, skillTextBuckets, null);
            MatchResult best = null;
            foreach (MatchResult match in exact)
                if (best == null || match.Entry.Normalized.Length > best.Entry.Normalized.Length)
                    best = match;
            if (best != null) return CategoryId(best.Entry.Category);

            List<MatchResult> approximate = FindApproximateDetailMatch(text, skillTextEntries);
            return approximate.Count > 0 ? CategoryId(approximate[0].Entry.Category) : "";
        }

        public string DetectItemId(string text)
        {
            string id = DetectNamedDetailId(text, "怀旧服-装备#");
            return id.Length > 0 ? id : DetectNamedDetailId(text, "怀旧服-道具#");
        }

        public TranslationEntry GetSkillOverview(string detailId)
        {
            return GetLongestDetailEntry(skillTextEntries, detailId);
        }

        public TranslationEntry GetItemOverview(string detailId)
        {
            return GetLongestDetailEntry(itemTextEntries, detailId);
        }

        private static TranslationEntry GetLongestDetailEntry(List<TranslationEntry> source,
            string detailId)
        {
            if (String.IsNullOrEmpty(detailId)) return null;
            TranslationEntry best = null;
            foreach (TranslationEntry entry in source)
            {
                if (CategoryId(entry.Category) != detailId) continue;
                // The overview is the longest prose entry for an ID. Level rows are
                // deliberately shorter and remain matched from their visible numbers.
                if (best == null || entry.Normalized.Length > best.Normalized.Length)
                    best = entry;
            }
            return best;
        }

        private string DetectNamedDetailId(string text, string categoryPrefix)
        {
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith(categoryPrefix, StringComparison.Ordinal))
                    return CategoryId(match.Entry.Category);
            return "";
        }

        private static bool HasStrongDetailFragment(string text, List<TranslationEntry> entries)
        {
            string normalized = Normalize(text);
            if (normalized.Length < 18) return false;
            HashSet<string> input = SignificantWords(normalized);
            if (input.Count < 5) return false;
            foreach (TranslationEntry entry in entries)
            {
                if (entry.DetailWords == null) continue;
                int common = 0;
                foreach (string word in input) if (entry.DetailWords.Contains(word)) common++;
                if (common >= 5 && (float)common / input.Count >= 0.58f) return true;
            }
            return false;
        }

        public bool HasDetailCandidate(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Contains("master level") || normalized.Contains("next level") ||
                normalized.Contains("enhancements") || normalized.Contains("req lev") ||
                normalized.Contains("required level")) return true;
            // A tooltip can overlap the user-defined chat rectangle. Preserve lines that
            // strongly match a known skill/item description before chat exclusion runs;
            // ordinary player chat still stays excluded because it has no detail match.
            if (LooksLikeSkillTextStart(text) || LooksLikeItemTextStart(text)) return true;
            foreach (MatchResult match in FindInBuckets(text, buckets, null))
                if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal)) return true;
            return false;
        }

        public string DetectTaskId(string text)
        {
            string normalized = Normalize(text);
            Dictionary<string, int> scores = new Dictionary<string, int>();
            foreach (TranslationEntry entry in taskEntries)
            {
                int position = normalized.IndexOf(entry.Normalized, StringComparison.Ordinal);
                if (position < 0) continue;
                if (!IsBoundary(normalized, position - 1) || !IsBoundary(normalized, position + entry.Normalized.Length)) continue;
                int score = entry.Normalized.Length * (entry.IsTaskText ? 4 : 1);
                int current;
                scores.TryGetValue(entry.TaskId, out current);
                scores[entry.TaskId] = current + score;
            }
            string bestId = "";
            int bestScore = 0;
            bool tied = false;
            foreach (KeyValuePair<string, int> pair in scores)
            {
                if (pair.Value > bestScore) { bestId = pair.Key; bestScore = pair.Value; tied = false; }
                else if (pair.Value == bestScore) tied = true;
            }
            if (!tied && bestId.Length > 0) return bestId;
            List<MatchResult> approximate = FindApproximateTaskNameMatch(text);
            return approximate.Count > 0 ? approximate[0].Entry.TaskId : "";
        }

        public bool LooksLikeQuestInterface(string text)
        {
            if (!String.IsNullOrEmpty(DetectTaskId(text))) return true;
            string normalized = Normalize(text);
            bool questAnchor = normalized.Contains("quest") &&
                (normalized.Contains("accept") || normalized.Contains("decline") ||
                 normalized.Contains("reward") || normalized.Contains("complete") ||
                 normalized.Contains("in progress"));
            if (questAnchor) return true;
            List<MatchResult> matches = FindTaskMatches(text, null);
            foreach (MatchResult match in matches) if (match.Length >= 20) return true;
            return false;
        }

        private static List<MatchResult> FindInBuckets(string text,
            Dictionary<char, List<TranslationEntry>> source, string taskId)
        {
            string normalized = Normalize(text);
            List<MatchResult> results = new List<MatchResult>();
            int position = 0;
            while (position < normalized.Length)
            {
                List<TranslationEntry> candidates;
                TranslationEntry best = null;
                if (source.TryGetValue(normalized[position], out candidates))
                {
                    foreach (TranslationEntry candidate in candidates)
                    {
                        if (!String.IsNullOrEmpty(taskId) && candidate.TaskId != taskId) continue;
                        if (position + candidate.Normalized.Length > normalized.Length) continue;
                        if (string.CompareOrdinal(normalized, position, candidate.Normalized, 0,
                            candidate.Normalized.Length) != 0) continue;
                        if (!IsBoundary(normalized, position - 1) ||
                            !IsBoundary(normalized, position + candidate.Normalized.Length)) continue;
                        best = candidate;
                        break;
                    }
                }
                if (best == null) { position++; continue; }
                results.Add(new MatchResult { Entry = best, Start = position, Length = best.Normalized.Length });
                position += best.Normalized.Length;
            }
            return results;
        }

        public IconMatchResult FindIconAssistedMatch(ulong hash, string recognizedText)
        {
            string normalizedText = Normalize(recognizedText);
            PrepareIconCandidates(normalizedText);
            if (cachedIconCandidates.Count == 0) return null;
            IconMatchResult best = null;
            float secondScore = Single.MinValue;
            foreach (KeyValuePair<TranslationEntry, float> pair in cachedIconCandidates)
            {
                TranslationEntry entry = pair.Key;
                int distance = HammingDistance(hash, entry.IconHash);
                float similarity = pair.Value;
                if (Program.Benchmark && similarity >= 0.75f)
                {
                    if (distance < Program.BenchmarkBestIconDistance)
                    {
                        Program.BenchmarkBestIconDistance = distance;
                        Program.BenchmarkBestIcon = entry.English + "@" + Program.BenchmarkCurrentArea;
                    }
                }
                if (distance > 24) continue;
                if (similarity < 0.58f) continue;
                // A noisy icon is accepted only when the OCR text is already a strong fuzzy match.
                if (distance > 9 && similarity < 0.78f) continue;
                float score = similarity * 22.0f - distance * 0.45f;
                if (best == null || score > best.Score)
                {
                    if (best != null) secondScore = Math.Max(secondScore, best.Score);
                    best = new IconMatchResult { Entry = entry, Score = score, IconDistance = distance };
                }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || best.Score < 9.0f) return null;
            if (secondScore != Single.MinValue && best.Score - secondScore < 1.0f) return null;
            return best;
        }

        public bool HasPlausibleIconText(string recognizedText)
        {
            PrepareIconCandidates(Normalize(recognizedText));
            return cachedIconCandidates.Count > 0;
        }

        private void PrepareIconCandidates(string normalizedText)
        {
            if (normalizedText == cachedIconText) return;
            cachedIconText = normalizedText;
            cachedIconCandidates.Clear();
            if (normalizedText.Length < 3) return;
            List<TranslationEntry> candidates;
            if (!iconTextBuckets.TryGetValue(normalizedText[0], out candidates)) return;
            foreach (TranslationEntry entry in candidates)
            {
                float lengthRatio = (float)Math.Min(normalizedText.Length, entry.Normalized.Length) /
                    Math.Max(normalizedText.Length, entry.Normalized.Length);
                if (lengthRatio < 0.45f) continue;
                float similarity = TextSimilarity(normalizedText, entry.Normalized);
                if (similarity >= 0.58f)
                    cachedIconCandidates.Add(new KeyValuePair<TranslationEntry, float>(entry, similarity));
            }
        }

        private static int HammingDistance(ulong left, ulong right)
        {
            ulong value = left ^ right;
            int count = 0;
            while (value != 0) { value &= value - 1; count++; }
            return count;
        }

        private static float TextSimilarity(string left, string right)
        {
            if (left == right) return 1.0f;
            if (left.Length == 0 || right.Length == 0) return 0.0f;
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous; previous = current; current = swap;
            }
            return 1.0f - (float)previous[right.Length] / Math.Max(left.Length, right.Length);
        }

        internal static float DetailTextSimilarity(string text, string normalizedCandidate)
        {
            return TextSimilarity(Normalize(text), normalizedCandidate ?? String.Empty);
        }

        public static string Normalize(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder b = new StringBuilder(value.Length);
            bool space = false;
            foreach (char raw in value.Trim().ToLowerInvariant())
            {
                char c = raw == '\u2019' || raw == '\u2018' ? '\'' : raw;
                bool keep = Char.IsLetterOrDigit(c) || c == '\'' || c == '+' || c == '%' || c == '#';
                if (!keep)
                {
                    if (!space && b.Length > 0) b.Append(' ');
                    space = true;
                }
                else
                {
                    b.Append(c);
                    space = false;
                }
            }
            string normalized = b.ToString().Trim();
            // Keep apostrophes inside names (Biggs's), but discard quote-like OCR
            // decoration at token edges ('LERVE STORE) before whole-token repair.
            normalized = Regex.Replace(normalized,
                @"(?<![a-z0-9])'+|'+(?![a-z0-9])", " ");
            if (normalized.Length == 0) return normalized;
            string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                // Conservative whole-token fixes for the recurring Classic UI OCR shapes.
                // They improve panel discovery without changing player names or free-form chat.
                if (words[i] == "ouest" || words[i] == "ojest") words[i] = "quest";
                else if (words[i] == "lerve") words[i] = "leave";
                else if (words[i] == "pethils") words[i] = "details";
                else if (words[i] == "reo" || words[i] == "aeq") words[i] = "req";
                else if (words[i] == "attacx") words[i] = "attack";
                else if (words[i] == "tor") words[i] = "for";
                else if (words[i] == "tun") words[i] = "fun";
                else if (words[i] == "go" && i + 1 < words.Length && words[i + 1] == "seconds")
                    words[i] = "90";
            }
            normalized = String.Join(" ", words);
            normalized = normalized.Replace("jest helper", "quest helper")
                .Replace("uest helper", "quest helper");
            return normalized;
        }

        private static bool IsBoundary(string text, int at)
        {
            if (at < 0 || at >= text.Length) return true;
            char c = text[at];
            return !Char.IsLetterOrDigit(c) && c != '\'';
        }

        private static List<TranslationEntry> ReadTsv(string path)
        {
            List<TranslationEntry> result = new List<TranslationEntry>();
            if (!File.Exists(path)) throw new FileNotFoundException("找不到词库", path);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            // Stream the TSV instead of allocating a second array containing the whole
            // dictionary. Entry construction and index ordering stay exactly the same.
            foreach (string raw in File.ReadLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 2) continue;
                string english = parts[0].Trim();
                string chinese = PolishChinese(parts[1].Trim());
                string normalized = Normalize(english);
                string category = parts.Length > 2 ? parts[2].Trim() : "";
                bool isTaskName = category.StartsWith("怀旧服-任务#", StringComparison.Ordinal);
                bool isTaskText = category.StartsWith("怀旧服-任务说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-任务对白#", StringComparison.Ordinal);
                bool isSkillText = category.StartsWith("怀旧服-技能说明#", StringComparison.Ordinal);
                bool isItemText = category.StartsWith("怀旧服-装备说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-物品说明#", StringComparison.Ordinal);
                bool isInterfaceText = category.StartsWith("怀旧服-界面长句", StringComparison.Ordinal);
                string taskId = (isTaskName || isTaskText) && category.LastIndexOf('#') >= 0
                    ? category.Substring(category.LastIndexOf('#') + 1) : "";
                ulong iconHash = 0;
                bool hasIcon = parts.Length > 3 && UInt64.TryParse(parts[3].Trim(),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iconHash);
                // Icon-backed entries are distinct by source category/ID even when their English
                // names are identical. This preserves colour, appearance, job and duplicate-NPC
                // variants for icon-assisted disambiguation.
                string dedupeScope = (isTaskName || isTaskText) ? taskId :
                    ((hasIcon || isSkillText || isItemText || isInterfaceText) && category.Length > 0 ? category : "general");
                string dedupeKey = normalized + "\t" + dedupeScope;
                if (normalized.Length == 0 || chinese.Length == 0 || !seen.Add(dedupeKey)) continue;
                result.Add(new TranslationEntry {
                    English = english, Chinese = chinese,
                    Category = category, Normalized = normalized,
                    IconHash = hasIcon ? iconHash : 0, HasIcon = hasIcon,
                    TaskId = taskId, IsTaskName = isTaskName, IsTaskText = isTaskText,
                    IsSkillText = isSkillText, IsItemText = isItemText, IsInterfaceText = isInterfaceText,
                    DetailWords = (isTaskText || isSkillText || isItemText || isInterfaceText)
                        ? new HashSet<string>(normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
                        : null
                });
            }
            return result;
        }

        private static string PolishChinese(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            string text = value.Replace("Meso", "金币").Replace("meso", "金币")
                .Replace("DEX", "敏捷").Replace("STR", "力量")
                .Replace("INT", "智力").Replace("LUK", "运气")
                .Replace("8x伤害", "8倍伤害");
            text = Regex.Replace(text, "向(\\d+)投掷金币造成(\\d+)倍伤害",
                "投掷$1金币，造成$2倍伤害");
            text = Regex.Replace(text, "基于敏捷减少金币消耗量的(\\d+)%",
                "根据敏捷，有几率使金币消耗减少$1%");
            return text;
        }

        private static void WriteBinary(string path, List<TranslationEntry> entries)
        {
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(new byte[] { 77, 83, 68, 49 }); // MSD1
                writer.Write(entries.Count);
                foreach (TranslationEntry e in entries)
                {
                    writer.Write(e.English);
                    writer.Write(e.Chinese);
                    writer.Write(e.Category ?? "");
                }
            }
        }

        private static List<TranslationEntry> ReadBinary(string path)
        {
            List<TranslationEntry> result = new List<TranslationEntry>();
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                byte[] magic = reader.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 77 || magic[1] != 83 || magic[2] != 68 || magic[3] != 49)
                    throw new InvalidDataException("词库缓存版本不兼容");
                int count = reader.ReadInt32();
                if (count < 0 || count > 1000000) throw new InvalidDataException("词库缓存损坏");
                for (int i = 0; i < count; i++)
                {
                    string en = reader.ReadString();
                    result.Add(new TranslationEntry {
                        English = en, Chinese = reader.ReadString(), Category = reader.ReadString(),
                        Normalized = Normalize(en)
                    });
                }
            }
            return result;
        }
    }

    internal sealed class MatchResult
    {
        public TranslationEntry Entry;
        public int Start;
        public int Length;
    }

    internal sealed class IconMatchResult
    {
        public TranslationEntry Entry;
        public float Score;
        public int IconDistance;
    }

    internal sealed class OverlayLabel
    {
        public RectangleF Bounds;
        public string Text;
        public bool Wrap;
        public bool StableLayout;
    }

    internal static class OverlayPaintSafety
    {
        private const float MaximumCoordinate = 100000.0f;
        private const float MaximumDimension = 32768.0f;
        private const int MaximumTextLength = 4096;

        internal static bool TryPrepare(OverlayLabel label, out string text,
            out RectangleF bounds)
        {
            text = "";
            bounds = RectangleF.Empty;
            if (label == null || String.IsNullOrWhiteSpace(label.Text)) return false;

            RectangleF candidate = label.Bounds;
            if (!IsFinite(candidate.X) || !IsFinite(candidate.Y) ||
                !IsFinite(candidate.Width) || !IsFinite(candidate.Height)) return false;
            if (Math.Abs(candidate.X) > MaximumCoordinate ||
                Math.Abs(candidate.Y) > MaximumCoordinate ||
                candidate.Width < 0 || candidate.Height < 0 ||
                candidate.Width > MaximumDimension || candidate.Height > MaximumDimension)
                return false;

            candidate.Width = Math.Max(1.0f, candidate.Width);
            candidate.Height = Math.Max(1.0f, candidate.Height);
            text = label.Text.Length <= MaximumTextLength
                ? label.Text
                : label.Text.Substring(0, MaximumTextLength);
            bounds = candidate;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }
    }

    internal sealed class OcrPanelInfo
    {
        public readonly List<OcrLine> Lines = new List<OcrLine>();
        public string Text = "";
        public bool IsQuest;
        public bool IsSkillDetail;
        public bool IsCharacterStats;
        public bool IsCharacterInfo;
        public bool IsEquipmentDetail;
        public string TaskId = "";
        public string SkillId = "";
    }

    internal sealed class SkillPanelCandidate
    {
        public List<OcrLine> Lines;
        public string Text;
        public MatchResult Match;
        public int Score;
    }

    internal sealed class PanelCropCandidate
    {
        public Rectangle Bounds;
        public int Score;
        public string Kind;
        public RecognitionPriorityKind PriorityKind;
        public float SourceTextHeight;
        public double ResourceLevel;
    }

    internal enum RecognitionPriorityKind
    {
        Detail = 1,
        Dialogue = 2,
        OutsideDialogue = 3,
        CurrentInterface = 4
    }

    internal enum TranslationRangeMode
    {
        Maximum = 1,
        Balanced = 2,
        Minimum = 3
    }

    internal sealed class RecognitionTier
    {
        public RecognitionPriorityKind Kind;
        public double ResourceLevel;
    }

    internal static class RecognitionPriorityPlanner
    {
        private static readonly double[] Levels = new double[] { 1.0, 0.75, 0.5 };

        internal static bool ShouldUseCurrentInterface(bool hasDetail, bool hasDialogue,
            bool hasOutsideDialogue, bool hasVisibleText)
        {
            return hasVisibleText && !hasDetail && !hasDialogue && !hasOutsideDialogue;
        }

        internal static List<RecognitionTier> Select(bool hasDetail, bool hasDialogue,
            bool hasOutsideDialogue, bool hasCurrentInterface, int maximumTargets)
        {
            bool[] present = new bool[] { hasDetail, hasDialogue,
                hasOutsideDialogue, hasCurrentInterface };
            List<RecognitionTier> result = new List<RecognitionTier>();
            maximumTargets = Math.Max(1, Math.Min(Levels.Length, maximumTargets));
            for (int i = 0; i < present.Length && result.Count < maximumTargets; i++)
            {
                if (!present[i]) continue;
                result.Add(new RecognitionTier {
                    Kind = (RecognitionPriorityKind)(i + 1),
                    ResourceLevel = Levels[result.Count]
                });
            }
            return result;
        }

        internal static double LevelFor(List<RecognitionTier> plan, RecognitionPriorityKind kind)
        {
            if (plan != null) foreach (RecognitionTier tier in plan)
                if (tier.Kind == kind) return tier.ResourceLevel;
            return 0.0;
        }

        internal static float TargetLongEdge(Rectangle crop, float sourceTextHeight,
            double resourceLevel, bool tooltip)
        {
            int longEdge = Math.Max(1, Math.Max(crop.Width, crop.Height));
            float desiredGlyphHeight = resourceLevel >= 0.99 ? 28.0f :
                (resourceLevel >= 0.74 ? 23.0f : 19.0f);
            float byGlyph = sourceTextHeight >= 5.0f
                ? longEdge * desiredGlyphHeight / sourceTextHeight : longEdge * 2.0f;
            float minimum = tooltip
                ? (resourceLevel >= 0.99 ? 1500.0f : (resourceLevel >= 0.74 ? 1380.0f : 1240.0f))
                : (resourceLevel >= 0.99 ? 1650.0f : (resourceLevel >= 0.74 ? 1480.0f : 1320.0f));
            float maximum = tooltip
                ? (resourceLevel >= 0.99 ? 2100.0f : (resourceLevel >= 0.74 ? 1880.0f : 1660.0f))
                : (resourceLevel >= 0.99 ? 2250.0f : (resourceLevel >= 0.74 ? 2050.0f : 1840.0f));
            return Math.Min(maximum, Math.Max(minimum, byGlyph));
        }

        internal static string Describe(List<RecognitionTier> plan)
        {
            if (plan == null || plan.Count == 0) return "无";
            StringBuilder result = new StringBuilder();
            foreach (RecognitionTier tier in plan)
            {
                if (result.Length > 0) result.Append('>');
                string name = tier.Kind == RecognitionPriorityKind.Detail ? "详情框" :
                    (tier.Kind == RecognitionPriorityKind.Dialogue ? "对话框内" :
                    (tier.Kind == RecognitionPriorityKind.OutsideDialogue ? "对话框外" : "当前界面"));
                result.Append(name).Append(':').Append(tier.ResourceLevel.ToString("0.00", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }

    internal enum ChatRegionOrigin
    {
        None = 0,
        Automatic = 1,
        Manual = 2
    }

    internal sealed class ChatCaptureLine
    {
        internal string Text = "";
        internal ChatVisualStyle Style = ChatVisualStyle.Default;
    }

    internal sealed class ChatCaptureFrame
    {
        internal string Text = "";
        internal readonly List<ChatCaptureLine> Lines = new List<ChatCaptureLine>();

        internal string PhysicalLineText()
        {
            if (Lines.Count == 0) return Text ?? "";
            StringBuilder result = new StringBuilder();
            foreach (ChatCaptureLine line in Lines)
            {
                if (String.IsNullOrWhiteSpace(line.Text)) continue;
                if (result.Length > 0) result.AppendLine();
                result.Append(line.Text.Trim());
            }
            return result.ToString();
        }

        internal ChatVisualStyle FindStyle(string parsedLine)
        {
            string parsed = Normalize(parsedLine);
            if (parsed.Length == 0) return ChatVisualStyle.Default;
            ChatCaptureLine best = null;
            int bestScore = 0;
            foreach (ChatCaptureLine candidate in Lines)
            {
                string source = Normalize(candidate.Text);
                if (source.Length == 0) continue;
                int score = source.IndexOf(parsed, StringComparison.Ordinal) >= 0 ||
                    parsed.IndexOf(source, StringComparison.Ordinal) >= 0
                    ? Math.Min(source.Length, parsed.Length)
                    : Math.Max(SharedPrefix(source, parsed), LongestSharedRun(source, parsed));
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best != null && bestScore >= Math.Min(5, parsed.Length)
                ? best.Style : ChatVisualStylePolicy.FromSample(parsedLine, Color.Empty, Color.Empty, false);
        }

        private static string Normalize(string value)
        {
            StringBuilder result = new StringBuilder();
            foreach (char item in (value ?? "").ToLowerInvariant())
                if (Char.IsLetterOrDigit(item)) result.Append(item);
            return result.ToString();
        }

        private static int SharedPrefix(string left, string right)
        {
            int maximum = Math.Min(left.Length, right.Length);
            int count = 0;
            while (count < maximum && left[count] == right[count]) count++;
            return count;
        }

        private static int LongestSharedRun(string left, string right)
        {
            if (left.Length == 0 || right.Length == 0) return 0;
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            int best = 0;
            for (int i = 1; i <= left.Length; i++)
            {
                for (int j = 1; j <= right.Length; j++)
                {
                    current[j] = left[i - 1] == right[j - 1] ? previous[j - 1] + 1 : 0;
                    if (current[j] > best) best = current[j];
                }
                int[] swap = previous; previous = current; current = swap;
                Array.Clear(current, 0, current.Length);
            }
            return best;
        }
    }

    internal static class ChatRegionSettings
    {
        private const int Scale = 10000;
        private const string OriginValueName = "ChatRegionOrigin";

        private static void Save(Rectangle region, Rectangle gameBounds, ChatRegionOrigin origin)
        {
            if (region.Width < 40 || region.Height < 20 || gameBounds.Width < 1 || gameBounds.Height < 1) return;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
            {
                key.SetValue("ChatX", region.X); key.SetValue("ChatY", region.Y);
                key.SetValue("ChatW", region.Width); key.SetValue("ChatH", region.Height);
                key.SetValue("ChatRelX", (region.X - gameBounds.X) * Scale / gameBounds.Width);
                key.SetValue("ChatRelY", (region.Y - gameBounds.Y) * Scale / gameBounds.Height);
                key.SetValue("ChatRelW", region.Width * Scale / gameBounds.Width);
                key.SetValue("ChatRelH", region.Height * Scale / gameBounds.Height);
                key.SetValue("ChatGameX", gameBounds.X); key.SetValue("ChatGameY", gameBounds.Y);
                key.SetValue("ChatGameW", gameBounds.Width); key.SetValue("ChatGameH", gameBounds.Height);
                key.SetValue(OriginValueName, origin == ChatRegionOrigin.Manual ? "manual" : "automatic");
            }
        }

        public static void SaveManual(Rectangle region, Rectangle gameBounds)
        {
            Save(region, gameBounds, ChatRegionOrigin.Manual);
        }

        public static Rectangle SaveAutomatic(Rectangle suggestedRegion, Rectangle gameBounds)
        {
            // An explicit F9 realignment may refresh a previously automatic region, but it
            // must never replace a box that the player positioned manually.
            if (GetOrigin() == ChatRegionOrigin.Manual) return ResolveForGame(gameBounds);
            Rectangle region = Rectangle.Intersect(suggestedRegion, gameBounds);
            if (region.Width < 80 || region.Height < 30) region = DefaultForGame(gameBounds);
            Save(region, gameBounds, ChatRegionOrigin.Automatic);
            return region;
        }

        public static void ResaveForGame(Rectangle region, Rectangle gameBounds)
        {
            ChatRegionOrigin origin = GetOrigin();
            if (origin != ChatRegionOrigin.None) Save(region, gameBounds, origin);
        }

        public static Rectangle LoadAbsolute()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
                {
                    if (key == null) return Rectangle.Empty;
                    return new Rectangle(Convert.ToInt32(key.GetValue("ChatX", 0)),
                        Convert.ToInt32(key.GetValue("ChatY", 0)),
                        Convert.ToInt32(key.GetValue("ChatW", 0)),
                        Convert.ToInt32(key.GetValue("ChatH", 0)));
                }
            }
            catch { return Rectangle.Empty; }
        }

        public static bool HasSelection()
        {
            return IsUsable(LoadAbsolute());
        }

        internal static bool IsUsable(Rectangle value)
        {
            return value.Width >= 80 && value.Height >= 20;
        }

        public static ChatRegionOrigin GetOrigin()
        {
            if (!HasSelection()) return ChatRegionOrigin.None;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
                {
                    string value = key == null ? "" : Convert.ToString(key.GetValue(OriginValueName, ""));
                    if (String.Equals(value, "automatic", StringComparison.OrdinalIgnoreCase))
                        return ChatRegionOrigin.Automatic;
                    if (String.Equals(value, "manual", StringComparison.OrdinalIgnoreCase))
                        return ChatRegionOrigin.Manual;
                }
            }
            catch { }
            // Regions saved by an older release could only have come from the selector.
            // Treat them as manual so an upgrade never overwrites the user's choice.
            return ChatRegionOrigin.Manual;
        }

        public static Rectangle ResolveForGame(Rectangle gameBounds)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
                {
                    if (key != null)
                    {
                        int relX = Convert.ToInt32(key.GetValue("ChatRelX", -1));
                        int relY = Convert.ToInt32(key.GetValue("ChatRelY", -1));
                        int relW = Convert.ToInt32(key.GetValue("ChatRelW", -1));
                        int relH = Convert.ToInt32(key.GetValue("ChatRelH", -1));
                        if (relX >= 0 && relY >= 0 && relW >= 100 && relH >= 100 &&
                            relX + relW <= Scale && relY + relH <= Scale)
                        {
                            Rectangle relative = new Rectangle(gameBounds.X + relX * gameBounds.Width / Scale,
                                gameBounds.Y + relY * gameBounds.Height / Scale,
                                Math.Max(40, relW * gameBounds.Width / Scale),
                                Math.Max(20, relH * gameBounds.Height / Scale));
                            return Rectangle.Intersect(relative, gameBounds);
                        }
                    }
                }
            }
            catch { }

            Rectangle absolute = LoadAbsolute();
            if (absolute.Width >= 80 && absolute.Height >= 30)
            {
                Rectangle overlap = Rectangle.Intersect(absolute, gameBounds);
                long sourceArea = (long)absolute.Width * absolute.Height;
                long overlapArea = (long)overlap.Width * overlap.Height;
                if (sourceArea > 0 && overlapArea * 10 >= sourceArea * 7) return overlap;
            }
            return DefaultForGame(gameBounds);
        }

        public static Rectangle DefaultForGame(Rectangle gameBounds)
        {
            return new Rectangle(gameBounds.Left + gameBounds.Width * 10 / 100,
                gameBounds.Top + gameBounds.Height * 76 / 100,
                gameBounds.Width * 55 / 100, gameBounds.Height * 21 / 100);
        }
    }

    internal static class ChatRegionDetector
    {
        public static Rectangle Detect(IList<RectangleF> chatLines, Rectangle gameBounds)
        {
            RectangleF union = RectangleF.Empty;
            int count = 0;
            float minimumY = gameBounds.Top + gameBounds.Height * 0.58f;
            float maximumLeft = gameBounds.Left + gameBounds.Width * 0.72f;
            if (chatLines != null)
            {
                foreach (RectangleF line in chatLines)
                {
                    if (line.IsEmpty || line.Bottom < minimumY || line.Left > maximumLeft) continue;
                    union = count == 0 ? line : RectangleF.Union(union, line);
                    count++;
                }
            }
            if (count == 0) return ChatRegionSettings.DefaultForGame(gameBounds);

            int horizontalPadding = Math.Max(8, gameBounds.Width * 12 / 1000);
            int left = Math.Max(gameBounds.Left, (int)Math.Floor(union.Left) - horizontalPadding);
            int top = Math.Max(gameBounds.Top + gameBounds.Height * 58 / 100,
                (int)Math.Floor(union.Top) - Math.Max(34, gameBounds.Height * 9 / 100));
            int right = Math.Max((int)Math.Ceiling(union.Right) + gameBounds.Width * 2 / 100,
                left + gameBounds.Width * 42 / 100);
            int bottom = Math.Max((int)Math.Ceiling(union.Bottom) + gameBounds.Height * 5 / 100,
                gameBounds.Top + gameBounds.Height * 96 / 100);
            right = Math.Min(gameBounds.Right, right);
            bottom = Math.Min(gameBounds.Bottom, bottom);
            Rectangle detected = Rectangle.FromLTRB(left, top, right, bottom);
            return detected.Width >= 240 && detected.Height >= 70
                ? detected : ChatRegionSettings.DefaultForGame(gameBounds);
        }
    }

    [Flags]
    internal enum GamepadButton : uint
    {
        None = 0,
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000,
        LeftTrigger = 0x00010000,
        RightTrigger = 0x00020000
    }

    internal struct GamepadShortcut
    {
        public readonly GamepadButton First;
        public readonly GamepadButton Second;

        public GamepadShortcut(GamepadButton first, GamepadButton second)
        {
            First = first;
            Second = first == GamepadButton.None || second == first ? GamepadButton.None : second;
        }

        public bool IsBound { get { return First != GamepadButton.None; } }
        public uint Mask { get { return (uint)First | (uint)Second; } }

        public bool IsPressed(uint buttons)
        {
            uint mask = Mask;
            return mask != 0 && (buttons & mask) == mask;
        }

        public bool ConflictsWith(GamepadShortcut other)
        {
            if (!IsBound || !other.IsBound) return false;
            uint left = Mask;
            uint right = other.Mask;
            return (left & right) == left || (left & right) == right;
        }

        public string Serialize()
        {
            if (!IsBound) return "";
            return First + (Second == GamepadButton.None ? "" : "+" + Second);
        }

        public static GamepadShortcut Parse(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return new GamepadShortcut();
            string[] parts = value.Split('+');
            GamepadButton first;
            GamepadButton second = GamepadButton.None;
            if (!Enum.TryParse<GamepadButton>(parts[0].Trim(), true, out first) || first == GamepadButton.None)
                return new GamepadShortcut();
            if (parts.Length > 1)
            {
                GamepadButton parsed;
                if (Enum.TryParse<GamepadButton>(parts[1].Trim(), true, out parsed)) second = parsed;
            }
            return new GamepadShortcut(first, second);
        }

        public override string ToString()
        {
            if (!IsBound) return "未绑定";
            string text = GamepadButtonNames.Display(First);
            if (Second != GamepadButton.None) text += "+" + GamepadButtonNames.Display(Second);
            return text;
        }
    }

    internal static class GamepadButtonNames
    {
        public static string Display(GamepadButton button)
        {
            switch (button)
            {
                case GamepadButton.LeftShoulder: return "LB";
                case GamepadButton.RightShoulder: return "RB";
                case GamepadButton.LeftTrigger: return "LT";
                case GamepadButton.RightTrigger: return "RT";
                case GamepadButton.LeftThumb: return "L3";
                case GamepadButton.RightThumb: return "R3";
                case GamepadButton.DPadUp: return "方向上";
                case GamepadButton.DPadDown: return "方向下";
                case GamepadButton.DPadLeft: return "方向左";
                case GamepadButton.DPadRight: return "方向右";
                case GamepadButton.Start: return "菜单";
                case GamepadButton.Back: return "视图";
                default: return button == GamepadButton.None ? "未绑定" : button.ToString();
            }
        }

        public static GamepadButton[] SelectableButtons()
        {
            return new GamepadButton[] {
                GamepadButton.A, GamepadButton.B, GamepadButton.X, GamepadButton.Y,
                GamepadButton.LeftShoulder, GamepadButton.RightShoulder,
                GamepadButton.LeftTrigger, GamepadButton.RightTrigger,
                GamepadButton.LeftThumb, GamepadButton.RightThumb,
                GamepadButton.DPadUp, GamepadButton.DPadDown,
                GamepadButton.DPadLeft, GamepadButton.DPadRight,
                GamepadButton.Start, GamepadButton.Back
            };
        }
    }

    internal enum GamepadShortcutAction
    {
        None,
        Show,
        Hide
    }

    internal sealed class GamepadShortcutLatch
    {
        private bool showBlocked = true;
        private bool hideBlocked = true;

        public void Reset()
        {
            // Requiring a release after startup/settings changes prevents a held button
            // from activating the overlay as soon as the app begins polling.
            showBlocked = true;
            hideBlocked = true;
        }

        public GamepadShortcutAction Evaluate(uint[] controllerStates,
            GamepadShortcut showShortcut, GamepadShortcut hideShortcut)
        {
            bool showPressed = IsPressedOnOneController(controllerStates, showShortcut);
            bool hidePressed = IsPressedOnOneController(controllerStates, hideShortcut);

            if (!showPressed) showBlocked = false;
            if (!hidePressed) hideBlocked = false;

            // Ambiguous input must never fire both commands. It remains blocked until release.
            if (showPressed && hidePressed)
            {
                showBlocked = true;
                hideBlocked = true;
                return GamepadShortcutAction.None;
            }
            if (showPressed && !showBlocked)
            {
                showBlocked = true;
                return GamepadShortcutAction.Show;
            }
            if (hidePressed && !hideBlocked)
            {
                hideBlocked = true;
                return GamepadShortcutAction.Hide;
            }
            return GamepadShortcutAction.None;
        }

        private static bool IsPressedOnOneController(uint[] controllerStates, GamepadShortcut shortcut)
        {
            if (!shortcut.IsBound || controllerStates == null) return false;
            for (int i = 0; i < controllerStates.Length; i++)
                if (shortcut.IsPressed(controllerStates[i])) return true;
            return false;
        }

        internal static bool RunSelfTest()
        {
            GamepadShortcut show = new GamepadShortcut(GamepadButton.A, GamepadButton.None);
            GamepadShortcut hide = new GamepadShortcut(GamepadButton.LeftShoulder, GamepadButton.RightShoulder);
            GamepadShortcutLatch latch = new GamepadShortcutLatch();
            uint[] states = new uint[4];

            states[0] = (uint)GamepadButton.A;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.None) return false;
            states[0] = 0;
            latch.Evaluate(states, show, hide);
            states[0] = (uint)GamepadButton.A;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.Show) return false;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.None) return false;
            states[0] = 0;
            latch.Evaluate(states, show, hide);
            states[0] = (uint)GamepadButton.LeftShoulder;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.None) return false;
            states[0] |= (uint)GamepadButton.RightShoulder;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.Hide) return false;
            states[0] = 0;
            latch.Evaluate(states, show, hide);
            states[0] = (uint)GamepadButton.A;
            states[1] = (uint)GamepadButton.LeftShoulder | (uint)GamepadButton.RightShoulder;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.None) return false;

            latch.Reset();
            states[0] = 0;
            states[1] = 0;
            latch.Evaluate(states, show, hide);
            states[0] = (uint)GamepadButton.LeftShoulder;
            states[1] = (uint)GamepadButton.RightShoulder;
            if (latch.Evaluate(states, show, hide) != GamepadShortcutAction.None) return false;

            GamepadShortcut overlapping = new GamepadShortcut(GamepadButton.A, GamepadButton.B);
            if (!show.ConflictsWith(overlapping)) return false;
            if (show.ConflictsWith(hide)) return false;
            if (GamepadShortcut.Parse("LeftShoulder+RightShoulder").Mask != hide.Mask) return false;
            for (int i = 0; i < 4; i++) XInputReader.ReadButtons(i);
            return true;
        }
    }

    internal static class XInputReader
    {
        private const uint ErrorSuccess = 0;
        private const byte TriggerThreshold = 48;
        private static int backend;

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState14(uint userIndex, out XInputState state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState910(uint userIndex, out XInputState state);
        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState13(uint userIndex, out XInputState state);

        public static uint ReadButtons(int userIndex)
        {
            XInputState state;
            uint result;
            if (!TryGetState((uint)userIndex, out state, out result) || result != ErrorSuccess) return 0;
            uint buttons = state.Gamepad.Buttons;
            if (state.Gamepad.LeftTrigger >= TriggerThreshold) buttons |= (uint)GamepadButton.LeftTrigger;
            if (state.Gamepad.RightTrigger >= TriggerThreshold) buttons |= (uint)GamepadButton.RightTrigger;
            return buttons;
        }

        private static bool TryGetState(uint userIndex, out XInputState state, out uint result)
        {
            state = new XInputState();
            result = 0;
            if (backend != 0) return TryBackend(backend, userIndex, out state, out result);
            for (int candidate = 1; candidate <= 3; candidate++)
            {
                if (!TryBackend(candidate, userIndex, out state, out result)) continue;
                backend = candidate;
                return true;
            }
            backend = -1;
            return false;
        }

        private static bool TryBackend(int candidate, uint userIndex,
            out XInputState state, out uint result)
        {
            state = new XInputState();
            result = 0;
            try
            {
                if (candidate == 1) result = GetState14(userIndex, out state);
                else if (candidate == 2) result = GetState910(userIndex, out state);
                else if (candidate == 3) result = GetState13(userIndex, out state);
                else return false;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    internal sealed class OverlayForm : Form
    {
        private const int HOTKEY_SHOW = 1001;
        private const int HOTKEY_HIDE = 1002;
        private const int WM_HOTKEY = 0x0312;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        private TranslationStore translations;
        private readonly List<OverlayLabel> labels = new List<OverlayLabel>();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Font overlayFont = new Font("Microsoft YaHei UI", 12.0f,
            FontStyle.Bold, GraphicsUnit.Pixel);
        private bool processing;
        private bool manualTranslationPending;
        private int manualTranslationRequestId;
        private bool visibleTranslation;
        private bool shuttingDown;
        private OcrEngine ocr;
        private Keys showKey = Keys.F8;
        private Keys hideKey = Keys.F9;
        private bool showHotkeyRegistered;
        private bool hideHotkeyRegistered;
        private uint showModifiers;
        private uint hideModifiers;
        private GamepadShortcut showGamepadShortcut;
        private GamepadShortcut hideGamepadShortcut;
        private readonly GamepadShortcutLatch gamepadLatch = new GamepadShortcutLatch();
        private readonly uint[] gamepadStates = new uint[4];
        private readonly System.Windows.Forms.Timer gamepadTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer continuousTranslationTimer = new System.Windows.Forms.Timer();
        private TranslationRangeMode translationRangeMode = TranslationRangeMode.Balanced;
        private bool continuousTranslationEnabled;
        private bool aiChatFloatingWindowEnabled;
        private int continuousTranslationMisses;
        private int continuousTranslationFailures;
        private DateTime continuousTranslationSuppressedUntilUtc = DateTime.MinValue;
        private Rectangle captureBounds;
        private Rectangle gameBounds;
        private DictionaryOnlyForm dictionaryEditor;
        private HotkeyForm hotkeyEditor;
        private OfflineChatForm chatTranslator;
        private MainPanelForm mainPanel;
        private RegisteredWaitHandle activationWait;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        public OverlayForm()
        {
            translations = new TranslationStore(Path.Combine(baseDir, "枫语幕词库.tsv"));
            translations.Load();
            // The international client and the dictionary use English source text.
            // Pin OCR to English first so a Chinese Windows profile does not misclassify Latin glyphs.
            ocr = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if (ocr == null) ocr = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocr == null) throw new InvalidOperationException("Windows OCR 不可用，请在系统语言设置中安装英语 OCR。 ");

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            DoubleBuffered = true;

            LoadSettings();
            gamepadTimer.Interval = 25;
            gamepadTimer.Tick += delegate { PollGamepadShortcuts(); };
            continuousTranslationTimer.Interval = 260;
            continuousTranslationTimer.Tick += async delegate { await PollContinuousTranslationAsync(); };
            if (Program.Benchmark && Program.BenchmarkRangeMode >= 1 && Program.BenchmarkRangeMode <= 3)
                translationRangeMode = (TranslationRangeMode)Program.BenchmarkRangeMode;

            BuildTray();
            Shown += async delegate {
                if (!String.IsNullOrEmpty(Program.BenchmarkChatImagePath))
                {
                    await RunChatStyleBenchmarkAsync(Program.BenchmarkChatImagePath);
                    Close();
                    return;
                }
                if (Program.BenchmarkHotkeyToggle)
                {
                    await RunHotkeyToggleBenchmarkAsync();
                    Close();
                    return;
                }
                SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) |
                    WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                bool h1 = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                bool h2 = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                if (!h1 || !h2)
                {
                    await Task.Delay(120);
                    if (!h1) h1 = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                    if (!h2) h2 = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                }
                showHotkeyRegistered = h1;
                hideHotkeyRegistered = h2;
                UpdateGamepadPolling();
                UpdateContinuousTranslationPolling();
                tray.ShowBalloonTip(2500, "枫语幕已启动",
                    "常规/任务共 " + translations.Count + " 条，任务 " + translations.TaskCount + " 个/文本 " +
                    translations.TaskTextCount + " 条，图标指纹 " + translations.IconCount + " 条，已载入内存。" +
                    HotkeyText(showKey, showModifiers) + " 翻译开关，" +
                    HotkeyText(hideKey, hideModifiers) + " 自动对齐聊天框。手柄：翻译 " +
                    showGamepadShortcut + "，缩回 " + hideGamepadShortcut + "。" +
                    "双击托盘图标打开主界面；右键可重新显示AI翻译悬浮窗。" +
                    ((!h1 || !h2) ? "（有快捷键注册失败）" : ""), ToolTipIcon.Info);
                ShowPendingApplicationUpdateResult();
                BeginSafeWarmup();
                if (!Program.Benchmark) BeginActivationRecovery();
                if (!Program.Benchmark) ShowMainPanel();
                if (Program.Benchmark)
                {
                    await Task.Delay(350);
                    if (Program.BenchmarkContinuousCycles > 0)
                    {
                        continuousTranslationEnabled = true;
                        List<int> elapsedValues = new List<int>();
                        List<int> probeValues = new List<int>();
                        Stopwatch loop = Stopwatch.StartNew();
                        for (int cycle = 0; cycle < Program.BenchmarkContinuousCycles; cycle++)
                        {
                            await ShowTranslationAsync(true);
                            ReadContinuousBenchmarkCycle(elapsedValues, probeValues);
                            if (cycle + 1 < Program.BenchmarkContinuousCycles)
                                await Task.Delay(ContinuousTranslationPolicy.NextInterval(
                                    visibleTranslation, continuousTranslationMisses,
                                    continuousTranslationFailures));
                        }
                        loop.Stop();
                        WriteContinuousBenchmarkSummary(elapsedValues, probeValues,
                            loop.ElapsedMilliseconds);
                    }
                    else await ShowTranslationAsync();
                    Close();
                }
            };
        }

        private async Task RunChatStyleBenchmarkAsync(string imagePath)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ChatCaptureFrame frame = await AnalyzeChatImageAsync(imagePath);
            stopwatch.Stop();
            StringBuilder report = new StringBuilder();
            report.Append("elapsed_ms=").Append(stopwatch.ElapsedMilliseconds)
                .Append(" lines=").Append(frame.Lines.Count).AppendLine();
            foreach (ChatCaptureLine line in frame.Lines)
                report.Append(line.Style.HasBackground ? "BAND" : "TEXT")
                    .Append('/').Append(line.Style.Kind)
                    .Append(" fg=").Append(line.Style.ForeColor.R).Append(',')
                    .Append(line.Style.ForeColor.G).Append(',').Append(line.Style.ForeColor.B)
                    .Append(" bg=").Append(line.Style.BackColor.R).Append(',')
                    .Append(line.Style.BackColor.G).Append(',').Append(line.Style.BackColor.B)
                    .Append(" | ").AppendLine(line.Text);
            foreach (string parsed in OfflineChatForm.ParseChatLines(frame.PhysicalLineText()))
                report.Append("PARSED | ").AppendLine(parsed);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "chat_style_benchmark.txt"), report.ToString(), new UTF8Encoding(false));
        }

        private async Task RunHotkeyToggleBenchmarkAsync()
        {
            string reportPath = Path.Combine(baseDir, "hotkey_toggle_test.txt");
            try
            {
                await ShowTranslationFromHotkeyAsync();
                if (!visibleTranslation || labels.Count == 0)
                    throw new InvalidOperationException("第一次F8未用新截图显示翻译");

                await ShowTranslationFromHotkeyAsync();
                if (visibleTranslation || labels.Count != 0)
                    throw new InvalidOperationException("第二次F8未关闭当前翻译");

                Task opening = ShowTranslationFromHotkeyAsync();
                Task cancel = ShowTranslationFromHotkeyAsync();
                await cancel;
                await opening;
                if (visibleTranslation || labels.Count != 0)
                    throw new InvalidOperationException("OCR进行中再次按F8后，迟到结果仍重新出现");

                await ShowTranslationFromHotkeyAsync();
                if (!visibleTranslation || labels.Count == 0)
                    throw new InvalidOperationException("取消后再次按F8未重新截图翻译");

                File.WriteAllText(reportPath,
                    "通过：首次显示、再次关闭、处理中取消、取消后重新截图" +
                    Environment.NewLine + "最终命中=" + labels.Count,
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 3;
                File.WriteAllText(reportPath, "失败：" + ex, Encoding.UTF8);
            }
        }

        private void ReadContinuousBenchmarkCycle(List<int> elapsedValues,
            List<int> probeValues)
        {
            string path = Path.Combine(baseDir, "last_run.txt");
            if (!File.Exists(path)) return;
            string value = File.ReadAllText(path, Encoding.UTF8);
            Match elapsed = Regex.Match(value, @"耗时毫秒=(\d+)");
            Match probe = Regex.Match(value, @"快速探测:(\d+)ms");
            if (elapsed.Success) elapsedValues.Add(Int32.Parse(elapsed.Groups[1].Value,
                CultureInfo.InvariantCulture));
            if (probe.Success) probeValues.Add(Int32.Parse(probe.Groups[1].Value,
                CultureInfo.InvariantCulture));
        }

        private void WriteContinuousBenchmarkSummary(List<int> elapsedValues,
            List<int> probeValues, long wallMilliseconds)
        {
            Func<List<int>, int> average = delegate(List<int> values) {
                if (values.Count == 0) return 0;
                long total = 0;
                foreach (int value in values) total += value;
                return (int)Math.Round((double)total / values.Count);
            };
            Func<List<int>, int> maximum = delegate(List<int> values) {
                int result = 0;
                foreach (int value in values) result = Math.Max(result, value);
                return result;
            };
            File.WriteAllText(Path.Combine(baseDir, "continuous_benchmark.txt"),
                "周期=" + Program.BenchmarkContinuousCycles + Environment.NewLine +
                "已记录周期=" + elapsedValues.Count + Environment.NewLine +
                "平均响应毫秒=" + average(elapsedValues) + Environment.NewLine +
                "最大响应毫秒=" + maximum(elapsedValues) + Environment.NewLine +
                "平均快速探测毫秒=" + average(probeValues) + Environment.NewLine +
                "最大快速探测毫秒=" + maximum(probeValues) + Environment.NewLine +
                "最终轮询毫秒=" + ContinuousTranslationPolicy.NextInterval(visibleTranslation,
                    continuousTranslationMisses, continuousTranslationFailures) + Environment.NewLine +
                "总墙钟毫秒=" + wallMilliseconds + Environment.NewLine,
                Encoding.UTF8);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private void LoadSettings()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
            {
                Keys parsed;
                if (key == null) return;
                string sk = Convert.ToString(key.GetValue("ShowKey", "F8"));
                string hk = Convert.ToString(key.GetValue("HideKey", "F9"));
                if (Enum.TryParse<Keys>(sk, true, out parsed)) showKey = parsed;
                if (Enum.TryParse<Keys>(hk, true, out parsed)) hideKey = parsed;
                showModifiers = ParseModifiers(Convert.ToString(key.GetValue("ShowModifiers", "")));
                hideModifiers = ParseModifiers(Convert.ToString(key.GetValue("HideModifiers", "")));
                showGamepadShortcut = GamepadShortcut.Parse(Convert.ToString(key.GetValue("GamepadShowShortcut", "")));
                hideGamepadShortcut = GamepadShortcut.Parse(Convert.ToString(key.GetValue("GamepadHideShortcut", "")));
                int savedRange = Convert.ToInt32(key.GetValue("TranslationRangeMode", 2), CultureInfo.InvariantCulture);
                if (savedRange >= (int)TranslationRangeMode.Maximum && savedRange <= (int)TranslationRangeMode.Minimum)
                    translationRangeMode = (TranslationRangeMode)savedRange;
                continuousTranslationEnabled = Convert.ToInt32(
                    key.GetValue("ContinuousTranslationEnabled", 0), CultureInfo.InvariantCulture) != 0;
                aiChatFloatingWindowEnabled = Convert.ToInt32(
                    key.GetValue("AiChatFloatingWindowEnabled",
                        key.GetValue("IndependentWindowEnabled", 1)),
                    CultureInfo.InvariantCulture) != 0;
            }
        }

        internal static uint ParseModifiers(string value)
        {
            uint result = 0;
            foreach (string part in value.Split('+'))
            {
                string m = part.Trim().ToUpperInvariant();
                if (m == "ALT") result |= 0x0001;
                else if (m == "CTRL" || m == "CONTROL") result |= 0x0002;
                else if (m == "SHIFT") result |= 0x0004;
                else if (m == "WIN" || m == "WINDOWS") result |= 0x0008;
            }
            return result;
        }

        internal static string ModifiersText(uint modifiers)
        {
            List<string> items = new List<string>();
            if ((modifiers & 0x0002) != 0) items.Add("CTRL");
            if ((modifiers & 0x0001) != 0) items.Add("ALT");
            if ((modifiers & 0x0004) != 0) items.Add("SHIFT");
            if ((modifiers & 0x0008) != 0) items.Add("WIN");
            return String.Join("+", items.ToArray());
        }

        private string HotkeyText(Keys key, uint modifiers)
        {
            string p = ModifiersText(modifiers);
            if (p.Length > 0) p += "+";
            return p + key;
        }

        internal Keys ShowKey { get { return showKey; } }
        internal Keys HideKey { get { return hideKey; } }
        internal uint ShowModifiers { get { return showModifiers; } }
        internal uint HideModifiers { get { return hideModifiers; } }
        internal GamepadShortcut ShowGamepadShortcut { get { return showGamepadShortcut; } }
        internal GamepadShortcut HideGamepadShortcut { get { return hideGamepadShortcut; } }
        internal int DictionaryEntryCount { get { return translations.Count; } }
        internal int TaskEntryCount { get { return translations.TaskTextCount; } }
        internal string ShowHotkeyDescription { get { return HotkeyText(showKey, showModifiers); } }
        internal string HideHotkeyDescription { get { return HotkeyText(hideKey, hideModifiers); } }
        internal string HotkeyRegistrationStatus
        {
            get { return showHotkeyRegistered && hideHotkeyRegistered ? "" : "键盘快捷键注册失败，请更换冲突组合"; }
        }
        internal string ShowGamepadShortcutDescription { get { return showGamepadShortcut.ToString(); } }
        internal string HideGamepadShortcutDescription { get { return hideGamepadShortcut.ToString(); } }
        internal TranslationRangeMode TranslationRangeMode { get { return translationRangeMode; } }
        internal bool ContinuousTranslationEnabled { get { return continuousTranslationEnabled; } }
        internal bool AiChatFloatingWindowEnabled { get { return aiChatFloatingWindowEnabled; } }

        internal void ApplyTranslationRangeMode(TranslationRangeMode mode)
        {
            if (mode < TranslationRangeMode.Maximum || mode > TranslationRangeMode.Minimum)
                mode = TranslationRangeMode.Maximum;
            translationRangeMode = mode;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                key.SetValue("TranslationRangeMode", (int)mode, RegistryValueKind.DWord);
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        internal void ApplyContinuousTranslation(bool enabled)
        {
            continuousTranslationEnabled = enabled;
            continuousTranslationMisses = 0;
            continuousTranslationFailures = 0;
            continuousTranslationSuppressedUntilUtc = DateTime.MinValue;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                key.SetValue("ContinuousTranslationEnabled", enabled ? 1 : 0,
                    RegistryValueKind.DWord);
            UpdateContinuousTranslationPolling();
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        internal void ApplyAiChatFloatingWindow(bool enabled)
        {
            aiChatFloatingWindowEnabled = enabled;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                key.SetValue("AiChatFloatingWindowEnabled", enabled ? 1 : 0,
                    RegistryValueKind.DWord);
            if (chatTranslator != null && !chatTranslator.IsDisposed)
                chatTranslator.ApplyFloatingWindow(enabled);
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        private void ShowCurrentTranslations()
        {
            Invalidate();
        }

        internal bool ApplyHotkeys(Keys newShowKey, uint newShowModifiers, Keys newHideKey, uint newHideModifiers)
        {
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            bool ok1 = RegisterHotKey(Handle, HOTKEY_SHOW, newShowModifiers, (uint)newShowKey);
            bool ok2 = RegisterHotKey(Handle, HOTKEY_HIDE, newHideModifiers, (uint)newHideKey);
            if (!ok1 || !ok2)
            {
                UnregisterHotKey(Handle, HOTKEY_SHOW);
                UnregisterHotKey(Handle, HOTKEY_HIDE);
                showHotkeyRegistered = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                hideHotkeyRegistered = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                MessageBox.Show("快捷键被其他程序占用，已恢复原来的可用设置。", "快捷键设置");
                if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
                return false;
            }
            showKey = newShowKey; showModifiers = newShowModifiers;
            hideKey = newHideKey; hideModifiers = newHideModifiers;
            showHotkeyRegistered = true;
            hideHotkeyRegistered = true;
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
            return true;
        }

        internal void ApplyGamepadShortcuts(GamepadShortcut newShowShortcut,
            GamepadShortcut newHideShortcut)
        {
            showGamepadShortcut = newShowShortcut;
            hideGamepadShortcut = newHideShortcut;
            gamepadLatch.Reset();
            UpdateGamepadPolling();
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        private void UpdateGamepadPolling()
        {
            if (Program.Benchmark || Program.BenchmarkUi ||
                (!showGamepadShortcut.IsBound && !hideGamepadShortcut.IsBound))
                gamepadTimer.Stop();
            else if (!gamepadTimer.Enabled)
                gamepadTimer.Start();
        }

        private void UpdateContinuousTranslationPolling()
        {
            if (Program.Benchmark || Program.BenchmarkUi || !continuousTranslationEnabled)
                continuousTranslationTimer.Stop();
            else
            {
                continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(
                    visibleTranslation, continuousTranslationMisses, continuousTranslationFailures);
                if (!continuousTranslationTimer.Enabled) continuousTranslationTimer.Start();
            }
        }

        private async Task PollContinuousTranslationAsync()
        {
            if (!continuousTranslationEnabled || processing || manualTranslationPending ||
                DateTime.UtcNow < continuousTranslationSuppressedUntilUtc) return;
            if ((mainPanel != null && !mainPanel.IsDisposed && mainPanel.Visible) ||
                (hotkeyEditor != null && !hotkeyEditor.IsDisposed && hotkeyEditor.Visible) ||
                (dictionaryEditor != null && !dictionaryEditor.IsDisposed && dictionaryEditor.Visible)) return;
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || IsIconic(foreground)) return;
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            if (processId == (uint)Process.GetCurrentProcess().Id) return;
            await ShowTranslationAsync(true);
        }

        private void PollGamepadShortcuts()
        {
            for (int i = 0; i < gamepadStates.Length; i++)
                gamepadStates[i] = XInputReader.ReadButtons(i);
            GamepadShortcutAction action = gamepadLatch.Evaluate(gamepadStates,
                showGamepadShortcut, hideGamepadShortcut);
            if (action == GamepadShortcutAction.Show)
            {
                Task ignored = ShowTranslationFromHotkeyAsync();
            }
            else if (action == GamepadShortcutAction.Hide)
            {
                HideTranslation();
            }
        }

        internal void ReloadDictionary()
        {
            try
            {
                TranslationStore replacement = new TranslationStore(Path.Combine(baseDir, "枫语幕词库.tsv"));
                replacement.Load();
                translations = replacement;
                tray.ShowBalloonTip(1200, "词库已重新载入",
                    translations.Count + " 条；AI知识将在打开AI功能时按需同步。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(2600, "词库重新载入失败",
                    ex.Message + "；已继续使用上一次的稳定索引。", ToolTipIcon.Error);
            }
        }

        private void BeginSafeWarmup()
        {
            // The dictionary index, OCR engine and overlay handle are already ready here.
            // Warm only disposable drawing resources; failure leaves the original F8 path intact.
            try
            {
                BeginInvoke((MethodInvoker)delegate {
                    try
                    {
                        using (Bitmap bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb))
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        using (GraphicsPath path = RoundedRect(new RectangleF(0, 0, 2, 2), 0.5f))
                        {
                            graphics.MeasureString("枫语幕", overlayFont);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        internal void SyncAiKnowledge(bool showNotice)
        {
            try
            {
                string aiRoot = Path.Combine(baseDir, "模型");
                bool hasModel = Directory.Exists(aiRoot) &&
                    Directory.GetFiles(aiRoot, "Qwen3-*.gguf", SearchOption.AllDirectories).Length > 0;
                if (!hasModel)
                {
                    if (showNotice) tray.ShowBalloonTip(1800, "AI词库同步", "尚未安装AI模型；正式词库已经载入。", ToolTipIcon.Info);
                    return;
                }
                KnowledgeInitializationResult result = MapleKnowledgeInitializer.Initialize(aiRoot,
                    Path.Combine(baseDir, "枫语幕词库.tsv"));
                if (chatTranslator != null && !chatTranslator.IsDisposed) chatTranslator.SyncKnowledge();
                if (showNotice) tray.ShowBalloonTip(2200, "AI词库同步完成",
                    result.Entries + " 条、" + result.Categories + " 类" +
                    (result.Changed ? "，已切换到新词库。" : "，当前已经是最新版。"), ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                if (showNotice) MessageBox.Show("AI词库同步失败：\n\n" + ex.Message, "枫语幕");
            }
        }

        internal void ShowDictionaryEditor()
        {
            HideTranslation();
            if (dictionaryEditor == null || dictionaryEditor.IsDisposed) dictionaryEditor = new DictionaryOnlyForm(this, baseDir);
            dictionaryEditor.Show();
            dictionaryEditor.Activate();
        }

        internal void ShowHotkeyEditor()
        {
            HideTranslation();
            if (hotkeyEditor == null || hotkeyEditor.IsDisposed) hotkeyEditor = new HotkeyForm(this);
            hotkeyEditor.Show();
            hotkeyEditor.Activate();
        }

        internal void ShowChatTranslator()
        {
            HideTranslation();
            try
            {
                // Constructing this form performs the AI glossary/knowledge initialization.
                // Keeping it here makes AI fully lazy and isolates failures from F8 translation.
                if (chatTranslator == null || chatTranslator.IsDisposed)
                    chatTranslator = new OfflineChatForm(this, baseDir);
                chatTranslator.ApplyFloatingWindow(aiChatFloatingWindowEnabled);
                chatTranslator.Show();
                chatTranslator.Activate();
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(3000, "AI功能打开失败",
                    ex.Message + "；F8词库翻译仍可继续使用。", ToolTipIcon.Error);
            }
        }

        internal async Task InstallLatestApplicationAsync(Button button)
        {
            if (button == null) return;
            string originalText = button.Text;
            button.Enabled = false;
            button.Text = "下载并校验中…";
            try
            {
                PreparedApplicationUpdate update =
                    await ApplicationUpdater.PrepareLatestAsync(baseDir);
                if (!update.HasChanges)
                {
                    MessageBox.Show("当前三个安装文件已经和 GitHub 最新正式包完全一致，不需要替换。",
                        "枫语幕一键更新");
                    return;
                }
                button.Text = "正在安全重启…";
                update.StartInstaller();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("更新没有动到当前版本。\n\n" + ex.Message +
                    "\n\n可以稍后重试，也可以继续正常使用。", "枫语幕一键更新");
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Enabled = true;
                    button.Text = originalText;
                }
            }
        }

        private void ShowPendingApplicationUpdateResult()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                {
                    object stored = key.GetValue("ApplicationUpdateSuccess", null);
                    if (stored == null) return;
                    bool success = Convert.ToInt32(stored, CultureInfo.InvariantCulture) != 0;
                    string message = Convert.ToString(key.GetValue("ApplicationUpdateMessage", ""));
                    string detail = Convert.ToString(key.GetValue("ApplicationUpdateDetail", ""));
                    key.DeleteValue("ApplicationUpdateSuccess", false);
                    key.DeleteValue("ApplicationUpdateMessage", false);
                    key.DeleteValue("ApplicationUpdateDetail", false);
                    string body = message;
                    if (!success && detail.Length > 0)
                        body += "\n" + (detail.Length > 180 ? detail.Substring(0, 180) : detail);
                    tray.ShowBalloonTip(success ? 3200 : 6000,
                        success ? "枫语幕更新完成" : "枫语幕更新未完成",
                        body, success ? ToolTipIcon.Info : ToolTipIcon.Error);
                }
            }
            catch { }
        }

        internal bool RestoreAiChatFloatingWindowFromTray()
        {
            if (chatTranslator == null || chatTranslator.IsDisposed ||
                !chatTranslator.IsLiveTranslationRunning)
            {
                tray.ShowBalloonTip(1800, "AI翻译悬浮窗",
                    "实时翻译还没开始。先打开“AI实时聊天翻译”，再点开始。", ToolTipIcon.Info);
                return false;
            }
            chatTranslator.RestoreFloatingWindowFromTray();
            return true;
        }

        private void BuildTray()
        {
            tray.Icon = Program.AppIcon;
            tray.Text = "枫语幕 v3.0";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem main = new ToolStripMenuItem("打开主界面");
            main.Font = new Font(main.Font, FontStyle.Bold);
            main.Click += delegate { ShowMainPanel(); };
            ToolStripMenuItem dictionary = new ToolStripMenuItem("打开并更改词库");
            dictionary.Click += delegate { ShowDictionaryEditor(); };
            ToolStripMenuItem hotkeys = new ToolStripMenuItem("更改快捷键");
            hotkeys.Click += delegate { ShowHotkeyEditor(); };
            ToolStripMenuItem chat = new ToolStripMenuItem("AI实时聊天翻译");
            chat.Click += delegate { ShowChatTranslator(); };
            ToolStripMenuItem restoreChat = new ToolStripMenuItem("重新显示AI翻译悬浮窗");
            restoreChat.Click += delegate { RestoreAiChatFloatingWindowFromTray(); };
            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Close(); };
            menu.Items.Add(main);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(dictionary);
            menu.Items.Add(hotkeys);
            menu.Items.Add(chat);
            menu.Items.Add(restoreChat);
            menu.Items.Add(exit);
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowMainPanel(); };
        }

        private void BeginActivationRecovery()
        {
            if (Program.ActivationEvent == null || activationWait != null) return;
            activationWait = ThreadPool.RegisterWaitForSingleObject(Program.ActivationEvent,
                delegate
                {
                    try
                    {
                        if (IsDisposed || Disposing || !IsHandleCreated) return;
                        BeginInvoke((MethodInvoker)delegate { RestoreTrayIcon(true); });
                    }
                    catch (InvalidOperationException) { }
                }, null, Timeout.Infinite, false);
        }

        private void RestoreTrayIcon(bool openMainPanel)
        {
            try
            {
                tray.Visible = false;
                tray.Icon = Program.AppIcon;
                tray.Text = "枫语幕 v3.0（内存待机）";
                tray.Visible = true;
            }
            catch (ObjectDisposedException) { return; }
            if (openMainPanel) ShowMainPanel();
        }

        internal void ShowMainPanel()
        {
            HideTranslation();
            if (mainPanel == null || mainPanel.IsDisposed) mainPanel = new MainPanelForm(this);
            mainPanel.RefreshStatus();
            mainPanel.Show();
            mainPanel.Activate();
        }

        internal void HideMainPanel()
        {
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.Hide();
        }

        private async Task ShowTranslationFromHotkeyAsync()
        {
            // F8 is a true toggle. A second press also cancels an OCR pass that has not yet
            // published results, so a late completion cannot make the overlay reappear.
            if (manualTranslationPending || visibleTranslation)
            {
                HideTranslation();
                return;
            }
            int requestId = ++manualTranslationRequestId;
            manualTranslationPending = true;
            bool panelWasVisible = mainPanel != null && !mainPanel.IsDisposed && mainPanel.Visible;
            if (panelWasVisible)
            {
                mainPanel.Hide();
            }
            bool chatWindowWasVisible = false;
            if (chatTranslator != null && !chatTranslator.IsDisposed)
                chatWindowWasVisible = await chatTranslator.PrepareForScreenshotCaptureAsync();
            if (panelWasVisible || chatWindowWasVisible) await Task.Delay(140);
            while (processing && requestId == manualTranslationRequestId && !shuttingDown)
                await Task.Delay(15);
            if (requestId != manualTranslationRequestId || shuttingDown) return;
            try { await ShowTranslationAsync(false, requestId); }
            finally
            {
                if (requestId == manualTranslationRequestId)
                {
                    manualTranslationPending = false;
                    if (chatTranslator != null && !chatTranslator.IsDisposed)
                        chatTranslator.RestoreAfterScreenshotCapture();
                }
            }
        }

        private async Task AutoAlignChatRegionFromHotkeyAsync()
        {
            if (ChatRegionSettings.GetOrigin() == ChatRegionOrigin.Manual)
            {
                tray.ShowBalloonTip(1800, "聊天框位置未改变",
                    "当前使用手动框选区域；如需修改，请在AI窗口点击“框选/调整游戏聊天区”。",
                    ToolTipIcon.Info);
                return;
            }

            bool panelWasVisible = mainPanel != null && !mainPanel.IsDisposed && mainPanel.Visible;
            if (panelWasVisible) mainPanel.Hide();
            bool chatWindowWasVisible = false;
            if (chatTranslator != null && !chatTranslator.IsDisposed)
                chatWindowWasVisible = await chatTranslator.PrepareForScreenshotCaptureAsync();
            bool ownsProcessing = false;
            try
            {
                if (panelWasVisible || chatWindowWasVisible) await Task.Delay(140);
                while (processing && !shuttingDown) await Task.Delay(15);
                if (shuttingDown) return;
                processing = true;
                ownsProcessing = true;
                Rectangle screen = GetForegroundCaptureBounds();
                using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height,
                    PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size,
                            CopyPixelOperation.SourceCopy);
                    float scale;
                    using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false,
                        screen.Width >= 1900 ? 2200.0f : 2000.0f))
                    {
                        OcrResult result = await RecognizeAsync(prepared);
                        List<RectangleF> chatLineBounds = FindPlayerChatLineBounds(result,
                            scale, screen);
                        Rectangle region = ChatRegionSettings.SaveAutomatic(
                            ChatRegionDetector.Detect(chatLineBounds, screen), screen);
                        if (chatTranslator != null && !chatTranslator.IsDisposed)
                            chatTranslator.UpdateChatRegion(region, ChatRegionOrigin.Automatic);
                        tray.ShowBalloonTip(1800, "聊天框已自动对齐",
                            region.Width + "×" + region.Height +
                            (chatLineBounds.Count > 0 ? "，已按当前聊天行定位。" :
                                "，未发现可靠聊天行，已使用经典布局。"), ToolTipIcon.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(2600, "自动对齐失败", ex.Message, ToolTipIcon.Error);
            }
            finally
            {
                if (ownsProcessing) processing = false;
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.RestoreAfterScreenshotCapture();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_SHOW) { Task ignored = ShowTranslationFromHotkeyAsync(); }
                else if (id == HOTKEY_HIDE) { Task ignored = AutoAlignChatRegionFromHotkeyAsync(); }
            }
            else if (m.Msg == TaskbarCreatedMessage)
            {
                // Explorer recreates the taskbar without preserving NotifyIcon registrations.
                // Re-register immediately so the process can never become tray-less and hidden.
                RestoreTrayIcon(false);
            }
            base.WndProc(ref m);
        }

        internal void HideTranslation()
        {
            if (manualTranslationPending)
            {
                manualTranslationPending = false;
                manualTranslationRequestId++;
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.RestoreAfterScreenshotCapture();
            }
            visibleTranslation = false;
            labels.Clear();
            ShowCurrentTranslations();
            if (continuousTranslationEnabled)
                continuousTranslationSuppressedUntilUtc = DateTime.UtcNow.AddSeconds(3);
            tray.Text = "枫语幕 v3.0（低配置优化）";
        }

        private Task ShowTranslationAsync()
        {
            return ShowTranslationAsync(false, 0);
        }

        private async Task ShowTranslationAsync(bool automatic)
        {
            await ShowTranslationAsync(automatic, 0);
        }

        private bool RecognitionWasCancelled(bool automatic, int requestId)
        {
            return automatic ? manualTranslationPending :
                requestId != 0 && (requestId != manualTranslationRequestId ||
                    !manualTranslationPending);
        }

        private async Task ShowTranslationAsync(bool automatic, int requestId)
        {
            if (processing) return;
            processing = true;
            IntPtr automaticSourceWindow = automatic ? GetForegroundWindow() : IntPtr.Zero;
            bool restoreExistingOverlay = automatic && visibleTranslation && labels.Count > 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            long captureDuration = 0;
            long probePassDuration = 0;
            long mainPassDuration = 0;
            long hoverPassDuration = 0;
            long panelPassDuration = 0;
            long fallbackPassDuration = 0;
            long finalizeDuration = 0;
            string recognitionPlanText = "范围最大（兼容路径）";
            // Screen results are never reused. Every F8 activation starts from an empty overlay
            // and a fresh capture; a second press cancels or hides it.
            visibleTranslation = false;
            if (!restoreExistingOverlay) labels.Clear();
            Invalidate();
            // Invalidate only queues a repaint. Force that repaint through the desktop compositor
            // before CopyFromScreen so a second F8 can never OCR its own previous black labels.
            Update();
            if (!Program.Benchmark)
            {
                try { DwmFlush(); }
                catch { }
            }
            string benchmarkOcrText = "";
            string recognizedScenes = "普通界面";
            try
            {
                Rectangle screen;
                if (Program.Benchmark && !String.IsNullOrEmpty(Program.BenchmarkImagePath) && File.Exists(Program.BenchmarkImagePath))
                {
                    using (Image source = Image.FromFile(Program.BenchmarkImagePath))
                        screen = new Rectangle(0, 0, source.Width, source.Height);
                }
                else screen = Program.Benchmark ? new Rectangle(0, 0, 1280, 720) : GetForegroundCaptureBounds();
                captureBounds = screen;
                gameBounds = screen;
                long captureStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        if (Program.Benchmark)
                        {
                            if (!String.IsNullOrEmpty(Program.BenchmarkImagePath) && File.Exists(Program.BenchmarkImagePath))
                            {
                                using (Image source = Image.FromFile(Program.BenchmarkImagePath))
                                    g.DrawImage(source, new Rectangle(0, 0, screen.Width, screen.Height),
                                        0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                            }
                            else if (!String.IsNullOrEmpty(Program.BenchmarkText))
                            {
                                g.Clear(Color.FromArgb(28, 28, 32));
                                using (Font titleFont = new Font("Arial", 15, FontStyle.Bold))
                                using (Font textFont = new Font("Arial", 14, FontStyle.Regular))
                                {
                                    if (!Program.BenchmarkUi)
                                        g.DrawString("Quest   Accept", titleFont, Brushes.White, 40, 35);
                                    g.DrawString(Program.BenchmarkUi ? Program.BenchmarkText.Replace("|", Environment.NewLine) : Program.BenchmarkText,
                                        textFont, Brushes.White,
                                        Program.BenchmarkUi ? new RectangleF(40, 78, 700, 600) : new RectangleF(40, 78, 430, 220));
                                }
                            }
                            else if (!String.IsNullOrEmpty(Program.BenchmarkIconPath) && File.Exists(Program.BenchmarkIconPath))
                            {
                                g.Clear(Color.FromArgb(18, 18, 20));
                                using (Image icon = Image.FromFile(Program.BenchmarkIconPath))
                                    g.DrawImage(icon, new Rectangle(40, 40, 32, 32));
                                using (Font f = new Font("Arial", 14, FontStyle.Regular))
                                    g.DrawString("Blue Potlon", f, Brushes.White, 82, 47);
                            }
                            else
                            {
                                g.Clear(Color.White);
                                using (Font f = new Font("Arial", 14, FontStyle.Regular))
                                    g.DrawString("MapleStory Classic World   Henesys   Orange Mushroom   Quest Complete", f, Brushes.Black, 40, 40);
                            }
                        }
                        else g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
                    }
                    // Continuous mode keeps the old labels visible while the clean screenshot is
                    // being recognized. They disappear only for the compositor flush above.
                    if (restoreExistingOverlay)
                    {
                        visibleTranslation = true;
                        ShowCurrentTranslations();
                        Update();
                    }
                    if (RecognitionWasCancelled(automatic, requestId)) return;
                    if (Program.Benchmark) captureDuration = stopwatch.ElapsedMilliseconds - captureStarted;
                    CharacterStatVisualLayout visualCharacter = ClassicSceneVision.FindCharacterStats(bitmap);
                    bool useSceneProbe = ContinuousTranslationPolicy.ShouldRunProbe(automatic,
                        restoreExistingOverlay, Program.BenchmarkSceneProbe);
                    if (useSceneProbe && visualCharacter == null)
                    {
                        long probeStarted = stopwatch.ElapsedMilliseconds;
                        float probeScale;
                        using (Bitmap probePrepared = PrepareForOcr(bitmap, out probeScale, false, 1200.0f))
                        {
                            OcrResult probeResult = await RecognizeAsync(probePrepared);
                            if (RecognitionWasCancelled(automatic, requestId)) return;
                            SceneSnapshot probeSnapshot = SceneClassifier.Analyze(probeResult,
                                translations, true);
                            recognizedScenes = "快速探测[" + probeSnapshot.Describe() + "]";
                            if (Program.Benchmark)
                                benchmarkOcrText = "快速探测=" + probeResult.Text;
                            probePassDuration = stopwatch.ElapsedMilliseconds - probeStarted;
                            if (!ContinuousTranslationPolicy.ShouldTranslate(probeSnapshot, false))
                            {
                                if (automatic)
                                {
                                    continuousTranslationMisses++;
                                    continuousTranslationFailures = 0;
                                    if (!restoreExistingOverlay || continuousTranslationMisses >= 2)
                                    {
                                        visibleTranslation = false;
                                        labels.Clear();
                                        ShowCurrentTranslations();
                                    }
                                    continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(
                                        false, continuousTranslationMisses, continuousTranslationFailures);
                                }
                                if (Program.Benchmark)
                                    WriteBenchmarkResult(stopwatch, captureDuration, probePassDuration,
                                        mainPassDuration, hoverPassDuration, panelPassDuration,
                                        fallbackPassDuration, finalizeDuration, recognitionPlanText,
                                        recognizedScenes, benchmarkOcrText);
                                return;
                            }
                        }
                    }
                    else if (visualCharacter != null)
                        recognizedScenes = "快速探测[人物视觉:200/2]";
                    long mainPassStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                    float ocrScale;
                    // A 2200px first pass keeps classic-client tooltip and quest text legible.
                    // The indexed matchers below are now cheap enough that this remains well inside
                    // the two-second budget without falling back to repeated full-screen OCR passes.
                    using (Bitmap prepared = PrepareForOcr(bitmap, out ocrScale, false, screen.Width >= 1900 ? 2200.0f : 2000.0f))
                    {
                        OcrResult result = await RecognizeAsync(prepared);
                        if (RecognitionWasCancelled(automatic, requestId)) return;
                        // CHARACTER INFO uses a similar magenta row pattern to CHARACTER STAT.
                        // When its own title/pet anchors fall inside the visually detected panel,
                        // the colour detector must not project the full STR/DEX/stat grid over it.
                        if (visualCharacter != null && IsVisualCharacterInformationPanel(
                            visualCharacter, result, ocrScale)) visualCharacter = null;
                        SceneSnapshot sceneSnapshot = SceneClassifier.Analyze(result, translations, true);
                        string preciseScenes = sceneSnapshot.Describe();
                        if (visualCharacter != null) preciseScenes += ">人物视觉:200/2";
                        recognizedScenes = useSceneProbe
                            ? recognizedScenes + ">精确识别[" + preciseScenes + "]"
                            : preciseScenes;
                        if (automatic)
                        {
                            continuousTranslationMisses = 0;
                            continuousTranslationFailures = 0;
                            continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(
                                true, 0, 0);
                        }
                        List<OverlayLabel> next = BuildLabels(result, ocrScale, prepared);
                        if (Program.Benchmark) mainPassDuration = stopwatch.ElapsedMilliseconds - mainPassStarted;
                        if (visualCharacter != null)
                            AddCharacterStatVisualLayoutLabels(next, visualCharacter, screen);
                        benchmarkOcrText = result.Text;
                        if (Program.Benchmark)
                        {
                            StringBuilder benchmarkLines = new StringBuilder();
                            foreach (OcrLine benchmarkLine in result.Lines)
                            {
                                if (benchmarkLines.Length > 0) benchmarkLines.Append(" <> ");
                                benchmarkLines.Append(benchmarkLine.Text);
                            }
                            benchmarkOcrText += " || OCR行=" + benchmarkLines;
                        }
                        bool hasImageCapture = !String.IsNullOrEmpty(Program.BenchmarkImagePath) && File.Exists(Program.BenchmarkImagePath);
                        bool useHoverPass = !Program.Benchmark ||
                            (hasImageCapture && Program.BenchmarkCursor != System.Drawing.Point.Empty);
                        Rectangle hoverCapture = Rectangle.Empty;
                        System.Drawing.Point pointer = Program.Benchmark && Program.BenchmarkCursor != System.Drawing.Point.Empty
                            ? Program.BenchmarkCursor : Cursor.Position;
                        Rectangle chat = GetChatExclusionBounds();
                        Rectangle tooltipLocal = useHoverPass && !chat.Contains(pointer)
                            ? FindClassicTooltipCrop(bitmap, pointer, screen) : Rectangle.Empty;
                        bool usePriorityPlan = translationRangeMode != TranslationRangeMode.Maximum;
                        List<RecognitionTier> recognitionPlan = new List<RecognitionTier>();
                        List<PanelCropCandidate> plannedPanelTargets = new List<PanelCropCandidate>();
                        if (usePriorityPlan)
                        {
                            List<PanelCropCandidate> priorityCandidates =
                                FindPanelCandidates(result, ocrScale, screen, true);
                            if (visualCharacter != null)
                            {
                                Rectangle visualCrop = new Rectangle(screen.Left + visualCharacter.Crop.Left,
                                    screen.Top + visualCharacter.Crop.Top,
                                    visualCharacter.Crop.Width, visualCharacter.Crop.Height);
                                priorityCandidates.Insert(0, new PanelCropCandidate {
                                    Bounds = visualCrop, Score = 990, Kind = "character-visual",
                                    PriorityKind = RecognitionPriorityKind.OutsideDialogue,
                                    SourceTextHeight = 10.0f
                                });
                            }
                            if (!tooltipLocal.IsEmpty)
                            {
                                priorityCandidates.Insert(0, new PanelCropCandidate {
                                    Bounds = new Rectangle(screen.Left + tooltipLocal.Left,
                                        screen.Top + tooltipLocal.Top, tooltipLocal.Width, tooltipLocal.Height),
                                    Score = 1000, Kind = "cursor-detail",
                                    PriorityKind = RecognitionPriorityKind.Detail,
                                    SourceTextHeight = 11.0f
                                });
                            }
                            bool hasDetail = priorityCandidates.Exists(delegate(PanelCropCandidate value) {
                                return value.PriorityKind == RecognitionPriorityKind.Detail; });
                            bool hasDialogue = priorityCandidates.Exists(delegate(PanelCropCandidate value) {
                                return value.PriorityKind == RecognitionPriorityKind.Dialogue; });
                            bool hasOutsideDialogue = priorityCandidates.Exists(delegate(PanelCropCandidate value) {
                                return value.PriorityKind == RecognitionPriorityKind.OutsideDialogue; });
                            // The fourth tier means the current interface only when no higher
                            // priority panel exists. Adding it beside a character/shop/dialogue
                            // panel defeated the user's four-tier rule and kept HUD fragments.
                            bool hasCurrentInterface = RecognitionPriorityPlanner.ShouldUseCurrentInterface(
                                hasDetail, hasDialogue, hasOutsideDialogue, result.Lines.Count > 0);
                            int maximumTargets = translationRangeMode == TranslationRangeMode.Minimum ? 2 : 3;
                            recognitionPlan = RecognitionPriorityPlanner.Select(hasDetail, hasDialogue,
                                hasOutsideDialogue, hasCurrentInterface, maximumTargets);
                            plannedPanelTargets = SelectPriorityPanelTargets(priorityCandidates, recognitionPlan);
                            recognitionPlanText = RecognitionPriorityPlanner.Describe(recognitionPlan);
                        }
                        // Fixed stat help does not need another OCR pass. Add it immediately so
                        // a slower first pass cannot leave a shallow hover tooltip half translated.
                        if (visualCharacter != null && !tooltipLocal.IsEmpty)
                            AddCharacterStatHoverHelp(next, visualCharacter,
                                tooltipLocal, pointer, screen);
                        double hoverResourceLevel = usePriorityPlan
                            ? RecognitionPriorityPlanner.LevelFor(recognitionPlan, RecognitionPriorityKind.Detail) : 1.0;
                        bool allowHoverReview = !usePriorityPlan ||
                            (!tooltipLocal.IsEmpty && hoverResourceLevel > 0.0);
                        // A detected cursor tooltip is the highest-priority content. On a cold
                        // OCR start it must still receive its focused review instead of losing
                        // fields merely because the full-screen pass crossed the warm-run gate.
                        long hoverReviewDeadline = tooltipLocal.IsEmpty ? 780 : 1250;
                        if (useHoverPass && allowHoverReview &&
                            stopwatch.ElapsedMilliseconds < hoverReviewDeadline)
                        {
                            Rectangle hover = tooltipLocal.IsEmpty
                                ? Rectangle.Intersect(screen,
                                    new Rectangle(pointer.X - 500, pointer.Y - 360, 1000, 720))
                                : new Rectangle(screen.Left + tooltipLocal.Left,
                                    screen.Top + tooltipLocal.Top, tooltipLocal.Width, tooltipLocal.Height);
                            int minimumHoverHeight = tooltipLocal.IsEmpty ? 220 : 82;
                            if (!chat.Contains(pointer) && hover.Width >= 260 && hover.Height >= minimumHoverHeight &&
                                hover.Width * hover.Height < screen.Width * screen.Height * 0.90)
                            {
                                long hoverPassStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                                hoverCapture = hover;
                                Rectangle local = new Rectangle(hover.Left - screen.Left, hover.Top - screen.Top, hover.Width, hover.Height);
                                using (Bitmap hoverBitmap = bitmap.Clone(local, PixelFormat.Format32bppArgb))
                                {
                                    float hoverScale;
                                    float hoverTarget = usePriorityPlan
                                        ? RecognitionPriorityPlanner.TargetLongEdge(local, 11.0f,
                                            hoverResourceLevel, !tooltipLocal.IsEmpty)
                                        : (tooltipLocal.IsEmpty ? 1750.0f :
                                            Math.Min(1900.0f, Math.Max(1450.0f, hover.Width * 3.0f)));
                                    using (Bitmap hoverPrepared = tooltipLocal.IsEmpty
                                        ? PrepareForOcr(hoverBitmap, out hoverScale, false, hoverTarget)
                                        : PrepareTooltipForOcr(hoverBitmap, out hoverScale, hoverTarget))
                                    {
                                        captureBounds = hover;
                                        OcrResult hoverResult = await RecognizeAsync(hoverPrepared);
                                        if (RecognitionWasCancelled(automatic, requestId)) return;
                                        MergeLabels(next, BuildLabels(hoverResult, hoverScale, hoverPrepared));
                                        if (Program.Benchmark) benchmarkOcrText += " || 光标详情=" + hoverResult.Text;
                                    }
                                }
                                captureBounds = screen;
                                if (Program.Benchmark)
                                    hoverPassDuration += stopwatch.ElapsedMilliseconds - hoverPassStarted;
                            }
                        }
                        // Small classic UI labels (NAME/STR/DEX, quest objectives, etc.) are
                        // often only 9-12 pixels high. Once the first pass locates a panel header,
                        // re-read that panel alone at higher effective resolution instead of
                        // slowing every full-screen capture or guessing individual words.
                        if (stopwatch.ElapsedMilliseconds < 1120)
                        {
                            List<PanelCropCandidate> panelTargets = new List<PanelCropCandidate>();
                            if (usePriorityPlan) panelTargets.AddRange(plannedPanelTargets);
                            else
                            {
                                foreach (Rectangle legacyCrop in FindPanelCrops(result, ocrScale, screen))
                                    panelTargets.Add(new PanelCropCandidate { Bounds = legacyCrop,
                                        ResourceLevel = 1.0, SourceTextHeight = 10.0f,
                                        Kind = "legacy" });
                            }
                            Rectangle visualCharacterCrop = Rectangle.Empty;
                            if (!usePriorityPlan && visualCharacter != null)
                            {
                                visualCharacterCrop = new Rectangle(screen.Left + visualCharacter.Crop.Left,
                                    screen.Top + visualCharacter.Crop.Top,
                                    visualCharacter.Crop.Width, visualCharacter.Crop.Height);
                                bool alreadyPresent = false;
                                foreach (PanelCropCandidate existing in panelTargets)
                                {
                                    Rectangle overlap = Rectangle.Intersect(existing.Bounds, visualCharacterCrop);
                                    long smaller = Math.Min((long)existing.Bounds.Width * existing.Bounds.Height,
                                        (long)visualCharacterCrop.Width * visualCharacterCrop.Height);
                                    if (smaller > 0 && (long)overlap.Width * overlap.Height * 10 >= smaller * 6)
                                    { alreadyPresent = true; break; }
                                }
                                if (!alreadyPresent) panelTargets.Insert(0, new PanelCropCandidate {
                                    Bounds = visualCharacterCrop, ResourceLevel = 1.0,
                                    SourceTextHeight = 10.0f, Kind = "character-visual" });
                                while (panelTargets.Count > 2) panelTargets.RemoveAt(panelTargets.Count - 1);
                            }
                            foreach (PanelCropCandidate panelTarget in panelTargets)
                            {
                                if (stopwatch.ElapsedMilliseconds >= 1680) break;
                                Rectangle panelCrop = panelTarget.Bounds;
                                bool isVisualCharacterCrop = panelTarget.Kind == "character-visual" ||
                                    (!visualCharacterCrop.IsEmpty && panelCrop == visualCharacterCrop);
                                if (!isVisualCharacterCrop && IsCharacterPanelAlreadyCovered(panelCrop, next)) continue;
                                if (!hoverCapture.IsEmpty)
                                {
                                    Rectangle overlap = Rectangle.Intersect(hoverCapture, panelCrop);
                                    long smaller = Math.Min((long)hoverCapture.Width * hoverCapture.Height,
                                        (long)panelCrop.Width * panelCrop.Height);
                                    bool sameFocusedDetail = !usePriorityPlan ||
                                        panelTarget.PriorityKind == RecognitionPriorityKind.Detail;
                                    if (sameFocusedDetail && smaller > 0 &&
                                        (long)overlap.Width * overlap.Height * 10 >= smaller * 6)
                                        continue;
                                }
                                long panelPassStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                                Rectangle local = new Rectangle(panelCrop.Left - screen.Left,
                                    panelCrop.Top - screen.Top, panelCrop.Width, panelCrop.Height);
                                using (Bitmap panelBitmap = bitmap.Clone(local, PixelFormat.Format32bppArgb))
                                {
                                    float panelScale;
                                    float panelTargetLongEdge = usePriorityPlan
                                        ? RecognitionPriorityPlanner.TargetLongEdge(local,
                                            panelTarget.SourceTextHeight, panelTarget.ResourceLevel, false)
                                        : Math.Min(2200.0f, Math.Max(1600.0f, panelCrop.Width * 2.8f));
                                    using (Bitmap panelPrepared = PrepareForOcr(panelBitmap, out panelScale,
                                        false, panelTargetLongEdge))
                                    {
                                        captureBounds = panelCrop;
                                        OcrResult panelResult = await RecognizeAsync(panelPrepared);
                                        if (RecognitionWasCancelled(automatic, requestId)) return;
                                        MergeLabels(next, BuildLabels(panelResult, panelScale, panelPrepared));
                                        if (Program.Benchmark) benchmarkOcrText += " || 面板复核=" + panelResult.Text;

                                        // Classic equipment tooltips use red requirements and white
                                        // attributes on a dark blue background. A normal luminance
                                        // conversion makes the red glyphs almost as dark as the panel.
                                        // Re-read only an item/equipment crop with a red-aware channel;
                                        // this keeps the ordinary full-screen path fast while recovering
                                        // REQ STR/DEX, item type and the complete hover-detail block.
                                        if (LooksLikeItemPanelText(panelResult.Text) &&
                                            (!usePriorityPlan || panelTarget.ResourceLevel >= 0.74) &&
                                            stopwatch.ElapsedMilliseconds < 1320)
                                        {
                                            Rectangle equipmentTooltipLocal = FindEquipmentTooltipCrop(panelResult,
                                                panelScale, panelBitmap.Size);
                                            using (Bitmap tooltipBitmap = panelBitmap.Clone(equipmentTooltipLocal,
                                                PixelFormat.Format32bppArgb))
                                            {
                                                float tooltipScale;
                                                float tooltipTargetLongEdge = usePriorityPlan
                                                    ? RecognitionPriorityPlanner.TargetLongEdge(equipmentTooltipLocal,
                                                        panelTarget.SourceTextHeight, panelTarget.ResourceLevel, true)
                                                    : Math.Min(2150.0f, Math.Max(1650.0f,
                                                        equipmentTooltipLocal.Width * 3.25f));
                                                using (Bitmap tooltipPrepared = PrepareTooltipForOcr(
                                                    tooltipBitmap, out tooltipScale,
                                                    tooltipTargetLongEdge))
                                                {
                                                    captureBounds = new Rectangle(panelCrop.Left + equipmentTooltipLocal.Left,
                                                        panelCrop.Top + equipmentTooltipLocal.Top,
                                                        equipmentTooltipLocal.Width, equipmentTooltipLocal.Height);
                                                    OcrResult tooltipResult = await RecognizeAsync(tooltipPrepared);
                                                    if (RecognitionWasCancelled(automatic, requestId)) return;
                                                    MergeLabels(next, BuildLabels(tooltipResult, tooltipScale,
                                                        tooltipPrepared));
                                                    if (Program.Benchmark)
                                                        benchmarkOcrText += " || 装备红字复核=" + tooltipResult.Text;
                                                    captureBounds = panelCrop;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (Program.Benchmark)
                                    panelPassDuration += stopwatch.ElapsedMilliseconds - panelPassStarted;
                            }
                            captureBounds = screen;
                        }
                        bool needsMoreOcr = !useHoverPass && (next.Count < 3 || result.Lines.Count > next.Count + 1);

                        // Use the spare time budget for a second, grayscale/high-contrast OCR pass.
                        // Exact dictionary hits unique to either pass are merged; overlapping results
                        // keep the colour pass. Slow machines skip the second pass before one second.
                        if (needsMoreOcr && stopwatch.ElapsedMilliseconds < 450)
                        {
                            long fallbackPassStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                            float secondScale;
                            using (Bitmap contrastPrepared = PrepareForOcr(bitmap, out secondScale, true))
                            {
                                OcrResult secondResult = await RecognizeAsync(contrastPrepared);
                                if (RecognitionWasCancelled(automatic, requestId)) return;
                                List<OverlayLabel> second = BuildLabels(secondResult, secondScale, prepared);
                                MergeLabels(next, second);
                                needsMoreOcr = next.Count < 3 || secondResult.Lines.Count > next.Count + 1;
                                if (Program.Benchmark)
                                    benchmarkOcrText += " || 二次=" + secondResult.Text;
                            }
                            if (Program.Benchmark)
                                fallbackPassDuration += stopwatch.ElapsedMilliseconds - fallbackPassStarted;
                        }
                        // A higher-resolution colour pass improves very small item and quest text.
                        if (needsMoreOcr && stopwatch.ElapsedMilliseconds < 650)
                        {
                            long fallbackPassStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                            float thirdScale;
                            using (Bitmap largePrepared = PrepareForOcr(bitmap, out thirdScale, false, 3000.0f))
                            {
                                OcrResult thirdResult = await RecognizeAsync(largePrepared);
                                if (RecognitionWasCancelled(automatic, requestId)) return;
                                MergeLabels(next, BuildLabels(thirdResult, thirdScale, largePrepared));
                                if (Program.Benchmark)
                                    benchmarkOcrText += " || 三次=" + thirdResult.Text;
                            }
                            if (Program.Benchmark)
                                fallbackPassDuration += stopwatch.ElapsedMilliseconds - fallbackPassStarted;
                        }
                        if (visualCharacter != null)
                        {
                            AddCharacterStatVisualLayoutLabels(next, visualCharacter, screen);
                            if (!tooltipLocal.IsEmpty)
                                AddCharacterStatHoverHelp(next, visualCharacter,
                                    tooltipLocal, pointer, screen);
                        }
                        if (usePriorityPlan && plannedPanelTargets.Count > 0)
                            RetainLabelsInPriorityRegions(next, plannedPanelTargets);
                        long finalizeStarted = Program.Benchmark ? stopwatch.ElapsedMilliseconds : 0;
                        MergeAdjacentLongLabels(next);
                        MergeOverlappingSameTextLabels(next);
                        MergeSameTextLabels(next);
                        RemoveContainedLabels(next);
                        labels.Clear(); labels.AddRange(next);
                        if (Program.Benchmark) finalizeDuration = stopwatch.ElapsedMilliseconds - finalizeStarted;
                    }
                }
                if (RecognitionWasCancelled(automatic, requestId)) return;
                if (automatic && (!continuousTranslationEnabled ||
                    GetForegroundWindow() != automaticSourceWindow))
                {
                    visibleTranslation = false;
                    labels.Clear();
                    ShowCurrentTranslations();
                    return;
                }
                if (automatic && labels.Count == 0)
                {
                    continuousTranslationMisses++;
                    visibleTranslation = false;
                    ShowCurrentTranslations();
                    continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(
                        false, continuousTranslationMisses, 0);
                    return;
                }
                visibleTranslation = true;
                ShowCurrentTranslations();
                stopwatch.Stop();
                tray.Text = "枫语幕 v3.0（已显示，" + stopwatch.ElapsedMilliseconds + "ms）";
                if (Program.Benchmark)
                    WriteBenchmarkResult(stopwatch, captureDuration, probePassDuration,
                        mainPassDuration, hoverPassDuration, panelPassDuration,
                        fallbackPassDuration, finalizeDuration, recognitionPlanText,
                        recognizedScenes, benchmarkOcrText);
            }
            catch (Exception ex)
            {
                if (!restoreExistingOverlay)
                {
                    visibleTranslation = false;
                    labels.Clear();
                    ShowCurrentTranslations();
                }
                if (Program.Benchmark)
                    File.WriteAllText(Path.Combine(baseDir, "last_run.txt"),
                        "错误=" + ex + Environment.NewLine +
                        "时间=" + DateTime.Now.ToString("s") + Environment.NewLine, Encoding.UTF8);
                if (automatic)
                {
                    continuousTranslationFailures++;
                    continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(
                        restoreExistingOverlay, continuousTranslationMisses, continuousTranslationFailures);
                }
                else tray.ShowBalloonTip(3000, "识别失败", ex.Message, ToolTipIcon.Error);
            }
            finally { processing = false; }
        }

        private void WriteBenchmarkResult(Stopwatch stopwatch, long captureDuration,
            long probePassDuration, long mainPassDuration, long hoverPassDuration,
            long panelPassDuration, long fallbackPassDuration, long finalizeDuration,
            string recognitionPlanText, string recognizedScenes, string benchmarkOcrText)
        {
            stopwatch.Stop();
            StringBuilder benchmarkLabels = new StringBuilder();
            foreach (OverlayLabel label in labels)
            {
                if (benchmarkLabels.Length > 0) benchmarkLabels.Append(" | ");
                benchmarkLabels.Append(label.Text).Append('@').Append(label.Bounds.ToString());
            }
            File.WriteAllText(Path.Combine(baseDir, "last_run.txt"),
                "耗时毫秒=" + stopwatch.ElapsedMilliseconds + Environment.NewLine +
                "阶段耗时=截图:" + captureDuration + "ms, 快速探测:" + probePassDuration +
                "ms, 主OCR与匹配:" + mainPassDuration + "ms, 光标详情:" + hoverPassDuration +
                "ms, 面板复核:" + panelPassDuration + "ms, 兜底复核:" + fallbackPassDuration +
                "ms, 合并绘制:" + finalizeDuration + "ms" + Environment.NewLine +
                "识别计划=" + recognitionPlanText + Environment.NewLine +
                "识别场景=" + recognizedScenes + Environment.NewLine +
                "命中数量=" + labels.Count + Environment.NewLine +
                "图标指纹数量=" + translations.IconCount + Environment.NewLine +
                "任务数量=" + translations.TaskCount + Environment.NewLine +
                "任务文本数量=" + translations.TaskTextCount + Environment.NewLine +
                "技能说明数量=" + translations.SkillTextCount + Environment.NewLine +
                "装备物品说明数量=" + translations.ItemTextCount + Environment.NewLine +
                "覆盖标签=" + benchmarkLabels + Environment.NewLine +
                "OCR文本=" + (benchmarkOcrText ?? "").Replace("\r", " ").Replace("\n", " | ") + Environment.NewLine +
                "最佳图标距离=" + Program.BenchmarkBestIconDistance + Environment.NewLine +
                "最佳图标候选=" + Program.BenchmarkBestIcon + Environment.NewLine +
                "时间=" + DateTime.Now.ToString("s") + Environment.NewLine, Encoding.UTF8);
        }

        internal static Rectangle GetForegroundCaptureBounds()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && !IsIconic(foreground))
            {
                RECT client;
                POINT origin = new POINT();
                if (GetClientRect(foreground, out client) && ClientToScreen(foreground, ref origin))
                {
                    Rectangle window = new Rectangle(origin.X, origin.Y,
                        client.Right - client.Left, client.Bottom - client.Top);
                    Rectangle clipped = Rectangle.Intersect(window, SystemInformation.VirtualScreen);
                    if (clipped.Width >= 160 && clipped.Height >= 120) return clipped;
                }
            }

            IntPtr monitor = MonitorFromWindow(foreground, 2); // nearest monitor
            MONITORINFO info = new MONITORINFO();
            info.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
            return Screen.PrimaryScreen.Bounds;
        }

        internal async Task<ChatCaptureFrame> CaptureChatAsync(Rectangle screen)
        {
            ChatCaptureFrame frame = new ChatCaptureFrame();
            if (!ChatRegionSettings.IsUsable(screen)) return frame;
            using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
                return await AnalyzeChatBitmapAsync(bitmap);
            }
        }

        internal async Task<ChatCaptureFrame> AnalyzeChatImageAsync(string imagePath)
        {
            if (String.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return new ChatCaptureFrame();
            using (Image source = Image.FromFile(imagePath))
            using (Bitmap bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.DrawImageUnscaled(source, 0, 0);
                return await AnalyzeChatBitmapAsync(bitmap);
            }
        }

        private async Task<ChatCaptureFrame> AnalyzeChatBitmapAsync(Bitmap bitmap)
        {
            ChatCaptureFrame frame = new ChatCaptureFrame();
            float scale;
            using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false, 1800.0f))
            {
                OcrResult result = await RecognizeAsync(prepared);
                frame.Text = result.Text ?? "";
                foreach (OcrLine line in result.Lines)
                {
                    string text = (line.Text ?? "").Trim();
                    if (text.Length == 0) continue;
                    frame.Lines.Add(new ChatCaptureLine {
                        Text = text,
                        Style = SampleChatVisualStyle(bitmap, line, scale)
                    });
                }
                return frame;
            }
        }

        internal async Task<string> CaptureTextAsync(Rectangle screen)
        {
            ChatCaptureFrame frame = await CaptureChatAsync(screen);
            return frame.Text;
        }

        private sealed class ColorCluster
        {
            internal int Count;
            internal long Red;
            internal long Green;
            internal long Blue;
        }

        private sealed class DominantColorSample
        {
            internal Color Color = Color.Empty;
            internal double Coverage;
        }

        private static ChatVisualStyle SampleChatVisualStyle(Bitmap bitmap, OcrLine line, float scale)
        {
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty || scale <= 0)
                return ChatVisualStylePolicy.FromSample(line.Text, Color.Empty, Color.Empty, false);
            Rectangle text = Rectangle.FromLTRB((int)Math.Floor(raw.Left / scale),
                (int)Math.Floor(raw.Top / scale), (int)Math.Ceiling(raw.Right / scale),
                (int)Math.Ceiling(raw.Bottom / scale));
            text = Rectangle.Intersect(text, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            if (text.Width < 2 || text.Height < 2)
                return ChatVisualStylePolicy.FromSample(line.Text, Color.Empty, Color.Empty, false);

            Rectangle backdropArea = Rectangle.FromLTRB(Math.Max(0, text.Left - 10),
                Math.Max(0, text.Top - 3), Math.Min(bitmap.Width, text.Right + 10),
                Math.Min(bitmap.Height, text.Bottom + 3));
            DominantColorSample backdrop = SampleDominantColor(bitmap, backdropArea, Color.Empty, false);
            DominantColorSample foreground = SampleDominantColor(bitmap, text, backdrop.Color, true);
            int saturation = backdrop.Color.IsEmpty ? 0 :
                Math.Max(backdrop.Color.R, Math.Max(backdrop.Color.G, backdrop.Color.B)) -
                Math.Min(backdrop.Color.R, Math.Min(backdrop.Color.G, backdrop.Color.B));
            // Classic regular megaphones use a pale blue band; super megaphones use
            // a warm pink band. Both are full-row backgrounds in the supplied videos.
            bool pinkSuperMegaphoneBand = !backdrop.Color.IsEmpty && backdrop.Color.R >= 95 &&
                backdrop.Color.R >= backdrop.Color.G + 12 &&
                backdrop.Color.R >= backdrop.Color.B - 12;
            bool blueMegaphoneBand = !backdrop.Color.IsEmpty && backdrop.Color.R >= 105 &&
                backdrop.Color.G >= 125 && backdrop.Color.B >= 130 &&
                backdrop.Color.G >= backdrop.Color.R + 12 &&
                backdrop.Color.B >= backdrop.Color.R + 20;
            bool coloredBackground = !backdrop.Color.IsEmpty && saturation >= 28 &&
                backdrop.Coverage >= 0.18 && (pinkSuperMegaphoneBand || blueMegaphoneBand);
            return ChatVisualStylePolicy.FromSample(line.Text, foreground.Color,
                backdrop.Color, coloredBackground);
        }

        private static DominantColorSample SampleDominantColor(Bitmap bitmap, Rectangle area,
            Color background, bool foreground)
        {
            Dictionary<int, ColorCluster> clusters = new Dictionary<int, ColorCluster>();
            int total = 0;
            int step = area.Width * area.Height > 30000 ? 2 : 1;
            for (int y = area.Top; y < area.Bottom; y += step)
                for (int x = area.Left; x < area.Right; x += step)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 100) continue;
                    if (foreground && !background.IsEmpty)
                    {
                        int distance = Math.Abs(pixel.R - background.R) +
                            Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B);
                        if (distance < 86) continue;
                    }
                    int key = (pixel.R >> 5) << 6 | (pixel.G >> 5) << 3 | (pixel.B >> 5);
                    ColorCluster cluster;
                    if (!clusters.TryGetValue(key, out cluster))
                    {
                        cluster = new ColorCluster();
                        clusters[key] = cluster;
                    }
                    cluster.Count++; cluster.Red += pixel.R;
                    cluster.Green += pixel.G; cluster.Blue += pixel.B; total++;
                }
            ColorCluster best = null;
            double bestScore = -1;
            foreach (ColorCluster cluster in clusters.Values)
            {
                double score = cluster.Count;
                if (foreground)
                {
                    Color average = Color.FromArgb((int)(cluster.Red / cluster.Count),
                        (int)(cluster.Green / cluster.Count), (int)(cluster.Blue / cluster.Count));
                    int saturation = Math.Max(average.R, Math.Max(average.G, average.B)) -
                        Math.Min(average.R, Math.Min(average.G, average.B));
                    int brightness = Math.Max(average.R, Math.Max(average.G, average.B));
                    // Prefer the coloured glyph fill over its black/white outline.
                    score *= 1.0 + saturation / 28.0 + brightness / 510.0;
                }
                if (score > bestScore) { bestScore = score; best = cluster; }
            }
            DominantColorSample result = new DominantColorSample();
            if (best == null || best.Count == 0) return result;
            result.Color = Color.FromArgb((int)(best.Red / best.Count),
                (int)(best.Green / best.Count), (int)(best.Blue / best.Count));
            result.Coverage = total == 0 ? 0 : (double)best.Count / total;
            return result;
        }

        private static Bitmap PrepareForOcr(Bitmap source, out float scale, bool grayscale,
            float targetLongEdge = 2400.0f)
        {
            int longEdge = Math.Max(source.Width, source.Height);
            // 1440p/4K full-screen captures must be allowed to shrink. Keeping them at native
            // size made Windows OCR spend over two seconds even before a hover-detail pass.
            float minimumScale = longEdge >= 1900 ? 0.58f : 1.0f;
            scale = Math.Min(2.5f, Math.Max(minimumScale, targetLongEdge / Math.Max(1, longEdge)));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            result.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Slight contrast boost helps outlined game text while preserving coloured item names.
                float contrast = grayscale ? 1.38f : 1.18f;
                float offset = (1.0f - contrast) / 2.0f;
                float red = grayscale ? 0.299f * contrast : contrast;
                float green = grayscale ? 0.587f * contrast : contrast;
                float blue = grayscale ? 0.114f * contrast : contrast;
                ColorMatrix matrix = new ColorMatrix(new float[][] {
                    new float[] { red, grayscale ? red : 0, grayscale ? red : 0, 0, 0 },
                    new float[] { grayscale ? green : 0, green, grayscale ? green : 0, 0, 0 },
                    new float[] { grayscale ? blue : 0, grayscale ? blue : 0, blue, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { offset, offset, offset, 0, 1 }
                });
                attributes.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, width, height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private static Bitmap PrepareTooltipForOcr(Bitmap source, out float scale,
            float targetLongEdge)
        {
            int longEdge = Math.Max(source.Width, source.Height);
            float minimumScale = longEdge >= 1900 ? 0.58f : 1.0f;
            scale = Math.Min(2.6f, Math.Max(minimumScale,
                targetLongEdge / Math.Max(1, longEdge)));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            result.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Output brightness = 82% red + 18% green. White and red text
                // stay bright, while the blue/purple tooltip background becomes dark.
                const float contrast = 1.48f;
                float offset = (1.0f - contrast) / 2.0f;
                float red = 0.82f * contrast, green = 0.18f * contrast;
                ColorMatrix matrix = new ColorMatrix(new float[][] {
                    new float[] { red, red, red, 0, 0 },
                    new float[] { green, green, green, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { offset, offset, offset, 0, 1 }
                });
                attributes.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, width, height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private static bool IsClassicTooltipPixel(Color color)
        {
            // Classic tooltips are a translucent navy/purple rectangle. Detecting the
            // surface itself is much more reliable than assuming a fixed tooltip size or
            // position around the cursor, and excludes bright sky/snow backgrounds.
            return color.A > 180 && color.R >= 18 && color.R <= 122 &&
                color.G >= 18 && color.G <= 138 && color.B >= 64 && color.B <= 184 &&
                color.B >= color.R + 18 && color.B >= color.G + 8;
        }

        private static Rectangle FindClassicTooltipCrop(Bitmap bitmap,
            System.Drawing.Point screenPointer, Rectangle screen)
        {
            if (bitmap == null || bitmap.Width < 160 || bitmap.Height < 120)
                return Rectangle.Empty;
            System.Drawing.Point pointer = new System.Drawing.Point(
                screenPointer.X - screen.Left, screenPointer.Y - screen.Top);
            const int step = 4;
            int columns = (bitmap.Width + step - 1) / step;
            int rows = (bitmap.Height + step - 1) / step;
            bool[] mask = new bool[columns * rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int sampleX = Math.Min(bitmap.Width - 1, x * step + step / 2);
                    int sampleY = Math.Min(bitmap.Height - 1, y * step + step / 2);
                    mask[y * columns + x] = IsClassicTooltipPixel(bitmap.GetPixel(sampleX, sampleY));
                }

            bool[] visited = new bool[mask.Length];
            int[] queue = new int[mask.Length];
            Rectangle best = Rectangle.Empty;
            double bestScore = 0;
            int[] dx = new int[] { -1, 1, 0, 0 };
            int[] dy = new int[] { 0, 0, -1, 1 };
            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || visited[start]) continue;
                int head = 0, tail = 0, count = 0;
                int minX = columns, minY = rows, maxX = 0, maxY = 0;
                visited[start] = true; queue[tail++] = start;
                while (head < tail)
                {
                    int index = queue[head++]; count++;
                    int x = index % columns, y = index / columns;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int nextX = x + dx[direction], nextY = y + dy[direction];
                        if (nextX < 0 || nextY < 0 || nextX >= columns || nextY >= rows) continue;
                        int next = nextY * columns + nextX;
                        if (!mask[next] || visited[next]) continue;
                        visited[next] = true; queue[tail++] = next;
                    }
                }
                int width = (maxX - minX + 1) * step;
                int height = (maxY - minY + 1) * step;
                if (width < 190 || height < 90 || count < 800) continue;
                double density = (double)count / Math.Max(1, (maxX - minX + 1) * (maxY - minY + 1));
                if (density < 0.20) continue;
                Rectangle candidate = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    Rectangle.FromLTRB(minX * step - 14, minY * step - 14,
                        Math.Min(bitmap.Width, (maxX + 1) * step + 14),
                        Math.Min(bitmap.Height, (maxY + 1) * step + 14)));
                int nearestX = Math.Max(candidate.Left, Math.Min(pointer.X, candidate.Right));
                int nearestY = Math.Max(candidate.Top, Math.Min(pointer.Y, candidate.Bottom));
                double distance = Math.Sqrt((pointer.X - nearestX) * (pointer.X - nearestX) +
                    (pointer.Y - nearestY) * (pointer.Y - nearestY));
                if (distance > Math.Max(520, bitmap.Width * 0.28)) continue;
                double score = candidate.Width * candidate.Height * density / (1.0 + distance / 180.0);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }

        private void AddCharacterStatVisualLayoutLabels(List<OverlayLabel> output,
            CharacterStatVisualLayout layout, Rectangle screen)
        {
            if (layout == null) return;
            // Once the colored columns provide an exact geometry, discard only the generic
            // fixed-grid character labels produced from noisy OCR anchors. The visual layout
            // immediately restores them at measured coordinates; unrelated translated text
            // and dynamic values are left untouched.
            for (int i = output.Count - 1; i >= 0; i--)
                if (IsCharacterLayoutLabel(output[i].Text)) output.RemoveAt(i);
            int stableStart = output.Count;
            AddCharacterStatLayoutLabelsAt(output,
                screen.Left + layout.Left - Bounds.Left,
                screen.Top + layout.Top - Bounds.Top, layout.Scale,
                screen.Left + layout.RightLeft - Bounds.Left,
                screen.Top + layout.RightTop - Bounds.Top);
            for (int i = stableStart; i < output.Count; i++)
                output[i].StableLayout = true;
        }

        private static bool IsVisualCharacterInformationPanel(CharacterStatVisualLayout layout,
            OcrResult result, float ocrScale)
        {
            if (layout == null || result == null || ocrScale <= 0) return false;
            RectangleF panel = layout.Crop;
            panel.Inflate(30.0f * layout.Scale, 30.0f * layout.Scale);
            foreach (OcrLine line in result.Lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                bool informationAnchor = normalized.Contains("character info") ||
                    normalized.Contains("citizenship") || normalized.Contains("show pet info") ||
                    normalized.Contains("closeness");
                if (!informationAnchor) continue;
                RectangleF raw = GetOcrLineBounds(line);
                PointF center = new PointF((raw.Left + raw.Width / 2.0f) / ocrScale,
                    (raw.Top + raw.Height / 2.0f) / ocrScale);
                if (panel.Contains(center)) return true;
            }
            return false;
        }

        private void AddCharacterStatHoverHelp(List<OverlayLabel> output,
            CharacterStatVisualLayout layout, Rectangle tooltipLocal,
            System.Drawing.Point screenPointer, Rectangle screen)
        {
            if (layout == null || tooltipLocal.IsEmpty) return;
            float localX = screenPointer.X - screen.Left;
            float localY = screenPointer.Y - screen.Top;
            float row = 34.0f * layout.Scale;
            if (localX < layout.RightLeft - 22.0f * layout.Scale ||
                localX > layout.RightLeft + 150.0f * layout.Scale) return;
            int index = (int)Math.Floor((localY - layout.RightTop + 5.0f * layout.Scale) /
                Math.Max(1.0f, row));
            if (index < 0 || index >= 10) return;
            float rowCenter = layout.RightTop + index * row + 10.0f * layout.Scale;
            if (Math.Abs(localY - rowCenter) > 24.0f * layout.Scale) return;
            string[] help = new string[] {
                "攻击力：角色普通攻击能够造成的伤害范围。",
                "物理防御力：降低受到的物理攻击伤害。",
                "魔法攻击力：影响魔法技能造成的伤害。",
                "魔法防御力：降低受到的魔法攻击伤害。",
                "命中率：影响攻击命中目标的概率。",
                "回避率：影响躲避敌人攻击的概率。",
                "暴击率：决定攻击造成暴击的概率；暴击的额外伤害取决于暴击伤害属性。",
                "暴击伤害：决定暴击时额外增加的伤害。",
                "移动速度：决定角色的移动速度。",
                "跳跃力：决定角色的跳跃高度。"
            };
            RectangleF translatedBounds = new RectangleF(
                screen.Left + tooltipLocal.Left - Bounds.Left,
                screen.Top + tooltipLocal.Top - Bounds.Top,
                tooltipLocal.Width, tooltipLocal.Height);
            for (int i = output.Count - 1; i >= 0; i--)
            {
                RectangleF current = output[i].Bounds;
                float centerX = current.Left + current.Width / 2.0f;
                float centerY = current.Top + current.Height / 2.0f;
                if (!translatedBounds.Contains(centerX, centerY) ||
                    IsCharacterStatLabel(output[i].Text)) continue;
                output.RemoveAt(i);
            }
            output.Add(new OverlayLabel { Bounds = translatedBounds, Text = help[index], Wrap = true });
        }

        private static bool IsCharacterStatLabel(string text)
        {
            string[] values = new string[] { "攻击力", "物理防御力", "魔法攻击力", "魔法防御力",
                "命中率", "回避率", "暴击率", "暴击伤害", "移动速度", "跳跃力" };
            foreach (string value in values)
                if (String.Equals(value, text, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsCharacterLayoutLabel(string text)
        {
            string[] values = new string[] { "角色属性", "名称", "职业", "等级", "生命值", "魔法值", "经验", "人气",
                "力量", "敏捷", "智力", "运气", "能力值点数", "攻击力", "物理防御力", "魔法攻击力",
                "魔法防御力", "命中率", "回避率", "暴击率", "暴击伤害", "移动速度", "跳跃力" };
            foreach (string value in values)
                if (String.Equals(value, text, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void MergeLabels(List<OverlayLabel> primary, List<OverlayLabel> secondary)
        {
            foreach (OverlayLabel candidate in secondary)
            {
                bool duplicate = false;
                for (int i = 0; i < primary.Count; i++)
                {
                    OverlayLabel existing = primary[i];
                    RectangleF intersection = RectangleF.Intersect(existing.Bounds, candidate.Bounds);
                    float smallerArea = Math.Min(existing.Bounds.Width * existing.Bounds.Height,
                        candidate.Bounds.Width * candidate.Bounds.Height);
                    if ((smallerArea > 0 && intersection.Width * intersection.Height / smallerArea >= 0.45f) ||
                        (SameOverlayText(existing.Text, candidate.Text) &&
                         Math.Abs(existing.Bounds.Y - candidate.Bounds.Y) < 20))
                    {
                        // A focused panel pass usually turns a one-word fallback into a complete
                        // structured row. Prefer that richer result instead of letting the noisy
                        // full-screen pass permanently mask it.
                        if (!String.Equals(existing.Text, candidate.Text, StringComparison.Ordinal) &&
                            candidate.Text.Length >= existing.Text.Length + 4)
                            primary[i] = candidate;
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) primary.Add(candidate);
            }
        }

        private static bool SameOverlayText(string left, string right)
        {
            if (String.Equals(left, right, StringComparison.Ordinal)) return true;
            if (String.IsNullOrEmpty(left) || String.IsNullOrEmpty(right)) return false;
            return String.Equals(Regex.Replace(left, @"\s+", ""),
                Regex.Replace(right, @"\s+", ""), StringComparison.Ordinal);
        }

        private async Task<OcrResult> RecognizeAsync(Bitmap bitmap)
        {
            using (MemoryStream png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png);
                byte[] bytes = png.ToArray();
                using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
                {
                    using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await ToTask<uint>((IAsyncOperation<uint>)writer.StoreAsync());
                        await ToTask<bool>(writer.FlushAsync());
                    }
                    BitmapDecoder decoder = await ToTask<BitmapDecoder>(BitmapDecoder.CreateAsync(stream));
                    SoftwareBitmap software = await ToTask<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied));
                    using (software) { return await ToTask<OcrResult>(ocr.RecognizeAsync(software)); }
                }
            }
        }

        private static Task<T> ToTask<T>(IAsyncOperation<T> operation)
        {
            TaskCompletionSource<T> source = new TaskCompletionSource<T>();
            operation.Completed = delegate(IAsyncOperation<T> info, AsyncStatus status)
            {
                try
                {
                    if (status == AsyncStatus.Completed) source.TrySetResult(info.GetResults());
                    else if (status == AsyncStatus.Canceled) source.TrySetCanceled();
                    else source.TrySetException(info.ErrorCode ?? new InvalidOperationException("Windows OCR 异步操作失败"));
                }
                catch (Exception ex) { source.TrySetException(ex); }
            };
            return source.Task;
        }

        private List<PanelCropCandidate> FindPanelCandidates(OcrResult result, float ocrScale,
            Rectangle screen, bool includeDialogueAnchors)
        {
            List<PanelCropCandidate> candidates = new List<PanelCropCandidate>();
            foreach (OcrLine line in result.Lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                SceneEvidence evidence = SceneClassifier.Classify(line.Text, translations,
                    includeDialogueAnchors);
                bool character = (evidence.Kinds & SceneKind.Character) != 0;
                bool skillHeader = evidence.SkillHeader;
                bool skillDetail = evidence.SkillDetail;
                bool skill = (evidence.Kinds & SceneKind.Skill) != 0;
                string taskId = evidence.TaskId;
                bool questHeader = evidence.QuestHeader;
                bool quest = (evidence.Kinds & SceneKind.Quest) != 0;
                bool itemHeader = evidence.ItemHeader;
                bool itemDetail = evidence.ItemDetail || LooksLikeEquipmentStatText(line.Text);
                bool item = (evidence.Kinds & (SceneKind.Item | SceneKind.Shop | SceneKind.Inventory)) != 0 ||
                    itemDetail;
                bool dialogue = (evidence.Kinds & SceneKind.Dialogue) != 0;
                if (!character && !skill && !quest && !item && !dialogue) continue;
                RectangleF source = GetOcrLineBounds(line);
                int sourceLeft = screen.Left + (int)(source.Left / ocrScale);
                int sourceTop = screen.Top + (int)(source.Top / ocrScale);
                int left = sourceLeft - 28;
                int top = sourceTop - 24;
                int width, height;
                bool sideHelper = false;
                RecognitionPriorityKind priorityKind;
                string kind;
                if (character)
                {
                    width = Math.Max(620, screen.Width * 29 / 100);
                    height = Math.Max(500, screen.Height * 43 / 100);
                    priorityKind = RecognitionPriorityKind.OutsideDialogue;
                    kind = "character";
                }
                else if (skill)
                {
                    if (!skillHeader) { left = sourceLeft - 210; top = sourceTop - 180; }
                    width = Math.Max(950, screen.Width * 48 / 100);
                    height = Math.Max(700, screen.Height * 72 / 100);
                    priorityKind = skillDetail && !skillHeader
                        ? RecognitionPriorityKind.Detail : RecognitionPriorityKind.OutsideDialogue;
                    kind = "skill";
                }
                else if (quest)
                {
                    if (!questHeader) { left = sourceLeft - 120; top = sourceTop - 180; }
                    sideHelper = sourceLeft > screen.Left + screen.Width * 70 / 100;
                    width = sideHelper ? Math.Max(430, screen.Width * 23 / 100) : Math.Max(900, screen.Width * 48 / 100);
                    height = sideHelper ? Math.Max(420, screen.Height * 45 / 100) : Math.Max(680, screen.Height * 68 / 100);
                    priorityKind = sideHelper ? RecognitionPriorityKind.OutsideDialogue :
                        RecognitionPriorityKind.Dialogue;
                    kind = "quest";
                }
                else if (item)
                {
                    if (!itemHeader) { left = sourceLeft - 260; top = sourceTop - 320; }
                    width = Math.Max(900, screen.Width * 48 / 100);
                    height = Math.Max(680, screen.Height * 74 / 100);
                    priorityKind = itemDetail && !itemHeader
                        ? RecognitionPriorityKind.Detail : RecognitionPriorityKind.OutsideDialogue;
                    kind = "item";
                }
                else
                {
                    left = sourceLeft - 180; top = sourceTop - 240;
                    width = Math.Max(900, screen.Width * 48 / 100);
                    height = Math.Max(620, screen.Height * 62 / 100);
                    priorityKind = RecognitionPriorityKind.Dialogue;
                    kind = "dialogue";
                }
                Rectangle crop = Rectangle.Intersect(screen, new Rectangle(left, top, width, height));
                if (crop.Width < 300 || crop.Height < 250) continue;
                int score = character ? SceneClassifier.EvidenceScore(evidence, SceneKind.Character) :
                    (skillDetail ? SceneClassifier.EvidenceScore(evidence, SceneKind.Skill) : (itemDetail ? 145 :
                    (skillHeader ? 120 : (questHeader ? 115 :
                    (itemHeader ? 110 : (taskId.Length > 0 ? 88 :
                    (dialogue ? 100 : (LooksLikeEquipmentStatText(line.Text) ? 82 : 60))))))));
                // A main quest/equipment/skill window is more useful than the small tracker
                // at the right edge. Sort all proposals before de-duplication so OCR line order
                // can never consume the three high-resolution passes on low-value fragments.
                bool atRightEdge = sourceLeft > screen.Left + screen.Width * 72 / 100;
                if (atRightEdge) score -= 40;
                else if (quest && taskId.Length > 0) score += 34;
                candidates.Add(new PanelCropCandidate { Bounds = crop, Score = score,
                    Kind = kind, PriorityKind = priorityKind,
                    SourceTextHeight = Math.Max(5.0f, source.Height / Math.Max(0.01f, ocrScale)) });
            }

            candidates.Sort(delegate(PanelCropCandidate left, PanelCropCandidate right) {
                int score = right.Score.CompareTo(left.Score);
                if (score != 0) return score;
                long leftArea = (long)left.Bounds.Width * left.Bounds.Height;
                long rightArea = (long)right.Bounds.Width * right.Bounds.Height;
                return rightArea.CompareTo(leftArea);
            });
            return candidates;
        }

        private List<Rectangle> FindPanelCrops(OcrResult result, float ocrScale, Rectangle screen)
        {
            List<PanelCropCandidate> candidates = FindPanelCandidates(result, ocrScale, screen, false);
            List<PanelCropCandidate> accepted = new List<PanelCropCandidate>();
            HashSet<string> acceptedKinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelCropCandidate candidate in candidates)
            {
                if (acceptedKinds.Contains(candidate.Kind)) continue;
                bool duplicate = false;
                foreach (PanelCropCandidate existing in accepted)
                {
                    Rectangle overlap = Rectangle.Intersect(existing.Bounds, candidate.Bounds);
                    long smaller = Math.Min((long)existing.Bounds.Width * existing.Bounds.Height,
                        (long)candidate.Bounds.Width * candidate.Bounds.Height);
                    long overlapArea = (long)overlap.Width * overlap.Height;
                    bool samePanelFamily = String.Equals(existing.Kind, candidate.Kind,
                        StringComparison.Ordinal);
                    if (smaller > 0 && (overlapArea * 10 >= smaller * 7 ||
                        (samePanelFamily && overlapArea * 10 >= smaller * 3)))
                    { duplicate = true; break; }
                }
                if (!duplicate)
                {
                    accepted.Add(candidate);
                    acceptedKinds.Add(candidate.Kind);
                }
                if (accepted.Count >= 2) break;
            }
            List<Rectangle> crops = new List<Rectangle>();
            foreach (PanelCropCandidate candidate in accepted) crops.Add(candidate.Bounds);
            return crops;
        }

        private static List<PanelCropCandidate> SelectPriorityPanelTargets(
            List<PanelCropCandidate> candidates, List<RecognitionTier> plan)
        {
            List<PanelCropCandidate> accepted = new List<PanelCropCandidate>();
            foreach (RecognitionTier tier in plan)
            {
                if (tier.Kind == RecognitionPriorityKind.CurrentInterface) continue;
                foreach (PanelCropCandidate candidate in candidates)
                {
                    if (candidate.PriorityKind != tier.Kind) continue;
                    // Different priority layers can legitimately overlap: a tooltip is drawn on
                    // top of the skill/item window it describes. Keep one candidate per layer;
                    // de-duplicating by geometry here would silently drop the panel behind it.
                    candidate.ResourceLevel = tier.ResourceLevel;
                    accepted.Add(candidate);
                    break;
                }
            }
            return accepted;
        }

        private void RetainLabelsInPriorityRegions(List<OverlayLabel> currentLabels,
            List<PanelCropCandidate> priorityRegions)
        {
            if (currentLabels == null || priorityRegions == null || priorityRegions.Count == 0) return;
            for (int i = currentLabels.Count - 1; i >= 0; i--)
            {
                OverlayLabel label = currentLabels[i];
                // The colour-band detector has already verified the complete Character Stat
                // geometry. Do not cut its last rows merely because a higher-priority tooltip
                // overlaps only the upper part of the same panel.
                if (label.StableLayout) continue;
                PointF center = new PointF(Bounds.Left + label.Bounds.Left + label.Bounds.Width / 2.0f,
                    Bounds.Top + label.Bounds.Top + label.Bounds.Height / 2.0f);
                bool inside = false;
                foreach (PanelCropCandidate region in priorityRegions)
                {
                    Rectangle expanded = region.Bounds;
                    expanded.Inflate(8, 8);
                    if (expanded.Contains(System.Drawing.Point.Round(center))) { inside = true; break; }
                }
                if (!inside) currentLabels.RemoveAt(i);
            }
        }

        private bool IsCharacterPanelAlreadyCovered(Rectangle panelCrop,
            List<OverlayLabel> currentLabels)
        {
            string[] statMarkers = new string[] { "名称", "职业", "生命值", "魔法值",
                "能力值点数", "物理防御力", "暴击伤害", "跳跃力" };
            string[] infoMarkers = new string[] { "角色信息", "公民身份", "人气", "家族",
                "邀请组队", "申请交易", "查看宠物信息" };
            int statCount = 0, infoCount = 0;
            foreach (OverlayLabel label in currentLabels)
            {
                bool statMarker = false, infoMarker = false;
                foreach (string value in statMarkers)
                    if (String.Equals(value, label.Text, StringComparison.Ordinal))
                    { statMarker = true; break; }
                foreach (string value in infoMarkers)
                    if (String.Equals(value, label.Text, StringComparison.Ordinal))
                    { infoMarker = true; break; }
                if (!statMarker && !infoMarker) continue;
                RectangleF screenBounds = new RectangleF(Bounds.Left + label.Bounds.Left,
                    Bounds.Top + label.Bounds.Top, label.Bounds.Width, label.Bounds.Height);
                if (!screenBounds.IntersectsWith(panelCrop)) continue;
                if (statMarker) statCount++;
                if (infoMarker) infoCount++;
            }
            // Once a deterministic layout has covered the panel, another focused OCR pass
            // only repeats the same short labels at slightly different offsets. Character
            // Info is deliberately separate from Character Stat: treating both as one panel
            // was the cause of the duplicated/misaligned labels seen when trade was open.
            return statCount >= 6 || infoCount >= 4;
        }

        private Dictionary<OcrLine, OcrPanelInfo> BuildOcrPanels(List<OcrLine> lines)
        {
            Dictionary<OcrLine, OcrPanelInfo> byLine = new Dictionary<OcrLine, OcrPanelInfo>();
            int count = lines.Count;
            int[] parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (BelongToSamePanel(lines[i], lines[j])) UnionPanel(parent, i, j);

            Dictionary<int, OcrPanelInfo> groups = new Dictionary<int, OcrPanelInfo>();
            for (int i = 0; i < count; i++)
            {
                int root = FindPanelRoot(parent, i);
                OcrPanelInfo panel;
                if (!groups.TryGetValue(root, out panel))
                {
                    panel = new OcrPanelInfo(); groups.Add(root, panel);
                }
                panel.Lines.Add(lines[i]);
                byLine[lines[i]] = panel;
            }

            foreach (OcrPanelInfo panel in groups.Values)
            {
                panel.Lines.Sort(delegate(OcrLine left, OcrLine right) {
                    RectangleF a = GetOcrLineBounds(left), b = GetOcrLineBounds(right);
                    int vertical = a.Top.CompareTo(b.Top);
                    return Math.Abs(a.Top - b.Top) <= Math.Max(a.Height, b.Height) * 0.45f
                        ? a.Left.CompareTo(b.Left) : vertical;
                });
                StringBuilder text = new StringBuilder();
                foreach (OcrLine line in panel.Lines)
                {
                    if (text.Length > 0) text.Append(' ');
                    text.Append(line.Text);
                }
                panel.Text = text.ToString();
                string normalized = TranslationStore.Normalize(panel.Text);
                panel.TaskId = translations.DetectTaskId(panel.Text);
                panel.IsQuest = panel.TaskId.Length > 0 ||
                    (normalized.Contains("quest") &&
                     (normalized.Contains("available") || normalized.Contains("in progress") ||
                      normalized.Contains("completed") || normalized.Contains("forfeit") ||
                      normalized.Contains("quest helper") || normalized.Contains("accept") ||
                      normalized.Contains("decline")));
                panel.SkillId = translations.DetectSkillId(panel.Text);
                string skillContentId = translations.DetectSkillContentId(panel.Text);
                if (panel.SkillId.Length == 0) panel.SkillId = skillContentId;
                panel.IsSkillDetail = normalized.Contains("master level") ||
                    (normalized.Contains("current level") && normalized.Contains("next level")) ||
                    normalized.Contains("required skill") ||
                    (skillContentId.Length > 0 && normalized.Length >= 24) ||
                    (panel.SkillId.Length > 0 && normalized.Length >= 35 &&
                     (normalized.Contains("damage") || normalized.Contains("duration") ||
                      normalized.Contains("seconds") || normalized.Contains(" mp ")));
                panel.IsCharacterInfo = CharacterPanelPolicy.IsInformation(normalized);
                panel.IsCharacterStats = CharacterPanelPolicy.IsStatistics(normalized);
                panel.IsEquipmentDetail = normalized.Contains("req lev") ||
                    normalized.Contains("required level") || normalized.Contains("remaining enhancements") ||
                    (normalized.Contains("weapon def") && normalized.Contains("magic def"));
            }
            return byLine;
        }

        private static int FindPanelRoot(int[] parent, int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        private static void UnionPanel(int[] parent, int left, int right)
        {
            int a = FindPanelRoot(parent, left), b = FindPanelRoot(parent, right);
            if (a != b) parent[b] = a;
        }

        private static bool BelongToSamePanel(OcrLine first, OcrLine second)
        {
            RectangleF a = GetOcrLineBounds(first), b = GetOcrLineBounds(second);
            if (a.IsEmpty || b.IsEmpty) return false;
            float height = Math.Max(8.0f, Math.Max(a.Height, b.Height));
            float verticalGap = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
            if (verticalGap > Math.Max(34.0f, height * 2.5f)) return false;
            float overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            float narrow = Math.Max(1.0f, Math.Min(a.Width, b.Width));
            if (overlap >= Math.Min(24.0f, narrow * 0.18f)) return true;
            float leftDrift = Math.Abs(a.Left - b.Left);
            return leftDrift <= Math.Max(72.0f, narrow * 0.52f);
        }

        private List<OverlayLabel> BuildLabels(OcrResult result, float ocrScale, Bitmap prepared)
        {
            List<OverlayLabel> output = new List<OverlayLabel>();
            Rectangle chatExclusion = GetChatExclusionBounds();
            List<OcrLine> allLines = new List<OcrLine>();
            StringBuilder visibleText = new StringBuilder();
            foreach (OcrLine candidate in result.Lines)
            {
                // Static interface/item matching must never translate player chat. A chat line
                // can legitimately contain an exact item or skill name ("T> Bamboo Hat"), but
                // that is not evidence that the corresponding tooltip is open. Chat has its
                // own explicit AI translator and is excluded here even without a saved region.
                if (SceneClassifier.LooksLikePlayerChat(candidate.Text)) continue;
                if (IsChatLine(candidate, ocrScale, chatExclusion) &&
                    !translations.HasDetailCandidate(candidate.Text) &&
                    !LooksLikeEquipmentStatText(candidate.Text)) continue;
                allLines.Add(candidate);
                if (visibleText.Length > 0) visibleText.AppendLine();
                visibleText.Append(candidate.Text);
            }
            Dictionary<OcrLine, OcrPanelInfo> panelByLine = BuildOcrPanels(allLines);
            AddStablePanelLayouts(output, allLines, panelByLine, ocrScale);
            SceneSnapshot visibleScene = SceneClassifier.Analyze(result, translations, false);
            bool globalEquipmentContext = visibleScene.IsStrong(SceneKind.Item);
            string globalTaskId = translations.DetectTaskId(visibleText.ToString());
            if (globalTaskId.Length == 0)
            {
                // DetectTaskId intentionally rejects fuzzy matching on one giant page string.
                // A focused quest crop can still contain a slightly damaged title such as
                // "Mars Training"; recover context from individual visual lines.
                foreach (OcrLine candidate in allLines)
                {
                    globalTaskId = translations.DetectTaskId(candidate.Text);
                    if (globalTaskId.Length > 0) break;
                }
            }
            if (globalTaskId.Length > 0)
            {
                AddQuestPanelTextLabels(output, allLines, globalTaskId, ocrScale);
                AddQuestWholeResultFallback(output, allLines, visibleText.ToString(),
                    globalTaskId, ocrScale);
            }
            HashSet<OcrPanelInfo> handledSkillPanels = new HashSet<OcrPanelInfo>();
            HashSet<OcrPanelInfo> handledEquipmentPanels = new HashSet<OcrPanelInfo>();
            for (int lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
            {
                OcrLine line = allLines[lineIndex];
                OcrPanelInfo currentPanel;
                panelByLine.TryGetValue(line, out currentPanel);
                bool questInterface = currentPanel != null && currentPanel.IsQuest;
                string activeTaskId = questInterface && currentPanel.TaskId.Length > 0
                    ? currentPanel.TaskId : globalTaskId;
                if (currentPanel != null && currentPanel.IsSkillDetail &&
                    handledSkillPanels.Add(currentPanel))
                {
                    AddSkillPanelLabels(output, currentPanel, ocrScale);
                }
                if (currentPanel != null && currentPanel.IsSkillDetail) continue;

                bool equipmentPanel = currentPanel != null && currentPanel.IsEquipmentDetail;
                if (equipmentPanel && handledEquipmentPanels.Add(currentPanel))
                    AddEquipmentPanelLabels(output, currentPanel, ocrScale);

                string structuredLine;
                bool lineEquipmentStructure = LooksLikeEquipmentStatText(line.Text);
                if (TryTranslateStructuredLine(line.Text,
                    equipmentPanel || globalEquipmentContext || lineEquipmentStructure,
                    false, out structuredLine))
                {
                    AddWholeLineLabel(output, line, structuredLine, ocrScale,
                        structuredLine.Length > 24);
                    continue;
                }

                List<MatchResult> characterStatMatches = translations.FindCharacterStatMatches(line.Text);
                bool characterStatLine = currentPanel != null && currentPanel.IsCharacterStats;
                bool earlyEquipmentStats = lineEquipmentStructure || characterStatLine;
                if (equipmentPanel && LooksLikeEquipmentStatText(line.Text)) continue;
                if (earlyEquipmentStats)
                {
                    List<MatchResult> statMatches = characterStatLine
                        ? characterStatMatches : translations.FindMatches(line.Text);
                    if (statMatches.Count > 0)
                    {
                        AddExactLabels(output, new List<OcrLine> { line }, line.Text, statMatches, ocrScale);
                        continue;
                    }
                }
                // Dialogue, shop and crafting descriptions are often wrapped over several OCR
                // lines without a quest ID. Prefer one high-coverage interface-text match over
                // several unrelated short dictionary hits inside the sentence.
                List<OcrLine> interfaceLines = new List<OcrLine>();
                StringBuilder interfaceText = new StringBuilder();
                List<MatchResult> bestInterfaceMatches = null;
                string bestInterfaceText = ""; int bestInterfaceSpan = 0; int bestInterfaceLength = 0;
                for (int span = 0; span < 8 && lineIndex + span < allLines.Count; span++)
                {
                    OcrLine candidateLine = allLines[lineIndex + span];
                    if (span > 0 && !CanJoinOcrLines(allLines[lineIndex + span - 1], candidateLine)) break;
                    interfaceLines.Add(candidateLine);
                    if (interfaceText.Length > 0) interfaceText.Append(' ');
                    interfaceText.Append(candidateLine.Text);
                    List<MatchResult> interfaceMatches = translations.FindInterfaceTextMatches(interfaceText.ToString());
                    int longest = 0;
                    foreach (MatchResult match in interfaceMatches) longest = Math.Max(longest, match.Entry.Normalized.Length);
                    if (longest > bestInterfaceLength)
                    {
                        bestInterfaceLength = longest; bestInterfaceMatches = interfaceMatches;
                        bestInterfaceText = interfaceText.ToString(); bestInterfaceSpan = span + 1;
                    }
                }
                if (bestInterfaceMatches != null && bestInterfaceMatches.Count > 0)
                {
                    // Short static UI words (ALL, LEVEL, MESOS, SHOP...) are not useful
                    // translations by themselves. They used to win before the actual dialogue
                    // or description and produced dozens of tiny boxes across the HUD.
                    bestInterfaceMatches.RemoveAll(delegate(MatchResult match) {
                        return !match.Entry.IsInterfaceText && match.Entry.Normalized.Length < 18;
                    });
                }
                if (bestInterfaceMatches != null && bestInterfaceMatches.Count > 0)
                {
                    AddExactLabels(output, interfaceLines.GetRange(0, bestInterfaceSpan),
                        bestInterfaceText, bestInterfaceMatches, ocrScale);
                    // Windows OCR can flatten several nearby UI blocks into one very long line.
                    // Keep non-overlapping equipment attributes from the same OCR line instead
                    // of dropping everything after the matched dialogue/crafting sentence.
                    List<MatchResult> secondaryMatches = translations.FindMatches(bestInterfaceText);
                    secondaryMatches.RemoveAll(delegate(MatchResult secondary) {
                        foreach (MatchResult primary in bestInterfaceMatches)
                            if (secondary.Start < primary.Start + primary.Length &&
                                primary.Start < secondary.Start + secondary.Length) return true;
                        return false;
                    });
                    string normalizedSecondaryLine = TranslationStore.Normalize(bestInterfaceText);
                    bool secondaryEquipmentStats = LooksLikeEquipmentStatText(bestInterfaceText);
                    if (normalizedSecondaryLine.Length > 28 && !secondaryEquipmentStats)
                        secondaryMatches.RemoveAll(delegate(MatchResult match) {
                            return match.Length < 12 || match.Length * 3 < normalizedSecondaryLine.Length;
                        });
                    if (secondaryMatches.Count > 0)
                        AddExactLabels(output, interfaceLines.GetRange(0, bestInterfaceSpan),
                            bestInterfaceText, secondaryMatches, ocrScale);
                    lineIndex += bestInterfaceSpan - 1; continue;
                }
                bool skillDetailStart = !questInterface && translations.LooksLikeSkillTextStart(line.Text);
                bool itemDetailStart = !questInterface && !skillDetailStart && translations.LooksLikeItemTextStart(line.Text);
                if (skillDetailStart || itemDetailStart)
                {
                    string detailId = skillDetailStart ? translations.DetectSkillId(line.Text) :
                        translations.DetectItemId(line.Text);
                    List<OcrLine> detailLines = new List<OcrLine>();
                    StringBuilder detailText = new StringBuilder();
                    List<MatchResult> bestDetailMatches = null;
                    string bestDetailText = ""; int bestDetailLineCount = 0; int bestDetailAdvance = 0;
                    int bestDetailLength = 0;
                    int bestDetailCoverage = 0;
                    int skippedDetailNoise = 0;
                    for (int scan = 0; scan < 9 && lineIndex + scan < allLines.Count; scan++)
                    {
                        OcrLine candidateLine = allLines[lineIndex + scan];
                        if (detailLines.Count > 0 && !CanJoinOcrLines(detailLines[detailLines.Count - 1], candidateLine))
                        {
                            // OCR ordering can interleave a small notification or watermark that is
                            // spatially outside the tooltip. Skip at most two such lines, then resume
                            // only if the next line continues the same tooltip block.
                            if (skippedDetailNoise < 2) { skippedDetailNoise++; continue; }
                            break;
                        }
                        detailLines.Add(candidateLine);
                        if (detailText.Length > 0) detailText.Append(' ');
                        detailText.Append(candidateLine.Text);
                        List<MatchResult> detailMatches = skillDetailStart
                            ? translations.FindSkillTextMatches(detailText.ToString(), detailId)
                            : translations.FindItemTextMatches(detailText.ToString(), detailId);
                        detailMatches.RemoveAll(delegate(MatchResult match) { return match.Length < 8; });
                        int longest = 0;
                        // Approximate matches cover the OCR fragment, so ranking by Match.Length
                        // would reward appending unrelated lines. Rank by the dictionary phrase
                        // instead: the first tight fragment wins unless a genuinely fuller entry appears.
                        foreach (MatchResult match in detailMatches) longest = Math.Max(longest, match.Entry.Normalized.Length);
                        int coverage = detailMatches.Count > 0 ? TranslationStore.Normalize(detailText.ToString()).Length : 0;
                        if (longest > bestDetailLength || (longest == bestDetailLength && coverage > bestDetailCoverage))
                        {
                            bestDetailLength = longest; bestDetailMatches = detailMatches;
                            bestDetailText = detailText.ToString(); bestDetailLineCount = detailLines.Count;
                            bestDetailAdvance = scan + 1;
                            bestDetailCoverage = coverage;
                        }
                    }
                    if (bestDetailMatches != null && bestDetailMatches.Count > 0)
                    {
                        List<OcrLine> matchedLines = detailLines.GetRange(0, bestDetailLineCount);
                        AddExactLabels(output, matchedLines, bestDetailText, bestDetailMatches, ocrScale);
                        lineIndex += bestDetailAdvance - 1; continue;
                    }
                }
                string normalizedCurrentLine = TranslationStore.Normalize(line.Text);
                bool equipmentStatLine = LooksLikeEquipmentStatText(line.Text);
                bool taskTextContext = questInterface || (globalTaskId.Length > 0 &&
                    normalizedCurrentLine.Length >= 18 &&
                    (currentPanel == null || (!currentPanel.IsSkillDetail && !currentPanel.IsEquipmentDetail &&
                     !currentPanel.IsCharacterStats)));
                bool taskSceneAnchor = visibleScene.IsStrong(SceneKind.Quest) &&
                    (normalizedCurrentLine.Contains("quest helper") ||
                     ContainsWholeNormalizedPhrase(normalizedCurrentLine, "exp"));
                List<MatchResult> matches;
                if (taskSceneAnchor && !equipmentStatLine)
                {
                    // Panel title and reward amount are small but semantically important.
                    // Route them explicitly instead of weakening the generic short-word filter,
                    // which would bring back dozens of ALL/LEVEL/MESOS labels across the HUD.
                    matches = translations.FindMatches(line.Text);
                }
                else if (taskTextContext && !equipmentStatLine)
                {
                    // A quest list and the helper can show several different quest titles
                    // around one selected quest body. Never constrain a short title row to
                    // the selected task ID; only prose/dialogue uses that context.
                    List<MatchResult> independentTaskNames =
                        translations.FindTaskMatches(line.Text, null);
                    independentTaskNames.RemoveAll(delegate(MatchResult match) {
                        return !match.Entry.IsTaskName;
                    });
                    matches = independentTaskNames.Count > 0
                        ? independentTaskNames
                        : translations.FindTaskMatches(line.Text, activeTaskId);
                }
                else matches = translations.FindMatches(line.Text);
                if (!taskTextContext && !taskSceneAnchor && !equipmentStatLine && matches.Count > 0)
                    KeepHighCoverageMatches(matches, normalizedCurrentLine.Length);
                if (!questInterface && normalizedCurrentLine.Length > 28 && !equipmentStatLine)
                    matches.RemoveAll(delegate(MatchResult match) {
                        int lineLength = normalizedCurrentLine.Length;
                        return match.Length < 12 || match.Length * 3 < lineLength;
                    });
                if (taskTextContext && matches.Count == 0 && TranslationStore.Normalize(line.Text).Length <= 18)
                    matches = translations.FindMatches(line.Text); // buttons, rewards and short item names only
                if (matches.Count == 0)
                {
                    // Quest dialogue is commonly wrapped into several visual OCR lines.
                    // Combine only nearby consecutive lines and accept long quest-text entries.
                    List<OcrLine> combinedLines = new List<OcrLine>();
                    StringBuilder combinedText = new StringBuilder();
                    List<MatchResult> bestTaskMatches = null;
                    string bestTaskText = ""; int bestTaskSpan = 0; int bestTaskCoverage = 0;
                    for (int span = 0; span < 7 && lineIndex + span < allLines.Count; span++)
                    {
                        OcrLine candidateLine = allLines[lineIndex + span];
                        if (span > 0 && !CanJoinOcrLines(allLines[lineIndex + span - 1], candidateLine)) break;
                        combinedLines.Add(candidateLine);
                        if (combinedText.Length > 0) combinedText.Append(' ');
                        combinedText.Append(candidateLine.Text);
                        if (span == 0) continue;
                        List<MatchResult> combinedMatches = taskTextContext
                            ? translations.FindTaskMatches(combinedText.ToString(), activeTaskId)
                            : new List<MatchResult>();
                        combinedMatches.RemoveAll(delegate(MatchResult match) {
                            return match.Length < 20 || !match.Entry.Category.StartsWith("怀旧服-任务", StringComparison.Ordinal);
                        });
                        int coverage = combinedMatches.Count > 0 ? TranslationStore.Normalize(combinedText.ToString()).Length : 0;
                        if (coverage > bestTaskCoverage)
                        {
                            bestTaskMatches = combinedMatches; bestTaskText = combinedText.ToString();
                            bestTaskSpan = span + 1; bestTaskCoverage = coverage;
                        }
                    }
                    if (bestTaskMatches != null && bestTaskMatches.Count > 0)
                    {
                        AddExactLabels(output, combinedLines.GetRange(0, bestTaskSpan), bestTaskText, bestTaskMatches, ocrScale);
                        lineIndex += bestTaskSpan - 1;
                        continue;
                    }
                    OverlayLabel assisted = BuildIconAssistedLabel(line, ocrScale, prepared);
                    if (assisted != null) output.Add(assisted);
                    continue;
                }
                bool iconBackedName = false;
                foreach (MatchResult match in matches)
                    if (match.Entry.HasIcon) { iconBackedName = true; break; }
                if (iconBackedName && normalizedCurrentLine.Length <= 42)
                {
                    // Text-only OCR commonly confuses visually similar names (for example
                    // Kitty/Nitty). Resolve a short icon-backed row with both signals before
                    // committing the textual match; otherwise a perfect OCR typo can select
                    // the wrong item even though the adjacent icon is unambiguous.
                    OverlayLabel resolved = BuildIconAssistedLabel(line, ocrScale, prepared);
                    if (resolved != null) { output.Add(resolved); continue; }
                    // In a confirmed item scene, a single high-coverage name is still useful
                    // when the tiny icon is too blurred for a reliable hash (Lionheart is a
                    // public regression sample). Outside an item scene the same fallback
                    // turned player/job names into unrelated equipment, so reject it there.
                    bool strongFocusedItemName = globalEquipmentContext && matches.Count == 1 &&
                        matches[0].Entry.HasIcon && normalizedCurrentLine.Length >= 8 &&
                        matches[0].Length * 10 >= normalizedCurrentLine.Length * 7;
                    if (!strongFocusedItemName) continue;
                }
                AddExactLabels(output, new List<OcrLine> { line }, line.Text, matches, ocrScale);
            }
            // A preceding multi-line dictionary match may advance lineIndex past a compact
            // equipment or skill-level row even though OCR captured that row correctly.
            // Revisit only explicit structured prefixes so ordinary dialogue numbers cannot
            // be mistaken for live panel values.
            foreach (OcrLine line in allLines)
            {
                string structured;
                if (LooksLikeEquipmentStatText(line.Text) &&
                    TryTranslateStructuredLine(line.Text, true, false, out structured))
                    AddWholeLineLabel(output, line, structured, ocrScale,
                        structured.Length > 24);
                bool skillLevel = line.Text.IndexOf("master level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.Text.IndexOf("current level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.Text.IndexOf("next level", StringComparison.OrdinalIgnoreCase) >= 0;
                if (skillLevel && TryTranslateStructuredLine(line.Text, false, true, out structured))
                    AddWholeLineLabel(output, line, structured, ocrScale, false);
            }
            MergeAdjacentLongLabels(output);
            MergeOverlappingSameTextLabels(output);
            MergeSameTextLabels(output);
            RemoveContainedLabels(output);
            return output;
        }

        private static void KeepHighCoverageMatches(List<MatchResult> matches, int lineLength)
        {
            if (matches == null || matches.Count == 0 || lineLength <= 0) return;
            int covered = 0;
            foreach (MatchResult match in matches) covered += Math.Max(0, match.Length);
            bool collectivelyMeaningful = covered * 10 >= lineLength * 6;
            matches.RemoveAll(delegate(MatchResult match) {
                // The generic dictionary must not paint small static UI vocabulary across
                // unrelated screens. Dedicated panel layouts and long-interface matching
                // remain responsible for those labels.
                bool shortInterface = match.Entry.Category.StartsWith("怀旧服-界面",
                    StringComparison.Ordinal) && match.Entry.Normalized.Length < 18;
                if (shortInterface) return true;
                return !collectivelyMeaningful && match.Length * 10 < lineLength * 7;
            });
        }

        private void AddStablePanelLayouts(List<OverlayLabel> output, List<OcrLine> lines,
            Dictionary<OcrLine, OcrPanelInfo> panelByLine, float ocrScale)
        {
            bool quest = false, characterStat = false, characterInfo = false, playerShop = false;
            // A task title/dialogue can occur in an NPC conversation without the Quest window.
            // Template labels are safe only when the surrounding Quest shell is also visible.
            // This prevents one dialogue line from projecting non-existent tabs/buttons across
            // the screen while still tolerating one missed header or tab in small classic UI.
            bool allowQuestLayout = captureBounds == gameBounds && HasQuestWindowShell(lines);
            foreach (OcrLine line in lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                OcrPanelInfo panel;
                panelByLine.TryGetValue(line, out panel);
                if (allowQuestLayout && !quest && translations.DetectTaskId(line.Text).Length > 0 &&
                    IsMainQuestWindowTaskLine(line, ocrScale))
                {
                    AddQuestWindowLayoutLabels(output, line, ocrScale); quest = true;
                }
                if (!characterStat && normalized.Contains("character stat"))
                {
                    AddCharacterStatLayoutLabels(output, line, ocrScale); characterStat = true;
                }
                else if (!characterStat && panel != null && panel.IsCharacterStats &&
                    normalized.StartsWith("name", StringComparison.Ordinal))
                {
                    AddCharacterStatLayoutFromName(output, line, ocrScale); characterStat = true;
                }
                if (!characterInfo && normalized.Contains("character info"))
                {
                    AddCharacterInfoLayoutLabels(output, line, ocrScale); characterInfo = true;
                }
                else if (!characterInfo && normalized.Contains("citizenship"))
                {
                    AddCharacterInfoLayoutFromCitizenship(output, line, ocrScale); characterInfo = true;
                }
                if (!playerShop && normalized.Contains("leave store"))
                {
                    AddPlayerShopLayoutLabels(output, line, ocrScale); playerShop = true;
                }
                if (quest && characterStat && characterInfo && playerShop) break;
            }
        }

        private static bool HasQuestWindowShell(List<OcrLine> lines)
        {
            bool header = false;
            int tabs = 0;
            bool footer = false;
            foreach (OcrLine line in lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                if (normalized == "quest" ||
                    (normalized.StartsWith("quest ", StringComparison.Ordinal) &&
                     !normalized.StartsWith("quest helper", StringComparison.Ordinal)))
                    header = true;
                if (normalized == "available" || normalized.StartsWith("available ", StringComparison.Ordinal)) tabs++;
                if (normalized == "in progress" || normalized.StartsWith("in progress ", StringComparison.Ordinal)) tabs++;
                if (normalized == "completed" || normalized.StartsWith("completed ", StringComparison.Ordinal)) tabs++;
                if (normalized.Contains("details") || normalized.Contains("forfeit")) footer = true;
            }
            return tabs >= 2 || (header && (tabs >= 1 || footer));
        }

        private void AddQuestPanelTextLabels(List<OverlayLabel> output,
            List<OcrLine> lines, string taskId, float ocrScale)
        {
            Dictionary<string, SkillPanelCandidate> candidates =
                new Dictionary<string, SkillPanelCandidate>(StringComparer.Ordinal);
            for (int start = 0; start < lines.Count; start++)
            {
                List<OcrLine> window = new List<OcrLine>();
                StringBuilder combined = new StringBuilder();
                int skippedNoise = 0;
                for (int scan = 0; scan < 12 && start + scan < lines.Count; scan++)
                {
                    OcrLine current = lines[start + scan];
                    if (window.Count > 0 && !CanJoinOcrLines(window[window.Count - 1], current))
                    {
                        // OCR ordering can interleave a nearby notification or HUD label with
                        // wrapped quest prose. Ignore at most two spatially disconnected lines;
                        // the task ID and long-text matcher still guard against cross-panel joins.
                        if (skippedNoise < 2) { skippedNoise++; continue; }
                        break;
                    }
                    window.Add(current);
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(current.Text);
                    string text = combined.ToString();
                    if (TranslationStore.Normalize(text).Length < 24) continue;
                    foreach (MatchResult match in translations.FindTaskMatches(text, taskId))
                    {
                        if (!match.Entry.IsTaskText || match.Entry.Normalized.Length < 24) continue;
                        int score = match.Entry.Normalized.Length * 1000 -
                            Math.Abs(TranslationStore.Normalize(text).Length - match.Entry.Normalized.Length);
                        string key = match.Entry.Category + "\t" + match.Entry.English;
                        SkillPanelCandidate previous;
                        if (!candidates.TryGetValue(key, out previous) || score > previous.Score)
                            candidates[key] = new SkillPanelCandidate {
                                Lines = new List<OcrLine>(window), Text = text,
                                Match = match, Score = score
                            };
                    }
                }
            }
            List<SkillPanelCandidate> ranked = new List<SkillPanelCandidate>(candidates.Values);
            ranked.Sort(delegate(SkillPanelCandidate left, SkillPanelCandidate right) {
                return right.Score.CompareTo(left.Score);
            });
            List<RectangleF> used = new List<RectangleF>();
            int added = 0;
            foreach (SkillPanelCandidate candidate in ranked)
            {
                List<OcrLine> tight = SelectBestDetailLines(candidate.Lines, candidate.Match.Entry);
                RectangleF raw = GetCombinedLineBounds(tight);
                if (raw.IsEmpty) continue;
                bool overlaps = false;
                foreach (RectangleF previous in used)
                {
                    RectangleF intersection = RectangleF.Intersect(previous, raw);
                    float smaller = Math.Min(previous.Width * previous.Height, raw.Width * raw.Height);
                    if (smaller > 0 && intersection.Width * intersection.Height / smaller > 0.35f)
                    { overlaps = true; break; }
                }
                if (overlaps) continue;
                AddDetailBlockLabel(output, tight, candidate.Match.Entry.Chinese, ocrScale);
                used.Add(raw);
                if (++added >= 3) break;
            }
        }

        private void AddQuestWholeResultFallback(List<OverlayLabel> output,
            List<OcrLine> lines, string text, string taskId, float ocrScale)
        {
            foreach (MatchResult match in translations.FindTaskMatches(text, taskId))
            {
                if (!match.Entry.IsTaskText || match.Entry.Normalized.Length < 24) continue;
                bool alreadyAdded = false;
                foreach (OverlayLabel label in output)
                    if (SameOverlayText(label.Text, match.Entry.Chinese))
                    { alreadyAdded = true; break; }
                if (alreadyAdded) continue;

                List<OcrLine> related = new List<OcrLine>();
                foreach (OcrLine line in lines)
                {
                    string normalized = TranslationStore.Normalize(line.Text);
                    HashSet<string> words = new HashSet<string>(normalized.Split(
                        new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                        StringComparer.Ordinal);
                    int common = 0;
                    foreach (string word in words)
                        if (word.Length >= 3 && match.Entry.DetailWords.Contains(word)) common++;
                    if (common >= 3) related.Add(line);
                }
                // The whole-result matcher already proved the long text belongs to this task.
                // Word-selected lines exclude interleaved HUD noise while preserving its bounds.
                if (related.Count >= 2)
                    AddDetailBlockLabel(output, related, match.Entry.Chinese, ocrScale);
            }
        }

        private void AddCharacterStatLayoutLabels(List<OverlayLabel> output, OcrLine header, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(header);
            if (raw.IsEmpty) return;
            float left = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float top = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float headerHeight = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, headerHeight / 14.0f));
            AddCharacterStatLayoutLabelsAt(output, left, top, scale);
        }

        private void AddCharacterStatLayoutFromName(List<OverlayLabel> output,
            OcrLine nameLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(nameLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, height / 14.0f));
            float nameLeft = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float nameTop = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            AddCharacterStatLayoutLabelsAt(output, nameLeft + 2.0f * scale,
                nameTop - 44.0f * scale, scale);
        }

        private static void AddCharacterStatLayoutLabelsAt(List<OverlayLabel> output,
            float left, float top, float scale)
        {
            AddCharacterStatLayoutLabelsAt(output, left, top, scale,
                left + 360.0f * scale, top + 112.0f * scale);
        }

        private static void AddCharacterStatLayoutLabelsAt(List<OverlayLabel> output,
            float left, float top, float scale, float rightLeft, float rightTop)
        {
            float row = 34.0f * scale, labelHeight = Math.Max(18.0f, 20.0f * scale);
            // The title is often rendered in a stylized pixel font that full-screen OCR misses.
            // A verified character-panel geometry is stronger evidence than that single glyph row.
            AddLayoutLabel(output, "角色属性", new RectangleF(left - 2, top - 2,
                150.0f * scale, labelHeight));
            string[] leftUpper = new string[] { "名称", "职业", "等级", "生命值", "魔法值", "经验", "人气" };
            // JOB has a second line for the advancement name, so LEVEL and the rows
            // below it start about half a row later than a uniform grid predicts.
            float[] leftOffsets = new float[] { 44, 78, 128, 162, 196, 230, 264 };
            for (int i = 0; i < leftUpper.Length; i++)
                AddLayoutLabel(output, leftUpper[i], new RectangleF(left - 2, top + leftOffsets[i] * scale,
                    104.0f * scale, labelHeight));
            float attributesTop = top + 310.0f * scale;
            string[] attributes = new string[] { "力量", "敏捷", "智力", "运气" };
            for (int i = 0; i < attributes.Length; i++)
                AddLayoutLabel(output, attributes[i], new RectangleF(left - 2, attributesTop + row * i,
                    104.0f * scale, labelHeight));
            AddLayoutLabel(output, "能力值点数", new RectangleF(left - 2,
                attributesTop + row * attributes.Length + 18.0f * scale, 150.0f * scale, labelHeight));

            string[] right = new string[] { "攻击力", "物理防御力", "魔法攻击力", "魔法防御力", "命中率",
                "回避率", "暴击率", "暴击伤害", "移动速度", "跳跃力" };
            for (int i = 0; i < right.Length; i++)
                AddLayoutLabel(output, right[i], new RectangleF(rightLeft, rightTop + row * i,
                    118.0f * scale, labelHeight));
        }

        private void AddCharacterInfoLayoutLabels(List<OverlayLabel> output, OcrLine header, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(header);
            if (raw.IsEmpty) return;
            float left = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float top = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float headerHeight = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, headerHeight / 14.0f));
            AddCharacterInfoLayoutLabelsAt(output, left, top, scale);
        }

        private void AddCharacterInfoLayoutFromCitizenship(List<OverlayLabel> output,
            OcrLine citizenshipLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(citizenshipLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, height / 14.0f));
            float citizenshipLeft = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float citizenshipTop = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            AddCharacterInfoLayoutLabelsAt(output, citizenshipLeft - 228.0f * scale,
                citizenshipTop - 42.0f * scale, scale);
        }

        private static void AddCharacterInfoLayoutLabelsAt(List<OverlayLabel> output,
            float left, float top, float scale)
        {
            float labelHeight = Math.Max(18.0f, 20.0f * scale);

            AddLayoutLabel(output, "角色信息", new RectangleF(left - 2, top - 2,
                150.0f * scale, labelHeight));
            AddLayoutLabel(output, "公民身份", new RectangleF(left + 228.0f * scale,
                top + 42.0f * scale, 150.0f * scale, labelHeight));
            string[] fields = new string[] { "等级", "职业", "人气", "家族" };
            for (int i = 0; i < fields.Length; i++)
                AddLayoutLabel(output, fields[i], new RectangleF(left + 230.0f * scale,
                    top + (105.0f + i * 35.0f) * scale, 130.0f * scale, labelHeight));
            AddLayoutLabel(output, "邀请组队", new RectangleF(left + 232.0f * scale,
                top + 240.0f * scale, 160.0f * scale, labelHeight));
            AddLayoutLabel(output, "申请交易", new RectangleF(left + 410.0f * scale,
                top + 240.0f * scale, 160.0f * scale, labelHeight));
            AddLayoutLabel(output, "查看宠物信息", new RectangleF(left + 405.0f * scale,
                top + 298.0f * scale, 180.0f * scale, labelHeight));
        }

        private void AddPlayerShopLayoutLabels(List<OverlayLabel> output,
            OcrLine leaveStoreLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(leaveStoreLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.82f, Math.Min(1.55f, height / 16.0f));
            float left = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float top = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float labelHeight = Math.Max(18.0f, 20.0f * scale);
            AddLayoutLabel(output, "卖家信息", new RectangleF(left - 2.0f * scale,
                top - 92.0f * scale, 142.0f * scale, labelHeight));
            AddLayoutLabel(output, "购买道具", new RectangleF(left - 2.0f * scale,
                top - 46.0f * scale, 142.0f * scale, labelHeight));
            AddLayoutLabel(output, "离开商店", new RectangleF(left - 2.0f * scale,
                top - 2.0f * scale, 142.0f * scale, labelHeight));
        }

        private bool IsMainQuestWindowTaskLine(OcrLine line, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty) return false;
            float x = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float y = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            return x < Bounds.Width * 0.68f && y > Bounds.Height * 0.18f;
        }

        private void AddQuestWindowLayoutLabels(List<OverlayLabel> output,
            OcrLine taskLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(taskLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(15.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.82f, Math.Min(1.45f, height / 20.0f));
            float taskLeft = raw.Left / ocrScale + captureBounds.Left - Bounds.Left;
            float taskTop = raw.Top / ocrScale + captureBounds.Top - Bounds.Top;
            float left = taskLeft - 78.0f * scale;
            float top = taskTop - 126.0f * scale;
            float labelHeight = Math.Max(18.0f, 21.0f * scale);

            AddLayoutLabel(output, "任务", new RectangleF(left + 4.0f * scale,
                top + 3.0f * scale, 90.0f * scale, labelHeight));
            AddLayoutLabel(output, "可接取", new RectangleF(left + 18.0f * scale,
                top + 36.0f * scale, 112.0f * scale, labelHeight));
            AddLayoutLabel(output, "进行中", new RectangleF(left + 145.0f * scale,
                top + 36.0f * scale, 124.0f * scale, labelHeight));
            AddLayoutLabel(output, "已完成", new RectangleF(left + 282.0f * scale,
                top + 36.0f * scale, 112.0f * scale, labelHeight));
            AddLayoutLabel(output, "详情", new RectangleF(left + 330.0f * scale,
                top + 620.0f * scale, 112.0f * scale, labelHeight));
            AddLayoutLabel(output, "任务助手", new RectangleF(left + 742.0f * scale,
                top + 620.0f * scale, 148.0f * scale, labelHeight));
            AddLayoutLabel(output, "放弃任务", new RectangleF(left + 900.0f * scale,
                top + 620.0f * scale, 122.0f * scale, labelHeight));
        }

        private static void AddLayoutLabel(List<OverlayLabel> output, string text, RectangleF bounds)
        {
            foreach (OverlayLabel existing in output)
            {
                if (!String.Equals(existing.Text, text, StringComparison.Ordinal)) continue;
                RectangleF overlap = RectangleF.Intersect(existing.Bounds, bounds);
                if (overlap.Width > 0 && overlap.Height > 0) return;
            }
            output.Add(new OverlayLabel { Bounds = bounds, Text = text, Wrap = false });
        }

        private static string ExtractStructuredValue(string normalized, string expression)
        {
            Match match = Regex.Match(normalized, expression, RegexOptions.IgnoreCase);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : "";
        }

        private static string LastNumber(string text)
        {
            MatchCollection values = Regex.Matches(text ?? "", @"[+-]?\d+(?:\.\d+)?%?");
            return values.Count == 0 ? "" : values[values.Count - 1].Value;
        }

        private static string ExtractStructuredOcrNumber(string normalized, string prefixExpression)
        {
            Match match = Regex.Match(normalized ?? "", prefixExpression +
                @"\s*([+-]?[0-9lIoOgqSsB]+(?:\.[0-9lIoOgqSsB]+)?%?)",
                RegexOptions.IgnoreCase);
            return match.Success ? RepairOcrNumber(match.Groups[1].Value) : "";
        }

        private static string LastStructuredOcrNumber(string text)
        {
            MatchCollection values = Regex.Matches(text ?? "",
                @"(?<![a-z])([+-]?[0-9lIoOgqSsB]+(?:\.[0-9lIoOgqSsB]+)?%?)(?![a-z])",
                RegexOptions.IgnoreCase);
            return values.Count == 0 ? "" : RepairOcrNumber(values[values.Count - 1].Groups[1].Value);
        }

        private static string SkillLevelNumber(string original, string normalized, string phrase)
        {
            string value = ExtractStructuredValue(normalized,
                Regex.Escape(phrase) + @"\s*([0-9]{1,3})");
            if (value.Length == 0) value = LastNumber(normalized);
            if (value.Length <= 1 || !value.EndsWith("1", StringComparison.Ordinal)) return value;

            // Classic UI surrounds these labels with [brackets]. Windows OCR sometimes reads
            // the closing ] as an extra 1 ("[Current Level 5]" -> "[Current Level 51").
            // Repair only an unmatched opening bracket, so legitimate levels 11/21 remain intact.
            string source = original ?? "";
            int phraseIndex = source.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            if (phraseIndex < 0) return value;
            int squareOpen = source.LastIndexOf('[', phraseIndex);
            int roundOpen = source.LastIndexOf('(', phraseIndex);
            int opening = Math.Max(squareOpen, roundOpen);
            if (opening < 0) return value;
            int squareClose = source.IndexOf(']', phraseIndex);
            int roundClose = source.IndexOf(')', phraseIndex);
            if (squareClose >= 0 || roundClose >= 0) return value;
            return value.Substring(0, value.Length - 1);
        }

        private static string RepairOcrNumber(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return value.Replace('l', '1').Replace('I', '1').Replace('g', '9')
                .Replace('q', '9').Replace('O', '0').Replace('o', '0')
                .Replace('S', '5').Replace('s', '5').Replace('B', '8').Replace('b', '8');
        }

        private static string TranslateEquipmentType(string normalized)
        {
            string[,] values = new string[,] {
                { "two handed blunt weapon", "双手钝器" }, { "one handed blunt weapon", "单手钝器" },
                { "two handed sword", "双手剑" }, { "one handed sword", "单手剑" },
                { "crossbow", "弩" }, { "polearm", "战戟" }, { "knuckle", "指虎" },
                { "earrings", "耳环" }, { "overall", "套服" }, { "shield", "盾牌" },
                { "staff", "长杖" }, { "wand", "短杖" }, { "dagger", "短刀" },
                { "claw", "拳套" }, { "spear", "枪" }, { "shoes", "鞋子" },
                { "gloves", "手套" }, { "bottom", "下装" }, { "cape", "披风" },
                { "bow", "弓" }, { "gun", "枪械" }, { "hat", "帽子" },
                { "top", "上衣" }
            };
            for (int i = 0; i < values.GetLength(0); i++)
                if (ContainsWholeNormalizedPhrase(normalized, values[i, 0])) return values[i, 1];
            return "";
        }

        private static bool ContainsWholeNormalizedPhrase(string normalized, string phrase)
        {
            if (String.IsNullOrEmpty(normalized) || String.IsNullOrEmpty(phrase)) return false;
            return (" " + normalized + " ").IndexOf(" " + phrase + " ",
                StringComparison.Ordinal) >= 0;
        }

        private static int StructuredEditDistance(string left, string right)
        {
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1,
                        previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }

        private static string CanonicalizeStructuredToken(string token)
        {
            if (String.IsNullOrEmpty(token)) return "";
            return token.Replace("0", "o").Replace("1", "i").Replace("4", "a")
                .Replace("+", "in");
        }

        private static bool ContainsApproximateStructuredToken(string normalized,
            string expected, float minimumSimilarity)
        {
            foreach (string raw in normalized.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string token = CanonicalizeStructuredToken(raw);
                if (token == expected) return true;
                if (token.Length < Math.Max(2, expected.Length / 2) ||
                    Math.Abs(token.Length - expected.Length) > Math.Max(3, expected.Length / 2)) continue;
                int distance = StructuredEditDistance(token, expected);
                float similarity = 1.0f - (float)distance / Math.Max(token.Length, expected.Length);
                if (similarity >= minimumSimilarity) return true;
            }
            return false;
        }

        private bool TryTranslateStructuredLine(string text, bool equipmentContext,
            bool skillContext, out string translated)
        {
            translated = "";
            string normalized = TranslationStore.Normalize(text);
            if (normalized.Length == 0) return false;
            List<string> fields = new List<string>();
            string value;

            // Quest Helper objectives use a compact "3/10 Snail" form. It is
            // structurally distinct from chat/player names, so translate only the
            // dictionary-backed monster or item and preserve the live counter.
            Match progress = Regex.Match(text ?? "",
                @"^\s*[•·*\-]?\s*([\dlIoO]+)\s*/\s*([\dlIoO]+)\s+(.{2,80}?)\s*$",
                RegexOptions.IgnoreCase);
            if (progress.Success)
            {
                string objective = progress.Groups[3].Value.Trim(' ', '.', ':', '-', '*');
                TranslationEntry bestObjective = null;
                foreach (MatchResult match in translations.FindMatches(objective))
                {
                    bool allowed = match.Entry.Category.StartsWith("怀旧服-怪物#", StringComparison.Ordinal) ||
                        match.Entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal) ||
                        match.Entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal);
                    if (!allowed) continue;
                    if (bestObjective == null || match.Entry.Normalized.Length > bestObjective.Normalized.Length)
                        bestObjective = match.Entry;
                }
                if (bestObjective != null)
                    fields.Add(RepairOcrNumber(progress.Groups[1].Value) + "/" +
                        RepairOcrNumber(progress.Groups[2].Value) + " " + bestObjective.Chinese);
            }

            bool requirementLine = normalized.Contains("req ") ||
                normalized.Contains("required ") ||
                (equipmentContext && Regex.IsMatch(normalized, @"\b(?:reo|r\s*eq)\b",
                    RegexOptions.IgnoreCase));
            if (requirementLine)
            {
                value = ExtractStructuredOcrNumber(normalized,
                    @"(?:req|required)?\s*(?:lev|level)");
                bool hasLevel = value.Length > 0 || normalized.Contains("req lev") ||
                    normalized.Contains("required level") ||
                    ContainsApproximateStructuredToken(normalized, "lev", 0.66f);
                if (hasLevel) fields.Add("需要等级" + (value.Length > 0 ? "：" + value : ""));
                string[] keys = new string[] { "str", "dex|oex|0ex", "int", "luk", "fam|fame" };
                string[] names = new string[] { "力量", "敏捷", "智力", "运气", "人气" };
                for (int i = 0; i < keys.Length; i++)
                {
                    value = ExtractStructuredOcrNumber(normalized,
                        @"(?:req|required)?\s*(?:" + keys[i] + @")");
                    string approximateKey = i == 4 ? "fame" : keys[i];
                    bool hasField = value.Length > 0 ||
                        ContainsApproximateStructuredToken(normalized, approximateKey,
                            approximateKey.Length <= 3 ? 0.66f : 0.56f) ||
                        (i == 4 && ContainsApproximateStructuredToken(normalized, "fam", 0.66f));
                    if (hasField) fields.Add("需要" + names[i] +
                        (value.Length > 0 ? "：" + value : ""));
                }
            }

            int jobCount = 0;
            string[] jobs = new string[] { "beginner", "warrior", "magician", "bowman", "thief", "pirate" };
            string[] jobNames = new string[] { "新手", "战士", "魔法师", "弓箭手", "飞侠", "海盗" };
            List<string> allowedJobs = new List<string>();
            for (int i = 0; i < jobs.Length; i++)
                if (normalized.Contains(jobs[i]) ||
                    ContainsApproximateStructuredToken(normalized, jobs[i], 0.52f))
                { jobCount++; allowedJobs.Add(jobNames[i]); }
            // A friend/party list can contain several job names on adjacent rows. Only an
            // already-confirmed equipment tooltip may turn a class ribbon into an allowed-job
            // field; job words by themselves are not equipment structure.
            if (equipmentContext && jobCount >= 2)
                fields.Add("可用职业：" + String.Join("／", allowedJobs.ToArray()));

            string type = TranslateEquipmentType(normalized);
            if ((equipmentContext || normalized.Contains("type ")) && type.Length > 0)
                fields.Add("类型：" + type);

            if (normalized.Contains("attack speed"))
            {
                string speed = normalized.Contains("fast") ? "快" :
                    (normalized.Contains("slow") ? "慢" : "普通");
                fields.Add("攻击速度：" + speed);
            }
            string[,] stats = new string[,] {
                { "weapon attack", "物理攻击力" }, { "magic attack", "魔法攻击力" },
                { "weapon def", "物理防御力" }, { "magic def", "魔法防御力" },
                { "avoidability", "回避率" }, { "accuracy", "命中率" },
                { "movement speed", "移动速度" }, { "jump", "跳跃力" }
            };
            for (int i = 0; i < stats.GetLength(0); i++)
            {
                if (!normalized.Contains(stats[i, 0])) continue;
                value = LastStructuredOcrNumber(normalized.Substring(
                    normalized.IndexOf(stats[i, 0], StringComparison.Ordinal)));
                fields.Add(stats[i, 1] + (value.Length > 0 ? "：" + value : ""));
            }
            if (equipmentContext && !requirementLine)
            {
                string[] attributeKeys = new string[] { "str", "dex|oex|0ex", "int", "luk" };
                string[] attributeNames = new string[] { "力量", "敏捷", "智力", "运气" };
                for (int i = 0; i < attributeKeys.Length; i++)
                {
                    value = ExtractStructuredOcrNumber(normalized,
                        @"\b(?:" + attributeKeys[i] + @")");
                    if (value.Length > 0) fields.Add(attributeNames[i] + "：" + value);
                }
            }
            if (equipmentContext && !normalized.Contains("magic def") &&
                Regex.IsMatch(normalized, @"\bmagic\s+[+-]?[0-9lIoOgqSsB]", RegexOptions.IgnoreCase))
            {
                value = ExtractStructuredOcrNumber(normalized, @"\bmagic");
                fields.Add("魔法防御力" + (value.Length > 0 ? "：" + value : ""));
            }
            if (normalized.Contains("remaining enhancements"))
            {
                value = LastStructuredOcrNumber(normalized);
                fields.Add("剩余强化次数" + (value.Length > 0 ? "：" + value : ""));
            }
            if (normalized.Contains("upgrades available"))
            {
                value = LastStructuredOcrNumber(normalized);
                fields.Add("可升级次数" + (value.Length > 0 ? "：" + value : ""));
            }

            if (skillContext || normalized.Contains("master level") ||
                normalized.Contains("current level") || normalized.Contains("next level") ||
                normalized.Contains("required skill"))
            {
                if (normalized.Contains("master level"))
                {
                    value = SkillLevelNumber(text, normalized, "master level");
                    fields.Add("最高等级" + (value.Length > 0 ? "：" + value : ""));
                }
                if (normalized.Contains("current level"))
                {
                    value = SkillLevelNumber(text, normalized, "current level");
                    fields.Add("当前等级" + (value.Length > 0 ? "：" + value : ""));
                }
                if (normalized.Contains("next level"))
                {
                    value = SkillLevelNumber(text, normalized, "next level");
                    fields.Add("下一级" + (value.Length > 0 ? "：" + value : ""));
                }
                if (normalized.Contains("required skill"))
                {
                    string level = ExtractStructuredValue(normalized, @"(?:level)\s*(\d+)");
                    string skillName = "";
                    foreach (MatchResult match in translations.FindMatches(text))
                        if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal))
                        { skillName = match.Entry.Chinese; break; }
                    fields.Add("需要技能：" + (skillName.Length > 0 ? skillName : "前置技能") +
                        (level.Length > 0 ? "至少" + level + "级" : ""));
                }
            }

            Match price = Regex.Match(text ?? "", @"([\dlI]+)\s+(?:for|tor)\s+([\dlIgqOo,]+)(?:\s+mesos?)?",
                RegexOptions.IgnoreCase);
            if (price.Success) fields.Add(RepairOcrNumber(price.Groups[1].Value) + "个，售价" +
                RepairOcrNumber(price.Groups[2].Value) + "金币");

            Match shopPresence = Regex.Match((text ?? "").Trim(),
                @"^([A-Za-z0-9_]{2,24})\s+has\s+(entered|left)\.?$",
                RegexOptions.IgnoreCase);
            if (shopPresence.Success)
                fields.Add(shopPresence.Groups[1].Value +
                    (shopPresence.Groups[2].Value.Equals("entered",
                        StringComparison.OrdinalIgnoreCase) ? " 已进入" : " 已离开"));

            if (fields.Count == 0) return false;
            translated = String.Join("　", fields.ToArray());
            return true;
        }

        private void AddWholeLineLabel(List<OverlayLabel> output, OcrLine line,
            string text, float ocrScale, bool wrap)
        {
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            RectangleF bounds = new RectangleF(raw.Left / ocrScale + captureBounds.Left - Bounds.Left - 3,
                raw.Top / ocrScale + captureBounds.Top - Bounds.Top - 2,
                Math.Max(34, raw.Width / ocrScale + 6), Math.Max(18, raw.Height / ocrScale + 5));
            output.Add(new OverlayLabel { Bounds = bounds, Text = text, Wrap = wrap });
        }

        private static List<List<OcrLine>> BuildVisualRows(List<OcrLine> source)
        {
            List<OcrLine> sorted = new List<OcrLine>(source);
            sorted.Sort(delegate(OcrLine left, OcrLine right) {
                RectangleF a = GetOcrLineBounds(left), b = GetOcrLineBounds(right);
                int top = a.Top.CompareTo(b.Top);
                return Math.Abs(a.Top - b.Top) <= Math.Max(a.Height, b.Height) * 0.55f
                    ? a.Left.CompareTo(b.Left) : top;
            });
            List<List<OcrLine>> rows = new List<List<OcrLine>>();
            foreach (OcrLine line in sorted)
            {
                RectangleF current = GetOcrLineBounds(line);
                List<OcrLine> target = null;
                foreach (List<OcrLine> row in rows)
                {
                    RectangleF bounds = GetCombinedLineBounds(row);
                    float centerDistance = Math.Abs((bounds.Top + bounds.Bottom) / 2.0f -
                        (current.Top + current.Bottom) / 2.0f);
                    if (centerDistance <= Math.Max(bounds.Height, current.Height) * 0.62f)
                    { target = row; break; }
                }
                if (target == null) { target = new List<OcrLine>(); rows.Add(target); }
                target.Add(line);
                target.Sort(delegate(OcrLine left, OcrLine right) {
                    return GetOcrLineBounds(left).Left.CompareTo(GetOcrLineBounds(right).Left);
                });
            }
            return rows;
        }

        private static string CombineVisualRow(List<OcrLine> row)
        {
            StringBuilder text = new StringBuilder();
            foreach (OcrLine line in row)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(line.Text);
            }
            return text.ToString();
        }

        private void AddEquipmentPanelLabels(List<OverlayLabel> output,
            OcrPanelInfo panel, float ocrScale)
        {
            int before = output.Count;
            foreach (List<OcrLine> row in BuildVisualRows(panel.Lines))
            {
                string translated;
                if (!TryTranslateStructuredLine(CombineVisualRow(row), true, false, out translated)) continue;
                AddDetailBlockLabel(output, row, translated, ocrScale);
            }
            string itemId = translations.DetectItemId(panel.Text);
            TranslationEntry overview = translations.GetItemOverview(itemId);
            List<OcrLine> prose = FindBestPanelProseBlock(panel.Lines);
            if (overview != null && prose.Count > 0 && output.Count == before)
                AddDetailBlockLabel(output, prose, overview.Chinese, ocrScale);
        }

        private void AddSkillPanelLabels(List<OverlayLabel> output, OcrPanelInfo panel, float ocrScale)
        {
            if (panel == null || panel.Lines.Count == 0) return;
            string skillId = panel.SkillId;
            if (skillId.Length == 0) skillId = translations.DetectSkillContentId(panel.Text);

            foreach (List<OcrLine> row in BuildVisualRows(panel.Lines))
            {
                string structured;
                if (TryTranslateStructuredLine(CombineVisualRow(row), false, true, out structured))
                    AddDetailBlockLabel(output, row, structured, ocrScale);
            }
            // Names and fixed labels (Master/Current/Next Level) are
            // resolved independently from the long description so one OCR wobble cannot
            // hide the entire tooltip.
            List<MatchResult> fixedMatches = translations.FindMatches(panel.Text);
            fixedMatches.RemoveAll(delegate(MatchResult match) {
                if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal)) return false;
                string key = match.Entry.Normalized;
                return key != "skill point" && key != "skill points" && key != "skill inventory";
            });
            if (fixedMatches.Count > 0)
                AddExactLabels(output, panel.Lines, panel.Text, fixedMatches, ocrScale);

            Dictionary<string, SkillPanelCandidate> candidates =
                new Dictionary<string, SkillPanelCandidate>(StringComparer.Ordinal);
            int lineCount = panel.Lines.Count;
            for (int start = 0; start < lineCount; start++)
            {
                StringBuilder combined = new StringBuilder();
                List<OcrLine> window = new List<OcrLine>();
                RectangleF firstBounds = GetOcrLineBounds(panel.Lines[start]);
                for (int span = 0; span < 16 && start + span < lineCount; span++)
                {
                    OcrLine current = panel.Lines[start + span];
                    RectangleF currentBounds = GetOcrLineBounds(current);
                    if (span > 0 && currentBounds.Top - firstBounds.Top > 620.0f) break;
                    window.Add(current);
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(current.Text);
                    string text = combined.ToString();
                    string normalized = TranslationStore.Normalize(text);
                    if (normalized.Length < 18) continue;
                    List<MatchResult> detailMatches = translations.FindSkillTextMatches(text, skillId);
                    foreach (MatchResult match in detailMatches)
                    {
                        if (!match.Entry.IsSkillText || match.Entry.Normalized.Length < 18) continue;
                        int distance = Math.Abs(normalized.Length - match.Entry.Normalized.Length);
                        int score = match.Entry.Normalized.Length * 1000 - distance;
                        string key = match.Entry.Category + "\t" + match.Entry.English;
                        SkillPanelCandidate previous;
                        if (!candidates.TryGetValue(key, out previous) || score > previous.Score)
                        {
                            candidates[key] = new SkillPanelCandidate {
                                Lines = new List<OcrLine>(window), Text = text,
                                Match = match, Score = score
                            };
                        }
                    }
                }
            }

            List<SkillPanelCandidate> ranked = new List<SkillPanelCandidate>(candidates.Values);
            ranked.Sort(delegate(SkillPanelCandidate left, SkillPanelCandidate right) {
                return right.Score.CompareTo(left.Score);
            });
            List<RectangleF> used = new List<RectangleF>();
            int added = 0;
            foreach (SkillPanelCandidate candidate in ranked)
            {
                List<OcrLine> tight = SelectBestDetailLines(candidate.Lines, candidate.Match.Entry);
                RectangleF raw = GetCombinedLineBounds(tight);
                if (raw.IsEmpty) continue;
                bool overlaps = false;
                foreach (RectangleF previous in used)
                {
                    RectangleF intersection = RectangleF.Intersect(previous, raw);
                    float smaller = Math.Min(previous.Width * previous.Height, raw.Width * raw.Height);
                    if (smaller > 0 && intersection.Width * intersection.Height / smaller > 0.38f)
                    { overlaps = true; break; }
                }
                if (overlaps) continue;
                AddDetailBlockLabel(output, tight, candidate.Match.Entry.Chinese, ocrScale);
                used.Add(raw);
                if (++added >= 3) break; // description + current level + next level
            }
            if (added == 0)
            {
                TranslationEntry overview = translations.GetSkillOverview(skillId);
                List<OcrLine> prose = FindBestPanelProseBlock(panel.Lines);
                if (overview != null && prose.Count > 0)
                    AddDetailBlockLabel(output, prose, overview.Chinese, ocrScale);
            }
        }

        private static List<OcrLine> FindBestPanelProseBlock(List<OcrLine> source)
        {
            List<OcrLine> sorted = new List<OcrLine>(source);
            sorted.Sort(delegate(OcrLine left, OcrLine right) {
                return GetOcrLineBounds(left).Top.CompareTo(GetOcrLineBounds(right).Top);
            });
            List<OcrLine> best = new List<OcrLine>(), current = new List<OcrLine>();
            int bestScore = 0, currentScore = 0;
            foreach (OcrLine line in sorted)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                bool structural = normalized.Contains("master level") ||
                    normalized.Contains("current level") || normalized.Contains("next level") ||
                    normalized.Contains("required skill") || normalized.Contains("skill point") ||
                    normalized.Contains("remaining enhancements") || normalized.Contains("req lev") ||
                    normalized.Contains("beginner warrior") || normalized.Contains("character stat") ||
                    normalized.Contains("character info");
                int words = normalized.Split(new char[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries).Length;
                bool prose = !structural && normalized.Length >= 18 && words >= 4;
                if (prose && (current.Count == 0 || CanJoinOcrLines(current[current.Count - 1], line)))
                {
                    current.Add(line);
                    int digits = 0;
                    foreach (char c in normalized) if (Char.IsDigit(c)) digits++;
                    currentScore += normalized.Length - digits * 3;
                    if (currentScore > bestScore)
                    { bestScore = currentScore; best = new List<OcrLine>(current); }
                }
                else
                {
                    current.Clear(); currentScore = 0;
                    if (prose)
                    {
                        current.Add(line); currentScore = normalized.Length;
                        if (currentScore > bestScore)
                        { bestScore = currentScore; best = new List<OcrLine>(current); }
                    }
                }
            }
            return best;
        }

        private static RectangleF GetCombinedLineBounds(List<OcrLine> lines)
        {
            RectangleF bounds = RectangleF.Empty;
            int count = 0;
            foreach (OcrLine line in lines)
            {
                RectangleF current = GetOcrLineBounds(line);
                if (current.IsEmpty) continue;
                bounds = count++ == 0 ? current : RectangleF.Union(bounds, current);
            }
            return bounds;
        }

        private static List<OcrLine> SelectBestDetailLines(List<OcrLine> source, TranslationEntry entry)
        {
            if (source == null || source.Count <= 1 || entry == null || entry.DetailWords == null)
                return source ?? new List<OcrLine>();
            int bestStart = 0, bestSpan = source.Count, bestScore = Int32.MinValue;
            for (int start = 0; start < source.Count; start++)
            {
                StringBuilder combined = new StringBuilder();
                for (int span = 1; span <= 7 && start + span <= source.Count; span++)
                {
                    if (span > 1 && !CanJoinOcrLines(source[start + span - 2], source[start + span - 1])) break;
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(source[start + span - 1].Text);
                    string normalized = TranslationStore.Normalize(combined.ToString());
                    HashSet<string> words = new HashSet<string>(normalized.Split(
                        new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                    int common = 0;
                    foreach (string word in words) if (entry.DetailWords.Contains(word)) common++;
                    // Word overlap alone can borrow a generic continuation from the previous
                    // level row (for example "chance to Freeze...") and make adjacent skill
                    // levels overlap. Sequence similarity strongly prefers the smallest actual
                    // phrase while retaining fuzzy tolerance for damaged Classic UI glyphs.
                    float similarity = TranslationStore.DetailTextSimilarity(combined.ToString(), entry.Normalized);
                    int score = (int)(similarity * 100000.0f) + common * 120 - span;
                    if (common >= 3 && score > bestScore)
                    { bestScore = score; bestStart = start; bestSpan = span; }
                }
            }
            return bestScore == Int32.MinValue ? source : source.GetRange(bestStart, bestSpan);
        }

        private void AddDetailBlockLabel(List<OverlayLabel> output, List<OcrLine> lines,
            string text, float ocrScale)
        {
            RectangleF raw = GetCombinedLineBounds(lines);
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            output.Add(new OverlayLabel {
                Bounds = new RectangleF(raw.Left / ocrScale + captureBounds.Left - Bounds.Left - 3,
                    raw.Top / ocrScale + captureBounds.Top - Bounds.Top - 2,
                    Math.Max(100, raw.Width / ocrScale + 6), Math.Max(20, raw.Height / ocrScale + 5)),
                Text = text, Wrap = true
            });
        }

        private static void MergeAdjacentLongLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i].Text.Length < 16) continue;
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!SameOverlayText(labels[j].Text, labels[i].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    float horizontalOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    float verticalGap = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
                    if (horizontalOverlap < Math.Min(a.Width, b.Width) * 0.35f || verticalGap > 12) continue;
                    labels[i].Bounds = RectangleF.Union(a, b);
                    labels[i].Wrap = true;
                    labels.RemoveAt(j);
                }
            }
        }

        private static void MergeSameTextLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i].Text.Length < 16) continue;
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!SameOverlayText(labels[j].Text, labels[i].Text)) continue;
                    labels[i].Bounds = RectangleF.Union(labels[i].Bounds, labels[j].Bounds);
                    labels[i].Wrap = true;
                    labels.RemoveAt(j);
                }
            }
        }

        private static void MergeOverlappingSameTextLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!SameOverlayText(labels[i].Text, labels[j].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    RectangleF overlap = RectangleF.Intersect(a, b);
                    float smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
                    if (smaller <= 0 || overlap.Width * overlap.Height / smaller < 0.35f) continue;
                    labels[i].Bounds = RectangleF.Union(a, b);
                    labels[i].Wrap = labels[i].Wrap || labels[j].Wrap;
                    labels.RemoveAt(j);
                }
            }
        }

        private static void RemoveContainedLabels(List<OverlayLabel> labels)
        {
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < labels.Count; j++)
                {
                    if (i == j || labels[i].Text.Length >= labels[j].Text.Length) continue;
                    if (IsCharacterStatLabel(labels[i].Text) ||
                        IsStructuredPanelFieldLabel(labels[i].Text)) continue;
                    string shorter = Regex.Replace(labels[i].Text, @"\s+", "");
                    string longer = Regex.Replace(labels[j].Text, @"\s+", "");
                    RectangleF overlap = RectangleF.Intersect(labels[i].Bounds, labels[j].Bounds);
                    float area = labels[i].Bounds.Width * labels[i].Bounds.Height;
                    if (area <= 0) continue;
                    float covered = overlap.Width * overlap.Height / area;
                    bool translatedTextContains = longer.Contains(shorter);

                    // A complete phrase and a shorter dictionary hit can come from the same
                    // source line even when their Chinese translations share no characters
                    // (for example "To all ..." and the inner token "all" -> "全部").
                    // Suppress only a narrow, nearly fully covered, same-line fragment.  The
                    // strict geometry keeps adjacent fields and multi-line tooltip content.
                    float shortCenterY = labels[i].Bounds.Top + labels[i].Bounds.Height / 2.0f;
                    float longCenterY = labels[j].Bounds.Top + labels[j].Bounds.Height / 2.0f;
                    bool sameSourceLineFragment = longer.Length >= 10 &&
                        labels[j].Bounds.Width >= labels[i].Bounds.Width * 2.5f &&
                        labels[j].Bounds.Height <= labels[i].Bounds.Height * 1.8f &&
                        Math.Abs(shortCenterY - longCenterY) <= Math.Max(4.0f, labels[i].Bounds.Height * 0.30f) &&
                        covered >= 0.80f;
                    if ((!translatedTextContains || covered < 0.62f) && !sameSourceLineFragment) continue;
                    labels.RemoveAt(i); break;
                }
            }
        }

        private static bool IsStructuredPanelFieldLabel(string text)
        {
            if (String.IsNullOrEmpty(text)) return false;
            string[] prefixes = new string[] {
                "最高等级", "当前等级", "下一级", "需要等级", "需要力量", "需要敏捷",
                "需要智力", "需要运气", "需要人气", "可用职业", "类型", "攻击速度",
                "物理攻击力", "魔法攻击力", "物理防御力", "魔法防御力", "命中率",
                "回避率", "移动速度", "跳跃力", "剩余强化次数", "可升级次数"
            };
            foreach (string prefix in prefixes)
                if (text.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private Rectangle GetChatExclusionBounds()
        {
            Rectangle resolved = ChatRegionSettings.ResolveForGame(gameBounds);
            // If the game moved or changed resolution, an F8 capture is the reliable moment
            // when its current client bounds are known. Refresh the same shared absolute box
            // so the AI reader and the F8 exclusion continue to point at one region.
            if (ChatRegionSettings.HasSelection() && ChatRegionSettings.LoadAbsolute() != resolved)
            {
                ChatRegionSettings.ResaveForGame(resolved, gameBounds);
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.UpdateChatRegion(resolved, ChatRegionSettings.GetOrigin());
            }
            return resolved;
        }

        private bool IsChatLine(OcrLine line, float ocrScale, Rectangle exclusion)
        {
            if (exclusion.Width <= 0 || exclusion.Height <= 0 || line.Words.Count == 0) return false;
            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, (float)word.BoundingRect.X / ocrScale + captureBounds.Left);
                top = Math.Min(top, (float)word.BoundingRect.Y / ocrScale + captureBounds.Top);
                right = Math.Max(right, (float)(word.BoundingRect.X + word.BoundingRect.Width) / ocrScale + captureBounds.Left);
                bottom = Math.Max(bottom, (float)(word.BoundingRect.Y + word.BoundingRect.Height) / ocrScale + captureBounds.Top);
            }
            if (left == Single.MaxValue) return false;
            RectangleF lineBounds = RectangleF.FromLTRB(left, top, right, bottom);
            return IsInsideChatBounds(lineBounds, exclusion);
        }

        private static bool IsInsideChatBounds(RectangleF lineBounds, Rectangle exclusion)
        {
            float centerX = lineBounds.Left + lineBounds.Width / 2.0f;
            float centerY = lineBounds.Top + lineBounds.Height / 2.0f;
            if (centerX >= exclusion.Left && centerX <= exclusion.Right &&
                centerY >= exclusion.Top && centerY <= exclusion.Bottom) return true;
            RectangleF overlap = RectangleF.Intersect(lineBounds, exclusion);
            float area = lineBounds.Width * lineBounds.Height;
            return area > 0 && overlap.Width * overlap.Height / area >= 0.35f;
        }

        private void AddExactLabels(List<OverlayLabel> output, List<OcrLine> lines,
            string combinedText, List<MatchResult> matches, float ocrScale)
        {
            string normalizedLine = TranslationStore.Normalize(combinedText);
            foreach (MatchResult match in matches)
            {
                // OCR exposes word boxes, not character boxes. Approximate the phrase span by character ratio.
                float x0 = Single.MaxValue, y0 = Single.MaxValue, x1 = Single.MinValue, y1 = Single.MinValue;
                int cursor = 0;
                foreach (OcrLine line in lines)
                {
                    foreach (OcrWord word in line.Words)
                    {
                        string nw = TranslationStore.Normalize(word.Text);
                        if (nw.Length == 0) continue;
                        int safeCursor = Math.Min(cursor, normalizedLine.Length);
                        int found = safeCursor < normalizedLine.Length
                            ? normalizedLine.IndexOf(nw, safeCursor, StringComparison.Ordinal) : -1;
                        if (found < 0) found = safeCursor;
                        int end = found + nw.Length;
                        if (end > match.Start && found < match.Start + match.Length)
                        {
                            x0 = Math.Min(x0, (float)word.BoundingRect.X / ocrScale + captureBounds.Left - Bounds.Left);
                            y0 = Math.Min(y0, (float)word.BoundingRect.Y / ocrScale + captureBounds.Top - Bounds.Top);
                            x1 = Math.Max(x1, (float)(word.BoundingRect.X + word.BoundingRect.Width) / ocrScale + captureBounds.Left - Bounds.Left);
                            y1 = Math.Max(y1, (float)(word.BoundingRect.Y + word.BoundingRect.Height) / ocrScale + captureBounds.Top - Bounds.Top);
                        }
                        cursor = Math.Min(normalizedLine.Length, end + 1);
                    }
                }
                if (x0 == Single.MaxValue) continue;
                output.Add(new OverlayLabel {
                    Bounds = new RectangleF(x0 - 3, y0 - 2, Math.Max(28, x1 - x0 + 6), Math.Max(18, y1 - y0 + 4)),
                    Text = match.Entry.Chinese,
                    Wrap = lines.Count > 1 || match.Entry.IsTaskText || match.Entry.IsSkillText ||
                        match.Entry.IsItemText || match.Entry.IsInterfaceText
                });
            }
        }

        private static bool CanJoinOcrLines(OcrLine first, OcrLine second)
        {
            return AreOcrBlocksContinuous(GetOcrLineBounds(first), GetOcrLineBounds(second));
        }

        private static bool LooksLikeEquipmentStatText(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("attack speed") || normalized.Contains("weapon attack") ||
                normalized.Contains("enhancements") || normalized.Contains("required level") ||
                normalized.Contains("upgrades available") ||
                normalized.Contains("req lev") || normalized.Contains("req str") ||
                normalized.Contains("req dex") || normalized.Contains("req int") ||
                normalized.Contains("req luk") || normalized.Contains("req fame") ||
                normalized.Contains("weapon def") || normalized.Contains("magic def") ||
                normalized.Contains("avoidability") || normalized.StartsWith("str ", StringComparison.Ordinal) ||
                normalized.StartsWith("dex ", StringComparison.Ordinal) ||
                normalized.StartsWith("int ", StringComparison.Ordinal) ||
                normalized.StartsWith("luk ", StringComparison.Ordinal) ||
                normalized.StartsWith("type:", StringComparison.Ordinal) ||
                normalized.Contains(" type:");
        }

        private static bool LooksLikeItemPanelText(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("req lv") || normalized.Contains("req lev") ||
                normalized.Contains("required level") || normalized.Contains("remaining enhancements") ||
                normalized.Contains("upgrades available") ||
                normalized.Contains("beginner warrior") || normalized.Contains("weapon def") ||
                normalized.Contains("item list") || normalized.Contains("item inventory");
        }

        private static Rectangle FindEquipmentTooltipCrop(OcrResult result, float ocrScale,
            System.Drawing.Size sourceSize)
        {
            RectangleF anchors = RectangleF.Empty;
            int count = 0;
            foreach (OcrLine line in result.Lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                bool anchor = normalized.Contains("remaining enhancements") ||
                    normalized.Contains("upgrades available") ||
                    normalized.Contains("weapon def") || normalized.Contains("magic def") ||
                    normalized.Contains("beginner warrior") || normalized == "shoes" ||
                    normalized == "gloves" || normalized.Contains("attack speed");
                if (!anchor) continue;
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                RectangleF source = new RectangleF(raw.Left / ocrScale, raw.Top / ocrScale,
                    raw.Width / ocrScale, raw.Height / ocrScale);
                anchors = count == 0 ? source : RectangleF.Union(anchors, source);
                count++;
            }
            if (count < 2) return new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
            int left = Math.Max(0, (int)Math.Floor(anchors.Left - 220));
            int top = Math.Max(0, (int)Math.Floor(anchors.Top - 300));
            int right = Math.Min(sourceSize.Width, (int)Math.Ceiling(anchors.Right + 170));
            int bottom = Math.Min(sourceSize.Height, (int)Math.Ceiling(anchors.Bottom + 65));
            Rectangle crop = Rectangle.FromLTRB(left, top, right, bottom);
            return crop.Width >= 280 && crop.Height >= 300
                ? crop : new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
        }

        private static RectangleF GetOcrLineBounds(OcrLine line)
        {
            if (line == null || line.Words.Count == 0) return RectangleF.Empty;
            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, (float)word.BoundingRect.X);
                top = Math.Min(top, (float)word.BoundingRect.Y);
                right = Math.Max(right, (float)(word.BoundingRect.X + word.BoundingRect.Width));
                bottom = Math.Max(bottom, (float)(word.BoundingRect.Y + word.BoundingRect.Height));
            }
            return left == Single.MaxValue ? RectangleF.Empty : RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static List<RectangleF> FindPlayerChatLineBounds(OcrResult result,
            float ocrScale, Rectangle screen)
        {
            List<RectangleF> output = new List<RectangleF>();
            if (result == null || ocrScale <= 0) return output;
            foreach (OcrLine line in result.Lines)
            {
                if (!SceneClassifier.LooksLikePlayerChat(line.Text)) continue;
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                output.Add(new RectangleF(screen.Left + raw.Left / ocrScale,
                    screen.Top + raw.Top / ocrScale, raw.Width / ocrScale,
                    raw.Height / ocrScale));
            }
            return output;
        }

        private static bool AreOcrBlocksContinuous(RectangleF first, RectangleF second)
        {
            if (first.IsEmpty || second.IsEmpty) return false;
            float lineHeight = Math.Max(8.0f, Math.Max(first.Height, second.Height));
            if (second.Top < first.Top - lineHeight * 0.4f) return false;
            float verticalGap = second.Top - first.Bottom;
            if (verticalGap > Math.Max(28.0f, lineHeight * 2.2f)) return false;
            float horizontalOverlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
            float leftDrift = Math.Abs(first.Left - second.Left);
            float allowedDrift = Math.Max(90.0f, Math.Max(first.Width, second.Width) * 0.35f);
            return horizontalOverlap > 0 || leftDrift <= allowedDrift;
        }

        private OverlayLabel BuildIconAssistedLabel(OcrLine line, float ocrScale, Bitmap prepared)
        {
            string normalized = TranslationStore.Normalize(line.Text);
            if (normalized.Length < 3 || normalized.Length > 64 || line.Words.Count == 0) return null;
            if (!translations.HasPlausibleIconText(line.Text)) return null;

            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, (float)word.BoundingRect.X);
                top = Math.Min(top, (float)word.BoundingRect.Y);
                right = Math.Max(right, (float)(word.BoundingRect.X + word.BoundingRect.Width));
                bottom = Math.Max(bottom, (float)(word.BoundingRect.Y + word.BoundingRect.Height));
            }
            if (left == Single.MaxValue) return null;

            float baseSize = Math.Max(22.0f * ocrScale, 32.0f * ocrScale);
            float[] sizeFactors = new float[] { 0.78f, 1.0f, 1.25f, 1.48f };
            float[] gaps = new float[] { 2.0f * ocrScale, 10.0f * ocrScale,
                22.0f * ocrScale, 36.0f * ocrScale };
            // Tooltips often place a 32px icon above the text baseline; skill lists centre it.
            float[] verticalFactors = new float[] { -0.85f, -0.45f, -0.10f, 0.25f };
            IconMatchResult best = null;

            foreach (float sizeFactor in sizeFactors)
            {
                float size = baseSize * sizeFactor;
                foreach (float gap in gaps)
                {
                    foreach (float verticalFactor in verticalFactors)
                    {
                        RectangleF iconArea = new RectangleF(left - gap - size,
                            (top + bottom - size) / 2.0f + verticalFactor * size, size, size);
                        ulong hash;
                        if (!TryComputeDHash(prepared, iconArea, out hash)) continue;
                        if (Program.Benchmark) Program.BenchmarkCurrentArea = iconArea.ToString();
                        IconMatchResult candidate = translations.FindIconAssistedMatch(hash, line.Text);
                        if (candidate != null && (best == null || candidate.Score > best.Score)) best = candidate;
                    }
                }
            }
            if (best == null) return null;

            return new OverlayLabel {
                Bounds = new RectangleF(left / ocrScale + captureBounds.Left - Bounds.Left - 3,
                    top / ocrScale + captureBounds.Top - Bounds.Top - 2,
                    Math.Max(28, (right - left) / ocrScale + 6),
                    Math.Max(18, (bottom - top) / ocrScale + 4)),
                Text = best.Entry.Chinese
            };
        }

        private static bool TryComputeDHash(Bitmap image, RectangleF area, out ulong hash)
        {
            hash = 0;
            if (area.Width < 8 || area.Height < 8 || area.Left < 0 || area.Top < 0 ||
                area.Right >= image.Width || area.Bottom >= image.Height) return false;
            int[,] luminance = new int[8, 9];
            using (Bitmap reduced = new Bitmap(9, 8, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(reduced))
            {
                graphics.Clear(Color.FromArgb(18, 18, 20));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, 9, 8), area.X, area.Y,
                    area.Width, area.Height, GraphicsUnit.Pixel);
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 9; x++)
                    {
                        Color color = reduced.GetPixel(x, y);
                        luminance[y, x] = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    }
                }
            }
            int bit = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++, bit++)
                    if (luminance[y, x] > luminance[y, x + 1]) hash |= 1UL << bit;
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (shuttingDown || !visibleTranslation) return;
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (SolidBrush background = new SolidBrush(Color.FromArgb(238, 18, 18, 20)))
                using (Pen border = new Pen(Color.FromArgb(220, 255, 190, 45), 1.0f))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    OverlayLabel[] snapshot = labels.ToArray();
                    foreach (OverlayLabel label in snapshot)
                    {
                        string labelText;
                        RectangleF r;
                        if (!OverlayPaintSafety.TryPrepare(label, out labelText, out r))
                            continue;
                        try
                        {
                            PaintOverlayLabel(e.Graphics, label, labelText, r,
                                background, border, textBrush);
                        }
                        catch (ArgumentException)
                        {
                            // A malformed OCR rectangle or a transient GDI+ font state must not
                            // replace the entire transparent overlay with WinForms' red error cross.
                        }
                        catch (System.Runtime.InteropServices.ExternalException)
                        {
                            // Isolate a single GDI+ drawing failure and keep the remaining labels.
                        }
                    }
                }
            }
            catch (ArgumentException)
            {
                // Keep paint callbacks exception-safe even while the overlay is closing.
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // GDI+ can invalidate a paint surface during display/DPI changes.
            }
        }

        private void PaintOverlayLabel(Graphics graphics, OverlayLabel label, string labelText,
            RectangleF r, Brush background, Pen border, Brush textBrush)
        {
            SizeF size;
            if (label.Wrap)
            {
                r.Width = Math.Max(90, r.Width);
                size = graphics.MeasureString(labelText, overlayFont,
                    new SizeF(Math.Max(20, r.Width - 8), 1000),
                    StringFormat.GenericTypographic);
                r.Height = Math.Max(r.Height, size.Height + 7);
            }
            else
            {
                size = graphics.MeasureString(labelText, overlayFont);
                r.Width = Math.Max(r.Width, size.Width + 8);
                r.Height = Math.Max(r.Height, size.Height + 5);
            }
            using (GraphicsPath path = RoundedRect(r, 4.0f))
            {
                graphics.FillPath(background, path);
                graphics.DrawPath(border, path);
            }
            if (label.Wrap)
                graphics.DrawString(labelText, overlayFont, textBrush,
                    new RectangleF(r.X + 4, r.Y + 3, r.Width - 8, r.Height - 5),
                    StringFormat.GenericTypographic);
            else
                graphics.DrawString(labelText, overlayFont, textBrush, r.X + 4,
                    r.Y + (r.Height - size.Height) / 2 - 1);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            shuttingDown = true;
            visibleTranslation = false;
            labels.Clear();
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            gamepadTimer.Stop();
            gamepadTimer.Dispose();
            continuousTranslationTimer.Stop();
            continuousTranslationTimer.Dispose();
            if (activationWait != null)
            {
                activationWait.Unregister(null);
                activationWait = null;
            }
            tray.Visible = false;
            tray.Dispose();
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.Dispose();
            if (chatTranslator != null && !chatTranslator.IsDisposed) chatTranslator.StopService();
            base.OnFormClosed(e);
            overlayFont.Dispose();
        }
    }
}
