using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jvedio.Core.Logs;
using BclClient = System.Net.Http.HttpClient;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// HTTP 请求入口，取代 <c>SuperUtils.NetWork.HttpClient</c>。
    /// <para>
    /// 语义是从原实现逐条复刻的，改动前先看这几条：
    /// </para>
    /// <list type="bullet">
    /// <item>整次请求外面还包了一层"总超时"（<see cref="NetSetting.HttpTimeout"/> 秒），
    ///       它比 <see cref="RequestHeader.TimeOut"/> 那层单请求超时更外层。</item>
    /// <item>出错不抛异常，而是返回一个 <see cref="HttpResult"/> 并把异常消息写进
    ///       <see cref="HttpResult.Error"/>；超时特殊处理成固定文案 <c>HTTP TIME OUT</c>。</item>
    /// <item><c>Host</c> 头总会按 URL 的 host 补一条；请求头里 <c>cookie</c>/<c>cookies</c>
    ///       不当作普通头，而是解析进 <see cref="CookieContainer"/>；
    ///       <c>user-agent</c> 必须走 <c>UserAgent.ParseAdd</c>，否则会因格式校验抛异常。</item>
    /// </list>
    /// </summary>
    public static class HttpClient
    {
        /// <summary>3 参数重载：证书与超时取全局设置。</summary>
        public static Task<HttpResult> Get(string Url, RequestHeader header, HttpMode httpMode)
        {
            return Get(Url, header, httpMode, NetSetting.IgnoreCertVal, NetSetting.HttpTimeout);
        }

        public static Task<HttpResult> Get(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal, int timeOut)
        {
            return DoHttp(Url, HttpMethod.Get, header, httpMode, ignoreCertVal, timeOut);
        }

        public static Task<HttpResult> Post(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal)
        {
            return Post(Url, header, httpMode, ignoreCertVal, NetSetting.HttpTimeout);
        }

        public static Task<HttpResult> Post(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal, int timeOut)
        {
            return DoHttp(Url, HttpMethod.Post, header, httpMode, ignoreCertVal, timeOut);
        }

        private static async Task<HttpResult> DoHttp(string Url, HttpMethod method, RequestHeader header, HttpMode httpMode, bool ignoreCertVal, int timeOut)
        {
            if (string.IsNullOrEmpty(Url) || !NetUtils.IsProperUrl(Url))
                throw new ArgumentException("Url");

            if (header == null) {
                Logger.Instance.Warn("header is null, use default");
                header = new RequestHeader();
            }

            header.Method = method;

            try {
                return await AsyncSend(Url, header, httpMode, ignoreCertVal).TimeoutAfter(TimeSpan.FromSeconds(timeOut));
            } catch (Exception ex) {
                Logger.Instance.Error(ex.Message);
                return new HttpResult() { Error = ex.Message };
            }
        }

        private static async Task<HttpResult> AsyncSend(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal)
        {
            Uri uri = new Uri(Url);
            HttpResult result = new HttpResult();

            HttpClientHandler handler = new HttpClientHandler() {
                UseProxy = header.WebProxy != null,
                Proxy = header.WebProxy,
                AllowAutoRedirect = header.AllowAutoRedirect,
            };

            if (ignoreCertVal) {
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
            }

            // cookie 单独解析进 CookieContainer，不当作普通请求头
            Dictionary<string, string> headers = header.Headers;
            string cookies = null;
            if (headers != null) {
                cookies = GetCookie(headers);
                CookieContainer cookieContainer = new CookieContainer();
                if (!string.IsNullOrEmpty(cookies)) {
                    foreach (string item in cookies.Split(';')) {
                        string kv = item.Trim();
                        if (string.IsNullOrEmpty(kv) || kv.IndexOf('=') <= 0)
                            continue;

                        string[] pair = kv.Split('=');
                        cookieContainer.Add(uri, new Cookie(pair[0], pair[1]));
                    }
                }
                handler.CookieContainer = cookieContainer;
            }

            using (BclClient client = new BclClient(handler)) {
                client.DefaultRequestHeaders.Add("Host", uri.Host);
                client.Timeout = TimeSpan.FromMilliseconds(header.TimeOut);

                if (headers != null && headers.Count > 0) {
                    foreach (string key in headers.Keys) {
                        if (string.IsNullOrEmpty(key))
                            continue;

                        string lower = key.ToLower();
                        if (lower.Equals("cookie") || lower.Equals("cookies"))
                            continue;

                        string value = headers[key];
                        if (string.IsNullOrEmpty(value))
                            continue;

                        if (lower.Equals("user-agent")) {
                            client.DefaultRequestHeaders.UserAgent.ParseAdd(value);
                        } else {
                            try {
                                client.DefaultRequestHeaders.Add(key, value);
                            } catch (Exception ex) {
                                Logger.Instance.Error(ex.Message);
                            }
                        }
                    }
                }

                HttpResponseMessage response = null;
                try {
                    if (header.Method == HttpMethod.Get) {
                        Logger.Instance.Info("HTTP [GET] URL = " + uri.AbsoluteUri);
                        response = await client.GetAsync(uri);
                    } else if (header.Method == HttpMethod.Post) {
                        Logger.Instance.Info("HTTP [POST] URL = " + uri.AbsoluteUri);
                        response = await client.PostAsync(uri, header.RequestBody);
                    }

                    if (response != null) {
                        result.Headers = response.Headers;
                        result.StatusCode = response.StatusCode;

                        if (httpMode == HttpMode.String) {
                            result.SourceCode = await response.Content.ReadAsStringAsync();
                        } else if (httpMode == HttpMode.Stream) {
                            result.FileByte = await response.Content.ReadAsByteArrayAsync();
                        }
                    }
                } catch (TaskCanceledException ex) {
                    // 超时也走这里（.NET Framework 的 HttpClient 超时表现为 TaskCanceled）
                    Logger.Instance.Error(ex.Message);
                    result.Error = "HTTP TIME OUT";
                } catch (HttpRequestException e) {
                    Logger.Instance.Error(e.Message);
                    result.Error = e.Message;
                } catch (Exception e) {
                    Logger.Instance.Error(e.Message);
                    result.Error = e.Message;
                } finally {
                    response?.Dispose();
                }

                return result;
            }
        }

        /// <summary>按原实现支持的四种大小写找 cookie 头，找不到返回空串（不是 null）。</summary>
        private static string GetCookie(Dictionary<string, string> headers)
        {
            if (headers.ContainsKey("cookie"))
                return headers["cookie"];
            if (headers.ContainsKey("cookies"))
                return headers["cookies"];
            if (headers.ContainsKey("Cookie"))
                return headers["Cookie"];
            if (headers.ContainsKey("Cookies"))
                return headers["Cookies"];

            return string.Empty;
        }
    }
}
