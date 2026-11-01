using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// 一次请求的配置（方法、超时、请求头、代理、请求体），取代
    /// <c>SuperUtils.NetWork.Entity.RequestHeader</c>。
    /// <para>
    /// <see cref="TimeOut"/> 的单位是<b>毫秒</b>（默认 10000），而
    /// <see cref="Jvedio.Core.Net.NetSetting.HttpTimeout"/> 的单位是<b>秒</b>，
    /// 两者在调用处相乘转换，不要混用。
    /// </para>
    /// </summary>
    public class RequestHeader
    {
        public const double DEFAULT_TIMEOUT = 10000;

        public HttpMethod Method { get; set; } = HttpMethod.Get;

        public bool AllowAutoRedirect { get; set; } = true;

        public double TimeOut { get; set; } = DEFAULT_TIMEOUT;

        public HttpContent RequestBody { get; set; }

        /// <summary>为 null 表示不走代理（请求时 <c>UseProxy</c> 会关掉）。</summary>
        public IWebProxy WebProxy { get; set; } = SuperWebProxy.SystemWebProxy;

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public override string ToString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("method: " + Method);
            sb.AppendLine("allow redirect: " + AllowAutoRedirect);
            sb.AppendLine("TimeOut: " + TimeOut);
            sb.AppendLine("Headers: ");
            if (Headers != null) {
                foreach (var kv in Headers)
                    sb.AppendLine(kv.Key + ": " + kv.Value);
            }
            return sb.ToString();
        }
    }
}
