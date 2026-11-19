using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MapleOverlay
{
    internal sealed class UpdateFilePlan
    {
        internal string SourceName;
        internal string TargetName;
        internal string Sha256;
    }

    internal sealed class UpdateTransactionFailure : IOException
    {
        internal readonly bool ReplacementStarted;
        internal readonly bool RollbackSucceeded;
        internal readonly string Phase;
        internal readonly string Target;
        internal UpdateTransactionFailure(Exception cause, bool started, bool restored, string phase, string target)
            : base("更新事务失败；replacementStarted=" + started + "; rollbackSucceeded=" + restored, cause)
        { ReplacementStarted = started; RollbackSucceeded = restored; Phase = phase; Target = target; }
    }

    // This class is also exercised without Windows: tests perform real file replacement,
    // partial failure and restoration, rather than asserting implementation strings.
    internal static class UpdateFileTransaction
    {
        internal static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (Stream input = File.OpenRead(path))
            {
                StringBuilder result = new StringBuilder();
                foreach (byte b in sha.ComputeHash(input)) result.Append(b.ToString("X2"));
                return result.ToString();
            }
        }

        internal static void Execute(string install, string stage, string backup, IList<UpdateFilePlan> files)
        {
            Dictionary<string, string> originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<UpdateFilePlan> touched = new List<UpdateFilePlan>();
            List<string> temporary = new List<string>();
            string phase = "backup", activeTarget = "";
            try
            {
                if (Directory.Exists(backup) || File.Exists(backup))
                    throw new IOException("本次备份目录已存在，拒绝复用其他事务的备份");
                Directory.CreateDirectory(backup);
                foreach (UpdateFilePlan file in files)
                {
                    activeTarget = file.TargetName;
                    string current = Path.Combine(install, file.TargetName);
                    if (!File.Exists(current)) continue;
                    string saved = Path.Combine(backup, file.TargetName);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved));
                    originals[file.TargetName] = Hash(current);
                    File.Copy(current, saved, false);
                    if (Hash(saved) != originals[file.TargetName]) throw new IOException("备份校验失败");
                }
                // No installed byte is changed before the entire backup phase succeeds.
                phase = "replace";
                foreach (UpdateFilePlan file in files)
                {
                    activeTarget = file.TargetName;
                    string target = Path.Combine(install, file.TargetName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (file.SourceName == null)
                    {
                        touched.Add(file);
                        if (File.Exists(target)) File.Delete(target);
                        continue;
                    }
                    string source = Path.Combine(stage, file.SourceName);
                    string pending = target + ".update-" + Guid.NewGuid().ToString("N");
                    temporary.Add(pending);
                    File.Copy(source, pending, false);
                    if (!String.Equals(Hash(pending), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("写入临时文件后校验失败");
                    touched.Add(file);
                    if (File.Exists(target)) File.Replace(pending, target, null);
                    else File.Move(pending, target);
                    if (!String.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("替换后校验失败");
                }
            }
            catch (Exception error)
            {
                bool restored = true;
                for (int i = touched.Count - 1; i >= 0; i--)
                {
                    UpdateFilePlan file = touched[i];
                    string target = Path.Combine(install, file.TargetName);
                    try
                    {
                        if (originals.ContainsKey(file.TargetName))
                        {
                            string saved = Path.Combine(backup, file.TargetName);
                            if (Hash(saved) != originals[file.TargetName]) throw new IOException("回滚备份已损坏");
                            File.Copy(saved, target, true);
                            if (Hash(target) != originals[file.TargetName]) throw new IOException("回滚后校验失败");
                        }
                        else if (File.Exists(target)) File.Delete(target);
                    }
                    catch { restored = false; }
                }
                throw new UpdateTransactionFailure(error, touched.Count > 0, restored, phase, activeTarget);
            }
            finally
            {
                foreach (string path in temporary)
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}
