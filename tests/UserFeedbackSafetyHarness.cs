using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MapleOverlay;

public static class UserFeedbackSafetyHarness
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS: " + name);
    }
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.dat");
        int legacyReads = 0;
        Func<string, string, object, object> legacy = delegate(string section, string name, object fallback) {
            legacyReads++; return name == "ShowKey" ? "F8" : fallback;
        };
        using (UserSettingsSection s = new UserSettingsSection(path, "root", legacy))
        {
            Check((string)s.GetValue("ShowKey", "none") == "F8", "legacy value remains readable");
            s.SetValue("ShowKey", "F11"); s.SetValue("ShowModifiers", "CTRL+ALT"); s.Commit();
        }
        using (UserSettingsSection s = new UserSettingsSection(path, "root", legacy))
        {
            int before = legacyReads;
            Check((string)s.GetValue("ShowKey", "none") == "F11" &&
                (string)s.GetValue("ShowModifiers", "") == "CTRL+ALT" && legacyReads == before,
                "new preferences persist without invoking legacy lookup");
        }
        using (UserSettingsSection first = new UserSettingsSection(path, "root", null))
        using (UserSettingsSection stale = new UserSettingsSection(path, "OnlineAI", null))
        {
            first.SetValue("Width", 500); first.Commit();
            stale.SetValue("Model", "deepseek-chat"); stale.Commit();
        }
        using (UserSettingsSection s = new UserSettingsSection(path, "root", null))
            Check(Convert.ToInt32(s.GetValue("Width", 0)) == 500, "stale section merge retains other writer's values");
        using (UserSettingsSection s = new UserSettingsSection(path, "OnlineAI", legacy))
        { s.SetValue("ApiKey", "DPAPI-encrypted-fixture"); s.Commit(); s.DeleteValue("ApiKey", false); }
        using (UserSettingsSection s = new UserSettingsSection(path, "OnlineAI", delegate { return "old-secret-fixture"; }))
            Check((string)s.GetValue("ApiKey", "") == "", "cleared credential does not resurrect legacy value");
        using (UserSettingsSection s = new UserSettingsSection(path, "root", null))
        { s.SetValue("Unicode", "设置\n中文|内容"); s.SetValue("Empty", ""); }
        using (UserSettingsSection s = new UserSettingsSection(path, "root", null))
            Check((string)s.GetValue("Unicode", "") == "设置\n中文|内容" &&
                (string)s.GetValue("Empty", "fallback") == "", "Unicode, newlines, separators and empty values round trip");
        Parallel.For(0, 20, delegate(int i) {
            using (UserSettingsSection s = new UserSettingsSection(path, "parallel", null)) s.SetValue("K" + i, i);
        });
        using (UserSettingsSection s = new UserSettingsSection(path, "parallel", null))
            Check(Enumerable.Range(0, 20).All(i => Convert.ToInt32(s.GetValue("K" + i, -1)) == i),
                "concurrent commits retain all 20 changes");
        byte[] original = File.ReadAllBytes(path);
        bool rejected = false;
        try {
            using (UserSettingsSection s = new UserSettingsSection(Path.Combine(path, "blocked.dat"), "root", null))
                s.SetValue("ShowKey", "F12");
        } catch (IOException) { rejected = true; }
        Check(rejected && original.SequenceEqual(File.ReadAllBytes(path)), "filesystem failure preserves existing settings");
        string bad = Path.Combine(root, "corrupt.dat"); File.WriteAllText(bad, "corrupt evidence");
        rejected = false;
        try { using (UserSettingsSection s = new UserSettingsSection(bad, "root", null)) s.SetValue("New", "1"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(bad) == "corrupt evidence", "corrupt settings are refused and kept intact");
        Check(!Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "owned temporary writes cleaned up");
        string previous = UserSettings.FilePath;
        try {
            UserSettings.FilePath = Path.Combine(root, "fresh.dat");
            using (UserSettingsSection s = UserSettings.Open("Software\\FengYuMu\\FreshFixture"))
                Check((string)s.GetValue("Absent", "default") == "default", "actual preference facade handles absent legacy profile");
        } finally { UserSettings.FilePath = previous; }
        Check(OnlineAiProtocol.NormalizeEndpoint("https://api.deepseek.com") ==
            "https://api.deepseek.com/chat/completions", "base address normalizes to chat endpoint");
        Check(OnlineAiProtocol.NormalizeEndpoint("https://api.deepseek.com/v1/") ==
            "https://api.deepseek.com/v1/chat/completions", "versioned base address retains v1");
        Check(OnlineAiProtocol.ModelsEndpoint("https://ark.cn-beijing.volces.com/api/v3/responses").AbsoluteUri ==
            "https://ark.cn-beijing.volces.com/api/v3/models", "models query retains vendor base path");
        Check(OnlineAiProtocol.ModelsEndpoint("https://api.deepseek.com/v1/chat/completions").AbsoluteUri ==
            "https://api.deepseek.com/v1/models", "DeepSeek model query uses same API authority and version");
        foreach (string endpoint in new [] { "http://api.example.com/v1", "https://user:secret@api.example.com/v1",
            "https://api.example.com/v1?key=secret", "https://api.example.com/v1#fragment", "https://api.example.com/bad-path" })
        {
            rejected = false; try { OnlineAiProtocol.NormalizeEndpoint(endpoint); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "unsafe or incomplete endpoint refused: " + new Uri(endpoint).Host);
        }
        Check(OnlineAiProtocol.SafeModel("deepseek-chat", "fixture-key") == "deepseek-chat" &&
            OnlineAiProtocol.SafeModel("sk-secret-fixture", "") == "redacted" &&
            OnlineAiProtocol.SafeModel("fixture-key", "fixture-key") == "redacted" &&
            OnlineAiProtocol.SafeModel("model\r\nsecret", "") == "redacted", "model diagnostic excludes credentials and injected lines");
        return passed;
    }
}
