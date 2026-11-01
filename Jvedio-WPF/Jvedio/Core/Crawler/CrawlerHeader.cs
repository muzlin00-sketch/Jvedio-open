using Jvedio.Core.Net;
using System.Collections.Generic;

namespace Jvedio.Core.Crawler
{
    public static class CrawlerHeader
    {
        /// <summary>默认 UA。刮削站点与 GitHub 都认它，改之前先确认对方没做 UA 白名单。</summary>
        public const string DEFAULT_USER_AGENT =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/95.0.4638.69 Safari/537.36";

        private static Dictionary<string, string> DEFAULT_HEADERS { get; set; } =
            new Dictionary<string, string>() {
                { "User-Agent", DEFAULT_USER_AGENT },
            };

        public static RequestHeader GitHub { get; set; }

        public static RequestHeader Default { get; set; }

        public static System.Net.IWebProxy WebProxy { get; set; }

        static CrawlerHeader()
        {
            Init();
        }

        public static void Init()
        {
            WebProxy = ConfigManager.ProxyConfig.GetWebProxy();
            Default = new RequestHeader() {
                WebProxy = WebProxy,
                Headers = new Dictionary<string, string>(DEFAULT_HEADERS),
            };
            GitHub = Default;
        }
    }
}
