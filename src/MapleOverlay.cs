using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MapleOverlay
{
    internal static class Program
    {
        internal static bool Benchmark;
        internal static string BenchmarkIconPath;
        internal static string BenchmarkText;
        internal static int BenchmarkBestIconDistance = 65;
        internal static string BenchmarkBestIcon = "";
        internal static string BenchmarkCurrentArea = "";
        [STAThread]
        private static void Main(string[] args)
        {
            Benchmark = args != null && Array.IndexOf(args, "--benchmark") >= 0;
            if (args != null) foreach (string arg in args)
                if (arg.StartsWith("--benchmark-icon=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkIconPath = arg.Substring("--benchmark-icon=".Length);
                else if (arg.StartsWith("--benchmark-text=", StringComparison.OrdinalIgnoreCase))
                    BenchmarkText = arg.Substring("--benchmark-text=".Length);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new OverlayForm()); }
            catch (Exception ex)
            {
                MessageBox.Show("程序发生错误：\n\n" + ex.Message,
                    "枫语幕", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
    }

    internal sealed class TranslationStore
    {
        private readonly string sourcePath;
        private readonly Dictionary<char, List<TranslationEntry>> buckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> taskBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly List<TranslationEntry> taskNames = new List<TranslationEntry>();
        private readonly List<TranslationEntry> iconEntries = new List<TranslationEntry>();
        public int Count { get; private set; }
        public int IconCount { get { return iconEntries.Count; } }
        public int TaskTextCount { get; private set; }
        public int TaskCount { get { return taskNames.Count; } }

        public TranslationStore(string source)
        {
            sourcePath = source;
        }

        public void Load()
        {
            List<TranslationEntry> entries = ReadTsv(sourcePath);

            buckets.Clear();
            taskBuckets.Clear();
            taskNames.Clear();
            iconEntries.Clear();
            TaskTextCount = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (entry.Normalized.Length == 0) continue;
                if (entry.IsTaskName || entry.IsTaskText)
                {
                    AddToBucket(taskBuckets, entry);
                    if (entry.IsTaskName) taskNames.Add(entry);
                    if (entry.IsTaskText) TaskTextCount++;
                }
                else
                {
                    AddToBucket(buckets, entry);
                    if (entry.HasIcon) iconEntries.Add(entry);
                }
            }
            SortBuckets(buckets);
            SortBuckets(taskBuckets);
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
            return FindInBuckets(text, buckets, null);
        }

        public List<MatchResult> FindTaskMatches(string text, string taskId)
        {
            return FindInBuckets(text, taskBuckets, taskId);
        }

        public string DetectTaskId(string text)
        {
            string normalized = Normalize(text);
            TranslationEntry best = null;
            foreach (TranslationEntry entry in taskNames)
            {
                int position = normalized.IndexOf(entry.Normalized, StringComparison.Ordinal);
                if (position < 0) continue;
                if (!IsBoundary(normalized, position - 1) || !IsBoundary(normalized, position + entry.Normalized.Length)) continue;
                if (best == null || entry.Normalized.Length > best.Normalized.Length) best = entry;
            }
            return best == null ? "" : best.TaskId;
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
            if (normalizedText.Length < 3) return null;
            IconMatchResult best = null;
            float secondScore = Single.MinValue;
            foreach (TranslationEntry entry in iconEntries)
            {
                int distance = HammingDistance(hash, entry.IconHash);
                float similarity = TextSimilarity(normalizedText, entry.Normalized);
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

        public static string Normalize(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder b = new StringBuilder(value.Length);
            bool space = false;
            foreach (char raw in value.Trim().ToLowerInvariant())
            {
                char c = raw == '\u2019' || raw == '\u2018' ? '\'' : raw;
                if (Char.IsWhiteSpace(c))
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
            return b.ToString().Trim();
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
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 2) continue;
                string english = parts[0].Trim();
                string chinese = parts[1].Trim();
                string normalized = Normalize(english);
                string category = parts.Length > 2 ? parts[2].Trim() : "";
                bool isTaskName = category.StartsWith("怀旧服-任务#", StringComparison.Ordinal);
                bool isTaskText = category.StartsWith("怀旧服-任务说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-任务对白#", StringComparison.Ordinal);
                string taskId = (isTaskName || isTaskText) && category.LastIndexOf('#') >= 0
                    ? category.Substring(category.LastIndexOf('#') + 1) : "";
                string dedupeKey = normalized + "\t" + ((isTaskName || isTaskText) ? taskId : "general");
                if (normalized.Length == 0 || chinese.Length == 0 || !seen.Add(dedupeKey)) continue;
                ulong iconHash = 0;
                bool hasIcon = parts.Length > 3 && UInt64.TryParse(parts[3].Trim(),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iconHash);
                result.Add(new TranslationEntry {
                    English = english, Chinese = chinese,
                    Category = category, Normalized = normalized,
                    IconHash = hasIcon ? iconHash : 0, HasIcon = hasIcon,
                    TaskId = taskId, IsTaskName = isTaskName, IsTaskText = isTaskText
                });
            }
            return result;
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
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        private readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly TranslationStore translations;
        private readonly List<OverlayLabel> labels = new List<OverlayLabel>();
        private readonly NotifyIcon tray = new NotifyIcon();
        private bool processing;
        private bool visibleTranslation;
        private OcrEngine ocr;
        private Keys showKey = Keys.F8;
        private Keys hideKey = Keys.F9;
        private uint showModifiers;
        private uint hideModifiers;
        private Rectangle captureBounds;
        private DictionaryOnlyForm dictionaryEditor;
        private HotkeyForm hotkeyEditor;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

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

            BuildTray();
            Shown += async delegate {
                SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) |
                    WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
                bool h1 = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                bool h2 = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                tray.ShowBalloonTip(2500, "枫语幕已启动",
                    "常规/任务共 " + translations.Count + " 条，任务 " + translations.TaskCount + " 个/文本 " +
                    translations.TaskTextCount + " 条，图标指纹 " + translations.IconCount + " 条，已载入内存。" +
                    HotkeyText(showKey, showModifiers) + " 呼出，" +
                    HotkeyText(hideKey, hideModifiers) + " 缩回后台。" + ((!h1 || !h2) ? "（有快捷键注册失败）" : ""), ToolTipIcon.Info);
                if (Program.Benchmark)
                {
                    await Task.Delay(350);
                    await ShowTranslationAsync();
                    Close();
                }
            };
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

        internal void ApplyHotkeys(Keys newShowKey, uint newShowModifiers, Keys newHideKey, uint newHideModifiers)
        {
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            showKey = newShowKey; showModifiers = newShowModifiers;
            hideKey = newHideKey; hideModifiers = newHideModifiers;
            bool ok1 = RegisterHotKey(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
            bool ok2 = RegisterHotKey(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
            if (!ok1 || !ok2) MessageBox.Show("快捷键被其他程序占用，请换一个组合。", "快捷键设置");
        }

        internal void ReloadDictionary()
        {
            translations.Load();
            tray.ShowBalloonTip(1200, "词库已载入内存", translations.Count + " 条", ToolTipIcon.Info);
        }

        private void ShowDictionaryEditor()
        {
            HideTranslation();
            if (dictionaryEditor == null || dictionaryEditor.IsDisposed) dictionaryEditor = new DictionaryOnlyForm(this, baseDir);
            dictionaryEditor.Show();
            dictionaryEditor.Activate();
        }

        private void ShowHotkeyEditor()
        {
            HideTranslation();
            if (hotkeyEditor == null || hotkeyEditor.IsDisposed) hotkeyEditor = new HotkeyForm(this);
            hotkeyEditor.Show();
            hotkeyEditor.Activate();
        }

        private void BuildTray()
        {
            tray.Icon = SystemIcons.Information;
            tray.Text = "枫语幕";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem dictionary = new ToolStripMenuItem("打开并更改词库");
            dictionary.Click += delegate { ShowDictionaryEditor(); };
            ToolStripMenuItem hotkeys = new ToolStripMenuItem("更改快捷键");
            hotkeys.Click += delegate { ShowHotkeyEditor(); };
            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Close(); };
            menu.Items.Add(dictionary);
            menu.Items.Add(hotkeys);
            menu.Items.Add(exit);
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowDictionaryEditor(); };
        }

        private async Task ToggleAsync()
        {
            if (visibleTranslation)
            {
                visibleTranslation = false;
                labels.Clear();
                Invalidate();
                tray.Text = "枫语幕（内存待机）";
            }
            else await ShowTranslationAsync();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_SHOW) { Task ignored = ShowTranslationAsync(); }
                else if (id == HOTKEY_HIDE) HideTranslation();
            }
            base.WndProc(ref m);
        }

        private void HideTranslation()
        {
            visibleTranslation = false;
            labels.Clear();
            Invalidate();
            tray.Text = "枫语幕（内存待机）";
        }

        private async Task ShowTranslationAsync()
        {
            if (processing) return;
            processing = true;
            // Screen results are never reused. Every F8 starts from an empty overlay and a fresh capture.
            visibleTranslation = false;
            labels.Clear();
            Invalidate();
            Stopwatch stopwatch = Stopwatch.StartNew();
            string benchmarkOcrText = "";
            try
            {
                Rectangle screen = Program.Benchmark
                    ? new Rectangle(0, 0, 1280, 720)
                    : GetForegroundCaptureBounds();
                captureBounds = screen;
                using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        if (Program.Benchmark)
                        {
                            if (!String.IsNullOrEmpty(Program.BenchmarkText))
                            {
                                g.Clear(Color.FromArgb(28, 28, 32));
                                using (Font titleFont = new Font("Arial", 15, FontStyle.Bold))
                                using (Font textFont = new Font("Arial", 14, FontStyle.Regular))
                                {
                                    g.DrawString("Quest   Accept", titleFont, Brushes.White, 40, 35);
                                    g.DrawString(Program.BenchmarkText, textFont, Brushes.White,
                                        new RectangleF(40, 78, 430, 220));
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
                    float ocrScale;
                    using (Bitmap prepared = PrepareForOcr(bitmap, out ocrScale, false))
                    {
                        OcrResult result = await RecognizeAsync(prepared);
                        List<OverlayLabel> next = BuildLabels(result, ocrScale, prepared);
                        benchmarkOcrText = result.Text;

                        // Use the spare time budget for a second, grayscale/high-contrast OCR pass.
                        // Exact dictionary hits unique to either pass are merged; overlapping results
                        // keep the colour pass. Slow machines skip the second pass before one second.
                        if (stopwatch.ElapsedMilliseconds < 450)
                        {
                            float secondScale;
                            using (Bitmap contrastPrepared = PrepareForOcr(bitmap, out secondScale, true))
                            {
                                OcrResult secondResult = await RecognizeAsync(contrastPrepared);
                                List<OverlayLabel> second = BuildLabels(secondResult, secondScale, prepared);
                                MergeLabels(next, second);
                                if (Program.Benchmark)
                                    benchmarkOcrText += " || 二次=" + secondResult.Text;
                            }
                        }
                        // A higher-resolution colour pass improves very small item and quest text.
                        if (stopwatch.ElapsedMilliseconds < 650)
                        {
                            float thirdScale;
                            using (Bitmap largePrepared = PrepareForOcr(bitmap, out thirdScale, false, 3000.0f))
                            {
                                OcrResult thirdResult = await RecognizeAsync(largePrepared);
                                MergeLabels(next, BuildLabels(thirdResult, thirdScale, largePrepared));
                                if (Program.Benchmark)
                                    benchmarkOcrText += " || 三次=" + thirdResult.Text;
                            }
                        }
                        labels.Clear(); labels.AddRange(next);
                    }
                }
                visibleTranslation = true;
                Invalidate();
                stopwatch.Stop();
                tray.Text = "枫语幕（已显示，" + stopwatch.ElapsedMilliseconds + "ms）";
                if (Program.Benchmark)
                    File.WriteAllText(Path.Combine(baseDir, "last_run.txt"),
                        "耗时毫秒=" + stopwatch.ElapsedMilliseconds + Environment.NewLine +
                        "命中数量=" + labels.Count + Environment.NewLine +
                        "图标指纹数量=" + translations.IconCount + Environment.NewLine +
                        "任务数量=" + translations.TaskCount + Environment.NewLine +
                        "任务文本数量=" + translations.TaskTextCount + Environment.NewLine +
                        "OCR文本=" + benchmarkOcrText.Replace("\r", " ").Replace("\n", " | ") + Environment.NewLine +
                        "最佳图标距离=" + Program.BenchmarkBestIconDistance + Environment.NewLine +
                        "最佳图标候选=" + Program.BenchmarkBestIcon + Environment.NewLine +
                        "时间=" + DateTime.Now.ToString("s") + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                visibleTranslation = false;
                labels.Clear();
                Invalidate();
                tray.ShowBalloonTip(3000, "识别失败", ex.Message, ToolTipIcon.Error);
            }
            finally { processing = false; }
        }

        private static Rectangle GetForegroundCaptureBounds()
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

        private static Bitmap PrepareForOcr(Bitmap source, out float scale, bool grayscale,
            float targetLongEdge = 2400.0f)
        {
            int longEdge = Math.Max(source.Width, source.Height);
            scale = Math.Min(2.5f, Math.Max(1.0f, targetLongEdge / Math.Max(1, longEdge)));
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

        private static void MergeLabels(List<OverlayLabel> primary, List<OverlayLabel> secondary)
        {
            foreach (OverlayLabel candidate in secondary)
            {
                bool duplicate = false;
                foreach (OverlayLabel existing in primary)
                {
                    RectangleF intersection = RectangleF.Intersect(existing.Bounds, candidate.Bounds);
                    float smallerArea = Math.Min(existing.Bounds.Width * existing.Bounds.Height,
                        candidate.Bounds.Width * candidate.Bounds.Height);
                    if ((smallerArea > 0 && intersection.Width * intersection.Height / smallerArea >= 0.45f) ||
                        (existing.Text == candidate.Text && Math.Abs(existing.Bounds.Y - candidate.Bounds.Y) < 20))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) primary.Add(candidate);
            }
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

        private List<OverlayLabel> BuildLabels(OcrResult result, float ocrScale, Bitmap prepared)
        {
            List<OverlayLabel> output = new List<OverlayLabel>();
            List<OcrLine> allLines = new List<OcrLine>(result.Lines);
            bool questInterface = translations.LooksLikeQuestInterface(result.Text);
            string activeTaskId = questInterface ? translations.DetectTaskId(result.Text) : "";
            for (int lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
            {
                OcrLine line = allLines[lineIndex];
                List<MatchResult> matches = questInterface
                    ? translations.FindTaskMatches(line.Text, activeTaskId)
                    : translations.FindMatches(line.Text);
                if (questInterface && matches.Count == 0 && TranslationStore.Normalize(line.Text).Length <= 18)
                    matches = translations.FindMatches(line.Text); // buttons, rewards and short item names only
                if (matches.Count == 0)
                {
                    // Quest dialogue is commonly wrapped into several visual OCR lines.
                    // Combine only nearby consecutive lines and accept long quest-text entries.
                    List<OcrLine> combinedLines = new List<OcrLine>();
                    StringBuilder combinedText = new StringBuilder();
                    for (int span = 0; span < 4 && lineIndex + span < allLines.Count; span++)
                    {
                        OcrLine candidateLine = allLines[lineIndex + span];
                        combinedLines.Add(candidateLine);
                        if (combinedText.Length > 0) combinedText.Append(' ');
                        combinedText.Append(candidateLine.Text);
                        if (span == 0) continue;
                        List<MatchResult> combinedMatches = questInterface
                            ? translations.FindTaskMatches(combinedText.ToString(), activeTaskId)
                            : new List<MatchResult>();
                        combinedMatches.RemoveAll(delegate(MatchResult match) {
                            return match.Length < 20 || !match.Entry.Category.StartsWith("怀旧服-任务", StringComparison.Ordinal);
                        });
                        if (combinedMatches.Count > 0)
                        {
                            AddExactLabels(output, combinedLines, combinedText.ToString(), combinedMatches, ocrScale);
                            matches = combinedMatches;
                            break;
                        }
                    }
                    if (matches.Count > 0) continue;
                    OverlayLabel assisted = BuildIconAssistedLabel(line, ocrScale, prepared);
                    if (assisted != null) output.Add(assisted);
                    continue;
                }
                AddExactLabels(output, new List<OcrLine> { line }, line.Text, matches, ocrScale);
            }
            return output;
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
                        int found = normalizedLine.IndexOf(nw, cursor, StringComparison.Ordinal);
                        if (found < 0) found = cursor;
                        int end = found + nw.Length;
                        if (end > match.Start && found < match.Start + match.Length)
                        {
                            x0 = Math.Min(x0, (float)word.BoundingRect.X / ocrScale + captureBounds.Left - Bounds.Left);
                            y0 = Math.Min(y0, (float)word.BoundingRect.Y / ocrScale + captureBounds.Top - Bounds.Top);
                            x1 = Math.Max(x1, (float)(word.BoundingRect.X + word.BoundingRect.Width) / ocrScale + captureBounds.Left - Bounds.Left);
                            y1 = Math.Max(y1, (float)(word.BoundingRect.Y + word.BoundingRect.Height) / ocrScale + captureBounds.Top - Bounds.Top);
                        }
                        cursor = end + 1;
                    }
                }
                if (x0 == Single.MaxValue) continue;
                output.Add(new OverlayLabel {
                    Bounds = new RectangleF(x0 - 3, y0 - 2, Math.Max(28, x1 - x0 + 6), Math.Max(18, y1 - y0 + 4)),
                    Text = match.Entry.Chinese
                });
            }
        }

        private OverlayLabel BuildIconAssistedLabel(OcrLine line, float ocrScale, Bitmap prepared)
        {
            string normalized = TranslationStore.Normalize(line.Text);
            if (normalized.Length < 3 || normalized.Length > 64 || line.Words.Count == 0) return null;

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
            float[] sizeFactors = new float[] { 0.82f, 1.0f, 1.2f };
            float[] gaps = new float[] { 2.0f * ocrScale, 9.0f * ocrScale, 18.0f * ocrScale };
            // Tooltips often place a 32px icon above the text baseline; skill lists centre it.
            float[] verticalFactors = new float[] { -0.85f, -0.55f, -0.25f, 0.0f, 0.25f };
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush background = new SolidBrush(Color.FromArgb(238, 18, 18, 20)))
            using (Pen border = new Pen(Color.FromArgb(220, 255, 190, 45), 1.0f))
            using (SolidBrush text = new SolidBrush(Color.White))
            using (Font font = new Font("Microsoft YaHei UI", 12.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                foreach (OverlayLabel label in labels)
                {
                    RectangleF r = label.Bounds;
                    SizeF size = e.Graphics.MeasureString(label.Text, font);
                    r.Width = Math.Max(r.Width, size.Width + 8);
                    r.Height = Math.Max(r.Height, size.Height + 5);
                    using (GraphicsPath path = RoundedRect(r, 4.0f))
                    {
                        e.Graphics.FillPath(background, path);
                        e.Graphics.DrawPath(border, path);
                    }
                    e.Graphics.DrawString(label.Text, font, text, r.X + 4, r.Y + (r.Height - size.Height) / 2 - 1);
                }
            }
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
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }
    }
}
