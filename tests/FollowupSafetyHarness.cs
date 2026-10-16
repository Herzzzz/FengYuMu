using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using MapleOverlay;

public static class FollowupSafetyHarness
{
    private static int passed;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }

    public static async Task<int> Run(string root, string shell, string fixture)
    {
        passed = 0;
        Func<string, CaptureProcessSession> create = delegate(string suffix) {
            return new CaptureProcessSession(new ProcessStartInfo {
                FileName = shell, Arguments = "-NoLogo -NoProfile -File \"" + fixture + "\" -PidFile \"" + Path.Combine(root, suffix + ".pid") + "\"",
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true
            }, 3000, 150);
        };
        using (CaptureProcessSession session = create("worker"))
        {
            Check(await session.SendAsync("PING", CancellationToken.None) == "OK:PING", "worker handshake and actual pipe request");
            int pid = Int32.Parse(File.ReadAllText(Path.Combine(root, "worker.pid")));
            Check(await session.SendAsync("NEXT", CancellationToken.None) == "OK:NEXT" &&
                pid == Int32.Parse(File.ReadAllText(Path.Combine(root, "worker.pid"))), "healthy worker reused");
            bool timedOut = false; Stopwatch watch = Stopwatch.StartNew();
            try { await session.SendAsync("HANG", CancellationToken.None); } catch (TimeoutException) { timedOut = true; }
            Check(timedOut && watch.ElapsedMilliseconds < 2000, "hung worker has bounded timeout");
            Check(await Gone(pid), "only owned hung process terminated");
        }
        using (CaptureProcessSession session = create("cancel"))
        {
            await session.SendAsync("PING", CancellationToken.None);
            int pid = Int32.Parse(File.ReadAllText(Path.Combine(root, "cancel.pid")));
            bool cancelled = false;
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.CancelAfter(40);
                try { await session.SendAsync("HANG", cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            }
            Check(cancelled && await Gone(pid), "cancel aborts owned worker and actual pending pipe read");
            Check(await session.SendAsync("AGAIN", CancellationToken.None) == "OK:AGAIN", "new capture succeeds after cancellation");
        }
        string component = Path.Combine(root, "StrongOcr"); Directory.CreateDirectory(component);
        List<string> manifest = new List<string>();
        foreach (string name in new string[] { "StrongOcrHelper.exe", "Sdcb.SimdPaddleOCR.dll", "Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll" })
        {
            File.WriteAllText(Path.Combine(component, name), "fixture-data");
            manifest.Add(name + "\t" + UpdateFileTransaction.Hash(Path.Combine(component, name)));
        }
        File.WriteAllLines(Path.Combine(component, StrongOcrComponent.ManifestName), manifest);
        StrongOcrValidationCache cache = new StrongOcrValidationCache();
        Check(await cache.ValidateAsync(component), "component fully verified before use");
        for (int i = 0; i < 100; i++) CheckCache(cache.IsAvailable(component));
        Check(cache.ValidationCount == 1, "100 unchanged availability checks reuse one full hash validation");
        string helper = Path.Combine(component, "StrongOcrHelper.exe");
        File.WriteAllText(helper, "changed-length-data");
        Check(!await cache.ValidateAsync(component), "component metadata change causes fresh hash rejection");
        string repair = Path.Combine(root, "repair"); Directory.CreateDirectory(repair);
        File.WriteAllText(Path.Combine(repair, "helper.exe"), "fixture-data");
        UpdateFileTransaction.Execute(component, repair, Path.Combine(root, "repair-backup"), new List<UpdateFilePlan> {
            new UpdateFilePlan { SourceName = "helper.exe", TargetName = "StrongOcrHelper.exe",
                Sha256 = UpdateFileTransaction.Hash(Path.Combine(repair, "helper.exe")) } });
        Check(await cache.ValidateAsync(component), "transactionally repaired component can recover");
        DateTime originalTime = File.GetLastWriteTimeUtc(helper);
        File.WriteAllText(helper, "tampered----"); File.SetLastWriteTimeUtc(helper, originalTime);
        typeof(StrongOcrValidationCache).GetField("checkedAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(cache, DateTime.UtcNow.AddMinutes(-2));
        Check(!await cache.ValidateAsync(component), "periodic full check rejects same-size/time tampering");
        Console.WriteLine("FOLLOWUP_PASSED=" + passed);
        return passed;
    }
    private static void CheckCache(bool value) { if (!value) throw new Exception("unchanged component rejected"); }
    private static async Task<bool> Gone(int pid)
    {
        for (int i = 0; i < 50; i++)
        {
            try { using (Process p = Process.GetProcessById(pid)) { if (p.HasExited) return true; } }
            catch (ArgumentException) { return true; }
            await Task.Delay(20);
        }
        return false;
    }
}
