using System;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal static class OnlineAiProtocol
    {
        internal static string NormalizeEndpoint(string endpoint)
        {
            Uri uri;
            if (!Uri.TryCreate((endpoint ?? "").Trim(), UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                uri.Fragment.Length != 0 || uri.Query.Length != 0)
                throw new ArgumentException("接口须为完整 HTTPS 地址，不能包含账号、查询参数或片段。");
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length == 0 || path == "/v1") path += "/chat/completions";
            if (!path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请填写完整接口：以 /chat/completions 或 /responses 结尾。");
            return uri.GetLeftPart(UriPartial.Authority) + path;
        }

        internal static Uri ModelsEndpoint(string endpoint)
        {
            string normalized = NormalizeEndpoint(endpoint);
            string suffix = normalized.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
                ? "/responses" : "/chat/completions";
            return new Uri(normalized.Substring(0, normalized.Length - suffix.Length) + "/models");
        }

        internal static string SafeDiagnosticToken(string value, string key)
        {
            if (String.IsNullOrEmpty(value)) return "";
            if (!Regex.IsMatch(value, @"^[A-Za-z0-9_.:-]{1,128}$") ||
                value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(key) && value.IndexOf(key, StringComparison.Ordinal) >= 0)
                return "redacted";
            return value;
        }

        internal static string SafeModel(string model, string key)
        {
            if (String.IsNullOrEmpty(model) ||
                !Regex.IsMatch(model, @"^[A-Za-z0-9._:/-]{1,128}$") ||
                model.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(key) && model.IndexOf(key, StringComparison.Ordinal) >= 0)
                return "redacted";
            return model;
        }
    }
}
