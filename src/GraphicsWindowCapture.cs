using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Size = System.Drawing.Size;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace MapleOverlay
{
    // Target-HWND capture runs only inside the killable capture worker. Unlike a client
    // DC, the compositor's target surface does not include our top-level translation UI.
    // One persistent session avoids capture-border cycling; commands accept only post-request QPC frames.
    // No display-affinity flag, desktop capture, cached bitmap, injection or input hook.
    internal sealed class GraphicsWindowCapture : IDisposable
    {
        private IntPtr nativeDevice, context;
        private IDirect3DDevice device;
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct SampleDescription { internal uint Count, Quality; }
        [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
        {
            internal uint Width, Height, MipLevels, ArraySize, Format;
            internal SampleDescription Sample;
            internal uint Usage, BindFlags, CpuAccessFlags, MiscFlags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MappedResource { internal IntPtr Data; internal uint RowPitch, DepthPitch; }
        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface CaptureItemInterop
        {
            void CreateForWindow(IntPtr window, ref Guid iid, out IntPtr item);
            void CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr item);
        }
        [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface SurfaceInterop { void GetInterface(ref Guid iid, out IntPtr result); }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture(IntPtr self, ref TextureDescription description, IntPtr initial, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescription(IntPtr self, out TextureDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResource(IntPtr self, IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MapResource(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint flags, out MappedResource mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UnmapResource(IntPtr self, IntPtr resource, uint subresource);
        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software,
            uint flags, IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out int feature, out IntPtr context);
        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect rect, int size);

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(method, typeof(T));
        }
        private void EnsureDevice()
        {
            if (device != null) return;
            int level;
            int result = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                out nativeDevice, out level, out context);
            if (result < 0)
            {
                ReleaseDevice();
                result = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                    out nativeDevice, out level, out context);
            }
            Marshal.ThrowExceptionForHR(result);
            IntPtr dxgi = IntPtr.Zero, inspectable = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(nativeDevice, ref iid, out dxgi));
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
                device = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable);
            }
            finally { if (inspectable != IntPtr.Zero) Marshal.Release(inspectable); if (dxgi != IntPtr.Zero) Marshal.Release(dxgi); }
        }

        private readonly object frameGate = new object();
        private readonly ManualResetEvent frameReady = new ManualResetEvent(false);
        private Direct3D11CaptureFramePool pool;
        private GraphicsCaptureSession session;
        private Direct3D11CaptureFrame acceptedFrame;
        private IntPtr targetWindow;
        private uint targetProcess;
        private Size targetClientSize;
        private long requestedTicks, acceptedTicks;
        private readonly object operationGate = new object();
        private readonly Timer idleTimer;
        private long lastActivity;
        private bool disposed;
        internal GraphicsWindowCapture()
        {
            lastActivity = Stopwatch.GetTimestamp();
            idleTimer = new Timer(ReleaseIdleSession, null, 1000, 1000);
        }
        private void ReleaseIdleSession(object state)
        {
            if (!Monitor.TryEnter(operationGate)) return;
            try
            {
                if (!disposed && pool != null && Stopwatch.GetTimestamp() - lastActivity > Stopwatch.Frequency * 2)
                    CloseSession();
            }
            catch { /* Worker timeout/lifetime remains the outer native isolation boundary. */ }
            finally { Monitor.Exit(operationGate); }
        }

        private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (frameGate)
            {
                if (!Object.ReferenceEquals(sender, pool)) return;
                Direct3D11CaptureFrame frame = null;
                try
                {
                    frame = sender.TryGetNextFrame();
                    if (frame == null || requestedTicks == 0 || acceptedFrame != null ||
                        !CaptureFramePolicy.IsFresh(frame.SystemRelativeTime.Ticks, requestedTicks)) return;
                    acceptedTicks = frame.SystemRelativeTime.Ticks;
                    acceptedFrame = frame; frame = null; requestedTicks = 0; frameReady.Set();
                }
                catch { /* A bounded request fails closed; never reuse an earlier frame. */ }
                finally { if (frame != null) frame.Dispose(); }
            }
        }
        private void CloseSession()
        {
            Direct3D11CaptureFramePool oldPool;
            GraphicsCaptureSession oldSession;
            lock (frameGate)
            {
                oldPool = pool; oldSession = session; pool = null; session = null; requestedTicks = 0;
                if (acceptedFrame != null) { acceptedFrame.Dispose(); acceptedFrame = null; }
            }
            // Close outside the callback lock: native close may wait for a queued callback.
            if (oldPool != null) oldPool.FrameArrived -= FrameArrived;
            if (oldSession != null) oldSession.Dispose();
            if (oldPool != null) oldPool.Dispose();
        }
        internal Bitmap Capture(IntPtr window, uint process, Rectangle client)
        {
            lock (operationGate)
            {
                if (disposed) throw new ObjectDisposedException("GraphicsWindowCapture");
                try { return CaptureCore(window, process, client); }
                finally { lastActivity = Stopwatch.GetTimestamp(); }
            }
        }
        private Bitmap CaptureCore(IntPtr window, uint process, Rectangle client)
        {
            if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("graphics-capture-unavailable");
            EnsureDevice();
            bool startSession = false;
            if (pool == null || targetWindow != window || targetProcess != process || targetClientSize != client.Size)
            {
                CloseSession();
                IntPtr pointer = IntPtr.Zero;
                GraphicsCaptureItem item;
                Guid iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                CaptureItemInterop factory = (CaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
                try { factory.CreateForWindow(window, ref iid, out pointer); item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(pointer); }
                finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
                lock (frameGate)
                {
                    pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                    session = pool.CreateCaptureSession(item);
                    targetWindow = window; targetProcess = process; targetClientSize = client.Size;
                    pool.FrameArrived += FrameArrived;
                }
                startSession = true;
            }
            long requestStarted;
            lock (frameGate)
            {
                if (acceptedFrame != null) { acceptedFrame.Dispose(); acceptedFrame = null; }
                frameReady.Reset();
                requestStarted = requestedTicks = CaptureFramePolicy.CounterTicks(Stopwatch.GetTimestamp(), Stopwatch.Frequency);
            }
            try
            {
                if (startSession) session.StartCapture();
                if (!frameReady.WaitOne(350)) throw new TimeoutException("graphics-capture-no-fresh-frame");
                Direct3D11CaptureFrame frame;
                lock (frameGate)
                {
                    frame = acceptedFrame;
                    if (frame == null) throw new InvalidOperationException("graphics-capture-lost-frame");
                }
                // operationGate keeps the session alive. Do not hold the callback lock
                // across GPU readback or file logging: incoming frames must drain promptly.
                Program.LogRuntimeEvent("capture-worker-frame", "backend=windows-graphics-capture fresh=true" +
                    " requestedTicks=" + requestStarted + " frameTicks=" + acceptedTicks + " width=" + client.Width + " height=" + client.Height);
                return ReadFrame(frame, window, client);
            }
            finally
            {
                lock (frameGate)
                {
                    requestedTicks = 0;
                    if (acceptedFrame != null) { acceptedFrame.Dispose(); acceptedFrame = null; }
                }
            }
        }

        private Bitmap ReadFrame(Direct3D11CaptureFrame frame, IntPtr window, Rectangle client)
        {
            NativeRect outer, extended;
            if (!GetWindowRect(window, out outer)) throw new InvalidOperationException("graphics-capture-window-bounds");
            Rectangle dwm = Rectangle.Empty;
            if (DwmGetWindowAttribute(window, 9, out extended, Marshal.SizeOf(typeof(NativeRect))) == 0)
                dwm = Rectangle.FromLTRB(extended.Left, extended.Top, extended.Right, extended.Bottom);
            Rectangle crop;
            if (!CaptureFramePolicy.TryClientCrop(new Size(frame.ContentSize.Width, frame.ContentSize.Height), client,
                dwm, Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom), out crop))
            {
                Program.LogRuntimeEvent("capture-worker-geometry", "frame=" + frame.ContentSize.Width + "x" + frame.ContentSize.Height +
                    " client=" + client + " extended=" + dwm + " outer=" + Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom));
                throw new InvalidOperationException("graphics-capture-geometry-mismatch");
            }
            IntPtr texture = IntPtr.Zero, staging = IntPtr.Zero;
            bool mapped = false; Bitmap bitmap = null;
            try
            {
                Guid iid = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
                ((SurfaceInterop)frame.Surface).GetInterface(ref iid, out texture);
                TextureDescription description;
                Method<GetDescription>(texture, 10)(texture, out description);
                if (description.Format != 87 || description.Width < frame.ContentSize.Width || description.Height < frame.ContentSize.Height)
                    throw new InvalidOperationException("graphics-capture-texture-format");
                description.Usage = 3; description.BindFlags = 0; description.CpuAccessFlags = 0x20000; description.MiscFlags = 0;
                Marshal.ThrowExceptionForHR(Method<CreateTexture>(nativeDevice, 5)(nativeDevice, ref description, IntPtr.Zero, out staging));
                Method<CopyResource>(context, 47)(context, staging, texture);
                MappedResource readback;
                Marshal.ThrowExceptionForHR(Method<MapResource>(context, 14)(context, staging, 0, 1, 0, out readback));
                mapped = true;
                if (readback.Data == IntPtr.Zero || readback.RowPitch < description.Width * 4 || readback.RowPitch > Int32.MaxValue)
                    throw new InvalidOperationException("graphics-capture-row-pitch");
                bitmap = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppRgb);
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, crop.Width, crop.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[checked(crop.Width * 4)];
                    for (int y = 0; y < crop.Height; y++)
                    {
                        int source = checked((crop.Top + y) * (int)readback.RowPitch + crop.Left * 4);
                        Marshal.Copy(IntPtr.Add(readback.Data, source), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
                    }
                }
                finally { bitmap.UnlockBits(data); }
                Bitmap result = bitmap; bitmap = null; return result;
            }
            finally
            {
                if (bitmap != null) bitmap.Dispose();
                if (mapped) Method<UnmapResource>(context, 15)(context, staging, 0);
                if (staging != IntPtr.Zero) Marshal.Release(staging);
                if (texture != IntPtr.Zero) Marshal.Release(texture);
            }
        }
        private void ReleaseDevice()
        {
            if (device != null) { device.Dispose(); device = null; }
            if (context != IntPtr.Zero) { Marshal.Release(context); context = IntPtr.Zero; }
            if (nativeDevice != IntPtr.Zero) { Marshal.Release(nativeDevice); nativeDevice = IntPtr.Zero; }
        }
        public void Dispose()
        {
            idleTimer.Dispose();
            lock (operationGate)
            {
                if (disposed) return;
                disposed = true; CloseSession(); ReleaseDevice(); frameReady.Dispose();
            }
        }
    }
}
