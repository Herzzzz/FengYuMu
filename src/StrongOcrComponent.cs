using System;
using System.Collections.Generic;
using System.IO;

namespace MapleOverlay
{
    internal static class StrongOcrComponent
    {
        internal const string ManifestName = "component-manifest.tsv";
        internal static bool Validate(string directory)
        {
            try
            {
                string manifest = Path.Combine(directory, ManifestName);
                if (!File.Exists(manifest)) return false;
                string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(manifest))
                {
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.Length == 0) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length != 2 || parts[1].Length != 64) return false;
                    string name = parts[0].Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(root, name));
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !files.Add(name) ||
                        !File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0 ||
                        !String.Equals(UpdateFileTransaction.Hash(full), parts[1], StringComparison.OrdinalIgnoreCase)) return false;
                }
                foreach (string required in new string[] { "StrongOcrHelper.exe", "Sdcb.SimdPaddleOCR.dll",
                    "Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll" })
                    if (!files.Contains(required)) return false;
                foreach (string full in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string name = full.Substring(root.Length);
                    if (name != ManifestName && !files.Contains(name)) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
