using Jvedio.Core.Net;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Crawler.Builtin
{
    /// <summary>内置刮削器共用的 JSON 取值工具：路径不存在就返回空值，不抛异常。</summary>
    internal static class BuiltinJson
    {
        public static JToken At(JToken token, params string[] path)
        {
            JToken current = token;
            foreach (string key in path) {
                if (current == null)
                    return null;
                if (current is JArray array) {
                    if (!int.TryParse(key, out int index) || index < 0 || index >= array.Count)
                        return null;
                    current = array[index];
                    continue;
                }
                if (!(current is JObject obj))
                    return null;
                current = obj[key];
            }
            return current;
        }

        public static string Str(JToken token, params string[] path)
        {
            JToken value = At(token, path);
            if (value == null || value.Type == JTokenType.Null)
                return string.Empty;
            if (value.Type == JTokenType.Array) {
                // 形如 ["173分钟"] 的数组，取第一个
                JToken first = ((JArray)value).FirstOrDefault();
                return first == null ? string.Empty : first.ToString();
            }
            return value.ToString().Trim();
        }

        public static int Int(JToken token, params string[] path)
        {
            string text = Str(token, path);
            if (string.IsNullOrEmpty(text))
                return 0;
            // 允许 "173分钟" / "2023-01-22" 这类带单位的值
            string digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length > 4)
                digits = new string(text.TakeWhile(c => char.IsDigit(c) || c == '-').ToArray());
            return int.TryParse(digits, out int value) ? value : 0;
        }

        public static float Float(JToken token, params string[] path)
        {
            string text = Str(token, path);
            if (string.IsNullOrEmpty(text))
                return 0f;
            return float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0f;
        }

        /// <summary>
        /// 取字符串数组；元素是对象时取 <paramref name="field"/> 字段。
        /// 字段本身是单个字符串时（豆瓣的 countries 就是这样）也返回单元素列表。
        /// </summary>
        public static List<string> Strings(JToken token, string field, params string[] path)
        {
            JToken value = At(token, path);
            if (value == null || value.Type == JTokenType.Null)
                return new List<string>();

            if (value is JArray array) {
                return array
                    .Select(item => {
                        if (item is JObject obj && !string.IsNullOrEmpty(field))
                            return obj[field] == null ? string.Empty : obj[field].ToString().Trim();
                        return item.ToString().Trim();
                    })
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();
            }

            string single = value.ToString().Trim();
            return string.IsNullOrWhiteSpace(single)
                ? new List<string>()
                : new List<string>() { single };
        }
    }

    /// <summary>内置刮削器共用的 HTTP 封装。</summary>
    internal static class BuiltinHttp
    {
        public const string DesktopUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        public const string MobileUserAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1";

        /// <summary>
        /// 基于现有 header 复制一份并覆盖 UA / Referer。
        /// 直接改传入对象会污染后续插件请求，所以必须复制。
        /// </summary>
        public static RequestHeader CreateHeader(RequestHeader baseHeader, string referer, string userAgent)
        {
            RequestHeader header = new RequestHeader();
            if (baseHeader != null) {
                header.WebProxy = baseHeader.WebProxy;
                header.TimeOut = baseHeader.TimeOut;
                header.AllowAutoRedirect = baseHeader.AllowAutoRedirect;
                if (baseHeader.Headers != null)
                    header.Headers = new Dictionary<string, string>(baseHeader.Headers);
            }
            if (string.IsNullOrEmpty(userAgent))
                userAgent = DesktopUserAgent;
            header.Headers["User-Agent"] = userAgent;
            if (!string.IsNullOrEmpty(referer))
                header.Headers["Referer"] = referer;
            header.Headers["Accept"] = "application/json, text/plain, */*";
            return header;
        }

        /// <summary>GET 一个 JSON 地址，返回解析后的 token；失败抛异常。</summary>
        public static async Task<JToken> GetJson(string url, RequestHeader header, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            HttpResult result = await Jvedio.Core.Net.HttpClient.Get(url, header, HttpMode.String);
            if (string.IsNullOrEmpty(result.SourceCode)) {
                throw new InvalidOperationException($"请求失败：{result.Error}（{url}）");
            }
            string content = result.SourceCode.Trim();
            if (content.Length == 0)
                throw new InvalidOperationException($"响应为空（{url}）");
            return JToken.Parse(content);
        }

        /// <summary>简单限速，避免触发站点风控。</summary>
        public static Task Delay(int milliseconds, CancellationToken token)
        {
            return milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, token);
        }
    }
}
