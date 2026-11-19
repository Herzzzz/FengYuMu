using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MapleOverlay
{
    internal sealed class StrongOcrValidationCache
    {
        private readonly object gate = new object();
        private string signature;
        private Task<bool> validation;
        private DateTime checkedAt;
        internal int ValidationCount { get; private set; }

        internal Task<bool> ValidateAsync(string directory)
        {
            string current;
            try { current = MetadataSignature(directory); }
            catch { return Task.FromResult(false); }
            lock (gate)
            {
                if (validation == null || current != signature ||
                    (validation.IsCompleted && DateTime.UtcNow - checkedAt > TimeSpan.FromMinutes(1)))
                {
                    signature = current;
                    checkedAt = DateTime.UtcNow;
                    ValidationCount++;
                    validation = Task.Run(delegate { return StrongOcrComponent.Validate(directory); });
                }
                return validation;
            }
        }

        internal bool IsAvailable(string directory)
        {
            Task<bool> result = ValidateAsync(directory);
            return result.Status == TaskStatus.RanToCompletion && result.Result;
        }

        internal void Invalidate() { lock (gate) { signature = null; validation = null; } }

        private static string MetadataSignature(string directory)
        {
            string root = Path.GetFullPath(directory);
            if (!Directory.Exists(root)) throw new IOException("Component missing");
            List<string> entries = new List<string>();
            Stack<string> pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Component contains a reparse point");
                foreach (string child in Directory.GetDirectories(current)) pending.Push(child);
                foreach (string name in Directory.GetFiles(current))
                {
                    FileInfo file = new FileInfo(name);
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Component file is a reparse point");
                    entries.Add(file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks);
                    if (entries.Count > 10000) throw new IOException("Component file count invalid");
                }
            }
            entries.Sort(StringComparer.Ordinal);
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n", entries.ToArray()))));
        }
    }
}
