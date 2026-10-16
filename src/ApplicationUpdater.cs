using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Web.Script.Serialization;

namespace MapleOverlay
{
    // One gate for "this process is driving the product automatically". The update flow ends
    // by starting the freshly installed program; under automation that launch would pop the
    // Windows "unknown publisher" prompt on the user's desktop, so it is skipped while the
    // rest of the update (verification, backup, replacement) still runs for real.
    internal static class AutomationPolicy
    {
        internal static bool SkipGuiRestart
        {
            get
            {
                try
                {
                    return String.Equals(Environment.GetEnvironmentVariable("FENGYUMU_AUTOMATION"),
                        "1", StringComparison.Ordinal);
                }
                catch { return false; }
            }
        }
    }

    internal sealed class PreparedApplicationUpdate
    {
        internal string Tag;
        internal string StageDirectory;
        internal string ManifestPath;
        internal bool HasChanges;

        internal void StartInstaller()
        {
            // See AutomationPolicy: this step must not start a GUI while driven automatically.
            if (AutomationPolicy.SkipGuiRestart) return;
            string installer = Path.Combine(StageDirectory, "枫语幕.exe");
            Process.Start(new ProcessStartInfo {
                FileName = installer,
                Arguments = "--apply-update=\"" + ManifestPath + "\"",
                WorkingDirectory = StageDirectory,
                UseShellExecute = true
            });
        }
    }

    internal sealed class ApplicationUpdateManifest
    {
        public string InstallDirectory { get; set; }
        public string StageDirectory { get; set; }
        public string BackupDirectory { get; set; }
        public string Tag { get; set; }
        public int OldProcessId { get; set; }
        public Dictionary<string, string> Hashes { get; set; }
    }

    // The mirror publishes this small manifest next to the ZIP and its SHA-256 file.
    // Keep the properties simple so the updater can validate the document before it
    // ever follows an asset URL.
    internal sealed class MirrorReleaseManifest
    {
        public string Tag { get; set; }
        public string ZipUrl { get; set; }
        public string Sha256Url { get; set; }
        public string Sha256 { get; set; }
    }

    internal static class ApplicationUpdater
    {
        private sealed class UpdateWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 12000;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null)
                {
                    http.ReadWriteTimeout = 12000;
                    http.AutomaticDecompression = DecompressionMethods.GZip |
                        DecompressionMethods.Deflate;
                    http.KeepAlive = true;
                }
                return request;
            }
        }

        private sealed class NetworkRoute
        {
            internal string Name;
            internal IWebProxy Proxy;
        }

        private const string LatestReleaseUrl =
            "https://api.github.com/repos/Herzzzz/FengYuMu/releases/latest";
        private const string LatestReleasePage =
            "https://github.com/Herzzzz/FengYuMu/releases/latest";
        // A bad, stale, or unavailable mirror never blocks the GitHub fallback.
        private const string DomesticMirrorManifestUrl =
            "https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/latest.json";
        // v3.2.4 的 ZIP、SHA-256 与清单已上传 Gitee 并逐字节校验通过，国内走镜像更快。
        // 清单里带固定 SHA-256，镜像异常时 UpdateWebClient 仍会回退 GitHub。
        private static readonly bool DomesticMirrorEnabled = true;
        private static readonly string[] PackageFiles = new string[] {
            "枫语幕.exe", "枫语幕词库.tsv", "使用说明.txt"
        };

        // Optional for compatibility with older three-file release packages.
        private static readonly string[] OptionalPackageFiles = new string[] {
            "基础补充词库.tsv", "任务正文.tsv"
        };

        private static bool IsPackageFile(string name)
        {
            return Array.IndexOf(PackageFiles, name) >= 0 ||
                Array.IndexOf(OptionalPackageFiles, name) >= 0;
        }

        internal static Task<PreparedApplicationUpdate> PrepareLatestAsync(string installDirectory)
        {
            return Task.Factory.StartNew(delegate {
                return PrepareLatest(installDirectory);
            });
        }

        private static PreparedApplicationUpdate PrepareLatest(string installDirectory)
        {
            return PrepareLatestCore(installDirectory, true);
        }

        private static PreparedApplicationUpdate PrepareLatestCore(string installDirectory, bool allowMirror)
        {
            string install = Path.GetFullPath(installDirectory);
            string updateRoot = Path.Combine(install, "更新临时");
            Directory.CreateDirectory(updateRoot);
            string tag = "", zipUrl = "", shaUrl = "", releaseDigest = "";
            string fixedSha256 = "";
            MirrorReleaseManifest mirror = allowMirror ? TryLoadDomesticMirrorManifest() : null;
            if (mirror != null)
            {
                // GitHub's latest tag is authoritative. Only an identical mirror release
                // may stand in for it; a reachable but stale mirror must not pin upgrades.
                try
                {
                    string officialTag = ResolveLatestTag();
                    if (!String.Equals(mirror.Tag, officialTag, StringComparison.OrdinalIgnoreCase)) mirror = null;
                }
                catch
                {
                    // A reachable, verified mirror remains usable when GitHub is blocked.
                    // It must still meet the local no-downgrade policy; latest is unknown.
                    try
                    {
                        if (ParseReleaseVersion(mirror.Tag).CompareTo(typeof(ApplicationUpdater).Assembly.GetName().Version) < 0)
                            mirror = null;
                        else Program.LogRuntimeEvent("update-official-unavailable", "using=verified-mirror latest=unknown");
                    }
                    catch { mirror = null; }
                }
            }
            if (mirror != null)
            {
                tag = mirror.Tag;
                zipUrl = mirror.ZipUrl;
                shaUrl = mirror.Sha256Url;
                fixedSha256 = mirror.Sha256;
            }
            else
            {
                try
                {
                    string json = DownloadText(LatestReleaseUrl);
                    Dictionary<string, object> release =
                        new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    tag = ReadString(release, "tag_name");
                    object assetsValue;
                    if (tag.Length == 0 || !release.TryGetValue("assets", out assetsValue))
                        throw new InvalidDataException("发布页没有完整版本信息");
                    string apiVersion = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                        ? tag.Substring(1) : tag;
                    string apiZipName = "FengYuMu_v" + apiVersion + ".zip";
                    foreach (object assetValue in ToArray(assetsValue))
                    {
                        Dictionary<string, object> asset = assetValue as Dictionary<string, object>;
                        if (asset == null) continue;
                        string name = ReadString(asset, "name");
                        if (String.Equals(name, apiZipName, StringComparison.OrdinalIgnoreCase))
                        {
                            zipUrl = ReadString(asset, "browser_download_url");
                            releaseDigest = ReadString(asset, "digest");
                        }
                        else if (String.Equals(name, apiZipName + ".sha256",
                            StringComparison.OrdinalIgnoreCase))
                            shaUrl = ReadString(asset, "browser_download_url");
                    }
                }
                catch (Exception)
                {
                    // Unauthenticated GitHub API access is rate limited per public IP.
                    // The ordinary latest-release redirect is not, so players still have a
                    // no-account recovery path instead of being stranded by an API 403.
                    tag = ""; zipUrl = ""; shaUrl = ""; releaseDigest = "";
                }
            }
            if (tag.Length == 0 || zipUrl.Length == 0 ||
                (shaUrl.Length == 0 && fixedSha256.Length == 0))
            {
                tag = ResolveLatestTag();
                string redirectedVersion = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tag.Substring(1) : tag;
                string redirectedZip = "FengYuMu_v" + redirectedVersion + ".zip";
                string baseUrl = "https://github.com/Herzzzz/FengYuMu/releases/latest/download/";
                zipUrl = baseUrl + redirectedZip;
                shaUrl = zipUrl + ".sha256";
                releaseDigest = "";
                fixedSha256 = "";
            }
            if (!Regex.IsMatch(tag, @"(?i)^v?\d+(?:\.\d+){1,3}(?:-[a-z0-9.-]+)?$"))
                throw new InvalidDataException("发布页版本号格式无效");
            string versionPart = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? tag.Substring(1) : tag;
            Version incoming = ParseReleaseVersion(tag);
            Version running = typeof(ApplicationUpdater).Assembly.GetName().Version;
            if (incoming.CompareTo(running) < 0)
                throw new InvalidDataException("最新发布的数值版本低于当前程序，已拒绝降级；测试版不自动退回旧稳定版");
            string expectedZipName = "FengYuMu_v" + versionPart + ".zip";

            string stage = Path.Combine(updateRoot, "v" + versionPart + "-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(stage);
            string zipPath = Path.Combine(stage, expectedZipName);
            string payload = Path.Combine(stage, "payload");
            try
            {
            DownloadFile(zipUrl, zipPath);
            string expectedHash = fixedSha256.Length > 0
                ? fixedSha256
                : ParseSha256Text(DownloadText(shaUrl), expectedZipName);
            if (!Regex.IsMatch(expectedHash ?? "", @"(?i)^[0-9a-f]{64}$"))
                throw new InvalidDataException("SHA-256 校验文件内容无效");
            string actualHash = ComputeSha256(zipPath);
            EnsureSha256Matches(expectedHash, actualHash);
            if (releaseDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(releaseDigest.Substring(7), actualHash,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("发布页摘要与安装包不一致，已停止更新");

            Directory.CreateDirectory(payload);
            ExtractRequiredFiles(zipPath, payload);
            }
            catch (Exception)
            {
                if (mirror == null) throw;
                Program.LogRuntimeEvent("update-mirror-fallback", "phase=download-hash-extract");
                return PrepareLatestCore(installDirectory, false);
            }
            Dictionary<string, string> hashes = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (string stagedFile in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
            {
                string name = stagedFile.Substring(payload.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                string hash = ComputeSha256(stagedFile);
                hashes[name] = hash;
                string currentFile = Path.Combine(install, name);
                if (name == "枫语幕词库.tsv" && File.Exists(currentFile)) continue;
                if (!File.Exists(currentFile) || !String.Equals(ComputeSha256(currentFile), hash,
                    StringComparison.OrdinalIgnoreCase)) changed = true;
            }
            string existingOcr = Path.Combine(install, "StrongOcr");
            if (Directory.Exists(existingOcr))
                foreach (string oldFile in Directory.GetFiles(existingOcr, "*", SearchOption.AllDirectories))
                {
                    string relative = oldFile.Substring(install.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    if (!hashes.ContainsKey(relative)) changed = true;
                }

            string backup = Path.Combine(install, "更新备份", tag + "-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            ApplicationUpdateManifest manifest = new ApplicationUpdateManifest {
                InstallDirectory = install,
                StageDirectory = payload,
                BackupDirectory = backup,
                Tag = tag,
                OldProcessId = Process.GetCurrentProcess().Id,
                Hashes = hashes
            };
            string manifestPath = Path.Combine(stage, "update-manifest.json");
            File.WriteAllText(manifestPath,
                new JavaScriptSerializer().Serialize(manifest), new UTF8Encoding(false));
            return new PreparedApplicationUpdate {
                Tag = tag, StageDirectory = payload, ManifestPath = manifestPath,
                HasChanges = changed
            };
        }

        internal static int ApplyPreparedUpdate(string manifestPath)
        {
            ApplicationUpdateManifest manifest = null;
            bool validated = false, oldStopped = false, safeToRestart = false;
            try
            {
                string fullManifest = Path.GetFullPath(manifestPath);
                manifest = new JavaScriptSerializer().Deserialize<ApplicationUpdateManifest>(
                    File.ReadAllText(fullManifest, Encoding.UTF8));
                ValidateManifest(manifest, fullManifest);
                validated = true;
                WaitForOldProcess(manifest.OldProcessId);
                oldStopped = true;
                List<UpdateFilePlan> plan = BuildInstallPlan(manifest);
                UpdateFileTransaction.Execute(manifest.InstallDirectory, manifest.StageDirectory,
                    manifest.BackupDirectory, plan);
                safeToRestart = true;
                SaveResult(true, "已安全更新到 " + manifest.Tag + "；已保留用户词库，官方词库另存。", "");
            }
            catch (Exception ex)
            {
                UpdateTransactionFailure transaction = ex as UpdateTransactionFailure;
                bool restored = transaction != null && transaction.RollbackSucceeded;
                safeToRestart = validated && oldStopped && (transaction == null || restored);
                string message = !validated ? "更新清单校验失败，未替换文件，也未使用清单路径回滚或启动。" :
                    transaction != null && transaction.ReplacementStarted ?
                        (restored ? "更新失败，本次已替换文件已验证回滚。" : "更新失败且回滚不完整，请从备份恢复；未启动程序。") :
                        "更新失败，尚未替换安装文件。";
                SaveResult(false, message, ex.GetType().Name + " HResult=" + ex.HResult +
                    (transaction == null ? "" : " phase=" + transaction.Phase + " file=" + transaction.Target +
                        " cause=" + transaction.InnerException.GetType().Name + " HResult=" + transaction.InnerException.HResult));
                Program.LogRuntimeEvent("update-failed", "validated=" + validated +
                    " oldStopped=" + oldStopped + " rollback=" + restored + " type=" + ex.GetType().Name);
                if (safeToRestart && !StartInstalledApplication(manifest.InstallDirectory))
                    SaveResult(false, message + "自动重启失败，请手动启动。", "restart-failed");
                return 3;
            }
            if (safeToRestart && !StartInstalledApplication(manifest.InstallDirectory))
            {
                SaveResult(false, "文件已更新，但自动重启失败，请手动启动。", "restart-failed");
                return 4;
            }
            return 0;
        }

        private static List<UpdateFilePlan> BuildInstallPlan(ApplicationUpdateManifest manifest)
        {
            List<UpdateFilePlan> plan = new List<UpdateFilePlan>();
            foreach (KeyValuePair<string, string> file in manifest.Hashes)
            {
                string target = file.Key;
                if (file.Key == "枫语幕词库.tsv" && File.Exists(Path.Combine(manifest.InstallDirectory, file.Key)))
                    target = "枫语幕官方词库-" + manifest.Tag + ".tsv";
                plan.Add(new UpdateFilePlan { SourceName = file.Key, TargetName = target, Sha256 = file.Value });
            }
            // Retire files not present in this package, so a partial old OCR runtime cannot
            // masquerade as the component supplied by a clean installation of this version.
            string oldOcr = Path.Combine(manifest.InstallDirectory, "StrongOcr");
            if (Directory.Exists(oldOcr))
                foreach (string file in Directory.GetFiles(oldOcr, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(manifest.InstallDirectory.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    if (!manifest.Hashes.ContainsKey(relative))
                        plan.Add(new UpdateFilePlan { SourceName = null, TargetName = relative });
                }
            return plan;
        }

        private static void ValidateManifest(ApplicationUpdateManifest manifest,
            string manifestPath)
        {
            if (manifest == null || manifest.Hashes == null)
                throw new InvalidDataException("更新清单无效");
            if (!Regex.IsMatch(manifest.Tag ?? "", @"(?i)^v?\d+(?:\.\d+){1,3}(?:-[a-z0-9.-]+)?$"))
                throw new InvalidDataException("更新清单版本无效");
            manifest.InstallDirectory = Path.GetFullPath(manifest.InstallDirectory);
            manifest.StageDirectory = Path.GetFullPath(manifest.StageDirectory);
            manifest.BackupDirectory = Path.GetFullPath(manifest.BackupDirectory);
            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            if (!IsChildPath(manifest.InstallDirectory, manifest.StageDirectory) ||
                !IsChildPath(manifest.InstallDirectory, manifest.BackupDirectory) ||
                !IsChildPath(manifest.InstallDirectory, manifestDirectory))
                throw new InvalidDataException("更新路径越过了枫语幕安装目录");
            if (IsChildPath(manifest.StageDirectory, manifest.BackupDirectory) ||
                IsChildPath(manifest.BackupDirectory, manifest.StageDirectory) ||
                String.Equals(manifest.StageDirectory, manifest.BackupDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新暂存与备份路径重叠");
            RejectReparsePoints(manifest.InstallDirectory);
            RejectReparsePoints(manifest.StageDirectory);
            RejectReparsePoints(manifest.BackupDirectory);
            foreach (string name in PackageFiles)
            {
                string hash;
                if (!manifest.Hashes.TryGetValue(name, out hash) ||
                    !Regex.IsMatch(hash ?? "", @"(?i)^[0-9a-f]{64}$"))
                    throw new InvalidDataException("更新清单缺少 " + name + " 的校验值");
                string staged = Path.Combine(manifest.StageDirectory, name);
                if (!File.Exists(staged) || !String.Equals(ComputeSha256(staged), hash,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(name + " 在安装前校验失败");
            }
            foreach (KeyValuePair<string, string> file in manifest.Hashes)
            {
                if (!IsPackageFile(file.Key) &&
                    !file.Key.StartsWith("StrongOcr" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException("更新清单包含未支持的组件路径");
                string staged = Path.GetFullPath(Path.Combine(manifest.StageDirectory, file.Key));
                string target = Path.GetFullPath(Path.Combine(manifest.InstallDirectory, file.Key));
                if (!IsPackageFile(file.Key) &&
                    (!IsChildPath(Path.Combine(manifest.StageDirectory, "StrongOcr"), staged) ||
                    !IsChildPath(Path.Combine(manifest.InstallDirectory, "StrongOcr"), target)))
                    throw new InvalidDataException("组件路径越过 StrongOcr 边界");
                if (!IsChildPath(manifest.StageDirectory, staged) || !IsChildPath(manifest.InstallDirectory, target))
                    throw new InvalidDataException("更新组件路径越界");
                RejectReparsePoints(staged); RejectReparsePoints(target);
                if (!File.Exists(staged) || !String.Equals(ComputeSha256(staged), file.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新组件哈希校验失败");
            }
            string oldOcr = Path.Combine(manifest.InstallDirectory, "StrongOcr");
            RejectReparsePoints(oldOcr);
            if (Directory.Exists(oldOcr))
            {
                foreach (string directory in Directory.GetDirectories(oldOcr, "*", SearchOption.AllDirectories)) RejectReparsePoints(directory);
                foreach (string file in Directory.GetFiles(oldOcr, "*", SearchOption.AllDirectories)) RejectReparsePoints(file);
            }
        }

        private static void RejectReparsePoints(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("更新路径包含符号链接或重解析点");
                current = Path.GetDirectoryName(current);
            }
        }

        internal static Version ParseReleaseVersion(string tag)
        {
            string value = Regex.Replace(tag ?? "", @"^(?i)v", "").Split('-')[0];
            Version version;
            if (!Version.TryParse(value, out version)) throw new InvalidDataException("版本号无效");
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private static void WaitForOldProcess(int processId)
        {
            if (processId <= 0 || processId == Process.GetCurrentProcess().Id) return;
            try
            {
                Process process = Process.GetProcessById(processId);
                if (!process.WaitForExit(60000))
                    throw new TimeoutException("等待旧版退出超时");
            }
            catch (ArgumentException) { }
        }

        private static bool StartInstalledApplication(string installDirectory)
        {
            if (AutomationPolicy.SkipGuiRestart) return true;
            try
            {
                Process.Start(new ProcessStartInfo {
                    FileName = Path.Combine(installDirectory, "枫语幕.exe"),
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex) { Program.LogRuntimeEvent("update-restart-failed", "type=" + ex.GetType().Name); return false; }
        }

        private static void SaveResult(bool success, string message, string detail)
        {
            try
            {
                using (UserSettingsSection key = UserSettings.Open(@"Software\FengYuMu"))
                {
                    key.SetValue("ApplicationUpdateSuccess", success ? 1 : 0,
                        RegistryValueKind.DWord);
                    key.SetValue("ApplicationUpdateMessage", message ?? "");
                    key.SetValue("ApplicationUpdateDetail", detail ?? "");
                }
            }
            catch { }
        }

        internal static bool IsChildPath(string parent, string candidate)
        {
            string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string child = Path.GetFullPath(candidate);
            return child.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(input);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("X2"));
                return result.ToString();
            }
        }

        internal static MirrorReleaseManifest ParseMirrorManifest(string json,
            string manifestUrl)
        {
            Uri manifestUri = RequireHttpsUri(manifestUrl, "国内镜像清单地址");
            Dictionary<string, object> value;
            try
            {
                value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("国内镜像清单不是有效 JSON", ex);
            }
            if (value == null) throw new InvalidDataException("国内镜像清单为空");

            string tag = ReadFirstString(value, "tag", "tag_name");
            if (!Regex.IsMatch(tag, @"(?i)^v?\d+(?:\.\d+){1,3}(?:-[a-z0-9.-]+)?$"))
                throw new InvalidDataException("国内镜像版本号格式无效");
            string versionPart = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? tag.Substring(1) : tag;
            string expectedZipName = "FengYuMu_v" + versionPart + ".zip";

            string zipUrl = ReadFirstString(value, "zipUrl", "zip_url");
            ValidateMirrorAssetUrl(zipUrl, manifestUri, expectedZipName, "ZIP 地址");
            string shaUrl = ReadFirstString(value, "sha256Url", "sha256_url");
            if (shaUrl.Length > 0)
                ValidateMirrorAssetUrl(shaUrl, manifestUri, expectedZipName + ".sha256",
                    "SHA-256 地址");
            string sha256 = ReadFirstString(value, "sha256", "sha256Hash");
            if (sha256.Length > 0 &&
                !Regex.IsMatch(sha256, @"(?i)^[0-9a-f]{64}$"))
                throw new InvalidDataException("国内镜像 SHA-256 格式无效");
            if (shaUrl.Length == 0 && sha256.Length == 0)
                throw new InvalidDataException("国内镜像缺少 SHA-256 地址或固定校验值");

            return new MirrorReleaseManifest {
                Tag = tag,
                ZipUrl = zipUrl,
                Sha256Url = shaUrl,
                Sha256 = sha256.ToUpperInvariant()
            };
        }

        internal static string ParseSha256Text(string text, string expectedFileName)
        {
            string normalized = (text ?? "").Trim();
            Match match = Regex.Match(normalized,
                @"(?is)^(?<hash>[0-9a-f]{64})(?:\s+(?<file>\*?[^\r\n]+))?$");
            if (!match.Success)
                throw new InvalidDataException("SHA-256 校验文件内容无效");
            string file = match.Groups["file"].Value.Trim().TrimStart('*').Trim();
            if (file.Length > 0 && !String.Equals(
                Path.GetFileName(file.Replace('/', '\\')), expectedFileName,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 校验文件名与安装包不一致");
            return match.Groups["hash"].Value.ToUpperInvariant();
        }

        internal static void EnsureSha256Matches(string expectedHash, string actualHash)
        {
            if (!Regex.IsMatch(expectedHash ?? "", @"(?i)^[0-9a-f]{64}$") ||
                !Regex.IsMatch(actualHash ?? "", @"(?i)^[0-9a-f]{64}$") ||
                !String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包 SHA-256 校验失败，已停止更新");
        }

        private static MirrorReleaseManifest TryLoadDomesticMirrorManifest()
        {
            if (!DomesticMirrorEnabled) return null;
            try
            {
                return ParseMirrorManifest(DownloadText(DomesticMirrorManifestUrl),
                    DomesticMirrorManifestUrl);
            }
            catch (Exception)
            {
                // A stale, malformed, rate-limited, or unreachable domestic mirror is
                // non-fatal. The caller immediately continues with the GitHub source.
                return null;
            }
        }

        private static Uri RequireHttpsUri(string raw, string fieldName)
        {
            Uri uri;
            if (!Uri.TryCreate(raw ?? "", UriKind.Absolute, out uri) ||
                !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment) ||
                !String.IsNullOrEmpty(uri.Query) || (!uri.IsDefaultPort && uri.Port != 443))
                throw new InvalidDataException(fieldName + "必须使用无跳转 HTTPS 地址");
            return uri;
        }

        private static void ValidateMirrorAssetUrl(string raw, Uri manifestUri,
            string expectedFileName, string fieldName)
        {
            Uri uri = RequireHttpsUri(raw, fieldName);
            if (!String.Equals(uri.Host, manifestUri.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(fieldName + "必须来自清单所在镜像域名");
            string path = uri.AbsolutePath.TrimEnd('/');
            int slash = path.LastIndexOf('/');
            string actualFileName = slash >= 0 ? path.Substring(slash + 1) : path;
            if (!String.Equals(actualFileName, expectedFileName, StringComparison.Ordinal))
                throw new InvalidDataException(fieldName + "文件名与版本号不一致");
        }

        private static string ReadFirstString(Dictionary<string, object> value,
            string firstKey, string secondKey)
        {
            string result = ReadString(value, firstKey);
            return result.Length > 0 ? result : ReadString(value, secondKey);
        }

        private static void ExtractRequiredFiles(string zipPath, string destination)
        {
            HashSet<string> remaining = new HashSet<string>(PackageFiles,
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    bool component = name.StartsWith("StrongOcr/", StringComparison.Ordinal);
                    if (String.IsNullOrEmpty(entry.Name)) continue;
                    if (!component && !IsPackageFile(name)) continue;
                    if (name.StartsWith("/", StringComparison.Ordinal) || name.IndexOf(':') >= 0 ||
                        !extracted.Add(name)) throw new InvalidDataException("安装包路径无效或重复");
                    name = name.Replace('/', Path.DirectorySeparatorChar);
                    string target = Path.GetFullPath(Path.Combine(destination, name));
                    if (!IsChildPath(destination, target)) throw new InvalidDataException("安装包包含越界路径");
                    if (component && !IsChildPath(Path.Combine(destination, "StrongOcr"), target))
                        throw new InvalidDataException("组件安装包路径越过 StrongOcr 边界");
                    expandedBytes += entry.Length;
                    if (expandedBytes > 512L * 1024 * 1024) throw new InvalidDataException("安装包解压体积异常");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(target,
                        FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                    remaining.Remove(name);
                }
            if (remaining.Count > 0)
                throw new InvalidDataException("安装包缺少：" +
                    String.Join("、", new List<string>(remaining).ToArray()));
            string componentDirectory = Path.Combine(destination, "StrongOcr");
            if (Directory.Exists(componentDirectory) && !StrongOcrComponent.Validate(componentDirectory))
                throw new InvalidDataException("强 OCR 组件清单缺失、不完整或校验失败");
        }

        private static string DownloadText(string url)
        {
            return WithOfficialNetworkRoutes(delegate(WebClient client) {
                return client.DownloadString(url);
            });
        }

        private static void DownloadFile(string url, string path)
        {
            WithOfficialNetworkRoutes(delegate(WebClient client) {
                if (File.Exists(path)) File.Delete(path);
                client.DownloadFile(url, path);
                return true;
            });
        }

        private static WebClient CreateWebClient(IWebProxy proxy)
        {
            WebClient client = new UpdateWebClient();
            client.Encoding = Encoding.UTF8;
            client.Headers[HttpRequestHeader.UserAgent] = "FengYuMu/4.0";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json, text/plain, */*";
            client.Proxy = proxy;
            return client;
        }

        private static T WithOfficialNetworkRoutes<T>(Func<WebClient, T> action)
        {
            Exception last = null;
            foreach (NetworkRoute route in BuildNetworkRoutes())
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        using (WebClient client = CreateWebClient(route.Proxy))
                            return action(client);
                    }
                    catch (WebException ex)
                    {
                        last = ex;
                        if (attempt == 0) System.Threading.Thread.Sleep(350);
                    }
                }
            }
            throw CreateOfficialNetworkError(last);
        }

        private static List<NetworkRoute> BuildNetworkRoutes()
        {
            List<NetworkRoute> routes = new List<NetworkRoute>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IWebProxy systemProxy = WebRequest.DefaultWebProxy;
            AddNetworkRoute(routes, seen, "Windows系统代理", systemProxy);
            foreach (string variable in new string[] { "HTTPS_PROXY", "https_proxy",
                "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy" })
            {
                string value = Environment.GetEnvironmentVariable(variable);
                Uri uri;
                if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(),
                    UriKind.Absolute, out uri) ||
                    !(uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                      uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))) continue;
                WebProxy proxy = new WebProxy(uri);
                proxy.Credentials = CredentialCache.DefaultCredentials;
                AddNetworkRoute(routes, seen, variable, proxy);
            }
            AddNetworkRoute(routes, seen, "直连", null);
            return routes;
        }

        private static void AddNetworkRoute(List<NetworkRoute> routes, HashSet<string> seen,
            string name, IWebProxy proxy)
        {
            string key = "DIRECT";
            if (proxy != null)
            {
                try
                {
                    Uri destination = new Uri(LatestReleasePage);
                    Uri address = proxy.GetProxy(destination);
                    if (address != null && address != destination) key = address.AbsoluteUri;
                }
                catch { key = name; }
            }
            if (!seen.Add(key)) return;
            routes.Add(new NetworkRoute { Name = name, Proxy = proxy });
        }

        private static WebException CreateOfficialNetworkError(Exception inner)
        {
            return new WebException(
                "无法连接 GitHub 官方发布服务器；已自动尝试 Windows 系统代理、环境代理和直连。" +
                "请确认浏览器能打开 GitHub，或稍后再试。", inner);
        }

        private static string ResolveLatestTag()
        {
            Exception last = null;
            foreach (NetworkRoute route in BuildNetworkRoutes())
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestReleasePage);
                    request.Method = "HEAD";
                    request.AllowAutoRedirect = false;
                    request.UserAgent = "FengYuMu/4.0";
                    request.Proxy = route.Proxy;
                    request.Timeout = 12000;
                    request.ReadWriteTimeout = 12000;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        string location = response.Headers[HttpResponseHeader.Location] ?? "";
                        Match match = Regex.Match(location, @"/releases/tag/([^/?#]+)",
                            RegexOptions.IgnoreCase);
                        if (!match.Success) throw new InvalidDataException("无法确定最新正式版本号");
                        return Uri.UnescapeDataString(match.Groups[1].Value);
                    }
                }
                catch (WebException ex) { last = ex; }
            }
            throw CreateOfficialNetworkError(last);
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item)
                ? Convert.ToString(item) : "";
        }

        private static object[] ToArray(object value)
        {
            object[] array = value as object[];
            if (array != null) return array;
            ArrayList list = value as ArrayList;
            return list == null ? new object[0] : list.ToArray();
        }
    }
}
