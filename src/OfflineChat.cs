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
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MapleOverlay
{
    internal sealed class KnowledgeInitializationResult
    {
        public int Entries;
        public int Categories;
        public string Fingerprint;
        public bool Changed;
    }

    internal static class MapleKnowledgeInitializer
    {
        private const string ManifestName = "枫语幕知识初始化.json";

        public static KnowledgeInitializationResult Initialize(string aiRoot, string dictionaryPath)
        {
            if (!File.Exists(dictionaryPath)) throw new FileNotFoundException("找不到枫语幕词库", dictionaryPath);
            Directory.CreateDirectory(aiRoot);
            byte[] bytes = File.ReadAllBytes(dictionaryPath);
            string fingerprint;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder value = new StringBuilder(hash.Length * 2);
                foreach (byte item in hash) value.Append(item.ToString("x2"));
                fingerprint = value.ToString();
            }

            int entries = 0;
            HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadLines(dictionaryPath, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 2 || String.IsNullOrWhiteSpace(parts[0]) || String.IsNullOrWhiteSpace(parts[1])) continue;
                entries++;
                if (parts.Length > 2)
                {
                    string category = parts[2]; int separator = category.LastIndexOf('#');
                    if (separator > 0) category = category.Substring(0, separator);
                    if (category.Length > 0) categories.Add(category);
                }
            }

            string path = Path.Combine(aiRoot, ManifestName);
            string previousFingerprint = "";
            if (File.Exists(path))
            {
                try
                {
                    Dictionary<string, object> old = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                    object oldValue; if (old.TryGetValue("dictionarySha256", out oldValue)) previousFingerprint = Convert.ToString(oldValue);
                }
                catch { }
            }
            bool changed = !String.Equals(previousFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                Dictionary<string, object> manifest = new Dictionary<string, object> {
                    { "format", 1 },
                    { "initializedAt", DateTime.Now.ToString("s") },
                    { "dictionarySha256", fingerprint },
                    { "entries", entries },
                    { "categories", categories.Count },
                    { "mode", "local-retrieval-knowledge-initialization" },
                    { "sources", new string[] {
                        "https://mscw-guidebook.com/", "https://mxdgcw.dvg.cn/",
                        "https://meowdb.com/msclassic/", "https://mxd079.dvg.cn/"
                    } },
                    { "note", "资料站已整理内容通过枫语幕词库按需检索；不修改GGUF模型权重。" }
                };
                string temp = path + ".new";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(manifest), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            return new KnowledgeInitializationResult {
                Entries = entries, Categories = categories.Count, Fingerprint = fingerprint, Changed = changed
            };
        }
    }

    internal sealed class OnlineAiSettings
    {
        public const string DefaultProvider = "豆包 2.0 Lite（推荐）";
        public const string DefaultEndpoint = "https://ark.cn-beijing.volces.com/api/v3/responses";
        public const string DefaultModel = "doubao-seed-2-0-lite-260215";
        public string Provider = DefaultProvider;
        public string Endpoint = DefaultEndpoint;
        public string Model = DefaultModel;
        public string ApiKey = "";
        public bool IsReady
        {
            get
            {
                return Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    Model.Length > 0 && ApiKey.Length > 0;
            }
        }

        public static OnlineAiSettings Load()
        {
            OnlineAiSettings value = new OnlineAiSettings();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu\OnlineAI"))
            {
                if (key == null) return value;
                value.Endpoint = Convert.ToString(key.GetValue("Endpoint", DefaultEndpoint));
                value.Model = Convert.ToString(key.GetValue("Model", DefaultModel));
                object storedProvider = key.GetValue("Provider", null);
                value.Provider = Convert.ToString(storedProvider);
                if (value.Provider.Length == 0)
                {
                    bool legacyCustom = !String.Equals(value.Endpoint, DefaultEndpoint,
                        StringComparison.OrdinalIgnoreCase) ||
                        !String.Equals(value.Model, DefaultModel,
                            StringComparison.OrdinalIgnoreCase);
                    value.Provider = legacyCustom ? "自定义兼容接口" : DefaultProvider;
                }
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
                key.SetValue("Provider", Provider); key.SetValue("Endpoint", Endpoint);
                key.SetValue("Model", Model);
                string encrypted = ApiKey.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(ApiKey), null, DataProtectionScope.CurrentUser));
                key.SetValue("ApiKey", encrypted);
            }
        }

        public static void Clear()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu\OnlineAI"))
            {
                key.SetValue("Provider", DefaultProvider);
                key.SetValue("Endpoint", DefaultEndpoint);
                key.SetValue("Model", DefaultModel);
                key.DeleteValue("ApiKey", false);
            }
        }
    }

    internal sealed class OnlineAiPreset
    {
        internal string Name;
        internal string Endpoint;
        internal string Model;
        internal string ApplyUrl;
        internal string PlainHint;
    }

    internal static class OnlineAiPresets
    {
        internal static readonly OnlineAiPreset[] All = new OnlineAiPreset[] {
            new OnlineAiPreset {
                Name = OnlineAiSettings.DefaultProvider,
                Endpoint = OnlineAiSettings.DefaultEndpoint,
                Model = OnlineAiSettings.DefaultModel,
                ApplyUrl = "https://console.volcengine.com/ark",
                PlainHint = "国内连接通常快，短句效果和费用比较均衡；需要火山方舟账号并开通模型。"
            },
            new OnlineAiPreset {
                Name = "DeepSeek V4 Flash（快速）",
                Endpoint = "https://api.deepseek.com/v1/chat/completions",
                Model = "deepseek-v4-flash",
                ApplyUrl = "https://platform.deepseek.com/api_keys",
                PlainHint = "短句速度快、理解口语不错；需要 DeepSeek 开放平台账号和可用余额。"
            },
            new OnlineAiPreset {
                Name = "智谱 GLM-4-Flash（免费备用）",
                Endpoint = "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                Model = "glm-4-flash-250414",
                ApplyUrl = "https://bigmodel.cn/usercenter/proj-mgmt/apikeys",
                PlainHint = "官方提供免费 API，适合先试用；繁忙时速度和译文稳定性可能不如前两项。"
            },
            new OnlineAiPreset {
                Name = "自定义兼容接口",
                Endpoint = "https://",
                Model = "",
                ApplyUrl = "",
                PlainHint = "仅给已经知道接口地址和模型名的用户使用；普通玩家不用选这一项。"
            }
        };

        internal static OnlineAiPreset Find(string name)
        {
            foreach (OnlineAiPreset item in All)
                if (String.Equals(item.Name, name, StringComparison.Ordinal)) return item;
            return All[0];
        }
    }

    internal static class OnlineAiClient
    {
        public static Task<string> TranslateAsync(OnlineAiSettings settings, string source,
            string target, string glossary)
        {
            return Task.Factory.StartNew(delegate {
                string body = BuildRequestBody(settings, source, target, glossary);
                byte[] data = Encoding.UTF8.GetBytes(body);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(settings.Endpoint);
                request.Method = "POST"; request.ContentType = "application/json";
                request.Timeout = 12000; request.ReadWriteTimeout = 12000;
                request.KeepAlive = true; request.ContentLength = data.Length;
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + settings.ApiKey;
                using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                string json;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) json = reader.ReadToEnd();
                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                string translated = ExtractText(root);
                translated = Regex.Replace(translated, "<think>[\\s\\S]*?</think>", "",
                    RegexOptions.IgnoreCase).Trim();
                translated = Regex.Replace(translated, @"^(?:最终译文|译文|中文)\s*[:：]\s*", "",
                    RegexOptions.IgnoreCase).Trim().Trim('`', '"');
                translated = Regex.Replace(translated, @"[\r\n]+", " ").Trim();
                if (translated.Length == 0) throw new InvalidOperationException("联网AI没有返回译文");
                return translated;
            });
        }

        internal static string DescribeFailure(Exception error, OnlineAiSettings settings)
        {
            while (error is AggregateException && error.InnerException != null)
                error = error.InnerException;
            WebException web = error as WebException;
            if (web != null)
            {
                HttpWebResponse response = web.Response as HttpWebResponse;
                if (response != null)
                {
                    int statusCode = (int)response.StatusCode;
                    string responseText = "";
                    try
                    {
                        using (response)
                        using (StreamReader reader = new StreamReader(
                            response.GetResponseStream(), Encoding.UTF8))
                            responseText = reader.ReadToEnd();
                    }
                    catch { }
                    return DescribeHttpFailure(statusCode, responseText,
                        settings == null ? "" : settings.Provider);
                }
                if (web.Status == WebExceptionStatus.Timeout)
                    return "连接超时。先确认浏览器能打开服务商页面，再点一次测试。";
                if (web.Status == WebExceptionStatus.NameResolutionFailure)
                    return "找不到服务商地址。请检查网络或 DNS，接口地址本身不要手改。";
                if (web.Status == WebExceptionStatus.ConnectFailure ||
                    web.Status == WebExceptionStatus.ProxyNameResolutionFailure)
                    return "网络没有连到服务商。请检查代理或防火墙，再点一次测试。";
                if (web.Status == WebExceptionStatus.TrustFailure ||
                    web.Status == WebExceptionStatus.SecureChannelFailure)
                    return "安全连接失败。请校准 Windows 日期时间并确认已启用 TLS 1.2。";
            }
            if (error is InvalidOperationException)
                return "已经连到服务商，但没有拿到有效译文。请稍后重试或换一家 AI 服务。";
            return "连接失败。请检查网络后再试；仍失败可换一家 AI 服务。";
        }

        internal static string DescribeHttpFailure(int statusCode, string responseText,
            string provider)
        {
            string service = provider != null && provider.IndexOf("豆包",
                StringComparison.OrdinalIgnoreCase) >= 0 ? "火山方舟" : "服务商";
            string lower = (responseText ?? "").ToLowerInvariant();
            string suffix = ExtractSafeErrorCode(responseText);
            if (lower.Contains("modelnotopen") || lower.Contains("model_not_open") ||
                lower.Contains("model not open"))
                return "API Key 已通过验证，但账号还没开通所选模型。点上面的“打开申请页面”→“开通管理”→选择并开通豆包 Seed 2.0 Lite，等一两分钟再测试。" + suffix;
            if (statusCode == 401)
                return "API Key 不对或复制不完整。请在" + service +
                    "的“API Key 管理”重新创建；不要填 Access Key 或 Secret Key。" + suffix;
            if (statusCode == 403)
                return "Key 已到达" + service +
                    "，但模型未开通、没有调用权限或余额不足。先开通所选模型再试。" + suffix;
            if (statusCode == 404)
                return "接口地址或模型名不存在。重新选择上面的 AI 服务，可恢复官方地址和模型名。" + suffix;
            if (statusCode == 429)
                return "免费额度、余额或调用频率已到上限。稍后重试，或到服务商页面检查额度。" + suffix;
            if (statusCode == 400 && (lower.Contains("model") ||
                lower.Contains("endpoint")))
                return "模型没有开通或模型名无效。请先在服务商页面开通该模型，再重新选择服务。" + suffix;
            if (statusCode == 400)
                return "服务商拒绝了请求参数。请重新选择 AI 服务恢复默认设置，再测试。" + suffix;
            if (statusCode >= 500)
                return "服务商临时故障。等几十秒再试；仍失败可先换另一家 AI 服务。" + suffix;
            return "服务商返回错误（HTTP " + statusCode + "）。请稍后重试或换一家 AI 服务。" + suffix;
        }

        private static string ExtractSafeErrorCode(string responseText)
        {
            Match match = Regex.Match(responseText ?? "",
                "\\\"(?:code|error_code)\\\"\\s*:\\s*\\\"([A-Za-z0-9._-]{1,80})\\\"",
                RegexOptions.IgnoreCase);
            return match.Success ? "（错误码：" + match.Groups[1].Value + "）" : "";
        }

        internal static string BuildRequestBody(OnlineAiSettings settings, string source,
            string target, string glossary)
        {
            string system =
                "你是冒险岛怀旧服国际服老玩家兼聊天翻译。把一名玩家的一条聊天翻成自然、简短的" + target +
                "游戏口语，先理解整句意图，禁止逐词硬译。结合冒险岛的地图、职业、装备、技能、任务、" +
                "组队任务、交易缩写和玩家俚语理解；B>、S>、WTB、WTS分别按收购、出售等玩家说法处理。" +
                "混合大小写、数字或无空格的专有词可能是玩家ID，句尾称呼也可能是玩家ID，证据不足就保留原文；" +
                "__FYM_PLAYER_数字__占位符必须原样保留。保留数字、频道、表情和语气，不得漏译、重复或编造。" +
                "例：How do you whisper back someone?＝怎么回复别人的悄悄话？；" +
                "which event is it i JUST went to orbis bro BriskIcedTea＝这是哪个活动啊兄弟？我刚去了天空之城，BriskIcedTea。" +
                "只输出一行最终译文，不要输出分析、思考、解释、标题、原文、注释、前缀或引号。";
            string user = "只翻译下面这一条玩家聊天：\n" + source +
                (glossary.Length > 0 ? "\n这句话命中的冒险岛词库术语（必须优先采用）：\n" + glossary : "");
            bool responsesApi = settings.Endpoint.TrimEnd('/').EndsWith("/responses",
                StringComparison.OrdinalIgnoreCase);
            Dictionary<string, object> requestBody = new Dictionary<string, object> {
                { "model", settings.Model }, { "temperature", 0.1 }
            };
            if (responsesApi)
            {
                requestBody["instructions"] = system;
                requestBody["input"] = user;
                requestBody["max_output_tokens"] = 160;
                requestBody["thinking"] = new Dictionary<string, string> { { "type", "disabled" } };
            }
            else
            {
                requestBody["max_tokens"] = 160;
                if (settings.Model.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase))
                    requestBody["thinking"] = new Dictionary<string, string> { { "type", "disabled" } };
                if (settings.Model.StartsWith("glm-", StringComparison.OrdinalIgnoreCase))
                    requestBody["do_sample"] = false;
                requestBody["messages"] = new object[] {
                    new Dictionary<string, string> { { "role", "system" }, { "content", system } },
                    new Dictionary<string, string> { { "role", "user" }, { "content", user } }
                };
            }
            return new JavaScriptSerializer().Serialize(requestBody);
        }

        private static string ExtractText(Dictionary<string, object> root)
        {
            object value;
            if (root.TryGetValue("output_text", out value) && Convert.ToString(value).Length > 0)
                return Convert.ToString(value);
            if (root.TryGetValue("choices", out value))
            {
                object[] choices = AsArray(value);
                if (choices.Length > 0)
                {
                    Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
                    object messageValue;
                    if (choice != null && choice.TryGetValue("message", out messageValue))
                    {
                        Dictionary<string, object> message = messageValue as Dictionary<string, object>;
                        object contentValue;
                        if (message != null && message.TryGetValue("content", out contentValue))
                            return Convert.ToString(contentValue);
                    }
                }
            }
            if (root.TryGetValue("output", out value))
                foreach (object outputItem in AsArray(value))
                {
                    Dictionary<string, object> item = outputItem as Dictionary<string, object>;
                    object contentValue;
                    if (item == null || !item.TryGetValue("content", out contentValue)) continue;
                    foreach (object contentItem in AsArray(contentValue))
                    {
                        Dictionary<string, object> content = contentItem as Dictionary<string, object>;
                        object textValue;
                        if (content != null && content.TryGetValue("text", out textValue) &&
                            Convert.ToString(textValue).Length > 0) return Convert.ToString(textValue);
                    }
                }
            throw new InvalidOperationException("联网AI返回格式中没有找到最终译文");
        }

        private static object[] AsArray(object value)
        {
            object[] array = value as object[];
            if (array != null) return array;
            System.Collections.ArrayList list = value as System.Collections.ArrayList;
            return list == null ? new object[0] : list.ToArray();
        }
    }

    internal sealed class OfflineAiClient
    {
        private const int Port = 17891;
        private const uint LcmapSimplifiedChinese = 0x02000000;
        private readonly string baseDir;
        private readonly object startupLock = new object();
        private Process process;
        private Task<bool> startupTask;
        private string cachedServerPath;
        private string cachedModelPath;
        public string Status { get; private set; }

        public OfflineAiClient(string programDir) { baseDir = programDir; Status = "AI模型尚未启动"; }

        public string AiRoot
        {
            get { return Path.Combine(baseDir, "模型"); }
        }

        private string LegacyAiRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FengYuMu", "AI"); }
        }

        public bool IsInstalled
        {
            get { return FindServer() != null && FindModel() != null; }
        }

        private string FindServer()
        {
            if (!String.IsNullOrEmpty(cachedServerPath) && File.Exists(cachedServerPath))
                return cachedServerPath;
            string[] roots = new string[] { AiRoot, Path.Combine(baseDir, "AI"), LegacyAiRoot };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string direct = Path.Combine(root, "llama-server.exe");
                if (File.Exists(direct)) { cachedServerPath = direct; return direct; }
                string[] found = Directory.GetFiles(root, "llama-server.exe", SearchOption.AllDirectories);
                if (found.Length > 0) { cachedServerPath = found[0]; return cachedServerPath; }
            }
            return null;
        }

        private string FindModel()
        {
            if (!String.IsNullOrEmpty(cachedModelPath) && File.Exists(cachedModelPath))
                return cachedModelPath;
            string[] roots = new string[] { AiRoot, Path.Combine(baseDir, "AI"), LegacyAiRoot };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string selected = Path.Combine(root, "selected-model.txt");
                if (File.Exists(selected))
                {
                    string selectedPath = Path.Combine(root, File.ReadAllText(selected).Trim());
                    if (File.Exists(selectedPath)) { cachedModelPath = selectedPath; return selectedPath; }
                }
                string[] found = Directory.GetFiles(root, "Qwen3-*.gguf", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, delegate(string left, string right) {
                        return new FileInfo(left).Length.CompareTo(new FileInfo(right).Length);
                    });
                    cachedModelPath = found[0];
                    return cachedModelPath;
                }
            }
            return null;
        }

        public void RefreshInstallationPaths()
        {
            cachedServerPath = null;
            cachedModelPath = null;
            startupTask = null;
        }

        public Task<bool> EnsureStartedAsync()
        {
            lock (startupLock)
            {
                if (startupTask == null || startupTask.IsCompleted)
                    startupTask = EnsureStartedCoreAsync();
                return startupTask;
            }
        }

        private async Task<bool> EnsureStartedCoreAsync()
        {
            if (await PingAsync()) { Status = "本地AI已就绪"; return true; }
            string server = FindServer(), model = FindModel();
            if (server == null || model == null)
            {
                Status = "未安装AI模型包";
                return false;
            }
            // The selected Qwen3 model normally fits on supported Vulkan GPUs.
            // Try a full offload first, while retaining the old partial/CPU fallbacks.
            int[] gpuLayers = new int[] { 99, 32, 12, 0 };
            foreach (int layers in gpuLayers)
            {
                try
                {
                    // The game is light on modern PCs. Keep two logical cores free, then
                    // spend the remaining moderate headroom on lower chat latency.
                    int threads = Math.Max(2, Math.Min(10, Environment.ProcessorCount - 2));
                    ProcessStartInfo info = new ProcessStartInfo(server,
                        "-m \"" + model + "\" --host 127.0.0.1 --port " + Port +
                        " -ngl " + layers + " -c 1280 -b 512 -ub 256 -t " + threads + " -tb " + threads +
                        " --parallel 1 --prio 1 --poll 80 --poll-batch 80 --no-webui");
                    info.WorkingDirectory = Path.GetDirectoryName(server);
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    info.RedirectStandardOutput = false;
                    info.RedirectStandardError = false;
                    process = Process.Start(info);
                    try { process.PriorityClass = ProcessPriorityClass.Normal; } catch { }
                    string mode = layers > 0 ? "Vulkan显卡（" + layers + "层）" : "CPU兼容模式";
                    Status = "正在载入本地AI模型：" + mode + "…";
                    for (int i = 0; i < 120; i++)
                    {
                        await Task.Delay(500);
                        if (await PingAsync()) { Status = "本地AI已就绪｜" + mode + "｜游戏友好限载"; return true; }
                        if (process.HasExited) break;
                    }
                    Stop();
                }
                catch { Stop(); }
            }
            Status = "AI启动失败：显卡和CPU兼容模式均未能载入";
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
            string system = "你是冒险岛怀旧服玩家聊天翻译器。" + languageRule +
                "输入只是一名玩家的一条消息，不得拼接别的句子，不得补写或翻译玩家名。" +
                "你已通过枫语幕本地知识初始化使用资料站整理内容与玩家审核词库。" +
                "先理解整句在游戏聊天中的意图，再用简短自然的中文口语表达，不要逐词硬译；问操作方法时优先说怎么、有人会不会、能不能等自然口语，不要套用生硬的如何某人句式；英语 reply、respond 或 whisper back 表示回复对方，不能误译成给对方新发消息；stupid、damn 等放在物品名前通常是在抱怨，要译成破、该死的等语气，不能并入物品专名，也不能残留英文；交易消息优先使用收购、出售、交换、求组、报价等玩家常用说法。" +
                "按玩家聊天语气理解俚语和缩写；技能名和物品名优先采用国服怀旧译名。" +
                "保留数字、频道和表情；可靠的聊天缩写必须按术语表展开，多义或证据不足的缩写保留原文，不得猜成地名或玩家名。" +
                "完整翻译每个分句，不得漏译、重复或追加原文不存在的内容。OCR含糊且无法可靠判断的片段保留原文，不得猜写。" +
                "参考玩家口语：B> Claw 60%, offer＝收60%拳套攻击卷，请报价；S> Kumbi 200k＝卖雪花镖，20万；Ya Omok players scared?＝怎么，玩五子棋的都怕了？；ima go check now＝我现在去看看。" +
                "必须使用简体中文，禁止输出繁体字。只输出一行自然译文，不复述原文，不解释。" +
                (String.IsNullOrEmpty(glossary) ? "" : "本次从知识库检索到的术语如下，必须优先采用：\n" + glossary);
            string userText = "待翻译消息：\n" + text + "\n/no_think";
            string body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "model", "local-qwen3" },
                { "temperature", 0.0 }, { "top_p", 0.7 }, { "max_tokens", 96 },
                { "messages", new object[] {
                    new Dictionary<string, string> { { "role", "system" }, { "content", system } },
                    new Dictionary<string, string> { { "role", "user" }, { "content", userText } }
                } }
            });
            string result = await Task.Factory.StartNew(delegate { return Post(body); });
            if (targetLanguage.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Regex.IsMatch(text, "[A-Za-z]") &&
                (Regex.Matches(result, "[\\u3400-\\u9fff]").Count < 2 ||
                    LooksLikeInstructionLeak(result)))
            {
                string retrySystem = "把玩家消息翻译成自然、简短的简体中文。必须出现中文，不得照抄英文，不得解释；操作问句用怎么、能不能等玩家口语；reply、respond、whisper back 表示回复，不能误译成新发消息；物品名前的 stupid、damn 是抱怨语气，不能并入物品名或残留英文；交易消息用收购、出售、交换、求组、报价等常用说法；技能名和物品名用冒险岛国服译名。" +
                    "可靠的聊天缩写按术语表展开；多义或证据不足时保留缩写，不得猜成地名或玩家名。" +
                    (String.IsNullOrEmpty(glossary) ? "" : "术语：\n" + glossary);
                string retryBody = new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                    { "model", "local-qwen3" }, { "temperature", 0.0 }, { "top_p", 0.7 }, { "max_tokens", 96 },
                    { "messages", new object[] {
                        new Dictionary<string, string> { { "role", "system" }, { "content", retrySystem } },
                        new Dictionary<string, string> { { "role", "user" }, { "content",
                            "待翻译消息：\n" + text + "\n只输出中文译文。\n/no_think" } }
                    } }
                });
                result = await Task.Factory.StartNew(delegate { return Post(retryBody); });
            }
            return ToSimplifiedChinese(result);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int LCMapStringEx(string localeName, uint mapFlags,
            string source, int sourceLength, StringBuilder destination, int destinationLength,
            IntPtr versionInformation, IntPtr reserved, IntPtr sortHandle);

        internal static string ToSimplifiedChinese(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? "";
            try
            {
                StringBuilder converted = new StringBuilder(value.Length * 2 + 2);
                int length = LCMapStringEx("zh-CN", LcmapSimplifiedChinese, value, -1,
                    converted, converted.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                return length > 0 ? converted.ToString() : value;
            }
            catch { return value; }
        }

        internal static bool LooksLikeInstructionLeak(string value)
        {
            string output = value ?? "";
            return output.IndexOf("待翻译消息", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("翻译说明", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("只输出中文", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("/no_think", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("系统提示", StringComparison.OrdinalIgnoreCase) >= 0;
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
            process = null;
            Status = "本地AI已释放内存";
        }
    }

    internal sealed class ChatRegionSelectorForm : Form
    {
        private const int LeftEdge = 1, RightEdge = 2, TopEdge = 4, BottomEdge = 8;
        private const int MoveArea = 16, NewArea = 32;
        private readonly Rectangle gameBounds;
        private readonly Bitmap snapshot;
        private readonly Panel toolbar = new Panel();
        private Rectangle selection;
        private Rectangle dragOriginal;
        private System.Drawing.Point dragStart;
        private int dragMode;
        private bool dragging;

        public Rectangle SelectedScreenRegion
        {
            get { return new Rectangle(gameBounds.X + selection.X, gameBounds.Y + selection.Y,
                selection.Width, selection.Height); }
        }

        public ChatRegionSelectorForm(Rectangle game, Rectangle initialScreenRegion)
        {
            gameBounds = game;
            snapshot = new Bitmap(Math.Max(1, game.Width), Math.Max(1, game.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(snapshot))
            {
                try { graphics.CopyFromScreen(game.Location, System.Drawing.Point.Empty, game.Size,
                    CopyPixelOperation.SourceCopy); }
                catch { graphics.Clear(Color.FromArgb(44, 48, 56)); }
            }

            Rectangle initial = Rectangle.Intersect(initialScreenRegion, game);
            selection = initial.Width >= 80 && initial.Height >= 30
                ? new Rectangle(initial.X - game.X, initial.Y - game.Y, initial.Width, initial.Height)
                : new Rectangle(game.Width * 10 / 100, game.Height * 76 / 100,
                    game.Width * 55 / 100, game.Height * 21 / 100);

            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Bounds = game; DoubleBuffered = true; KeyPreview = true;
            Cursor = Cursors.Cross;

            toolbar.Dock = DockStyle.Top; toolbar.Height = 46; toolbar.BackColor = Color.FromArgb(235, 20, 23, 28);
            Label help = new Label { Text = "拖动框体移动｜拖边缘或四角调整大小｜框内仅由AI翻译，F8完全跳过",
                ForeColor = Color.White, AutoSize = true, Location = new System.Drawing.Point(14, 14),
                Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold) };
            Button confirm = new Button { Text = "确定区域", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Button cancel = new Button { Text = "取消", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            confirm.Location = new System.Drawing.Point(Math.Max(420, game.Width - 176), 9);
            cancel.Location = new System.Drawing.Point(Math.Max(510, game.Width - 88), 9);
            confirm.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            toolbar.Controls.Add(help); toolbar.Controls.Add(confirm); toolbar.Controls.Add(cancel); Controls.Add(toolbar);
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
            MouseDown += SelectorMouseDown; MouseMove += SelectorMouseMove; MouseUp += SelectorMouseUp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(snapshot, 0, 0);
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(shade, 0, toolbar.Height, Width, Math.Max(0, selection.Top - toolbar.Height));
                e.Graphics.FillRectangle(shade, 0, selection.Bottom, Width, Math.Max(0, Height - selection.Bottom));
                e.Graphics.FillRectangle(shade, 0, selection.Top, Math.Max(0, selection.Left), selection.Height);
                e.Graphics.FillRectangle(shade, selection.Right, selection.Top, Math.Max(0, Width - selection.Right), selection.Height);
            }
            using (Pen border = new Pen(Color.FromArgb(255, 255, 170, 25), 3.0f))
                e.Graphics.DrawRectangle(border, selection);
            foreach (Rectangle handle in Handles())
            {
                e.Graphics.FillRectangle(Brushes.White, handle);
                e.Graphics.DrawRectangle(Pens.DarkOrange, handle);
            }
            string size = selection.Width + " × " + selection.Height;
            using (Font font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold))
            using (SolidBrush background = new SolidBrush(Color.FromArgb(220, 18, 18, 20)))
            {
                SizeF measured = e.Graphics.MeasureString(size, font);
                RectangleF label = new RectangleF(selection.Left, Math.Max(toolbar.Height, selection.Top - measured.Height - 8),
                    measured.Width + 12, measured.Height + 5);
                e.Graphics.FillRectangle(background, label);
                e.Graphics.DrawString(size, font, Brushes.White, label.X + 6, label.Y + 2);
            }
            base.OnPaint(e);
        }

        private Rectangle[] Handles()
        {
            const int size = 10, half = size / 2;
            int middleX = selection.Left + selection.Width / 2;
            int middleY = selection.Top + selection.Height / 2;
            return new Rectangle[] {
                new Rectangle(selection.Left-half, selection.Top-half, size, size),
                new Rectangle(middleX-half, selection.Top-half, size, size),
                new Rectangle(selection.Right-half, selection.Top-half, size, size),
                new Rectangle(selection.Left-half, middleY-half, size, size),
                new Rectangle(selection.Right-half, middleY-half, size, size),
                new Rectangle(selection.Left-half, selection.Bottom-half, size, size),
                new Rectangle(middleX-half, selection.Bottom-half, size, size),
                new Rectangle(selection.Right-half, selection.Bottom-half, size, size)
            };
        }

        private int HitTest(System.Drawing.Point point)
        {
            const int margin = 11;
            bool left = Math.Abs(point.X - selection.Left) <= margin;
            bool right = Math.Abs(point.X - selection.Right) <= margin;
            bool top = Math.Abs(point.Y - selection.Top) <= margin;
            bool bottom = Math.Abs(point.Y - selection.Bottom) <= margin;
            if (point.X >= selection.Left - margin && point.X <= selection.Right + margin &&
                point.Y >= selection.Top - margin && point.Y <= selection.Bottom + margin)
            {
                int edges = (left ? LeftEdge : 0) | (right ? RightEdge : 0) |
                    (top ? TopEdge : 0) | (bottom ? BottomEdge : 0);
                return edges != 0 ? edges : (selection.Contains(point) ? MoveArea : 0);
            }
            return NewArea;
        }

        private void SelectorMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Y < toolbar.Height) return;
            dragging = true; dragStart = e.Location; dragOriginal = selection; dragMode = HitTest(e.Location);
            if (dragMode == NewArea) selection = new Rectangle(e.X, e.Y, 1, 1);
            Capture = true; Invalidate();
        }

        private void SelectorMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                int hit = HitTest(e.Location);
                Cursor = CursorFor(hit); return;
            }
            int x = Math.Max(0, Math.Min(Width - 1, e.X));
            int y = Math.Max(toolbar.Height, Math.Min(Height - 1, e.Y));
            if (dragMode == NewArea)
            {
                selection = Rectangle.FromLTRB(Math.Min(dragStart.X, x), Math.Min(dragStart.Y, y),
                    Math.Max(dragStart.X, x), Math.Max(dragStart.Y, y));
            }
            else if (dragMode == MoveArea)
            {
                int nx = Math.Max(0, Math.Min(Width - dragOriginal.Width, dragOriginal.X + x - dragStart.X));
                int ny = Math.Max(toolbar.Height, Math.Min(Height - dragOriginal.Height, dragOriginal.Y + y - dragStart.Y));
                selection = new Rectangle(nx, ny, dragOriginal.Width, dragOriginal.Height);
            }
            else
            {
                int left = dragOriginal.Left, right = dragOriginal.Right;
                int top = dragOriginal.Top, bottom = dragOriginal.Bottom;
                if ((dragMode & LeftEdge) != 0) left = Math.Min(x, right - 80);
                if ((dragMode & RightEdge) != 0) right = Math.Max(x, left + 80);
                if ((dragMode & TopEdge) != 0) top = Math.Min(y, bottom - 40);
                if ((dragMode & BottomEdge) != 0) bottom = Math.Max(y, top + 40);
                left = Math.Max(0, left); top = Math.Max(toolbar.Height, top);
                right = Math.Min(Width - 1, right); bottom = Math.Min(Height - 1, bottom);
                selection = Rectangle.FromLTRB(left, top, right, bottom);
            }
            Invalidate();
        }

        private void SelectorMouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false; Capture = false;
            if (selection.Width < 80 || selection.Height < 40)
                selection = dragOriginal.Width >= 80 ? dragOriginal :
                    new Rectangle(Width * 10 / 100, Height * 76 / 100, Width * 55 / 100, Height * 21 / 100);
            Invalidate();
        }

        private static Cursor CursorFor(int hit)
        {
            if ((hit & (LeftEdge | RightEdge)) != 0 && (hit & (TopEdge | BottomEdge)) != 0)
                return ((hit & LeftEdge) != 0) == ((hit & TopEdge) != 0) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            if ((hit & (LeftEdge | RightEdge)) != 0) return Cursors.SizeWE;
            if ((hit & (TopEdge | BottomEdge)) != 0) return Cursors.SizeNS;
            return hit == MoveArea ? Cursors.SizeAll : Cursors.Cross;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && snapshot != null) snapshot.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class OfflineChatForm : Form
    {
        private sealed class PendingChatLine
        {
            internal string Text = "";
            internal ChatVisualStyle Style = ChatVisualStyle.Default;
        }

        private readonly OverlayForm overlay;
        private readonly string dictionaryPath;
        private readonly OfflineAiClient ai;
        private readonly RichTextBox output = new RichTextBox();
        private readonly Font outputOriginalFont = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
        private readonly Font outputTranslationFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        private readonly Label status = new Label();
        private readonly Button liveButton = new Button();
        private readonly Label cloudMode = new Label();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer releaseTimer = new System.Windows.Forms.Timer();
        private readonly HashSet<string> protectedPlayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<PendingChatLine> pendingChatLines = new Queue<PendingChatLine>();
        private readonly Queue<KeyValuePair<string, DateTime>> recentlyQueuedChatSources =
            new Queue<KeyValuePair<string, DateTime>>();
        private readonly Dictionary<string, string> translationCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Queue<string> translationCacheOrder = new Queue<string>();
        private List<string> previousChatFrame = new List<string>();
        private readonly List<List<string>> recentChatFrames = new List<List<string>>();
        private readonly List<KeyValuePair<string, string>> glossaryEntries = new List<KeyValuePair<string, string>>();
        private readonly HashSet<string> contextOnlyGlossaryKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private KnowledgeInitializationResult knowledge;
        private bool knowledgeInitializationBusy;
        private Rectangle chatRegion;
        private bool live;
        private bool firstLiveCapture;
        private bool captureBusy;
        private bool translateBusy;
        private int liveSessionVersion;
        private int screenshotCaptureSuspendCount;
        private bool floatingWindowEnabled;
        private AiTranslationWindowForm floatingWindow;
        private DateTime lastAiUse = DateTime.MinValue;

        public OfflineChatForm(OverlayForm owner, string baseDir)
        {
            overlay = owner;
            dictionaryPath = Path.Combine(baseDir, "枫语幕词库.tsv");
            ai = new OfflineAiClient(baseDir);
            LoadGlossary();
            Text = "枫语幕｜AI实时聊天翻译｜@奇怪小鸭";
            Icon = SystemIcons.Information; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(600, 560); MinimumSize = new Size(520, 480);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BuildUi(); LoadRegion();
            timer.Interval = 140;
            timer.Tick += async delegate { await PollChatAsync(); };
            releaseTimer.Interval = 30000;
            releaseTimer.Tick += delegate {
                if (!live && lastAiUse != DateTime.MinValue && DateTime.Now - lastAiUse > TimeSpan.FromMinutes(5))
                {
                    ai.Stop(); lastAiUse = DateTime.MinValue;
                    status.Text = "空闲5分钟，已释放AI模型内存";
                }
            };
            releaseTimer.Start();
            Shown += async delegate {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(work.Left, work.Right - Width - 18), work.Top + 55);
                RefreshAiStatus();
                OnlineAiSettings online = OnlineAiSettings.Load();
                Task<bool> warmup = !online.IsReady && ai.IsInstalled ? ai.EnsureStartedAsync() : null;
                await InitializeKnowledgeInBackgroundAsync(false);
                if (warmup != null)
                {
                    bool ready = await warmup;
                    status.Text = ready ? "AI已预热｜热态翻译约数百毫秒" : ai.Status;
                }
                else if (online.IsReady) status.Text = "联网AI已就绪｜离线模型仅在联网失败时启动";
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    // The control window is only a settings/history surface. Closing it must
                    // not stop live capture or take the in-game translation window with it.
                    e.Cancel = true;
                    Hide();
                    if (live && floatingWindowEnabled && floatingWindow != null &&
                        !floatingWindow.IsDisposed && floatingWindow.DisplayedText.Length > 0)
                        floatingWindow.ShowPassiveIfAllowed();
                }
            };
            Disposed += delegate { outputOriginalFont.Dispose(); outputTranslationFont.Dispose(); };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            liveButton.Text = "开始实时翻译"; liveButton.AutoSize = true;
            liveButton.Click += async delegate { await ToggleLiveAsync(); };
            Button bind = new Button { Text = "聊天框位置", AutoSize = true };
            bind.Click += async delegate { await BindRegionAsync(); };
            Button onlineSettings = new Button { Text = "联网AI设置", AutoSize = true };
            onlineSettings.Click += delegate {
                using (OnlineAiForm form = new OnlineAiForm()) form.ShowDialog(this);
                translationCache.Clear(); translationCacheOrder.Clear();
                RefreshCloudAiState();
            };
            Button install = new Button { Text = "离线备用模型", AutoSize = true };
            install.Click += async delegate {
                ai.Stop();
                using (AiInstallForm form = new AiInstallForm(ai.AiRoot, dictionaryPath)) form.ShowDialog(this);
                ai.RefreshInstallationPaths();
                LoadGlossary(); RefreshAiStatus();
                await InitializeKnowledgeInBackgroundAsync(false);
            };
            status.AutoSize = true; status.Padding = new Padding(8, 7, 0, 0); status.ForeColor = Color.DarkGreen;
            cloudMode.AutoSize = true; cloudMode.Padding = new Padding(8, 7, 0, 0);
            cloudMode.ForeColor = Color.FromArgb(37, 99, 235);
            tools.Controls.Add(liveButton); tools.Controls.Add(bind); tools.Controls.Add(onlineSettings);
            tools.Controls.Add(install); tools.Controls.Add(cloudMode);
            tools.Controls.Add(status);
            root.Controls.Add(tools, 0, 0);

            output.Dock = DockStyle.Fill; output.ReadOnly = true; output.BorderStyle = BorderStyle.FixedSingle;
            output.ScrollBars = RichTextBoxScrollBars.Vertical; output.BackColor = Color.FromArgb(24, 27, 32); output.ForeColor = Color.White;
            output.DetectUrls = false; output.Font = outputOriginalFont;
            output.Text = "准备好了。配好联网AI后会直接使用；联网失败时自动改用离线备用。\r\n\r\n";
            root.Controls.Add(output, 0, 1);
            RefreshCloudAiState();
            Controls.Add(root);
        }

        private void RefreshAiStatus()
        {
            status.Text = ai.IsInstalled
                ? "模型已安装｜知识初始化" + (knowledge == null ? "待检查" : "完成 " + knowledge.Entries + "条") + "｜游戏友好模式"
                : "未安装模型包";
        }

        private void RefreshCloudAiState()
        {
            OnlineAiSettings settings = OnlineAiSettings.Load();
            cloudMode.Text = settings.IsReady
                ? "当前：" + settings.Provider + "｜联网优先"
                : "当前：离线模式｜点“联网AI设置”可提速提准";
        }

        private void LoadRegion()
        {
            chatRegion = ChatRegionSettings.LoadAbsolute();
        }

        internal void UpdateChatRegion(Rectangle region, ChatRegionOrigin origin)
        {
            bool changed = chatRegion != region;
            chatRegion = region;
            if (changed)
            {
                previousChatFrame.Clear();
                recentChatFrames.Clear();
                pendingChatLines.Clear();
                recentlyQueuedChatSources.Clear();
            }
            if (IsHandleCreated)
                status.Text = (origin == ChatRegionOrigin.Manual ? "手动聊天区" : "F9自动聊天区") +
                    "已按当前游戏窗口同步 " + region.Width + "×" + region.Height;
        }

        internal void ApplyFloatingWindow(bool enabled)
        {
            floatingWindowEnabled = enabled;
            if (!enabled)
            {
                if (floatingWindow != null && !floatingWindow.IsDisposed) floatingWindow.Hide();
                return;
            }
            if (live) ShowFloatingWindowPassive();
        }

        private void ShowFloatingWindowPassive()
        {
            if (floatingWindow == null || floatingWindow.IsDisposed)
                floatingWindow = new AiTranslationWindowForm(overlay);
            floatingWindow.ShowPassive();
        }

        internal bool IsLiveTranslationRunning { get { return live; } }

        internal bool RestoreFloatingWindowFromTray()
        {
            if (!live) return false;
            floatingWindowEnabled = true;
            if (floatingWindow == null || floatingWindow.IsDisposed)
                floatingWindow = new AiTranslationWindowForm(overlay);
            floatingWindow.ShowPassive();
            return true;
        }

        internal async Task<bool> PrepareForScreenshotCaptureAsync()
        {
            screenshotCaptureSuspendCount++;
            timer.Stop();
            bool wasVisible = Visible;
            if (wasVisible) Hide();
            if (floatingWindow != null && !floatingWindow.IsDisposed && floatingWindow.Visible)
            {
                floatingWindow.Hide();
                wasVisible = true;
            }
            // AI live capture and F8/F9 use the same Windows OCR engine. Wait for an active
            // chat capture to leave it before the fresh screenshot starts.
            while (captureBusy && !IsDisposed) await Task.Delay(15);
            return wasVisible;
        }

        internal void RestoreAfterScreenshotCapture()
        {
            if (screenshotCaptureSuspendCount > 0) screenshotCaptureSuspendCount--;
            if (screenshotCaptureSuspendCount != 0) return;
            if (live)
            {
                timer.Start();
                if (floatingWindowEnabled && floatingWindow != null && !floatingWindow.IsDisposed)
                    floatingWindow.ShowPassiveIfAllowed();
            }
        }

        private async Task BindRegionAsync()
        {
            StopLiveTranslation();
            Hide();
            await Task.Delay(3000);
            Rectangle game = OverlayForm.GetForegroundCaptureBounds();
            Rectangle initial = ChatRegionSettings.ResolveForGame(game);
            using (ChatRegionSelectorForm selector = new ChatRegionSelectorForm(game, initial))
            {
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    chatRegion = selector.SelectedScreenRegion;
                    ChatRegionSettings.SaveManual(chatRegion, game);
                    previousChatFrame.Clear(); recentChatFrames.Clear(); pendingChatLines.Clear();
                    recentlyQueuedChatSources.Clear();
                    status.Text = "聊天区已统一绑定 " + chatRegion.Width + "×" + chatRegion.Height +
                        "｜框内AI翻译，F8跳过";
                }
            }
            Show(); Activate();
        }

        private async Task ToggleLiveAsync()
        {
            if (live)
            {
                StopLiveTranslation();
                return;
            }
            if (chatRegion.Width < 80) { MessageBox.Show("请先按 F9 自动对齐聊天框，或点击“框选/调整游戏聊天区”手动设置。", "实时聊天翻译"); return; }
            live = true;
            liveSessionVersion++;
            liveButton.Text = "停止实时翻译";
            previousChatFrame.Clear();
            recentChatFrames.Clear();
            pendingChatLines.Clear();
            recentlyQueuedChatSources.Clear();
            firstLiveCapture = true;
            if (overlay != null) overlay.ApplyAiChatFloatingWindow(true);
            else ApplyFloatingWindow(true);
            timer.Start();
            OnlineAiSettings online = OnlineAiSettings.Load();
            if (online.IsReady)
            {
                status.Text = online.Provider + "已就绪｜140ms快速监听";
            }
            else
            {
                status.Text = "正在预热离线AI，同时开始监听聊天…";
                int sessionVersion = liveSessionVersion;
                bool ready = await ai.EnsureStartedAsync();
                if (live && sessionVersion == liveSessionVersion)
                    status.Text = ready ? "离线AI已预热｜140ms快速监听" : ai.Status;
            }
        }

        internal void StopLiveTranslation()
        {
            live = false;
            liveSessionVersion++;
            timer.Stop();
            pendingChatLines.Clear();
            recentlyQueuedChatSources.Clear();
            liveButton.Text = "开始实时翻译";
            status.Text = "实时翻译已停止";
            if (floatingWindow != null && !floatingWindow.IsDisposed)
                floatingWindow.HideForStop();
        }

        private async Task PollChatAsync(bool forceOnce = false)
        {
            if ((!live && !forceOnce) || captureBusy || screenshotCaptureSuspendCount > 0) return;
            int sessionVersion = liveSessionVersion;
            captureBusy = true;
            try
            {
                ChatCaptureFrame capture = await overlay.CaptureChatAsync(chatRegion);
                if (!forceOnce && (!live || sessionVersion != liveSessionVersion)) return;
                // OcrResult.Text may flatten unrelated visual rows into one sentence.
                // Preserve the OCR engine's physical line boundaries for chat parsing.
                List<string> lines = ParseChatLines(capture.PhysicalLineText());
                List<string> newLines = GetNewChatLines(previousChatFrame, lines);
                if (firstLiveCapture)
                {
                    firstLiveCapture = false;
                    int first = Math.Max(0, lines.Count - 2);
                    newLines = lines.GetRange(first, lines.Count - first);
                }
                newLines = SuppressRecentOcrReappearances(recentChatFrames,
                    previousChatFrame, lines, newLines);
                previousChatFrame = lines;
                recentChatFrames.Add(new List<string>(lines));
                while (recentChatFrames.Count > 3) recentChatFrames.RemoveAt(0);
                // Frame differencing already preserves genuine repeated messages while
                // suppressing a static OCR frame. Text-based queue filtering would swallow
                // a real duplicate sent while the first copy is still being translated.
                foreach (string line in newLines)
                {
                    if (!IsUsefulChatLine(line) || IsRecentlyQueuedChatSource(line)) continue;
                    pendingChatLines.Enqueue(new PendingChatLine {
                        Text = line, Style = capture.FindStyle(line)
                    });
                    RememberQueuedChatSource(line);
                }
                status.Text = pendingChatLines.Count > 0 ? "检测到新消息，待翻译 " + pendingChatLines.Count + " 条" : status.Text;
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { captureBusy = false; }
            if (!translateBusy) await ProcessPendingChatAsync(sessionVersion, forceOnce);
        }

        private async Task ProcessPendingChatAsync(int sessionVersion, bool forceOnce)
        {
            if (translateBusy) return;
            translateBusy = true;
            try
            {
                while (pendingChatLines.Count > 0 &&
                    (forceOnce || (live && sessionVersion == liveSessionVersion)))
                {
                    PendingChatLine pending = pendingChatLines.Dequeue();
                    string line = pending.Text;
                    string speakerPrefix, message;
                    SplitSpeaker(line, out speakerPrefix, out message);
                    string cleanedMessage = NormalizeCommonChatOcr(message);
                    Dictionary<string, string> nameTokens;
                    string protectedMessage = ProtectPlayerNames(cleanedMessage, out nameTokens);
                    string glossary = BuildGlossary(protectedMessage);
                    lastAiUse = DateTime.Now;
                    string translated;
                    if (!TryKnownChatIntentTranslation(protectedMessage, out translated) &&
                        !TryExactGlossaryTranslation(protectedMessage, out translated))
                    {
                        string cacheKey = NormalizeChatPhrase(cleanedMessage);
                        if (!translationCache.TryGetValue(cacheKey, out translated))
                        {
                            string sourceLanguage = DetectChatSourceLanguage(cleanedMessage);
                            translated = await TranslateWithPreferredAiAsync(protectedMessage,
                                sourceLanguage, glossary);
                            if (!forceOnce && (!live || sessionVersion != liveSessionVersion)) return;
                            RememberTranslation(cacheKey, translated);
                        }
                    }
                    translated = RestorePlayerNames(translated, nameTokens);
                    AppendTranslation(line, speakerPrefix + translated, pending.Style);
                    status.Text = pendingChatLines.Count == 0 ? "新消息已翻译" : "正在翻译，剩余 " + pendingChatLines.Count + " 条";
                }
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { translateBusy = false; }
        }

        private async Task<string> TranslateWithPreferredAiAsync(string source,
            string sourceLanguage, string glossary)
        {
            OnlineAiSettings online = OnlineAiSettings.Load();
            if (online.IsReady)
            {
                try
                {
                    string translated = await OnlineAiClient.TranslateAsync(online, source,
                        "简体中文", glossary);
                    translated = OfflineAiClient.ToSimplifiedChinese(translated);
                    if (IsPlausibleChatTranslation(source, translated))
                    {
                        status.Text = online.Provider + "已翻译";
                        return translated;
                    }
                    status.Text = online.Provider + "返回异常，自动改用离线备用";
                }
                catch
                {
                    status.Text = online.Provider + "连接失败，自动改用离线备用";
                }
            }
            try
            {
                string translated = await ai.TranslateAsync(source, sourceLanguage, "中文", glossary);
                return IsPlausibleChatTranslation(source, translated) ? translated : source;
            }
            catch
            {
                status.Text = online.IsReady
                    ? "联网和离线备用都不可用，已保留原文"
                    : "请先配置联网AI或安装离线备用模型";
                return source;
            }
        }

        internal static List<string> GetNewChatLines(List<string> previous, List<string> current)
        {
            List<string> added = new List<string>();
            if (current == null || current.Count == 0) return added;
            if (previous == null || previous.Count == 0) { added.AddRange(current); return added; }
            int bestOverlap = 0;
            int maximum = Math.Min(previous.Count, current.Count);
            for (int overlap = maximum; overlap >= 1; overlap--)
            {
                bool same = true;
                for (int i = 0; i < overlap; i++)
                    if (!SameChatLine(previous[previous.Count - overlap + i], current[i])) { same = false; break; }
                if (same) { bestOverlap = overlap; break; }
            }
            if (bestOverlap > 0)
            {
                for (int i = bestOverlap; i < current.Count; i++) added.Add(current[i]);
                return added;
            }
            // OCR may alter one old character between frames. Use the newest stable anchor,
            // then enqueue only the suffix after it instead of waiting for many later lines.
            int anchor = -1;
            for (int i = 0; i < current.Count; i++)
                for (int j = previous.Count - 1; j >= 0; j--)
                    if (SameChatLine(previous[j], current[i])) { anchor = i; break; }
            if (anchor >= 0)
            {
                for (int i = anchor + 1; i < current.Count; i++) added.Add(current[i]);
            }
            else if (current.Count > previous.Count)
            {
                // With no reliable anchor, only a growing chat frame proves that something
                // was appended. A same-size but OCR-noisy frame is not new content.
                int appendCount = Math.Min(current.Count - previous.Count, current.Count);
                for (int i = current.Count - appendCount; i < current.Count; i++) added.Add(current[i]);
            }
            return added;
        }

        internal static List<string> SuppressRecentOcrReappearances(
            IList<List<string>> recentFrames, List<string> previous, List<string> current,
            List<string> candidates)
        {
            List<string> filtered = new List<string>();
            if (candidates == null || candidates.Count == 0) return filtered;
            foreach (string candidate in candidates)
            {
                int previousCount = CountSimilarChatLines(previous, candidate);
                int currentCount = CountSimilarChatLines(current, candidate);
                int olderMaximum = 0;
                if (recentFrames != null)
                    foreach (List<string> frame in recentFrames)
                        olderMaximum = Math.Max(olderMaximum,
                            CountSimilarChatLines(frame, candidate));
                // One OCR miss must not make an older line look newly appended. A genuine
                // repeated post raises the occurrence count and therefore still passes.
                if (previousCount == 0 && olderMaximum > 0 &&
                    currentCount <= olderMaximum) continue;
                filtered.Add(candidate);
            }
            return filtered;
        }

        private static int CountSimilarChatLines(List<string> lines, string target)
        {
            if (lines == null || lines.Count == 0) return 0;
            int count = 0;
            foreach (string line in lines) if (SameChatLine(line, target)) count++;
            return count;
        }

        private static string ChatIdentity(string value)
        {
            string identity = Regex.Replace((value ?? "").ToLowerInvariant(), @"[^a-z0-9\u3400-\u9fff]+", "");
            return identity.Replace('0', 'o').Replace('1', 'l');
        }

        private bool IsRecentlyQueuedChatSource(string line)
        {
            DateTime cutoff = DateTime.Now - TimeSpan.FromSeconds(8);
            while (recentlyQueuedChatSources.Count > 0 &&
                recentlyQueuedChatSources.Peek().Value < cutoff)
                recentlyQueuedChatSources.Dequeue();
            foreach (KeyValuePair<string, DateTime> recent in recentlyQueuedChatSources)
                if (IsSameTranslationSource(recent.Key, line)) return true;
            return false;
        }

        private void RememberQueuedChatSource(string line)
        {
            recentlyQueuedChatSources.Enqueue(
                new KeyValuePair<string, DateTime>(line, DateTime.Now));
            while (recentlyQueuedChatSources.Count > 32) recentlyQueuedChatSources.Dequeue();
        }

        internal static bool IsSameTranslationSource(string left, string right)
        {
            string leftSpeaker, leftMessage, rightSpeaker, rightMessage;
            SplitChatIdentityParts(left, out leftSpeaker, out leftMessage);
            SplitChatIdentityParts(right, out rightSpeaker, out rightMessage);
            if (leftSpeaker.Length > 0 && rightSpeaker.Length > 0 && leftSpeaker != rightSpeaker) return false;
            string a = ChatIdentity(NormalizeCommonChatOcr(leftMessage));
            string b = ChatIdentity(NormalizeCommonChatOcr(rightMessage));
            if (a == b) return true;
            int longest = Math.Max(a.Length, b.Length), shortest = Math.Min(a.Length, b.Length);
            if (shortest < 8 || longest - shortest > Math.Max(5, longest / 2)) return false;
            int limit = Math.Max(3, longest / 4);
            if (EditDistanceWithin(a, b, limit)) return true;
            int sharedPrefix = 0;
            while (sharedPrefix < shortest && a[sharedPrefix] == b[sharedPrefix]) sharedPrefix++;
            return sharedPrefix >= Math.Min(9, shortest);
        }

        private static void SplitChatIdentityParts(string value, out string speaker, out string message)
        {
            string line = value ?? "";
            int separator = line.IndexOf(':');
            int chineseSeparator = line.IndexOf('：');
            if (separator < 0 || (chineseSeparator >= 0 && chineseSeparator < separator)) separator = chineseSeparator;
            if (separator > 0)
            {
                speaker = ChatIdentity(line.Substring(0, separator));
                message = line.Substring(separator + 1);
            }
            else { speaker = ""; message = line; }
        }

        private static bool SameChatLine(string left, string right)
        {
            string a = ChatIdentity(left), b = ChatIdentity(right);
            if (a == b) return true;
            int longest = Math.Max(a.Length, b.Length);
            if (longest < 8 || Math.Abs(a.Length - b.Length) > Math.Max(2, longest / 10)) return false;
            return EditDistanceWithin(a, b, Math.Max(2, longest / 10));
        }

        private static bool EditDistanceWithin(string left, string right, int limit)
        {
            if (Math.Abs(left.Length - right.Length) > limit) return false;
            int[] previous = new int[right.Length + 1], current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i; int rowMinimum = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                    rowMinimum = Math.Min(rowMinimum, current[j]);
                }
                if (rowMinimum > limit) return false;
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length] <= limit;
        }

        internal static List<string> ParseChatLines(string ocrText)
        {
            List<string> output = new List<string>();
            if (String.IsNullOrWhiteSpace(ocrText)) return output;
            string[] physicalLines = ocrText.Replace("\r", "").Split('\n');
            // SHOT/SHOUT and similar message words must never be mistaken for the
            // SHO±9 channel badge. A bare O/0 badge is accepted only with a colon.
            Regex speaker = new Regex(@"(?<![A-Za-z0-9_])([A-Za-z][A-Za-z0-9_]{2,23}?)(?:(?:CH[O0]?\d+|SH[O0](?=[0-9@±])[A-Z0-9@±]*)\s*[:：•·]?|\s+(?:CH(?:O|0)?\s*\d+|SH[O0](?=[0-9@±])[A-Z0-9@±]*)\s*[:：•·]?|\s+[0-9O@±]{1,4}\s*[:：•·]|\s*[:：•·])\s*", RegexOptions.IgnoreCase);
            foreach (string sourceLine in physicalLines)
            {
                string line = Regex.Replace(sourceLine.Trim(), @"\s+", " ");
                if (line.Length < 2) continue;
                if (IsStructuredUiNoise(line)) continue;
                string presence;
                if (TryParseChatPresence(line, out presence))
                {
                    output.Add(presence);
                    continue;
                }
                line = RepairChatSpeakerMarker(line);
                int noticeAt = line.IndexOf("[Notice]", StringComparison.OrdinalIgnoreCase);
                if (noticeAt < 0 && Regex.IsMatch(line, @"^\[[^\]]{2,12}\]\s*Money\s+lost\s+through\s+cash\s+transactions", RegexOptions.IgnoreCase))
                    noticeAt = line.IndexOf(']') + 1;
                string notice = "";
                if (noticeAt >= 0)
                {
                    int noticeTextAt = line.IndexOf(']', noticeAt);
                    if (noticeTextAt < noticeAt) noticeTextAt = noticeAt + 7;
                    notice = line.Substring(Math.Min(line.Length, noticeTextAt + 1)).Trim(' ', '-', '>', '|');
                    line = line.Substring(0, noticeAt).Trim();
                }
                MatchCollection rawMatches = speaker.Matches(line);
                List<Match> matches = new List<Match>();
                foreach (Match candidate in rawMatches)
                {
                    string prefix = line.Substring(0, candidate.Index);
                    bool atStart = Regex.IsMatch(prefix, @"^\s*[.'`\""|_\-•·]*\s*$") ||
                        Regex.IsMatch(prefix, @"^\s*[Il1|]{0,3}\s*\d[\d.:)\s]*$", RegexOptions.IgnoreCase);
                    bool hasChannelBadge = Regex.IsMatch(candidate.Value,
                        @"(?:CH[O0]?\s*\d+|SH[O0][A-Z0-9@±]*|\s[0-9O@±]{1,4}\s*[:：•·])",
                        RegexOptions.IgnoreCase);
                    // Embedded bare "word:" fragments belong to the message itself. Only a
                    // real channel badge may start a second message on a flattened OCR row.
                    if (atStart || hasChannelBadge) matches.Add(candidate);
                }
                if (matches.Count == 0)
                {
                    // Windows OCR sometimes puts a wrapped tail (for example RUSH/FJ?)
                    // on the next physical line. Require a long preceding message so panel
                    // labels and isolated HUD fragments cannot leak into AI translation.
                    if (output.Count > 0 && IsLikelyWrappedChatTail(output[output.Count - 1], line))
                        output[output.Count - 1] = output[output.Count - 1] + " " + line;
                }
                else for (int i = 0; i < matches.Count; i++)
                {
                    Match marker = matches[i];
                    string name = marker.Groups[1].Value;
                    // Channel badges are frequently glued to the name by OCR (SHO±9/CHO1).
                    name = Regex.Replace(name, @"(?:CH[O0]?\d+|SH[O0][A-Z0-9@±]*)$", "", RegexOptions.IgnoreCase);
                    if (name.Length < 3) continue;
                    int messageStart = marker.Index + marker.Length;
                    int messageEnd = i + 1 < matches.Count ? matches[i + 1].Index : line.Length;
                    string message = line.Substring(messageStart, messageEnd - messageStart).Trim(' ', '-', '>', '|');
                    if (message.Length < 2) continue;
                    output.Add(name + ": " + message);
                }
                if (IsCompleteChatNotice(notice)) output.Add("系统公告: " + notice);
            }
            return output;
        }

        private static bool IsCompleteChatNotice(string notice)
        {
            if (String.IsNullOrWhiteSpace(notice) || notice.Length < 2) return false;
            if (Regex.IsMatch(notice, @"^Money\s+lost(?:\s+through\s+cash\s+transactions)?\s*$",
                RegexOptions.IgnoreCase)) return false;
            return true;
        }

        private static string RepairChatSpeakerMarker(string value)
        {
            string line = value ?? "";
            // MapleStory renders a small channel icon after a name; at video scale it is
            // often read as (). Quotes around the first glyph are another common wobble.
            line = Regex.Replace(line,
                @"^['`""]?([A-Za-z])['`""]?([A-Za-z0-9_]{2,22})\s*\(\)\s*[:：]?\s*",
                "$1$2: ", RegexOptions.IgnoreCase);
            // Character names cannot contain spaces. Repair one OCR-inserted space only
            // when a short O/0 channel badge and colon prove that this is a chat prefix.
            line = Regex.Replace(line,
                @"^([A-Za-z][A-Za-z0-9_]{1,11})\s+([A-Za-z][A-Za-z0-9_]{1,11})\s+([0-9O@±]{1,4})\s*[:：•·]",
                "$1$2 $3:", RegexOptions.IgnoreCase);
            return line;
        }

        private static bool TryParseChatPresence(string value, out string parsed)
        {
            parsed = "";
            string line = (value ?? "").TrimEnd(' ', ':');
            Match tagged = Regex.Match(line,
                @"^[\[\(_|]?\s*(?<kind>friend|friehd|lhiend|buddy|guild|party|alliance)\s*[\]\)l_]*\s+(?<name>[A-Za-z][A-Za-z0-9_]{2,23})\s+has.{0,14}\s+(?<state>in|out)\.?$",
                RegexOptions.IgnoreCase);
            if (!tagged.Success) return false;
            string kind = tagged.Groups["kind"].Value.ToLowerInvariant();
            if (kind == "friehd" || kind == "lhiend") kind = "friend";
            string label = Char.ToUpperInvariant(kind[0]) + kind.Substring(1);
            parsed = tagged.Groups["name"].Value + ": [" + label + "] has logged " +
                tagged.Groups["state"].Value.ToLowerInvariant() + ".";
            return true;
        }

        internal static bool IsStructuredUiNoise(string value)
        {
            string line = Regex.Replace((value ?? "").TrimStart(' ', '•', '·', '-', '*'), @"\s+", " ");
            if (line.Length == 0) return true;
            return Regex.IsMatch(line,
                @"^(?:(?:weapon|magic|physical)\s+)?(?:attack|def(?:ense)?|accuracy|avoidability|speed|jump|enhancements?|remaining\s+enhancements?|type|level|job|name|fame|max\s*hp|max\s*mp|str|dex|int|luk|ability\s+points?|skill\s+points?)\s*\.?\s*[:：]",
                RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line,
                @"^(?:req(?:uired)?\s+)?(?:lev(?:el)?|str|dex|int|luk|fam)\s*[:：]?\s*[+\-]?\d+\b",
                RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"^(?:beginner|warrior|magician|bowman|thief)(?:\s+(?:warrior|magician|bowman|thief))*$",
                    RegexOptions.IgnoreCase);
        }

        private static bool IsLikelyWrappedChatTail(string previous, string value)
        {
            string line = (value ?? "").Trim();
            if (line.Length < 2 || line.Length > 100 || previous == null || previous.Length < 55 ||
                line.StartsWith("[") || IsStructuredUiNoise(line)) return false;
            if (Regex.IsMatch(line, @"^[A-Za-z][A-Za-z0-9_]{2,23}\s*[:：]")) return false;
            int textCharacters = 0;
            foreach (char item in line) if (Char.IsLetterOrDigit(item)) textCharacters++;
            return textCharacters >= Math.Max(3, line.Length / 3);
        }

        private string BuildGlossary(string text)
        {
            StringBuilder result = new StringBuilder(); int count = 0;
            foreach (KeyValuePair<string, string> entry in glossaryEntries)
            {
                if (count >= 16) break;
                if (!ContainsGlossaryTerm(text, entry.Key) && !ContainsGlossaryTerm(text, entry.Value)) continue;
                result.Append(entry.Key).Append(" = ").Append(entry.Value).AppendLine(); count++;
            }
            return result.ToString();
        }

        private static bool ContainsGlossaryTerm(string text, string term)
        {
            if (String.IsNullOrWhiteSpace(text) || String.IsNullOrWhiteSpace(term)) return false;
            if (Regex.IsMatch(term, @"^[A-Za-z0-9_ ]+$"))
                return Regex.IsMatch(text, @"(?<![A-Za-z0-9_])" + Regex.Escape(term) + @"(?![A-Za-z0-9_])", RegexOptions.IgnoreCase);
            return text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryExactGlossaryTranslation(string text, out string translation)
        {
            string key = NormalizeChatPhrase(text);
            foreach (KeyValuePair<string, string> entry in glossaryEntries)
            {
                if (contextOnlyGlossaryKeys.Contains(entry.Key)) continue;
                if (NormalizeChatPhrase(entry.Key) != key) continue;
                translation = entry.Value;
                return true;
            }
            translation = "";
            return false;
        }

        private static bool TryKnownChatIntentTranslation(string text, out string translation)
        {
            string line = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
            bool asksHow = Regex.IsMatch(line, @"^how\b", RegexOptions.IgnoreCase);
            bool mentionsWhisper = Regex.IsMatch(line, @"\bwhispers?\b", RegexOptions.IgnoreCase);
            bool asksToReply = Regex.IsMatch(line,
                @"\b(?:reply|respond)\b.*\bwhispers?\b|\bwhispers?\b.*\b(?:reply|respond)\b|\bwhispers?\s+back\b",
                RegexOptions.IgnoreCase);
            if (asksHow && mentionsWhisper && asksToReply)
            {
                translation = "怎么回复别人的悄悄话？";
                return true;
            }
            if (Regex.IsMatch(line, @"^I['’]?m going to\s+(?:go\s+)?check(?:\s+it)?(?:\s+now)?[.!?]*$",
                RegexOptions.IgnoreCase))
            {
                translation = "我现在去看看。";
                return true;
            }
            Match partyRecruit = Regex.Match(line,
                @"^need\s+(\d+)\s+more\s+for\s+KPQ[.!?]*$",
                RegexOptions.IgnoreCase);
            if (partyRecruit.Success)
            {
                translation = "废弃都市组队任务还缺" + partyRecruit.Groups[1].Value + "人。";
                return true;
            }
            if (Regex.IsMatch(line, @"^anyone\s+(?:doing|for)\s+the\s+GM\s+event\s+(?:rn|now)[?!.]*$",
                RegexOptions.IgnoreCase))
            {
                translation = "现在有人做GM活动吗？";
                return true;
            }
            if (Regex.IsMatch(line, @"^I['’]?m\s+farming\s+(?:them|em)\s+(?:rn|now)[.!?]*$",
                RegexOptions.IgnoreCase))
            {
                translation = "我现在在刷这些。";
                return true;
            }
            if (Regex.IsMatch(line, @"^(?:selling|S\s*>)\s+clean\s+gear\s*,?\s*(?:offer|obo)[.!?]*$",
                RegexOptions.IgnoreCase))
            {
                translation = "出售未砸卷装备，请报价。";
                return true;
            }
            if (Regex.IsMatch(line, @"\bOmok\b.*\b(?:players?\s+)?scared\b|\bscared\b.*\bOmok\b",
                RegexOptions.IgnoreCase))
            {
                translation = "怎么，玩五子棋的都怕了？";
                return true;
            }
            if (Regex.IsMatch(line, @"^S\s*>\s*Kumbi\b", RegexOptions.IgnoreCase))
            {
                Match price = Regex.Match(line, @"\b(\d+)\s*k\b", RegexOptions.IgnoreCase);
                int priceThousands;
                string priceText = "";
                if (price.Success && Int32.TryParse(price.Groups[1].Value, out priceThousands))
                    priceText = "，" + (priceThousands % 10 == 0
                        ? priceThousands / 10 + "万" : priceThousands + "千");
                translation = "出售雪花镖" + priceText +
                    (Regex.IsMatch(line, @"\b(?:best offer|obo)\b", RegexOptions.IgnoreCase) ? "，价高者得" : "") + "！";
                return true;
            }
            Match scrollSale = Regex.Match(line,
                @"^S\s*>.*?(\d+%)\s+Ear LUK.*?(\d+%)\s+OA STR\b",
                RegexOptions.IgnoreCase);
            if (scrollSale.Success)
            {
                translation = "出售" + scrollSale.Groups[1].Value + "耳环运气卷、" +
                    scrollSale.Groups[2].Value + "套服力量卷，请报价。";
                return true;
            }
            if (Regex.IsMatch(line, @"^B\s*>.*\bClaw\s+60%", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(line, @"\b7/7\b", RegexOptions.IgnoreCase))
            {
                translation = "收一张60%拳套攻击卷，求好运，7张全成就差这一张了。";
                return true;
            }
            translation = "";
            return false;
        }

        private static string NormalizeChatPhrase(string value)
        {
            // 'R>PQ' means recruiting for a party quest, while plain 'RPQ' is ambiguous.
            return Regex.Replace((value ?? "").ToLowerInvariant(), @"[^a-z0-9\u3400-\u9fff>]+", "");
        }

        private static string NormalizeCommonChatOcr(string value)
        {
            string result = value ?? "";
            result = Regex.Replace(result, @"\bdarksight\b", "Dark Sight", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bpowerstrike\b", "Power Strike", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\baswell\b", "as well", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bcmon\b", "come on", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bive\b", "I've", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"^\s*(?:ima|irna|lma)\s+go\b", "I'm going to", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bchec\*(?=\s|$)", "check", RegexOptions.IgnoreCase);
            bool trade = Regex.IsMatch(result, @"^\s*(?:[BSWT]\s*>|WTB\b|WTS\b|WTT\b)", RegexOptions.IgnoreCase);
            if (trade)
            {
                result = Regex.Replace(result, @"\b6[0OÖÜ][0OÖÜ]?/0\b", "60%", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"\b1[0O][0O]/0\b", "10%", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"\bEar\s+(?:LI-JK|LUIK|LUKS|I-UK|L[UJI]?K)\b", "Ear LUK", RegexOptions.IgnoreCase);
            }
            return result.Trim();
        }

        internal static bool IsPlausibleChatTranslation(string source, string translation)
        {
            string input = (source ?? "").Trim();
            string output = (translation ?? "").Trim();
            if (input.Length == 0 || output.Length == 0) return false;
            if (output.IndexOf('\r') >= 0 || output.IndexOf('\n') >= 0) return false;
            if (OfflineAiClient.LooksLikeInstructionLeak(output)) return false;
            int maximum = Math.Max(32, input.Length * 3 + 12);
            if (output.Length > maximum) return false;
            if (Regex.Matches(input, "[A-Za-z]").Count >= 3 &&
                Regex.Matches(output, "[\\u3400-\\u9fff]").Count == 0) return false;
            // This is a common hallucination for opaque player names and OCR fragments.
            if (Regex.IsMatch(input, @"^[A-Za-z][A-Za-z0-9_]{2,23}$") &&
                Regex.IsMatch(output, @"^玩家\s*\d+$")) return false;
            return true;
        }

        internal static string DetectChatSourceLanguage(string value)
        {
            int latin = 0, cjk = 0, kana = 0, hangul = 0;
            foreach (char item in value ?? "")
            {
                if ((item >= 'A' && item <= 'Z') || (item >= 'a' && item <= 'z')) latin++;
                else if (item >= '\u4e00' && item <= '\u9fff') cjk++;
                else if (item >= '\u3040' && item <= '\u30ff') kana++;
                else if (item >= '\uac00' && item <= '\ud7af') hangul++;
            }
            if (latin >= 3 && latin >= cjk + kana + hangul) return "英语";
            if (kana > 0) return "日语";
            if (hangul > 0) return "韩语";
            if (cjk > 0) return "中文";
            return "自动识别";
        }

        internal static bool IsUsefulChatLine(string value)
        {
            string message = value ?? "";
            int separator = message.IndexOf(':');
            int chineseSeparator = message.IndexOf('：');
            if (separator < 0 || (chineseSeparator >= 0 && chineseSeparator < separator))
                separator = chineseSeparator;
            if (separator >= 0 && separator + 1 < message.Length)
                message = message.Substring(separator + 1);
            string compact = Regex.Replace(message.ToLowerInvariant(), @"[^a-z0-9\u3400-\u9fff]+", "");
            if (compact == "gg" || compact == "ty" || compact == "hi" || compact == "yo" ||
                compact == "ok" || compact == "lf") return true;
            return compact.Length >= 3;
        }

        private void LoadGlossary()
        {
            glossaryEntries.Clear();
            contextOnlyGlossaryKeys.Clear();
            knowledge = null;
            if (!File.Exists(dictionaryPath)) return;
            Dictionary<string, HashSet<string>> valuesByEnglish =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            List<KeyValuePair<string, string>> loaded = new List<KeyValuePair<string, string>>();
            foreach (string raw in File.ReadLines(dictionaryPath, Encoding.UTF8))
            {
                if (raw.StartsWith("#")) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 2 || parts[0].Length < 2 || parts[1].Length == 0) continue;
                string english = parts[0].Trim(), chinese = parts[1].Trim();
                string category = parts.Length > 2 ? parts[2].Trim() : "";
                if (category.StartsWith("怀旧服-聊天多义缩写", StringComparison.Ordinal))
                    contextOnlyGlossaryKeys.Add(english);
                loaded.Add(new KeyValuePair<string, string>(english, chinese));
                HashSet<string> values;
                if (!valuesByEnglish.TryGetValue(english, out values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal); valuesByEnglish.Add(english, values);
                }
                values.Add(chinese);
            }
            foreach (KeyValuePair<string, string> entry in loaded)
                if (valuesByEnglish[entry.Key].Count == 1) glossaryEntries.Add(entry);
            glossaryEntries.Sort(delegate(KeyValuePair<string, string> left, KeyValuePair<string, string> right) {
                return right.Key.Length.CompareTo(left.Key.Length);
            });
        }

        private async Task InitializeKnowledgeInBackgroundAsync(bool force)
        {
            if (!ai.IsInstalled) return;
            while (knowledgeInitializationBusy) await Task.Delay(40);
            if (!force && knowledge != null) return;
            knowledgeInitializationBusy = true;
            status.Text = force ? "正在后台同步AI词库…" : "词库已就绪｜后台检查AI知识…";
            try
            {
                KnowledgeInitializationResult result = await Task.Factory.StartNew(delegate {
                    return MapleKnowledgeInitializer.Initialize(ai.AiRoot, dictionaryPath);
                });
                knowledge = result;
                status.Text = (result.Changed ? "AI已切换到新词库 " : "AI词库已就绪 ") + result.Entries + "条";
            }
            catch (Exception ex) { status.Text = "AI知识检查失败：" + ex.Message; }
            finally { knowledgeInitializationBusy = false; }
        }

        internal KnowledgeInitializationResult SyncKnowledge()
        {
            LoadGlossary();
            if (ai.IsInstalled)
            {
                try { knowledge = MapleKnowledgeInitializer.Initialize(ai.AiRoot, dictionaryPath); }
                catch { knowledge = null; }
            }
            RefreshAiStatus();
            return knowledge;
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
            Match match = Regex.Match(remaining, @"^(\s*(?:\[[^\]]{1,20}\]\s*)?(?:<[^>]{1,48}>|[^:：]{1,48})\s*[:：]\s*)(.+)$");
            if (!match.Success)
            {
                match = Regex.Match(remaining, @"^(\s*<([^>]{1,48})>\s*)(.+)$");
                if (!match.Success) return;
            }
            string speakerPrefix = match.Groups[1].Value;
            prefix += speakerPrefix;
            message = match.Groups[match.Groups.Count - 1].Value;
            string name = Regex.Replace(speakerPrefix, @"^\s*(?:\[[^\]]+\]\s*)?", "");
            name = name.Trim().TrimEnd(':', '：').Trim().Trim('<', '>').Trim();
            if (name.Length >= 3 && name.Length <= 48) protectedPlayerNames.Add(name);
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

        private void AppendTranslation(string original, string translation, ChatVisualStyle visualStyle)
        {
            if (visualStyle == null) visualStyle = ChatVisualStyle.Default;
            if (output.TextLength > 24000)
            {
                output.Select(0, Math.Min(8000, output.TextLength));
                output.SelectedText = "";
            }
            output.SelectionStart = output.TextLength;
            output.SelectionColor = Color.FromArgb(132, 142, 154);
            output.SelectionFont = outputOriginalFont;
            output.AppendText("原文  " + original.Trim() + Environment.NewLine);
            output.SelectionStart = output.TextLength;
            output.SelectionColor = Color.FromArgb(255, 183, 77);
            output.SelectionFont = outputTranslationFont;
            output.AppendText("中文  ");
            output.SelectionColor = visualStyle.ForeColor;
            output.SelectionBackColor = visualStyle.HasBackground
                ? visualStyle.BackColor : output.BackColor;
            output.SelectionFont = outputTranslationFont;
            output.AppendText(visualStyle.HasBackground
                ? "  " + translation.Trim() + "  " + Environment.NewLine
                : translation.Trim() + Environment.NewLine);
            output.SelectionBackColor = output.BackColor;
            output.SelectionColor = Color.FromArgb(62, 70, 82);
            output.SelectionFont = outputOriginalFont;
            output.AppendText("────────────────────────" + Environment.NewLine);
            output.SelectionStart = output.TextLength; output.ScrollToCaret();
            if (floatingWindowEnabled && live && screenshotCaptureSuspendCount == 0)
            {
                if (floatingWindow == null || floatingWindow.IsDisposed)
                    floatingWindow = new AiTranslationWindowForm(overlay);
                floatingWindow.AppendTranslation(translation, visualStyle);
                floatingWindow.ShowPassiveIfAllowed();
            }
        }

        public void StopService()
        {
            StopLiveTranslation();
            releaseTimer.Stop(); ai.Stop();
            if (floatingWindow != null && !floatingWindow.IsDisposed)
                floatingWindow.ClosePermanently();
        }

        private void RememberTranslation(string key, string translation)
        {
            if (String.IsNullOrEmpty(key) || String.IsNullOrWhiteSpace(translation) ||
                translationCache.ContainsKey(key)) return;
            translationCache[key] = translation;
            translationCacheOrder.Enqueue(key);
            while (translationCacheOrder.Count > 256)
                translationCache.Remove(translationCacheOrder.Dequeue());
        }
    }

    internal sealed class AiInstallForm : Form
    {
        private readonly string aiRoot;
        private readonly string dictionaryPath;
        private readonly Label progress = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Button install17 = new Button();
        private readonly Button install4 = new Button();
        private readonly Button install8 = new Button();
        private readonly Button update = new Button();

        private sealed class RemoteFileInfo
        {
            public long Length;
            public string ETag;
        }

        public AiInstallForm(string aiRoot, string dictionaryPath)
        {
            this.aiRoot = aiRoot;
            this.dictionaryPath = dictionaryPath;
            Text = "安装离线AI模型"; StartPosition = FormStartPosition.CenterParent;
            Size = new Size(690, 390); Font = new Font("Microsoft YaHei UI", 9.0f);
            TextBox info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = "模型安装位置（就在程序旁边）：\r\n" + aiRoot + "\r\n\r\n程序自动识别 AMD、NVIDIA、Intel Vulkan 显卡；显卡不可用时自动回退CPU。\r\n老电脑推荐1.7B约1.1GB；4B约2.5GB；8B约5GB。下载后永久离线免费。\r\n\r\n下载完成后会自动用资料站整理内容和本地词库完成首次知识初始化；这是轻量检索知识库，不修改模型权重。\r\n如果官方站下载不畅，也可以从QQ群取得模型包并解压到上面的目录。" };
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 145 };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, WrapContents = true };
            install17.Text = "安装超轻1.7B（老电脑推荐）"; install17.AutoSize = true;
            install17.Click += async delegate { await InstallAsync("1.7B"); };
            install4.Text = "一键安装轻量4B"; install4.AutoSize = true;
            install4.Click += async delegate { await InstallAsync("4B"); };
            install8.Text = "一键安装高质量8B"; install8.AutoSize = true;
            install8.Click += async delegate { await InstallAsync("8B"); };
            update.Text = "检查并更新现有模型"; update.AutoSize = true;
            update.Click += async delegate { await UpdateExistingAsync(); };
            Button folder = new Button { Text = "打开模型文件夹", AutoSize = true };
            folder.Click += delegate { Directory.CreateDirectory(aiRoot); Process.Start("explorer.exe", aiRoot); };
            buttons.Controls.Add(install17); buttons.Controls.Add(install4); buttons.Controls.Add(install8); buttons.Controls.Add(update); buttons.Controls.Add(folder);
            progressBar.Location = new Point(8, 92); progressBar.Size = new Size(430, 20);
            progress.Location = new Point(447, 94); progress.AutoSize = true; progress.Text = "尚未开始";
            bottom.Controls.Add(buttons); bottom.Controls.Add(progressBar); bottom.Controls.Add(progress);
            Controls.Add(info); Controls.Add(bottom);
        }

        private async Task InstallAsync(string size)
        {
            SetButtons(false);
            try
            {
                Directory.CreateDirectory(aiRoot);
                await InstallRuntimeAsync(false);

                string fileName = "Qwen3-" + size + "-Q4_K_M.gguf";
                string modelUrl = await Task.Factory.StartNew(delegate { return FindModelUrl(size, fileName); });
                await InstallModelAsync(modelUrl, Path.Combine(aiRoot, fileName), "下载" + size + "模型", false);
                File.WriteAllText(Path.Combine(aiRoot, "selected-model.txt"), fileName, new UTF8Encoding(false));
                KnowledgeInitializationResult knowledge = await InitializeKnowledgeAsync();
                progressBar.Style = ProgressBarStyle.Continuous; progressBar.Value = 100; progress.Text = "安装及知识初始化完成";
                MessageBox.Show("离线AI模型已安装，并已使用资料站整理内容和本地词库完成首次知识初始化（" +
                    knowledge.Entries + "条、" + knowledge.Categories + "类）。\n\n关闭本窗口后点击AI翻译，首次载入模型可能需要几十秒。", "安装完成");
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
                if (models.Length == 0) { MessageBox.Show("没有发现已安装模型，请先选择1.7B、4B或8B安装。", "检查更新"); return; }
                bool changed = false;
                string runtimeWarning = null;
                try { changed = await InstallRuntimeAsync(true); }
                catch (Exception ex)
                {
                    runtimeWarning = ex.Message;
                    progress.Text = "运行库沿用当前版本，继续检查模型…";
                }
                foreach (string modelPath in models)
                {
                    string fileName = Path.GetFileName(modelPath);
                    string size = fileName.IndexOf("1.7B", StringComparison.OrdinalIgnoreCase) >= 0 ? "1.7B" :
                        (fileName.IndexOf("8B", StringComparison.OrdinalIgnoreCase) >= 0 ? "8B" : "4B");
                    string url = await Task.Factory.StartNew(delegate { return FindModelUrl(size, fileName); });
                    changed = await InstallModelAsync(url, modelPath, "更新" + size + "模型", true) || changed;
                }
                KnowledgeInitializationResult knowledge = await InitializeKnowledgeAsync();
                progressBar.Style = ProgressBarStyle.Continuous; progressBar.Value = 100;
                progress.Text = runtimeWarning == null
                    ? (changed ? "更新及知识初始化完成" : "模型与知识库已是最新版")
                    : "模型与知识库检查完成；运行库沿用当前版本";
                string summary = changed ? "模型和运行库检查完成，已安装可用更新。" : "当前模型和运行库已经是最新版。";
                if (runtimeWarning != null)
                    summary = "模型与知识库已正常检查；显卡运行库暂时无法联网核对，已继续使用当前可用版本。\n原因：" + runtimeWarning;
                MessageBox.Show(summary + "\n知识初始化：" + knowledge.Entries + "条、" + knowledge.Categories + "类。", "检查更新");
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
            install17.Enabled = enabled; install4.Enabled = enabled; install8.Enabled = enabled; update.Enabled = enabled;
        }

        private Task<KnowledgeInitializationResult> InitializeKnowledgeAsync()
        {
            progressBar.Style = ProgressBarStyle.Marquee; progress.Text = "首次知识初始化…";
            return Task.Factory.StartNew(delegate { return MapleKnowledgeInitializer.Initialize(aiRoot, dictionaryPath); });
        }

        private async Task<bool> InstallRuntimeAsync(bool updateOnly)
        {
            progressBar.Style = ProgressBarStyle.Marquee; progress.Text = "检查官方运行库…";
            string marker = Path.Combine(aiRoot, "runtime.url.txt");
            string server = Directory.Exists(aiRoot) ? FindFile(aiRoot, "llama-server.exe") : null;
            // The normal release includes a ready-to-use Vulkan runtime, so first
            // installation does not depend on GitHub being reachable in China.
            if (!updateOnly && server != null) { progress.Text = "已使用随程序提供的显卡运行库"; return false; }
            string runtimeUrl = await Task.Factory.StartNew(delegate { return FindRuntimeUrl(); });
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
            return WithNetworkRetry(delegate {
                HttpWebRequest request = CreateRequest(url, 30000);
                request.Method = "HEAD";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return new RemoteFileInfo { Length = response.ContentLength, ETag = (response.Headers["ETag"] ?? "").Trim() };
            });
        }

        private static string FindModelUrl(string size, string fileName)
        {
            string repo = "Qwen/Qwen3-" + size + "-GGUF";
            string[] urls = new string[] {
                "https://modelscope.cn/models/" + repo + "/resolve/master/" + fileName,
                "https://hf-mirror.com/" + repo + "/resolve/main/" + fileName + "?download=true",
                "https://huggingface.co/" + repo + "/resolve/main/" + fileName + "?download=true"
            };
            Exception last = null;
            foreach (string url in urls)
            {
                try { GetRemoteInfo(url); return url; }
                catch (Exception ex) { last = ex; }
            }
            throw new InvalidOperationException("国内模型源和海外备用源均无法连接。无需强制开启VPN；请先确认浏览器能打开 ModelScope（魔搭社区），或从QQ群取得离线模型放入‘模型’文件夹。", last);
        }

        private static string FindRuntimeUrl()
        {
            Exception latestError = null;
            try
            {
                string json = ReadTextUrl("https://api.github.com/repos/ggml-org/llama.cpp/releases/latest");
                string direct = FindRuntimeAssetUrl(json);
                if (!String.IsNullOrEmpty(direct)) return direct;

                string tagUrl = FindNamedAssetUrl(json, "nightly-tag.txt");
                if (!String.IsNullOrEmpty(tagUrl))
                    return BuildRuntimeUrlFromNightlyTag(ReadTextUrl(tagUrl));
            }
            catch (Exception ex) { latestError = ex; }

            try
            {
                // The stable release can intentionally contain only nightly-tag.txt.
                // This download endpoint avoids GitHub API rate limits and still points
                // to the official build selected by the llama.cpp maintainers.
                string tag = ReadTextUrl("https://github.com/ggml-org/llama.cpp/releases/latest/download/nightly-tag.txt");
                return BuildRuntimeUrlFromNightlyTag(tag);
            }
            catch (Exception fallbackError)
            {
                throw new InvalidOperationException(
                    "无法从 llama.cpp 官方稳定版或官方 nightly 索引找到 Windows Vulkan 运行包；当前运行库未被改动。",
                    latestError ?? fallbackError);
            }
        }

        private static string ReadTextUrl(string url)
        {
            return WithNetworkRetry(delegate {
                HttpWebRequest request = CreateRequest(url, 30000);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            });
        }

        private static string FindRuntimeAssetUrl(string json)
        {
            return FindNamedAssetUrl(json, "bin-win-vulkan-x64.zip", true);
        }

        private static string FindNamedAssetUrl(string json, string expectedName)
        {
            return FindNamedAssetUrl(json, expectedName, false);
        }

        private static string FindNamedAssetUrl(string json, string expectedName, bool contains)
        {
            Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object assetsValue;
            if (root == null || !root.TryGetValue("assets", out assetsValue)) return null;
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
                object nameValue;
                object urlValue;
                if (!asset.TryGetValue("name", out nameValue) || !asset.TryGetValue("browser_download_url", out urlValue)) continue;
                string name = Convert.ToString(nameValue);
                bool match = contains
                    ? name.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0
                    : String.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase);
                if (match) return Convert.ToString(urlValue);
            }
            return null;
        }

        private static string BuildRuntimeUrlFromNightlyTag(string tagText)
        {
            string tag = (tagText ?? "").Trim();
            if (!Regex.IsMatch(tag, "^b[0-9]+$"))
                throw new InvalidDataException("官方 nightly 版本标记格式无效");
            return "https://github.com/ggml-org/llama.cpp/releases/download/" + tag +
                "/llama-" + tag + "-bin-win-vulkan-x64.zip";
        }

        private async Task DownloadAsync(string url, string destination, string stage)
        {
            await Task.Factory.StartNew(delegate {
                WithNetworkRetry(delegate {
                    long existing = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                    HttpWebRequest request = CreateRequest(url, 60000);
                    request.ReadWriteTimeout = 60000;
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
                    return true;
                });
            });
        }

        private static HttpWebRequest CreateRequest(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "FengYuMu-ModelInstaller/1.4.2";
            request.Timeout = timeout;
            request.KeepAlive = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Proxy = WebRequest.GetSystemWebProxy();
            if (request.Proxy != null) request.Proxy.Credentials = CredentialCache.DefaultCredentials;
            return request;
        }

        private static T WithNetworkRetry<T>(Func<T> action)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { return action(); }
                catch (WebException ex)
                {
                    last = ex;
                    if (attempt < 2) System.Threading.Thread.Sleep(800 * (attempt + 1));
                }
            }
            WebException web = last as WebException;
            if (web != null && web.Status == WebExceptionStatus.SecureChannelFailure)
                throw new InvalidOperationException("无法建立 TLS 1.2 安全连接。请确认 Windows 日期时间正确，并在系统的 Internet 选项→高级中启用‘使用 TLS 1.2’；如果使用代理，请先确认浏览器可以打开 GitHub 和 Hugging Face。", web);
            throw last ?? new InvalidOperationException("网络连接失败");
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
        private readonly ComboBox provider = new ComboBox();
        private readonly TextBox endpoint = new TextBox();
        private readonly TextBox model = new TextBox();
        private readonly TextBox apiKey = new TextBox();
        private readonly Label providerHint = new Label();
        private readonly Label testStatus = new Label();

        public OnlineAiForm()
        {
            Text = "联网AI实时翻译（不用租服务器）"; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(760, 560); Font = new Font("Microsoft YaHei UI", 9.0f);
            OnlineAiSettings value = OnlineAiSettings.Load();
            Label heading = new Label {
                Text = "选一家 → 申请密钥 → 粘贴 → 保存并测试，以后点“开始实时翻译”就行。",
                Location = new Point(24, 18), Size = new Size(710, 28),
                Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(22, 101, 52)
            };
            Label tutorial = new Label {
                Location = new Point(24, 50), Size = new Size(710, 105),
                ForeColor = Color.FromArgb(55, 65, 81),
                Text = "小白教程：\r\n1. 不知道选谁就先用豆包；想先免费试就选智谱。\r\n" +
                    "2. 点“打开申请页面”，按网页提示注册、开通对应模型并新建 API Key。\r\n" +
                    "3. 把“API Key 管理”页给你的整串 Key 复制到下面；不要填 Access Key 或 Secret Key。\r\n" +
                    "不用租服务器，也不用每次打开网页；费用/免费额度由你选的服务商账号结算。"
            };

            Controls.Add(new Label { Text = "AI服务：", Location = new Point(24, 174), AutoSize = true });
            provider.Location = new Point(112, 169); provider.Size = new Size(430, 28);
            provider.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (OnlineAiPreset item in OnlineAiPresets.All) provider.Items.Add(item.Name);
            Button open = new Button { Text = "打开申请页面", Location = new Point(558, 168), Size = new Size(176, 30) };
            open.Click += delegate { OpenApplyPage(); };
            providerHint.Location = new Point(112, 202); providerHint.Size = new Size(622, 38);
            providerHint.ForeColor = Color.FromArgb(75, 85, 99);

            AddField("接口地址：", endpoint, 250);
            AddField("模型名称：", model, 296);
            AddField("API Key：", apiKey, 342);
            apiKey.UseSystemPasswordChar = true;

            Label privacy = new Label {
                Location = new Point(24, 384), Size = new Size(710, 56),
                ForeColor = Color.DimGray,
                Text = "枫语幕只把当前聊天短句、命中的冒险岛词库术语和固定游戏提示词发给你选的服务商。" +
                    "中间分析不会显示，悬浮窗只给最终译文。Key 用 Windows 当前账户加密保存在本机，不写进词库或压缩包。"
            };
            testStatus.Location = new Point(24, 449); testStatus.Size = new Size(710, 44);
            testStatus.ForeColor = Color.FromArgb(37, 99, 235);

            Button offline = new Button { Text = "清除密钥，改用离线", Location = new Point(365, 500), Size = new Size(170, 34) };
            offline.Click += delegate {
                OnlineAiSettings.Clear();
                DialogResult = DialogResult.OK;
                Close();
            };
            Button save = new Button { Text = "保存并测试", Location = new Point(552, 500), Size = new Size(182, 34) };
            save.Click += async delegate { await SaveAndTestAsync(save); };

            provider.SelectedIndexChanged += delegate { ApplySelectedPreset(); };
            string savedProvider = value.Provider;
            if (!provider.Items.Contains(savedProvider)) savedProvider = "自定义兼容接口";
            provider.SelectedItem = savedProvider;
            endpoint.Text = value.Endpoint; model.Text = value.Model; apiKey.Text = value.ApiKey;
            ApplySelectedPresetHintOnly();

            Controls.Add(heading); Controls.Add(tutorial); Controls.Add(provider);
            Controls.Add(open); Controls.Add(providerHint); Controls.Add(privacy);
            Controls.Add(testStatus); Controls.Add(offline); Controls.Add(save);
        }

        private void AddField(string label, TextBox box, int y)
        {
            Controls.Add(new Label { Text = label, Location = new Point(24, y + 5), AutoSize = true });
            box.Location = new Point(112, y); box.Size = new Size(622, 27); Controls.Add(box);
        }

        private void ApplySelectedPreset()
        {
            OnlineAiPreset selected = OnlineAiPresets.Find(Convert.ToString(provider.SelectedItem));
            endpoint.Text = selected.Endpoint;
            model.Text = selected.Model;
            ApplySelectedPresetHintOnly();
        }

        private void ApplySelectedPresetHintOnly()
        {
            providerHint.Text = OnlineAiPresets.Find(Convert.ToString(provider.SelectedItem)).PlainHint;
        }

        private void OpenApplyPage()
        {
            string url = OnlineAiPresets.Find(Convert.ToString(provider.SelectedItem)).ApplyUrl;
            if (url.Length == 0)
            {
                MessageBox.Show("自定义接口没有统一申请页面，请向接口提供方获取地址、模型名和 Key。", "联网AI设置");
                return;
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { MessageBox.Show("浏览器没有成功打开，请检查系统默认浏览器。", "联网AI设置"); }
        }

        private async Task SaveAndTestAsync(Button save)
        {
            OnlineAiSettings settings = new OnlineAiSettings {
                Provider = Convert.ToString(provider.SelectedItem),
                Endpoint = endpoint.Text.Trim(), Model = model.Text.Trim(), ApiKey = apiKey.Text.Trim()
            };
            if (!settings.IsReady)
            {
                MessageBox.Show("还差接口地址、模型名或 API Key。按上面的 1、2、3 步补齐就行。", "联网AI设置");
                return;
            }
            save.Enabled = false; testStatus.Text = "正在试连并翻译一句冒险岛聊天……";
            try
            {
                string result = await OnlineAiClient.TranslateAsync(settings,
                    "How do you whisper back someone?", "简体中文", "whisper = 悄悄话\n");
                if (!OfflineChatForm.IsPlausibleChatTranslation("How do you whisper back someone?", result) ||
                    !Regex.IsMatch(result, "[\\u3400-\\u9fff]"))
                    throw new InvalidOperationException("返回内容不像有效译文");
                settings.Save();
                testStatus.Text = "连接成功，已保存。以后直接开始实时翻译。";
                await Task.Delay(550);
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex)
            {
                testStatus.Text = OnlineAiClient.DescribeFailure(ex, settings);
                testStatus.ForeColor = Color.FromArgb(185, 28, 28);
            }
            finally { save.Enabled = true; }
        }
    }
}
