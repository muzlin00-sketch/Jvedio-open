using System;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// 抓取辅助，取代 <c>SuperUtils.NetWork.HttpHelper</c>。
    /// <para>
    /// 两个方法都是"成功则返回内容，失败则抛 <see cref="WebException"/>"，
    /// 交给外层的重试/换源逻辑处理；不要在内部吞掉异常。
    /// </para>
    /// </summary>
    public static class HttpHelper
    {
        /// <summary>取网页的 &lt;title&gt;。页面没有 title 时返回空串。</summary>
        public static async Task<string> AsyncGetWebTitle(string Url, RequestHeader header)
        {
            string title = string.Empty;
            HttpResult httpResult;
            try {
                httpResult = await HttpClient.Get(Url, header, HttpMode.String);
            } catch (Exception ex) {
                httpResult = new HttpResult() { Error = ex.Message };
            }

            if (!string.IsNullOrEmpty(httpResult.SourceCode)) {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(httpResult.SourceCode);
                HtmlNode titleNode = doc.DocumentNode.SelectSingleNode("//title");
                if (titleNode != null)
                    title = titleNode.InnerText;
            } else {
                throw new WebException(httpResult.Error);
            }

            return title;
        }

        /// <summary>按字节下载资源，结果放在 <see cref="HttpResult.FileByte"/>。</summary>
        public static async Task<HttpResult> AsyncDownLoadFile(string Url, RequestHeader header)
        {
            HttpResult httpResult;
            try {
                httpResult = await HttpClient.Get(Url, header, HttpMode.Stream);
            } catch (Exception ex) {
                httpResult = new HttpResult() { Error = ex.Message };
            }

            if (httpResult.StatusCode == HttpStatusCode.OK)
                return httpResult;

            throw new WebException(httpResult.Error);
        }

        /// <summary>全局忽略证书校验（老式 ServicePointManager 通道用）。</summary>
        public static void SetCertificatePolicy()
        {
            ServicePointManager.ServerCertificateValidationCallback += RemoteCertificateValidate;
        }

        private static bool RemoteCertificateValidate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate cert, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
        {
            return true;
        }
    }
}
