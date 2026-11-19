using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace MapleOverlay
{
    internal sealed class OnlineAiResponseException : InvalidOperationException
    {
        internal OnlineAiResponseException(string message) : base(message) { }
    }

    internal static class OnlineAiResponse
    {
        internal static string Extract(Dictionary<string, object> root)
        {
            if (root == null) throw new OnlineAiResponseException("服务商未返回有效 JSON 对象。");
            object value;
            if (root.TryGetValue("output_text", out value) && value is string && !String.IsNullOrWhiteSpace((string)value))
                return (string)value;
            if (root.TryGetValue("choices", out value))
            {
                object[] choices = AsArray(value);
                if (choices.Length > 0)
                {
                    Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
                    object messageValue, content;
                    if (choice != null && choice.TryGetValue("message", out messageValue))
                    {
                        Dictionary<string, object> message = messageValue as Dictionary<string, object>;
                        if (message != null && message.TryGetValue("content", out content))
                        {
                            string text = ContentText(content);
                            if (!String.IsNullOrWhiteSpace(text)) return text;
                        }
                    }
                    object reason;
                    if (choice != null && choice.TryGetValue("finish_reason", out reason) && Convert.ToString(reason) == "length")
                        throw new OnlineAiResponseException("已连接服务商，但输出额度耗尽，尚无最终译文。请选用非推理模型，或在服务商控制台核对模型参数。");
                }
            }
            if (root.TryGetValue("output", out value))
            {
                StringBuilder text = new StringBuilder();
                foreach (object item in AsArray(value))
                {
                    Dictionary<string, object> output = item as Dictionary<string, object>;
                    object content;
                    // Reasoning summaries are not translations.
                    if (output != null && output.TryGetValue("content", out content)) text.Append(ContentText(content));
                }
                if (!String.IsNullOrWhiteSpace(text.ToString())) return text.ToString();
            }
            if (root.TryGetValue("status", out value) && Convert.ToString(value) == "incomplete")
                throw new OnlineAiResponseException("已连接服务商，但响应未完成，没有最终译文。请核对模型输出额度和请求协议。");
            throw new OnlineAiResponseException("已连接服务商，但响应中没有最终译文；请核对模型和 Chat Completions / Responses 协议。");
        }

        private static string ContentText(object content)
        {
            if (content is string) return (string)content;
            StringBuilder text = new StringBuilder();
            foreach (object part in AsArray(content))
            {
                Dictionary<string, object> block = part as Dictionary<string, object>;
                object type, value;
                if (block != null && block.TryGetValue("type", out type) &&
                    (Convert.ToString(type) == "text" || Convert.ToString(type) == "output_text") &&
                    block.TryGetValue("text", out value) && value is string) text.Append((string)value);
            }
            return text.ToString();
        }

        private static object[] AsArray(object value)
        {
            object[] array = value as object[];
            if (array != null) return array;
            ArrayList list = value as ArrayList;
            return list == null ? new object[0] : list.ToArray();
        }
    }
}
