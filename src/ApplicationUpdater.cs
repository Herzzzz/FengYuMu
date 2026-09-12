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
    internal sealed class PreparedApplicationUpdate
    {
        internal string Tag;
        internal string StageDirectory;
        internal string ManifestPath;
        internal bool HasChanges;

        internal void StartInstaller()
        {
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

    internal static class ApplicationUpdater
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/Herzzzz/FengYuMu/releases/latest";
        private const string LatestReleasePage =
            "https://github.com/Herzzzz/FengYuMu/releases/latest";
        private static readonly string[] PackageFiles = new string[] {
            "枫语幕.exe", "枫语幕词库.tsv", "使用说明.txt"
        };

        internal static Task<PreparedApplicationUpdate> PrepareLatestAsync(string installDirectory)
        {
            return Task.Factory.StartNew(delegate {
                return PrepareLatest(installDirectory);
            });
        }

        private static PreparedApplicationUpdate PrepareLatest(string installDirectory)
        {
            string install = Path.GetFullPath(installDirectory);
            string updateRoot = Path.Combine(install, "更新临时");
            Directory.CreateDirectory(updateRoot);
            string tag = "", zipUrl = "", shaUrl = "", releaseDigest = "";
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
            if (tag.Length == 0 || zipUrl.Length == 0 || shaUrl.Length == 0)
            {
                tag = ResolveLatestTag();
                string redirectedVersion = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tag.Substring(1) : tag;
                string redirectedZip = "FengYuMu_v" + redirectedVersion + ".zip";
                string baseUrl = "https://github.com/Herzzzz/FengYuMu/releases/latest/download/";
                zipUrl = baseUrl + redirectedZip;
                shaUrl = zipUrl + ".sha256";
                releaseDigest = "";
            }
            if (!Regex.IsMatch(tag, @"(?i)^v?\d+(?:\.\d+){1,3}(?:-[a-z0-9.-]+)?$"))
                throw new InvalidDataException("发布页版本号格式无效");
            string versionPart = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? tag.Substring(1) : tag;
            string expectedZipName = "FengYuMu_v" + versionPart + ".zip";

            string stage = Path.Combine(updateRoot, "v" + versionPart + "-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(stage);
            string zipPath = Path.Combine(stage, expectedZipName);
            DownloadFile(zipUrl, zipPath);
            string expectedHash = Regex.Match(DownloadText(shaUrl),
                @"(?i)\b[0-9a-f]{64}\b").Value.ToUpperInvariant();
            if (expectedHash.Length != 64)
                throw new InvalidDataException("SHA-256 校验文件内容无效");
            string actualHash = ComputeSha256(zipPath);
            if (!String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包 SHA-256 校验失败，已停止更新");
            if (releaseDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(releaseDigest.Substring(7), actualHash,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("发布页摘要与安装包不一致，已停止更新");

            string payload = Path.Combine(stage, "payload");
            Directory.CreateDirectory(payload);
            ExtractRequiredFiles(zipPath, payload);
            Dictionary<string, string> hashes = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (string name in PackageFiles)
            {
                string stagedFile = Path.Combine(payload, name);
                string hash = ComputeSha256(stagedFile);
                hashes[name] = hash;
                string currentFile = Path.Combine(install, name);
                if (!File.Exists(currentFile) || !String.Equals(ComputeSha256(currentFile), hash,
                    StringComparison.OrdinalIgnoreCase)) changed = true;
            }

            string backup = Path.Combine(install, "更新备份", tag + "-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
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
            string error = "";
            try
            {
                string fullManifest = Path.GetFullPath(manifestPath);
                manifest = new JavaScriptSerializer().Deserialize<ApplicationUpdateManifest>(
                    File.ReadAllText(fullManifest, Encoding.UTF8));
                ValidateManifest(manifest, fullManifest);
                WaitForOldProcess(manifest.OldProcessId);
                Directory.CreateDirectory(manifest.BackupDirectory);
                foreach (string name in PackageFiles)
                {
                    string current = Path.Combine(manifest.InstallDirectory, name);
                    if (!File.Exists(current))
                        throw new FileNotFoundException("当前安装缺少 " + name, current);
                    File.Copy(current, Path.Combine(manifest.BackupDirectory, name), false);
                }
                foreach (string name in PackageFiles)
                    ReplaceVerifiedFile(manifest, name);
                SaveResult(true, "已安全更新到 " + manifest.Tag + "；旧版已备份。", "");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (manifest != null)
                {
                    try { RestoreBackup(manifest); }
                    catch (Exception rollback)
                    {
                        error += "；自动回退也失败：" + rollback.Message;
                    }
                }
                SaveResult(false, "更新失败，已继续使用更新前版本。", error);
            }
            finally
            {
                if (manifest != null) StartInstalledApplication(manifest.InstallDirectory);
            }
            return error.Length == 0 ? 0 : 3;
        }

        private static void ValidateManifest(ApplicationUpdateManifest manifest,
            string manifestPath)
        {
            if (manifest == null || manifest.Hashes == null)
                throw new InvalidDataException("更新清单无效");
            manifest.InstallDirectory = Path.GetFullPath(manifest.InstallDirectory);
            manifest.StageDirectory = Path.GetFullPath(manifest.StageDirectory);
            manifest.BackupDirectory = Path.GetFullPath(manifest.BackupDirectory);
            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            if (!IsChildPath(manifest.InstallDirectory, manifest.StageDirectory) ||
                !IsChildPath(manifest.InstallDirectory, manifest.BackupDirectory) ||
                !IsChildPath(manifest.InstallDirectory, manifestDirectory))
                throw new InvalidDataException("更新路径越过了枫语幕安装目录");
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
        }

        private static void ReplaceVerifiedFile(ApplicationUpdateManifest manifest, string name)
        {
            string source = Path.Combine(manifest.StageDirectory, name);
            string target = Path.Combine(manifest.InstallDirectory, name);
            string pending = target + ".update";
            File.Copy(source, pending, true);
            if (!String.Equals(ComputeSha256(pending), manifest.Hashes[name],
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(name + " 写入临时文件后校验失败");
            if (File.Exists(target)) File.Replace(pending, target, null, true);
            else File.Move(pending, target);
            if (!String.Equals(ComputeSha256(target), manifest.Hashes[name],
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(name + " 替换后校验失败");
        }

        private static void RestoreBackup(ApplicationUpdateManifest manifest)
        {
            if (!Directory.Exists(manifest.BackupDirectory)) return;
            foreach (string name in PackageFiles)
            {
                string backup = Path.Combine(manifest.BackupDirectory, name);
                if (File.Exists(backup))
                    File.Copy(backup, Path.Combine(manifest.InstallDirectory, name), true);
            }
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

        private static void StartInstalledApplication(string installDirectory)
        {
            try
            {
                Process.Start(new ProcessStartInfo {
                    FileName = Path.Combine(installDirectory, "枫语幕.exe"),
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private static void SaveResult(bool success, string message, string detail)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
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

        private static void ExtractRequiredFiles(string zipPath, string destination)
        {
            HashSet<string> remaining = new HashSet<string>(PackageFiles,
                StringComparer.OrdinalIgnoreCase);
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('/', '\\');
                    if (name.IndexOf('\\') >= 0 || !remaining.Contains(name)) continue;
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(Path.Combine(destination, name),
                        FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                    remaining.Remove(name);
                }
            if (remaining.Count > 0)
                throw new InvalidDataException("安装包缺少：" +
                    String.Join("、", new List<string>(remaining).ToArray()));
        }

        private static string DownloadText(string url)
        {
            using (WebClient client = CreateWebClient()) return client.DownloadString(url);
        }

        private static void DownloadFile(string url, string path)
        {
            using (WebClient client = CreateWebClient()) client.DownloadFile(url, path);
        }

        private static WebClient CreateWebClient()
        {
            WebClient client = new WebClient();
            client.Encoding = Encoding.UTF8;
            client.Headers[HttpRequestHeader.UserAgent] = "FengYuMu/3.0";
            return client;
        }

        private static string ResolveLatestTag()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestReleasePage);
            request.Method = "HEAD";
            request.AllowAutoRedirect = false;
            request.UserAgent = "FengYuMu/3.0";
            request.Timeout = 12000;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                string location = response.Headers[HttpResponseHeader.Location] ?? "";
                Match match = Regex.Match(location, @"/releases/tag/([^/?#]+)",
                    RegexOptions.IgnoreCase);
                if (!match.Success) throw new InvalidDataException("无法确定最新正式版本号");
                return Uri.UnescapeDataString(match.Groups[1].Value);
            }
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
