using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MapleOverlay
{
    internal enum StrongOcrStatus
    {
        Success = 0,
        Unavailable = 1,
        TimedOut = 2,
        ProcessFailed = 3,
        InvalidOutput = 4,
        SaveFailed = 5
    }

    internal sealed class StrongOcrLine
    {
        public string Text = "";
        public float Confidence;
        public RectangleF Bounds;
    }

    internal sealed class StrongOcrResult
    {
        public StrongOcrStatus Status;
        public string Reason = "";
        public int ExitCode = -1;
        public long ElapsedMilliseconds;
        public long ModelLoadMilliseconds;
        public long RecognitionMilliseconds;
        public readonly List<StrongOcrLine> Lines = new List<StrongOcrLine>();

        public bool Succeeded { get { return Status == StrongOcrStatus.Success; } }
    }

    // Starts the optional x64 OCR helper in a separate process. The helper receives only one
    // already-cropped PNG; it never searches for a game window and it has no access to the
    // caller's screen capture code. Every failure is represented as a result so the existing
    // Windows OCR labels remain the final fallback.
    internal sealed class StrongOcrClient
    {
        internal const int DefaultTimeoutMilliseconds = 2500;
        internal const int MaximumOutputLines = 512;
        internal const int MaximumOutputCharacters = 1024 * 1024;
        internal const float MinimumConfidence = 0.42f;

        private readonly string helperPath;
        private readonly string tempRoot;
        private readonly int timeoutMilliseconds;

        internal StrongOcrClient(string helperPath, string tempRoot, int timeoutMilliseconds)
        {
            this.helperPath = helperPath ?? "";
            this.tempRoot = String.IsNullOrWhiteSpace(tempRoot)
                ? Path.Combine(Path.GetTempPath(), "FengYuMu", "StrongOcr") : tempRoot;
            this.timeoutMilliseconds = Math.Max(50, timeoutMilliseconds);
        }

        internal static StrongOcrClient CreateDefault(string baseDirectory)
        {
            string root = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            string helper = Path.Combine(root,
                "StrongOcr", "StrongOcrHelper.exe");
            return new StrongOcrClient(helper,
                Path.Combine(root, "运行临时", "StrongOcr"),
                DefaultTimeoutMilliseconds);
        }

        internal bool IsAvailable
        {
            get
            {
                if (!File.Exists(helperPath)) return false;
                string folder = Path.GetDirectoryName(helperPath) ?? "";
                return File.Exists(Path.Combine(folder, "Sdcb.SimdPaddleOCR.dll")) &&
                    File.Exists(Path.Combine(folder,
                        "Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll"));
            }
        }

        internal async Task<StrongOcrResult> RecognizeAsync(Bitmap bitmap)
        {
            Stopwatch watch = Stopwatch.StartNew();
            if (bitmap == null || bitmap.Width < 2 || bitmap.Height < 2)
                return Failure(StrongOcrStatus.SaveFailed, "截图为空", watch, -1);
            if (!IsAvailable)
                return Failure(StrongOcrStatus.Unavailable, "增强 OCR 组件未安装", watch, -1);

            string imagePath = "";
            Process process = null;
            try
            {
                Directory.CreateDirectory(tempRoot);
                imagePath = Path.Combine(tempRoot,
                    "panel-" + Guid.NewGuid().ToString("N") + ".png");
                bitmap.Save(imagePath, ImageFormat.Png);

                ProcessStartInfo start = new ProcessStartInfo {
                    FileName = helperPath,
                    Arguments = QuoteWindowsArgument(imagePath),
                    WorkingDirectory = Path.GetDirectoryName(helperPath) ?? "",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };
                process = new Process { StartInfo = start, EnableRaisingEvents = true };
                StringBuilder stdout = new StringBuilder();
                StringBuilder stderr = new StringBuilder();
                object outputGate = new object();
                int outputLines = 0, errorLines = 0;
                bool outputOverflow = false;
                TaskCompletionSource<bool> outputClosed =
                    new TaskCompletionSource<bool>();
                TaskCompletionSource<bool> errorClosed =
                    new TaskCompletionSource<bool>();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) {
                    if (args.Data == null) { outputClosed.TrySetResult(true); return; }
                    lock (outputGate)
                    {
                        outputLines++;
                        if (outputLines > MaximumOutputLines ||
                            stdout.Length + args.Data.Length + 1 > MaximumOutputCharacters)
                            outputOverflow = true;
                        else stdout.AppendLine(args.Data);
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) {
                    if (args.Data == null) { errorClosed.TrySetResult(true); return; }
                    lock (outputGate)
                    {
                        errorLines++;
                        if (errorLines > MaximumOutputLines ||
                            stderr.Length + args.Data.Length + 1 > MaximumOutputCharacters)
                            outputOverflow = true;
                        else stderr.AppendLine(args.Data);
                    }
                };
                if (!process.Start())
                    return Failure(StrongOcrStatus.ProcessFailed,
                        "增强 OCR 进程未启动", watch, -1);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Task exit = Task.Factory.StartNew(delegate { process.WaitForExit(); },
                    System.Threading.CancellationToken.None,
                    TaskCreationOptions.None, TaskScheduler.Default);
                Task finished = await Task.WhenAny(exit, Task.Delay(timeoutMilliseconds));
                if (finished != exit)
                {
                    SafeKill(process);
                    await WaitBriefly(exit);
                    return Failure(StrongOcrStatus.TimedOut,
                        "增强 OCR 超时，已退回系统识别", watch, -1);
                }
                await Task.WhenAny(Task.WhenAll(outputClosed.Task, errorClosed.Task),
                    Task.Delay(250));
                int exitCode = process.ExitCode;
                if (outputOverflow)
                    return Failure(StrongOcrStatus.InvalidOutput,
                        "增强 OCR 输出超过安全上限", watch, exitCode);
                if (exitCode != 0)
                    return Failure(StrongOcrStatus.ProcessFailed,
                        SafeReason(stderr.ToString(), "增强 OCR 退出码 " + exitCode),
                        watch, exitCode);
                StrongOcrResult parsed = ParseOutput(stdout.ToString(), bitmap.Size);
                parsed.ElapsedMilliseconds = watch.ElapsedMilliseconds;
                parsed.ExitCode = exitCode;
                return parsed;
            }
            catch (Exception error)
            {
                SafeKill(process);
                return Failure(StrongOcrStatus.ProcessFailed,
                    error.GetType().Name + "：" + error.Message, watch, -1);
            }
            finally
            {
                if (process != null) process.Dispose();
                DeleteExactTemporaryImage(imagePath);
            }
        }

        internal static StrongOcrResult ParseOutput(string jsonLines, Size imageSize)
        {
            StrongOcrResult result = new StrongOcrResult {
                Status = StrongOcrStatus.InvalidOutput };
            if (String.IsNullOrWhiteSpace(jsonLines))
            { result.Reason = "增强 OCR 没有返回内容"; return result; }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumOutputCharacters;
            bool summarySeen = false, summaryOk = false;
            HashSet<string> duplicates = new HashSet<string>(StringComparer.Ordinal);
            string[] rows = jsonLines.Replace("\r\n", "\n").Split('\n');
            if (rows.Length > MaximumOutputLines + 2)
            { result.Reason = "增强 OCR 返回行数超过安全上限"; return result; }
            try
            {
                foreach (string raw in rows)
                {
                    string row = raw.Trim();
                    if (row.Length == 0) continue;
                    Dictionary<string, object> value =
                        serializer.Deserialize<Dictionary<string, object>>(row);
                    bool summary = ReadBoolean(value, "summary", false);
                    if (summary)
                    {
                        summarySeen = true;
                        summaryOk = ReadBoolean(value, "ok", false);
                        result.ModelLoadMilliseconds = ReadLong(value, "load_ms", 0);
                        result.RecognitionMilliseconds = ReadLong(value, "run_ms", 0);
                        continue;
                    }
                    string text = ReadString(value, "text").Trim();
                    double rawConfidence = ReadDouble(value, "confidence", -1.0);
                    double confidence = NormalizeConfidence(rawConfidence);
                    double x = ReadDouble(value, "x", Double.NaN);
                    double y = ReadDouble(value, "y", Double.NaN);
                    double width = ReadDouble(value, "width", Double.NaN);
                    double height = ReadDouble(value, "height", Double.NaN);
                    if (text.Length == 0 || confidence < MinimumConfidence ||
                        !IsFinite(x) || !IsFinite(y) || !IsFinite(width) || !IsFinite(height) ||
                        width <= 0.0 || height <= 0.0) continue;
                    RectangleF bounds = ClampBounds(new RectangleF((float)x, (float)y,
                        (float)width, (float)height), imageSize);
                    if (bounds.Width < 1.0f || bounds.Height < 1.0f) continue;
                    string identity = NormalizeIdentity(text) + "|" +
                        Math.Round(bounds.X).ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(bounds.Y).ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(bounds.Width).ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(bounds.Height).ToString(CultureInfo.InvariantCulture);
                    if (!duplicates.Add(identity)) continue;
                    result.Lines.Add(new StrongOcrLine {
                        Text = text, Confidence = (float)confidence, Bounds = bounds });
                }
            }
            catch (Exception error)
            {
                result.Lines.Clear();
                result.Reason = "增强 OCR JSON 无效：" + error.GetType().Name;
                return result;
            }
            if (!summarySeen || !summaryOk)
            {
                result.Lines.Clear();
                result.Reason = "增强 OCR 缺少成功摘要";
                return result;
            }
            if (result.Lines.Count == 0)
            { result.Reason = "增强 OCR 没有可信文字"; return result; }
            result.Status = StrongOcrStatus.Success;
            result.Reason = "";
            return result;
        }

        // CommandLineToArgvW-compatible quoting, required because net48 has no ArgumentList.
        internal static string QuoteWindowsArgument(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder quoted = new StringBuilder(value.Length + 2);
            quoted.Append('"');
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"')
                {
                    quoted.Append('\\', slashes * 2 + 1);
                    quoted.Append('"');
                    slashes = 0;
                    continue;
                }
                if (slashes > 0) { quoted.Append('\\', slashes); slashes = 0; }
                quoted.Append(c);
            }
            if (slashes > 0) quoted.Append('\\', slashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private void DeleteExactTemporaryImage(string imagePath)
        {
            if (String.IsNullOrEmpty(imagePath)) return;
            try
            {
                string root = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(imagePath);
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
                if (!String.Equals(Path.GetExtension(candidate), ".png",
                    StringComparison.OrdinalIgnoreCase)) return;
                if (File.Exists(candidate)) File.Delete(candidate);
            }
            catch { }
        }

        private static RectangleF ClampBounds(RectangleF value, Size imageSize)
        {
            float left = Math.Max(0.0f, Math.Min(imageSize.Width, value.Left));
            float top = Math.Max(0.0f, Math.Min(imageSize.Height, value.Top));
            float right = Math.Max(left, Math.Min(imageSize.Width, value.Right));
            float bottom = Math.Max(top, Math.Min(imageSize.Height, value.Bottom));
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        // SimdPaddleOCR 2.x exposes the recognition score on a logit-like scale (real samples
        // are commonly around 7-23), while test/future helpers may emit a conventional 0-1
        // confidence. Convert both forms into one monotonic 0-1 value before filtering/merging.
        internal static double NormalizeConfidence(double value)
        {
            if (!IsFinite(value) || value < 0.0) return -1.0;
            if (value <= 1.0) return value;
            return 1.0 - Math.Exp(-value / 8.0);
        }

        private static string NormalizeIdentity(string value)
        {
            StringBuilder output = new StringBuilder();
            foreach (char c in (value ?? "").Trim().ToLowerInvariant())
                if (!Char.IsWhiteSpace(c)) output.Append(c);
            return output.ToString();
        }

        private static StrongOcrResult Failure(StrongOcrStatus status, string reason,
            Stopwatch watch, int exitCode)
        {
            return new StrongOcrResult { Status = status, Reason = reason ?? "",
                ExitCode = exitCode, ElapsedMilliseconds = watch.ElapsedMilliseconds };
        }

        private static async Task WaitBriefly(Task task)
        {
            if (task == null) return;
            try { await Task.WhenAny(task, Task.Delay(300)); }
            catch { }
        }

        private static void SafeKill(Process process)
        {
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); }
            catch { }
        }

        private static string SafeReason(string value, string fallback)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return fallback;
            return text.Length <= 300 ? text : text.Substring(0, 300);
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item) && item != null
                ? Convert.ToString(item, CultureInfo.InvariantCulture) : "";
        }

        private static bool ReadBoolean(Dictionary<string, object> value, string key,
            bool fallback)
        {
            object item;
            if (value == null || !value.TryGetValue(key, out item) || item == null) return fallback;
            bool parsed;
            return Boolean.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture),
                out parsed) ? parsed : fallback;
        }

        private static long ReadLong(Dictionary<string, object> value, string key, long fallback)
        {
            double parsed = ReadDouble(value, key, Double.NaN);
            return IsFinite(parsed) ? (long)Math.Round(parsed) : fallback;
        }

        private static double ReadDouble(Dictionary<string, object> value, string key,
            double fallback)
        {
            object item;
            if (value == null || !value.TryGetValue(key, out item) || item == null) return fallback;
            try { return Convert.ToDouble(item, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }
    }
}
