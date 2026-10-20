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
[assembly: AssemblyVersion("4.0.0.0")]
[assembly: AssemblyFileVersion("4.0.0.0")]

namespace MapleOverlay
{
    internal static class Program
    {
        private const string InstanceMutexName = @"Local\FengYuMu.SingleInstance.questR13";
        private const string ActivationEventName = @"Local\FengYuMu.Activate.questR13";
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
        internal static bool ForceLegacyOcrBridge;
        // Diagnostic switch used by the real-image acceptance tests to compare the fast
        // Windows OCR result with the optional accuracy pass. It never changes normal use.
        internal static bool DisableStrongChatOcr;
        internal static bool ForceStrongChatOcr;
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
            int captureParent;
            if (args != null && args.Length == 1 && args[0].StartsWith("--window-capture-worker=", StringComparison.Ordinal) &&
                Int32.TryParse(args[0].Substring("--window-capture-worker=".Length), out captureParent) && captureParent > 0)
            { WindowCaptureClient.RunWorker(captureParent); return; }
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
            ForceLegacyOcrBridge = args != null && Array.IndexOf(args, "--ocr-png-bridge") >= 0;
            DisableStrongChatOcr = args != null && Array.IndexOf(args, "--chat-fast-only") >= 0;
            ForceStrongChatOcr = args != null && Array.IndexOf(args, "--chat-strong-only") >= 0;
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
            // The "Windows Default Beep" is what WinForms plays from its default
            // ThreadExceptionDialog. Every timer tick and button handler here is an
            // async delegate, i.e. an async void, so any exception that escapes one of
            // them would surface as that beep in the middle of a game. Route UI-thread
            // failures to a log file instead: the player never hears them, and the
            // trace is still available for diagnosis.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                LogSilentFailure(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) {
                LogSilentFailure(e.ExceptionObject as Exception);
            };
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
                        if (Array.IndexOf(args, "--main-ui-test-dashboard") >= 0)
                            panel.ShowDashboardForTest();
                        else if (Array.IndexOf(args, "--main-ui-test-ai") >= 0)
                            panel.ShowAiPageForTest();
                        panel.Show();
                        bool maximizedTest = Array.IndexOf(args, "--main-ui-test-maximized") >= 0;
                        bool snapBeforeMaximize = panel.SnapEnabledForTest;
                        Application.DoEvents();
                        int requestedScale = 100;
                        List<int> requestedScales = new List<int>();
                        bool transitionTest = false;
                        foreach (string arg in args)
                        {
                            if (arg.StartsWith("--main-ui-test-transition=", StringComparison.OrdinalIgnoreCase))
                            {
                                transitionTest = true;
                                string[] values = arg.Substring("--main-ui-test-transition=".Length).Split(',');
                                foreach (string value in values)
                                {
                                    int parsed;
                                    if (Int32.TryParse(value, out parsed))
                                        requestedScales.Add(Math.Max(100, Math.Min(200, parsed)));
                                }
                                continue;
                            }
                            if (!arg.StartsWith("--main-ui-test-scale=", StringComparison.OrdinalIgnoreCase)) continue;
                            Int32.TryParse(arg.Substring("--main-ui-test-scale=".Length), out requestedScale);
                            requestedScale = Math.Max(100, Math.Min(200, requestedScale));
                        }
                        if (requestedScales.Count == 0) requestedScales.Add(requestedScale);
                        foreach (int scale in requestedScales)
                        {
                            requestedScale = scale;
                            panel.ApplyDpiTestScale(scale);
                            Application.DoEvents();
                            string stepResult = panel.RunDpiLayoutSelfTest(scale);
                            if (!stepResult.StartsWith("OK", StringComparison.Ordinal))
                                throw new InvalidOperationException(stepResult);
                        }
                        if (maximizedTest)
                        {
                            panel.WindowState = FormWindowState.Maximized;
                            Application.DoEvents();
                        }
                        string layoutResult = panel.RunDpiLayoutSelfTest(requestedScale);
                        if (!layoutResult.StartsWith("OK", StringComparison.Ordinal))
                            throw new InvalidOperationException(layoutResult);
                        if (maximizedTest)
                        {
                            string expandedResult = panel.RunExpandedLayoutSelfTest();
                            if (!expandedResult.StartsWith("OK", StringComparison.Ordinal))
                                throw new InvalidOperationException(expandedResult);
                            if (panel.SnapEnabledForTest != snapBeforeMaximize)
                                throw new InvalidOperationException(
                                    "Maximizing changed the saved snap preference");
                        }
                        // PerMonitorV2 can resize the form when its handle is first shown. Allocate
                        // the QA bitmap afterwards so high-DPI screenshots are not clipped or scaled.
                        panel.Refresh();
                        Application.DoEvents();
                        Thread.Sleep(80);
                        ProgramWindowRect liveRect;
                        if (GetWindowRect(panel.Handle, out liveRect) &&
                            liveRect.Right > liveRect.Left && liveRect.Bottom > liveRect.Top)
                        {
                            using (Bitmap live = new Bitmap(liveRect.Right - liveRect.Left,
                                liveRect.Bottom - liveRect.Top, PixelFormat.Format32bppArgb))
                            {
                                using (Graphics graphics = Graphics.FromImage(live))
                                    graphics.CopyFromScreen(liveRect.Left, liveRect.Top, 0, 0,
                                        live.Size, CopyPixelOperation.SourceCopy);
                                string pageName = Array.IndexOf(args, "--main-ui-test-dashboard") >= 0
                                    ? "dashboard" : (Array.IndexOf(args, "--main-ui-test-ai") >= 0
                                    ? "ai" : "start");
                                live.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                    "main_ui_live_" + pageName + ".png"), ImageFormat.Png);
                            }
                        }
                        using (Bitmap bitmap = new Bitmap(panel.Width, panel.Height, PixelFormat.Format32bppArgb))
                        {
                            panel.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                            panel.Hide();
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                maximizedTest ? "main_ui_test_maximized.png" :
                                (transitionTest ? "main_ui_test_transition.png" :
                                (requestedScale == 100 ? "main_ui_test.png" :
                                "main_ui_test_" + requestedScale + ".png"))), ImageFormat.Png);
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
            if (args != null && Array.IndexOf(args, "--ai-chat-ui-test") >= 0)
            {
                string uiErrorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "ai_chat_ui_test_error.txt");
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    if (File.Exists(uiErrorPath)) File.Delete(uiErrorPath);
                    using (OfflineChatForm panel = new OfflineChatForm(null,
                        AppDomain.CurrentDomain.BaseDirectory))
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
                                "ai_chat_ui_test.png"), ImageFormat.Png);
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
                    using (AiTranslationWindowForm window = new AiTranslationWindowForm(null,
                        delegate(string source) { return Task.FromResult("R> test result"); }))
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
                        window.AppendTranslation("DunkChai：射手村组队，3缺1",
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
            if (args != null && Array.IndexOf(args, "--gamepad-probe") >= 0)
            {
                // Reads HID reports directly, so controller back buttons can be identified
                // even though XInput has no bit for them. The report file is written first so
                // a crash or an exception still leaves a readable trace behind.
                string probePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamepad_probe.txt");
                try
                {
                    int probeSeconds = 20;
                    foreach (string arg in args)
                    {
                        if (!arg.StartsWith("--gamepad-probe-seconds=", StringComparison.OrdinalIgnoreCase)) continue;
                        Int32.TryParse(arg.Substring("--gamepad-probe-seconds=".Length), out probeSeconds);
                    }
                    if (probeSeconds < 3) probeSeconds = 3;
                    if (probeSeconds > 120) probeSeconds = 120;
                    File.WriteAllText(probePath, "探测已启动，正在采集…", Encoding.UTF8);
                    string probeReport = GamepadProbe.Run(probeSeconds);
                    File.WriteAllText(probePath, probeReport, Encoding.UTF8);
                    // The launcher script prints the report itself and would be blocked by a
                    // modal dialog, so the popup is opt-out.
                    if (Array.IndexOf(args, "--gamepad-probe-quiet") < 0)
                    {
                        MessageBox.Show("手柄探测完成，结果已保存到：\n\n" + probePath +
                            "\n\n把这个文件发给开发者即可判断背键能否直接绑定。",
                            "枫语幕手柄探测");
                    }
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(probePath, "手柄探测失败：\r\n" + ex, Encoding.UTF8); }
                    catch { }
                    if (Array.IndexOf(args, "--gamepad-probe-quiet") < 0)
                    {
                        MessageBox.Show("手柄探测失败：\n\n" + ex.Message, "枫语幕手柄探测");
                    }
                }
                return;
            }
            if(args!=null&&Array.IndexOf(args,"--image-test")>=0)
            {
                Application.Run(new ImageTestForm(null,null));return;
            }
            if (!Benchmark && !BenchmarkUi && !AcquirePrimaryInstance()) return;
            try {
                if (!Benchmark && !BenchmarkUi)
                {
                    try
                    {
                        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                        using (Stream executable = File.OpenRead(Assembly.GetExecutingAssembly().Location))
                            LogRuntimeEvent("startup-identity", "role=primary build=windows-v4.0 exeSha256=" +
                                BitConverter.ToString(hash.ComputeHash(executable)).Replace("-", ""));
                    }
                    catch (Exception identityError) { LogRuntimeEvent("startup-identity-failed", "type=" + identityError.GetType().Name); }
                }
                Application.Run(new OverlayForm());
            }
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

        // UI-thread failures are written to a log instead of being shown. The default
        // WinForms exception dialog plays the "Windows Default Beep", which must never
        // happen while the player is in a game.
        private static void LogSilentFailure(Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "运行诊断.log"),
                    DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        private static readonly object diagnosticGate = new object();
        internal static void LogRuntimeEvent(string category, string detail)
        {
            // Only callers' event names, numeric state and provider host/status enter this
            // channel. Never log OCR text, prompts, Authorization or credential values.
            try
            {
                lock (diagnosticGate)
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "运行诊断.log"), DateTime.UtcNow.ToString("o") + " PID=" +
                        Process.GetCurrentProcess().Id + " " + category + " " +
                        (detail ?? "").Replace("\r", " ").Replace("\n", " ") +
                        Environment.NewLine, Encoding.UTF8);
            }
            catch { }
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
        public bool IsOrdinaryItemText;
        public bool IsInterfaceText;
        public bool IsSettingsText;
        public bool IsNpcDialogue;
        public bool ExactOnly;
        // Quest Summary body prose / level gates. Kept separate from IsTaskText because
        // these entries are indexed twice (generic bucket + task bucket) and carry no
        // task id when one wording is shared by a whole chain.
        public bool IsQuestTarget;
        public HashSet<string> DetailWords;
        // Item prose is usable only after the shared semantic guard verifies that the
        // complete visible effect/limit/number semantics agree on both sides.
        public ItemSemanticStatus ItemStatus = ItemSemanticStatus.Undecidable;
        public string ItemStatusReason = "";
    }

    internal sealed partial class TranslationStore
    {
        private readonly string sourcePath;
        private readonly Dictionary<char, List<TranslationEntry>> buckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> taskBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> taskNameBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> skillTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> itemTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> interfaceTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> settingsTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> dialogueTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly Dictionary<char, List<TranslationEntry>> iconTextBuckets =
            new Dictionary<char, List<TranslationEntry>>();
        private readonly List<TranslationEntry> taskNames = new List<TranslationEntry>();
        private readonly List<TranslationEntry> taskEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> iconEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> skillTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> itemTextEntries = new List<TranslationEntry>();
        // Ordinary-item context needs its own trusted indexes: the generic bucket excludes
        // duplicate names, while a tooltip may still be resolvable from its visible body.
        private readonly List<TranslationEntry> itemNameEntries = new List<TranslationEntry>();
        private readonly Dictionary<string, List<TranslationEntry>> itemNamesByNormalized =
            new Dictionary<string, List<TranslationEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TranslationEntry>> itemDescriptionsById =
            new Dictionary<string, List<TranslationEntry>>(StringComparer.Ordinal);
        private readonly List<ItemSentenceReference> itemSentenceReferences =
            new List<ItemSentenceReference>();
        private readonly Dictionary<string, List<ItemSentenceReference>> itemSentenceIndex =
            new Dictionary<string, List<ItemSentenceReference>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> itemPlaces =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<TranslationEntry> interfaceTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> settingsTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> dialogueTextEntries = new List<TranslationEntry>();
        private readonly List<TranslationEntry> uiEntries = new List<TranslationEntry>();
        private readonly Dictionary<string, byte> skillClassificationCache =
            new Dictionary<string, byte>(StringComparer.Ordinal);
        private readonly object skillClassificationLock = new object();
        private string cachedIconText = "";
        private readonly List<KeyValuePair<TranslationEntry, float>> cachedIconCandidates =
            new List<KeyValuePair<TranslationEntry, float>>();
        public int Count { get; private set; }
        public int Revision { get; private set; }
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
            AddSupplementalGlossary(entries);
            RefreshSkillNameIndex(entries);
            // The item semantic guard runs once per dictionary load. Every later path
            // (exact, sentence, template, shared description and auxiliary vocabulary)
            // consults this same verdict; a conflicting row is never drawable.
            ItemTextSemantics.AuditEntries(entries);

            // The classic data contains legitimate same-English-name variants (hair colours,
            // job-specific skills, duplicate transport NPCs, and so on).  If their Chinese
            // translations differ, plain OCR cannot choose safely. Keep every variant in the
            // icon index, but require icon + text matching instead of accepting an arbitrary
            // text-only translation.
            Dictionary<string, HashSet<string>> translationsByEnglish =
                new Dictionary<string, HashSet<string>>();
            foreach (TranslationEntry entry in entries)
            {
                if (entry.IsTaskName || entry.IsTaskText || entry.IsSkillText || entry.IsItemText ||
                    entry.IsSettingsText || entry.IsNpcDialogue) continue;
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
            taskNameBuckets.Clear();
            skillTextBuckets.Clear();
            itemTextBuckets.Clear();
            interfaceTextBuckets.Clear();
            settingsTextBuckets.Clear();
            dialogueTextBuckets.Clear();
            iconTextBuckets.Clear();
            taskNames.Clear();
            taskEntries.Clear();
            iconEntries.Clear();
            skillTextEntries.Clear();
            itemTextEntries.Clear();
            interfaceTextEntries.Clear();
            settingsTextEntries.Clear();
            dialogueTextEntries.Clear();
            itemNameEntries.Clear();
            itemNamesByNormalized.Clear();
            itemDescriptionsById.Clear();
            itemSentenceReferences.Clear();
            itemSentenceIndex.Clear();
            itemPlaces.Clear();
            uiEntries.Clear();
            TaskTextCount = 0;
            SkillTextCount = 0;
            ItemTextCount = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (entry.Normalized.Length == 0) continue;
                // Quest Summary body prose and level gates are indexed twice on purpose.
                // taskBuckets lets the wrapped body text match the selected task, while the
                // generic bucket below keeps short gates such as "Level 12+" paintable in
                // and around the quest window. Moving them into taskBuckets alone would
                // silently disable the generic path.
                if (entry.Category.StartsWith("怀旧服-任务目标", StringComparison.Ordinal))
                {
                    AddToBucket(taskBuckets, entry);
                    taskEntries.Add(entry);
                    TaskTextCount++;
                }
                if (entry.Category.StartsWith("怀旧服-界面", StringComparison.Ordinal))
                    uiEntries.Add(entry);
                if (entry.IsTaskName || entry.IsTaskText)
                {
                    AddToBucket(taskBuckets, entry);
                    taskEntries.Add(entry);
                    if (entry.Category.StartsWith("怀旧服-任务对白#", StringComparison.Ordinal))
                    {
                        AddToBucket(dialogueTextBuckets, entry);
                        dialogueTextEntries.Add(entry);
                    }
                    if (entry.IsTaskName)
                    {
                        taskNames.Add(entry);
                        AddToBucket(taskNameBuckets, entry);
                    }
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
                else if (entry.IsSettingsText)
                {
                    AddToBucket(settingsTextBuckets, entry); settingsTextEntries.Add(entry);
                }
                else if (entry.IsNpcDialogue)
                {
                    AddToBucket(dialogueTextBuckets, entry); dialogueTextEntries.Add(entry);
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
            SortBuckets(taskNameBuckets);
            SortBuckets(skillTextBuckets);
            SortBuckets(itemTextBuckets);
            SortBuckets(interfaceTextBuckets);
            SortBuckets(settingsTextBuckets);
            SortBuckets(dialogueTextBuckets);
            SortBuckets(iconTextBuckets);
            cachedIconText = "";
            cachedIconCandidates.Clear();
            lock (skillClassificationLock) skillClassificationCache.Clear();
            BuildJournalIndex(entries);
            BuildSkillEffectTemplates();
            BuildObjectiveNameIndex(entries);
            BuildItemTextIndex(entries);
            Count = entries.Count; Revision++;
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
            exact.RemoveAll(delegate(MatchResult match) { return match.Entry.IsTaskText &&
                !TranslationValueSafety.PreservesNumbers(text, match.Entry.English); });
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

        public List<MatchResult> FindTaskNameMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, taskNameBuckets, null);
            // Server journals display a chain/group heading as the selected title.
            // These existing translations have no single quest ID, but still belong
            // in the independent short-title path before task-specific prose matching.
            foreach (MatchResult group in FindInBuckets(text, buckets, null))
                if (group.Entry.Category.StartsWith("怀旧服-任务组", StringComparison.Ordinal)) exact.Add(group);
            if (exact.Count > 0) return exact;
            List<MatchResult> approximate = FindApproximateTaskNameMatch(text);
            if (approximate.Count > 0) return approximate;
            // Quest chain titles carry a sequence number in the dictionary but not always on
            // screen ("I Need to Find My Daughter 1" vs the quest list's "I Need to Find My
            // Daughter"). Retry without a trailing number so both shapes meet. Duplicate
            // numbering is still handled inside the approximate matcher, which compares the
            // sequence suffix, so " 2" can never cross-match a " 1" row.
            string normalized = Normalize(text);
            Match tail = Regex.Match(normalized, @"^(?<name>.+?)\s+\d{1,3}$");
            if (!tail.Success || tail.Groups["name"].Value.Length < 5) return new List<MatchResult>();
            exact = FindInBuckets(tail.Groups["name"].Value, taskNameBuckets, null);
            if (exact.Count > 0) return exact;
            return FindApproximateTaskNameMatch(tail.Groups["name"].Value);
        }

        // Shared chain bodies carry no id, so FindTaskMatches would fall back to the
        // task-NAME matcher and never reach the body prose. This entry exposes the body
        // matcher for that case and returns id-less entries only, so a body belonging to
        // one specific quest can never be borrowed by a differently selected task.
        public List<MatchResult> FindSharedTaskBodyMatches(string text)
        {
            List<MatchResult> results = FindInBuckets(text, taskBuckets, "");
            foreach (MatchResult approximate in FindApproximateTaskMatch(text, ""))
            {
                bool duplicate = false;
                foreach (MatchResult existing in results)
                {
                    if (!String.Equals(existing.Entry.English, approximate.Entry.English,
                        StringComparison.Ordinal)) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) results.Add(approximate);
            }
            results.RemoveAll(delegate(MatchResult match) { return match.Entry.TaskId.Length > 0 ||
                !TranslationValueSafety.PreservesNumbers(text, match.Entry.English); });
            if (results.Count == 0)
                foreach (TranslationEntry entry in taskEntries)
                    if (entry.ExactOnly && entry.IsTaskText && entry.TaskId.Length == 0 &&
                        ObservedProseMatch.Equivalent(text, entry.English))
                        results.Add(new MatchResult { Entry=entry, Start=0, Length=Normalize(text).Length });
            return results;
        }

        public string ResolveUniqueTaskTitleId(string text)
        {
            if (String.IsNullOrWhiteSpace(text) || text.Contains("...") || text.Contains("…")) return "";
            string normalized = Normalize(text);
            int longest = 0;
            HashSet<string> exactIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in taskNames)
            {
                int position = normalized.IndexOf(entry.Normalized, StringComparison.Ordinal);
                if (position < 0 || !IsBoundary(normalized, position - 1) ||
                    !IsBoundary(normalized, position + entry.Normalized.Length)) continue;
                if (entry.Normalized.Length * 10 < normalized.Length * 7) continue;
                if (entry.Normalized.Length > longest)
                {
                    longest = entry.Normalized.Length;
                    exactIds.Clear();
                }
                if (entry.Normalized.Length == longest && entry.TaskId.Length > 0)
                    exactIds.Add(entry.TaskId);
            }
            if (exactIds.Count == 1)
                foreach (string id in exactIds) return id;
            if (exactIds.Count > 1) return "";

            List<MatchResult> approximate = FindApproximateTaskNameMatch(text);
            if (approximate.Count == 1) return approximate[0].Entry.TaskId;
            return "";
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
            // An ordinary-item row whose English/Chinese semantics conflict is never
            // drawable, even when the English side matches byte for byte. Equipment
            // descriptions keep their existing path so EQUIP-001 semantics are untouched.
            bool ordinaryConflict = false;
            exact.RemoveAll(delegate(MatchResult match) {
                if (match.Entry == null || !match.Entry.IsOrdinaryItemText) return false;
                if (match.Entry.ItemStatus == ItemSemanticStatus.Trusted) return false;
                ordinaryConflict = true;
                return true;
            });
            if (ordinaryConflict) return new List<MatchResult>();
            if (exact.Count > 0) return exact;
            List<MatchResult> approximate = FindApproximateDetailMatch(text, itemTextEntries, detailId);
            approximate.RemoveAll(delegate(MatchResult match) {
                return match.Entry != null && match.Entry.IsOrdinaryItemText &&
                    match.Entry.ItemStatus != ItemSemanticStatus.Trusted;
            });
            return approximate;
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

        public List<MatchResult> FindSettingsTextMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, settingsTextBuckets, null);
            string normalized = Normalize(text);
            int exactCoverage = 0;
            foreach (MatchResult match in exact) exactCoverage += match.Length;
            // A short settings token can sit inside a longer label (for example,
            // "Screenshot Save Location" used to stop at "Screenshot" + "Location").
            // Prefer a confident whole-label correction unless the exact matches already
            // explain nearly all of the OCR line. This keeps dynamic percentages/numbers
            // visible while preventing short labels from fragmenting a compound control.
            if (exact.Count > 0 && normalized.Length > 0 &&
                exactCoverage * 100 >= normalized.Length * 82) return exact;
            List<MatchResult> approximate =
                FindApproximateScopedText(text, settingsTextEntries, 3, 44, 0.74f);
            return approximate.Count > 0 ? approximate : exact;
        }

        public List<MatchResult> FindDialogueTextMatches(string text)
        {
            List<MatchResult> exact = FindInBuckets(text, dialogueTextBuckets, null);
            string normalized = Normalize(text);
            int longest = 0;
            foreach (MatchResult match in exact)
                longest = Math.Max(longest, match.Entry.Normalized.Length);
            if (exact.Count > 0 && (longest >= 22 || longest * 2 >= normalized.Length))
                return exact;
            List<MatchResult> approximate = FindApproximateDialogueMatch(normalized);
            return approximate.Count > 0 ? approximate : exact;
        }

        private static List<MatchResult> FindApproximateScopedText(string text,
            List<TranslationEntry> entries, int minimumLength, int maximumLength,
            float requiredScore)
        {
            string normalized = Normalize(text);
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < minimumLength || normalized.Length > maximumLength) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in entries)
            {
                if (Math.Abs(entry.Normalized.Length - normalized.Length) >
                    Math.Max(4, entry.Normalized.Length / 3)) continue;
                float score = TextSimilarity(normalized, entry.Normalized);
                if (score < requiredScore) continue;
                if (score > bestScore)
                {
                    secondScore = bestScore; bestScore = score; best = entry;
                }
                else secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.07f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
        }

        private List<MatchResult> FindApproximateDialogueMatch(string normalized)
        {
            List<MatchResult> none = new List<MatchResult>();
            if (normalized.Length < 28 || normalized.Length > 900) return none;
            HashSet<string> inputWords = SignificantWords(normalized);
            if (inputWords.Count < 5) return none;
            TranslationEntry best = null;
            float bestScore = 0, secondScore = 0;
            foreach (TranslationEntry entry in dialogueTextEntries)
            {
                if (entry.DetailWords == null) continue;
                int common = 0;
                foreach (string word in inputWords)
                    if (entry.DetailWords.Contains(word)) common++;
                if (common < 7) continue;
                float precision = (float)common / Math.Max(1, inputWords.Count);
                float coverage = (float)common / Math.Max(1, entry.DetailWords.Count);
                if (precision < 0.55f || coverage < 0.45f) continue;
                float score = precision * 0.46f + coverage * 0.54f;
                if (score > bestScore)
                {
                    if (best != null && best != entry) secondScore = Math.Max(secondScore, bestScore);
                    best = entry; bestScore = score;
                }
                else if (best != entry) secondScore = Math.Max(secondScore, score);
            }
            if (best == null || (secondScore > 0 && bestScore - secondScore < 0.045f)) return none;
            return new List<MatchResult> { new MatchResult {
                Entry = best, Start = 0, Length = normalized.Length
            } };
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
                HashSet<string> candidateWords = entry.DetailWords;
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
                if (entry.ExactOnly || !TranslationValueSafety.PreservesNumbers(text, entry.English)) continue;
                // Three scopes are intentional: null is identity discovery (only rows that
                // own an id), empty is the shared-chain fallback, and a concrete id accepts
                // both that quest and deliberately shared prose.
                if ((!entry.IsTaskText && !entry.IsQuestTarget) || entry.DetailWords == null) continue;
                if (taskId == null)
                {
                    if (entry.TaskId.Length == 0) continue;
                }
                else if (taskId.Length == 0)
                {
                    if (entry.TaskId.Length > 0) continue;
                }
                else if (entry.TaskId.Length > 0 && entry.TaskId != taskId) continue;
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
                if (entry.ExactOnly) continue;
                if (Math.Abs(entry.Normalized.Length - normalized.Length) > Math.Max(5, entry.Normalized.Length / 3))
                    continue;
                if (!TaskSequenceSuffixMatches(normalized, entry.Normalized)) continue;
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
            return DetectUniqueSkillNameId(text);
        }

        public string DetectSkillContentId(string text)
        {
            // The visible body is the identity evidence, never the generic
            // "[Master Level : N]" header. The old first-exact-row path resolved
            // every level-20 tooltip to whichever skill happened to own that
            // header segment, which then borrowed that skill's description.
            return DetectUniqueSkillContentId(text);
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

        public bool HasItemNameAndDescription(string recognizedName, TranslationEntry name,
            string observedText)
        {
            if (name == null || !name.HasIcon || String.IsNullOrEmpty(name.Normalized) ||
                !(name.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal) ||
                  name.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal))) return false;
            string recognized = Normalize(recognizedName), observed = Normalize(observedText);
            if (recognized != name.Normalized && !Regex.IsMatch(recognized,
                "^" + Regex.Escape(name.Normalized) + @" \d+$")) return false;
            string id = CategoryId(name.Category);
            if (id.Length == 0 || observed.Length < 24) return false;
            foreach (TranslationEntry detail in itemTextEntries)
                if (CategoryId(detail.Category) == id && detail.Normalized.Length >= 24 &&
                    (" " + observed + " ").IndexOf(" " + detail.Normalized + " ",
                        StringComparison.Ordinal) >= 0) return true;
            return false;
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
                // Shared chain prose is intentionally indexed with an empty task id so it can
                // still be translated when no unique quest is selected. It is translation
                // evidence, not identity evidence, and must never outscore the selected title.
                if (entry.TaskId.Length == 0) continue;
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
            // Modernized quest prose can keep most of the original body while changing the
            // visible title and a few sentences. A unique long-body match is stronger than a
            // truncated title and lets the selected detail pane recover its task id.
            if (normalized.Length >= 80)
            {
                List<MatchResult> body = FindApproximateTaskMatch(text, null);
                if (body.Count == 1 && body[0].Entry.TaskId.Length > 0)
                    return body[0].Entry.TaskId;
            }
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
                        // null means unscoped lookup; empty means shared prose only; a concrete
                        // task accepts its own rows plus deliberately shared chain text.
                        if (taskId != null)
                        {
                            if (taskId.Length == 0 && candidate.TaskId.Length > 0) continue;
                            if (taskId.Length > 0 && candidate.TaskId.Length > 0 &&
                                candidate.TaskId != taskId) continue;
                        }
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

        // Item prose compares using the scoped Restore/Recover canonical form. The raw
        // similarity is still used everywhere else (tasks, skills, dialogue, names).
        internal static float ItemCanonicalSimilarity(string text, string candidateEnglish)
        {
            return TextSimilarity(ItemTextSemantics.CanonicalSentence(text),
                ItemTextSemantics.CanonicalSentence(candidateEnglish ?? String.Empty));
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
                // The Quest Summary item icon renders a leading "B" as a lowercase "a"
                // ("Bubbling's Huge Bubble" -> "aubbling's Huge Bubble", see the citizenship
                // donation screenshot). This token only ever occurs as that item name, so the
                // repair cannot rewrite player names or free-form chat.
                else if (words[i] == "aubbling's") words[i] = "bubbling's";
                else if (words[i] == "aubbling") words[i] = "bubbling";
                else if (words[i] == "ot") words[i] = "of";
                else if (words[i] == "tor") words[i] = "for";
                else if (words[i] == "torget") words[i] = "forget";
                else if (words[i] == "tun") words[i] = "fun";
                else if (words[i] == "wont") words[i] = "won't";
                else if (words[i] == "cant") words[i] = "can't";
                else if (words[i] == "dont") words[i] = "don't";
                else if (words[i] == "ive") words[i] = "i've";
                else if (words[i] == "thankyou") words[i] = "thank you";
                else if (words[i] == "go" && i + 1 < words.Length && words[i + 1] == "seconds")
                    words[i] = "90";
            }
            normalized = String.Join(" ", words);
            normalized = normalized.Replace("jest helper", "quest helper")
                .Replace("uest helper", "quest helper");
            return normalized;
        }

        internal static bool IsBoundary(string text, int at)
        {
            if (at < 0 || at >= text.Length) return true;
            char c = text[at];
            return !Char.IsLetterOrDigit(c) && c != '\'';
        }

        private static bool TaskSequenceSuffixMatches(string recognized, string candidate)
        {
            string recognizedSuffix = TaskSequenceSuffix(recognized);
            string candidateSuffix = TaskSequenceSuffix(candidate);
            if (recognizedSuffix.Length == 0 && candidateSuffix.Length == 0) return true;
            return recognizedSuffix.Length > 0 &&
                String.Equals(recognizedSuffix, candidateSuffix, StringComparison.Ordinal);
        }

        private static string TaskSequenceSuffix(string normalized)
        {
            if (String.IsNullOrWhiteSpace(normalized)) return "";
            string[] words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "";
            string last = words[words.Length - 1];
            int number;
            if (Int32.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return "n:" + number.ToString(CultureInfo.InvariantCulture);
            if (Regex.IsMatch(last, @"^(?:i|ii|iii|iv|v|vi|vii|viii|ix|x)$",
                RegexOptions.IgnoreCase)) return "r:" + last.ToLowerInvariant();
            return "";
        }

        // Observed server wording is translation evidence, not an invented canonical
        // quest ID. User dictionary entries always win and are never written back.
        private void AddSupplementalGlossary(List<TranslationEntry> entries)
        {
            string path = Path.Combine(Path.GetDirectoryName(sourcePath), "基础补充词库.tsv");
            if (!File.Exists(path)) path = Path.Combine(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location), "基础补充词库.tsv");
            if (!File.Exists(path)) return;
            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationEntry entry in entries) existing.Add(entry.Normalized);
            foreach (TranslationEntry entry in ReadTsv(path))
                if (existing.Add(entry.Normalized)) entries.Add(entry);
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
                string category = parts.Length > 2 ? parts[2].Trim() : "";
                string chinese = PolishChinese(parts[1].Trim());
                bool isTaskName = category.StartsWith("怀旧服-任务#", StringComparison.Ordinal) ||
                    category == "怀旧服-任务别名";
                // Task prose may be scoped to one quest (the #id form) or deliberately
                // shared by a whole chain (the exact category without an id).  Both forms
                // belong in taskBuckets; the shared matcher filters out id-owned rows later.
                // Keep this exact for the no-id form so other task-prefixed categories (names,
                // groups and targets) are not accidentally promoted to prose.
                bool isTaskText = category.StartsWith("怀旧服-任务说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-任务对白#", StringComparison.Ordinal) ||
                    String.Equals(category, "怀旧服-任务说明", StringComparison.Ordinal) ||
                    String.Equals(category, "怀旧服-任务对白", StringComparison.Ordinal);
                // Quest Summary body prose and level gates. These are indexed twice (see the
                // bucket pass below) and need DetailWords so the approximate matcher can still
                // recognise a body whose OCR dropped or garbled a few words mid-sentence.
                bool isQuestTarget = category.StartsWith("怀旧服-任务目标", StringComparison.Ordinal);
                bool isSkillText = category.StartsWith("怀旧服-技能说明#", StringComparison.Ordinal);
                bool isItemText = category.StartsWith("怀旧服-装备说明#", StringComparison.Ordinal) ||
                    category.StartsWith("怀旧服-物品说明#", StringComparison.Ordinal);
                bool isOrdinaryItemText = category.StartsWith("怀旧服-物品说明#", StringComparison.Ordinal);
                if (isItemText)
                {
                    // Item rows frequently use literal "\n" as a soft line break. The
                    // ordinary matcher works on visible sentences, so decode those escapes
                    // before Normalize instead of leaving a bogus "n" token in the source.
                    english = ItemTextSemantics.DecodeLineBreaks(english);
                    chinese = ItemTextSemantics.DecodeLineBreaks(chinese);
                }
                string normalized = Normalize(english);
                bool isInterfaceText = category.StartsWith("怀旧服-界面长句", StringComparison.Ordinal);
                bool isSettingsText = category.StartsWith("怀旧服-设置", StringComparison.Ordinal);
                bool isNpcDialogue = category.StartsWith("怀旧服-NPC对白#", StringComparison.Ordinal);
                string taskId = (isTaskName || isTaskText) && category.LastIndexOf('#') >= 0
                    ? category.Substring(category.LastIndexOf('#') + 1) : "";
                ulong iconHash = 0;
                bool hasIcon = parts.Length > 3 && UInt64.TryParse(parts[3].Trim(),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iconHash);
                // Icon-backed entries are distinct by source category/ID even when their English
                // names are identical. This preserves colour, appearance, job and duplicate-NPC
                // variants for icon-assisted disambiguation.
                string dedupeScope = (isTaskName || isTaskText) ? taskId :
                    ((hasIcon || category.StartsWith("怀旧服-技能#", StringComparison.Ordinal) || isSkillText || isItemText || isInterfaceText || isSettingsText ||
                      isNpcDialogue) && category.Length > 0 ? category : "general");
                string dedupeKey = normalized + "\t" + dedupeScope;
                if (normalized.Length == 0 || chinese.Length == 0 || !seen.Add(dedupeKey)) continue;
                result.Add(new TranslationEntry {
                    English = english, Chinese = chinese,
                    Category = category, Normalized = normalized,
                    IconHash = hasIcon ? iconHash : 0, HasIcon = hasIcon,
                    TaskId = taskId, IsTaskName = isTaskName, IsTaskText = isTaskText,
                    IsSkillText = isSkillText, IsItemText = isItemText,
                    IsOrdinaryItemText = isOrdinaryItemText,
                    IsInterfaceText = isInterfaceText, IsSettingsText = isSettingsText,
                    IsNpcDialogue = isNpcDialogue,
                    IsQuestTarget = isQuestTarget,
                    ExactOnly = parts.Length > 3 && parts[3] == "观测原文（精确）",
                    DetailWords = (isTaskText || isQuestTarget || isSkillText || isItemText ||
                        isInterfaceText || isSettingsText || isNpcDialogue)
                        ? new HashSet<string>(normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
                        : null
                });
            }
            return result;
        }

        private static string PolishChinese(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            string text = value.Replace("<br>", Environment.NewLine)
                .Replace("Meso", "金币").Replace("meso", "金币")
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

    // How much the pipeline trusts a finished label. v4.0 publishes high-confidence labels
    // first (fast first paint) and only spends a second OCR pass on the low-confidence ones,
    // so every label carries its own evidence instead of being judged by text length.
    internal enum LabelConfidence
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    // v4.0 rendering modes. Seamless replaces the original text area with the sampled
    // background so the overlay stops looking like a stack of black cards; CardFallback keeps
    // the high-contrast card whenever the background cannot be estimated reliably.
    internal enum LabelRenderMode
    {
        CardFallback = 0,
        Seamless = 1,
        PreserveOriginal = 2
    }

    internal sealed class OverlayLabel
    {
        public RectangleF Bounds;
        public string Text;
        public bool Wrap;
        public bool StableLayout;

        // v4.0 confidence metadata. SourceBounds is the original text area that may be erased,
        // while Bounds stays the safe area the translation lays out in. Keeping them apart
        // means a longer translation can never widen the erase region over live game values.
        public RectangleF SourceBounds;
        public bool HasSourceBounds;
        // True when the source span was split inside one OCR word. Never promote
        // this to a measured boundary in evidence or equipment-style planning.
        public bool SourceBoundaryEstimated;
        public LabelConfidence Confidence = LabelConfidence.High;
        public string MatchSource = "";
        public float ConfidenceReason = 0.0f;

        // v4.0 rendering.
        public LabelRenderMode RenderMode = LabelRenderMode.CardFallback;
        public Color BackgroundColor = Color.Empty;
        // The colour the original text was drawn in, so the translation can match it.
        public Color TextColor = Color.Empty;
        // v4.0 geometry. A multi-line body keeps the rectangle of every OCR row it replaces, so
        // the erase pass can clear each original row on its own. Filling their union covered the
        // gaps between the rows and left a wide light slab across the quest panel.
        public List<RectangleF> SourceLineBounds;
        // Optional detected panel/body boundary this label must stay inside. An empty rectangle
        // means no panel boundary is known, and the measured source block is used instead - never
        // the whole game client.
        public RectangleF PanelBounds;
        // Set when the complete translation cannot be shown inside its boundary at a readable
        // font: the original text is then preserved and only an explicit notice is drawn.
        public bool Overflow;
        public bool PaintedLastPass;
        public bool TooltipPriority;
        public string LayoutFailureReason = "";
        public string FusionFailureReason = "";
        public string SemanticRole = "";
        public RectangleF KeepClearBounds;
        public QuestJournalDocument Journal;
        // Equipment fields carry the shared panel plan and their own protected value
        // rectangle. Composition uses the plan's typography and never samples the value
        // pixels into the replacement.
        public EquipmentFieldPlan EquipmentField;
        public EquipmentPanelPlan EquipmentPanel;
        // Ordinary item tooltips carry one shared plan so the name, description and
        // auxiliary rows are measured as one panel while still failing independently.
        public ItemPanelPlan ItemPanel;
        public ItemTextPart ItemTextPart;
        // Skill panels carry one shared plan and one section owner per source region.
        // The compositor uses these sizes directly, so prose sections cannot shrink
        // independently to an unreadable 8px fallback.
        public SkillPanelPlan SkillPanel;
        public SkillSectionPlan SkillSection;
        public float LayoutFontSize;
        public float LayoutMinimumFontSize;
        public string LayoutFontFamily = "";
    }

    // Windows OCR exposes no usable per-word confidence, so the pipeline derives an
    // explainable proxy from evidence it already has: whether the dictionary matched exactly
    // or approximately, whether the source text was truncated, and how much of the screen the
    // pass that produced the label actually read.
    internal static class LabelConfidencePolicy
    {
        internal const int ExactMatched = 70;
        internal const int ApproximateMatched = 35;
        internal const int Unmatched = 20;
        internal const int TruncatedPenalty = 25;

        internal static int Score(string translated, int matchQuality, int stageQuality)
        {
            int score = matchQuality + stageQuality;
            if (String.IsNullOrEmpty(translated)) return score;
            // OCR truncated rows end in an ellipsis; those must never outrank a complete row.
            if (translated.EndsWith("...", StringComparison.Ordinal) ||
                translated.EndsWith("…", StringComparison.Ordinal))
                score -= TruncatedPenalty;
            return score;
        }

        internal static LabelConfidence Level(int score)
        {
            if (score >= 70) return LabelConfidence.High;
            if (score >= 50) return LabelConfidence.Medium;
            return LabelConfidence.Low;
        }

        // High confidence is what the fast first paint may trust. Medium and low results are
        // still painted, but they never replace an existing high-confidence label.
        internal static bool IsTrusted(LabelConfidence level)
        {
            return level == LabelConfidence.High;
        }

        internal static bool IsTruncated(string translated)
        {
            if (String.IsNullOrEmpty(translated)) return false;
            return translated.EndsWith("...", StringComparison.Ordinal) ||
                translated.EndsWith("…", StringComparison.Ordinal);
        }
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
            // No silent truncation: an over-long translation is routed to the explicit overflow
            // state by the paint pass, which leaves the original English in place.
            text = label.Text;
            bounds = candidate;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }

        // The overlay sizes most labels by itself. This is the ceiling for growth when nothing
        // better is known. The rule used to be Math.Max(bounds.Width, 360), which can never cap an
        // already oversized rectangle: a bad quest-body rectangle stayed exactly as wide as it was.
        internal const float MaximumAutoLabelWidth = 360.0f;
        // Hard stop for an automatically sized label. Even a measured block is not trusted past
        // this, so a bogus OCR rectangle can never size a card across the desktop.
        internal const float ConservativeMaximumLabelWidth = 720.0f;
        // A short label may still grow a little past the block it replaces, because Chinese is
        // often a few pixels wider than the English word. Long prose may not grow at all.
        internal const float ShortLabelGrowthAllowance = 80.0f;
        internal const float MaximumAutoLabelHeight = 600.0f;
        // A translation at or above this length counts as prose. The layout and the ink policy must
        // agree on that, otherwise a wrapped body can keep a purple ink sampled from mixed glyphs.
        internal const int LongProseLength = 24;
        // Hard limit for one label. Beyond it the renderer refuses to guess and shows the explicit
        // overflow state instead of silently truncating the translation.
        internal const int MaximumDrawableTextLength = 4096;

        // Exact fit proof. GDI+ reports how many characters it actually laid out; anything below
        // the full length means the paragraph would be clipped, which must never happen silently.
        internal static bool FitsExactly(Graphics graphics, string text, Font font, SizeF area)
        {
            if (graphics == null || font == null || String.IsNullOrEmpty(text)) return false;
            if (area.Width < 8.0f || area.Height < 8.0f) return false;
            int charactersFitted = 0;
            int linesFilled = 0;
            graphics.MeasureString(text, font, area, StringFormat.GenericTypographic,
                out charactersFitted, out linesFilled);
            return charactersFitted >= text.Length;
        }

        internal static bool TryMeasureReadableCard(Graphics graphics, string text, Font font,
            StringFormat format, RectangleF anchor, RectangleF client, float maximumWidth,
            out RectangleF card, out RectangleF textArea, out int charactersFitted, out string failure)
        {
            card = textArea = RectangleF.Empty;
            charactersFitted = 0; failure = "invalid-input";
            if (graphics == null || font == null || format == null || String.IsNullOrEmpty(text) ||
                !IsFiniteRect(client) || client.Width < 32 || client.Height < 24 ||
                Single.IsNaN(maximumWidth) || Single.IsInfinity(maximumWidth) || maximumWidth < 32) return false;
            if (text.Length > MaximumDrawableTextLength) { failure = "text-length-limit"; return false; }
            float contentWidth = (float)Math.Floor(Math.Min(client.Width, maximumWidth) - 16);
            // Finite width from the real game client. Int32.MaxValue is not a valid GDI layout budget.
            SizeF measured = graphics.MeasureString(text, font, new SizeF(contentWidth, 32767), format);
            card = SceneTranslationPolicy.CardBounds(anchor, measured, measured, client, maximumWidth);
            if (card.IsEmpty) { failure = "client-height-limit"; return false; }
            // Tightening a short card can change line breaks. Re-measure at its ACTUAL content width.
            SizeF actual = graphics.MeasureString(text, font, new SizeF(card.Width - 16, 32767), format);
            if (Single.IsNaN(actual.Height) || Single.IsInfinity(actual.Height) || actual.Height < 0)
            { failure = "invalid-measurement"; return false; }
            card.Height = (float)Math.Ceiling(actual.Height) + 16;
            if (card.Height > client.Height) { failure = "client-height-limit"; return false; }
            float lineStep = Math.Max(1, (float)Math.Ceiling(font.GetHeight(graphics)) + 2);
            for (;;)
            {
                card.Y = Math.Max(client.Top, Math.Min(anchor.Top, client.Bottom - card.Height));
                textArea = new RectangleF(card.X + 8, card.Y + 6, card.Width - 16, card.Height - 12);
                int lines;
                // Measurement, proof and drawing share the same font, format and content rectangle.
                graphics.MeasureString(text, font, textArea.Size, format, out charactersFitted, out lines);
                if (charactersFitted >= text.Length) { failure = ""; return true; }
                if (card.Height >= client.Height) { failure = "incomplete-character-fit"; return false; }
                // Keep the full-character requirement; grow the box for fallback-font line metrics.
                card.Height = Math.Min(client.Height, card.Height + lineStep);
            }
        }

        // The one predicate shared by the sampling pass and the paint pass, so "long prose" cannot
        // mean two different things in the two places that decide ink and layout.
        internal static bool IsWrappedProse(OverlayLabel label, string text)
        {
            if (label == null) return false;
            if (label.Wrap) return true;
            if (label.SourceLineBounds != null && label.SourceLineBounds.Count > 1) return true;
            return !String.IsNullOrEmpty(text) && text.Length >= LongProseLength;
        }

        internal static bool IsFiniteRect(RectangleF rect)
        {
            return IsFinite(rect.X) && IsFinite(rect.Y) &&
                IsFinite(rect.Width) && IsFinite(rect.Height);
        }

        // Geometry safety for Seamless. "The text was recognised confidently" says nothing about
        // whether the rectangle can be erased safely, so the geometry is judged on its own terms:
        // non-finite, empty, off-capture, absurdly large or poorly aligned regions keep the card.
        // A high-confidence dictionary match on a bogus rectangle used to recolour a slab of the
        // game in a colour that came from somewhere else entirely.
        internal static bool IsSafeSeamlessGeometry(RectangleF layout, RectangleF source,
            int captureWidth, int captureHeight)
        {
            if (!IsFiniteRect(layout) || !IsFiniteRect(source)) return false;
            if (source.Width < 6.0f || source.Height < 5.0f) return false;
            if (layout.Width < 6.0f || layout.Height < 5.0f) return false;
            if (captureWidth < 16 || captureHeight < 16) return false;
            // Wide tooltips can legitimately span most of a small game window.
            // Limit total area as well as height instead of vetoing a long row.
            float maximumWidth = Math.Max(64.0f, captureWidth * 0.95f);
            float maximumHeight = Math.Max(64.0f, captureHeight * 0.60f);
            if (source.Width > maximumWidth || source.Height > maximumHeight) return false;
            if (layout.Width > maximumWidth || layout.Height > maximumHeight) return false;
            float maximumArea = Math.Max(4096.0f, captureWidth * (float)captureHeight * 0.36f);
            if (source.Width * source.Height > maximumArea || layout.Width * layout.Height > maximumArea) return false;
            // Poorly aligned: the erase has to sit inside the area the translation lays out in,
            // otherwise a sliver of the original text survives beside its replacement.
            RectangleF slack = layout;
            slack.Inflate(4.0f, 4.0f);
            RectangleF overlap = RectangleF.Intersect(slack, source);
            if (overlap.Width <= 0.0f || overlap.Height <= 0.0f) return false;
            return overlap.Width * overlap.Height >= source.Width * source.Height * 0.90f;
        }

        // The rectangle to measure must fit inside the captured frame; anything else cannot be
        // verified against real pixels at all and must keep the card.
        internal static bool IsInsideCapture(RectangleF rect, int captureWidth, int captureHeight)
        {
            if (!IsFiniteRect(rect)) return false;
            if (rect.Width <= 0.0f || rect.Height <= 0.0f) return false;
            return rect.Left >= 0.0f && rect.Top >= 0.0f &&
                rect.Right <= captureWidth && rect.Bottom <= captureHeight;
        }

        // Ink is recovered from the pixels that are not the background. On a multi-colour quest
        // line that average mixes black, blue and red into a purple the panel never used, and
        // magenta is this window's transparency key, so a sampled ink is only usable when it is
        // a real colour that actually contrasts with the background it will be drawn on.
        internal static bool IsUsableInk(Color ink, Color background)
        {
            if (ink == Color.Empty || background == Color.Empty) return false;
            if (ink.R > 150 && ink.B > 150 && ink.G < 110) return false;
            // Dark purple/magenta as well: when red and blue both dominate green the colour came
            // from mixing glyphs of different colours, and it is never a colour this UI uses.
            if (ink.R > ink.G + 40 && ink.B > ink.G + 40) return false;
            double inkLuminance = ink.R * 0.299 + ink.G * 0.587 + ink.B * 0.114;
            double backgroundLuminance = background.R * 0.299 + background.G * 0.587 +
                background.B * 0.114;
            return Math.Abs(inkLuminance - backgroundLuminance) >= 60.0;
        }

        // Width a label may lay out in. The measured block wins whenever it is known, because it
        // is the area the original text really occupied. It is then bounded by the game client's
        // remaining width and by a conservative maximum scaled for the current DPI, so a bad OCR
        // rectangle can never size a label across the desktop.
        internal static float ComputeLabelCeiling(RectangleF bounds, RectangleF block, bool hasBlock,
            bool longProse, bool allowGrowth, RectangleF safeArea, float conservativeMaximumWidth)
        {
            float ceiling;
            if (hasBlock)
                ceiling = longProse || !allowGrowth
                    ? block.Width
                    : Math.Min(MaximumAutoLabelWidth, block.Width + ShortLabelGrowthAllowance);
            else
                ceiling = Math.Min(MaximumAutoLabelWidth, Math.Max(bounds.Width, 90.0f));
            float maximum = Math.Max(120.0f,
                Math.Min(ConservativeMaximumLabelWidth, conservativeMaximumWidth));
            if (safeArea.Width >= 16.0f)
                maximum = Math.Min(maximum, Math.Max(24.0f, safeArea.Right - bounds.X));
            return Math.Max(24.0f, Math.Min(ceiling, maximum));
        }

        // Pure layout decision plus the explicit fit contract.
        //
        // * Long prose (and every label that may not grow) never leaves the measured block
        //   horizontally.
        // * The height may grow downwards inside the safe area, so a translation longer than the
        //   English block is shown in full instead of being cut off.
        // * `fitted` states whether the measured text really fits the returned rectangle. The
        //   caller keeps shrinking the font while it does not, and only a text that cannot fit even
        //   at the smallest font is allowed to fall back to a card.
        internal static bool TryComputeLabelLayout(RectangleF bounds, RectangleF block, bool hasBlock,
            bool longProse, bool allowGrowth, RectangleF safeArea, float conservativeMaximumWidth,
            SizeF measured, SizeF wrappedSize, out RectangleF layout, out bool useWrapped,
            out bool forceCard, out bool fitted)
        {
            layout = RectangleF.Empty;
            useWrapped = false;
            forceCard = false;
            fitted = false;
            if (!IsFiniteRect(bounds)) return false;
            if (hasBlock && (!IsFiniteRect(block) || block.Width < 2.0f || block.Height < 2.0f))
                return false;
            float ceiling = ComputeLabelCeiling(bounds, block, hasBlock, longProse, allowGrowth,
                safeArea, conservativeMaximumWidth);
            useWrapped = longProse || measured.Width + 8.0f > ceiling;
            // The ceiling caps the width in both branches: the previous rule only capped growth,
            // so an oversized rectangle passed straight through and was drawn at full size.
            float width = useWrapped
                ? ceiling
                : Math.Min(ceiling, Math.Max(bounds.Width, measured.Width + 8.0f));
            float neededHeight = useWrapped ? wrappedSize.Height + 7.0f : measured.Height + 5.0f;
            float height = Math.Max(bounds.Height, neededHeight);
            if (hasBlock && longProse)
            {
                // Long prose may not widen past the block. It may grow downwards, but only inside
                // the safe area and never past the conservative height ceiling.
                width = Math.Min(width, Math.Max(24.0f, block.Right - bounds.X));
                float safeBottom = safeArea.Height >= 16.0f
                    ? safeArea.Bottom : bounds.Y + MaximumAutoLabelHeight;
                float verticalBudget = Math.Max(block.Height,
                    Math.Min(MaximumAutoLabelHeight, safeBottom - bounds.Y - 6.0f));
                height = Math.Min(height, Math.Max(12.0f, verticalBudget));
            }
            else
            {
                height = Math.Min(height, MaximumAutoLabelHeight);
            }
            fitted = neededHeight <= height + 1.0f;
            forceCard = longProse && hasBlock && !fitted;
            layout = new RectangleF(bounds.X, bounds.Y, Math.Max(1.0f, width), Math.Max(1.0f, height));
            return true;
        }

        // The erase rectangles may never be wider or taller than the label's measured span: a skill
        // row can share one OCR line with a "Master Level : 3" prefix, and that prefix has to stay
        // visible. Rows are clipped to the span instead of being stored at full OCR line width.
        internal static List<RectangleF> ClampRowsToSpan(List<RectangleF> rows, RectangleF span)
        {
            List<RectangleF> clamped = new List<RectangleF>();
            if (rows == null) return clamped;
            foreach (RectangleF row in rows)
            {
                if (!IsFiniteRect(row) || row.Width <= 0.0f || row.Height <= 0.0f) continue;
                RectangleF clipped = IsFiniteRect(span) && span.Width > 0.0f && span.Height > 0.0f
                    ? RectangleF.Intersect(row, span) : row;
                if (clipped.Width < 1.0f || clipped.Height < 1.0f) continue;
                clamped.Add(clipped);
            }
            return clamped;
        }

        // Erase rectangles in paint space: one per original OCR row when the label kept them,
        // otherwise the single measured source block. This is what removes the wide light slab
        // the union fill used to leave between the rows of a wrapped body.
        internal static List<RectangleF> BuildEraseRects(List<RectangleF> sourceLines,
            RectangleF sourceUnion, float dx, float dy)
        {
            List<RectangleF> erase = new List<RectangleF>();
            if (sourceLines != null)
            {
                foreach (RectangleF line in sourceLines)
                {
                    if (!IsFiniteRect(line) || line.Width <= 0.0f || line.Height <= 0.0f) continue;
                    erase.Add(new RectangleF(line.Left + dx, line.Top + dy,
                        Math.Max(1.0f, line.Width), Math.Max(1.0f, line.Height)));
                }
            }
            if (erase.Count == 0 && IsFiniteRect(sourceUnion) &&
                sourceUnion.Width > 0.0f && sourceUnion.Height > 0.0f)
            {
                erase.Add(new RectangleF(sourceUnion.Left + dx, sourceUnion.Top + dy,
                    Math.Max(1.0f, sourceUnion.Width), Math.Max(1.0f, sourceUnion.Height)));
            }
            return erase;
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
        public string EquipmentReason = "";
        public bool IsSettings;
        public string TaskId = "";
        public string SkillId = "";
    }

    internal sealed class SkillPanelCandidate
    {
        public List<OcrLine> Lines;
        public string Text;
        public MatchResult Match;
        public int Score;
        public string SemanticRole = "";
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
        // Detail and Dialogue are two kinds of foreground evidence, not two competing
        // resource levels. When one or more real panels are open, every visible panel
        // and the tooltip above it are promoted to the first scheduling level together.
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

    internal enum LabelBuildScope
    {
        Discovery = 0,
        FocusedPanel = 1
    }

    internal sealed class RecognitionTier
    {
        public RecognitionPriorityKind Kind;
        public double ResourceLevel;
    }

    internal static class RecognitionPriorityPlanner
    {
        internal static bool ShouldUseCurrentInterface(bool hasDetail, bool hasDialogue,
            bool hasOutsideDialogue, bool hasVisibleText)
        {
            return hasVisibleText && !hasDetail && !hasDialogue && !hasOutsideDialogue;
        }

        internal static List<RecognitionTier> Select(bool hasDetail, bool hasDialogue,
            bool hasOutsideDialogue, bool hasCurrentInterface, int maximumTargets)
        {
            List<RecognitionTier> result = new List<RecognitionTier>();
            maximumTargets = Math.Max(1, Math.Min(3, maximumTargets));
            bool foreground = hasDetail || hasDialogue;

            // A cursor tooltip and every deliberately opened panel are peers. Giving the
            // second open panel only 75% resources made "属性 + 背包" depend on OCR order.
            // Keep their kinds separate for crop handling, but schedule both at level one.
            if (hasDetail && result.Count < maximumTargets)
                result.Add(new RecognitionTier { Kind = RecognitionPriorityKind.Detail, ResourceLevel = 1.0 });
            if (hasDialogue && result.Count < maximumTargets)
                result.Add(new RecognitionTier { Kind = RecognitionPriorityKind.Dialogue, ResourceLevel = 1.0 });
            if (hasOutsideDialogue && result.Count < maximumTargets)
                result.Add(new RecognitionTier { Kind = RecognitionPriorityKind.OutsideDialogue,
                    ResourceLevel = foreground ? 0.5 : 1.0 });
            if (hasCurrentInterface && result.Count < maximumTargets)
                result.Add(new RecognitionTier { Kind = RecognitionPriorityKind.CurrentInterface,
                    ResourceLevel = foreground ? 0.35 : (hasOutsideDialogue ? 0.75 : 1.0) });
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
                string name = tier.Kind == RecognitionPriorityKind.Detail ? "光标详情" :
                    (tier.Kind == RecognitionPriorityKind.Dialogue ? "已打开面板" :
                    (tier.Kind == RecognitionPriorityKind.OutsideDialogue ? "任务助手" : "普通界面"));
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
        internal ChatColorSample Evidence { get; set; }
        internal RectangleF Bounds = RectangleF.Empty;
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
            return FindStyleCore(parsedLine, 0, true);
        }

        internal ChatVisualStyle FindStyle(string parsedLine, int occurrence)
        {
            return FindStyleCore(parsedLine, occurrence, false);
        }

        internal ChatVisualStyle StyleForRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= Lines.Count) return ChatVisualStyle.Default;
            ChatVisualStyle style = Lines[rowIndex].Style;
            return style == null ? ChatVisualStyle.Default : style;
        }

        private ChatVisualStyle FindStyleCore(string parsedLine, int occurrence,
            bool requireUnambiguous)
        {
            string parsed = Normalize(parsedLine);
            if (parsed.Length == 0) return ChatVisualStyle.Default;
            if (occurrence < 0) occurrence = 0;
            string parsedSpeaker, parsedMessage;
            SplitSpeakerText(parsedLine, out parsedSpeaker, out parsedMessage);
            List<ChatLineMatch> matches = new List<ChatLineMatch>();
            foreach (ChatCaptureLine candidate in Lines)
            {
                bool exact;
                int score = MatchScore(parsedLine, parsedSpeaker, parsedMessage, candidate,
                    out exact);
                if (score <= 0) continue;
                matches.Add(new ChatLineMatch {
                    Score = score, Exact = exact, Line = candidate
                });
            }
            if (matches.Count == 0) return ChatVisualStyle.Default;
            matches.Sort(delegate(ChatLineMatch left, ChatLineMatch right) {
                int byScore = right.Score.CompareTo(left.Score);
                if (byScore != 0) return byScore;
                return left.Line.Bounds.Top.CompareTo(right.Line.Bounds.Top);
            });
            // Exact speaker + exact body identity always outranks a similar row.
            List<ChatLineMatch> exactMatches = new List<ChatLineMatch>();
            foreach (ChatLineMatch match in matches)
                if (match.Exact) exactMatches.Add(match);
            List<ChatLineMatch> pool;
            if (exactMatches.Count > 0) pool = exactMatches;
            else
            {
                // Similar rows stay ambiguous: a shared body or a one-character name
                // difference must not silently pick the first channel.
                int bestScore = matches[0].Score;
                pool = new List<ChatLineMatch>();
                foreach (ChatLineMatch match in matches)
                    if (match.Score >= bestScore - 400) pool.Add(match);
            }
            if (pool.Count == 0) return ChatVisualStyle.Default;
            if (requireUnambiguous)
            {
                // The text-only API has no row index. More than one equally good
                // physical row is ambiguous even when the sampled source happens to
                // agree; the row-aware live path resolves it by index instead.
                if (pool.Count != 1) return ChatVisualStyle.Default;
                ChatVisualStyle only = pool[0].Line.Style;
                return only == null ? ChatVisualStyle.Default : only;
            }
            // Without an exact nickname+body identity, several equally good fuzzy
            // rows are ambiguous in the occurrence API too.
            if (exactMatches.Count == 0 && pool.Count != 1)
                return ChatVisualStyle.Default;
            if (occurrence >= pool.Count) occurrence = pool.Count - 1;
            ChatVisualStyle style = pool[occurrence].Line.Style;
            return style == null ? ChatVisualStyle.Default : style;
        }


        internal List<ChatVisualStyle> FindStyles(IList<string> parsedLines)
        {
            List<ChatVisualStyle> result = new List<ChatVisualStyle>();
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
            if (parsedLines != null)
                foreach (string line in parsedLines)
                {
                    string key = Normalize(line);
                    int occurrence;
                    if (!seen.TryGetValue(key, out occurrence)) occurrence = 0;
                    seen[key] = occurrence + 1;
                    result.Add(FindStyle(line, occurrence));
                }
            return result;
        }

        private sealed class ChatLineMatch
        {
            internal int Score;
            internal bool Exact;
            internal ChatCaptureLine Line;
        }

        private static int MatchScore(string parsedLine, string parsedSpeaker,
            string parsedMessage, ChatCaptureLine candidate, out bool exact)
        {
            exact = false;
            if (candidate == null) return 0;
            string candidateSpeaker, candidateMessage;
            SplitSpeakerText(candidate.Text, out candidateSpeaker, out candidateMessage);
            if (candidateSpeaker.Length > 0 && IsNoticeSpeaker(candidateSpeaker))
                candidateSpeaker = "";
            if (parsedSpeaker.Length > 0 && IsNoticeSpeaker(parsedSpeaker))
                parsedSpeaker = "";
            bool exactSpeaker = false;
            if (parsedSpeaker.Length > 0)
            {
                // The source row must contain the same speaker. A shared sentence
                // body alone can never associate one player's row with another's.
                if (candidateSpeaker.Length == 0) return 0;
                if (!SameSpeaker(SpeakerCore(parsedSpeaker), SpeakerCore(candidateSpeaker)))
                    return 0;
                // SameSpeaker is deliberately OCR-tolerant. Exact identity is only
                // claimed when the normalised nickname is identical as well.
                exactSpeaker = ExactSpeaker(parsedSpeaker, candidateSpeaker);
            }
            else if (candidateSpeaker.Length > 0)
            {
                // Without a nickname there is no identity to associate. Only the
                // whole normalised row may match, never the body alone.
                int lineLength = Normalize(parsedLine).Length;
                if (Normalize(parsedLine) != Normalize(candidate.Text)) return 0;
                exact = true;
                return 12000 + lineLength;
            }
            string parsedBody = Normalize(parsedMessage);
            string candidateBody = Normalize(candidateMessage);
            if (parsedBody.Length == 0 || candidateBody.Length == 0) return 0;
            if (parsedBody == candidateBody)
            {
                exact = exactSpeaker;
                return (exact ? 10000 : 8000) + parsedBody.Length;
            }
            int shortest = Math.Min(parsedBody.Length, candidateBody.Length);
            if (shortest >= 6 &&
                (parsedBody.IndexOf(candidateBody, StringComparison.Ordinal) >= 0 ||
                 candidateBody.IndexOf(parsedBody, StringComparison.Ordinal) >= 0))
                return 6000 + shortest;
            int minimumPrefix = Math.Max(6, (int)Math.Ceiling(shortest * 0.6));
            int prefix = SharedPrefix(parsedBody, candidateBody);
            if (prefix >= minimumPrefix) return 4000 + prefix;
            // OCR jitter that preserves the same speaker and most of the same body
            // is still one message; a five-character coincidence is not.
            if (parsedSpeaker.Length > 0 && SameOcrMessage(parsedLine, candidate.Text))
                return 3000 + shortest;
            return 0;
        }

        private static bool ExactSpeaker(string left, string right)
        {
            string a = SpeakerIdentity(SpeakerCore(left));
            string b = SpeakerIdentity(SpeakerCore(right));
            return a.Length > 0 && String.Equals(a, b, StringComparison.Ordinal);
        }

        private static bool SameOcrMessage(string left, string right)
        {
            string leftSpeaker, leftMessage, rightSpeaker, rightMessage;
            SplitSpeakerText(left, out leftSpeaker, out leftMessage);
            SplitSpeakerText(right, out rightSpeaker, out rightMessage);
            if (leftSpeaker.Length > 0 && rightSpeaker.Length > 0 &&
                !SameSpeaker(SpeakerCore(leftSpeaker), SpeakerCore(rightSpeaker))) return false;
            string a = Normalize(leftMessage), b = Normalize(rightMessage);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            int longest = Math.Max(a.Length, b.Length), shortest = Math.Min(a.Length, b.Length);
            if (shortest < 8 || longest - shortest > Math.Max(5, longest / 2)) return false;
            int limit = Math.Max(3, longest / 4);
            if (EditDistanceWithin(a, b, limit)) return true;
            return SharedPrefix(a, b) >= Math.Min(9, shortest);
        }

        private static bool SameSpeaker(string left, string right)
        {
            string a = SpeakerIdentity(left), b = SpeakerIdentity(right);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            int shortest = Math.Min(a.Length, b.Length);
            if (shortest < 4 || Math.Abs(a.Length - b.Length) > 2) return false;
            if ((a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)) &&
                Math.Abs(a.Length - b.Length) <= 2) return true;
            return EditDistanceWithin(a, b, 1);
        }

        private static string SpeakerIdentity(string value)
        {
            return Regex.Replace((value ?? "").ToLowerInvariant(),
                @"[^a-z0-9\u3400-\u9fff]+", "").Replace('0', 'o').Replace('1', 'l');
        }

        private static bool EditDistanceWithin(string left, string right, int limit)
        {
            if (Math.Abs(left.Length - right.Length) > limit) return false;
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i; int rowMinimum = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                    rowMinimum = Math.Min(rowMinimum, current[j]);
                }
                if (rowMinimum > limit) return false;
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length] <= limit;
        }

        private static bool IsNoticeSpeaker(string speaker)
        {
            return Regex.IsMatch((speaker ?? "").Trim(),
                @"^(?:system|notice|系统|系统公告|公告)$", RegexOptions.IgnoreCase);
        }

        private static string StripChannelBadge(string speaker)
        {
            return Regex.Replace((speaker ?? "").Trim(),
                @"\s+(?:CH|SH|C)[A-Z0-9@±'’]{0,6}\s*$", "",
                RegexOptions.IgnoreCase).Trim();
        }

        private static string SpeakerCore(string speaker)
        {
            string stripped = StripChannelBadge(speaker);
            // GMS character names contain no spaces. OCR often appends the channel
            // badge/ear glyph to the name (BeelsFad«HÖl 0); the leading name token is
            // the stable identity in that case.
            Match name = Regex.Match(stripped, @"^([A-Za-z][A-Za-z0-9_]{1,23})");
            if (name.Success) return name.Groups[1].Value;
            return stripped;
        }

        private static void SplitSpeakerText(string line, out string speaker, out string message)
        {
            string text = line ?? "";
            int separator = -1;
            foreach (char marker in new char[] { ':', '：', '•', '·' })
            {
                int at = text.IndexOf(marker);
                if (at > 0 && (separator < 0 || at < separator)) separator = at;
            }
            if (separator > 0)
            {
                speaker = text.Substring(0, separator).Trim();
                message = text.Substring(separator + 1).Trim();
            }
            else
            {
                speaker = "";
                message = text.Trim();
            }
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
    }

    internal static class ChatRegionSettings
    {
        internal const int MinimumWidth = 80;
        internal const int MinimumHeight = 20;
        private const int Scale = 10000;
        private const string OriginValueName = "ChatRegionOrigin";

        // These are the user's canonical pixels and reference client, never a previously
        // rounded projection. Only an explicit selection/alignment changes them.
        private static string lastSavedRegionSignature = "";
        private static Rectangle lastSavedRegion = Rectangle.Empty;
        private static Rectangle lastSavedGame = Rectangle.Empty;
        private static ChatRegionOrigin lastSavedOrigin;

        private static void Save(Rectangle region, Rectangle gameBounds, ChatRegionOrigin origin)
        {
            if (!IsUsable(region) || gameBounds.Width < 1 || gameBounds.Height < 1) return;
            string signature = region.X + "," + region.Y + "," + region.Width + "," + region.Height + "," +
                gameBounds.X + "," + gameBounds.Y + "," + gameBounds.Width + "," + gameBounds.Height + "," +
                (int)origin;
            if (signature == lastSavedRegionSignature) return;
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
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
            catch (UnauthorizedAccessException)
            {
                // Locked-down or virtualised profiles deny the key. Keep the in-memory region so
                // exclusion still works for this session instead of failing the translate path.
            }
            catch (System.Security.SecurityException) { }
            lastSavedRegionSignature = signature;
            lastSavedRegion = region;
            lastSavedGame = gameBounds;
            lastSavedOrigin = origin;
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
            if (!IsUsable(region)) region = DefaultForGame(gameBounds);
            Save(region, gameBounds, ChatRegionOrigin.Automatic);
            return region;
        }

        public static Rectangle LoadAbsolute()
        {
            if (IsUsable(lastSavedRegion)) return lastSavedRegion;
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
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
            return value.Width >= MinimumWidth && value.Height >= MinimumHeight;
        }

        public static ChatRegionOrigin GetOrigin()
        {
            if (IsUsable(lastSavedRegion)) return lastSavedOrigin;
            if (!HasSelection()) return ChatRegionOrigin.None;
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
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
            if (IsUsable(lastSavedRegion) && lastSavedGame.Width > 0 && lastSavedGame.Height > 0)
                return ResolveCanonical(lastSavedRegion, lastSavedGame, gameBounds);
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
                {
                    if (key != null)
                    {
                        Rectangle reference = new Rectangle(
                            Convert.ToInt32(key.GetValue("ChatGameX", 0)),
                            Convert.ToInt32(key.GetValue("ChatGameY", 0)),
                            Convert.ToInt32(key.GetValue("ChatGameW", 0)),
                            Convert.ToInt32(key.GetValue("ChatGameH", 0)));
                        Rectangle saved = new Rectangle(
                            Convert.ToInt32(key.GetValue("ChatX", 0)),
                            Convert.ToInt32(key.GetValue("ChatY", 0)),
                            Convert.ToInt32(key.GetValue("ChatW", 0)),
                            Convert.ToInt32(key.GetValue("ChatH", 0)));
                        if (IsUsable(saved) && reference.Width > 0 && reference.Height > 0)
                            return ResolveCanonical(saved, reference, gameBounds);
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
            if (IsUsable(absolute))
            {
                Rectangle overlap = Rectangle.Intersect(absolute, gameBounds);
                long sourceArea = (long)absolute.Width * absolute.Height;
                long overlapArea = (long)overlap.Width * overlap.Height;
                if (sourceArea > 0 && overlapArea * 10 >= sourceArea * 7) return overlap;
            }
            return DefaultForGame(gameBounds);
        }

        internal static Rectangle ResolveCanonical(Rectangle selection, Rectangle reference,
            Rectangle current)
        {
            if (reference.Width <= 0 || reference.Height <= 0 || current.Width <= 0 || current.Height <= 0)
                return Rectangle.Empty;
            Rectangle projected = new Rectangle(
                current.X + (int)Math.Round((selection.X - reference.X) * (double)current.Width / reference.Width),
                current.Y + (int)Math.Round((selection.Y - reference.Y) * (double)current.Height / reference.Height),
                Math.Max(MinimumWidth, (int)Math.Round(selection.Width * (double)current.Width / reference.Width)),
                Math.Max(MinimumHeight, (int)Math.Round(selection.Height * (double)current.Height / reference.Height)));
            return Rectangle.Intersect(projected, current);
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

    internal sealed partial class OverlayForm : Form
    {
        private const int HOTKEY_JOURNAL_PREVIOUS = 1011;
        private const int HOTKEY_JOURNAL_NEXT = 1012;
        private bool journalPagingShortcuts;
        private const int HOTKEY_SHOW = 1001;
        private const int HOTKEY_HIDE = 1002;
        private const int HOTKEY_FLOATING_WINDOW = 1003;
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
        // Smaller variants of the label font, built lazily. A translated quest body is usually
        // longer than the English block it replaces, so the body is scaled down to stay inside
        // that block instead of being clipped at its edge.
        private Font[] scaledLabelFonts;

        private Font[] ScaledLabelFonts()
        {
            if (scaledLabelFonts == null)
            {
                float size = overlayFont.Size;
                scaledLabelFonts = new Font[] {
                    new Font(overlayFont.FontFamily, Math.Max(6.0f, size * 0.90f),
                        overlayFont.Style, GraphicsUnit.Pixel),
                    new Font(overlayFont.FontFamily, Math.Max(6.0f, size * 0.80f),
                        overlayFont.Style, GraphicsUnit.Pixel),
                    new Font(overlayFont.FontFamily, Math.Max(6.0f, size * 0.70f),
                        overlayFont.Style, GraphicsUnit.Pixel)
                };
            }
            return scaledLabelFonts;
        }
        private bool processing;
        private bool manualTranslationPending;
        private int manualTranslationRequestId;
        private int recognitionGeneration;
        private Stopwatch visibleRequestWatch;
        private bool firstPaintRecorded;
        private int lastPaintedTranslationCount;
        // The optional panel-AI fallback belongs to one manual F8 request only.  Keeping its
        // source here lets HideTranslation abort an in-flight HTTP request before the next
        // screenshot starts; generation checks still provide the second, UI-side late-result
        // guard after the await returns.
        private CancellationTokenSource panelAiCancellation;
        private readonly object panelAiCacheLock = new object();
        private readonly Dictionary<string, string> panelAiCache =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private const int PanelAiCacheCapacity = 128;
        private const int PanelAiBudgetMs = 2420;
        private sealed class ScenePatch
        {
            internal Rectangle Area;
            internal int[] Pixels;
        }
        private readonly List<RectangleF> paintedCards = new List<RectangleF>();
        private readonly List<ScenePatch> displayedScene = new List<ScenePatch>();
        private readonly System.Windows.Forms.Timer displayedSceneTimer = new System.Windows.Forms.Timer();
        private bool sceneCheckBusy;
        private int changedSceneChecks;
        private int displayedSceneRevision;
        private int lastRenderModeLogGeneration = -1;
        private int lastItemPaintLogGeneration = -1;
        private bool visibleTranslation;
        private bool shuttingDown;
        private OcrEngine ocr;
        private readonly StrongOcrClient strongOcr;
        private string lastFastChatIdentity = "";
        private ChatCaptureFrame lastReviewedChatFrame;
        private bool directOcrBitmapBridgeAvailable = true;
        private Keys showKey = Keys.F8;
        private Keys hideKey = Keys.F9;
        private Keys floatingWindowKey = Keys.F10;
        private bool showHotkeyRegistered;
        private bool hideHotkeyRegistered;
        private bool floatingWindowHotkeyRegistered;
        private uint showModifiers;
        private uint hideModifiers;
        private uint floatingWindowModifiers;
        private GamepadShortcut showGamepadShortcut;
        private GamepadShortcut hideGamepadShortcut;
        private readonly GamepadShortcutLatch gamepadLatch = new GamepadShortcutLatch();
        private readonly uint[] gamepadStates = new uint[4];
        private readonly System.Windows.Forms.Timer gamepadTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer continuousTranslationTimer = new System.Windows.Forms.Timer();
        // v4.0 layer 1: the client we translate is a locked window, not "whatever is in front".
        // This timer is deliberately the only thing that runs while idle; it reads window
        // geometry and identity, never OCR and never the network.
        private readonly MapleWindowTracker mapleWindows = new MapleWindowTracker();
        private readonly System.Windows.Forms.Timer mapleWindowTimer = new System.Windows.Forms.Timer();
        private string lastLockNotice = "";
        // v4.0 acceptance metrics, published in last_run.txt.
        private long benchmarkFirstVisibleMs;
        private string benchmarkStrongOcrStatus = "未安装";
        // Temporary quest-shell diagnostic; remove after the synthetic benchmark is repaired.
        private string benchmarkQuestDebug = "";
        // Labels already shown by the fast first paint. The final pass must keep them,
        // otherwise the player watches the translation shrink as reviews finish - which
        // looks like the overlay losing words it had just displayed.
        private RectangleF requestFocusBounds = RectangleF.Empty;
        private bool requestFocusIsTooltip;
        private long benchmarkConvergedMs;
        // Confidence breakdown of the finished label set, reported in last_run.txt so the
        // fast-first-paint logic and the confidence policy can be checked from a benchmark
        // run rather than only inferred.
        private int benchmarkHighLabels;
        private int benchmarkMediumLabels;
        private int benchmarkLowLabels;
        private int benchmarkSeamlessLabels;
        // Why each label landed on its confidence: the match source decides the base score,
        // so an all-high-confidence reading is only meaningful if the sources are known.
        private int benchmarkExactLabels;
        private int benchmarkApproximateLabels;
        private int benchmarkLayoutLabels;
        // The erase regions themselves, so the render decision can be replayed offline
        // against the exact area the renderer would sample. The boxes in 覆盖标签 are wider,
        // and measuring those overstates the background deviation.
        private string benchmarkSourceBounds = "";
        // A modal dialog during play steals focus and interrupts the game, so a translate-time
        // problem is shown as a short-lived on-screen notice instead.
        private string lockNoticeText = "";
        private DateTime lockNoticeUntilUtc = DateTime.MinValue;
        private System.Windows.Forms.Timer lockNoticeTimer;
        private bool scanIndicatorVisible;
        private DateTime scanIndicatorStartedUtc = DateTime.MinValue;
        private System.Windows.Forms.Timer scanIndicatorTimer;
        private TranslationRangeMode translationRangeMode = TranslationRangeMode.Balanced;
        private bool continuousTranslationEnabled;
        private bool aiChatFloatingWindowEnabled;
        private bool suppressSystemAlerts;
        private int continuousTranslationMisses;
        private int continuousTranslationFailures;
        private DateTime continuousTranslationSuppressedUntilUtc = DateTime.MinValue;
        private Rectangle captureBounds;
        private readonly List<RectangleF> captureTextRows = new List<RectangleF>();
        private bool captureTextRowsOverflow;
        private Rectangle gameBounds;
        private DictionaryOnlyForm dictionaryEditor;
        private HotkeyForm hotkeyEditor;
        private OfflineChatForm chatTranslator;
        private MainPanelForm mainPanel;
        private RegisteredWaitHandle activationWait;

        [ComImport]
        [Guid("905a0fef-bc53-11df-8c49-001e4fc686da")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IBufferByteAccess
        {
            void Buffer(out IntPtr value);
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
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

        public OverlayForm() : this(false, null, null) { }
        internal OverlayForm(bool pictureOnly, PrimaryOcrClient sharedOcr, TranslationStore sharedStore)
        {
            imageTestOnly=pictureOnly;
            ownsPrimaryOcr=sharedOcr==null;
            primaryOcr=sharedOcr??new PrimaryOcrClient(baseDir);
            translations = sharedStore??new TranslationStore(Path.Combine(baseDir, "枫语幕词库.tsv"));
            if(sharedStore==null)translations.Load();
            strongOcr = StrongOcrClient.CreateDefault(baseDir);
            if (!imageTestOnly && !Program.Benchmark && !Program.BenchmarkUi) { Task captureWarmup = mapleWindows.WarmCaptureAsync(); }
            // r11 uses the user's faster original engine by default. Paddle remains
            // available when its complete component is installed and explicitly selected.
            ocr=OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if(ocr==null)ocr=OcrEngine.TryCreateFromUserProfileLanguages();
            if(ocr==null&&primaryOcr.UseWindows)
            {
                if(primaryOcr.HasTier(primaryOcr.Tier))primaryOcr.SetEngine(primaryOcr.Tier);
                else throw new InvalidOperationException("Windows OCR 不可用，请安装系统英语 OCR，或使用完整 Paddle 对比包。");
            }
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
            // Lock layer: poll window identity/geometry on a low frequency. This is the only
            // work performed while idle, and it never OCRs and never calls the network.
            mapleWindowTimer.Interval = MapleWindowTracker.PollIntervalMs;
            mapleWindowTimer.Tick += delegate { PollMapleWindow(); };
            displayedSceneTimer.Interval = 600;
            displayedSceneTimer.Tick += async delegate { await RetireChangedSceneAsync(false); };
            if (!imageTestOnly && !Program.Benchmark && !Program.BenchmarkUi) displayedSceneTimer.Start();
            mapleWindows.Rebound += delegate(object sender, MapleRebindReason reason)
            {
                if (reason != MapleRebindReason.FirstLock)
                {
                    HideTranslation();
                    lastFastChatIdentity = "";
                    lastReviewedChatFrame = null;
                }
                MapleWindowInfo target = mapleWindows.Locked;
                if (target != null) gameBounds = target.ClientBounds;
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.OnCaptureGeometryChanged(target == null ? Rectangle.Empty :
                        ChatRegionSettings.ResolveForGame(target.ClientBounds));
                Program.LogRuntimeEvent("window-change", "reason=" + reason +
                    " version=" + mapleWindows.GeometryVersion);
            };
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
                bool h1 = RegisterShortcut(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                bool h2 = RegisterShortcut(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                bool h3 = RegisterShortcut(Handle, HOTKEY_FLOATING_WINDOW,
                    floatingWindowModifiers, (uint)floatingWindowKey);
                if (!h1 || !h2 || !h3)
                {
                    await Task.Delay(120);
                    if (!h1) h1 = RegisterShortcut(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                    if (!h2) h2 = RegisterShortcut(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                    if (!h3) h3 = RegisterShortcut(Handle, HOTKEY_FLOATING_WINDOW,
                        floatingWindowModifiers, (uint)floatingWindowKey);
                }
                showHotkeyRegistered = h1;
                hideHotkeyRegistered = h2;
                floatingWindowHotkeyRegistered = h3;
                Program.LogRuntimeEvent("hotkey-register", "F8=" + h1 + " F9=" + h2 + " F10=" + h3);
                if (!h1 || !h2 || !h3)
                    SetInteractionStatus("快捷键注册失败，请在主窗口快捷键页选择其他组合。");
                UpdateGamepadPolling();
                UpdateContinuousTranslationPolling();
                // Establish the lock once at startup, then let the low-frequency timer track it.
                if (!Program.Benchmark && !Program.BenchmarkUi)
                {
                    mapleWindows.Refresh();
                    lastLockNotice = mapleWindows.Diagnostic;
                }
                UpdateMapleWindowPolling();
                tray.ShowBalloonTip(2500, "枫语幕已启动",
                    "常规/任务共 " + translations.Count + " 条，任务 " + translations.TaskCount + " 个/文本 " +
                    translations.TaskTextCount + " 条，图标指纹 " + translations.IconCount + " 条，已载入内存。" +
                    HotkeyText(showKey, showModifiers) + " 翻译开关，" +
                    HotkeyText(hideKey, hideModifiers) + " 自动对齐聊天框，" +
                    HotkeyText(floatingWindowKey, floatingWindowModifiers) + " 呼出AI悬浮窗。手柄：翻译 " +
                    showGamepadShortcut + "，缩回 " + hideGamepadShortcut + "。" +
                    "双击托盘图标打开主界面；右键可重新显示AI翻译悬浮窗。" +
                    ((!h1 || !h2 || !h3) ? "（有快捷键注册失败）" : ""), ToolTipIcon.Info);
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
            {
                ChatColorSample evidence = line.Evidence ?? new ChatColorSample();
                report.Append(line.Style.HasBackground ? "BAND" : "TEXT")
                    .Append('/').Append(line.Style.Kind)
                    .Append('/').Append(line.Style.Category)
                    .Append(" bounds=").Append(line.Bounds.X.ToString("0", CultureInfo.InvariantCulture))
                    .Append(',').Append(line.Bounds.Y.ToString("0", CultureInfo.InvariantCulture))
                    .Append(',').Append(line.Bounds.Width.ToString("0", CultureInfo.InvariantCulture))
                    .Append(',').Append(line.Bounds.Height.ToString("0", CultureInfo.InvariantCulture))
                    .Append(" band=").Append(evidence.Band)
                    .Append(" cov=").Append(evidence.Coverage.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(" row=").Append(evidence.RowCoverage.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(" inkMatch=").Append(evidence.InkMatchesBand)
                    .Append(" edge=").Append(evidence.Boundary)
                    .Append(" inkPixels=").Append(evidence.InkPixels)
                    .Append(" fg=").Append(line.Style.ForeColor.R).Append(',')
                    .Append(line.Style.ForeColor.G).Append(',').Append(line.Style.ForeColor.B)
                    .Append(" bg=").Append(line.Style.BackColor.R).Append(',')
                    .Append(line.Style.BackColor.G).Append(',').Append(line.Style.BackColor.B)
                    .Append(" | ").AppendLine(line.Text);
            }
            foreach (string parsed in OfflineChatForm.ParseChatLines(frame.PhysicalLineText()))
            {
                ChatVisualStyle parsedStyle = frame.FindStyle(parsed);
                report.Append("PARSED_STYLE/").Append(parsedStyle.Kind).Append('/')
                    .Append(parsedStyle.Category).Append(" | ").AppendLine(parsed);
                report.Append("PARSED | ").AppendLine(parsed);
            }
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
            using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
            {
                Keys parsed;
                if (key == null) return;
                string sk = Convert.ToString(key.GetValue("ShowKey", "F8"));
                string hk = Convert.ToString(key.GetValue("HideKey", "F9"));
                string fk = Convert.ToString(key.GetValue("FloatingWindowKey", "F10"));
                if (Enum.TryParse<Keys>(sk, true, out parsed)) showKey = parsed;
                if (Enum.TryParse<Keys>(hk, true, out parsed)) hideKey = parsed;
                if (Enum.TryParse<Keys>(fk, true, out parsed)) floatingWindowKey = parsed;
                showModifiers = ParseModifiers(Convert.ToString(key.GetValue("ShowModifiers", "")));
                hideModifiers = ParseModifiers(Convert.ToString(key.GetValue("HideModifiers", "")));
                floatingWindowModifiers = ParseModifiers(Convert.ToString(
                    key.GetValue("FloatingWindowModifiers", "")));
                showGamepadShortcut = GamepadShortcut.Parse(Convert.ToString(key.GetValue("GamepadShowShortcut", "")));
                hideGamepadShortcut = GamepadShortcut.Parse(Convert.ToString(key.GetValue("GamepadHideShortcut", "")));
                int savedRange = Convert.ToInt32(key.GetValue("TranslationRangeMode", 2), CultureInfo.InvariantCulture);
                if (savedRange >= (int)TranslationRangeMode.Maximum && savedRange <= (int)TranslationRangeMode.Minimum)
                    translationRangeMode = (TranslationRangeMode)savedRange;
                continuousTranslationEnabled = Convert.ToInt32(
                    key.GetValue("ContinuousTranslationEnabled", 0), CultureInfo.InvariantCulture) != 0;
                aiChatFloatingWindowEnabled = Convert.ToInt32(
                    key.GetValue("AiChatFloatingWindowEnabled",
                        key.GetValue("IndependentWindowEnabled", 0)),
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
        internal Keys FloatingWindowKey { get { return floatingWindowKey; } }
        internal uint ShowModifiers { get { return showModifiers; } }
        internal uint HideModifiers { get { return hideModifiers; } }
        internal uint FloatingWindowModifiers { get { return floatingWindowModifiers; } }
        internal GamepadShortcut ShowGamepadShortcut { get { return showGamepadShortcut; } }
        internal GamepadShortcut HideGamepadShortcut { get { return hideGamepadShortcut; } }
        internal int DictionaryEntryCount { get { return translations.Count; } }
        internal int TaskEntryCount { get { return translations.TaskTextCount; } }
        internal string ShowHotkeyDescription { get { return HotkeyText(showKey, showModifiers); } }
        internal string HideHotkeyDescription { get { return HotkeyText(hideKey, hideModifiers); } }
        internal string FloatingWindowHotkeyDescription
        {
            get { return HotkeyText(floatingWindowKey, floatingWindowModifiers); }
        }
        internal string HotkeyRegistrationStatus
        {
            get
            {
                return showHotkeyRegistered && hideHotkeyRegistered &&
                    floatingWindowHotkeyRegistered ? "" : "键盘快捷键注册失败，请更换冲突组合";
            }
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
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
                    key.SetValue("TranslationRangeMode", (int)mode, RegistryValueKind.DWord);
            }
            catch { /* Read-only registry policies must not break the live UI. */ }
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        internal void ApplyContinuousTranslation(bool enabled)
        {
            continuousTranslationEnabled = enabled;
            continuousTranslationMisses = 0;
            continuousTranslationFailures = 0;
            continuousTranslationSuppressedUntilUtc = DateTime.MinValue;
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
                    key.SetValue("ContinuousTranslationEnabled", enabled ? 1 : 0,
                        RegistryValueKind.DWord);
            }
            catch { /* Keep the current-session setting when persistence is denied. */ }
            UpdateContinuousTranslationPolling();
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        internal void ApplyAiChatFloatingWindow(bool enabled)
        {
            aiChatFloatingWindowEnabled = enabled;
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
                    key.SetValue("AiChatFloatingWindowEnabled", enabled ? 1 : 0,
                        RegistryValueKind.DWord);
            }
            catch { /* Keep the current-session setting when persistence is denied. */ }
            if (chatTranslator != null && !chatTranslator.IsDisposed)
                chatTranslator.ApplyFloatingWindow(enabled);
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        private void ShowCurrentTranslations()
        {
            Invalidate();
        }

        private bool RegisterShortcut(IntPtr handle, int id, uint modifiers, uint key)
        {
            bool ok = RegisterHotKey(handle, id, modifiers | 0x4000, key);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            Program.LogRuntimeEvent("hotkey-native", "id=" + id + " key=" + key +
                " modifiers=" + modifiers + " registered=" + ok + " win32=" + error);
            return ok;
        }

        internal bool ApplyHotkeys(Keys newShowKey, uint newShowModifiers,
            Keys newHideKey, uint newHideModifiers,
            Keys newFloatingWindowKey, uint newFloatingWindowModifiers)
        {
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            UnregisterHotKey(Handle, HOTKEY_FLOATING_WINDOW);
            bool ok1 = RegisterShortcut(Handle, HOTKEY_SHOW, newShowModifiers, (uint)newShowKey);
            bool ok2 = RegisterShortcut(Handle, HOTKEY_HIDE, newHideModifiers, (uint)newHideKey);
            bool ok3 = RegisterShortcut(Handle, HOTKEY_FLOATING_WINDOW,
                newFloatingWindowModifiers, (uint)newFloatingWindowKey);
            if (!ok1 || !ok2 || !ok3)
            {
                UnregisterHotKey(Handle, HOTKEY_SHOW);
                UnregisterHotKey(Handle, HOTKEY_HIDE);
                UnregisterHotKey(Handle, HOTKEY_FLOATING_WINDOW);
                showHotkeyRegistered = RegisterShortcut(Handle, HOTKEY_SHOW, showModifiers, (uint)showKey);
                hideHotkeyRegistered = RegisterShortcut(Handle, HOTKEY_HIDE, hideModifiers, (uint)hideKey);
                floatingWindowHotkeyRegistered = RegisterShortcut(Handle,
                    HOTKEY_FLOATING_WINDOW, floatingWindowModifiers, (uint)floatingWindowKey);
                SetInteractionStatus("快捷键被其他程序占用，已恢复原来的可用设置。");
                if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
                return false;
            }
            showKey = newShowKey; showModifiers = newShowModifiers;
            hideKey = newHideKey; hideModifiers = newHideModifiers;
            floatingWindowKey = newFloatingWindowKey;
            floatingWindowModifiers = newFloatingWindowModifiers;
            showHotkeyRegistered = true;
            hideHotkeyRegistered = true;
            floatingWindowHotkeyRegistered = true;
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
                if (processing || manualTranslationPending || IsChatTranslationBusy()) return;
                RunManualTranslationFromInputAsync(true);
            }
            else if (action == GamepadShortcutAction.Hide)
            {
                HideTranslation();
            }
        }

        private bool IsChatTranslationBusy()
        {
            return chatTranslator != null && !chatTranslator.IsDisposed &&
                chatTranslator.HasPendingTranslationWork;
        }

        private async void RunManualTranslationFromInputAsync(bool suppressAlerts)
        {
            bool previous = suppressSystemAlerts;
            suppressSystemAlerts = suppressSystemAlerts || suppressAlerts;
            try { await ShowTranslationFromHotkeyAsync(); }
            catch (Exception ex)
            {
                Program.LogRuntimeEvent("manual-failed", "type=" + ex.GetType().Name);
                SetInteractionStatus("翻译未完成：" + ex.Message);
            }
            finally { suppressSystemAlerts = previous; }
        }

        internal void ReloadDictionary()
        {
            try
            {
                TranslationStore replacement = new TranslationStore(Path.Combine(baseDir, "枫语幕词库.tsv"));
                replacement.Load();
                translations = replacement;
                Exception aiReloadError = null;
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                {
                    try { chatTranslator.ReloadGlossary(); }
                    catch (Exception ex) { aiReloadError = ex; }
                }
                if (aiReloadError == null)
                    tray.ShowBalloonTip(1200, "词库已重新载入",
                        translations.Count + " 条；截屏与AI实时翻译已立即更新。", ToolTipIcon.Info);
                else
                    tray.ShowBalloonTip(2600, "截屏词库已更新，AI刷新失败",
                        aiReloadError.Message + "；AI仍保留上一次的稳定索引。", ToolTipIcon.Warning);
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
                int count = chatTranslator != null && !chatTranslator.IsDisposed ? chatTranslator.ReloadGlossary() : 0;
                if (showNotice) SetInteractionStatus("词库已载入" + (count > 0 ? "，聊天术语 " + count + " 条" : "") + "。");
            }
            catch (Exception ex)
            {
                if (showNotice) ShowInlineNotice("AI词库同步失败：\n\n" + ex.Message, "枫语幕");
            }
        }

        internal void ShowDictionaryEditor()
        {
            HideTranslation();
            if (dictionaryEditor == null || dictionaryEditor.IsDisposed) dictionaryEditor = new DictionaryOnlyForm(this, baseDir);
            EnsureMainPanel().ShowEmbeddedPage(dictionaryEditor, null);
        }

        private MainPanelForm EnsureMainPanel()
        {
            if (mainPanel == null || mainPanel.IsDisposed) mainPanel = new MainPanelForm(this);
            mainPanel.Show();
            mainPanel.Activate();
            return mainPanel;
        }

        internal void ShowEmbeddedPage(Form content, Action completed)
        {
            EnsureMainPanel().ShowEmbeddedPage(content, completed);
        }

        internal void ShowGameRegionSelector(ChatRegionSelectorForm selector, Rectangle game, Action completed)
        {
            HideTranslation();
            EnsureMainPanel().ShowGameRegionSelector(selector, game, completed);
        }

        internal void ShowOnlineSettings(Action completed)
        {
            EnsureMainPanel().ShowOnlineSettingsPage(completed);
        }

        private void ShowInlineNotice(string message, string title)
        {
            SetInteractionStatus(title + "：" + message);
        }

        internal string InteractionStatus { get; private set; }
        internal void SetInteractionStatus(string message)
        {
            InteractionStatus = message;
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.RefreshStatus();
        }

        internal void ShowHotkeyEditor()
        {
            HideTranslation();
            if (hotkeyEditor == null || hotkeyEditor.IsDisposed) hotkeyEditor = new HotkeyForm(this);
            EnsureMainPanel().ShowEmbeddedPage(hotkeyEditor, null);
        }

        internal void ShowChatTranslator()
        {
            HideTranslation();
            try
            {
                if (mainPanel == null || mainPanel.IsDisposed)
                    mainPanel = new MainPanelForm(this);
                mainPanel.ShowAiPage();
                mainPanel.Show();
                mainPanel.Activate();
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(3000, "AI功能打开失败",
                    ex.Message + "；F8词库翻译仍可继续使用。", ToolTipIcon.Error);
            }
        }

        internal void AttachChatTranslator(Control host)
        {
            if (host == null || host.IsDisposed) return;
            // Constructing this form performs the AI glossary/knowledge initialization.
            // It remains lazy so F8 dictionary translation is isolated from AI failures.
            if (chatTranslator == null || chatTranslator.IsDisposed)
                chatTranslator = new OfflineChatForm(this, baseDir);
            chatTranslator.ApplyFloatingWindow(aiChatFloatingWindowEnabled);
            chatTranslator.PrepareForEmbeddedHost();
            if (chatTranslator.Parent != host)
            {
                if (chatTranslator.Parent != null)
                    chatTranslator.Parent.Controls.Remove(chatTranslator);
                host.Controls.Add(chatTranslator);
            }
            chatTranslator.Show();
            chatTranslator.EnsureSurfaceInitialized();
            chatTranslator.BringToFront();
        }

        internal bool TryGetMapleWindowBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (Program.Benchmark || Program.BenchmarkUi) return false;
            bool fullScreen;
            if (mapleWindows.TryGetCaptureBounds(out bounds, out fullScreen) && !fullScreen)
                return true;
            mapleWindows.Refresh();
            return mapleWindows.TryGetCaptureBounds(out bounds, out fullScreen) && !fullScreen;
        }

        internal bool TryGetSnapWindowBounds(out Rectangle bounds)
        {
            // Positioning needs valid geometry, not permission to capture. An unrelated
            // covering window must not leave a manual resize using an old-width anchor.
            bounds = Rectangle.Empty;
            mapleWindows.Refresh();
            MapleWindowInfo target = mapleWindows.Locked;
            if (target == null || mapleWindows.State != MapleWindowState.Locked) return false;
            bounds = target.ClientBounds;
            return bounds.Width >= 160 && bounds.Height >= 300;
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
                    ShowInlineNotice("当前三个安装文件已经和 GitHub 最新正式包完全一致，不需要替换。",
                        "枫语幕一键更新");
                    return;
                }
                button.Text = "正在安全重启…";
                update.StartInstaller();
                Close();
            }
            catch (Exception ex)
            {
                ShowInlineNotice("更新没有动到当前版本。\n\n" + ex.Message +
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
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
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
            return RestoreAiChatFloatingWindow();
        }

        internal bool RestoreAiChatFloatingWindowFromHotkey()
        {
            return RestoreAiChatFloatingWindow();
        }

        private bool RestoreAiChatFloatingWindow()
        {
            if (chatTranslator == null || chatTranslator.IsDisposed)
            {
                if (mainPanel == null || mainPanel.IsDisposed)
                    mainPanel = new MainPanelForm(this);
                mainPanel.ShowAiPage();
                if (chatTranslator == null || chatTranslator.IsDisposed) return false;
            }
            if (!aiChatFloatingWindowEnabled) ApplyAiChatFloatingWindow(true);
            return chatTranslator.RestoreFloatingWindowFromTray();
        }

        private void BuildTray()
        {
            tray.Icon = Program.AppIcon;
            tray.Text = "枫语幕 v4.0";
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
                tray.Text = "枫语幕 v4.0（内存待机）";
                tray.Visible = true;
                Program.LogRuntimeEvent("tray-register-request", "localVisible=" + tray.Visible + " shellPresence=unverified");
            }
            catch (ObjectDisposedException) { return; }
            if (openMainPanel) ShowMainPanel();
        }

        internal void ShowMainPanel()
        {
            HideTranslation();
            if (mainPanel == null || mainPanel.IsDisposed) mainPanel = new MainPanelForm(this);
            mainPanel.ShowStartPage();
            mainPanel.RefreshStatus();
            mainPanel.Show();
            mainPanel.Activate();
        }

        internal void HideMainPanel()
        {
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.Hide();
        }

        internal void MinimizeMainPanelToTray()
        {
            try
            {
                RestoreTrayIcon(false);
                if (!tray.Visible)
                {
                    ShowInlineNotice("托盘图标未能建立，主窗口不会被隐藏。", "枫语幕");
                    return;
                }
                tray.ShowBalloonTip(2200, "枫语幕仍在运行",
                    "双击右下角托盘图标即可恢复主界面。", ToolTipIcon.Info);
                HideMainPanel();
            }
            catch (Exception ex)
            {
                ShowInlineNotice("最小化到托盘失败，主窗口仍保留。\r\n" + ex.Message, "枫语幕");
            }
        }

        private async Task ShowTranslationFromHotkeyAsync()
        {
            // F8 is a true toggle. A second press also cancels an OCR pass that has not yet
            // published results, so a late completion cannot make the overlay reappear.
            if (manualTranslationPending)
            {
                HideTranslation();
                SetInteractionStatus("已取消本次识别；再次按 F8 可重新识别。");
                return;
            }
            if (visibleTranslation)
            {
                HideTranslation(true);
                SetInteractionStatus("译文已清除；再次按 F8 重新识别并核对可补全内容。");
                return;
            }
            // Rebind events invalidate old requests. Resolve an idle window change before
            // creating this request, so the first F8 after bringing the game forward or
            // moving it is not cancelled by its own initial refresh.
            if (!Program.Benchmark) mapleWindows.Refresh();
            int requestId = ++manualTranslationRequestId;
            Program.LogRuntimeEvent("manual-request", "id=" + requestId + " generation=" + recognitionGeneration);
            visibleRequestWatch = Stopwatch.StartNew();
            firstPaintRecorded = false;
            manualTranslationPending = true;
            try
            {
                // Target-surface capture does not require hiding the main panel or chat UI.
                while (processing && requestId == manualTranslationRequestId && !shuttingDown)
                    await Task.Delay(15);
                if (requestId != manualTranslationRequestId || shuttingDown) return;
                await ShowTranslationAsync(false, requestId);
            }
            finally
            {
                if (requestId == manualTranslationRequestId) manualTranslationPending = false;
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
                // The chat box lives inside the client, so it is aligned against the locked
                // client rectangle. ChatRegionSettings stores coordinates relative to it.
                Rectangle screen;
                if (!ResolveCaptureBounds(out screen)) return;
                using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height,
                    PixelFormat.Format32bppArgb))
                {
                    long geometryVersion;
                    Rectangle captured;
                    using (WindowCaptureFrame client = await mapleWindows.CaptureClientBitmapAsync())
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        captured = client.Bounds; geometryVersion = client.Version;
                        if (captured != screen) return;
                        graphics.DrawImageUnscaled(client.Bitmap, 0, 0);
                    }
                    int generation = recognitionGeneration;
                    float scale;
                    using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false,
                        screen.Width >= 1900 ? 2200.0f : 2000.0f))
                    {
                        OcrResult result = await RecognizeAsync(prepared);
                        mapleWindows.Refresh();
                        if (shuttingDown || generation != recognitionGeneration ||
                            geometryVersion != mapleWindows.GeometryVersion) return;
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
                if (panelWasVisible && mainPanel != null && !mainPanel.IsDisposed)
                    mainPanel.Show();
            }
        }

        private void SetJournalPagingShortcuts(bool active)
        {
            bool hasHandle = false;
            try { hasHandle = IsHandleCreated; }
            catch (NullReferenceException) { hasHandle = false; }
            if (!hasHandle || Program.Benchmark || active == journalPagingShortcuts) return;
            UnregisterHotKey(Handle, HOTKEY_JOURNAL_PREVIOUS);
            UnregisterHotKey(Handle, HOTKEY_JOURNAL_NEXT);
            journalPagingShortcuts = false;
            if (active)
            {
                bool previous = RegisterShortcut(Handle, HOTKEY_JOURNAL_PREVIOUS, 1, (uint)Keys.PageUp);
                bool next = RegisterShortcut(Handle, HOTKEY_JOURNAL_NEXT, 1, (uint)Keys.PageDown);
                journalPagingShortcuts = previous || next;
                Program.LogRuntimeEvent("quest-journal-shortcuts", "previous="+previous+" next="+next);
                if (!previous || !next) SetInteractionStatus("正文已分页；Alt+PageUp / Alt+PageDown 快捷键注册失败，请关闭占用该组合的程序。");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                Program.LogRuntimeEvent("hotkey-arrived", "id=" + id);
                if (id == HOTKEY_SHOW) {
                    if(ImageTestForm.IsActiveTest)ImageTestForm.ForwardF8();
                    else RunManualTranslationFromInputAsync(false);
                }
                else if (id == HOTKEY_JOURNAL_PREVIOUS || id == HOTKEY_JOURNAL_NEXT)
                {
                    if (!processing && visibleTranslation && mapleWindows.Locked != null && GetForegroundWindow() == mapleWindows.Locked.Handle)
                        ChangeQuestJournalPage(id == HOTKEY_JOURNAL_NEXT ? 1 : -1);
                }
                else if (id == HOTKEY_HIDE) { Task ignored = AutoAlignChatRegionFromHotkeyAsync(); }
                else if (id == HOTKEY_FLOATING_WINDOW) { RestoreAiChatFloatingWindowFromHotkey(); }
            }
            else if (m.Msg == TaskbarCreatedMessage)
            {
                Program.LogRuntimeEvent("taskbar-created", "received=true");
                // Explorer recreates the taskbar without preserving NotifyIcon registrations.
                // Re-register immediately so the process can never become tray-less and hidden.
                RestoreTrayIcon(false);
            }
            base.WndProc(ref m);
        }

        internal void HideTranslation(bool preserveManualMemory = false)
        {
            Program.LogRuntimeEvent("overlay-hidden", "generation=" + recognitionGeneration +
                " manualRequest=" + manualTranslationRequestId + " processing=" + processing);
            recognitionGeneration++;
            if (!preserveManualMemory) manualMemory.Clear();
            displayedScene.Clear(); changedSceneChecks = 0; displayedSceneRevision++;
            ClearFusionAssets();
            CancelPanelAiRequest();
            HideWindowScanIndicator();
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
            tray.Text = "枫语幕 v4.0";
        }

        private Task ShowTranslationAsync()
        {
            return ShowTranslationAsync(false, 0);
        }

        private async Task ShowTranslationAsync(bool automatic)
        {
            await ShowTranslationAsync(automatic, 0);
        }

        private bool RecognitionWasCancelled(bool automatic, int requestId, int generation)
        {
            if (shuttingDown || IsDisposed || Disposing) return true;
            if (!Program.Benchmark) mapleWindows.Refresh();
            if (generation != recognitionGeneration) return true;
            return automatic ? manualTranslationPending :
                requestId != 0 && (requestId != manualTranslationRequestId ||
                    !manualTranslationPending);
        }

        private Task ShowTranslationAsync(bool automatic, int requestId)
        {
            return ShowSimpleTranslationAsync(automatic, requestId);
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
                "首次可见耗时=" + benchmarkFirstVisibleMs + Environment.NewLine +
                "首显指标类型=阶段估算；不是生产F8实际绘制或呈现时间" + Environment.NewLine +
                "最终收敛耗时=" + benchmarkConvergedMs + Environment.NewLine +
                "阶段耗时=截图:" + captureDuration + "ms, 快速探测:" + probePassDuration +
                "ms, 主OCR与匹配:" + mainPassDuration + "ms, 光标详情:" + hoverPassDuration +
                "ms, 面板复核:" + panelPassDuration + "ms, 兜底复核:" + fallbackPassDuration +
                "ms, 合并绘制:" + finalizeDuration + "ms" + Environment.NewLine +
                "识别计划=" + recognitionPlanText + Environment.NewLine +
                "识别场景=" + recognizedScenes + Environment.NewLine +
                "本地强OCR=" + benchmarkStrongOcrStatus + Environment.NewLine +
                "命中数量=" + labels.Count + Environment.NewLine + "置信度分布=高:" + benchmarkHighLabels + " 中:" + benchmarkMediumLabels + " 低:" + benchmarkLowLabels + Environment.NewLine + "可无缝标签=" + benchmarkSeamlessLabels + Environment.NewLine + "匹配来源=精确:" + benchmarkExactLabels + " 模糊:" + benchmarkApproximateLabels + " 布局:" + benchmarkLayoutLabels + Environment.NewLine + "源区域=" + benchmarkSourceBounds + Environment.NewLine +
                "图标指纹数量=" + translations.IconCount + Environment.NewLine +
                "任务数量=" + translations.TaskCount + Environment.NewLine +
                "任务文本数量=" + translations.TaskTextCount + Environment.NewLine +
                "技能说明数量=" + translations.SkillTextCount + Environment.NewLine +
                "装备物品说明数量=" + translations.ItemTextCount + Environment.NewLine +
                "覆盖标签=" + benchmarkLabels + Environment.NewLine +
                "OCR文本=" + (benchmarkOcrText ?? "").Replace("\r", " ").Replace("\n", " | ") + Environment.NewLine +
                 "任务调试=" + (benchmarkQuestDebug ?? "") + Environment.NewLine +
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

        internal Task<WindowCaptureFrame> CaptureSelectionSnapshotAsync()
        {
            return mapleWindows.CaptureClientBitmapAsync();
        }

        internal async Task<ChatCaptureFrame> CaptureChatAsync(Rectangle screen,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ChatCaptureFrame frame = new ChatCaptureFrame();
            Rectangle client;
            long version;
            using (WindowCaptureFrame captured = await mapleWindows.CaptureClientBitmapAsync(cancellationToken))
            {
                client = captured.Bounds; version = captured.Version;
                Bitmap bitmap = captured.Bitmap;
                screen = ChatRegionSettings.ResolveForGame(client);
                if (!ChatRegionSettings.IsUsable(screen) || Rectangle.Intersect(screen, client) != screen)
                    return frame;
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.UpdateChatRegion(screen, ChatRegionSettings.GetOrigin());
                Rectangle crop = new Rectangle(screen.X - client.X, screen.Y - client.Y, screen.Width, screen.Height);
                using (Bitmap chat = bitmap.Clone(crop, PixelFormat.Format32bppArgb))
                    frame = await AnalyzeChatBitmapAsync(chat, version, cancellationToken);
                mapleWindows.Refresh();
                return version == mapleWindows.GeometryVersion && !shuttingDown ? frame : new ChatCaptureFrame();
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

        private async Task<ChatCaptureFrame> AnalyzeChatBitmapAsync(Bitmap bitmap, long geometryVersion = -1,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ChatCaptureFrame frame = new ChatCaptureFrame();
            float scale;
            using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false, 1800.0f))
            {
                OcrResult result = await RecognizeAsync(prepared);
                frame.Text = result.Text ?? "";
                foreach (OcrLine line in GroupChatOcrRows(result.Lines))
                {
                    string text = (line.Text ?? "").Trim();
                    if (text.Length == 0) continue;
                    RectangleF raw = GetOcrLineBounds(line);
                    RectangleF sourceBounds = scale <= 0 ? RectangleF.Empty :
                        new RectangleF(raw.X / scale, raw.Y / scale,
                            raw.Width / scale, raw.Height / scale);
                    ChatColorSample evidence;
                    ChatVisualStyle style = SampleChatVisualStyle(bitmap, line, scale,
                        out evidence);
                    frame.Lines.Add(new ChatCaptureLine {
                        Text = text,
                        Style = style,
                        Evidence = evidence,
                        Bounds = sourceBounds
                    });
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (geometryVersion >= 0)
            {
                mapleWindows.Refresh();
                if (geometryVersion != mapleWindows.GeometryVersion) return new ChatCaptureFrame();
            }
            if (Program.DisableStrongChatOcr || !strongOcr.IsAvailable) return frame;
            string fastIdentity = ChatFrameIdentity(frame);
            if (fastIdentity.Length > 0 && fastIdentity == lastFastChatIdentity &&
                lastReviewedChatFrame != null)
            {
                // The identity is deliberately text-only so a static frame does not pay
                // for a second OCR pass. Source colour, however, is not in the text:
                // identical words under another channel band must not inherit the
                // previous frame's category. Keep the fresh per-line evidence instead.
                if (FramesShareStyles(frame, lastReviewedChatFrame)) return lastReviewedChatFrame;
                return frame;
            }

            StrongOcrResult strong = await strongOcr.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();
            if (geometryVersion >= 0)
            {
                mapleWindows.Refresh();
                if (geometryVersion != mapleWindows.GeometryVersion) return new ChatCaptureFrame();
            }
            if (!strong.Succeeded)
            {
                lastFastChatIdentity = fastIdentity;
                lastReviewedChatFrame = frame;
                return frame;
            }

            ChatCaptureFrame reviewed = BuildStrongChatFrame(bitmap, strong);
            ChatCaptureFrame selected = Program.ForceStrongChatOcr
                ? reviewed : ChooseChatFrame(frame, reviewed);
            lastFastChatIdentity = fastIdentity;
            lastReviewedChatFrame = selected;
            return selected;
        }

        private sealed class PrimaryChatRow
        {
            internal readonly List<OcrLine> Parts = new List<OcrLine>();
            internal RectangleF Anchor;
        }

        private static List<OcrLine> GroupChatOcrRows(IList<OcrLine> lines)
        {
            // Windows OCR may split a nickname, channel badge and body into separate
            // lines when their colours differ. Rejoin only measured fragments of the
            // same visual baseline, using the original word boxes and no extra OCR.
            List<OcrLine> ordered = new List<OcrLine>(lines);
            ordered.Sort(delegate(OcrLine a, OcrLine b) {
                RectangleF left = GetOcrLineBounds(a), right = GetOcrLineBounds(b);
                int vertical = left.Top.CompareTo(right.Top);
                return vertical != 0 ? vertical : left.Left.CompareTo(right.Left);
            });
            List<PrimaryChatRow> rows = new List<PrimaryChatRow>();
            foreach (OcrLine line in ordered)
            {
                RectangleF bounds = GetOcrLineBounds(line);
                PrimaryChatRow best = null;
                float bestOverlap = 0;
                if (!bounds.IsEmpty)
                    foreach (PrimaryChatRow row in rows)
                    {
                        RectangleF anchorBounds = row.Anchor;
                        if (anchorBounds.IsEmpty) continue;
                        float shorter = Math.Min(bounds.Height, anchorBounds.Height);
                        float overlap = Math.Min(bounds.Bottom, anchorBounds.Bottom) -
                            Math.Max(bounds.Top, anchorBounds.Top);
                        float centerDrift = Math.Abs((bounds.Top + bounds.Bottom) -
                            (anchorBounds.Top + anchorBounds.Bottom)) / 2.0f;
                        if (overlap < shorter * 0.68f || centerDrift > shorter * 0.45f ||
                            overlap <= bestOverlap) continue;
                        best = row; bestOverlap = overlap;
                    }
                if (best == null)
                {
                    best = new PrimaryChatRow { Anchor = bounds };
                    rows.Add(best);
                }
                best.Parts.Add(line);
            }
            List<OcrLine> grouped = new List<OcrLine>();
            foreach (PrimaryChatRow row in rows)
            {
                if (row.Parts.Count == 1) { grouped.Add(row.Parts[0]); continue; }
                row.Parts.Sort(delegate(OcrLine a, OcrLine b) {
                    return GetOcrLineBounds(a).Left.CompareTo(GetOcrLineBounds(b).Left);
                });
                OcrLine merged = new OcrLine();
                StringBuilder text = new StringBuilder();
                RectangleF previous = RectangleF.Empty;
                foreach (OcrLine part in row.Parts)
                {
                    string value = (part.Text ?? "").Trim();
                    if (value.Length == 0) continue;
                    RectangleF bounds = GetOcrLineBounds(part);
                    if (text.Length > 0 && NeedsChatPartSpace(text[text.Length - 1], value[0],
                        previous, bounds)) text.Append(' ');
                    text.Append(value);
                    merged.Words.AddRange(part.Words);
                    previous = bounds;
                }
                merged.Text = text.ToString();
                grouped.Add(merged);
            }
            return grouped;
        }

        private sealed class StrongChatRow
        {
            internal readonly List<StrongOcrLine> Parts = new List<StrongOcrLine>();
            internal RectangleF Bounds = RectangleF.Empty;
        }

        private static ChatCaptureFrame BuildStrongChatFrame(Bitmap bitmap,
            StrongOcrResult result)
        {
            ChatCaptureFrame frame = new ChatCaptureFrame();
            List<StrongChatRow> rows = new List<StrongChatRow>();
            List<StrongOcrLine> ordered = new List<StrongOcrLine>(result.Lines);
            ordered.Sort(delegate(StrongOcrLine left, StrongOcrLine right) {
                int vertical = left.Bounds.Top.CompareTo(right.Bounds.Top);
                return vertical != 0 ? vertical : left.Bounds.Left.CompareTo(right.Bounds.Left);
            });

            foreach (StrongOcrLine line in ordered)
            {
                if (String.IsNullOrWhiteSpace(line.Text) || line.Bounds.IsEmpty) continue;
                StrongChatRow best = null;
                float bestOverlap = 0.0f;
                foreach (StrongChatRow row in rows)
                {
                    float overlap = Math.Min(row.Bounds.Bottom, line.Bounds.Bottom) -
                        Math.Max(row.Bounds.Top, line.Bounds.Top);
                    // Fragments of the same rendered row overlap almost completely. Adjacent
                    // MapleStory chat rows are tightly packed and may overlap by one or two
                    // antialiased pixels, so the old 34% threshold could chain two messages.
                    float required = Math.Min(row.Bounds.Height, line.Bounds.Height) * 0.65f;
                    float centerDistance = Math.Abs((row.Bounds.Top + row.Bounds.Bottom) / 2.0f -
                        (line.Bounds.Top + line.Bounds.Bottom) / 2.0f);
                    float horizontalGap = Math.Max(0.0f, Math.Max(line.Bounds.Left - row.Bounds.Right,
                        row.Bounds.Left - line.Bounds.Right));
                    float maximumGap = Math.Max(48.0f,
                        Math.Max(row.Bounds.Height, line.Bounds.Height) * 6.0f);
                    if ((overlap >= required || centerDistance <=
                        Math.Max(row.Bounds.Height, line.Bounds.Height) * 0.22f) &&
                        horizontalGap <= maximumGap && overlap >= bestOverlap)
                    {
                        best = row; bestOverlap = Math.Max(0.0f, overlap);
                    }
                }
                if (best == null)
                {
                    best = new StrongChatRow { Bounds = line.Bounds };
                    rows.Add(best);
                }
                else best.Bounds = RectangleF.Union(best.Bounds, line.Bounds);
                best.Parts.Add(line);
            }

            rows.Sort(delegate(StrongChatRow left, StrongChatRow right) {
                return left.Bounds.Top.CompareTo(right.Bounds.Top);
            });
            StringBuilder all = new StringBuilder();
            foreach (StrongChatRow row in rows)
            {
                row.Parts.Sort(delegate(StrongOcrLine left, StrongOcrLine right) {
                    return left.Bounds.Left.CompareTo(right.Bounds.Left);
                });
                StringBuilder text = new StringBuilder();
                RectangleF previous = RectangleF.Empty;
                foreach (StrongOcrLine part in row.Parts)
                {
                    string value = (part.Text ?? "").Trim();
                    if (value.Length == 0) continue;
                    if (text.Length > 0 && NeedsChatPartSpace(text[text.Length - 1], value[0],
                        previous, part.Bounds)) text.Append(' ');
                    text.Append(value);
                    previous = part.Bounds;
                }
                string rowText = text.ToString().Trim();
                if (rowText.Length == 0) continue;
                if (all.Length > 0) all.AppendLine();
                all.Append(rowText);
                ChatColorSample evidence;
                ChatVisualStyle rowStyle = SampleChatVisualStyle(bitmap, row.Bounds, rowText,
                    null, out evidence);
                frame.Lines.Add(new ChatCaptureLine {
                    Text = rowText,
                    Bounds = row.Bounds,
                    Style = rowStyle,
                    Evidence = evidence
                });
            }
            frame.Text = all.ToString();
            return frame;
        }

        private static bool NeedsChatPartSpace(char left, char right,
            RectangleF leftBounds, RectangleF rightBounds)
        {
            if (Char.IsWhiteSpace(left) || Char.IsWhiteSpace(right)) return false;
            if (left == '[' || left == '(' || left == '/' || right == ']' || right == ')' ||
                right == ':' || right == ',' || right == '.' || right == ';') return false;
            float gap = leftBounds.IsEmpty ? 1.0f : rightBounds.Left - leftBounds.Right;
            return gap >= -1.5f;
        }

        private static ChatCaptureFrame ChooseChatFrame(ChatCaptureFrame fast,
            ChatCaptureFrame reviewed)
        {
            List<string> fastMessages = OfflineChatForm.ParseChatLines(fast.PhysicalLineText());
            List<string> reviewedMessages = OfflineChatForm.ParseChatLines(reviewed.PhysicalLineText());
            int fastParsed = fastMessages.Count;
            int reviewedParsed = reviewedMessages.Count;
            // The accuracy pass is allowed to add a genuinely missed row, but it must retain every
            // speaker/system identity already recovered by the fast pass. This prevents a sharper
            // yet semantically wrong OCR result from replacing Arrowshot with an unrelated name.
            if (reviewedParsed > fastParsed)
                return fastParsed == 0 || PreservesParsedSpeakers(fastMessages, reviewedMessages)
                    ? reviewed : fast;
            if (reviewedParsed < fastParsed || reviewedParsed == 0) return fast;
            // On equal message counts, a changed speaker is not an accuracy improvement. Keep the
            // stable fast result and use Paddle only when it is refining the same messages.
            if (!SameParsedSpeakers(fastMessages, reviewedMessages)) return fast;
            int fastQuality = ChatFrameTextQuality(fast);
            int reviewedQuality = ChatFrameTextQuality(reviewed);
            return reviewedQuality >= fastQuality ? reviewed : fast;
        }

        private static bool PreservesParsedSpeakers(List<string> baseline,
            List<string> candidate)
        {
            HashSet<string> candidateSpeakers = ParsedSpeakerSet(candidate);
            foreach (string speaker in ParsedSpeakerSet(baseline))
            {
                bool found = false;
                foreach (string other in candidateSpeakers)
                    if (OfflineChatForm.IsSameChatSpeakerOcr(speaker, other))
                    { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        private static bool SameParsedSpeakers(List<string> left, List<string> right)
        {
            HashSet<string> leftSpeakers = ParsedSpeakerSet(left);
            HashSet<string> rightSpeakers = ParsedSpeakerSet(right);
            return leftSpeakers.Count == rightSpeakers.Count &&
                PreservesParsedSpeakers(left, right) && PreservesParsedSpeakers(right, left);
        }

        private static HashSet<string> ParsedSpeakerSet(List<string> messages)
        {
            HashSet<string> speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (messages == null) return speakers;
            foreach (string message in messages)
            {
                int separator = (message ?? "").IndexOf(':');
                string speaker = separator > 0 ? message.Substring(0, separator).Trim() : "";
                if (speaker.Length > 0) speakers.Add(speaker);
            }
            return speakers;
        }

        private static int ChatFrameTextQuality(ChatCaptureFrame frame)
        {
            int score = 0;
            foreach (ChatCaptureLine line in frame.Lines)
            {
                string text = line.Text ?? "";
                foreach (char item in text)
                {
                    if (Char.IsLetterOrDigit(item)) score += 3;
                    else if (Char.IsWhiteSpace(item) || ":[]()'.,/+-><".IndexOf(item) >= 0) score++;
                    else if (item == '|' || item == '�') score -= 5;
                }
                if (ChatVisualStylePolicy.LooksLikePlayerChatLine(text)) score += 40;
            }
            return Math.Max(0, score);
        }

        private static bool FramesShareStyles(ChatCaptureFrame left, ChatCaptureFrame right)
        {
            if (left == null || right == null || left.Lines.Count != right.Lines.Count) return false;
            for (int index = 0; index < left.Lines.Count; index++)
            {
                ChatVisualStyle a = left.Lines[index].Style;
                ChatVisualStyle b = right.Lines[index].Style;
                if (a == null || b == null) return false;
                if (a.Category != b.Category || a.Kind != b.Kind ||
                    a.HasBackground != b.HasBackground) return false;
            }
            return true;
        }

        private static string ChatFrameIdentity(ChatCaptureFrame frame)
        {
            StringBuilder value = new StringBuilder();
            foreach (char item in (frame == null ? "" : frame.PhysicalLineText()).ToLowerInvariant())
                if (Char.IsLetterOrDigit(item)) value.Append(item);
            return value.ToString();
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

        private static ChatVisualStyle SampleChatVisualStyle(Bitmap bitmap, OcrLine line, float scale,
            out ChatColorSample evidence)
        {
            evidence = new ChatColorSample();
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty || scale <= 0)
                return ChatVisualStylePolicy.FromSample(line.Text, Color.Empty, Color.Empty, false);
            List<RectangleF> ink=new List<RectangleF>();
            foreach(OcrWord word in line.Words){RectangleF r=word.BoundingRect;ink.Add(new RectangleF(r.X/scale,r.Y/scale,r.Width/scale,r.Height/scale));}
            return SampleChatVisualStyle(bitmap, new RectangleF(raw.X / scale, raw.Y / scale,
                raw.Width / scale, raw.Height / scale), line.Text,ink, out evidence);
        }

        private static ChatVisualStyle SampleChatVisualStyle(Bitmap bitmap,
            RectangleF sourceBounds, string lineText, IList<RectangleF> inkWords,
            out ChatColorSample evidence)
        {
            evidence = new ChatColorSample();
            if (sourceBounds.IsEmpty)
                return ChatVisualStylePolicy.FromSample(lineText, Color.Empty, Color.Empty, false);
            Rectangle text = Rectangle.FromLTRB((int)Math.Floor(sourceBounds.Left),
                (int)Math.Floor(sourceBounds.Top), (int)Math.Ceiling(sourceBounds.Right),
                (int)Math.Ceiling(sourceBounds.Bottom));
            text = Rectangle.Intersect(text, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            if (text.Width < 2 || text.Height < 2)
                return ChatVisualStylePolicy.FromSample(lineText, Color.Empty, Color.Empty, false);

            ChatColorSample sample = ChatColorEvidence.Analyze(ReadSimplePixels(bitmap), bitmap.Width,
                bitmap.Height, text, inkWords);
            evidence = sample;
            return ChatVisualStylePolicy.FromSample(lineText, sample.Foreground,
                sample.Background, sample.IsBroadcastBand);
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

        private static bool IsClassicTooltipPixel(int alpha, int red, int green, int blue)
        {
            // Classic tooltips are a translucent navy/purple rectangle. Detecting the
            // surface itself is much more reliable than assuming a fixed tooltip size or
            // position around the cursor, and excludes bright sky/snow backgrounds.
            return alpha > 180 && red >= 18 && red <= 122 &&
                green >= 18 && green <= 138 && blue >= 64 && blue <= 184 &&
                blue >= red + 18 && blue >= green + 8;
        }

private Rectangle FindCursorPanelCrop(
    Bitmap bitmap,
    System.Drawing.Point pointer,
    Rectangle screen,
    CharacterStatVisualLayout stats,
    out bool tooltip)
{
    List<Rectangle> panels = new List<Rectangle>();
    List<Rectangle> tips = new List<Rectangle>();

    foreach (VisualPanelRegion region in
        ClassicSceneVision.FindForegroundPanels(bitmap))
    {
        if (region.Kind == VisualPanelSurfaceKind.DarkBlue)
            tips.Add(region.Bounds);
        else
            panels.Add(region.Bounds);
    }

    if (stats != null) panels.Add(stats.Crop);

    Rectangle classic =
        FindClassicTooltipCrop(bitmap, pointer, screen);

    if (!classic.IsEmpty) tips.Add(classic);

    Rectangle chat = GetChatExclusionBounds();
    chat.Offset(-screen.Left, -screen.Top);

    Rectangle selected = PanelFocusPriority.Select(
        new System.Drawing.Point(
            pointer.X - screen.Left,
            pointer.Y - screen.Top),
        bitmap.Size,
        panels,
        tips,
        chat);

    tooltip = tips.Contains(selected);

    Program.LogRuntimeEvent(
        "cursor-panel",
        "generation=" + recognitionGeneration +
        " selected=" + (!selected.IsEmpty) +
        " tooltip=" + tooltip +
        " x=" + selected.X +
        " y=" + selected.Y +
        " width=" + selected.Width +
        " height=" + selected.Height);

    return selected;
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
            BitmapData data = null;
            try
            {
                data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] scanline = new byte[bitmap.Width * 4];
                for (int y = 0; y < rows; y++)
                {
                    int sampleY = Math.Min(bitmap.Height - 1, y * step + step / 2);
                    Marshal.Copy(IntPtr.Add(data.Scan0, sampleY * data.Stride),
                        scanline, 0, scanline.Length);
                    for (int x = 0; x < columns; x++)
                    {
                        int sampleX = Math.Min(bitmap.Width - 1, x * step + step / 2);
                        int offset = sampleX * 4;
                        mask[y * columns + x] = IsClassicTooltipPixel(
                            scanline[offset + 3], scanline[offset + 2],
                            scanline[offset + 1], scanline[offset]);
                    }
                }
            }
            finally
            {
                if (data != null) bitmap.UnlockBits(data);
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
                    if (!CanMergeSemanticLabels(existing, candidate)) continue;
                    RectangleF intersection = RectangleF.Intersect(existing.Bounds, candidate.Bounds);
                    float smallerArea = Math.Min(existing.Bounds.Width * existing.Bounds.Height,
                        candidate.Bounds.Width * candidate.Bounds.Height);
                    bool overlapDuplicate = smallerArea > 0 &&
                        intersection.Width * intersection.Height / smallerArea >= 0.45f;
                    bool sameText = SameOverlayText(existing.Text, candidate.Text);
                    if (overlapDuplicate ||
                        (sameText &&
                         Math.Abs(existing.Bounds.Y - candidate.Bounds.Y) < 20))
                    {
                        duplicate = true;
                        // A visually verified fixed row must not be replaced by a broad OCR
                        // rectangle from a neighbouring row. This is especially important for
                        // the compact player menu, where one noisy "REQUEST" box can overlap
                        // two adjacent 24px buttons.
                        if (overlapDuplicate && !sameText && existing.StableLayout)
                            break;
                        if (overlapDuplicate && !sameText && candidate.StableLayout)
                        {
                            primary[i] = candidate;
                            break;
                        }
                        // The discovery pass may know only a static field prefix while a focused
                        // pass recovers its live value. Preserve the latter even when the prefix
                        // came from an exact glossary hit; exactness must not discard runtime data.
                        bool candidateHasLiveExpiry = candidate.Text.StartsWith("可使用至：",
                            StringComparison.Ordinal) && candidate.Text.StartsWith(existing.Text,
                            StringComparison.Ordinal) && candidate.Text.Length > existing.Text.Length;
                        bool existingHasLiveExpiry = existing.Text.StartsWith("可使用至：",
                            StringComparison.Ordinal) && existing.Text.StartsWith(candidate.Text,
                            StringComparison.Ordinal) && existing.Text.Length > candidate.Text.Length;
                        if (candidateHasLiveExpiry)
                        {
                            MergeLabelInto(candidate, existing);
                            primary[i] = candidate;
                            break;
                        }
                        if (existingHasLiveExpiry)
                        {
                            MergeLabelInto(existing, candidate);
                            break;
                        }
                        // v4.0 merge rule. The old rule replaced a label whenever the new
                        // translation was four characters longer, which let a noisy pass
                        // overwrite a correct first paint. Confidence decides now; the
                        // established "focused pass refines coarse pass" behaviour is preserved
                        // because a focused pass scores its labels higher.
                        //
                        // Whenever the candidate replaces the existing object, the existing
                        // geometry is merged into it first: a later pass may only have read part of
                        // the rows, and dropping the earlier rectangles would shrink both the erase
                        // scope and the measured source union.
                        if ((int)candidate.Confidence > (int)existing.Confidence)
                        {
                            MergeLabelInto(candidate, existing);
                            primary[i] = candidate;
                            break;
                        }
                        if ((int)candidate.Confidence < (int)existing.Confidence)
                        {
                            // A low-confidence result must not replace a correct first paint.
                            // It can still contribute row/source geometry when both passes produced
                            // the same translation for the same overlapping source text.
                            if (sameText && overlapDuplicate)
                                MergeLabelInto(existing, candidate);
                            break;
                        }
                        bool candidateExact = candidate.MatchSource == "exact";
                        bool existingExact = existing.MatchSource == "exact";
                        if (candidateExact && !existingExact)
                        {
                            MergeLabelInto(candidate, existing);
                            primary[i] = candidate;
                            break;
                        }
                        if (!candidateExact && existingExact)
                        {
                            if (sameText && overlapDuplicate)
                                MergeLabelInto(existing, candidate);
                            break;
                        }
                        bool candidateTruncated = LabelConfidencePolicy.IsTruncated(candidate.Text);
                        bool existingTruncated = LabelConfidencePolicy.IsTruncated(existing.Text);
                        if (existingTruncated && !candidateTruncated)
                        {
                            MergeLabelInto(candidate, existing);
                            primary[i] = candidate;
                            break;
                        }
                        if (!existingTruncated && candidateTruncated)
                        {
                            if (sameText && overlapDuplicate)
                                MergeLabelInto(existing, candidate);
                            break;
                        }
                        if (!String.Equals(existing.Text, candidate.Text, StringComparison.Ordinal) &&
                            candidate.Text.Length >= existing.Text.Length + 4)
                        {
                            MergeLabelInto(candidate, existing);
                            primary[i] = candidate;
                        }
                        else if (sameText && overlapDuplicate)
                        {
                            // Equal evidence keeps the established object but must not discard the
                            // other pass's source rows or union. Paint safety depends on both.
                            MergeLabelInto(existing, candidate);
                        }
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

        internal bool WindowsOcrAvailable {get{return ocr!=null;}}
        private async Task<OcrResult> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool windows=primaryOcr.UseWindows;
            if(windows&&ocr==null)throw new InvalidOperationException("系统英语 OCR 不可用。");
            Stopwatch clock=Stopwatch.StartNew();
            OcrResult result=windows?await RecognizeWindowsAsync(bitmap):await primaryOcr.RecognizeAsync(bitmap,cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Program.LogRuntimeEvent("ocr-pass","engine="+(windows?"windows":"paddle-"+primaryOcr.Tier)+
                " lines="+result.Lines.Count+" elapsedMs="+clock.ElapsedMilliseconds);
            return result;
        }

        private async Task<OcrResult> RecognizeWindowsAsync(Bitmap bitmap)
        {
            // Prepared OCR frames are already 32-bit BGRA. Copying those pixels directly
            // avoids PNG compression, byte-array duplication and a decoder pass on every
            // recognition. Keep the encoded path as a compatibility fallback and as an A/B
            // switch for regression testing.
            if (!Program.ForceLegacyOcrBridge && directOcrBitmapBridgeAvailable)
            {
                SoftwareBitmap directBitmap = null;
                try
                {
                    directBitmap = CreateDirectSoftwareBitmap(bitmap);
                }
                catch (Exception)
                {
                    directOcrBitmapBridgeAvailable = false;
                }
                if (directBitmap != null)
                    using (directBitmap)
                        return OcrWindowsAdapter.FromWindows(await ToTask<Windows.Media.Ocr.OcrResult>(ocr.RecognizeAsync(directBitmap)));
            }
            return await RecognizeWithEncodedBitmapAsync(bitmap);
        }

        private static SoftwareBitmap CreateDirectSoftwareBitmap(Bitmap bitmap)
        {
            if (bitmap == null) throw new ArgumentNullException("bitmap");
            int rowBytes = checked(bitmap.Width * 4);
            int byteCount = checked(rowBytes * bitmap.Height);
            BitmapData data = null;
            try
            {
                data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Windows.Storage.Streams.Buffer buffer =
                    new Windows.Storage.Streams.Buffer((uint)byteCount);
                buffer.Length = (uint)byteCount;
                IntPtr destination;
                ((IBufferByteAccess)(object)buffer).Buffer(out destination);
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr sourceRow = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(sourceRow, row, 0, rowBytes);
                    Marshal.Copy(row, 0, IntPtr.Add(destination, y * rowBytes), rowBytes);
                }
                return SoftwareBitmap.CreateCopyFromBuffer(buffer,
                    BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height,
                    BitmapAlphaMode.Ignore);
            }
            finally
            {
                if (data != null) bitmap.UnlockBits(data);
            }
        }

        private async Task<OcrResult> RecognizeWithEncodedBitmapAsync(Bitmap bitmap)
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
                    using (software) { return OcrWindowsAdapter.FromWindows(await ToTask<Windows.Media.Ocr.OcrResult>(ocr.RecognizeAsync(software))); }
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
            return FindPanelCandidates(null, result, ocrScale, screen, includeDialogueAnchors);
        }

        private List<PanelCropCandidate> FindPanelCandidates(Bitmap bitmap, OcrResult result,
            float ocrScale, Rectangle screen, bool includeDialogueAnchors)
        {
            List<PanelCropCandidate> candidates = new List<PanelCropCandidate>();
            bool dominantKeyBindings = SettingsPanelPolicy.IsKeyBindingsWindow(result.Text);
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
                bool itemDetail = evidence.ItemDetail || LooksLikeEquipmentFieldText(line.Text);
                bool item = (evidence.Kinds & (SceneKind.Item | SceneKind.Shop | SceneKind.Inventory)) != 0 ||
                    itemDetail;
                bool dialogue = (evidence.Kinds & SceneKind.Dialogue) != 0;
                bool mainInterface = evidence.MainPanelHeader ||
                    (evidence.Kinds & SceneKind.Interface) != 0;
                if (dominantKeyBindings && !mainInterface) continue;
                if (!character && !skill && !quest && !item && !dialogue && !mainInterface) continue;
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
                    priorityKind = RecognitionPriorityKind.Dialogue;
                    kind = "character";
                }
                else if (skill)
                {
                    if (!skillHeader) { left = sourceLeft - 210; top = sourceTop - 180; }
                    width = Math.Max(950, screen.Width * 48 / 100);
                    height = Math.Max(700, screen.Height * 72 / 100);
                    priorityKind = skillDetail && !skillHeader
                        ? RecognitionPriorityKind.Detail : RecognitionPriorityKind.Dialogue;
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
                        ? RecognitionPriorityKind.Detail : RecognitionPriorityKind.Dialogue;
                    kind = "item";
                }
                else if (mainInterface)
                {
                    // World map, crafting journal and buddy/user-list windows are foreground
                    // panels too. They must rise with Character/Quest/Inventory instead of
                    // being treated as fourth-level background UI.
                    width = Math.Max(900, screen.Width * 52 / 100);
                    height = Math.Max(620, screen.Height * 68 / 100);
                    priorityKind = RecognitionPriorityKind.Dialogue;
                    kind = "interface";
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
                    (mainInterface ? SceneClassifier.EvidenceScore(evidence, SceneKind.Interface) :
                    (dialogue ? 100 : (LooksLikeEquipmentFieldText(line.Text) ? 82 : 60)))))))));
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

            // The full screenshot is the source of truth. OCR anchors remain useful, but a
            // missed title must not make an otherwise obvious white quest/dialogue window or
            // dark-blue item/skill card disappear. The visual pass is deliberately conservative:
            // it accepts only large, dense rectangular surfaces away from screen edges; tiny
            // speech bubbles, wooden player adverts, HUD bars and irregular spell effects do not
            // satisfy the geometry contract. Any text already seen inside the region raises its
            // score and provides the glyph size for the focused reread.
            if (bitmap != null)
            {
                foreach (VisualPanelRegion visual in ClassicSceneVision.FindForegroundPanels(bitmap))
                {
                    int lineCount = 0, textCharacters = 0;
                    float totalTextHeight = 0.0f;
                    foreach (OcrLine line in result.Lines)
                    {
                        RectangleF raw = GetOcrLineBounds(line);
                        PointF center = new PointF(raw.Left / ocrScale, raw.Top / ocrScale);
                        center.X += raw.Width / Math.Max(0.01f, ocrScale) / 2.0f;
                        center.Y += raw.Height / Math.Max(0.01f, ocrScale) / 2.0f;
                        if (!visual.Bounds.Contains((int)center.X, (int)center.Y)) continue;
                        string normalized = TranslationStore.Normalize(line.Text);
                        if (normalized.Length == 0 || SceneClassifier.LooksLikePlayerChat(line.Text))
                            continue;
                        lineCount++;
                        textCharacters += normalized.Length;
                        totalTextHeight += raw.Height / Math.Max(0.01f, ocrScale);
                    }
                    long area = (long)visual.Bounds.Width * visual.Bounds.Height;
                    bool visuallyCertain = visual.Score >= 168 &&
                        area >= (long)screen.Width * screen.Height / 30;
                    int minimumLines = visual.Kind == VisualPanelSurfaceKind.DarkBlue ? 1 : 2;
                    if (lineCount < minimumLines && !visuallyCertain) continue;
                    Rectangle crop = Rectangle.Intersect(screen, new Rectangle(
                        screen.Left + visual.Bounds.Left, screen.Top + visual.Bounds.Top,
                        visual.Bounds.Width, visual.Bounds.Height));
                    if (crop.Width < 180 || crop.Height < 80) continue;
                    int score = visual.Score + Math.Min(42, lineCount * 6 + textCharacters / 12);
                    candidates.Add(new PanelCropCandidate {
                        Bounds = crop,
                        Score = score,
                        Kind = visual.Kind == VisualPanelSurfaceKind.DarkBlue
                            ? "visual-tooltip" : "visual-light",
                        PriorityKind = visual.Kind == VisualPanelSurfaceKind.DarkBlue
                            ? RecognitionPriorityKind.Detail : RecognitionPriorityKind.Dialogue,
                        SourceTextHeight = lineCount > 0
                            ? Math.Max(7.0f, totalTextHeight / lineCount) : 10.0f
                    });
                }
            }

            // Quest bodies are commonly split into 5-10 OCR lines. A per-line planner cannot
            // see enough words to recover the task id, so it never schedules the focused pass
            // even though the full OCR text is correct. Join only spatial neighbours and add a
            // high-priority crop once a unique dictionary-backed body identifies the task.
            for (int start = 0; start < result.Lines.Count; start++)
            {
                OcrLine firstLine = result.Lines[start];
                if (SceneClassifier.LooksLikePlayerChat(firstLine.Text)) continue;
                StringBuilder combined = new StringBuilder();
                RectangleF combinedBounds = RectangleF.Empty;
                int bestSpan = 0;
                RectangleF bestBounds = RectangleF.Empty;
                for (int span = 0; span < 12 && start + span < result.Lines.Count; span++)
                {
                    OcrLine candidateLine = result.Lines[start + span];
                    if (span > 0 && !CanJoinOcrLines(result.Lines[start + span - 1],
                        candidateLine)) break;
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(candidateLine.Text);
                    RectangleF lineBounds = GetOcrLineBounds(candidateLine);
                    combinedBounds = combinedBounds.IsEmpty ? lineBounds :
                        RectangleF.Union(combinedBounds, lineBounds);
                    if (span < 1 || translations.DetectTaskId(combined.ToString()).Length == 0)
                        continue;
                    bestSpan = span + 1;
                    bestBounds = combinedBounds;
                }
                if (bestSpan == 0) continue;
                int sourceLeft = screen.Left + (int)(bestBounds.Left / ocrScale);
                int sourceTop = screen.Top + (int)(bestBounds.Top / ocrScale);
                Rectangle crop = Rectangle.Intersect(screen, new Rectangle(
                    sourceLeft - 160, sourceTop - 220,
                    Math.Max(900, screen.Width * 48 / 100),
                    Math.Max(680, screen.Height * 68 / 100)));
                if (crop.Width >= 300 && crop.Height >= 250)
                    candidates.Add(new PanelCropCandidate { Bounds = crop, Score = 168,
                        Kind = "quest", PriorityKind = RecognitionPriorityKind.Dialogue,
                        SourceTextHeight = Math.Max(5.0f,
                            bestBounds.Height / Math.Max(1, bestSpan) /
                            Math.Max(0.01f, ocrScale)) });
                start += bestSpan - 1;
            }

            // OCR normally splits the body of an NPC dialogue into 3-6 visual lines. The
            // scene snapshot can recover it from OcrResult.Text, but a line-only crop planner
            // then has no dialogue target and lets the small quest helper win. Join only
            // spatially adjacent lines and require a long dictionary-backed dialogue match;
            // this keeps arbitrary player chat and announcements out of the foreground plan.
            for (int start = 0; start < result.Lines.Count; start++)
            {
                OcrLine firstLine = result.Lines[start];
                if (SceneClassifier.LooksLikePlayerChat(firstLine.Text)) continue;
                StringBuilder combined = new StringBuilder();
                RectangleF combinedBounds = RectangleF.Empty;
                int bestSpan = 0, bestLength = 0;
                RectangleF bestBounds = RectangleF.Empty;
                for (int span = 0; span < 8 && start + span < result.Lines.Count; span++)
                {
                    OcrLine candidateLine = result.Lines[start + span];
                    if (span > 0 && !CanJoinOcrLines(result.Lines[start + span - 1],
                        candidateLine)) break;
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(candidateLine.Text);
                    RectangleF lineBounds = GetOcrLineBounds(candidateLine);
                    combinedBounds = combinedBounds.IsEmpty ? lineBounds :
                        RectangleF.Union(combinedBounds, lineBounds);
                    if (span < 1) continue;
                    List<MatchResult> dialogueMatches =
                        translations.FindDialogueTextMatches(combined.ToString());
                    int longest = 0;
                    foreach (MatchResult match in dialogueMatches)
                        longest = Math.Max(longest, match.Entry.Normalized.Length);
                    if (longest < 22 || (longest == bestLength && span + 1 <= bestSpan)) continue;
                    bestLength = longest; bestSpan = span + 1; bestBounds = combinedBounds;
                }
                if (bestSpan == 0) continue;
                int sourceLeft = screen.Left + (int)(bestBounds.Left / ocrScale);
                int sourceTop = screen.Top + (int)(bestBounds.Top / ocrScale);
                Rectangle crop = Rectangle.Intersect(screen, new Rectangle(
                    sourceLeft - 180, sourceTop - 240,
                    Math.Max(900, screen.Width * 48 / 100),
                    Math.Max(620, screen.Height * 62 / 100)));
                if (crop.Width >= 300 && crop.Height >= 250)
                    candidates.Add(new PanelCropCandidate { Bounds = crop, Score = 154,
                        Kind = "dialogue", PriorityKind = RecognitionPriorityKind.Dialogue,
                        SourceTextHeight = Math.Max(5.0f,
                            bestBounds.Height / Math.Max(1, bestSpan) / Math.Max(0.01f, ocrScale)) });
                start += bestSpan - 1;
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

        private List<Rectangle> FindPanelCrops(Bitmap bitmap, OcrResult result, float ocrScale,
            Rectangle screen)
        {
            // Preserve the semantic-only safety path for callers without source pixels.
            if (bitmap == null) return FindPanelCrops(result, ocrScale, screen);
            List<PanelCropCandidate> candidates = FindPanelCandidates(bitmap, result, ocrScale,
                screen, false);
            List<PanelCropCandidate> accepted = new List<PanelCropCandidate>();
            HashSet<string> acceptedKinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelCropCandidate candidate in candidates)
            {
                // Visual candidates of one colour may represent several independent open panels,
                // so unlike semantic headers they are de-duplicated by overlap, not by kind alone.
                bool visual = candidate.Kind == "visual-light" || candidate.Kind == "visual-tooltip";
                if (!visual && acceptedKinds.Contains(candidate.Kind)) continue;
                bool duplicate = false;
                foreach (PanelCropCandidate existing in accepted)
                {
                    Rectangle overlap = Rectangle.Intersect(existing.Bounds, candidate.Bounds);
                    long smaller = Math.Min((long)existing.Bounds.Width * existing.Bounds.Height,
                        (long)candidate.Bounds.Width * candidate.Bounds.Height);
                    if (smaller > 0 && (long)overlap.Width * overlap.Height * 10 >= smaller * 7)
                    { duplicate = true; break; }
                }
                if (!duplicate)
                {
                    accepted.Add(candidate);
                    if (!visual) acceptedKinds.Add(candidate.Kind);
                }
                if (accepted.Count >= 3) break;
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
                int acceptedForTier = 0;
                int tierLimit = tier.Kind == RecognitionPriorityKind.Dialogue ? 2 :
                    (tier.Kind == RecognitionPriorityKind.Detail ? 2 : 1);
                foreach (PanelCropCandidate candidate in candidates)
                {
                    if (candidate.PriorityKind != tier.Kind) continue;
                    bool duplicate = false;
                    foreach (PanelCropCandidate existing in accepted)
                    {
                        if (existing.PriorityKind != candidate.PriorityKind) continue;
                        Rectangle overlap = Rectangle.Intersect(existing.Bounds, candidate.Bounds);
                        long smaller = Math.Min((long)existing.Bounds.Width * existing.Bounds.Height,
                            (long)candidate.Bounds.Width * candidate.Bounds.Height);
                        // Semantic and colour detectors often describe the same open panel with
                        // different kinds. De-duplicate across those kinds; otherwise one quest
                        // window can consume two or three sequential OCR passes.
                        if (smaller > 0 && (long)overlap.Width * overlap.Height * 10 >= smaller * 5)
                        { duplicate = true; break; }

                        // A classic Quest window has adjacent list/detail surfaces, while item
                        // comparisons and translucent tooltips can be split into several colour
                        // components by icons. Read nearby components as one union crop. This is
                        // both more complete and much faster than invoking OCR once per half.
                        if (IsVisualPanelKind(existing.Kind) &&
                            String.Equals(existing.Kind, candidate.Kind, StringComparison.Ordinal) &&
                            AreNearbyPanelPieces(existing.Bounds, candidate.Bounds))
                        {
                            existing.Bounds = Rectangle.Union(existing.Bounds, candidate.Bounds);
                            existing.Score = Math.Max(existing.Score, candidate.Score);
                            existing.SourceTextHeight = Math.Min(existing.SourceTextHeight,
                                candidate.SourceTextHeight);
                            duplicate = true;
                            break;
                        }
                    }
                    if (duplicate) continue;
                    // Balanced translation intentionally performs at most two focused reads.
                    // Adjacent panels have already been coalesced above; a third pass is where
                    // the QQ/WeChat-like two-second interaction used to become a 3-4 second wait.
                    if (accepted.Count >= 2) continue;
                    PanelCropCandidate scheduled = new PanelCropCandidate {
                        Bounds = candidate.Bounds,
                        Score = candidate.Score,
                        Kind = candidate.Kind,
                        PriorityKind = candidate.PriorityKind,
                        SourceTextHeight = candidate.SourceTextHeight,
                        ResourceLevel = tier.ResourceLevel
                    };
                    accepted.Add(scheduled);
                    if (++acceptedForTier >= tierLimit) break;
                }
            }
            return accepted;
        }

        private static bool IsVisualPanelKind(string kind)
        {
            return String.Equals(kind, "visual-light", StringComparison.Ordinal) ||
                String.Equals(kind, "visual-tooltip", StringComparison.Ordinal);
        }

        private static bool AreNearbyPanelPieces(Rectangle first, Rectangle second)
        {
            int horizontalGap = Math.Max(0, Math.Max(first.Left, second.Left) -
                Math.Min(first.Right, second.Right));
            int verticalGap = Math.Max(0, Math.Max(first.Top, second.Top) -
                Math.Min(first.Bottom, second.Bottom));
            int horizontalOverlap = Math.Max(0, Math.Min(first.Right, second.Right) -
                Math.Max(first.Left, second.Left));
            int verticalOverlap = Math.Max(0, Math.Min(first.Bottom, second.Bottom) -
                Math.Max(first.Top, second.Top));
            int narrowWidth = Math.Max(1, Math.Min(first.Width, second.Width));
            int shortHeight = Math.Max(1, Math.Min(first.Height, second.Height));
            bool sideBySide = horizontalGap <= 48 && verticalOverlap * 5 >= shortHeight;
            bool stacked = verticalGap <= 40 && horizontalOverlap * 5 >= narrowWidth;
            return sideBySide || stacked;
        }

        private static bool ShouldRunStrongPanelOcr(PanelCropCandidate target)
        {
            if (target == null || String.IsNullOrEmpty(target.Kind)) return false;
            // The helper is an accuracy pass for text-dense focused content, not a second scan of
            // the whole interface. Skill lists, character sheets, generic interface windows and
            // the compact quest helper are already handled by the fast Windows pass; sending their
            // broad crops through Paddle adds 1-2 seconds and can introduce unrelated HUD text.
            if (target.Kind == "quest")
                return target.PriorityKind == RecognitionPriorityKind.Dialogue;
            // Windows OCR is already very reliable on large light quest/dialogue/options
            // surfaces. Starting the local Paddle helper there added about 1.3 seconds on a
            // real quest frame without yielding another translated label. Keep it for the
            // genuinely difficult dark translucent tooltip surface instead.
            if (target.Kind == "visual-light") return false;
            if (target.Kind == "dialogue" || target.Kind == "item" ||
                target.Kind == "visual-tooltip") return true;
            if (target.Kind == "skill")
                return target.PriorityKind == RecognitionPriorityKind.Detail;
            return false;
        }

        private static bool NeedsStrongTooltipReview(string sourceText,
            List<OverlayLabel> fastLabels)
        {
            string normalized = TranslationStore.Normalize(sourceText ?? "");
            if (normalized.Length < 12)
            {
                if (fastLabels != null) foreach (OverlayLabel label in fastLabels)
                    if (label != null && label.SemanticRole == "skill-name") return true;
                return fastLabels == null || fastLabels.Count == 0;
            }
            if (fastLabels == null || fastLabels.Count == 0) return true;
            bool skill = normalized.Contains("master level") || normalized.Contains("current level") ||
                normalized.Contains("next level") || normalized.Contains("required skill");
            bool name = false, body = false, currentBody = false, nextBody = false;
            foreach (OverlayLabel label in fastLabels)
            {
                if (label == null) continue;
                if (label.SemanticRole == "skill-name") { name = true; skill = true; }
                if (label.SemanticRole == "skill-body") body = true;
                if (label.SemanticRole == "skill-current-body") currentBody = true;
                if (label.SemanticRole == "skill-next-body") nextBody = true;
            }
            if (skill)
            {
                int current = normalized.IndexOf("current level", StringComparison.Ordinal);
                int next = normalized.IndexOf("next level", StringComparison.Ordinal);
                string intro = current >= 0 ? normalized.Substring(0, current) :
                    (next >= 0 ? normalized.Substring(0, next) : normalized);
                bool expectedIntro = intro.Length >= 70;
                bool expectedCurrent = current >= 0 && (next < 0 ? normalized.Length - current : next - current) >= 45;
                bool expectedNext = next >= 0 && normalized.Length - next >= 45;
                return !name || (expectedIntro && !body) ||
                    (expectedCurrent && !currentBody) || (expectedNext && !nextBody);
            }
            // Four or more independent fields are already a well-resolved structured tooltip;
            // prose length in the OCR dump can include nearby HUD/chat rows and must not force a
            // second engine over an otherwise complete equipment card.
            if (fastLabels.Count >= 4) return false;
            bool hasLongTranslation = false;
            int translatedCharacters = 0;
            foreach (OverlayLabel label in fastLabels)
            {
                if (label == null || String.IsNullOrWhiteSpace(label.Text)) continue;
                translatedCharacters += label.Text.Trim().Length;
                if (label.Text.Length >= OverlayPaintSafety.LongProseLength)
                    hasLongTranslation = true;
            }
            // Dialogue panes commonly contain two separate sentences. Neither sentence has to
            // exceed the prose threshold by itself; together they still prove the focused pass
            // has translated the complete exchange.
            if (fastLabels.Count >= 2 && translatedCharacters >= 30) return false;
            if (normalized.Length >= 80 && !hasLongTranslation) return true;
            return !hasLongTranslation && fastLabels.Count < 3 && normalized.Length >= 36;
        }

        private static List<OverlayLabel> LabelsInsideArea(List<OverlayLabel> labels,
            RectangleF area)
        {
            List<OverlayLabel> result = new List<OverlayLabel>();
            if (labels == null || area.IsEmpty) return result;
            foreach (OverlayLabel label in labels)
            {
                if (label == null || label.Bounds.IsEmpty) continue;
                PointF center = new PointF(label.Bounds.Left + label.Bounds.Width / 2.0f,
                    label.Bounds.Top + label.Bounds.Height / 2.0f);
                if (area.Contains(center)) result.Add(label);
            }
            return result;
        }

        private void RetainLabelsInPriorityRegions(List<OverlayLabel> currentLabels,
            List<PanelCropCandidate> priorityRegions)
        {
            if (currentLabels == null || priorityRegions == null || priorityRegions.Count == 0) return;
            int debugBefore = currentLabels.Count, debugRemoved = 0;
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
                if (!inside)
                {
                    if (Program.Benchmark) benchmarkQuestDebug += " [retain-drop=" + label.Text +
                        ",x=" + label.Bounds.Left.ToString("0") + ",y=" + label.Bounds.Top.ToString("0") + "]";
                    currentLabels.RemoveAt(i); debugRemoved++;
                }
            }
            if (Program.Benchmark) benchmarkQuestDebug += " [retain=" + debugBefore + "->" +
                currentLabels.Count + ",removed=" + debugRemoved + ",regions=" + priorityRegions.Count + "]";
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
                // A projected caption is a discovery hint, not proof of a visible
                // translation. Only an asset that actually painted can cover a panel.
                if (!label.HasSourceBounds || !label.PaintedLastPass || !simpleReady.Contains(label)) continue;
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

        private static int TooltipOwnerOf(RectangleF bounds, List<RectangleF> owners)
        {
            if (owners == null || bounds.IsEmpty) return -1;
            int best = -1; float bestArea = Single.MaxValue;
            for (int i = 0; i < owners.Count; i++)
            {
                RectangleF owner = owners[i];
                if (!owner.Contains(new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2))) continue;
                RectangleF overlap = RectangleF.Intersect(owner, bounds);
                if (overlap.Width * overlap.Height < bounds.Width * bounds.Height * 0.85f) continue;
                float area = owner.Width * owner.Height;
                if (area < bestArea) { best = i; bestArea = area; }
            }
            return best;
        }

        private static List<RectangleF> FindConfirmedTooltipOwners(List<VisualPanelRegion> surfaces,
            IList<OcrLine> lines)
        {
            List<RectangleF> owners = new List<RectangleF>();
            foreach (VisualPanelRegion surface in surfaces)
            {
                if (surface.Kind != VisualPanelSurfaceKind.DarkBlue || surface.SurfaceBounds.IsEmpty) continue;
                RectangleF bounds = surface.SurfaceBounds; bounds.Inflate(2, 2);
                StringBuilder text = new StringBuilder();
                foreach (OcrLine line in lines) foreach (OcrWord word in line.Words)
                    if (bounds.Contains(new PointF(word.BoundingRect.Left + word.BoundingRect.Width / 2,
                        word.BoundingRect.Top + word.BoundingRect.Height / 2)))
                        text.Append(word.Text).Append(' ');
                string key = TranslationStore.Normalize(text.ToString());
                bool skill = key.Contains("master level") || key.Contains("current level") ||
                    key.Contains("next level") || key.Contains("required skill");
                bool equipment = EquipmentFieldGrammar.LooksLikeEquipmentStructure(text.ToString());
                // Blue scenery, chat backgrounds and character stat cells do not
                // become tooltips merely because their colour is similar.
                if (!skill && !equipment) continue;
                owners.Add(bounds);
            }
            Program.LogRuntimeEvent("tooltip-surface", "confirmed=" + owners.Count + " extraOcr=0");
            return owners;
        }

        private static List<OcrLine> SplitTooltipOcrLines(IList<OcrLine> lines, List<RectangleF> owners)
        {
            List<OcrLine> result = new List<OcrLine>();
            foreach (OcrLine line in lines)
            {
                if (owners.Count == 0 || line.Words.Count == 0) { result.Add(line); continue; }
                Dictionary<int, List<OcrWord>> parts = new Dictionary<int, List<OcrWord>>();
                foreach (OcrWord word in line.Words)
                {
                    int owner = TooltipOwnerOf(word.BoundingRect, owners);
                    List<OcrWord> words;
                    if (!parts.TryGetValue(owner, out words)) { words = new List<OcrWord>(); parts.Add(owner, words); }
                    words.Add(word);
                }
                if (parts.Count == 1) { result.Add(line); continue; }
                foreach (List<OcrWord> words in parts.Values)
                {
                    OcrLine part = new OcrLine(); StringBuilder text = new StringBuilder();
                    foreach (OcrWord word in words) { if (text.Length > 0) text.Append(' '); text.Append(word.Text); part.Words.Add(word); }
                    part.Text = text.ToString(); result.Add(part);
                }
            }
            return result;
        }

        private Dictionary<OcrLine, OcrPanelInfo> BuildOcrPanels(List<OcrLine> lines)
        { return BuildOcrPanels(lines, null); }

        private Dictionary<OcrLine, OcrPanelInfo> BuildOcrPanels(List<OcrLine> lines,
            List<RectangleF> tooltipOwners)
        {
            Dictionary<OcrLine, OcrPanelInfo> byLine = new Dictionary<OcrLine, OcrPanelInfo>();
            int count = lines.Count;
            int[] parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    int a = TooltipOwnerOf(GetOcrLineBounds(lines[i]), tooltipOwners);
                    int b = TooltipOwnerOf(GetOcrLineBounds(lines[j]), tooltipOwners);
                    if (a != b) continue;
                    if (a >= 0 || BelongToSamePanel(lines[i], lines[j])) UnionPanel(parent, i, j);
                }

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
                panel.SkillId = ResolveSkillPanelId(panel.Lines);
                string skillContentId = panel.SkillId;
                if (panel.SkillId.Length == 0) panel.SkillId = skillContentId;
                panel.IsSkillDetail = normalized.Contains("master level") ||
                    (normalized.Contains("current level") && normalized.Contains("next level")) ||
                    normalized.Contains("required skill") ||
                    ((normalized.Contains("current level") || normalized.Contains("next level")) &&
                     panel.Lines.Count >= 2 && LooksLikeSkillEffectBody(panel.Text)) ||
                    (skillContentId.Length > 0 && normalized.Length >= 24) ||
                    (panel.SkillId.Length > 0 && normalized.Length >= 35 &&
                     (normalized.Contains("damage") || normalized.Contains("duration") ||
                      normalized.Contains("seconds") || normalized.Contains(" mp ")));
                panel.IsCharacterInfo = CharacterPanelPolicy.IsInformation(normalized);
                panel.IsCharacterStats = CharacterPanelPolicy.IsStatistics(normalized);
                // Equipment recognition is grammar-based rather than a short list of
                // literal labels. A partially OCR'd tooltip (req row missing, only one
                // defence line recovered, Evasion/Attack Speed present) still reaches the
                // structured field path. Character-stat windows are explicitly excluded
                // because they share Attack/Def/Accuracy/Speed vocabulary.
                bool characterShape = normalized.Contains("character stat") ||
                    normalized.Contains("ability point") ||
                    (normalized.Contains("accuracy") && normalized.Contains("jump") &&
                     normalized.Contains("speed"));
                panel.EquipmentReason = "";
                if (!panel.IsSkillDetail && !panel.IsCharacterStats && !characterShape &&
                    EquipmentFieldGrammar.LooksLikeEquipmentStructure(panel.Text))
                    panel.EquipmentReason = "grammar-structure";
                panel.IsEquipmentDetail = panel.EquipmentReason.Length > 0;
                panel.IsSettings = SettingsPanelPolicy.IsSettingsWindow(panel.Text);
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

        private List<OverlayLabel> BuildLabels(OcrResult result, float ocrScale, Bitmap prepared,
            LabelBuildScope scope)
        {
            Stopwatch dictionaryClock=Stopwatch.StartNew();
            try {
            List<OverlayLabel> output = new List<OverlayLabel>();
            List<VisualPanelRegion> measuredSurfaces = prepared == null
                ? new List<VisualPanelRegion>() : ClassicSceneVision.FindForegroundPanels(prepared);
            List<RectangleF> tooltipOwners = FindConfirmedTooltipOwners(measuredSurfaces, result.Lines);
            foreach (OcrLine detected in result.Lines)
            {
                RectangleF raw = GetOcrLineBounds(detected);
                RememberCaptureTextRow(raw, ocrScale);
            }
            Rectangle chatExclusion = GetChatExclusionBounds();
            List<OcrLine> allLines = new List<OcrLine>();
            foreach (OcrLine candidate in SplitTooltipOcrLines(result.Lines, tooltipOwners))
            {
                // Static interface/item matching must never translate player chat. A chat line
                // can legitimately contain an exact item or skill name ("T> Bamboo Hat"), but
                // that is not evidence that the corresponding tooltip is open. Chat has its
                // own explicit AI translator and is excluded here even without a saved region.
                bool inTooltip = TooltipOwnerOf(GetOcrLineBounds(candidate), tooltipOwners) >= 0;
                if (!inTooltip && SceneClassifier.LooksLikePlayerChat(candidate.Text)) continue;
                if (!inTooltip && IsChatLine(candidate, ocrScale, chatExclusion) &&
                    !translations.HasDetailCandidate(candidate.Text) &&
                    !LooksLikeEquipmentStatText(candidate.Text)) continue;
                allLines.Add(candidate);
            }
            allLines = SkillVisualLines(allLines);
            // Colour surfaces own their text before proximity grouping. Inventory
            // rows cannot bridge into a dark-blue tooltip, even at the same Y.
            Dictionary<OcrLine, OcrPanelInfo> panelByLine = BuildOcrPanels(allLines, tooltipOwners);
            AddStablePanelLayouts(output, allLines, panelByLine, ocrScale);
            SceneSnapshot visibleScene = SceneClassifier.Analyze(result, translations, false);
            bool focusedSettingsWindow = scope == LabelBuildScope.FocusedPanel &&
                SettingsPanelPolicy.IsSettingsWindow(result.Text);
            bool discoveredSettingsWindow = SettingsPanelPolicy.IsSettingsWindow(result.Text);
            RectangleF characterStatScope = RectangleF.Empty, characterInfoScope = RectangleF.Empty;
            RectangleF worldMapScope = RectangleF.Empty, settingsScope = RectangleF.Empty;
            bool hasScopedCaption = allLines.Exists(delegate(OcrLine caption) {
                return PanelRecognitionPolicy.CaptionKind(TranslationStore.Normalize(caption.Text)) != VerifiedPanelKind.None;
            });
            List<RectangleF> measuredPanelScopes = new List<RectangleF>();
            bool questSummaryObserved = HasQuestSummaryMarker(allLines);
            if ((hasScopedCaption || questSummaryObserved) && prepared != null)
            {
                foreach (VisualPanelRegion measured in measuredSurfaces)
                    measuredPanelScopes.Add(measured.Bounds);
                CharacterStatVisualLayout measuredStats = ClassicSceneVision.FindCharacterStats(prepared);
                if (measuredStats != null) measuredPanelScopes.Add(measuredStats.Crop);
            }
            foreach (OcrLine caption in allLines)
            {
                VerifiedPanelKind kind = PanelRecognitionPolicy.CaptionKind(TranslationStore.Normalize(caption.Text));
                if (kind == VerifiedPanelKind.Settings && !discoveredSettingsWindow && !focusedSettingsWindow) continue;
                RectangleF scopeBounds = PanelRecognitionPolicy.MeasuredScope(GetOcrLineBounds(caption), ocrScale,
                    captureBounds.Width, captureBounds.Height, kind, measuredPanelScopes);
                if (kind == VerifiedPanelKind.Statistics) characterStatScope = scopeBounds;
                else if (kind == VerifiedPanelKind.Information) characterInfoScope = scopeBounds;
                else if (kind == VerifiedPanelKind.WorldMap) worldMapScope = scopeBounds;
                else if (kind == VerifiedPanelKind.Settings) settingsScope = scopeBounds;
            }
            bool globalEquipmentContext = visibleScene.IsStrong(SceneKind.Item);
            bool mainQuestShell = HasQuestWindowShell(allLines);
            bool questPanelContext = mainQuestShell || questSummaryObserved;
            List<PanelTextAnchor> questAnchors = new List<PanelTextAnchor>();
            if (questPanelContext) foreach (OcrLine line in allLines)
                questAnchors.Add(new PanelTextAnchor { Key = TranslationStore.Normalize(line.Text), Bounds = GetOcrLineBounds(line) });
            RectangleF measuredQuestDetails = QuestDetailGeometry.Measure(questAnchors,ocrScale,
                new System.Drawing.Size((int)Math.Ceiling(captureBounds.Width*ocrScale),(int)Math.Ceiling(captureBounds.Height*ocrScale)));
            if (measuredQuestDetails.IsEmpty && questPanelContext)
                measuredQuestDetails = QuestTextScope.Measure(questAnchors, measuredPanelScopes, ocrScale,
                    new System.Drawing.Size((int)Math.Ceiling(captureBounds.Width*ocrScale),(int)Math.Ceiling(captureBounds.Height*ocrScale)));
            List<OcrLine> taskContextLines = questPanelContext
                ? (!measuredQuestDetails.IsEmpty ? allLines.FindAll(delegate(OcrLine line) {
                    return measuredQuestDetails.Contains(GetOcrLineBounds(line));
                }) : mainQuestShell ? SelectQuestDetailLines(allLines, ocrScale) : allLines) : allLines;
            List<string> localTaskLines = new List<string>();
            StringBuilder taskContextText = new StringBuilder();
            foreach (OcrLine candidate in taskContextLines)
            {
                localTaskLines.Add(candidate.Text);
                if (taskContextText.Length > 0) taskContextText.AppendLine();
                taskContextText.Append(candidate.Text);
            }
            string focusedTaskTitle = questPanelContext
                ? FindQuestConcreteTaskTitle(taskContextLines, ocrScale) : taskContextText.ToString();
            string scopedTaskId = PanelContextPolicy.ResolveTaskId(focusedTaskTitle,
                localTaskLines, translations, scope == LabelBuildScope.FocusedPanel || !measuredQuestDetails.IsEmpty,
                questPanelContext);
            Program.LogRuntimeEvent("quest-context", "scope="+scope+" shell="+mainQuestShell+
                " summary="+questSummaryObserved+" measured="+!measuredQuestDetails.IsEmpty+
                " localLines="+taskContextLines.Count+" resolved="+(scopedTaskId.Length>0));
            if (questPanelContext && scopedTaskId.Length > 0) requestQuestMemoryIds.Add(scopedTaskId);
            if (Program.Benchmark && scope == LabelBuildScope.FocusedPanel)
                benchmarkQuestDebug += " [quest:" + (mainQuestShell ? "shell" : "noshell") +
                    ",lines=" + taskContextLines.Count + ",title=" + focusedTaskTitle +
                    ",id=" + scopedTaskId + ",textlen=" + taskContextText.Length + "]";
            bool hasJournal = TryAddQuestJournal(output, taskContextLines, allLines, prepared,
                ocrScale, questPanelContext, measuredQuestDetails);
            RectangleF ownedJournal = hasJournal ? output[output.Count-1].SourceBounds : RectangleF.Empty;
            if (!hasJournal && scopedTaskId.Length > 0)
            {
                AddQuestPanelTextLabels(output, taskContextLines, scopedTaskId, ocrScale, false);
                AddQuestWholeResultFallback(output, taskContextLines, taskContextText.ToString(),
                    scopedTaskId, ocrScale, false);
            }
            else if (!hasJournal && (mainQuestShell || FindQuestShellAnchor(allLines) != null ||
                HasQuestSummaryMarker(allLines)))
            {
                // A citizenship chain such as "Donating to Kerning City" repeats one body
                // across 18 identically named quests, so no single id can be resolved and
                // guessing one is forbidden. The shared wording is still safe to paint:
                // sharedOnly keeps entries that carry no id, so a body belonging to one
                // specific quest can never be borrowed by a differently selected task.
                AddQuestPanelTextLabels(output, taskContextLines, "", ocrScale, true);
                AddQuestWholeResultFallback(output, taskContextLines, taskContextText.ToString(),
                    "", ocrScale, true);
            }
            HashSet<OcrPanelInfo> handledSkillPanels = new HashSet<OcrPanelInfo>();
            HashSet<OcrPanelInfo> handledEquipmentPanels = new HashSet<OcrPanelInfo>();
            HashSet<OcrLine> handledItemLines = new HashSet<OcrLine>();
            List<ItemPanelPlan> itemPlans = new List<ItemPanelPlan>();
            AddOrdinaryItemPanels(output, allLines, panelByLine, ocrScale,
                handledItemLines, itemPlans);
            for (int lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
            {
                OcrLine line = allLines[lineIndex];
                if (handledItemLines.Contains(line)) continue;
                if (hasJournal && JournalOwnsLine(ownedJournal, line, ocrScale)) continue;
                OcrPanelInfo currentPanel;
                panelByLine.TryGetValue(line, out currentPanel);
                bool questInterface = currentPanel != null && currentPanel.IsQuest;
                bool settingsPanel = currentPanel != null && currentPanel.IsSettings;
                RectangleF currentLineBounds = GetOcrLineBounds(line);
                bool lineInsideSettingsWindow = PanelRecognitionPolicy.Owns(settingsScope, currentLineBounds);
                // Task context belongs to this focused crop only. OcrPanelInfo can still carry
                // a title for scene classification, but it may never override the selected
                // detail pane or leak a task ID from a neighbouring helper/list.
                string activeTaskId = !measuredQuestDetails.IsEmpty && !measuredQuestDetails.Contains(currentLineBounds)
                    ? "" : scopedTaskId;
                if (currentPanel != null && currentPanel.IsSkillDetail &&
                    handledSkillPanels.Add(currentPanel))
                {
                    AddSkillPanelLabels(output, currentPanel, ocrScale);
                }
                if (currentPanel != null && currentPanel.IsSkillDetail) continue;
                // A skill name is translated independently only in an explicit
                // skill-list/title context. A bare "Heal" in ordinary prose or a
                // player name must never become a global skill replacement.
                if (TryAddSkillListNameLabel(output, line, allLines, ocrScale)) continue;

                bool equipmentPanel = currentPanel != null && currentPanel.IsEquipmentDetail;
                if (equipmentPanel && handledEquipmentPanels.Add(currentPanel))
                    AddEquipmentPanelLabels(output, currentPanel, ocrScale);

                string structuredLine;
                bool lineEquipmentStructure = LooksLikeEquipmentFieldText(line.Text);
                bool questObjectiveContext = mainQuestShell || questInterface ||
                    visibleScene.IsStrong(SceneKind.Quest);
                if (settingsPanel || focusedSettingsWindow || lineInsideSettingsWindow)
                {
                    List<MatchResult> settingsMatches =
                        translations.FindSettingsTextMatches(line.Text);
                    if (settingsMatches.Count > 0)
                    {
                        AddExactLabels(output, new List<OcrLine> { line }, line.Text,
                            settingsMatches, ocrScale);
                        continue;
                    }
                }
                if (questObjectiveContext &&
                    TryAddQuestObjectiveNameLabel(output, line, ocrScale))
                    continue;
                if (TryTranslateStructuredLine(line.Text,
                    PanelContextPolicy.UseEquipmentSemantics(equipmentPanel,
                        lineEquipmentStructure),
                    false, out structuredLine))
                {
                    AddWholeLineLabel(output, line, structuredLine, ocrScale,
                        structuredLine.Length > 24);
                    continue;
                }

                // Quest-window columns have different responsibilities: every title in the
                // left list is translated independently, while the concrete title on the right
                // is also the only key allowed to select long task text. Never constrain these
                // short title rows to the selected task ID.
                if (mainQuestShell || questInterface || visibleScene.IsStrong(SceneKind.Quest))
                {
                    List<MatchResult> taskNameMatches = translations.FindTaskNameMatches(line.Text);
                    if (taskNameMatches.Count > 0)
                    {
                        AddExactLabels(output, new List<OcrLine> { line }, line.Text,
                            taskNameMatches, ocrScale);
                        continue;
                    }
                }

                List<MatchResult> characterStatMatches = translations.FindCharacterStatMatches(line.Text);
                bool characterStatLine = (currentPanel != null && currentPanel.IsCharacterStats) ||
                    PanelRecognitionPolicy.Owns(characterStatScope, currentLineBounds);
                bool characterInformationLine = (currentPanel != null && currentPanel.IsCharacterInfo) ||
                    PanelRecognitionPolicy.Owns(characterInfoScope, currentLineBounds);
                bool worldMapLine = PanelRecognitionPolicy.Owns(worldMapScope, currentLineBounds);
                if (characterInformationLine || worldMapLine)
                {
                    List<MatchResult> scopedFields = translations.FindMatches(line.Text);
                    scopedFields.RemoveAll(delegate(MatchResult field) {
                        if (characterInformationLine && (PanelRecognitionPolicy.IsInformationField(field.Entry.Normalized) || field.Entry.Category.StartsWith("怀旧服-职业#",StringComparison.Ordinal))) return false;
                        if (worldMapLine && (field.Entry.Category.StartsWith("怀旧服-地图", StringComparison.Ordinal) ||
                            field.Entry.Category.StartsWith("怀旧服-任务组", StringComparison.Ordinal) ||
                            field.Entry.Normalized == "world map" || field.Entry.Normalized == "maple island")) return false;
                        return true;
                    });
                    if (scopedFields.Count > 0)
                    {
                        AddExactLabels(output, new List<OcrLine> { line }, line.Text, scopedFields, ocrScale);
                        continue;
                    }
                }
                bool earlyEquipmentStats = lineEquipmentStructure || characterStatLine;
                if (equipmentPanel && LooksLikeEquipmentFieldText(line.Text)) continue;
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
                    List<MatchResult> interfaceMatches =
                        translations.FindInterfaceTextMatches(interfaceText.ToString());
                    List<MatchResult> dialogueMatches =
                        translations.FindDialogueTextMatches(interfaceText.ToString());
                    int interfaceLongest = 0, dialogueLongest = 0;
                    foreach (MatchResult match in interfaceMatches)
                        interfaceLongest = Math.Max(interfaceLongest, match.Entry.Normalized.Length);
                    foreach (MatchResult match in dialogueMatches)
                        dialogueLongest = Math.Max(dialogueLongest, match.Entry.Normalized.Length);
                    if (dialogueLongest > interfaceLongest) interfaceMatches = dialogueMatches;
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
                    bool secondaryEquipmentStats = LooksLikeEquipmentFieldText(bestInterfaceText);
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
                bool itemDetailAllowed = !questPanelContext &&
                    !visibleScene.IsStrong(SceneKind.Quest) &&
                    (currentPanel == null || (!currentPanel.IsQuest && !currentPanel.IsSkillDetail &&
                     !currentPanel.IsCharacterStats && !currentPanel.IsCharacterInfo &&
                     !currentPanel.IsSettings && !currentPanel.IsEquipmentDetail));
                bool itemDetailStart = itemDetailAllowed && !skillDetailStart &&
                    translations.LooksLikeItemTextStart(line.Text);
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
                bool equipmentStatLine = LooksLikeEquipmentFieldText(line.Text);
                bool taskTextContext = (scope == LabelBuildScope.FocusedPanel || !measuredQuestDetails.IsEmpty) &&
                    scopedTaskId.Length > 0 &&
                    normalizedCurrentLine.Length >= 18 &&
                    (currentPanel == null || (!currentPanel.IsSkillDetail && !currentPanel.IsEquipmentDetail &&
                     !currentPanel.IsCharacterStats));
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
                    KeepHighCoverageMatches(matches, normalizedCurrentLine.Length,
                        visibleScene.IsStrong(SceneKind.Quest));
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
                    int bestTaskLineCount = 0;
                    int skippedTaskNoise = 0;
                    for (int span = 0; span < 7 && lineIndex + span < allLines.Count; span++)
                    {
                        OcrLine candidateLine = allLines[lineIndex + span];
                        if (span > 0 && !CanJoinOcrLines(allLines[lineIndex + span - 1], candidateLine))
                        {
                            // A wrapped quest body can be split by the quest window's own
                            // scrollbar or label strip ("...a donation of 30" + "Citizen's R.."
                            // + "ge Bubbles for Kerning City."). Skip at most two such lines and
                            // resume only when the next line still continues the same block.
                            if (skippedTaskNoise < 2) { skippedTaskNoise++; continue; }
                            break;
                        }
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
                            // Skipped noise lines are not in combinedLines, so the winning
                            // fragment must be sliced by how many lines were actually added.
                            bestTaskLineCount = combinedLines.Count;
                        }
                    }
                    if (bestTaskMatches != null && bestTaskMatches.Count > 0)
                    {
                        AddExactLabels(output, combinedLines.GetRange(0, bestTaskLineCount), bestTaskText, bestTaskMatches, ocrScale);
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
                    bool corroboratedItemName = matches.Count == 1 &&
                        translations.HasItemNameAndDescription(line.Text, matches[0].Entry, result.Text);
                    if (!strongFocusedItemName && !corroboratedItemName) continue;
                }
                AddExactLabels(output, new List<OcrLine> { line }, line.Text, matches, ocrScale);
            }
            // A preceding multi-line dictionary match may advance lineIndex past a compact
            // equipment or skill-level row even though OCR captured that row correctly.
            // Revisit only explicit structured prefixes so ordinary dialogue numbers cannot
            // be mistaken for live panel values.
            foreach (OcrLine line in allLines)
            {
                if (hasJournal && JournalOwnsLine(ownedJournal, line, ocrScale)) continue;
                string structured;
                OcrPanelInfo fieldOwner;
                panelByLine.TryGetValue(line, out fieldOwner);
                if ((fieldOwner == null || !fieldOwner.IsSkillDetail) && LooksLikeEquipmentFieldText(line.Text) &&
                    TryTranslateStructuredLine(line.Text, true, false, out structured))
                    AddWholeLineLabel(output, line, structured, ocrScale,
                        structured.Length > 24);
                bool skillLevel = line.Text.IndexOf("master level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.Text.IndexOf("current level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.Text.IndexOf("next level", StringComparison.OrdinalIgnoreCase) >= 0;
                if (skillLevel && TryTranslateStructuredLine(line.Text, false, true, out structured))
                    AddWholeLineLabel(output, line, structured, ocrScale, false);
            }
            AddStructuredFieldLabels(output, allLines, panelByLine, ocrScale, handledItemLines);
            AddCharacterFieldSurfaces(output, allLines, prepared, ocrScale);
            // Long-match scanning may advance past a list row. Revisit independent
            // local list names, without borrowing the tooltip's skill identity.
            foreach (OcrLine listLine in allLines)
                TryAddSkillListNameLabel(output, listLine, allLines, ocrScale);
            AddSkillOverviewSections(output, allLines, panelByLine, prepared, ocrScale, handledSkillPanels);
            AddSkillEffectSections(output, allLines, panelByLine, ocrScale, handledSkillPanels,
                handledItemLines);
            if (prepared != null && questPanelContext)
                AddObjectiveSurfaceLabels(output, allLines, prepared,
                    ResolveQuestJournalOwner(allLines, prepared.Size, ocrScale, measuredQuestDetails), ocrScale);
            MergeAdjacentLongLabels(output);
            MergeOverlappingSameTextLabels(output);
            MergeSameTextLabels(output);
            if (Program.Benchmark && scope == LabelBuildScope.FocusedPanel)
            {
                benchmarkQuestDebug += " [before-contained=" + output.Count + ":";
                foreach (OverlayLabel debugLabel in output)
                    benchmarkQuestDebug += (debugLabel.Text ?? "") + "@" +
                        debugLabel.Bounds.Left.ToString("0") + "," + debugLabel.Bounds.Top.ToString("0") + ";";
                benchmarkQuestDebug += "]";
            }
            RemoveContainedLabels(output);
            if (Program.Benchmark && scope == LabelBuildScope.FocusedPanel)
                benchmarkQuestDebug += " [after-contained=" + output.Count + "]";
            ApplyTaskBodyScope(output, measuredQuestDetails, ocrScale);
            if (!hasJournal && output.Exists(delegate(OverlayLabel item){return item.SemanticRole=="task-body";}))
                TryAddQuestJournal(output, taskContextLines, allLines, prepared, ocrScale, questPanelContext, measuredQuestDetails);
            int bodyLabels=output.FindAll(delegate(OverlayLabel item){return item.SemanticRole=="task-body" || item.SemanticRole=="task-journal";}).Count;
            Program.LogRuntimeEvent("quest-body", "scope="+scope+" candidates="+bodyLabels);
            foreach (OverlayLabel label in output)
                foreach (RectangleF owner in tooltipOwners)
                    if (OverlaySpan(owner, ocrScale).Contains(label.SourceBounds))
                    { label.TooltipPriority = true; break; }
            return output;
        } finally {Program.LogRuntimeEvent("dictionary-pass","scope="+scope+
                " lines="+result.Lines.Count+" elapsedMs="+dictionaryClock.ElapsedMilliseconds);}
        }

        private sealed class StrongTextCandidate
        {
            internal List<StrongOcrLine> Lines;
            internal MatchResult Match;
            internal int Score;
        }

        // The strong helper has its own DTO and never leaks third-party Paddle types into the
        // overlay. This deliberately smaller matcher supplements the full Windows-OCR scene
        // pipeline: exact/near-exact dictionary evidence is added, while weak text can only leave
        // the already-published Windows labels unchanged.
        private List<OverlayLabel> BuildStrongOcrLabels(StrongOcrResult result,
            bool equipmentContext)
        {
            List<OverlayLabel> output = new List<OverlayLabel>();
            if (result == null || !result.Succeeded) return output;
            foreach (StrongOcrLine detected in result.Lines)
                if (detected != null) RememberCaptureTextRow(detected.Bounds, 1.0f);
            StringBuilder surfaceText = new StringBuilder();
            foreach (StrongOcrLine sourceLine in result.Lines) if (sourceLine != null) surfaceText.Append(sourceLine.Text).Append(' ');
            bool skillTooltip = equipmentContext && IsSkillTooltipText(surfaceText.ToString());
            if (skillTooltip) equipmentContext = false;
            List<StrongOcrLine> lines = new List<StrongOcrLine>(result.Lines);
            lines.Sort(delegate(StrongOcrLine left, StrongOcrLine right) {
                int vertical = left.Bounds.Top.CompareTo(right.Bounds.Top);
                return Math.Abs(left.Bounds.Top - right.Bounds.Top) <=
                    Math.Max(left.Bounds.Height, right.Bounds.Height) * 0.45f
                    ? left.Bounds.Left.CompareTo(right.Bounds.Left) : vertical;
            });

            foreach (StrongOcrLine line in lines)
            {
                if (line == null || String.IsNullOrWhiteSpace(line.Text) ||
                    SceneClassifier.LooksLikePlayerChat(line.Text)) continue;
                string structured;
                if (TryTranslateStructuredLine(line.Text,
                    PanelContextPolicy.UseEquipmentSemantics(equipmentContext,
                        !skillTooltip && LooksLikeEquipmentFieldText(line.Text)), skillTooltip, out structured))
                {
                    AddStrongBlockLabel(output, new List<StrongOcrLine> { line },
                        structured, structured.Length > 24, "strong-structured");
                    continue;
                }

                string normalized = TranslationStore.Normalize(line.Text);
                if (normalized.Length < 2) continue;
                List<MatchResult> matches = translations.FindSkillNameMatches(line.Text);
                if (matches.Count == 0) matches = translations.FindTaskNameMatches(line.Text);
                if (matches.Count == 0) matches = translations.FindMatches(line.Text);

                MatchResult best = null;
                int bestScore = Int32.MinValue;
                foreach (MatchResult match in matches)
                {
                    if (match == null || match.Entry == null ||
                        String.IsNullOrWhiteSpace(match.Entry.Chinese)) continue;
                    // The ordinary scene pipeline already owns generic buttons (AUTO/NEXT/MENU),
                    // settings and chat abbreviations. A broad strong-OCR crop can include HUD
                    // rows outside the actual panel, so only canonical game entities/titles may
                    // add a short label here. Long prose is separately guarded below by a
                    // high-coverage whole-block match.
                    if (!IsStrongCanonicalEntry(match.Entry)) continue;
                    int entryLength = match.Entry.Normalized.Length;
                    bool highCoverage = match.Length * 10 >= normalized.Length * 7 ||
                        entryLength * 10 >= normalized.Length * 7;
                    if (!highCoverage && normalized.Length > 24) continue;
                    if (entryLength < 4 && normalized.Length > 8) continue;
                    int score = Math.Min(match.Length, entryLength) * 100 -
                        Math.Abs(normalized.Length - entryLength);
                    if (score > bestScore) { best = match; bestScore = score; }
                }
                if (best != null)
                    AddStrongLineMatchLabel(output, line, normalized, best);
            }

            AddStrongLongTextLabel(output, lines);
            MergeOverlappingSameTextLabels(output);
            MergeSameTextLabels(output);
            RemoveContainedLabels(output);
            return output;
        }

        private static bool IsStrongCanonicalEntry(TranslationEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(entry.Category)) return false;
            string category = entry.Category;
            return category.StartsWith("怀旧服-地图", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-职业", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-装备", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-道具", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-怪物", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-技能", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-NPC", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-任务", StringComparison.Ordinal);
        }

        private void AddStrongLineMatchLabel(List<OverlayLabel> output, StrongOcrLine line,
            string normalized, MatchResult match)
        {
            RectangleF raw = line.Bounds;
            int normalizedLength = Math.Max(1, normalized.Length);
            bool wholeLine = match.Length * 10 >= normalizedLength * 8 ||
                match.Entry.Normalized.Length * 10 >= normalizedLength * 8;
            if (!wholeLine)
            {
                float leftRatio = Math.Max(0.0f,
                    Math.Min(1.0f, (float)match.Start / normalizedLength));
                float widthRatio = Math.Max(0.08f,
                    Math.Min(1.0f - leftRatio, (float)match.Length / normalizedLength));
                raw = new RectangleF(raw.Left + raw.Width * leftRatio, raw.Top,
                    Math.Max(2.0f, raw.Width * widthRatio), raw.Height);
            }
            int before = output.Count;
            AddStrongBlockLabel(output, new List<StrongOcrLine> {
                new StrongOcrLine { Text = line.Text, Confidence = line.Confidence,
                    Bounds = raw } }, match.Entry.Chinese,
                match.Entry.Chinese.Length > 24, "strong-exact");
            if (output.Count > before) output[output.Count - 1].SemanticRole = GetEntrySemanticRole(match.Entry);
        }

        private void AddStrongLongTextLabel(List<OverlayLabel> output,
            List<StrongOcrLine> lines)
        {
            StrongTextCandidate best = null;
            for (int start = 0; start < lines.Count; start++)
            {
                List<StrongOcrLine> window = new List<StrongOcrLine>();
                StringBuilder combined = new StringBuilder();
                for (int span = 0; span < 12 && start + span < lines.Count; span++)
                {
                    StrongOcrLine current = lines[start + span];
                    if (span > 0 && !CanJoinStrongLines(window[window.Count - 1], current)) break;
                    window.Add(current);
                    if (combined.Length > 0) combined.Append(' ');
                    combined.Append(current.Text);
                    string text = combined.ToString();
                    string normalized = TranslationStore.Normalize(text);
                    if (normalized.Length < 24) continue;
                    string taskId = translations.DetectTaskId(text);
                    List<MatchResult> candidates = taskId.Length > 0
                        ? translations.FindTaskMatches(text, taskId)
                        : translations.FindSharedTaskBodyMatches(text);
                    if (candidates.Count == 0)
                        candidates = translations.FindInterfaceTextMatches(text);
                    if (candidates.Count == 0)
                        candidates = translations.FindDialogueTextMatches(text);
                    foreach (MatchResult match in candidates)
                    {
                        if (match == null || match.Entry == null ||
                            match.Entry.Normalized.Length < 24) continue;
                        int score = ScoreStrongLongTextCandidate(text, normalized, match,
                            AverageStrongConfidence(window));
                        if (score == Int32.MinValue) continue;
                        if (best == null || score > best.Score)
                            best = new StrongTextCandidate {
                                Lines = new List<StrongOcrLine>(window), Match = match, Score = score };
                    }
                }
            }
            if (best == null) return;
            foreach (OverlayLabel existing in output)
                if (SameOverlayText(existing.Text, best.Match.Entry.Chinese)) return;
            int before = output.Count;
            AddStrongBlockLabel(output, best.Lines, best.Match.Entry.Chinese,
                true, "strong-long-text");
            if (output.Count > before) output[output.Count - 1].SemanticRole = GetEntrySemanticRole(best.Match.Entry);
        }

        private static int ScoreStrongLongTextCandidate(string text, string normalized,
            MatchResult match, float confidence)
        {
            if (match == null || match.Entry == null) return Int32.MinValue;
            if (match.Entry.IsOrdinaryItemText &&
                match.Entry.ItemStatus != ItemSemanticStatus.Trusted) return Int32.MinValue;
            int entryLength = match.Entry.Normalized == null ? 0 : match.Entry.Normalized.Length;
            if (entryLength < 24 || String.IsNullOrEmpty(normalized)) return Int32.MinValue;
            float similarity = TranslationStore.DetailTextSimilarity(text,
                match.Entry.Normalized);
            float sourceCoverage = (float)match.Length / Math.Max(1, normalized.Length);
            float entryCoverage = (float)match.Length / Math.Max(1, entryLength);
            // Never reward a longer dictionary row merely for being longer. Quest corpora can
            // contain several journal states under one task id; the old length-first score made
            // such an aggregate row beat the state currently visible in the game. Require evidence
            // in both directions, while sequence similarity keeps tolerance for damaged glyphs.
            bool enough = similarity >= 0.60f ||
                (sourceCoverage >= 0.70f && entryCoverage >= 0.55f);
            if (!enough) return Int32.MinValue;
            return (int)(similarity * 100000.0f) +
                (int)(Math.Min(sourceCoverage, entryCoverage) * 10000.0f) -
                Math.Abs(normalized.Length - entryLength) * 10 +
                (int)(confidence * 100.0f);
        }

        private static bool CanJoinStrongLines(StrongOcrLine first, StrongOcrLine second)
        {
            if (first == null || second == null) return false;
            RectangleF a = first.Bounds, b = second.Bounds;
            float height = Math.Max(8.0f, Math.Max(a.Height, b.Height));
            float verticalGap = Math.Max(0.0f, b.Top - a.Bottom);
            if (verticalGap > Math.Max(24.0f, height * 2.2f)) return false;
            float overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            float narrow = Math.Max(1.0f, Math.Min(a.Width, b.Width));
            return overlap >= Math.Min(16.0f, narrow * 0.12f) ||
                Math.Abs(a.Left - b.Left) <= Math.Max(64.0f, narrow * 0.50f);
        }

        private void AddStrongBlockLabel(List<OverlayLabel> output,
            List<StrongOcrLine> lines, string text, bool wrap, string source)
        {
            if (lines == null || lines.Count == 0 || String.IsNullOrWhiteSpace(text)) return;
            RectangleF raw = RectangleF.Empty;
            List<RectangleF> rows = new List<RectangleF>();
            foreach (StrongOcrLine line in lines)
            {
                if (line == null || line.Bounds.IsEmpty) continue;
                raw = raw.IsEmpty ? line.Bounds : RectangleF.Union(raw, line.Bounds);
                rows.Add(new RectangleF(ToOverlayX(line.Bounds.Left, 1.0f),
                    ToOverlayY(line.Bounds.Top, 1.0f), line.Bounds.Width, line.Bounds.Height));
            }
            if (raw.IsEmpty) return;
            RectangleF sourceBounds = new RectangleF(ToOverlayX(raw.Left, 1.0f),
                ToOverlayY(raw.Top, 1.0f), Math.Max(1.0f, raw.Width), Math.Max(1.0f, raw.Height));
            float confidence = AverageStrongConfidence(lines);
            output.Add(new OverlayLabel {
                Bounds = new RectangleF(sourceBounds.Left - 3.0f, sourceBounds.Top - 2.0f,
                    Math.Max(100.0f, sourceBounds.Width + 6.0f),
                    Math.Max(20.0f, sourceBounds.Height + 5.0f)),
                SourceBounds = sourceBounds,
                HasSourceBounds = true,
                SourceLineBounds = OverlayPaintSafety.ClampRowsToSpan(rows, sourceBounds),
                PanelBounds = new RectangleF(ToOverlayX(0.0f, 1.0f),
                    ToOverlayY(0.0f, 1.0f), captureBounds.Width, captureBounds.Height),
                Text = text,
                Wrap = wrap || lines.Count > 1,
                Confidence = confidence >= 0.78f ? LabelConfidence.High : LabelConfidence.Medium,
                MatchSource = source,
                ConfidenceReason = confidence
            });
        }

        private static float AverageStrongConfidence(List<StrongOcrLine> lines)
        {
            if (lines == null || lines.Count == 0) return 0.0f;
            float total = 0.0f; int count = 0;
            foreach (StrongOcrLine line in lines)
                if (line != null) { total += line.Confidence; count++; }
            return count == 0 ? 0.0f : total / count;
        }

        private static string DescribeStrongOcrResult(StrongOcrResult result)
        {
            if (result == null) return "无结果，已退回系统OCR";
            if (result.Succeeded)
                return "成功，行=" + result.Lines.Count + "，总耗时=" +
                    result.ElapsedMilliseconds + "ms（模型=" + result.ModelLoadMilliseconds +
                    "ms，识别=" + result.RecognitionMilliseconds + "ms）";
            return result.Status + "：" + (result.Reason ?? "已退回系统OCR").Replace("\r", " ").Replace("\n", " ");
        }

        private static void KeepHighCoverageMatches(List<MatchResult> matches, int lineLength,
            bool questSurface)
        {
            if (matches == null || matches.Count == 0 || lineLength <= 0) return;
            int covered = 0;
            foreach (MatchResult match in matches) covered += Math.Max(0, match.Length);
            bool collectivelyMeaningful = covered * 10 >= lineLength * 6;
            matches.RemoveAll(delegate(MatchResult match) {
                // The generic dictionary must not paint small static UI vocabulary across
                // unrelated screens. Dedicated panel layouts and long-interface matching
                // remain responsible for those labels.
                //
                // "Quest Summary" is the one short label that stays: it is the selected-task
                // pane heading, and the surrounding Quest panel already scopes it. It is
                // released only while a Quest scene is on screen, so ALL/LEVEL/MESOS and any
                // stray "quest summary" text elsewhere stay suppressed exactly as before.
                bool questPaneHeading = questSurface && IsQuestPanelChrome(match.Entry.Normalized);
                bool shortInterface = !questPaneHeading &&
                    match.Entry.Category.StartsWith("怀旧服-界面", StringComparison.Ordinal) &&
                    match.Entry.Normalized.Length < 18;
                if (shortInterface) return true;
                return !collectivelyMeaningful && match.Length * 10 < lineLength * 7;
            });
        }

        // Captions, tabs and buttons that belong to the quest window itself. They are short
        // generic UI words, so the generic coverage filter suppresses them - but on a quest
        // panel they are exactly what the player needs, and translating each one at its real
        // OCR position keeps the label glued to its own button instead of a guessed offset.
        private static bool IsQuestPanelChrome(string normalized)
        {
            switch (normalized)
            {
                case "quest":
                case "quest summary":
                case "details":
                case "forfeit":
                case "quest helper":
                case "available":
                case "in progress":
                case "completed":
                    return true;
                default:
                    return false;
            }
        }

        private void AddStablePanelLayouts(List<OverlayLabel> output, List<OcrLine> lines,
            Dictionary<OcrLine, OcrPanelInfo> panelByLine, float ocrScale)
        {
            bool quest = false, characterStat = false, characterInfo = false, playerShop = false;
            StringBuilder panelText = new StringBuilder();
            foreach (OcrLine candidate in lines)
            {
                if (panelText.Length > 0) panelText.Append(' ');
                panelText.Append(candidate.Text);
            }
            string normalizedPanelText = TranslationStore.Normalize(panelText.ToString());
            bool playerContextMenu = normalizedPanelText.Contains("character info") &&
                normalizedPanelText.Contains("report") &&
                (normalizedPanelText.Contains("blacklist") || normalizedPanelText.Contains("blrcklist")) &&
                (normalizedPanelText.Contains("whisper") || normalizedPanelText.Contains("ujhisper") ||
                 normalizedPanelText.Contains("invite to chat") || normalizedPanelText.Contains("ihvite to chrt"));
            // A task title/dialogue can occur in an NPC conversation without the Quest window.
            // Template labels are safe only when the surrounding Quest shell is also visible.
            // This prevents one dialogue line from projecting non-existent tabs/buttons across
            // the screen while still tolerating one missed header or tab in small classic UI.
            bool questShellPresent = HasQuestWindowShell(lines);
            // HasQuestWindowShell is deliberately strict because it also gates template
            // drawing. The citizenship anchor and the shared quest body need the looser
            // signal instead: Classic UI renders the tabs in low contrast, so OCR
            // frequently returns the "QUEST" caption while missing every tab, and an
            // overlay drawn on a previous pass can hide the caption entirely. The
            // "Quest Summary" heading belongs to the quest pane only, so it is a safe
            // extra proof that the quest window is the surface being read.
            OcrLine anyShellAnchor = FindQuestShellAnchor(lines);
            bool questSurfacePresent = questShellPresent || anyShellAnchor != null ||
                HasQuestSummaryMarker(lines);
            bool allowQuestLayout = captureBounds == gameBounds && questShellPresent;
            OcrLine questShellAnchor = allowQuestLayout ? anyShellAnchor : null;
            if (questShellAnchor != null)
            {
                // The window's own caption, tabs and buttons are translated from their real
                // OCR positions (see IsQuestPanelChrome), so the fixed-offset template is no
                // longer drawn. Its footer offset was a hard-coded 620px, which landed
                // wherever the real buttons were not whenever the window had another size -
                // that is the "labels do not sit on the source text" complaint.
                quest = true;
            }
            foreach (OcrLine line in lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                OcrPanelInfo panel;
                panelByLine.TryGetValue(line, out panel);
                if (!characterStat && normalized.Contains("character stat"))
                {
                    // Coloured field cells are measured by AddCharacterFieldSurfaces.
                    // Fixed offsets can put labels over live values on another UI scale.
                    characterStat = true;
                }
                else if (!characterStat && panel != null && panel.IsCharacterStats &&
                    normalized.StartsWith("name", StringComparison.Ordinal))
                {
                    characterStat = true;
                }
                if (!characterInfo && normalized.Contains("character info"))
                {
                    if (playerContextMenu)
                        AddPlayerContextMenuLayoutLabels(output, line, ocrScale);
                    else
                        AddCharacterInfoLayoutLabels(output, line, ocrScale);
                    characterInfo = true;
                }
                // CITIZENSHIP is both the Character Info tab and a quest title in the
                // citizenship chain. Use it as the Character Info anchor only when no Quest
                // shell is on screen. Otherwise the entire Character Info layout (role,
                // level, popularity, guild, party, trade and pet rows) is projected onto the
                // quest list at unrelated offsets, painting labels whose source text does not
                // exist anywhere in the captured frame.
                else if (!characterInfo && !questSurfacePresent &&
                    normalized.Contains("citizenship"))
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
                // Only the literal window caption is a shell header. "Quest Summary"
                // belongs to the selected task pane; treating it as the window origin
                // projects the left-side tabs and footer buttons across the right pane.
                if (normalized == "quest") header = true;
                if (normalized == "available" || normalized.StartsWith("available ", StringComparison.Ordinal)) tabs++;
                if (normalized == "in progress" || normalized.StartsWith("in progress ", StringComparison.Ordinal)) tabs++;
                if (normalized == "completed" || normalized.StartsWith("completed ", StringComparison.Ordinal)) tabs++;
                if (normalized.Contains("details") || normalized.Contains("forfeit")) footer = true;
            }
            return tabs >= 2 || (header && (tabs >= 1 || footer));
        }

        private static OcrLine FindQuestShellAnchor(List<OcrLine> lines)
        {
            string[] priorities = new string[] { "quest", "available", "in progress", "completed" };
            foreach (string wanted in priorities)
                foreach (OcrLine line in lines)
                {
                    string normalized = TranslationStore.Normalize(line.Text);
                    bool match = wanted == "quest"
                        ? normalized == "quest"
                        : (normalized == wanted || normalized.StartsWith(wanted + " ", StringComparison.Ordinal));
                    if (match) return line;
                }
            return null;
        }

        // "Quest Summary" is the selected-task pane heading and only ever appears inside the
        // quest window. It therefore proves the quest window is on screen even when an overlay
        // painted on an earlier pass has hidden the QUEST caption and every tab - the case that
        // otherwise switches the whole quest pipeline (list, detail and body text) off.
        private static bool HasQuestSummaryMarker(List<OcrLine> lines)
        {
            if (lines == null) return false;
            foreach (OcrLine line in lines)
            {
                if (QuestDetailGeometry.IsSummaryKey(TranslationStore.Normalize(line.Text))) return true;
            }
            return false;
        }

        private List<OcrLine> SelectQuestDetailLines(List<OcrLine> lines, float ocrScale)
        {
            List<OcrLine> detail = new List<OcrLine>();
            if (lines == null || lines.Count == 0 || ocrScale <= 0) return detail;

            float panelLeft = 0.0f;
            OcrLine shellAnchor = FindQuestShellAnchor(lines);
            if (shellAnchor != null)
            {
                RectangleF anchorBounds = GetOcrLineBounds(shellAnchor);
                if (!anchorBounds.IsEmpty) panelLeft = anchorBounds.Left / ocrScale;
            }
            float detailBoundary = panelLeft + captureBounds.Width * 0.40f;
            float tabRight = 0.0f;
            float helperLeft = Single.MaxValue;
            foreach (OcrLine line in lines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                float left = raw.Left / ocrScale;
                float right = raw.Right / ocrScale;
                if (normalized == "available" || normalized.StartsWith("available ", StringComparison.Ordinal) ||
                    normalized == "in progress" || normalized.StartsWith("in progress ", StringComparison.Ordinal) ||
                    normalized == "completed" || normalized.StartsWith("completed ", StringComparison.Ordinal))
                    tabRight = Math.Max(tabRight, right);
                if (normalized.StartsWith("quest helper", StringComparison.Ordinal))
                    helperLeft = Math.Min(helperLeft, left);
            }
            // The right edge of the three tabs is a measured split between the list and
            // selected-task panes. Prefer it over a fixed 40% estimate, which cuts too far
            // into the detail pane on narrow or DPI-scaled quest windows.
            if (tabRight > panelLeft) detailBoundary = tabRight + 12.0f;

            foreach (OcrLine line in lines)
            {
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                float left = raw.Left / ocrScale;
                float center = (raw.Left + raw.Right) * 0.5f / ocrScale;
                if (center < detailBoundary) continue;
                if (left >= helperLeft - 8.0f) continue;
                detail.Add(line);
            }
            return detail;
        }

        private string FindQuestConcreteTaskTitle(List<OcrLine> detailLines, float ocrScale)
        {
            if (detailLines == null || ocrScale <= 0) return "";
            float summaryBottom = Single.NaN;
            foreach (OcrLine line in detailLines)
            {
                string normalized = TranslationStore.Normalize(line.Text);
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                if (QuestDetailGeometry.IsSummaryKey(normalized))
                {
                    float bottom = raw.Bottom / ocrScale;
                    if (Single.IsNaN(summaryBottom) || bottom < summaryBottom)
                        summaryBottom = bottom;
                }
            }

            // Prefer semantic structure over a fixed percentage. The title beneath
            // "Quest Summary" identifies the concrete sub-task, while the blue header can
            // be a different chain caption. If OCR misses the summary anchor, use the lower
            // part only as a conservative fallback. More than one ID means the crop is mixed.
            string authoritativeText = "";
            string authoritativeId = "";
            float authoritativeTop = Single.MaxValue;
            bool authoritativeConflict = false;
            string fallbackText = "";
            string fallbackId = "";
            float fallbackTop = Single.MaxValue;
            bool fallbackConflict = false;
            foreach (OcrLine line in detailLines)
            {
                string taskId = translations.ResolveUniqueTaskTitleId(line.Text);
                if (taskId.Length == 0) continue;
                RectangleF raw = GetOcrLineBounds(line);
                if (raw.IsEmpty) continue;
                float top = raw.Top / ocrScale;
                bool belowSummary = !Single.IsNaN(summaryBottom)
                    ? top > summaryBottom + 4.0f
                    : top >= captureBounds.Height * 0.35f;
                if (belowSummary)
                {
                    if (authoritativeId.Length > 0 && authoritativeId != taskId)
                        authoritativeConflict = true;
                    else if (top < authoritativeTop)
                    {
                        authoritativeId = taskId;
                        authoritativeText = line.Text;
                        authoritativeTop = top;
                    }
                    continue;
                }
                if (fallbackId.Length > 0 && fallbackId != taskId)
                    fallbackConflict = true;
                else if (top < fallbackTop)
                {
                    fallbackId = taskId;
                    fallbackText = line.Text;
                    fallbackTop = top;
                }
            }
            if (!authoritativeConflict && authoritativeText.Length > 0) return authoritativeText;
            if (!fallbackConflict && fallbackText.Length > 0) return fallbackText;
            return "";
        }

        private void ApplyTaskBodyScope(List<OverlayLabel> output, RectangleF raw, float scale)
        {
            if (raw.IsEmpty || scale <= 0 || !OverlayPaintSafety.IsFiniteRect(raw)) return;
            RectangleF panel = new RectangleF(ToOverlayX(raw.Left, scale), ToOverlayY(raw.Top, scale),
                raw.Width / scale, raw.Height / scale);
            foreach (OverlayLabel label in output)
                if (label.SemanticRole == "task-body" && label.HasSourceBounds && panel.Contains(label.SourceBounds))
                    label.PanelBounds = panel;
        }

        private void AddQuestPanelTextLabels(List<OverlayLabel> output,
            List<OcrLine> lines, string taskId, float ocrScale, bool sharedOnly)
        {
            int debugWindows = 0, debugMatches = 0;
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
                    List<MatchResult> windowMatches = sharedOnly
                        ? translations.FindSharedTaskBodyMatches(text)
                        : translations.FindTaskMatches(text, taskId);
                    if (Program.Benchmark && !sharedOnly && windowMatches.Count > 0)
                    {
                        debugWindows++;
                        debugMatches += windowMatches.Count;
                    }
                    foreach (MatchResult match in windowMatches)
                    {
                        if (sharedOnly && match.Entry.TaskId.Length > 0) continue;
                        if ((!match.Entry.IsTaskText && !match.Entry.IsQuestTarget) ||
                            match.Entry.Normalized.Length < 24) continue;
                        int score = ScoreStrongLongTextCandidate(text,TranslationStore.Normalize(text),match,1.0f);
                        if (score == Int32.MinValue) continue;
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
                int beforeLabel = output.Count;
                AddDetailBlockLabel(output, tight, candidate.Match.Entry.Chinese, ocrScale);
                if (output.Count > beforeLabel) output[output.Count - 1].SemanticRole =
                    GetEntrySemanticRole(candidate.Match.Entry);
                used.Add(raw);
                if (++added >= 3) break;
            }
            if (Program.Benchmark && !sharedOnly)
                benchmarkQuestDebug += " [quest-body:lines=" + (lines == null ? 0 : lines.Count) +
                    ",windows=" + debugWindows + ",matches=" + debugMatches +
                    ",candidates=" + ranked.Count + ",added=" + added + "]";
        }

        private void AddQuestWholeResultFallback(List<OverlayLabel> output,
            List<OcrLine> lines, string text, string taskId, float ocrScale, bool sharedOnly)
        {
            foreach (MatchResult match in sharedOnly
                ? translations.FindSharedTaskBodyMatches(text)
                : translations.FindTaskMatches(text, taskId))
            {
                if (sharedOnly && match.Entry.TaskId.Length > 0) continue;
                if ((!match.Entry.IsTaskText && !match.Entry.IsQuestTarget) ||
                    match.Entry.Normalized.Length < 24) continue;
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
                {
                    int beforeLabel = output.Count;
                    AddDetailBlockLabel(output, related, match.Entry.Chinese, ocrScale);
                    if (output.Count > beforeLabel) output[output.Count - 1].SemanticRole =
                        GetEntrySemanticRole(match.Entry);
                }
            }
        }

        private void AddCharacterStatLayoutLabels(List<OverlayLabel> output, OcrLine header, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(header);
            if (raw.IsEmpty) return;
            float left = ToOverlayX(raw.Left, ocrScale);
            float top = ToOverlayY(raw.Top, ocrScale);
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
            float nameLeft = ToOverlayX(raw.Left, ocrScale);
            float nameTop = ToOverlayY(raw.Top, ocrScale);
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
            float left = ToOverlayX(raw.Left, ocrScale);
            float top = ToOverlayY(raw.Top, ocrScale);
            float headerHeight = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, headerHeight / 14.0f));
            AddCharacterInfoLayoutLabelsAt(output, left, top, scale);
        }

        private void AddPlayerContextMenuLayoutLabels(List<OverlayLabel> output,
            OcrLine characterInfoLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(characterInfoLine);
            if (raw.IsEmpty) return;
            float left = ToOverlayX(raw.Left, ocrScale);
            float top = ToOverlayY(raw.Top, ocrScale);
            float sourceHeight = Math.Max(14.0f, raw.Height / ocrScale);
            float pitch = Math.Max(21.0f, Math.Min(29.0f, sourceHeight * 1.35f));
            float labelHeight = Math.Max(18.0f, Math.Min(23.0f, sourceHeight + 2.0f));
            string[] labels = new string[] { "角色信息", "保存名字", "交易请求", "悄悄话",
                "邀请聊天", "加为好友", "加入黑名单", "举报" };
            for (int i = 0; i < labels.Length; i++)
                AddLayoutLabel(output, labels[i], new RectangleF(left - 2.0f,
                    top + pitch * i, 144.0f, labelHeight));
        }

        private void AddCharacterInfoLayoutFromCitizenship(List<OverlayLabel> output,
            OcrLine citizenshipLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(citizenshipLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(14.0f, raw.Height / ocrScale);
            float scale = Math.Max(0.85f, Math.Min(1.65f, height / 14.0f));
            float citizenshipLeft = ToOverlayX(raw.Left, ocrScale);
            float citizenshipTop = ToOverlayY(raw.Top, ocrScale);
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
            float left = ToOverlayX(raw.Left, ocrScale);
            float top = ToOverlayY(raw.Top, ocrScale);
            float labelHeight = Math.Max(18.0f, 20.0f * scale);
            AddLayoutLabel(output, "卖家信息", new RectangleF(left - 2.0f * scale,
                top - 92.0f * scale, 142.0f * scale, labelHeight));
            AddLayoutLabel(output, "购买道具", new RectangleF(left - 2.0f * scale,
                top - 46.0f * scale, 142.0f * scale, labelHeight));
            AddLayoutLabel(output, "离开商店", new RectangleF(left - 2.0f * scale,
                top - 2.0f * scale, 142.0f * scale, labelHeight));
        }

        private void AddQuestWindowLayoutLabels(List<OverlayLabel> output,
            OcrLine shellLine, float ocrScale)
        {
            RectangleF raw = GetOcrLineBounds(shellLine);
            if (raw.IsEmpty) return;
            float height = Math.Max(15.0f, raw.Height / ocrScale);
            string normalized = TranslationStore.Normalize(shellLine.Text);
            bool header = normalized == "quest";
            float scale = Math.Max(0.82f, Math.Min(1.45f, height / (header ? 15.0f : 20.0f)));
            float anchorLeft = ToOverlayX(raw.Left, ocrScale);
            float anchorTop = ToOverlayY(raw.Top, ocrScale);
            float offsetX = header ? 4.0f : (normalized.StartsWith("available", StringComparison.Ordinal) ? 18.0f :
                (normalized.StartsWith("in progress", StringComparison.Ordinal) ? 145.0f : 282.0f));
            float offsetY = header ? 3.0f : 36.0f;
            float left = anchorLeft - offsetX * scale;
            float top = anchorTop - offsetY * scale;
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
            // Fixed anchor layouts are the most trustworthy labels the pipeline produces: at
            // least two measured anchors confirmed the panel before this row was placed.
            output.Add(new OverlayLabel
            {
                Bounds = bounds, Text = text, Wrap = false, StableLayout = true,
                SemanticRole = "interface",
                Confidence = LabelConfidence.High, MatchSource = "layout"
            });
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

        private static int SkillStructuredPrefixLength(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return 0;
            Match prefix = Regex.Match(text,
                @"^\s*(?:[\[\(]\s*)?(?:master|current|next)\s+level\s*[:：]?\s*[0-9lIoOgqSsB]+(?:\s*[\]\)])?\s*",
                RegexOptions.IgnoreCase);
            return prefix.Success ? prefix.Length : 0;
        }

        private static string StripSkillStructuredPrefix(string text)
        {
            string source = (text ?? "").Trim();
            string body = source;
            bool stripped = false;
            while (body.Length > 0)
            {
                int prefixLength = SkillStructuredPrefixLength(body);
                if (prefixLength <= 0 || prefixLength >= body.Length) break;
                string next = body.Substring(prefixLength).Trim();
                if (next.Length == 0) break;
                body = next;
                stripped = true;
            }
            // A row containing only adjacent fields (for example Master + Current Level)
            // is structured data, not a prose body. Keep it intact for field translation.
            string normalizedBody = TranslationStore.Normalize(body);
            if (!stripped || normalizedBody.StartsWith("master level", StringComparison.Ordinal) ||
                normalizedBody.StartsWith("current level", StringComparison.Ordinal) ||
                normalizedBody.StartsWith("next level", StringComparison.Ordinal)) return source;
            return body;
        }

        private static int SkillStructuredBodyStart(string text)
        {
            string source = text ?? "";
            int prefixLength = SkillStructuredPrefixLength(source);
            if (prefixLength <= 0 || prefixLength >= source.Length) return 0;
            string body = StripSkillStructuredPrefix(source);
            if (String.Equals(body, source.Trim(), StringComparison.Ordinal)) return 0;
            string normalized = TranslationStore.Normalize(source);
            string normalizedBody = TranslationStore.Normalize(body);
            if (normalizedBody.Length == 0) return normalized.Length;
            int found = normalized.IndexOf(normalizedBody, StringComparison.Ordinal);
            return found >= 0 ? found : TranslationStore.Normalize(source.Substring(0, prefixLength)).Length;
        }

        private static string RepairOcrNumber(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return value.Replace('l', '1').Replace('I', '1').Replace('g', '9')
                .Replace('q', '9').Replace('O', '0').Replace('o', '0')
                .Replace('S', '5').Replace('s', '5').Replace('B', '8').Replace('b', '8');
        }

        // Windows OCR does not return clean text for these rows. Measured on a real quest
        // window it produced:
        //     "Level 11+"              -> "Level I I +"      (digits became letters, with a
        //                                                     space inserted between them)
        //     "Ayan's Toy Sword 1 / 1" -> "1 f 1"            (the slash became an f)
        //     "Henesys (1)"            -> "Henesys (l )"     (the 1 became a lowercase L)
        // A pattern that assumes clean digits therefore matches some rows and misses others
        // for no visible reason. Every structured pattern below is written to survive this.
        private const string OcrDigitRun = @"[0-9lIoOgqSsB]";
        private const string OcrDigitRunWithGaps = @"[0-9lIoOgqSsB\s]";
        // "/" is routinely read as f, t, r or a backslash.
        private const string OcrCounterSeparator = @"\s*[/fFtTrR\\|]\s*";

        // Collapse the gaps Windows OCR inserts inside a number ("I I" -> "11") and repair
        // the letter/digit confusion, so the value can be compared and displayed.
        private static string CompactOcrNumber(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            string compact = Regex.Replace(value,
                @"(?<=[0-9lIoOgqSsB])\s+(?=[0-9lIoOgqSsB])", "");
            return RepairOcrNumber(compact.Trim());
        }

        // Categories whose entries may legitimately be glued to a run-time number on a
        // quest panel heading or objective row. Player names and chat text never resolve
        // here, so a live counter can not make ordinary text look translatable.
        private static bool IsStructuredNameCategory(string category)
        {
            if (String.IsNullOrEmpty(category)) return false;
            return category.StartsWith("怀旧服-地图", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-任务组", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-任务#", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-怪物#", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-道具#", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-装备#", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-NPC#", StringComparison.Ordinal);
        }

        private bool TryResolveStructuredName(string name, out string chinese)
        {
            chinese = "";
            if (String.IsNullOrWhiteSpace(name)) return false;
            string trimmed = name.Trim(' ', '.', ':', '-', '*', '•', '·');
            if (trimmed.Length < 2) return false;
            TranslationEntry best = null;
            foreach (MatchResult match in translations.FindMatches(trimmed))
            {
                if (!IsStructuredNameCategory(match.Entry.Category)) continue;
                if (best == null || match.Entry.Normalized.Length > best.Normalized.Length)
                    best = match.Entry;
            }
            if (best == null) return false;
            chinese = best.Chinese;
            return true;
        }

        // The level gate, quest group headings and quest objectives all share one shape: a
        // dictionary-backed name glued to a number the game produces at run time
        // ("Level 13+", "Henesys (1)", "Ayan's Toy Sword 1 / 1", "Stump 30 / 30"). A
        // dictionary entry can pin only one fixed number, so listing them one by one can
        // never keep up with the game. Recognise the shape and translate only the name,
        // leaving the live number exactly as the game drew it.
        private bool TryTranslateNameWithLiveNumber(string text, out string translated)
        {
            translated = "";
            string source = (text ?? "").Trim();
            if (source.Length < 2 || source.Length > 120) return false;

            // "Level 11+" - OCR returns "Level I I +" for this row, so the digits must be
            // allowed to arrive as letter look-alikes with gaps inserted between them.
            Match levelOnly = Regex.Match(source,
                @"^level\s+(" + OcrDigitRunWithGaps + @"{1,6}?)\s*(\+?)$",
                RegexOptions.IgnoreCase);
            if (levelOnly.Success)
            {
                string level = CompactOcrNumber(levelOnly.Groups[1].Value);
                if (level.Length > 0)
                {
                    translated = "等级 " + level + levelOnly.Groups[2].Value;
                    return true;
                }
            }

            // "Henesys (1)" - a quest group heading. OCR returns "Henesys (l )".
            Match grouped = Regex.Match(source,
                @"^[•·*\-]?\s*(.{2,60}?)\s*\(\s*(" + OcrDigitRunWithGaps + @"{1,6}?)\s*\)$");
            string groupedName;
            if (grouped.Success &&
                TryResolveStructuredName(grouped.Groups[1].Value, out groupedName))
            {
                string count = CompactOcrNumber(grouped.Groups[2].Value);
                if (count.Length > 0)
                {
                    translated = groupedName + " (" + count + ")";
                    return true;
                }
            }

            // "Ayan's Toy Sword 1 / 1" - a quest objective with a live counter. OCR returns
            // "Ayan's Toy Sword 1 f 1" because it reads the slash as an f.
            Match nameFirst = Regex.Match(source,
                @"^[•·*\-]?\s*(.{2,60}?)\s+(" + OcrDigitRunWithGaps + @"{1,6}?)" +
                OcrCounterSeparator + @"(" + OcrDigitRunWithGaps + @"{1,6})$");
            string objectiveName;
            if (nameFirst.Success &&
                TryResolveStructuredName(nameFirst.Groups[1].Value, out objectiveName))
            {
                string current = CompactOcrNumber(nameFirst.Groups[2].Value);
                string target = CompactOcrNumber(nameFirst.Groups[3].Value);
                if (current.Length > 0 && target.Length > 0)
                {
                    translated = objectiveName + " " + current + " / " + target;
                    return true;
                }
            }

            return false;
        }

        private static string TranslateEquipmentType(string normalized)
        {
            string[,] values = new string[,] {
                { "two handed blunt weapon", "双手钝器" }, { "one handed blunt weapon", "单手钝器" },
                { "two handed sword", "双手剑" }, { "one handed sword", "单手剑" },
                { "crossbow", "弩" }, { "polearm", "战戟" }, { "knuckle", "指虎" },
                { "earrings", "耳环" }, { "overall", "套服" }, { "outfit", "套服" }, { "shield", "盾牌" },
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

            if (skillContext || normalized.Contains("master level") ||
                normalized.Contains("current level") || normalized.Contains("next level") ||
                normalized.Contains("required skill"))
            {
                List<string> skillFields = new List<string>();
                string skillValue;
                if (normalized.Contains("master level"))
                {
                    skillValue = SkillLevelNumber(text, normalized, "master level");
                    skillFields.Add("最高等级" + (skillValue.Length > 0 ? "：" + skillValue : ""));
                }
                if (normalized.Contains("current level"))
                {
                    skillValue = SkillLevelNumber(text, normalized, "current level");
                    skillFields.Add("当前等级" + (skillValue.Length > 0 ? "：" + skillValue : ""));
                }
                if (normalized.Contains("next level"))
                {
                    skillValue = SkillLevelNumber(text, normalized, "next level");
                    skillFields.Add("下一级" + (skillValue.Length > 0 ? "：" + skillValue : ""));
                }
                if (normalized.Contains("required skill"))
                {
                    string level = ExtractStructuredValue(normalized, @"(?:level)\s*(\d+)");
                    string skillName = "";
                    foreach (MatchResult match in translations.FindMatches(text))
                        if (match.Entry.Category.StartsWith("怀旧服-技能#", StringComparison.Ordinal))
                        { skillName = match.Entry.Chinese; break; }
                    skillFields.Add("需要技能：" + (skillName.Length > 0 ? skillName : "前置技能") +
                        (level.Length > 0 ? "至少" + level + "级" : ""));
                }
                if (skillFields.Count == 0) return false;
                translated = String.Join("　", skillFields.ToArray());
                return true;
            }


            // Item-expiry timestamps are generated at run time.  Translate only the
            // stable field name and preserve the OCR value verbatim; a fixed glossary
            // row would otherwise either drop the date or have to enumerate dates.
            Match usableUntil = equipmentContext ? Regex.Match(text ?? "",
                @"\busable\s+until\s*[:：]?\s*(?<expiry>.+?\b(?:UTC|GMT)\b)",
                RegexOptions.IgnoreCase) : Match.Empty;
            if (usableUntil.Success)
            {
                string liveExpiry = usableUntil.Groups["expiry"].Value.Trim();
                if (liveExpiry.Length > 0)
                {
                    translated = "可使用至：" + liveExpiry;
                    return true;
                }
            }
            List<string> fields = new List<string>();
            string value;

            // The level gate, the quest group headings and the quest objectives all share
            // one shape: a dictionary-backed name glued to a number the game generates at
            // run time ("Level 13+", "Henesys (1)", "Ayan's Toy Sword 1 / 1", "Stump 30 / 30").
            // A dictionary entry can pin only one fixed number, so enumerating them can never
            // keep up. Recognise the shape instead: translate the name, keep the live number.
            string structuredName;
            if (TryTranslateNameWithLiveNumber(text, out structuredName))
                fields.Add(structuredName);

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
                HasExplicitMagicValue(normalized))
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

        private bool TryAddQuestObjectiveNameLabel(List<OverlayLabel> output,
            OcrLine line, float ocrScale)
        {
            if (line == null || String.IsNullOrWhiteSpace(line.Text)) return false;
            // Windows OCR routinely drops the slash in a Quest Summary counter and leaves
            // "33 30" behind (citizenship donation screenshot). Accept a whitespace split as
            // well: the name/counter boundary stays unambiguous because this method only
            // paints when the remaining text resolves to a dictionary item or monster.
            const string counter = @"[\dlIoO]+(?:\s*/\s*|\s+)[\dlIoO]+";
            bool counterFirst = Regex.IsMatch(line.Text,
                @"^\s*[•·*\-]?\s*" + counter + @"\s+.{2,80}?\s*$",
                RegexOptions.IgnoreCase);
            bool nameFirst = Regex.IsMatch(line.Text,
                @"^\s*[•·*\-]?\s*.{2,80}?\s+" + counter + @"\s*$",
                RegexOptions.IgnoreCase);
            if (!counterFirst && !nameFirst) return false;

            List<MatchResult> objectives = translations.FindMatches(line.Text);
            objectives.RemoveAll(delegate(MatchResult match) {
                return !(match.Entry.Category.StartsWith("怀旧服-怪物#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-道具#", StringComparison.Ordinal) ||
                    match.Entry.Category.StartsWith("怀旧服-装备#", StringComparison.Ordinal));
            });
            if (objectives.Count == 0) return false;

            // Paint only the dictionary-backed objective name. The live x/y counter belongs
            // to the game and stays visible, so progress changes never require a new overlay.
            int beforeLabel = output.Count;
            AddExactLabels(output, new List<OcrLine> { line }, line.Text, objectives, ocrScale);
            Match counterMatch = Regex.Match(line.Text, counter);
            string normalized = TranslationStore.Normalize(line.Text);
            string counterText = TranslationStore.Normalize(counterMatch.Value);
            int counterStart = normalized.IndexOf(counterText, StringComparison.Ordinal);
            RectangleF counterBounds = GetOcrTextSpanBounds(new List<OcrLine> { line },
                line.Text, counterStart, counterText.Length);
            for (int i = beforeLabel; i < output.Count; i++)
            {
                output[i].SemanticRole = "task-objective";
                if (!counterBounds.IsEmpty) output[i].KeepClearBounds = new RectangleF(
                    ToOverlayX(counterBounds.Left, ocrScale), ToOverlayY(counterBounds.Top, ocrScale),
                    counterBounds.Width / ocrScale, counterBounds.Height / ocrScale);
            }
            return output.Count > beforeLabel;
        }

        private void AddWholeLineLabel(List<OverlayLabel> output, OcrLine line,
            string text, float ocrScale, bool wrap)
        {
            RectangleF raw = GetOcrLineBounds(line);
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            RectangleF bounds = new RectangleF(ToOverlayX(raw.Left, ocrScale) - 3,
                ToOverlayY(raw.Top, ocrScale) - 2,
                Math.Max(34, raw.Width / ocrScale + 6), Math.Max(18, raw.Height / ocrScale + 5));
            output.Add(new OverlayLabel {
                Bounds = bounds,
                SourceBounds = new RectangleF(ToOverlayX(raw.Left, ocrScale),
                    ToOverlayY(raw.Top, ocrScale),
                    Math.Max(1.0f, raw.Width / ocrScale), Math.Max(1.0f, raw.Height / ocrScale)),
                HasSourceBounds = true,
                Text = text, Wrap = wrap
            });
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
            foreach (List<OcrLine> row in BuildVisualRows(panel.Lines))
            {
                string rowText = CombineVisualRow(row);
                List<EquipmentField> fields = EquipmentFieldGrammar.Parse(rowText, true);
                if (fields.Count > 0)
                {
                    // The structured field path owns every attribute row. It replaces
                    // only the translated key and protects the measured value, so this
                    // whole-row translation must not erase the value first.
                    AddEquipmentRowPrefixLabel(output, row, rowText, fields[0].Start, ocrScale);
                    continue;
                }
                string translated;
                if (!TryTranslateStructuredLine(rowText, true, false, out translated)) continue;
                int beforeField = output.Count;
                AddDetailBlockLabel(output, row, translated, ocrScale);
                if (output.Count > beforeField) output[output.Count-1].SemanticRole = "item-field";
            }
            string itemId = translations.DetectItemId(panel.Text);
            if (itemId.Length == 0) return;
            List<OcrLine> prose = FindBestPanelProseBlock(panel.Lines);
            if (prose.Count == 0) return;
            string text = CombineVisualRow(prose), normalized = TranslationStore.Normalize(text);
            MatchResult best = null; int bestScore = Int32.MinValue;
            foreach (MatchResult candidate in translations.FindItemTextMatches(text,itemId))
            {
                int score = ScoreStrongLongTextCandidate(text,normalized,candidate,1.0f);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            // Stats and prose are independent. A single translated REQ/DEF row
            // cannot suppress the item's observed description, and an item name
            // alone cannot lend an unobserved longest overview to another panel.
            if (best == null) return;
            int beforeBody = output.Count;
            AddDetailBlockLabel(output,SelectBestDetailLines(prose,best.Entry),best.Entry.Chinese,ocrScale);
            if (output.Count > beforeBody) output[output.Count-1].SemanticRole = "item-body";
        }

        private void AddEquipmentRowPrefixLabel(List<OverlayLabel> output, List<OcrLine> row,
            string rowText, int firstFieldStart, float ocrScale)
        {
            if (firstFieldStart <= 0 || firstFieldStart > rowText.Length) return;
            string prefix = rowText.Substring(0, firstFieldStart).Trim();
            if (prefix.Length == 0) return;
            string translated;
            if (!TryTranslateStructuredLine(prefix, true, false, out translated)) return;
            int prefixLength = TranslationStore.Normalize(prefix).Length;
            if (prefixLength <= 0) return;
            RectangleF span = GetOcrTextSpanBounds(row, rowText, 0, prefixLength);
            if (span.IsEmpty) return;
            int before = output.Count;
            AddDetailBlockLabelAt(output, row, span, translated, ocrScale, false);
            if (output.Count > before)
            {
                OverlayLabel label = output[output.Count - 1];
                label.SemanticRole = "item-type";
                label.MatchSource = "equipment-prefix";
                // The prefix translation ("类型：帽子") must not grow into the
                // attribute columns that the structured plan replaces separately.
                // A per-label boundary is sufficient here; a global live-value guard
                // would wrongly block the field rows on the same OCR line.
                RectangleF prefixBox = OverlaySpan(span, ocrScale);
                label.PanelBounds = new RectangleF(prefixBox.Left - 2, prefixBox.Top - 2,
                    prefixBox.Width + 10, prefixBox.Height + 4);
            }
        }

        private static string SkillFieldRole(string text)
        {
            string key = TranslationStore.Normalize(text);
            if (key.Contains("current level")) return "skill-current-level";
            if (key.Contains("next level")) return "skill-next-level";
            if (key.Contains("master level")) return "skill-master-level";
            return "skill-field";
        }

        private void CancelPanelAiRequest()
        {
            CancellationTokenSource current = panelAiCancellation;
            if (current == null) return;
            try { current.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static string CombinePanelAiSource(List<OcrLine> prose)
        {
            if (prose == null || prose.Count == 0) return "";
            StringBuilder combined = new StringBuilder();
            foreach (OcrLine line in prose)
            {
                string text = (line == null ? "" : line.Text ?? "").Trim();
                if (text.Length == 0) continue;
                if (combined.Length > 0) combined.Append(' ');
                combined.Append(text);
            }
            return combined.ToString().Trim();

        }

        private bool IsPanelAiFallbackCandidate(PanelCropCandidate panelTarget,
            OcrResult panelResult, string source)
        {
            if (panelTarget == null || panelResult == null || String.IsNullOrWhiteSpace(source)) return false;
            // Settings, character/stat tables and chat are structured or user-authored text;
            // sending them to a prose translator would either waste the one-call budget or
            // risk changing names/numbers.  The local focused pass remains authoritative there.
            if (SettingsPanelPolicy.IsSettingsWindow(panelResult.Text)) return false;
            if (SceneClassifier.LooksLikePlayerChat(source)) return false;
            if (LooksLikeEquipmentStatText(source)) return false;
            string normalized = TranslationStore.Normalize(source);
            if (normalized.Length < 32) return false;
            int words = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words < 5) return false;
            if (normalized.Contains("character stat") || normalized.Contains("character info") ||
                normalized.Contains("key bindings") || normalized.Contains("keyboard mouse") ||
                normalized.Contains("quick slot")) return false;

            // A long exact/fuzzy dictionary body is already handled locally.  A short title or
            // item token inside a larger paragraph is not enough coverage and therefore does not
            // suppress this best-effort fallback.
            int longestKnown = 0;
            longestKnown = Math.Max(longestKnown,
                LongestPanelDictionaryMatch(translations.FindInterfaceTextMatches(source)));
            longestKnown = Math.Max(longestKnown,
                LongestPanelDictionaryMatch(translations.FindDialogueTextMatches(source)));
            longestKnown = Math.Max(longestKnown,
                LongestPanelDictionaryMatch(translations.FindSharedTaskBodyMatches(source)));
            longestKnown = Math.Max(longestKnown,
                LongestPanelDictionaryMatch(translations.FindSkillTextMatches(source)));
            longestKnown = Math.Max(longestKnown,
                LongestPanelDictionaryMatch(translations.FindItemTextMatches(source)));
            return longestKnown * 100 < normalized.Length * 68;
        }

        private static int LongestPanelDictionaryMatch(List<MatchResult> matches)
        {
            int longest = 0;
            if (matches == null) return longest;
            foreach (MatchResult match in matches)
            {
                if (match == null || match.Entry == null) continue;
                longest = Math.Max(longest, match.Entry.Normalized.Length);
            }
            return longest;
        }

        private async Task<OverlayLabel> BuildPanelAiFallbackLabelAsync(List<OcrLine> prose,
            string source, float ocrScale, int timeoutMs, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || String.IsNullOrWhiteSpace(source)) return null;
            OnlineAiSettings settings;
            try
            {
                settings = OnlineAiSettings.Load();
                if (settings == null || !settings.IsReady || !settings.PanelTranslationEnabled) return null;
            }
            catch { return null; }

            string cacheKey = BuildPanelAiCacheKey(settings, source);
            string cached = ReadPanelAiCache(cacheKey);
            if (!String.IsNullOrEmpty(cached))
                return CreatePanelAiLabel(prose, cached, ocrScale);

            string translated = "";
            try
            {
                // The linked source is what makes a second F8/HideTranslation abort the real
                // HttpWebRequest.  CancelAfter bounds provider stalls even if the request layer
                // reports its timeout a little late.
                using (CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(Math.Max(OnlineAiClient.PanelTimeoutMinMs,
                        Math.Min(OnlineAiClient.PanelTimeoutMaxMs, timeoutMs)));
                    translated = await OnlineAiClient.TranslatePanelAsync(settings, source,
                        timeoutMs, timeout.Token);
                }
            }
            catch { translated = ""; }
            if (cancellationToken.IsCancellationRequested ||
                !IsSafePanelAiTranslation(source, translated)) return null;
            WritePanelAiCache(cacheKey, translated);
            return CreatePanelAiLabel(prose, translated, ocrScale);
        }

        private static string BuildPanelAiCacheKey(OnlineAiSettings settings, string source)
        {
            return (settings.Endpoint ?? "").Trim().ToLowerInvariant() + "\n" +
                (settings.Model ?? "").Trim().ToLowerInvariant() + "\n" +
                (source ?? "").Trim();
        }

        private string ReadPanelAiCache(string key)
        {
            lock (panelAiCacheLock)
            {
                string value;
                return panelAiCache.TryGetValue(key, out value) ? value : "";
            }
        }

        private void WritePanelAiCache(string key, string value)
        {
            if (String.IsNullOrEmpty(key) || String.IsNullOrEmpty(value)) return;
            lock (panelAiCacheLock)
            {
                if (panelAiCache.Count >= PanelAiCacheCapacity)
                {
                    string oldest = null;
                    foreach (string existing in panelAiCache.Keys) { oldest = existing; break; }
                    if (oldest != null) panelAiCache.Remove(oldest);
                }
                panelAiCache[key] = value;
            }
        }

        private static bool IsSafePanelAiTranslation(string source, string translated)
        {
            if (String.IsNullOrWhiteSpace(translated) ||
                !Regex.IsMatch(translated, "[\\u4e00-\\u9fff]")) return false;
            if (translated.Length > 1600) return false;
            if (!TranslationValueSafety.PreservesNumbers(source, translated)) return false;
            return true;
        }

        private OverlayLabel CreatePanelAiLabel(List<OcrLine> prose, string translated,
            float ocrScale)
        {
            if (prose == null || prose.Count == 0 || String.IsNullOrWhiteSpace(translated)) return null;
            List<OverlayLabel> created = new List<OverlayLabel>();
            AddDetailBlockLabel(created, prose, translated, ocrScale);
            if (created.Count == 0) return null;
            OverlayLabel result = created[0];
            result.Confidence = LabelConfidence.Medium;
            result.ConfidenceReason = 0.68f;
            result.MatchSource = "ai-panel";
            return result;
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
                // The window that built this candidate is 12 lines; the detail span has to be
                // able to reach the end of it, otherwise the last rows of a wrapped quest body
                // are never part of the match and stay in English on the panel.
                for (int span = 1; span <= 12 && start + span <= source.Count; span++)
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
            if (bestScore == Int32.MinValue) return source;
            // A wrapped body is not complete until its last short continuation is included. The
            // dictionary entry does not repeat such a tail word for word (the screenshot case is
            // "he looks..."), so it is kept when it is contiguous and unmistakably prose - never
            // a counter, a button or an unrelated panel row.
            int end = bestStart + bestSpan;
            while (end < source.Count && end - bestStart < 12 &&
                CanJoinOcrLines(source[end - 1], source[end]) &&
                ContinuesDetailProse(source[end].Text,
                    TranslationStore.Normalize(source[end].Text ?? ""), entry.DetailWords,
                    DetailTailLength, DetailTailText(source, bestStart, end)))
                end++;
            return source.GetRange(bestStart, end - bestStart);
        }

        // Longest trailing row still treated as prose rather than as a panel control.
        private const int DetailTailLength = 24;

        // True when an OCR row continues the matched prose block instead of being a nearby
        // counter, button, structured field or panel title. A complete continuation must reuse a
        // word of the matched entry. A truncated tail ("he looks...") additionally has to read as
        // prose - two or more words, lowercase, no capitalised word after the first character -
        // and to carry ordered continuation evidence against the rows already selected, which is
        // what separates it from "Accept...", "Next Level...", "Citizen's R..." and
        // "Winston's F...".
        internal static bool ContinuesDetailProse(string rawText, string normalizedText,
            HashSet<string> detailWords, int maximumCharacters, string previousTail)
        {
            if (String.IsNullOrEmpty(normalizedText)) return false;
            if (normalizedText.Length > maximumCharacters) return false;
            string raw = (rawText ?? "").TrimEnd();
            string[] words = normalizedText.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return false;
            // Counters and structured fields never continue prose.
            if (raw.IndexOf('/') >= 0 || raw.EndsWith(":", StringComparison.Ordinal)) return false;
            for (int i = 0; i < words.Length; i++)
            {
                foreach (char c in words[i]) if (Char.IsDigit(c)) return false;
                if (i == 0 && IsPanelControlWord(words[0])) return false;
            }
            bool truncated = raw.EndsWith("...", StringComparison.Ordinal) ||
                raw.EndsWith("…", StringComparison.Ordinal);
            bool entryEvidence = false;
            if (detailWords != null)
            {
                foreach (string word in words)
                    if (detailWords.Contains(word)) { entryEvidence = true; break; }
            }
            if (entryEvidence && !truncated) return true;
            if (!truncated) return false;
            if (words.Length < 2) return false;
            string body = raw.TrimStart();
            if (body.Length == 0 || !Char.IsLower(body[0])) return false;
            for (int i = 1; i < body.Length; i++)
                if (Char.IsUpper(body[i])) return false;
            // Ordered continuation evidence. One shared word is not evidence - "show quest..."
            // shares "quest" with almost every quest panel - so the row must either repeat two
            // consecutive words of the selected body tail, or reuse at least two words of the
            // entry/tail vocabulary.
            string tail = TranslationStore.Normalize(previousTail ?? "");
            string[] tailWords = tail.Length == 0
                ? new string[0]
                : tail.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < words.Length; i++)
            {
                for (int j = 0; j + 1 < tailWords.Length; j++)
                {
                    if (String.Equals(words[i], tailWords[j], StringComparison.Ordinal) &&
                        String.Equals(words[i + 1], tailWords[j + 1], StringComparison.Ordinal))
                        return true;
                }
            }
            int shared = 0;
            foreach (string word in words)
            {
                bool known = detailWords != null && detailWords.Contains(word);
                if (!known)
                {
                    foreach (string previous in tailWords)
                    {
                        if (String.Equals(word, previous, StringComparison.Ordinal))
                        { known = true; break; }
                    }
                }
                if (known) shared++;
            }
            return shared >= 2;
        }

        // Words that begin a panel control or a structured field, never a sentence of prose.
        private static bool IsPanelControlWord(string word)
        {
            switch (word)
            {
                case "accept": case "ok": case "cancel": case "close": case "forfeit":
                case "details": case "detail": case "next": case "menu": case "all":
                case "shortcut": case "yes": case "no": case "back": case "quest":
                case "helper": case "level": case "exp": case "cash": case "shop":
                case "citizen": case "citizenship":
                    return true;
                default:
                    return false;
            }
        }

        // Ordered text of the rows selected so far, used as the continuation evidence for a
        // truncated tail row such as "he looks...".
        private static string DetailTailText(List<OcrLine> source, int start, int end)
        {
            StringBuilder tail = new StringBuilder();
            for (int i = start; i < end && i < source.Count; i++)
            {
                if (source[i] == null || String.IsNullOrEmpty(source[i].Text)) continue;
                if (tail.Length > 0) tail.Append(' ');
                tail.Append(source[i].Text);
            }
            return tail.ToString();
        }

        private void AddDetailBlockLabel(List<OverlayLabel> output, List<OcrLine> lines,
            string text, float ocrScale)
        {
            RectangleF raw = GetCombinedLineBounds(lines);
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            output.Add(new OverlayLabel {
                Bounds = new RectangleF(ToOverlayX(raw.Left, ocrScale) - 3,
                    ToOverlayY(raw.Top, ocrScale) - 2,
                    Math.Max(100, raw.Width / ocrScale + 6), Math.Max(20, raw.Height / ocrScale + 5)),
                // The erase region is the measured OCR block itself; Bounds above is slightly
                // wider for legibility. Without this a detail row could never use Seamless.
                SourceBounds = new RectangleF(ToOverlayX(raw.Left, ocrScale),
                    ToOverlayY(raw.Top, ocrScale),
                    Math.Max(1.0f, raw.Width / ocrScale), Math.Max(1.0f, raw.Height / ocrScale)),
                HasSourceBounds = true,
                // Preserve the source layout contract: a one-line game field stays one line.
                // GDI+ may widen it when the Chinese text needs a few more pixels, but it must
                // not grow into the row above/below. Only a genuinely multi-line OCR block may
                // wrap and consume multiple rows.
                Text = text, Wrap = lines.Count > 1,
                // Every original OCR row, so the erase pass can clear each row on its own
                // instead of filling the rectangle that spans the gaps between them.
                SourceLineBounds = ToOverlayLineBounds(lines, ocrScale,
                    new RectangleF(ToOverlayX(raw.Left, ocrScale), ToOverlayY(raw.Top, ocrScale),
                        Math.Max(1.0f, raw.Width / ocrScale), Math.Max(1.0f, raw.Height / ocrScale)))
            });
        }

        // Per-row overlay rectangles for a measured multi-line OCR block. Every row is clipped to
        // the label's measured span, so a row that shares an OCR line with a structured prefix
        // ("Master Level : 3") can never erase that prefix.
        private List<RectangleF> ToOverlayLineBounds(List<OcrLine> lines, float ocrScale,
            RectangleF span)
        {
            List<RectangleF> bounds = new List<RectangleF>();
            if (lines == null) return bounds;
            foreach (OcrLine line in lines)
            {
                RectangleF box = GetOcrLineBounds(line);
                if (box.IsEmpty) continue;
                bounds.Add(new RectangleF(ToOverlayX(box.Left, ocrScale),
                    ToOverlayY(box.Top, ocrScale),
                    Math.Max(1.0f, box.Width / ocrScale),
                    Math.Max(1.0f, box.Height / ocrScale)));
            }
            return OverlayPaintSafety.ClampRowsToSpan(bounds, span);
        }

        private void AddDetailBlockLabelAt(List<OverlayLabel> output, RectangleF raw,
            string text, float ocrScale, bool wrap)
        {
            AddDetailBlockLabelAt(output, null, raw, text, ocrScale, wrap);
        }

        // Same builder, but it also keeps the rectangle of every OCR row that was consumed. Every
        // multi-line builder has to populate that list: without it the erase pass falls back to one
        // union rectangle and the layout cannot tell the original rows apart.
        private void AddDetailBlockLabelAt(List<OverlayLabel> output, List<OcrLine> lines,
            RectangleF raw, string text, float ocrScale, bool wrap)
        {
            if (raw.IsEmpty || String.IsNullOrEmpty(text)) return;
            output.Add(new OverlayLabel {
                Bounds = new RectangleF(ToOverlayX(raw.Left, ocrScale) - 3,
                    ToOverlayY(raw.Top, ocrScale) - 2,
                    Math.Max(100, raw.Width / ocrScale + 6), Math.Max(20, raw.Height / ocrScale + 5)),
                SourceBounds = new RectangleF(ToOverlayX(raw.Left, ocrScale),
                    ToOverlayY(raw.Top, ocrScale),
                    Math.Max(1.0f, raw.Width / ocrScale), Math.Max(1.0f, raw.Height / ocrScale)),
                HasSourceBounds = true,
                Text = text, Wrap = wrap, SemanticRole = "detail-body",
                SourceLineBounds = ToOverlayLineBounds(lines, ocrScale,
                    new RectangleF(ToOverlayX(raw.Left, ocrScale), ToOverlayY(raw.Top, ocrScale),
                        Math.Max(1.0f, raw.Width / ocrScale), Math.Max(1.0f, raw.Height / ocrScale)))
            });
        }

        // The early first-paint snapshot is request-local: it may only reinforce the frame that
        // produced it. Re-adding a previous frame's objects to a frame that no longer shows those
        // rows is what made stale English reappear after the panel changed.


        // One merge path for labels that describe the same translation. It unions the layout box
        // and the measured source box and merges the ordered per-row source rectangles, so a merge
        // can never lose the geometry the erase pass and the fit contract depend on. The old plain
        // Bounds union left SourceBounds stale and collapsed SourceLineBounds, which broke both.
        private static string GetEntrySemanticRole(TranslationEntry entry)
        {
            string category = entry.Category ?? "";
            if (entry.IsTaskName || category.StartsWith("怀旧服-任务标题", StringComparison.Ordinal))
                return "task-name";
            if (entry.IsTaskText) return "task-body";
            if (category.StartsWith("怀旧服-技能#", StringComparison.Ordinal)) return "skill-name";
            if (entry.IsSkillText) return "skill-body";
            if (entry.IsItemText) return "item-body";
            if (category.StartsWith("怀旧服-装备#",StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-道具#",StringComparison.Ordinal)) return "item-name";
            // This legacy dictionary bucket includes prose and level gates, not objective rows.
            // Actual item/counter rows receive task-objective at their geometry-aware builder.
            if (entry.IsQuestTarget) return (entry.Normalized ?? "").Length >= 24 ? "task-body" : "task-gate";
            if (category.StartsWith("怀旧服-界面", StringComparison.Ordinal) ||
                category.StartsWith("怀旧服-任务组", StringComparison.Ordinal) || entry.IsSettingsText)
                return "interface";
            return "";
        }

        private static bool CanMergeSemanticLabels(OverlayLabel left, OverlayLabel right)
        {
            return SceneTranslationPolicy.CanMergeSemanticBlocks(left.SemanticRole, right.SemanticRole,
                left.HasSourceBounds ? left.SourceBounds : left.Bounds,
                right.HasSourceBounds ? right.SourceBounds : right.Bounds);
        }

        internal static void MergeLabelInto(OverlayLabel target, OverlayLabel source)
        {
            if (target == null || source == null) return;
            if (String.IsNullOrEmpty(target.SemanticRole)) target.SemanticRole = source.SemanticRole;
            if (target.KeepClearBounds.IsEmpty) target.KeepClearBounds = source.KeepClearBounds;
            target.Bounds = RectangleF.Union(target.Bounds, source.Bounds);
            if (source.HasSourceBounds && OverlayPaintSafety.IsFiniteRect(source.SourceBounds))
            {
                target.SourceBounds = target.HasSourceBounds &&
                    OverlayPaintSafety.IsFiniteRect(target.SourceBounds)
                    ? RectangleF.Union(target.SourceBounds, source.SourceBounds)
                    : source.SourceBounds;
                target.HasSourceBounds = true;
            }
            target.SourceLineBounds = MergeSourceLines(target.SourceLineBounds,
                source.SourceLineBounds);
            target.Wrap = target.Wrap || source.Wrap;
            target.StableLayout = target.StableLayout || source.StableLayout;
            // Keep the stronger evidence instead of dropping it with the merged label.
            if ((int)source.Confidence > (int)target.Confidence)
            {
                target.Confidence = source.Confidence;
                target.ConfidenceReason = source.ConfidenceReason;
                target.MatchSource = source.MatchSource;
            }
            else if ((int)source.Confidence == (int)target.Confidence &&
                source.ConfidenceReason > target.ConfidenceReason)
            {
                // Same level: the stronger reason wins, otherwise a merge can silently weaken the
                // evidence behind a label that is already on screen.
                target.ConfidenceReason = source.ConfidenceReason;
            }
            if (String.IsNullOrEmpty(target.MatchSource)) target.MatchSource = source.MatchSource;
            if (target.BackgroundColor == Color.Empty) target.BackgroundColor = source.BackgroundColor;
            if (target.TextColor == Color.Empty) target.TextColor = source.TextColor;
            if (target.RenderMode == LabelRenderMode.CardFallback)
                target.RenderMode = source.RenderMode;
        }

        // Ordered, de-duplicated union of the per-row source rectangles of two labels.
        internal static List<RectangleF> MergeSourceLines(List<RectangleF> first,
            List<RectangleF> second)
        {
            List<RectangleF> merged = new List<RectangleF>();
            AppendSourceLines(merged, first);
            AppendSourceLines(merged, second);
            merged.Sort(delegate(RectangleF left, RectangleF right) {
                if (Math.Abs(left.Top - right.Top) > 1.0f) return left.Top.CompareTo(right.Top);
                return left.Left.CompareTo(right.Left);
            });
            return merged;
        }

        private static void AppendSourceLines(List<RectangleF> target, List<RectangleF> lines)
        {
            if (lines == null) return;
            foreach (RectangleF line in lines)
            {
                if (!OverlayPaintSafety.IsFiniteRect(line) ||
                    line.Width <= 0.0f || line.Height <= 0.0f) continue;
                bool duplicate = false;
                foreach (RectangleF existing in target)
                {
                    // Same row, same span: both partners observed the same OCR row.
                    if (Math.Abs(existing.Top - line.Top) <= 1.0f &&
                        Math.Abs(existing.Left - line.Left) <= 1.0f &&
                        Math.Abs(existing.Width - line.Width) <= 1.0f &&
                        Math.Abs(existing.Height - line.Height) <= 1.0f)
                    { duplicate = true; break; }
                }
                if (!duplicate) target.Add(line);
            }
        }

        private static void MergeAdjacentLongLabels(List<OverlayLabel> labels)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i].Text.Length < 16) continue;
                for (int j = labels.Count - 1; j > i; j--)
                {
                    if (!CanMergeSemanticLabels(labels[i], labels[j])) continue;
                    if (!SameOverlayText(labels[j].Text, labels[i].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    float horizontalOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    float verticalGap = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
                    if (horizontalOverlap < Math.Min(a.Width, b.Width) * 0.35f || verticalGap > 12) continue;
                    MergeLabelInto(labels[i], labels[j]);
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
                    if (!CanMergeSemanticLabels(labels[i], labels[j])) continue;
                    if (!SameOverlayText(labels[j].Text, labels[i].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    RectangleF overlap = RectangleF.Intersect(a, b);
                    float horizontalOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    float verticalGap = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
                    bool overlapping = overlap.Width > 0 && overlap.Height > 0;
                    bool adjacentRows = horizontalOverlap >= Math.Min(a.Width, b.Width) * 0.35f &&
                        verticalGap <= 12.0f;
                    if (!overlapping && !adjacentRows) continue;
                    MergeLabelInto(labels[i], labels[j]);
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
                    if (!CanMergeSemanticLabels(labels[i], labels[j])) continue;
                    if (!SameOverlayText(labels[i].Text, labels[j].Text)) continue;
                    RectangleF a = labels[i].Bounds, b = labels[j].Bounds;
                    RectangleF overlap = RectangleF.Intersect(a, b);
                    float smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
                    if (smaller <= 0 || overlap.Width * overlap.Height / smaller < 0.35f) continue;
                    MergeLabelInto(labels[i], labels[j]);
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
                    if (!CanMergeSemanticLabels(labels[i], labels[j])) continue;
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

        private static void RepairPlayerContextMenuLabels(List<OverlayLabel> labels)
        {
            if (labels == null || labels.Count == 0) return;
            string[] menu = new string[] { "角色信息", "保存名字", "交易请求", "悄悄话",
                "邀请聊天", "加为好友", "加入黑名单", "举报" };
            OverlayLabel first = null, last = null;
            int evidence = 0;
            foreach (OverlayLabel label in labels)
            {
                int index = Array.IndexOf(menu, label.Text);
                if (index < 0) continue;
                evidence++;
                if (index == 0) first = label;
                if (index == menu.Length - 1) last = label;
            }
            if (evidence < 5 || first == null || last == null) return;
            float pitch = (last.Bounds.Top - first.Bounds.Top) / 7.0f;
            if (pitch < 18.0f || pitch > 32.0f) return;
            float width = Math.Max(120.0f, Math.Min(170.0f, first.Bounds.Width));
            float height = Math.Max(18.0f, Math.Min(23.0f, first.Bounds.Height));
            for (int i = 0; i < menu.Length; i++)
                AddLayoutLabel(labels, menu[i], new RectangleF(first.Bounds.Left,
                    first.Bounds.Top + pitch * i, width, height));
        }

        private Rectangle GetChatExclusionBounds()
        {
            if(imageTestOnly)return Rectangle.Empty;
            Rectangle resolved = ChatRegionSettings.ResolveForGame(gameBounds);
            // Resolve from the unchanged canonical selection. Synchronizing the consumer
            // must never turn rounded projections into new user settings.
            if (ChatRegionSettings.HasSelection() && ChatRegionSettings.LoadAbsolute() != resolved)
            {
                if (chatTranslator != null && !chatTranslator.IsDisposed)
                    chatTranslator.UpdateChatRegion(resolved, ChatRegionSettings.GetOrigin());
            }
            return resolved;
        }

        // ---------------------------------------------------------------------------
        // Lock layer plumbing (v4.0)
        // ---------------------------------------------------------------------------

        // Called from the poll timer and from rebind events, both of which may arrive on a
        // pool thread; the notice path touches the tray icon, so it is marshalled to the UI
        // thread when a handle already exists.
        private static int[] SampleScenePatch(Bitmap bitmap, Rectangle area)
        {
            int[] pixels = new int[24 * 12];
            for (int y = 0; y < 12; y++) for (int x = 0; x < 24; x++)
                pixels[y * 24 + x] = bitmap.GetPixel(area.Left + x * (area.Width - 1) / 23,
                    area.Top + y * (area.Height - 1) / 11).ToArgb();
            return pixels;
        }

        private async Task<bool> RememberDisplayedSceneAsync(Bitmap bitmap, Rectangle screen, List<OverlayLabel> source)
        {
            List<ScenePatch> stagedScene = new List<ScenePatch>();
            foreach (OverlayLabel label in source)
            {
                RectangleF span = label.HasSourceBounds ? label.SourceBounds : label.Bounds;
                if (!OverlayPaintSafety.IsFiniteRect(span)) continue;
                Rectangle area = Rectangle.Ceiling(span);
                area.Offset(Bounds.Left - screen.Left, Bounds.Top - screen.Top);
                area.Inflate(3, 3);
                area = Rectangle.Intersect(area, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (area.Width < 16 || area.Height < 8) continue;
                stagedScene.Add(new ScenePatch { Area = area, Pixels = SampleScenePatch(bitmap, area) });
            }
            if (!await PrepareSimpleFusionAsync(bitmap, screen, source)) return false;
            displayedScene.Clear(); displayedScene.AddRange(stagedScene);
            changedSceneChecks = 0; displayedSceneRevision++;
            return true;
        }

        private Task<WindowCaptureFrame> CaptureSceneFrameWithoutOverlayAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // HWND-targeted compositor frames exclude our UI. Never clear/hide/repaint
            // the visible translation merely to validate its source on the 600ms timer.
            return mapleWindows.CaptureClientBitmapAsync(cancellationToken);
        }

        private async Task RetireChangedSceneAsync(bool immediate)
        {
            if (sceneCheckBusy)
            {
                if (!immediate) return;
                while (sceneCheckBusy && !shuttingDown) await Task.Delay(10);
                if (shuttingDown || !visibleTranslation) return;
            }
            if (Program.Benchmark || shuttingDown || !visibleTranslation ||
                (!immediate && processing) || displayedScene.Count == 0) return;
            sceneCheckBusy = true;
            int generation = recognitionGeneration;
            int revision = displayedSceneRevision;
            ScenePatch[] evidence = displayedScene.ToArray();
            try
            {
                using (WindowCaptureFrame current = await CaptureSceneFrameWithoutOverlayAsync())
                {
                    if (generation != recognitionGeneration || revision != displayedSceneRevision || !visibleTranslation) return;
                    bool changed = false;
                    foreach (ScenePatch patch in evidence)
                    {
                        if (Rectangle.Intersect(patch.Area, new Rectangle(System.Drawing.Point.Empty, current.Bitmap.Size)) != patch.Area ||
                            SceneTranslationPolicy.PatchChanged(patch.Pixels, SampleScenePatch(current.Bitmap, patch.Area)))
                        { changed = true; break; }
                    }
                    changedSceneChecks = changed ? changedSceneChecks + 1 : 0;
                    if (changed && (immediate || changedSceneChecks >= 2))
                    {
                        HideTranslation();
                        SetInteractionStatus("游戏面板已变化，旧译文已清除；按 F8 识别当前界面。");
                        Program.LogRuntimeEvent("overlay-scene-retired", "generation=" + generation + " reason=source-pixels-changed");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A frame whose identity/visibility cannot be proved may not retain old text.
                if (generation == recognitionGeneration && revision == displayedSceneRevision && !shuttingDown)
                {
                    HideTranslation();
                    Program.LogRuntimeEvent("overlay-scene-retired", "generation=" + generation + " reason=" + ex.GetType().Name);
                }
            }
            finally { sceneCheckBusy = false; }
        }

        private void PollMapleWindow()
        {
            if (IsHandleCreated && InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(PollMapleWindowCore)); }
                // InvalidOperationException already covers ObjectDisposedException (a derived
                // type), so a single clause keeps the process alive across shutdown.
                catch (InvalidOperationException) { }
                return;
            }
            PollMapleWindowCore();
        }

        private void PollMapleWindowCore()
        {
            mapleWindows.Poll();
            string notice = mapleWindows.Diagnostic;
            if (notice == lastLockNotice) return;
            lastLockNotice = notice;
            if (Program.Benchmark) return;
            if (mapleWindows.State == MapleWindowState.Locked)
            {
                tray.ShowBalloonTip(1800, "枫语幕已锁定游戏窗口",
                    notice + "；仅在锁定窗口内识别，不再扫描整个桌面。", ToolTipIcon.Info);
            }
            else if (mapleWindows.State == MapleWindowState.FullScreen)
            {
                tray.ShowBalloonTip(2600, "请切换为窗口模式", notice, ToolTipIcon.Warning);
            }
        }

        // Why the player must not answer a dialog: the game is in the foreground while
        // translating, and a modal MessageBox steals focus out of it. A translate-time problem
        // is therefore drawn on the transparent overlay for a few seconds and disappears on
        // its own, so the player never has to click anything mid-game.
        private void NotifyLockProblem(string message, bool warning)
        {
            SetInteractionStatus(message);
            Program.LogRuntimeEvent("capture-blocked", "state=" + mapleWindows.State +
                " candidates=" + mapleWindows.CandidateCount + " version=" + mapleWindows.GeometryVersion);
            try
            {
                if (Program.Benchmark) return;
                // The tray balloon is the primary channel, not a fallback: a full-screen
                // game covers the overlay, and then a refused translate reads as "F8 does
                // nothing at all". The on-screen notice below is the secondary cue.
                tray.ShowBalloonTip(warning ? 3800 : 2800, "枫语幕无法翻译", message,
                    warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
                if (Program.BenchmarkUi)
                {
                    tray.ShowBalloonTip(2600, "枫语幕无法翻译", message,
                        warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
                    return;
                }
                lockNoticeText = message;
                lockNoticeUntilUtc = DateTime.UtcNow.AddSeconds(3.5);
                if (lockNoticeTimer == null)
                {
                    lockNoticeTimer = new System.Windows.Forms.Timer();
                    lockNoticeTimer.Interval = 600;
                    lockNoticeTimer.Tick += delegate
                    {
                        if (lockNoticeText.Length == 0) { lockNoticeTimer.Stop(); return; }
                        if (DateTime.UtcNow < lockNoticeUntilUtc) return;
                        lockNoticeText = "";
                        lockNoticeTimer.Stop();
                        Invalidate();
                    };
                }
                lockNoticeTimer.Start();
                Invalidate();
            }
            catch (InvalidOperationException) { }
        }

        private void ShowWindowScanIndicator()
        {
            if (Program.Benchmark || shuttingDown) return;
            // Capture has already finished. This is one subtle whole-client pulse, not a scan.
            Opacity = 0.005;
            scanIndicatorVisible = true;
            scanIndicatorStartedUtc = DateTime.UtcNow;
            if (scanIndicatorTimer == null)
            {
                scanIndicatorTimer = new System.Windows.Forms.Timer { Interval = 32 };
                scanIndicatorTimer.Tick += delegate {
                    if (!scanIndicatorVisible || shuttingDown) { HideWindowScanIndicator(); return; }
                    double elapsed = (DateTime.UtcNow - scanIndicatorStartedUtc).TotalMilliseconds;
                    if (elapsed >= 320) { HideWindowScanIndicator(); return; }
                    Opacity = Math.Max(0.005, SceneTranslationPolicy.PulseOpacity(elapsed));
                };
            }
            scanIndicatorTimer.Start(); Invalidate(); Update();
        }

        private void HideWindowScanIndicator()
        {
            if (!scanIndicatorVisible) return;
            scanIndicatorVisible = false;
            if (scanIndicatorTimer != null) scanIndicatorTimer.Stop();
            if (!IsDisposed && IsHandleCreated)
            {
                // Clear the tinted surface before restoring opacity, avoiding a white flash.
                Invalidate(); Update(); Opacity = 1.0;
            }
        }

        // The client rectangle is the single capture source for every feature. When no
        // windowed client is locked there is deliberately NO fallback to a desktop-wide scan:
        // that is what produced cross-window contamination before v4.0.
        private bool ResolveCaptureBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (Program.Benchmark) { bounds = Bounds; return true; }

            Rectangle client;
            bool fullScreen;
            if (mapleWindows.TryGetCaptureBounds(out client, out fullScreen) &&
                client.Width >= 160 && client.Height >= 120)
            {
                captureBounds = client;
                gameBounds = client;
                bounds = client;
                return true;
            }

            if (fullScreen)
            {
                if (!visibleTranslation)
                {
                    visibleTranslation = false;
                    labels.Clear();
                    Invalidate();
                }
                NotifyLockProblem("已将 MapleStory 识别为全屏模式，无法可靠截取。" +
                    Environment.NewLine + "请切换为窗口模式后再按翻译键。", true);
                return false;
            }

            // One immediate re-probe: the client may have been started after the last poll.
            mapleWindows.Refresh();
            if (mapleWindows.TryGetCaptureBounds(out client, out fullScreen) &&
                client.Width >= 160 && client.Height >= 120)
            {
                captureBounds = client;
                gameBounds = client;
                bounds = client;
                return true;
            }

            if (!visibleTranslation)
            {
                visibleTranslation = false;
                labels.Clear();
                Invalidate();
            }
            Program.LogRuntimeEvent("capture-refused", "state=" + mapleWindows.State);
            SetInteractionStatus("未锁定游戏窗口；维护期间请在主界面点击“打开图片测试”。");
            NotifyLockProblem(mapleWindows.Diagnostic + "；测试图片请使用主界面的打开图片测试。", false);
            return false;
        }

        // Runs in every real (non-benchmark) session. The lock layer is what the translate
        // hotkey depends on, and it is intentionally the only idle-time work.
        private void UpdateMapleWindowPolling()
        {
            if (Program.Benchmark || Program.BenchmarkUi) mapleWindowTimer.Stop();
            else if (!mapleWindowTimer.Enabled) mapleWindowTimer.Start();
        }

        // Every OCR rectangle is expressed in the coordinates of the frame handed to the
        // recogniser, which the pipeline renders at ocrScale. Turning one into overlay
        // coordinates used to be written out inline in 28 places, with the -3/-2 cosmetic
        // offsets copy-pasted alongside. These two helpers are the single definition; the
        // offsets stay explicit at each call site because they differ per surface.
        private void RememberCaptureTextRow(RectangleF raw, float scale)
        {
            if (scale <= 0 || raw.Width <= 0 || raw.Height <= 0) return;
            RectangleF box = new RectangleF(ToOverlayX(raw.Left, scale), ToOverlayY(raw.Top, scale),
                raw.Width / scale, raw.Height / scale);
            if (captureTextRows.Contains(box)) return;
            if (captureTextRows.Count >= 2048) { captureTextRowsOverflow = true; return; }
            captureTextRows.Add(box);
        }

        private float ToOverlayX(float ocrX, float ocrScale)
        {
            return ocrX / ocrScale + captureBounds.Left - Bounds.Left;
        }

        private float ToOverlayY(float ocrY, float ocrScale)
        {
            return ocrY / ocrScale + captureBounds.Top - Bounds.Top;
        }

        private bool IsChatLine(OcrLine line, float ocrScale, Rectangle exclusion)
        {
            if (exclusion.Width <= 0 || exclusion.Height <= 0 || line.Words.Count == 0) return false;
            float left = Single.MaxValue, top = Single.MaxValue;
            float right = Single.MinValue, bottom = Single.MinValue;
            foreach (OcrWord word in line.Words)
            {
                left = Math.Min(left, ToOverlayX((float)word.BoundingRect.X, ocrScale) + Bounds.Left);
                top = Math.Min(top, ToOverlayY((float)word.BoundingRect.Y, ocrScale) + Bounds.Top);
                right = Math.Max(right, ToOverlayX((float)(word.BoundingRect.X + word.BoundingRect.Width), ocrScale) + Bounds.Left);
                bottom = Math.Max(bottom, ToOverlayY((float)(word.BoundingRect.Y + word.BoundingRect.Height), ocrScale) + Bounds.Top);
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

        private static RectangleF GetOcrTextSpanBounds(List<OcrLine> lines,
            string combinedText, int matchStart, int matchLength)
        {
            if (lines == null || lines.Count == 0 || String.IsNullOrEmpty(combinedText) ||
                matchLength <= 0) return RectangleF.Empty;
            string normalizedLine = TranslationStore.Normalize(combinedText);
            if (normalizedLine.Length == 0) return RectangleF.Empty;
            int start = Math.Max(0, Math.Min(matchStart, normalizedLine.Length));
            int end = Math.Min(normalizedLine.Length, start + matchLength);
            if (end <= start) return RectangleF.Empty;

            float x0 = Single.MaxValue, y0 = Single.MaxValue;
            float x1 = Single.MinValue, y1 = Single.MinValue;
            int cursor = 0;
            foreach (OcrLine line in lines)
            {
                if (line == null || line.Words == null) continue;
                foreach (OcrWord word in line.Words)
                {
                    string normalizedWord = TranslationStore.Normalize(word.Text);
                    if (normalizedWord.Length == 0) continue;
                    int safeCursor = Math.Min(cursor, normalizedLine.Length);
                    int found = safeCursor < normalizedLine.Length
                        ? normalizedLine.IndexOf(normalizedWord, safeCursor,
                            StringComparison.Ordinal) : -1;
                    if (found < 0) found = safeCursor;
                    int wordEnd = found + normalizedWord.Length;
                    if (wordEnd > start && found < end)
                    {
                        x0 = Math.Min(x0, (float)word.BoundingRect.X);
                        y0 = Math.Min(y0, (float)word.BoundingRect.Y);
                        x1 = Math.Max(x1, (float)(word.BoundingRect.X + word.BoundingRect.Width));
                        y1 = Math.Max(y1, (float)(word.BoundingRect.Y + word.BoundingRect.Height));
                    }
                    cursor = Math.Min(normalizedLine.Length, wordEnd + 1);
                }
            }
            return x0 == Single.MaxValue ? RectangleF.Empty : RectangleF.FromLTRB(x0, y0, x1, y1);
        }

        private void AddExactLabels(List<OverlayLabel> output, List<OcrLine> lines,
            string combinedText, List<MatchResult> matches, float ocrScale)
        {
            string normalizedLine = TranslationStore.Normalize(combinedText);
            foreach (MatchResult match in matches)
            {
                // v4.0: the same dictionary lookup can be an exact hit or a fuzzy repair of OCR
                // noise. Only the exact hit is trusted by the fast first paint; a fuzzy one is
                // still painted, but it never replaces an existing high-confidence label.
                int matchQuality = LabelConfidencePolicy.ApproximateMatched;
                int matchStart = Math.Max(0, Math.Min(match.Start, normalizedLine.Length));
                int matchLength = Math.Max(0, Math.Min(match.Length, normalizedLine.Length - matchStart));
                string matchedText = normalizedLine.Substring(matchStart, matchLength);
                if (matchedText.Length > 0 &&
                    String.Equals(matchedText, match.Entry.Normalized, StringComparison.Ordinal))
                    matchQuality = LabelConfidencePolicy.ExactMatched;
                int confidenceScore = LabelConfidencePolicy.Score(match.Entry.Chinese, matchQuality, 0);
                // OCR exposes word boxes, not character boxes. Approximate the phrase span by character ratio.
                float x0 = Single.MaxValue, y0 = Single.MaxValue, x1 = Single.MinValue, y1 = Single.MinValue;
                List<RectangleF> sourceLines = new List<RectangleF>();
                int cursor = 0;
                foreach (OcrLine line in lines)
                {
                    float lx0 = Single.MaxValue, ly0 = Single.MaxValue;
                    float lx1 = Single.MinValue, ly1 = Single.MinValue;
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
                            float wx0 = ToOverlayX((float)word.BoundingRect.X, ocrScale);
                            float wy0 = ToOverlayY((float)word.BoundingRect.Y, ocrScale);
                            float wx1 = ToOverlayX((float)(word.BoundingRect.X + word.BoundingRect.Width), ocrScale);
                            float wy1 = ToOverlayY((float)(word.BoundingRect.Y + word.BoundingRect.Height), ocrScale);
                            x0 = Math.Min(x0, wx0); y0 = Math.Min(y0, wy0);
                            x1 = Math.Max(x1, wx1); y1 = Math.Max(y1, wy1);
                            lx0 = Math.Min(lx0, wx0); ly0 = Math.Min(ly0, wy0);
                            lx1 = Math.Max(lx1, wx1); ly1 = Math.Max(ly1, wy1);
                        }
                        cursor = Math.Min(normalizedLine.Length, end + 1);
                    }
                    // A match that spans OCR rows keeps one rectangle per row, so the erase pass
                    // can clear each source row instead of the rectangle spanning the gaps.
                    if (lx0 != Single.MaxValue)
                        sourceLines.Add(new RectangleF(lx0, ly0, Math.Max(1.0f, lx1 - lx0),
                            Math.Max(1.0f, ly1 - ly0)));
                }
                if (x0 == Single.MaxValue) continue;
                output.Add(new OverlayLabel {
                    Bounds = new RectangleF(x0 - 3, y0 - 2, Math.Max(28, x1 - x0 + 6), Math.Max(18, y1 - y0 + 4)),
                    // SourceBounds is the live text the overlay may erase, deliberately narrower
                    // than Bounds so a longer translation cannot cover dynamic game values.
                    SourceBounds = new RectangleF(x0, y0, Math.Max(1.0f, x1 - x0), Math.Max(1.0f, y1 - y0)),
                    HasSourceBounds = true,
                    SourceLineBounds = sourceLines,
                    Confidence = LabelConfidencePolicy.Level(confidenceScore),
                    ConfidenceReason = confidenceScore,
                    MatchSource = matchQuality == LabelConfidencePolicy.ExactMatched ? "exact" : "approximate",
                    Text = match.Entry.Chinese,
                    SemanticRole = GetEntrySemanticRole(match.Entry),
                    // Semantic categories do not determine geometry. If the game rendered the
                    // source on one OCR line, keep the replacement on one line as well unless
                    // the dictionary explicitly supplies a second line (for example a quiz-answer
                    // hint). Explicit line breaks must receive multi-line layout and height.
                    Wrap = lines.Count > 1 || match.Entry.Chinese.IndexOf('\n') >= 0 ||
                        match.Entry.Chinese.IndexOf('\r') >= 0
                });
            }
        }

        private static bool CanJoinOcrLines(OcrLine first, OcrLine second)
        {
            return AreOcrBlocksContinuous(GetOcrLineBounds(first), GetOcrLineBounds(second));
        }

        private static bool HasExplicitMagicValue(string normalized)
        {
            // 'Magic skills' is prose. Its leading s is not an OCR digit 5.
            // Require the COMPLETE numeric token, not a one-character prefix.
            return Regex.IsMatch(normalized ?? "", @"\bmagic\s+[+-]?(?:[0-9lIoOgqSsB]*[0-9][0-9lIoOgqSsB]*|[lIoOgqSsB])(?=$|[\s,;:)\]])", RegexOptions.IgnoreCase);
        }

        private static bool IsSkillTooltipText(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("master level") ||
                (normalized.Contains("current level") && normalized.Contains("next level"));
        }

        // Chat classification and the optional prose-AI eligibility gate deliberately
        // keep the pre-EQUIP vocabulary unchanged. Equipment-only paths use the
        // grammar-aware wrapper below.
        private static bool LooksLikeEquipmentStatText(string text)
        {
            string normalized = TranslationStore.Normalize(text);
            return normalized.Contains("attack speed") || normalized.Contains("weapon attack") ||
                normalized.Contains("enhancements") || normalized.Contains("required level") ||
                normalized.Contains("upgrades available") || normalized.Contains("usable until") ||
                normalized.Contains("req lev") || normalized.Contains("req str") ||
                normalized.Contains("req dex") || normalized.Contains("req int") ||
                normalized.Contains("req luk") || normalized.Contains("req fame") ||
                normalized.Contains("weapon def") || normalized.Contains("magic def") ||
                HasExplicitMagicValue(normalized) ||
                normalized.Contains("avoidability") || normalized.StartsWith("str ", StringComparison.Ordinal) ||
                normalized.StartsWith("dex ", StringComparison.Ordinal) ||
                normalized.StartsWith("int ", StringComparison.Ordinal) ||
                normalized.StartsWith("luk ", StringComparison.Ordinal) ||
                normalized.StartsWith("type ", StringComparison.Ordinal) ||
                normalized.Contains(" type ") ||
                normalized.StartsWith("category ", StringComparison.Ordinal) ||
                normalized.Contains(" category ");
        }

        // Grammar-backed recognition covers Evasion, Attack Speed, Critical, upgrade
        // slots and partial tooltips without naming any item. It is used only where the
        // equipment label path is already being built, never to change chat or AI gating.
        private static bool LooksLikeEquipmentFieldText(string text)
        {
            if (EquipmentFieldGrammar.LooksLikeEquipmentRow(text)) return true;
            string normalized = TranslationStore.Normalize(text);
            return LooksLikeEquipmentStatText(text) ||
                normalized.Contains("upgrade slots") || normalized.Contains("remaining upgrades") ||
                normalized.Contains("evasion") ||
                normalized.StartsWith("speed ", StringComparison.Ordinal) ||
                normalized.StartsWith("crit ", StringComparison.Ordinal) ||
                normalized.Contains("crit rate") || normalized.Contains("critical rate") ||
                normalized.Contains("critical damage");
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
                Bounds = new RectangleF(ToOverlayX(left, ocrScale) - 3,
                    ToOverlayY(top, ocrScale) - 2,
                    Math.Max(28, (right - left) / ocrScale + 6),
                    Math.Max(18, (bottom - top) / ocrScale + 4)),
                // The icon match measured the text it is replacing, so that span is the region
                // Seamless may recolour.
                SourceBounds = new RectangleF(ToOverlayX(left, ocrScale),
                    ToOverlayY(top, ocrScale),
                    Math.Max(1.0f, (right - left) / ocrScale),
                    Math.Max(1.0f, (bottom - top) / ocrScale)),
                HasSourceBounds = true,
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
            lastPaintedTranslationCount = 0;
            if (shuttingDown) return;
            try
            {
                if (scanIndicatorVisible) PaintWindowScanIndicator(e.Graphics);
                if (visibleTranslation) PaintSimpleFusion(e.Graphics);
            }
            catch (ArgumentException) { }
            catch (ExternalException) { }
        }

        // Decides, per label, whether the source text can be erased and replaced in place.
        //
        // Seamless is only safe when the area behind the original text is a reliable flat
        // colour: on a textured, gradient or animated background a "clean" rectangle would be
        // more visible than the card it replaced. Uniformity is therefore measured, not
        // guessed - the standard deviation of the sampled region must be small before the
        // label may drop the card. Everything else stays on CardFallback.
        private void SampleSeamlessBackgrounds(Bitmap bitmap, Rectangle screen, List<OverlayLabel> target)
        {
            if (bitmap == null || target == null || target.Count == 0) return;
            float maximumDeviation = 26.0f;
            foreach (OverlayLabel label in target)
            {
                if (label == null) continue;
                // Only a trusted, measured source region may be replaced in place.
                if (label.Confidence != LabelConfidence.High)
                {
                    label.RenderMode = LabelRenderMode.CardFallback;
                    continue;
                }
                if (!label.HasSourceBounds) continue;
                RectangleF source = label.SourceBounds;
                // Confidence is not geometry safety. A high-confidence dictionary match on an
                // invalid, empty, off-capture, absurdly large or poorly aligned rectangle must
                // still keep the card instead of recolouring part of the game.
                if (!OverlayPaintSafety.IsSafeSeamlessGeometry(label.Bounds, source,
                    bitmap.Width, bitmap.Height))
                {
                    label.RenderMode = LabelRenderMode.CardFallback;
                    continue;
                }

                int x0 = (int)Math.Floor(source.Left + Bounds.Left - screen.Left);
                int y0 = (int)Math.Floor(source.Top + Bounds.Top - screen.Top);
                int x1 = (int)Math.Ceiling(source.Right + Bounds.Left - screen.Left);
                int y1 = (int)Math.Ceiling(source.Bottom + Bounds.Top - screen.Top);
                if (x1 <= x0 || y1 <= y0)
                {
                    label.RenderMode = LabelRenderMode.CardFallback;
                    continue;
                }
                if (!OverlayPaintSafety.IsInsideCapture(
                    new RectangleF(x0, y0, x1 - x0, y1 - y0), bitmap.Width, bitmap.Height))
                {
                    // The region to measure runs off the captured frame, so the erase could not
                    // be checked against the real pixels at all.
                    label.RenderMode = LabelRenderMode.CardFallback;
                    continue;
                }

                Rectangle region = new Rectangle(x0, y0, x1 - x0, y1 - y0);
                Color background;
                Color ink;
                float deviation;
                if (!TrySampleUniformBackground(bitmap, region, out background, out deviation, out ink))
                {
                    label.RenderMode = LabelRenderMode.CardFallback;
                    continue;
                }
                if (deviation > maximumDeviation)
                {
                    // Texture or gradient: a flat patch would stand out, so keep the card.
                    label.RenderMode = LabelRenderMode.CardFallback;
                    continue;
                }
                label.BackgroundColor = background;
                // A wrapped or multi-line body mixes black, blue and red glyphs; averaging them
                // produced a purple no panel ever used. Only a short single-line label may keep
                // its sampled ink, and only when that ink is a usable contrasting colour. Long
                // prose takes the contrast ink of the background instead (Color.Empty).
                // A wrapped prose label always takes the contrast ink: the sampled ink averages
                // every glyph colour in the block, which is how a purple translation appeared on a
                // pale panel. The predicate is shared with the paint pass so the two cannot drift.
                bool prose = OverlayPaintSafety.IsWrappedProse(label, label.Text);
                label.TextColor = !prose && OverlayPaintSafety.IsUsableInk(ink, background)
                    ? ink : Color.Empty;
                label.RenderMode = LabelRenderMode.Seamless;
            }
        }

// Estimates the surface behind a text row and reports how uniform it is, together with the
// colour the original text was drawn in.
//
// Ink must not be averaged into the background. The naive version measured the whole region,
// so glyph strokes dominated: black text on a flat panel scored a deviation near 90 and was
// rejected as "textured", which made Seamless unreachable on every screenshot. Here the
// per-channel median is taken as the background colour, pixels close to it ARE the background,
// and only those are measured. A flat panel with text now scores low while a genuinely
// textured surface still scores high, because there the near-median set is not one flat colour.
// Pixels far from the median are the glyph strokes, and their average is the colour the
// translation has to be drawn in.
//
// Pixels are read through one LockBits copy instead of per-pixel GetPixel: the dense pass
// touches every pixel of the region, and GetPixel made a single label cost tens of
// milliseconds, which is far too slow to run inside the translate path.
private static bool TrySampleUniformBackground(Bitmap bitmap, Rectangle region,
    out Color average, out float deviation, out Color ink)
{
    average = Color.Empty;
    deviation = 0.0f;
    ink = Color.Empty;
    if (bitmap == null || region.Width < 2 || region.Height < 2) return false;
    if (region.Left < 0 || region.Top < 0 ||
        region.Right > bitmap.Width || region.Bottom > bitmap.Height) return false;

    int width = region.Width;
    int height = region.Height;
    int stride;
    byte[] pixels;
    System.Drawing.Imaging.BitmapData data = null;
    try
    {
        data = bitmap.LockBits(region, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        stride = data.Stride;
        pixels = new byte[stride * height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
    }
    catch (ArgumentException) { if (data != null) bitmap.UnlockBits(data); return false; }
    catch (System.Runtime.InteropServices.ExternalException)
    { if (data != null) bitmap.UnlockBits(data); return false; }

    try
    {
        // 1) Coarse pass, on a stride, only to locate the median background colour.
        //    Format32bppArgb is BGRA in memory.
        List<int> packed = new List<int>(256);
        int stepX = Math.Max(1, width / 16);
        int stepY = Math.Max(1, height / 8);
        for (int y = 0; y < height; y += stepY)
        {
            int row = y * stride;
            for (int x = 0; x < width; x += stepX)
            {
                int o = row + x * 4;
                packed.Add((pixels[o + 2] << 16) | (pixels[o + 1] << 8) | pixels[o]);
            }
        }
        if (packed.Count < 6) return false;
        packed.Sort();
        int median = packed[packed.Count / 2];
        int medianR = (median >> 16) & 0xFF;
        int medianG = (median >> 8) & 0xFF;
        int medianB = median & 0xFF;

        // 2) Dense pass keeps only background pixels: those close to the median colour, and
        //    accumulates the far ones as the ink the original text was drawn in.
        const int InkTolerance = 22;
        long sumR = 0, sumG = 0, sumB = 0, count = 0;
        long sumR2 = 0, sumG2 = 0, sumB2 = 0;
        long inkR = 0, inkG = 0, inkB = 0, inkCount = 0;
        long total = (long)width * height;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int o = row + x * 4;
                int b = pixels[o];
                int g = pixels[o + 1];
                int r = pixels[o + 2];
                if (Math.Abs(r - medianR) > InkTolerance ||
                    Math.Abs(g - medianG) > InkTolerance ||
                    Math.Abs(b - medianB) > InkTolerance)
                {
                    // These are the glyph pixels. Their average is the colour the original text
                    // was drawn in, which is what the translation should be drawn in too.
                    inkR += r; inkG += g; inkB += b; inkCount++;
                    continue;
                }
                sumR += r; sumG += g; sumB += b;
                sumR2 += (long)r * r;
                sumG2 += (long)g * g;
                sumB2 += (long)b * b;
                count++;
            }
        }
        if (count < 6) return false;

        double meanR = (double)sumR / count;
        double meanG = (double)sumG / count;
        double meanB = (double)sumB / count;
        double varianceR = (double)sumR2 / count - meanR * meanR;
        double varianceG = (double)sumG2 / count - meanG * meanG;
        double varianceB = (double)sumB2 / count - meanB * meanB;
        double spread = Math.Sqrt(Math.Max(0.0, varianceR)) +
            Math.Sqrt(Math.Max(0.0, varianceG)) +
            Math.Sqrt(Math.Max(0.0, varianceB));
        average = Color.FromArgb(
            Math.Max(0, Math.Min(255, (int)Math.Round(meanR))),
            Math.Max(0, Math.Min(255, (int)Math.Round(meanG))),
            Math.Max(0, Math.Min(255, (int)Math.Round(meanB))));
        deviation = (float)(spread / 3.0);

        // Uniform requires the surviving background pixels to be flat AND dominant. On a busy
        // texture the median filter discards most pixels, and thin coverage means "cannot
        // estimate reliably" - keep the card.
        double coverage = (double)count / total;
        // Magenta is this overlay's transparency key. Filling a region with it either
        // punches a hole through the window or, when the shade misses the key by a few
        // levels, shows up as a lurid purple block. Refuse the sample and keep the card.
        if (meanR > 200 && meanB > 200 && meanG < 90) return false;
        if (inkCount > 0)
        {
            ink = Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)Math.Round((double)inkR / inkCount))),
                Math.Max(0, Math.Min(255, (int)Math.Round((double)inkG / inkCount))),
                Math.Max(0, Math.Min(255, (int)Math.Round((double)inkB / inkCount))));
        }
        return coverage >= 0.35;
    }
    finally { bitmap.UnlockBits(data); }
}

        // A short-lived, self-dismissing notice drawn on the overlay. It must never require a
        // click, because the player's hands are on the game.
        private void PaintLockNotice(Graphics graphics)
        {
            string[] lines = lockNoticeText.Split('\n');
            float width = 0.0f;
            foreach (string line in lines)
            {
                float measured = graphics.MeasureString(line, overlayFont).Width;
                if (measured > width) width = measured;
            }
            float height = lines.Length * (overlayFont.Height + 2.0f) + 16.0f;
            RectangleF card = new RectangleF(
                Math.Max(8.0f, (Bounds.Width - width) / 2.0f - 16.0f),
                Math.Max(8.0f, Bounds.Height * 0.12f),
                Math.Min(Bounds.Width - 16.0f, width + 32.0f),
                height);
            using (SolidBrush background = new SolidBrush(Color.FromArgb(238, 18, 18, 20)))
            using (Pen border = new Pen(Color.FromArgb(230, 255, 190, 45), 2.0f))
            {
                graphics.FillRectangle(background, card);
                graphics.DrawRectangle(border, card.X, card.Y, card.Width, card.Height);
            }
            float textY = card.Y + 8.0f;
            foreach (string line in lines)
            {
                graphics.DrawString(line, overlayFont, Brushes.White, card.X + 16.0f, textY,
                    StringFormat.GenericTypographic);
                textY += overlayFont.Height + 2.0f;
            }
        }

        private void PaintWindowScanIndicator(Graphics graphics)
        {
            Rectangle local = gameBounds;
            local.Offset(-Bounds.Left, -Bounds.Top);
            local = Rectangle.Intersect(local, ClientRectangle);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(226, 241, 255)))
                graphics.FillRectangle(glow, local);
        }

private void PaintOverflowNotice(Graphics graphics, OverlayLabel label, RectangleF r,
    RectangleF boundary, Brush background, Pen border, Brush textBrush, string notice)
{
    // The complete translation cannot be shown inside its boundary at a readable font, so the
    // original text is deliberately preserved and this small bounded notice explains why. It is
    // the accepted safe alternative to pagination and it never erases the English.
    RectangleF area = boundary.Width >= 8.0f && boundary.Height >= 8.0f ? boundary : r;
    if (area.Width < 8.0f || area.Height < 8.0f) return;
    Font noticeFont = overlayFont;
    SizeF size = graphics.MeasureString(notice, noticeFont);
    if (size.Width + 14.0f > area.Width || size.Height + 8.0f > area.Height)
    {
        foreach (Font candidate in ScaledLabelFonts())
        {
            SizeF candidateSize = graphics.MeasureString(notice, candidate);
            if (candidateSize.Width + 14.0f <= area.Width &&
                candidateSize.Height + 8.0f <= area.Height)
            {
                noticeFont = candidate;
                size = candidateSize;
                break;
            }
        }
    }
    float width = Math.Min(area.Width, Math.Max(24.0f, size.Width + 14.0f));
    float height = Math.Min(area.Height, Math.Max(14.0f, size.Height + 8.0f));
    RectangleF card = new RectangleF(
        Math.Max(area.Left, Math.Min(r.Left, area.Right - width)),
        Math.Max(area.Top, Math.Min(r.Top, area.Bottom - height)), width, height);
    GraphicsState state = graphics.Save();
    try
    {
        graphics.SetClip(card, CombineMode.Intersect);
        if (card.Width >= 14.0f && card.Height >= 14.0f)
        {
            using (GraphicsPath path = RoundedRect(card, 4.0f))
            {
                graphics.FillPath(background, path);
                graphics.DrawPath(border, path);
            }
        }
        else
        {
            graphics.FillRectangle(background, card);
            graphics.DrawRectangle(border, card.X, card.Y, card.Width, card.Height);
        }
        graphics.DrawString(notice, noticeFont, textBrush,
            new RectangleF(card.X + 4.0f, card.Y + 3.0f,
                Math.Max(8.0f, card.Width - 8.0f), Math.Max(8.0f, card.Height - 6.0f)),
            StringFormat.GenericTypographic);
    }
    finally { graphics.Restore(state); }
    label.Overflow = true;
}

private bool PaintOverlayLabel(Graphics graphics, OverlayLabel label, string labelText,
    RectangleF r, Brush background, Pen border, Brush textBrush)
{
    // Option 2 never paints a substitute card or an overflow box. Failed reconstruction,
    // fit or placement leaves the live game's original pixels entirely untouched.
    RecordLabelLayoutFailure(label, String.IsNullOrEmpty(label.FusionFailureReason)
        ? "fusion-placement-unavailable" : label.FusionFailureReason, labelText.Length, 0, r);
    return false;
}

private void RecordLabelLayoutFailure(OverlayLabel label, string reason, int characters,
    int fittedCharacters, RectangleF textArea)
{
    label.Overflow = true;
    if (label.LayoutFailureReason == reason) return;
    label.LayoutFailureReason = reason;
    Program.LogRuntimeEvent("overlay-layout-failed", "generation=" + recognitionGeneration +
        " reason=" + reason + " chars=" + characters + " fitted=" + fittedCharacters +
        " width=" + textArea.Width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
        " height=" + textArea.Height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
}

// Returns black or white, whichever stays legible on the given background.
private static Color ContrastInk(Color background)
{
    double luminance = (background.R * 0.299 + background.G * 0.587 + background.B * 0.114);
    return luminance > 145.0 ? Color.FromArgb(18, 18, 22) : Color.White;
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
            mapleWindows.Dispose();
            CancelPanelAiRequest();
            visibleTranslation = false;
            labels.Clear();
            UnregisterHotKey(Handle, HOTKEY_SHOW);
            UnregisterHotKey(Handle, HOTKEY_HIDE);
            UnregisterHotKey(Handle, HOTKEY_FLOATING_WINDOW);
            SetJournalPagingShortcuts(false);
            gamepadTimer.Stop();
            gamepadTimer.Dispose();
            continuousTranslationTimer.Stop();
            continuousTranslationTimer.Dispose();
            mapleWindowTimer.Stop();
            mapleWindowTimer.Dispose();
            displayedSceneTimer.Stop(); displayedSceneTimer.Dispose();
            ClearFusionAssets();
            // The scaled label fonts are GDI+ objects owned by this form: disposing them here keeps
            // the label pipeline from leaking a font per size on every session.
            if (scaledLabelFonts != null)
            {
                foreach (Font font in scaledLabelFonts)
                {
                    if (font != null) font.Dispose();
                }
                scaledLabelFonts = null;
            }
            if (lockNoticeTimer != null)
            {
                lockNoticeTimer.Stop();
                lockNoticeTimer.Dispose();
                lockNoticeTimer = null;
            }
            if (scanIndicatorTimer != null)
            {
                scanIndicatorTimer.Stop();
                scanIndicatorTimer.Dispose();
                scanIndicatorTimer = null;
            }
            if (activationWait != null)
            {
                activationWait.Unregister(null);
                activationWait = null;
            }
            tray.Visible = false;
            tray.Dispose();
            if (chatTranslator != null && !chatTranslator.IsDisposed) chatTranslator.StopService();
            if (mainPanel != null && !mainPanel.IsDisposed) mainPanel.Dispose();
            base.OnFormClosed(e);
            overlayFont.Dispose();
        }
    }
}
