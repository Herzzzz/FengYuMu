using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MapleOverlay
{
    internal sealed class WindowCaptureFrame : IDisposable
    {
        internal Bitmap Bitmap;
        internal Rectangle Bounds;
        internal long Version;
        public void Dispose() { if (Bitmap != null) Bitmap.Dispose(); }
    }

    internal sealed class WindowCaptureClient : IDisposable
    {
        private readonly CaptureProcessSession session;
        private readonly string outputRoot;
        internal WindowCaptureClient()
        {
            string executable = typeof(WindowCaptureClient).Assembly.Location;
            outputRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "运行临时", "WindowCapture");
            session = new CaptureProcessSession(new ProcessStartInfo {
                FileName = executable, Arguments = "--window-capture-worker=" + Process.GetCurrentProcess().Id,
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
            }, 1500, 650);
        }

        internal Task WarmAsync() { return session.WarmAsync(); }
        internal async Task<Bitmap> CaptureAsync(MapleWindowInfo target, Rectangle bounds,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputRoot);
            string name = Guid.NewGuid().ToString("N") + ".png";
            string path = Path.Combine(outputRoot, name);
            try
            {
                string command = FormatRequest(target.ProcessId, target.Handle, bounds, name);
                string reply = await session.SendAsync(command, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (reply != "OK|windows-graphics-capture")
                {
                    string reason = Regex.IsMatch(reply ?? "", @"^FAILED\|[a-z-]{1,40}$")
                        ? reply.Substring(7) : "worker-protocol";
                    Program.LogRuntimeEvent("capture-refused", "reason=" + reason);
                    throw new InvalidOperationException("目标窗口未返回新的独立画面，已暂停。请保持游戏窗口可见并检查系统图形捕获支持后重试。");
                }
                Program.LogRuntimeEvent("capture-complete", "backend=" + reply.Substring(3) +
                    " width=" + bounds.Width + " height=" + bounds.Height);
                using (Bitmap saved = new Bitmap(path)) return new Bitmap(saved);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { Task ignored = Task.Run(async delegate {
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        await Task.Delay(30).ConfigureAwait(false);
                        try { if (File.Exists(path)) File.Delete(path); return; } catch (IOException) { }
                    }
                }); }
            }
        }
        public void Dispose() { session.Dispose(); }

        internal static string FormatRequest(uint pid, IntPtr handle, Rectangle bounds, string name)
        {
            return String.Join("|", new string[] {
                pid.ToString(CultureInfo.InvariantCulture), handle.ToInt64().ToString(CultureInfo.InvariantCulture),
                bounds.X.ToString(CultureInfo.InvariantCulture), bounds.Y.ToString(CultureInfo.InvariantCulture),
                bounds.Width.ToString(CultureInfo.InvariantCulture), bounds.Height.ToString(CultureInfo.InvariantCulture), name });
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { internal int X, Y; }
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

        internal static void RunWorker(int parentId)
        {
            // If the parent disappears while native graphics capture is stuck, this watchdog exits the
            // entire worker. No background thread in the product owns a live native bitmap.
            Task.Run(delegate {
                try { using (Process parent = Process.GetProcessById(parentId)) parent.WaitForExit(); }
                catch { }
                Environment.Exit(0);
            });
            Console.Out.WriteLine("READY"); Console.Out.Flush();
            using (GraphicsWindowCapture graphicsCapture = new GraphicsWindowCapture())
            {
            string command;
            while ((command = Console.In.ReadLine()) != null)
            {
                string result = "FAILED";
                string stage = "request-parse";
                try
                {
                    string[] parts = command.Split('|');
                    if (parts.Length != 7 || !Regex.IsMatch(parts[6], @"^[0-9a-f]{32}\.png$"))
                        throw new InvalidDataException();
                    uint pid = UInt32.Parse(parts[0], CultureInfo.InvariantCulture);
                    IntPtr handle = new IntPtr(Int64.Parse(parts[1], CultureInfo.InvariantCulture));
                    Rectangle expected = new Rectangle(Int32.Parse(parts[2], CultureInfo.InvariantCulture),
                        Int32.Parse(parts[3], CultureInfo.InvariantCulture), Int32.Parse(parts[4], CultureInfo.InvariantCulture),
                        Int32.Parse(parts[5], CultureInfo.InvariantCulture));
                    if (expected.Width < 1 || expected.Height < 1 || (long)expected.Width * expected.Height > 64000000)
                        throw new InvalidDataException();
                    stage = "target-before";
                    if (!MatchesTarget(handle, pid, expected)) throw new InvalidOperationException();
                    stage = "windows-graphics-capture";
                    using (Bitmap bitmap = graphicsCapture.Capture(handle, pid, expected))
                    {
                        stage = "target-after";
                        if (!MatchesTarget(handle, pid, expected)) throw new InvalidOperationException();
                        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "运行临时", "WindowCapture");
                        Directory.CreateDirectory(root);
                        stage = "save-image";
                        bitmap.Save(Path.Combine(root, parts[6]), ImageFormat.Png);
                        result = "OK|windows-graphics-capture";
                    }
                }
                catch (Exception ex)
                {
                    result = "FAILED|" + stage;
                    string reason = Regex.IsMatch(ex.Message ?? "", @"^graphics-capture-[a-z-]{1,50}$") ? ex.Message : ex.GetType().Name;
                    Program.LogRuntimeEvent("capture-worker-failed", "stage=" + stage + " reason=" + reason +
                        " hresult=" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
                }
                Console.Out.WriteLine(result); Console.Out.Flush();
            }
            }
        }

        private static bool MatchesTarget(IntPtr handle, uint expectedPid, Rectangle expected)
        {
            uint pid; GetWindowThreadProcessId(handle, out pid);
            RECT rect; POINT origin = new POINT();
            return pid == expectedPid && IsWindowVisible(handle) && !IsIconic(handle) &&
                GetClientRect(handle, out rect) && ClientToScreen(handle, ref origin) &&
                expected == new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

    }
}
