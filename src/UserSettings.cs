using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace MapleOverlay
{
    // Ordinary preferences never require registry write rights. Old values remain readable;
    // API keys are still DPAPI-protected by OnlineAiSettings before reaching this store.
    internal static class UserSettings
    {
        internal static string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FengYuMu", "user-settings-v1.dat");

        internal static UserSettingsSection Open(string section)
        {
            return new UserSettingsSection(FilePath, section, ReadLegacy);
        }

        private static object ReadLegacy(string section, string name, object fallback)
        {
            try
            {
                RegistryKey root = Registry.CurrentUser;
                if (root == null) return fallback;
                using (RegistryKey key = root.OpenSubKey(section, false))
                    return key == null ? fallback : key.GetValue(name, fallback);
            }
            catch (UnauthorizedAccessException) { return fallback; }
            catch (SecurityException) { return fallback; }
            catch (PlatformNotSupportedException) { return fallback; }
        }
    }

    internal sealed class UserSettingsSection : IDisposable
    {
        private readonly string path, section;
        private readonly Func<string, string, object, object> legacy;
        private readonly Dictionary<string, string> values;
        private readonly Dictionary<string, string> pending = new Dictionary<string, string>();
        private bool disposed;
        internal UserSettingsSection(string path, string section,
            Func<string, string, object, object> legacy)
        {
            this.path = path; this.section = section; this.legacy = legacy;
            values = ReadFile(path);
        }
        private string Key(string name) { return section + "\n" + name; }
        internal object GetValue(string name, object fallback)
        {
            string value;
            if (pending.TryGetValue(Key(name), out value) || values.TryGetValue(Key(name), out value))
                return value == null ? fallback : (object)value;
            return legacy == null ? fallback : legacy(section, name, fallback);
        }
        internal void SetValue(string name, object value)
        {
            if (disposed) throw new ObjectDisposedException("UserSettingsSection");
            pending[Key(name)] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        internal void SetValue(string name, object value, RegistryValueKind kind) { SetValue(name, value); }
        internal void DeleteValue(string name, bool throwOnMissing) { pending[Key(name)] = null; }
        internal void Commit()
        {
            if (pending.Count == 0) return;
            string mutexName;
            using (SHA256 hash = SHA256.Create())
                mutexName = "FengYuMu.Settings." + BitConverter.ToString(hash.ComputeHash(
                    Encoding.UTF8.GetBytes(Path.GetFullPath(path)))).Replace("-", "");
            using (Mutex mutex = new Mutex(false, mutexName))
            {
                bool entered = false;
                try
                {
                    try { entered = mutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { entered = true; }
                    if (!entered) throw new IOException("设置文件正在使用，请稍后再试。");
                    Dictionary<string, string> merged = ReadFile(path);
                    foreach (KeyValuePair<string, string> item in pending) merged[item.Key] = item.Value;
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                        {
                            writer.WriteLine("FengYuMu.Settings.1");
                            foreach (KeyValuePair<string, string> item in merged)
                                writer.WriteLine(Encode(item.Key) + "|" +
                                    (item.Value == null ? "-" : Encode(item.Value)));
                        }
                        if (File.Exists(path)) File.Replace(temp, path, null);
                        else File.Move(temp, path);
                        values.Clear();
                        foreach (KeyValuePair<string, string> item in merged) values[item.Key] = item.Value;
                        pending.Clear();
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                finally { if (entered) mutex.ReleaseMutex(); }
            }
        }
        private static string Encode(string text) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
        private static Dictionary<string, string> ReadFile(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (!File.Exists(path)) return result;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                if (stream.Length > 1048576 || reader.ReadLine() != "FengYuMu.Settings.1")
                    throw new InvalidDataException("用户设置文件格式损坏，原文件已保留。");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] pieces = line.Split('|');
                    if (pieces.Length != 2 || result.Count >= 10000) throw new InvalidDataException("用户设置文件格式损坏。");
                    result[Encoding.UTF8.GetString(Convert.FromBase64String(pieces[0]))] = pieces[1] == "-"
                        ? null : Encoding.UTF8.GetString(Convert.FromBase64String(pieces[1]));
                }
            }
            return result;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Commit();
        }
    }
}
