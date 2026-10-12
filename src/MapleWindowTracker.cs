using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MapleOverlay
{
    // Why this component exists (v4.0 architecture, layer 1):
    //
    // Until v3.x every press of the translate hotkey asked "which window is in the
    // foreground right now?" and screenshotted that. It worked only while the game was
    // focused; clicking the tray icon, a chat window or the dictionary editor silently
    // moved the source of truth to whatever happened to be in front. There was also no
    // notion of the client at all, so a full-screen session or a minimised client could
    // produce a useless black frame.
    //
    // This tracker is the single source of truth for "which client are we translating".
    // It is deliberately OCR-free, AI-free and almost free of CPU: it only reads window
    // geometry and identity on a low frequency timer, and it never touches game memory,
    // never injects, never hooks and never synthesises input.
    internal enum MapleWindowState
    {
        // No usable MapleStory window was found.
        NotFound = 0,
        // A window was found but it covers its monitor, i.e. the client is full-screen and
        // cannot be captured reliably from the outside.
        FullScreen = 1,
        // A windowed client is locked and ready to be captured.
        Locked = 2
    }

    // One resolved candidate window. Everything here is measured, never guessed.
    internal sealed class MapleWindowInfo
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string ProcessName = "";
        public string Title = "";
        public string ClassName = "";
        public Rectangle ClientBounds;
        public int Dpi = 96;
        public bool IsForeground;
        public bool IsMinimized;
        public int Evidence;
    }

    internal enum MapleRebindReason
    {
        None = 0,
        FirstLock = 1,
        HandleLost = 2,
        // The client window itself changed size (resolution, DPI or window resize).
        ClientResized = 3,
        DpiChanged = 4,
        Minimized = 5,
        // Windowed client turned into a full-screen one, or the other way round.
        FullScreenChanged = 6
    }

    internal sealed class MapleWindowTracker
    {
        // Clients are re-identified on a low frequency timer. A MapleStory client is not a
        // chat window; nothing here needs to react within milliseconds, and the whole point
        // of the layer is that it costs nothing while idle.
        internal const int PollIntervalMs = 1000;

        // Wrapper chrome such as "MapleStory Classic" is kept lower-case here so the title
        // probe stays a weak hint rather than a hard dependency (titles are localised).
        private static readonly string[] TitleHints = new string[] {
            "maplestory", "maple story", "冒险岛", "楓之谷", "枫之谷", "新枫之谷"
        };

        // Window class names seen on MapleStory clients. This is only ever a bonus signal:
        // an unknown or renamed class must never prevent the lock, otherwise a client update
        // would break translation entirely.
        private static readonly string[] ClassHints = new string[] {
            "MapleStoryClass", "MapleStory", "MapleStoryClassGlobal"
        };

        private readonly Dictionary<IntPtr, MapleWindowInfo> candidates =
            new Dictionary<IntPtr, MapleWindowInfo>();
        private readonly List<IntPtr> candidateOrder = new List<IntPtr>();
        private readonly object gate = new object();

        private MapleWindowState state = MapleWindowState.NotFound;
        private MapleWindowInfo locked;
        private Rectangle lastCaptureBounds = Rectangle.Empty;
        private int lockedClientWidth;
        private int lockedClientHeight;
        private int lockedDpi;
        private bool hadLock;
        private string diagnostic = "尚未查找 MapleStory 窗口";

        internal MapleWindowState State { get { lock (gate) return state; } }
        internal MapleWindowInfo Locked { get { lock (gate) return locked; } }
        internal string Diagnostic { get { lock (gate) return diagnostic; } }

        internal bool IsLocked { get { lock (gate) return state == MapleWindowState.Locked && locked != null; } }

        // A candidate list is exposed for diagnostics and for the regression tests, which
        // assert the evidence rules without needing a real game client on the machine.
        internal int CandidateCount { get { lock (gate) return candidateOrder.Count; } }

        // ---------------------------------------------------------------------------
        // Evidence rules (pure, testable without a window on screen)
        // ---------------------------------------------------------------------------

        // The launcher, the updater and patcher helpers all live next to the client and some
        // of them even carry "MapleStory" in their name. Only actual client executables pass.
        internal static bool IsMapleProcessName(string processName)
        {
            if (String.IsNullOrEmpty(processName)) return false;
            string name = processName.ToLowerInvariant();
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            if (name.Length == 0) return false;
            // Explicit non-clients first: these are tools, not the render surface.
            if (name.StartsWith("maplestory_") || name.EndsWith("launcher") ||
                name.EndsWith("patcher") || name.EndsWith("updater") ||
                name.Contains("setup") || name.Contains("install") || name.Contains("crashreport"))
                return false;
            // Any executable whose name starts with "maplestory" is the client itself
            // (MapleStory, MapleStoryClassic, MapleStoryGlobal, ...).
            return name.StartsWith("maplestory");
        }

        internal static bool TitleLooksLikeMaple(string title)
        {
            if (String.IsNullOrEmpty(title)) return false;
            string lower = title.ToLowerInvariant();
            foreach (string hint in TitleHints)
                if (lower.Contains(hint)) return true;
            return false;
        }

        internal static bool ClassLooksLikeMaple(string className)
        {
            if (String.IsNullOrEmpty(className)) return false;
            foreach (string hint in ClassHints)
                if (String.Equals(className, hint, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A single candidate's score. Process identity is authoritative; title and class are
        // corroboration. Zero means "not the client at all", and such a candidate is dropped.
        internal static int ScoreCandidate(bool processMatch, bool titleMatch, bool classMatch,
            bool visible, bool minimized, bool hasOwner, int clientWidth, int clientHeight)
        {
            if (!visible || minimized || hasOwner) return 0;
            if (clientWidth < 160 || clientHeight < 120) return 0;
            if (!processMatch) return 0;
            int score = 60;
            if (titleMatch) score += 15;
            if (classMatch) score += 25;
            // A client that is already showing a sizeable playfield is more likely to be the
            // real render surface than a tiny helper window that shares the process name.
            if (clientWidth >= 640 && clientHeight >= 480) score += 10;
            return score;
        }

        // ---------------------------------------------------------------------------
        // Polling
        // ---------------------------------------------------------------------------

        internal void Poll()
        {
            MapleWindowInfo foreground = null;
            TryGetWindowInfo(GetForegroundWindow(), ref foreground);

            List<MapleWindowInfo> found = EnumerateCandidates();
            MapleWindowInfo chosen = SelectCandidate(found, foreground);
            Apply(chosen, found);
        }

        // Re-runs identification right now (used on hotkey press so a client that was
        // started after the last poll is picked up without waiting for the timer).
        internal void Refresh()
        {
            Poll();
        }

        private void Apply(MapleWindowInfo chosen, List<MapleWindowInfo> found)
        {
            lock (gate)
            {
                candidateOrder.Clear();
                candidates.Clear();
                if (found != null)
                {
                    foreach (MapleWindowInfo item in found)
                    {
                        candidates[item.Handle] = item;
                        candidateOrder.Add(item.Handle);
                    }
                }
            }

            if (chosen == null)
            {
                bool wasLocked = hadLock;
                locked = null;
                state = MapleWindowState.NotFound;
                diagnostic = "未找到 MapleStory 客户端，请先启动游戏";
                if (wasLocked)
                {
                    hadLock = false;
                    lastCaptureBounds = Rectangle.Empty;
                    OnRebound(MapleRebindReason.HandleLost);
                }
                return;
            }

            if (IsFullScreenClient(chosen.ClientBounds))
            {
                bool wasLocked = hadLock;
                locked = chosen;
                state = MapleWindowState.FullScreen;
                diagnostic = "检测到全屏 MapleStory，请切换为窗口模式后重试";
                hadLock = false;
                if (wasLocked) OnRebound(MapleRebindReason.FullScreenChanged);
                return;
            }

            MapleRebindReason reason = MapleRebindReason.None;
            if (!hadLock) reason = MapleRebindReason.FirstLock;
            else if (locked == null || locked.Handle != chosen.Handle) reason = MapleRebindReason.HandleLost;
            else if (lockedClientWidth != chosen.ClientBounds.Width ||
                lockedClientHeight != chosen.ClientBounds.Height)
                reason = MapleRebindReason.ClientResized;
            else if (lockedDpi != chosen.Dpi) reason = MapleRebindReason.DpiChanged;

            locked = chosen;
            // The last known-good rectangle is retained so a temporarily hidden window (for
            // example while its own modal dialog is open) still has valid capture bounds.
            lastCaptureBounds = chosen.ClientBounds;
            lockedClientWidth = chosen.ClientBounds.Width;
            lockedClientHeight = chosen.ClientBounds.Height;
            lockedDpi = chosen.Dpi;
            state = MapleWindowState.Locked;
            hadLock = true;
            diagnostic = "已锁定 MapleStory，按 F8 翻译";

            if (reason != MapleRebindReason.None) OnRebound(reason);
        }

        private MapleWindowInfo SelectCandidate(List<MapleWindowInfo> found, MapleWindowInfo foreground)
        {
            if (found.Count == 0)
            {
                // Keep a still-valid lock even when the enumeration momentarily fails.
                lock (gate)
                {
                    if (locked != null && IsWindow(locked.Handle)) return locked;
                }
                return null;
            }

            // Prefer the window we already own so a background client is not stolen by a
            // second, weaker candidate that happens to be in front.
            lock (gate)
            {
                if (locked != null)
                {
                    foreach (MapleWindowInfo item in found)
                        if (item.Handle == locked.Handle) return item;
                }
            }

            // Otherwise, if the foreground window is itself a candidate, bind to it. This is
            // the "user just alt-tabbed into the game" case and is the only moment where
            // foreground may influence the decision.
            if (foreground != null)
            {
                foreach (MapleWindowInfo item in found)
                    if (item.Handle == foreground.Handle) return item;
            }

            MapleWindowInfo best = null;
            foreach (MapleWindowInfo item in found)
            {
                // Equal or weaker evidence is not enough to move an existing lock onto a
                // different window: fail closed instead of guessing between two clients.
                if (best == null || item.Evidence > best.Evidence) best = item;
            }
            return best;
        }

        private List<MapleWindowInfo> EnumerateCandidates()
        {
            List<MapleWindowInfo> result = new List<MapleWindowInfo>();
            IntPtr foreground = GetForegroundWindow();
            EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                MapleWindowInfo info = null;
                if (TryGetWindowInfo(handle, ref info) && info.Evidence > 0)
                {
                    info.IsForeground = handle == foreground;
                    result.Add(info);
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private bool TryGetWindowInfo(IntPtr handle, ref MapleWindowInfo info)
        {
            if (handle == IntPtr.Zero) return false;
            if (!IsWindowVisible(handle)) return false;
            if (IsIconic(handle)) return false;
            // Owned windows are popups and tool windows; the client's render surface is an
            // unowned top-level window.
            if (GetWindow(handle, GW_OWNER) != IntPtr.Zero) return false;

            uint processId = 0;
            GetWindowThreadProcessId(handle, out processId);
            if (processId == 0) return false;

            string processName = ProcessNameOf(processId);
            bool processMatch = IsMapleProcessName(processName);

            string title = WindowText(handle);
            string className = ClassName(handle);
            bool titleMatch = TitleLooksLikeMaple(title);
            bool classMatch = ClassLooksLikeMaple(className);

            RECT client;
            POINT origin = new POINT();
            if (!GetClientRect(handle, out client) || !ClientToScreen(handle, ref origin)) return false;
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;

            int evidence = ScoreCandidate(processMatch, titleMatch, classMatch, true, false,
                false, width, height);
            if (evidence <= 0) return false;

            MapleWindowInfo found = new MapleWindowInfo();
            found.Handle = handle;
            found.ProcessId = processId;
            found.ProcessName = processName;
            found.Title = title;
            found.ClassName = className;
            found.ClientBounds = new Rectangle(origin.X, origin.Y, width, height);
            found.Dpi = DpiOf(handle);
            found.IsMinimized = false;
            found.Evidence = evidence;
            info = found;
            return true;
        }

        // A windowed client must not cover its monitor edge to edge; that is the signature of
        // a full-screen (or borderless full-screen) session that cannot be captured reliably
        // from outside. The comparison is done against the monitor the window sits on, so a
        // multi-monitor desktop with the client on a secondary screen still works.
        internal static bool IsFullScreenClient(Rectangle client)
        {
            if (client.Width < 160 || client.Height < 120) return false;
            RECT monitor;
            if (!TryGetMonitorBounds(client, out monitor)) return false;
            int monitorWidth = monitor.Right - monitor.Left;
            int monitorHeight = monitor.Bottom - monitor.Top;
            if (monitorWidth < 160 || monitorHeight < 120) return false;
            // Allow a 1px tolerance for DPI rounding at the monitor edges.
            return client.Left <= monitor.Left + 1 && client.Top <= monitor.Top + 1 &&
                client.Right >= monitor.Right - 1 && client.Bottom >= monitor.Bottom - 1;
        }

        private static bool TryGetMonitorBounds(Rectangle client, out RECT bounds)
        {
            bounds = new RECT();
            POINT center = new POINT();
            center.X = client.Left + client.Width / 2;
            center.Y = client.Top + client.Height / 2;
            IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;
            MONITORINFO info = new MONITORINFO();
            info.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;
            bounds = info.Monitor;
            return true;
        }

        // ---------------------------------------------------------------------------
        // Capture bounds for the rest of the pipeline
        // ---------------------------------------------------------------------------

        // The rectangle the OCR pipeline should capture. Returns false when there is nothing
        // safe to capture, in which case callers must NOT fall back to a full-desktop scan.
        internal bool TryGetCaptureBounds(out Rectangle bounds, out bool fullScreen)
        {
            lock (gate)
            {
                bounds = Rectangle.Empty;
                fullScreen = state == MapleWindowState.FullScreen;
                if (state == MapleWindowState.FullScreen) return false;
                if (state != MapleWindowState.Locked || locked == null)
                {
                    // A window that is momentarily hidden (minimised animation, owned modal)
                    // keeps its last measured rectangle, which is strictly better than
                    // silently screenshotting the whole desktop.
                    if (hadLock && lastCaptureBounds.Width >= 160 && lastCaptureBounds.Height >= 120)
                    {
                        bounds = lastCaptureBounds;
                        return true;
                    }
                    return false;
                }
                // Re-measure on demand: the client may have moved since the last poll.
                RECT client;
                POINT origin = new POINT();
                if (GetClientRect(locked.Handle, out client) && ClientToScreen(locked.Handle, ref origin))
                {
                    Rectangle current = new Rectangle(origin.X, origin.Y,
                        client.Right - client.Left, client.Bottom - client.Top);
                    if (current.Width >= 160 && current.Height >= 120 && !IsFullScreenClient(current))
                    {
                        lastCaptureBounds = current;
                        bounds = current;
                        return true;
                    }
                }
                if (lastCaptureBounds.Width >= 160 && lastCaptureBounds.Height >= 120)
                {
                    bounds = lastCaptureBounds;
                    return true;
                }
                return false;
            }
        }

        internal string Describe()
        {
            lock (gate)
            {
                if (state == MapleWindowState.Locked && locked != null)
                    return diagnostic + "（" + locked.Title + " " + locked.ClientBounds.Width + "x" +
                        locked.ClientBounds.Height + "）";
                return diagnostic;
            }
        }

        // ---------------------------------------------------------------------------
        // Rebind notification
        // ---------------------------------------------------------------------------

        // Raised only for genuine identity/geometry changes, never for a mere move, so the
        // UI is not rewritten on every poll.
        internal event EventHandler<MapleRebindReason> Rebound;

        private void OnRebound(MapleRebindReason reason)
        {
            EventHandler<MapleRebindReason> handler = Rebound;
            if (handler != null) handler(this, reason);
        }

        // ---------------------------------------------------------------------------
        // Win32 plumbing (read-only, external capture boundary respected)
        // ---------------------------------------------------------------------------

        private const int GW_OWNER = 4;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        // The least privilege that still allows reading the image name; no VM_READ, no writes.
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

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

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, int command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr handle, ref POINT point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int maximum);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(
            uint desiredAccess, bool inheritHandle, uint processId);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags,
            StringBuilder name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private static string WindowText(IntPtr handle)
        {
            try
            {
                int length = GetWindowTextLength(handle);
                if (length <= 0) return "";
                StringBuilder buffer = new StringBuilder(length + 2);
                GetWindowText(handle, buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch { return ""; }
        }

        private static string ClassName(IntPtr handle)
        {
            try
            {
                StringBuilder buffer = new StringBuilder(256);
                GetClassName(handle, buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch { return ""; }
        }

        // PerMonitorV2 DPI is requested by the application manifest, so window rectangles are
        // already physical pixels; the DPI value is kept for diagnostics and for detecting a
        // monitor change that resizes the client.
        private static int DpiOf(IntPtr handle)
        {
            try
            {
                uint dpi = GetDpiForWindow(handle);
                if (dpi >= 72 && dpi <= 480) return (int)dpi;
            }
            catch { }
            return 96;
        }

        // Resolving a process name through System.Diagnostics creates a full Process object,
        // and the enumerator asks for one name per top-level window. On a normal desktop that
        // made a single idle poll cost tens of milliseconds, which is far too much for a
        // component whose whole job is to sit quietly. QueryFullProcessImageName is a single
        // read-only call and is measured to be an order of magnitude cheaper.
        private static string ProcessNameOf(uint processId)
        {
            if (processId == 0) return "";
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (handle == IntPtr.Zero) return "";
                StringBuilder buffer = new StringBuilder(1024);
                int length = buffer.Capacity;
                if (!QueryFullProcessImageName(handle, 0, buffer, ref length)) return "";
                string path = buffer.ToString(0, length);
                int separator = path.LastIndexOf('\\');
                string name = separator >= 0 ? path.Substring(separator + 1) : path;
                // Callers expect the name without an extension, matching Process.ProcessName.
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);
                return name;
            }
            catch { return ""; }
            finally
            {
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }
    }
}
