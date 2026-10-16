using System;
using System.Collections.Generic;
using System.IO;
using MapleOverlay;

public static class ReleaseSafetyHarness
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++;
        Console.WriteLine("PASS: " + name);
    }
    private static UpdateFilePlan FilePlan(string stage, string source, string target)
    {
        return new UpdateFilePlan { SourceName = source, TargetName = target,
            Sha256 = UpdateFileTransaction.Hash(Path.Combine(stage, source)) };
    }
    public static int Run(string root)
    {
        passed = 0;
        Check(!TranslationValueSafety.PreservesNumbers("5", "50"), "5 must not become 50");
        Check(!TranslationValueSafety.PreservesNumbers("5/12", "12/5"), "ordered progress values");
        Check(!TranslationValueSafety.PreservesNumbers("5 5", "5"), "duplicate number count");
        Check(!TranslationValueSafety.PreservesNumbers("5%", "5"), "percent unit retained");
        Check(!TranslationValueSafety.PreservesNumbers("1.5", "1.50"), "full decimal token");
        Check(TranslationValueSafety.PreservesNumbers("Level 12+; 30/50 items; 5%", "等级12+；30/50件；5％"), "unchanged complete values");

        string install = Path.Combine(root, "install"), stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(install); Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(install, "app.exe"), "old-app");
        File.WriteAllText(Path.Combine(install, "dictionary.tsv"), "user-custom-row");
        File.WriteAllText(Path.Combine(stage, "app.exe"), "new-app");
        File.WriteAllText(Path.Combine(stage, "dictionary.tsv"), "official-new");
        string backup = Path.Combine(root, "backup-success");
        UpdateFileTransaction.Execute(install, stage, backup, new List<UpdateFilePlan> {
            FilePlan(stage, "app.exe", "app.exe"), FilePlan(stage, "dictionary.tsv", "official.tsv") });
        Check(File.ReadAllText(Path.Combine(install, "app.exe")) == "new-app", "real replacement");
        Check(File.ReadAllText(Path.Combine(backup, "app.exe")) == "old-app", "verified old backup");
        Check(File.ReadAllText(Path.Combine(install, "dictionary.tsv")) == "user-custom-row", "active custom dictionary preserved");
        Check(File.ReadAllText(Path.Combine(install, "official.tsv")) == "official-new", "official dictionary delivered separately");

        File.WriteAllText(Path.Combine(stage, "app.exe"), "next-app");
        Directory.CreateDirectory(Path.Combine(install, "blocked-target"));
        bool failed = false, rolledBack = false;
        try {
            UpdateFileTransaction.Execute(install, stage, Path.Combine(root, "backup-partial"), new List<UpdateFilePlan> {
                FilePlan(stage, "app.exe", "app.exe"), FilePlan(stage, "dictionary.tsv", "new-before-failure.tsv"),
                FilePlan(stage, "dictionary.tsv", "blocked-target") });
        } catch (UpdateTransactionFailure error) { failed = error.ReplacementStarted; rolledBack = error.RollbackSucceeded; }
        Check(failed && rolledBack, "mid-replacement failure produces verified rollback");
        Check(File.ReadAllText(Path.Combine(install, "app.exe")) == "new-app", "partial replacement restores previous bytes");
        Check(!File.Exists(Path.Combine(install, "new-before-failure.tsv")), "rollback removes newly introduced files");
        Check(File.ReadAllText(Path.Combine(install, "dictionary.tsv")) == "user-custom-row", "failure preserves user data");

        failed = false;
        try { UpdateFileTransaction.Execute(install, stage, backup, new List<UpdateFilePlan> { FilePlan(stage, "app.exe", "app.exe") }); }
        catch (UpdateTransactionFailure error) { failed = !error.ReplacementStarted; }
        Check(failed && File.ReadAllText(Path.Combine(install, "app.exe")) == "new-app", "existing backup is never reused");
        Check(Directory.GetFiles(install, "*.update-*").Length == 0, "temporary writes cleaned after success and failure");

        string component = Path.Combine(root, "StrongOcr"); Directory.CreateDirectory(component);
        string[] names = { "StrongOcrHelper.exe", "Sdcb.SimdPaddleOCR.dll", "Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll" };
        List<string> manifest = new List<string>();
        foreach (string name in names) {
            File.WriteAllText(Path.Combine(component, name), "fixture-" + name);
            manifest.Add(name + "\t" + UpdateFileTransaction.Hash(Path.Combine(component, name)));
        }
        string manifestPath = Path.Combine(component, StrongOcrComponent.ManifestName);
        File.WriteAllLines(manifestPath, manifest);
        Check(StrongOcrComponent.Validate(component), "complete component file manifest");
        File.WriteAllText(Path.Combine(component, names[0]), "tampered");
        Check(!StrongOcrComponent.Validate(component), "tampered helper rejected");
        File.WriteAllText(Path.Combine(component, names[0]), "fixture-" + names[0]);
        File.WriteAllText(Path.Combine(component, "stale.dll"), "leftover");
        Check(!StrongOcrComponent.Validate(component), "extra old component rejected");
        File.Delete(Path.Combine(component, "stale.dll"));
        File.Delete(Path.Combine(component, names[1]));
        Check(!StrongOcrComponent.Validate(component), "missing component dependency rejected");
        Console.WriteLine("PASS_COUNT=" + passed);
        return passed;
    }
}
