using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MapleOverlay
{
    // One private worker, one in-flight command. A hung native capture is terminated in
    // its own process; neither its DC nor its bitmap can outlive their owning process.
    internal sealed class CaptureProcessSession : IDisposable
    {
        private readonly ProcessStartInfo start;
        private readonly int startupTimeout, requestTimeout;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly object processGate = new object();
        private Process worker;
        private DateTime retryAfter;
        private bool disposed;

        internal CaptureProcessSession(ProcessStartInfo start, int startupTimeout, int requestTimeout)
        { this.start = start; this.startupTimeout = startupTimeout; this.requestTimeout = requestTimeout; }

        internal async Task WarmAsync()
        {
            try { await SendAsync(null, CancellationToken.None).ConfigureAwait(false); }
            catch { /* Capture reports availability and the bounded failure to its caller. */ }
        }

        internal async Task<string> SendAsync(string command, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetime.Token))
            {
                CancellationToken token = linked.Token;
                await gate.WaitAsync(token).ConfigureAwait(false);
                Process active = null;
                try
                {
                    token.ThrowIfCancellationRequested();
                    lock (processGate)
                    {
                        if (disposed) throw new ObjectDisposedException("CaptureProcessSession");
                        active = worker;
                    }
                    if (active == null || active.HasExited)
                    {
                        if (DateTime.UtcNow < retryAfter) throw new IOException("截图进程暂不可用，请稍后重试");
                        AbortWorker(active);
                        active = new Process { StartInfo = start };
                        if (!active.Start()) throw new IOException("截图进程无法启动");
                        lock (processGate)
                        {
                            if (disposed) { try { active.Kill(); } catch { } active.Dispose(); throw new ObjectDisposedException("CaptureProcessSession"); }
                            worker = active;
                        }
                        string ready = await WithinAsync(active.StandardOutput.ReadLineAsync(), startupTimeout, token).ConfigureAwait(false);
                        if (ready != "READY") throw new IOException("截图进程初始化失败");
                    }
                    if (command == null) return "READY";
                    await WithinAsync(WriteCommandAsync(active.StandardInput, command), requestTimeout, token).ConfigureAwait(false);
                    string result = await WithinAsync(active.StandardOutput.ReadLineAsync(), requestTimeout, token).ConfigureAwait(false);
                    if (result == null) throw new IOException("截图进程已退出");
                    return result;
                }
                catch
                {
                    AbortWorker(active);
                    if (!token.IsCancellationRequested) retryAfter = DateTime.UtcNow.AddSeconds(2);
                    throw;
                }
                finally { gate.Release(); }
            }
        }

        private static async Task<string> WriteCommandAsync(StreamWriter writer, string command)
        {
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            return "sent";
        }

        private static async Task<string> WithinAsync(Task<string> work, int milliseconds,
            CancellationToken cancellationToken)
        {
            Task delay = Task.Delay(milliseconds, cancellationToken);
            if (await Task.WhenAny(work, delay).ConfigureAwait(false) != work)
            {
                Task observed = work.ContinueWith(delegate(Task<string> failed) { var ignored = failed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("目标窗口截图超时，已终止本次截图进程");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return await work.ConfigureAwait(false);
        }

        private void AbortWorker(Process expected)
        {
            Process owned = expected;
            lock (processGate)
            {
                if (owned == null) owned = worker;
                if (Object.ReferenceEquals(worker, owned)) worker = null;
            }
            if (owned == null) return;
            try { if (!owned.HasExited) owned.Kill(); } catch { }
            owned.Dispose();
        }

        public void Dispose()
        {
            lock (processGate) { if (disposed) return; disposed = true; }
            lifetime.Cancel();
            AbortWorker(null);
        }
    }
}
