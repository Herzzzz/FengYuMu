using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MapleOverlay
{
    internal sealed class OnlineAiSettings
    {
        public string Endpoint = "";
        public string Model = "";
        public string ApiKey = "";
        public bool IsReady { get { return Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Model.Length > 0; } }

        public static OnlineAiSettings Load()
        {
            OnlineAiSettings value = new OnlineAiSettings();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu\OnlineAI"))
            {
                if (key == null) return value;
                value.Endpoint = Convert.ToString(key.GetValue("Endpoint", ""));
                value.Model = Convert.ToString(key.GetValue("Model", ""));
                string protectedKey = Convert.ToString(key.GetValue("ApiKey", ""));
                try
                {
                    if (protectedKey.Length > 0)
                        value.ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), null, DataProtectionScope.CurrentUser));
                }
                catch { value.ApiKey = ""; }
            }
            return value;
        }

        public void Save()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu\OnlineAI"))
            {
                key.SetValue("Endpoint", Endpoint); key.SetValue("Model", Model);
                string encrypted = ApiKey.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(ApiKey), null, DataProtectionScope.CurrentUser));
                key.SetValue("ApiKey", encrypted);
            }
        }
    }

    internal static class OnlineAiClient
    {
        public static Task<string> ReviewAsync(OnlineAiSettings settings, string source, string localTranslation, string target, string glossary)
        {
            return Task.Factory.StartNew(delegate {
                string system = "你是冒险岛怀旧服翻译校对员。根据聊天语境、俚语和术语表纠正本地AI译文。目标语言是" + target + "。只输出最终译文，不解释。";
                string user = "原文：" + source + "\n本地AI译文：" + localTranslation +
                    (glossary.Length > 0 ? "\n术语表：\n" + glossary : "");
                string body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                    { "model", settings.Model }, { "temperature", 0.1 }, { "max_tokens", 256 },
                    { "messages", new object[] {
                        new Dictionary<string, string> { { "role", "system" }, { "content", system } },
                        new Dictionary<string, string> { { "role", "user" }, { "content", user } }
                    } }
                });
                byte[] data = Encoding.UTF8.GetBytes(body);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(settings.Endpoint);
                request.Method = "POST"; request.ContentType = "application/json"; request.Timeout = 30000; request.ContentLength = data.Length;
                if (settings.ApiKey.Length > 0) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + settings.ApiKey;
                using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                string json;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) json = reader.ReadToEnd();
                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object choicesValue = root["choices"];
                object[] choices = choicesValue as object[];
                if (choices == null)
                {
                    System.Collections.ArrayList list = choicesValue as System.Collections.ArrayList;
                    if (list != null) choices = list.ToArray();
                }
                if (choices == null || choices.Length == 0) throw new InvalidOperationException("在线AI返回为空");
                Dictionary<string, object> choice = (Dictionary<string, object>)choices[0];
                Dictionary<string, object> message = (Dictionary<string, object>)choice["message"];
                return Regex.Replace(Convert.ToString(message["content"]), "<think>[\\s\\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
            });
        }
    }

    internal sealed class OfflineAiClient
    {
        private const int Port = 17891;
        private readonly string baseDir;
        private Process process;
        public string Status { get; private set; }

        public OfflineAiClient(string programDir) { baseDir = programDir; Status = "AI模型尚未启动"; }

        public string AiRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FengYuMu", "AI"); }
        }

        public bool IsInstalled
        {
            get { return FindServer() != null && FindModel() != null; }
        }

        private string FindServer()
        {
            string[] roots = new string[] { AiRoot, Path.Combine(baseDir, "AI") };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string direct = Path.Combine(root, "llama-server.exe");
                if (File.Exists(direct)) return direct;
                string[] found = Directory.GetFiles(root, "llama-server.exe", SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
            return null;
        }

        private string FindModel()
        {
            string[] roots = new string[] { AiRoot, Path.Combine(baseDir, "AI") };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string[] found = Directory.GetFiles(root, "Qwen3-*.gguf", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, delegate(string left, string right) {
                        bool left8 = Path.GetFileName(left).IndexOf("8B", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool right8 = Path.GetFileName(right).IndexOf("8B", StringComparison.OrdinalIgnoreCase) >= 0;
                        return right8.CompareTo(left8);
                    });
                    return found[0];
                }
            }
            return null;
        }

        public async Task<bool> EnsureStartedAsync()
        {
            if (await PingAsync()) { Status = "本地AI已就绪"; return true; }
            string server = FindServer(), model = FindModel();
            if (server == null || model == null)
            {
                Status = "未安装AI模型包";
                return false;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(server,
                    "-m \"" + model + "\" --host 127.0.0.1 --port " + Port +
                    " -ngl 99 -c 4096 --parallel 1 --no-webui");
                info.WorkingDirectory = Path.GetDirectoryName(server);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = false;
                info.RedirectStandardError = false;
                process = Process.Start(info);
                Status = "正在载入本地AI模型…";
                for (int i = 0; i < 90; i++)
                {
                    await Task.Delay(500);
                    if (await PingAsync()) { Status = "本地AI已就绪（显卡优先）"; return true; }
                    if (process.HasExited) break;
                }
                Status = "AI启动失败，请检查模型包";
            }
            catch (Exception ex) { Status = "AI启动失败：" + ex.Message; }
            return false;
        }

        private Task<bool> PingAsync()
        {
            return Task.Factory.StartNew(delegate {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/health");
                    request.Timeout = 450;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
                }
                catch { return false; }
            });
        }

        public async Task<string> TranslateAsync(string text, string sourceLanguage,
            string targetLanguage, string glossary)
        {
            if (!await EnsureStartedAsync()) throw new InvalidOperationException(Status);
            string languageRule = "将输入从" + sourceLanguage + "翻译为" + targetLanguage + "。";
            string system = "你是冒险岛怀旧服聊天翻译器。" + languageRule +
                "理解玩家俚语、缩写和游戏语境；保留角色名、数字、频道名和表情。只输出译文，不解释。" +
                (String.IsNullOrEmpty(glossary) ? "" : "必须优先采用以下术语：\n" + glossary);
            string body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "model", "local-qwen3" },
                { "temperature", 0.2 }, { "top_p", 0.8 }, { "max_tokens", 256 },
                { "messages", new object[] {
                    new Dictionary<string, string> { { "role", "system" }, { "content", system } },
                    new Dictionary<string, string> { { "role", "user" }, { "content", text + "\n/no_think" } }
                } }
            });
            return await Task.Factory.StartNew(delegate { return Post(body); });
        }

        private static string Post(string body)
        {
            byte[] data = Encoding.UTF8.GetBytes(body);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/v1/chat/completions");
            request.Method = "POST"; request.ContentType = "application/json";
            request.Timeout = 30000; request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            string json;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) json = reader.ReadToEnd();
            Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object choicesValue;
            if (!root.TryGetValue("choices", out choicesValue)) throw new InvalidOperationException("本地AI没有返回译文");
            object[] choices = choicesValue as object[];
            if (choices == null)
            {
                System.Collections.ArrayList list = choicesValue as System.Collections.ArrayList;
                if (list != null) choices = list.ToArray();
            }
            if (choices == null || choices.Length == 0) throw new InvalidOperationException("本地AI返回为空");
            Dictionary<string, object> choice = (Dictionary<string, object>)choices[0];
            Dictionary<string, object> message = (Dictionary<string, object>)choice["message"];
            string result = Convert.ToString(message["content"]);
            result = Regex.Replace(result, "<think>[\\s\\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
            return result;
        }

        public void Stop()
        {
            try { if (process != null && !process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
            catch { }
        }
    }

    internal sealed class OfflineChatForm : Form
    {
        private readonly OverlayForm overlay;
        private readonly string dictionaryPath;
        private readonly string candidatesPath;
        private readonly OfflineAiClient ai;
        private readonly TextBox output = new TextBox();
        private readonly TextBox input = new TextBox();
        private readonly ComboBox source = new ComboBox();
        private readonly ComboBox target = new ComboBox();
        private readonly Label status = new Label();
        private readonly Button liveButton = new Button();
        private readonly CheckBox onlineReview = new CheckBox();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> protectedPlayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Rectangle chatRegion;
        private bool live;
        private bool busy;
        private string lastSource = "";
        private string lastTranslation = "";
        private bool lastWasOnline;

        public OfflineChatForm(OverlayForm owner, string baseDir)
        {
            overlay = owner;
            dictionaryPath = Path.Combine(baseDir, "枫语幕词库.tsv");
            candidatesPath = Path.Combine(baseDir, "枫语幕纠错候选.tsv");
            ai = new OfflineAiClient(baseDir);
            Text = "枫语幕｜AI实时聊天翻译";
            Icon = SystemIcons.Information; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(600, 560); MinimumSize = new Size(520, 480);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BuildUi(); LoadRegion();
            timer.Interval = 1250;
            timer.Tick += async delegate { await PollChatAsync(); };
            Shown += async delegate {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(work.Left, work.Right - Width - 18), work.Top + 55);
                await RefreshAiStatusAsync();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); live = false; timer.Stop(); }
            };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 5, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            liveButton.Text = "开始实时翻译"; liveButton.AutoSize = true;
            liveButton.Click += delegate { ToggleLive(); };
            Button bind = new Button { Text = "3秒后绑定游戏聊天区", AutoSize = true };
            bind.Click += async delegate { await BindRegionAsync(); };
            Button review = new Button { Text = "审核AI纠错", AutoSize = true };
            review.Click += delegate {
                using (CorrectionReviewForm form = new CorrectionReviewForm(dictionaryPath, candidatesPath)) form.ShowDialog(this);
                overlay.ReloadDictionary();
            };
            status.AutoSize = true; status.Padding = new Padding(8, 7, 0, 0); status.ForeColor = Color.DarkGreen;
            Button onlineSettings = new Button { Text = "在线AI设置", AutoSize = true };
            onlineSettings.Click += delegate { using (OnlineAiForm form = new OnlineAiForm()) form.ShowDialog(this); };
            tools.Controls.Add(liveButton); tools.Controls.Add(bind); tools.Controls.Add(review); tools.Controls.Add(onlineSettings); tools.Controls.Add(status);
            root.Controls.Add(tools, 0, 0);

            output.Dock = DockStyle.Fill; output.Multiline = true; output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Vertical; output.BackColor = Color.FromArgb(24, 27, 32); output.ForeColor = Color.White;
            root.Controls.Add(output, 0, 1);

            FlowLayoutPanel languages = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            languages.Controls.Add(new Label { Text = "手动翻译：", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            FillLanguages(source); FillLanguages(target); source.SelectedIndex = 0; target.SelectedIndex = 1;
            languages.Controls.Add(source); languages.Controls.Add(new Label { Text = "→", AutoSize = true, Padding = new Padding(3, 7, 3, 0) }); languages.Controls.Add(target);
            root.Controls.Add(languages, 0, 2);

            input.Dock = DockStyle.Fill; input.Multiline = true; input.ScrollBars = ScrollBars.Vertical;
            root.Controls.Add(input, 0, 3);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button translate = new Button { Text = "AI翻译", AutoSize = true };
            translate.Click += async delegate { await TranslateManualAsync(); };
            Button copy = new Button { Text = "复制译文", AutoSize = true };
            copy.Click += delegate { if (lastTranslation.Length > 0) Clipboard.SetText(lastTranslation); };
            Button correct = new Button { Text = "加入纠错候选", AutoSize = true };
            correct.Click += delegate { AddCandidate(); };
            Button install = new Button { Text = "安装/选择永久免费AI模型", AutoSize = true };
            install.Click += delegate { ai.Stop(); using (AiInstallForm form = new AiInstallForm(ai.AiRoot)) form.ShowDialog(this); };
            onlineReview.Text = "用在线AI复核"; onlineReview.AutoSize = true; onlineReview.Padding = new Padding(0, 6, 5, 0);
            actions.Controls.Add(translate); actions.Controls.Add(copy); actions.Controls.Add(correct); actions.Controls.Add(install); actions.Controls.Add(onlineReview);
            root.Controls.Add(actions, 0, 4);
            Controls.Add(root);
        }

        private static void FillLanguages(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList; box.Width = 105;
            box.Items.AddRange(new object[] { "自动识别", "中文", "英语", "日语", "韩语" });
        }

        private async Task RefreshAiStatusAsync()
        {
            bool ready = await ai.EnsureStartedAsync();
            status.Text = ready ? ai.Status : "未安装模型包";
        }

        private void LoadRegion()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
            {
                if (key == null) return;
                chatRegion = new Rectangle(Convert.ToInt32(key.GetValue("ChatX", 0)), Convert.ToInt32(key.GetValue("ChatY", 0)),
                    Convert.ToInt32(key.GetValue("ChatW", 0)), Convert.ToInt32(key.GetValue("ChatH", 0)));
            }
        }

        private async Task BindRegionAsync()
        {
            live = false; timer.Stop(); Hide();
            await Task.Delay(3000);
            Rectangle game = OverlayForm.GetForegroundCaptureBounds();
            // Classic MapleStory chat is a shallow strip at the bottom-left. Keeping this tight
            // avoids OCR-ing character nameplates and NPC labels above the chat window.
            chatRegion = new Rectangle(game.Left + game.Width * 10 / 100, game.Top + game.Height * 76 / 100,
                game.Width * 55 / 100, game.Height * 21 / 100);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
            {
                key.SetValue("ChatX", chatRegion.X); key.SetValue("ChatY", chatRegion.Y);
                key.SetValue("ChatW", chatRegion.Width); key.SetValue("ChatH", chatRegion.Height);
            }
            seen.Clear(); Show(); Activate();
            status.Text = "已绑定聊天区 " + chatRegion.Width + "×" + chatRegion.Height;
        }

        private void ToggleLive()
        {
            if (chatRegion.Width < 80) { MessageBox.Show("请先点击“3秒后绑定游戏聊天区”，倒计时内切回游戏。", "实时聊天翻译"); return; }
            live = !live;
            liveButton.Text = live ? "停止实时翻译" : "开始实时翻译";
            if (live) timer.Start(); else timer.Stop();
        }

        private async Task PollChatAsync()
        {
            if (!live || busy) return;
            busy = true;
            try
            {
                string text = await overlay.CaptureTextAsync(chatRegion);
                string[] lines = text.Replace("\r", "").Split('\n');
                foreach (string raw in lines)
                {
                    string line = Regex.Replace(raw.Trim(), "\\s+", " ");
                    if (line.Length < 3 || seen.Contains(line)) continue;
                    seen.Add(line);
                    if (seen.Count > 180) seen.Clear();
                    string speakerPrefix, message;
                    SplitSpeaker(line, out speakerPrefix, out message);
                    Dictionary<string, string> nameTokens;
                    string protectedMessage = ProtectPlayerNames(message, out nameTokens);
                    string glossary = BuildGlossary(protectedMessage);
                    lastWasOnline = false;
                    string translated = await ai.TranslateAsync(protectedMessage, "自动识别（中英日韩）", "中文", glossary);
                    translated = await ReviewOnlineIfEnabled(protectedMessage, translated, "中文", glossary);
                    translated = RestorePlayerNames(translated, nameTokens);
                    lastSource = message; lastTranslation = translated;
                    AppendOutput(line + Environment.NewLine + "→ " + speakerPrefix + translated + Environment.NewLine);
                }
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; }
        }

        private async Task TranslateManualAsync()
        {
            string value = input.Text.Trim(); if (value.Length == 0 || busy) return;
            busy = true;
            try
            {
                string speakerPrefix, message;
                SplitSpeaker(value, out speakerPrefix, out message);
                Dictionary<string, string> nameTokens;
                string protectedMessage = ProtectPlayerNames(message, out nameTokens);
                string glossary = BuildGlossary(protectedMessage);
                lastWasOnline = false;
                string translated = await ai.TranslateAsync(protectedMessage, Convert.ToString(source.SelectedItem), Convert.ToString(target.SelectedItem), glossary);
                translated = await ReviewOnlineIfEnabled(protectedMessage, translated, Convert.ToString(target.SelectedItem), glossary);
                translated = RestorePlayerNames(translated, nameTokens);
                lastSource = message; lastTranslation = translated;
                AppendOutput(value + Environment.NewLine + "→ " + speakerPrefix + translated + Environment.NewLine);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "AI翻译失败"); }
            finally { busy = false; }
        }

        private string BuildGlossary(string text)
        {
            if (!File.Exists(dictionaryPath)) return "";
            StringBuilder result = new StringBuilder(); int count = 0;
            foreach (string raw in File.ReadLines(dictionaryPath, Encoding.UTF8))
            {
                if (count >= 16 || raw.StartsWith("#")) continue;
                string[] p = raw.Split('\t'); if (p.Length < 2 || p[0].Length < 3) continue;
                if (text.IndexOf(p[0], StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf(p[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
                result.Append(p[0]).Append(" = ").Append(p[1]).AppendLine(); count++;
            }
            return result.ToString();
        }

        private void SplitSpeaker(string line, out string prefix, out string message)
        {
            prefix = ""; message = line;
            string remaining = line;
            Match timestamp = Regex.Match(remaining, @"^\s*\[\d{1,2}:\d{2}(?::\d{2})?\]\s*");
            if (timestamp.Success)
            {
                prefix = timestamp.Value;
                remaining = remaining.Substring(timestamp.Length);
                message = remaining;
            }
            Match match = Regex.Match(remaining, @"^(\s*(?:\[[^\]]{1,20}\]\s*)?(?:<[^>]{1,24}>|[^:：]{1,24})\s*[:：]\s*)(.+)$");
            if (!match.Success)
            {
                match = Regex.Match(remaining, @"^(\s*<([^>]{1,24})>\s*)(.+)$");
                if (!match.Success) return;
            }
            string speakerPrefix = match.Groups[1].Value;
            prefix += speakerPrefix;
            message = match.Groups[match.Groups.Count - 1].Value;
            string name = Regex.Replace(speakerPrefix, @"^\s*(?:\[[^\]]+\]\s*)?", "");
            name = name.Trim().TrimEnd(':', '：').Trim().Trim('<', '>').Trim();
            if (name.Length >= 3 && name.Length <= 24) protectedPlayerNames.Add(name);
        }

        private string ProtectPlayerNames(string message, out Dictionary<string, string> tokens)
        {
            tokens = new Dictionary<string, string>();
            List<string> names = new List<string>(protectedPlayerNames);
            names.Sort(delegate(string left, string right) { return right.Length.CompareTo(left.Length); });
            string result = message; int index = 0;
            foreach (string name in names)
            {
                if (result.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string token = "__FYM_PLAYER_" + index++ + "__";
                result = Regex.Replace(result, Regex.Escape(name), token, RegexOptions.IgnoreCase);
                tokens[token] = name;
            }
            return result;
        }

        private static string RestorePlayerNames(string translation, Dictionary<string, string> tokens)
        {
            string result = translation;
            foreach (KeyValuePair<string, string> item in tokens)
                result = Regex.Replace(result, Regex.Escape(item.Key), item.Value, RegexOptions.IgnoreCase);
            return result;
        }

        private async Task<string> ReviewOnlineIfEnabled(string original, string local, string targetLanguage, string glossary)
        {
            if (!onlineReview.Checked) return local;
            OnlineAiSettings settings = OnlineAiSettings.Load();
            if (!settings.IsReady) { status.Text = "请先填写在线AI设置，已保留本地译文"; return local; }
            try
            {
                string reviewed = await OnlineAiClient.ReviewAsync(settings, original, local, targetLanguage, glossary);
                status.Text = reviewed == local ? "在线AI复核：无需修改" : "在线AI已给出纠错建议";
                if (reviewed.Length > 0 && reviewed != local) lastWasOnline = true;
                return reviewed.Length > 0 ? reviewed : local;
            }
            catch (Exception ex) { status.Text = "在线复核失败，保留本地译文：" + ex.Message; return local; }
        }

        private void AppendOutput(string value)
        {
            output.AppendText((output.TextLength == 0 ? "" : Environment.NewLine) + value);
            if (output.TextLength > 16000) output.Text = output.Text.Substring(output.TextLength - 12000);
            output.SelectionStart = output.TextLength; output.ScrollToCaret();
        }

        private void AddCandidate()
        {
            if (lastSource.Length == 0 || lastTranslation.Length == 0) { MessageBox.Show("请先完成一次翻译。", "AI纠错候选"); return; }
            string row = Clean(lastSource) + "\t" + Clean(lastTranslation) + "\t" +
                (lastWasOnline ? "在线AI纠错-待审核" : "本地AI建议-待审核") + "\t" + DateTime.Now.ToString("s") + Environment.NewLine;
            File.AppendAllText(candidatesPath, row, new UTF8Encoding(true));
            status.Text = "已加入纠错候选，正式词库尚未改变";
        }

        private static string Clean(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(); }
        public void StopService() { timer.Stop(); ai.Stop(); }
    }

    internal sealed class CorrectionReviewForm : Form
    {
        private readonly string dictionaryPath, candidatesPath;
        private readonly DataGridView grid = new DataGridView();
        public CorrectionReviewForm(string dictionary, string candidates)
        {
            dictionaryPath = dictionary; candidatesPath = candidates;
            Text = "审核AI纠错候选"; Size = new Size(900, 560); StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9.0f);
            grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("Source", "原文"); grid.Columns.Add("Translation", "AI建议译文"); grid.Columns.Add("State", "状态"); grid.Columns.Add("Time", "时间");
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
            Button accept = new Button { Text = "审核通过并写入正式词库", AutoSize = true };
            accept.Click += delegate { AcceptSelected(); };
            Button delete = new Button { Text = "删除候选", AutoSize = true };
            delete.Click += delegate { foreach (DataGridViewRow row in grid.SelectedRows) grid.Rows.Remove(row); SaveCandidates(); };
            tools.Controls.Add(accept); tools.Controls.Add(delete);
            Controls.Add(grid); Controls.Add(tools); LoadRows();
        }
        private void LoadRows()
        {
            grid.Rows.Clear(); if (!File.Exists(candidatesPath)) return;
            foreach (string line in File.ReadAllLines(candidatesPath, Encoding.UTF8)) { string[] p = line.Split('\t'); if (p.Length >= 2) grid.Rows.Add(p[0], p[1], p.Length > 2 ? p[2] : "待审核", p.Length > 3 ? p[3] : ""); }
        }
        private void AcceptSelected()
        {
            if (grid.SelectedRows.Count == 0) return;
            List<string> dictionary = new List<string>(File.ReadAllLines(dictionaryPath, Encoding.UTF8));
            foreach (DataGridViewRow selected in grid.SelectedRows)
            {
                string source = Convert.ToString(selected.Cells[0].Value).Trim(), translated = Convert.ToString(selected.Cells[1].Value).Trim();
                bool replaced = false;
                for (int i = 0; i < dictionary.Count; i++)
                {
                    if (dictionary[i].StartsWith("#")) continue;
                    string[] p = dictionary[i].Split('\t');
                    if (p.Length > 0 && String.Equals(p[0].Trim(), source, StringComparison.OrdinalIgnoreCase))
                    {
                        string category = p.Length > 2 ? p[2] : "AI纠错-玩家审核";
                        dictionary[i] = source + "\t" + translated + "\t" + category + (p.Length > 3 ? "\t" + p[3] : ""); replaced = true; break;
                    }
                }
                if (!replaced) dictionary.Add(source + "\t" + translated + "\tAI纠错-玩家审核");
                grid.Rows.Remove(selected);
            }
            File.WriteAllLines(dictionaryPath, dictionary.ToArray(), new UTF8Encoding(true)); SaveCandidates();
            MessageBox.Show("已写入正式词库；关闭审核窗口后会立即重新载入内存。", "审核完成");
        }
        private void SaveCandidates()
        {
            StringBuilder b = new StringBuilder();
            foreach (DataGridViewRow row in grid.Rows) b.Append(Convert.ToString(row.Cells[0].Value)).Append('\t').Append(Convert.ToString(row.Cells[1].Value)).Append('\t').Append(Convert.ToString(row.Cells[2].Value)).Append('\t').Append(Convert.ToString(row.Cells[3].Value)).AppendLine();
            File.WriteAllText(candidatesPath, b.ToString(), new UTF8Encoding(true));
        }
    }

    internal sealed class AiInstallForm : Form
    {
        private readonly string aiRoot;
        private readonly Label progress = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Button install4 = new Button();
        private readonly Button install8 = new Button();
        private readonly Button update = new Button();

        private sealed class RemoteFileInfo
        {
            public long Length;
            public string ETag;
        }

        public AiInstallForm(string aiRoot)
        {
            this.aiRoot = aiRoot;
            Text = "安装永久免费AI模型"; StartPosition = FormStartPosition.CenterParent;
            Size = new Size(690, 390); Font = new Font("Microsoft YaHei UI", 9.0f);
            TextBox info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = "模型安装位置：\r\n" + aiRoot + "\r\n\r\n推荐直接点击下面的一键安装。程序只从 Qwen 和 llama.cpp 官方发布页下载。\r\n轻量版约2.5GB；高质量版约5GB。下载完成后永久离线免费，不需要账号、API或Python。\r\n\r\n如果官方站下载不畅，也可以从QQ群取得模型包并解压到上面的目录。" };
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 105 };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
            install4.Text = "一键安装轻量4B"; install4.AutoSize = true;
            install4.Click += async delegate { await InstallAsync(false); };
            install8.Text = "一键安装高质量8B"; install8.AutoSize = true;
            install8.Click += async delegate { await InstallAsync(true); };
            update.Text = "检查并更新现有模型"; update.AutoSize = true;
            update.Click += async delegate { await UpdateExistingAsync(); };
            Button folder = new Button { Text = "打开模型文件夹", AutoSize = true };
            folder.Click += delegate { Directory.CreateDirectory(aiRoot); Process.Start("explorer.exe", aiRoot); };
            buttons.Controls.Add(install4); buttons.Controls.Add(install8); buttons.Controls.Add(update); buttons.Controls.Add(folder);
            progressBar.Location = new Point(8, 49); progressBar.Size = new Size(430, 20);
            progress.Location = new Point(447, 51); progress.AutoSize = true; progress.Text = "尚未开始";
            bottom.Controls.Add(buttons); bottom.Controls.Add(progressBar); bottom.Controls.Add(progress);
            Controls.Add(info); Controls.Add(bottom);
        }

        private async Task InstallAsync(bool highQuality)
        {
            SetButtons(false);
            try
            {
                Directory.CreateDirectory(aiRoot);
                await InstallRuntimeAsync(false);

                string size = highQuality ? "8B" : "4B";
                string fileName = "Qwen3-" + size + "-Q4_K_M.gguf";
                string modelUrl = "https://huggingface.co/Qwen/Qwen3-" + size + "-GGUF/resolve/main/" + fileName + "?download=true";
                await InstallModelAsync(modelUrl, Path.Combine(aiRoot, fileName), "下载" + (highQuality ? "高质量8B" : "轻量4B") + "模型", false);
                progressBar.Style = ProgressBarStyle.Continuous; progressBar.Value = 100; progress.Text = "安装完成";
                MessageBox.Show("永久免费AI模型已安装。关闭本窗口后点击AI翻译，首次载入可能需要几十秒。", "安装完成");
            }
            catch (Exception ex)
            {
                progressBar.Style = ProgressBarStyle.Continuous; progressBar.Value = 0; progress.Text = "安装未完成";
                MessageBox.Show(ex.Message + "\n\n已下载的完整文件会保留；也可以从QQ群获取离线模型包。", "安装失败");
            }
            finally { SetButtons(true); }
        }

        private async Task UpdateExistingAsync()
        {
            SetButtons(false);
            try
            {
                Directory.CreateDirectory(aiRoot);
                string[] models = Directory.GetFiles(aiRoot, "Qwen3-*-Q4_K_M.gguf", SearchOption.AllDirectories);
                if (models.Length == 0) { MessageBox.Show("没有发现已安装模型，请先选择4B或8B安装。", "检查更新"); return; }
                bool changed = await InstallRuntimeAsync(true);
                foreach (string modelPath in models)
                {
                    string fileName = Path.GetFileName(modelPath);
                    string size = fileName.IndexOf("8B", StringComparison.OrdinalIgnoreCase) >= 0 ? "8B" : "4B";
                    string url = "https://huggingface.co/Qwen/Qwen3-" + size + "-GGUF/resolve/main/" + fileName + "?download=true";
                    changed = await InstallModelAsync(url, modelPath, "更新" + size + "模型", true) || changed;
                }
                progressBar.Style = ProgressBarStyle.Continuous; progressBar.Value = 100;
                progress.Text = changed ? "更新完成" : "已经是最新版";
                MessageBox.Show(changed ? "模型和运行库检查完成，已安装可用更新。" : "当前模型和运行库已经是最新版。", "检查更新");
            }
            catch (Exception ex)
            {
                progress.Text = "更新失败，旧版仍保留";
                MessageBox.Show(ex.Message + "\n\n更新使用临时文件，失败不会覆盖当前可用模型。", "更新失败");
            }
            finally { SetButtons(true); }
        }

        private void SetButtons(bool enabled)
        {
            install4.Enabled = enabled; install8.Enabled = enabled; update.Enabled = enabled;
        }

        private async Task<bool> InstallRuntimeAsync(bool updateOnly)
        {
            progressBar.Style = ProgressBarStyle.Marquee; progress.Text = "检查官方运行库…";
            string runtimeUrl = await Task.Factory.StartNew(delegate { return FindRuntimeUrl(); });
            string marker = Path.Combine(aiRoot, "runtime.url.txt");
            string server = Directory.Exists(aiRoot) ? FindFile(aiRoot, "llama-server.exe") : null;
            if (updateOnly && server != null && File.Exists(marker) && File.ReadAllText(marker).Trim() == runtimeUrl) return false;
            string zipPath = Path.Combine(aiRoot, "llama-vulkan.zip.download");
            await DownloadAsync(runtimeUrl, zipPath, "下载显卡运行库");
            progressBar.Style = ProgressBarStyle.Marquee; progress.Text = "安全解压运行库…";
            string staging = Path.Combine(aiRoot, "runtime-new");
            string current = Path.Combine(aiRoot, "runtime-current");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            await Task.Factory.StartNew(delegate { ExtractSafe(zipPath, staging); });
            if (FindFile(staging, "llama-server.exe") == null) throw new InvalidDataException("运行包中没有 llama-server.exe");
            try { File.Delete(zipPath); } catch { }
            string old = Path.Combine(aiRoot, "runtime-old");
            try { if (Directory.Exists(old)) Directory.Delete(old, true); } catch { }
            if (Directory.Exists(current)) Directory.Move(current, old);
            try { Directory.Move(staging, current); }
            catch { if (Directory.Exists(old) && !Directory.Exists(current)) Directory.Move(old, current); throw; }
            if (Directory.Exists(old)) Directory.Delete(old, true);
            File.WriteAllText(marker, runtimeUrl, new UTF8Encoding(false));
            return true;
        }

        private async Task<bool> InstallModelAsync(string url, string destination, string stage, bool updateOnly)
        {
            progressBar.Style = ProgressBarStyle.Marquee; progress.Text = "检查官方模型…";
            RemoteFileInfo remote = await Task.Factory.StartNew(delegate { return GetRemoteInfo(url); });
            string etagPath = destination + ".etag";
            bool sameLength = File.Exists(destination) && remote.Length > 0 && new FileInfo(destination).Length == remote.Length;
            bool sameEtag = !File.Exists(etagPath) || String.IsNullOrEmpty(remote.ETag) || File.ReadAllText(etagPath).Trim() == remote.ETag;
            if (sameLength && sameEtag) { progress.Text = "模型已经是最新版"; return false; }
            string temp = destination + ".download";
            if (File.Exists(temp) && remote.Length > 0 && new FileInfo(temp).Length > remote.Length) File.Delete(temp);
            if (!(File.Exists(temp) && remote.Length > 0 && new FileInfo(temp).Length == remote.Length))
                await DownloadAsync(url, temp, stage);
            if (remote.Length > 0 && new FileInfo(temp).Length != remote.Length) throw new InvalidDataException("模型下载大小不完整，可再次点击继续下载");
            if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
            if (!String.IsNullOrEmpty(remote.ETag)) File.WriteAllText(etagPath, remote.ETag, new UTF8Encoding(false));
            return true;
        }

        private static string FindFile(string root, string name)
        {
            string[] files = Directory.GetFiles(root, name, SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        private static RemoteFileInfo GetRemoteInfo(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "HEAD"; request.UserAgent = "FengYuMu-ModelInstaller"; request.Timeout = 20000;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                return new RemoteFileInfo { Length = response.ContentLength, ETag = (response.Headers["ETag"] ?? "").Trim() };
        }

        private static string FindRuntimeUrl()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/ggml-org/llama.cpp/releases/latest");
            request.UserAgent = "FengYuMu-ModelInstaller"; request.Timeout = 20000;
            string json;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) json = reader.ReadToEnd();
            Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object assetsValue = root["assets"];
            object[] assets = assetsValue as object[];
            if (assets == null)
            {
                System.Collections.ArrayList list = assetsValue as System.Collections.ArrayList;
                if (list != null) assets = list.ToArray();
            }
            if (assets != null) foreach (object item in assets)
            {
                Dictionary<string, object> asset = item as Dictionary<string, object>;
                if (asset == null) continue;
                string name = Convert.ToString(asset["name"]);
                if (name.IndexOf("bin-win-vulkan-x64.zip", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Convert.ToString(asset["browser_download_url"]);
            }
            throw new InvalidOperationException("官方发布页暂时没有 Windows Vulkan 运行包，请稍后再试。");
        }

        private async Task DownloadAsync(string url, string destination, string stage)
        {
            await Task.Factory.StartNew(delegate {
                long existing = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "FengYuMu-ModelInstaller"; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                if (existing > 0) request.AddRange(existing);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    bool resumed = response.StatusCode == HttpStatusCode.PartialContent;
                    if (!resumed) existing = 0;
                    long total = response.ContentLength > 0 ? existing + response.ContentLength : 0;
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(destination, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        byte[] buffer = new byte[1024 * 256]; int read; long done = existing; DateTime last = DateTime.MinValue;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read); done += read;
                            if ((DateTime.Now - last).TotalMilliseconds < 180) continue;
                            last = DateTime.Now;
                            int percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                            BeginInvoke((MethodInvoker)delegate {
                                progressBar.Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                                if (total > 0) progressBar.Value = percent;
                                progress.Text = stage + " " + (done / 1024 / 1024) + "MB" + (total > 0 ? "/" + (total / 1024 / 1024) + "MB" : "");
                            });
                        }
                    }
                }
            });
        }

        private static void ExtractSafe(string zipPath, string destination)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("运行包包含不安全路径");
                    if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write)) input.CopyTo(output);
                }
            }
        }
    }

    internal sealed class OnlineAiForm : Form
    {
        private readonly TextBox endpoint = new TextBox();
        private readonly TextBox model = new TextBox();
        private readonly TextBox apiKey = new TextBox();
        public OnlineAiForm()
        {
            Text = "可选在线AI设置"; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(650, 275); Font = new Font("Microsoft YaHei UI", 9.0f);
            OnlineAiSettings value = OnlineAiSettings.Load();
            AddField("接口地址：", endpoint, 24); AddField("模型名称：", model, 70); AddField("API密钥：", apiKey, 116);
            endpoint.Text = value.Endpoint; model.Text = value.Model; apiKey.Text = value.ApiKey; apiKey.UseSystemPasswordChar = true;
            Label hint = new Label { Location = new Point(24, 158), Size = new Size(596, 50), ForeColor = Color.DimGray,
                Text = "支持 OpenAI 兼容的 /v1/chat/completions 接口。在线复核完全可选；服务是否免费由提供方决定。密钥使用 Windows 当前账户加密保存，不写入词库。" };
            Button save = new Button { Text = "保存", Location = new Point(522, 225), Size = new Size(98, 31) };
            save.Click += delegate {
                OnlineAiSettings settings = new OnlineAiSettings { Endpoint = endpoint.Text.Trim(), Model = model.Text.Trim(), ApiKey = apiKey.Text.Trim() };
                if (settings.Endpoint.Length > 0 && !settings.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !settings.Endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show("远程接口必须使用 HTTPS。", "在线AI设置"); return; }
                settings.Save(); DialogResult = DialogResult.OK; Close();
            };
            Controls.Add(hint); Controls.Add(save);
        }
        private void AddField(string label, TextBox box, int y)
        {
            Controls.Add(new Label { Text = label, Location = new Point(24, y + 5), AutoSize = true });
            box.Location = new Point(112, y); box.Size = new Size(508, 27); Controls.Add(box);
        }
    }
}
