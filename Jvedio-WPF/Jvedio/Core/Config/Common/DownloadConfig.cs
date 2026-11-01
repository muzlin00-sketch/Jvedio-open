using Jvedio.Core.Config.Base;

namespace Jvedio.Core.Config
{
    public class DownloadConfig : AbstractConfig
    {

        private DownloadConfig() : base("DownloadConfig")
        {
            DownloadInfo = true;
            DownloadThumbNail = true;
            DownloadPoster = true;
            DownloadPreviewImage = false;
            DownloadActor = true;
            OverrideInfo = false;
            // 内置刮削器：默认开启豆瓣 / TMDB / Bangumi，TMDB 没填 Key 时自动跳过
            EnableBuiltinCrawler = true;
            EnableDoubanCrawler = true;
            EnableTmdbCrawler = true;
            EnableBangumiCrawler = true;
            TmdbApiKey = string.Empty;
            TmdbLanguage = "zh-CN";
            DoubanCookie = string.Empty;
        }

        private static DownloadConfig _instance = null;

        public static DownloadConfig CreateInstance()
        {
            if (_instance == null)
                _instance = new DownloadConfig();

            return _instance;
        }

        private bool _DownloadInfo;
        public bool DownloadInfo {
            get { return _DownloadInfo; }
            set {
                _DownloadInfo = value;
                RaisePropertyChanged();
            }
        }
        private bool _DownloadThumbNail;
        public bool DownloadThumbNail {
            get { return _DownloadThumbNail; }
            set {
                _DownloadThumbNail = value;
                RaisePropertyChanged();
            }
        }
        private bool _DownloadPoster;
        public bool DownloadPoster {
            get { return _DownloadPoster; }
            set {
                _DownloadPoster = value;
                RaisePropertyChanged();
            }
        }
        private bool _DownloadPreviewImage;
        public bool DownloadPreviewImage {
            get { return _DownloadPreviewImage; }
            set {
                _DownloadPreviewImage = value;
                RaisePropertyChanged();
            }
        }
        private bool _DownloadActor;
        public bool DownloadActor {
            get { return _DownloadActor; }
            set {
                _DownloadActor = value;
                RaisePropertyChanged();
            }
        }
        private bool _OverrideInfo;
        public bool OverrideInfo {
            get { return _OverrideInfo; }
            set {
                _OverrideInfo = value;
                RaisePropertyChanged();
            }
        }

        #region "内置刮削器（普通影片）"

        private bool _EnableBuiltinCrawler;
        /// <summary>内置刮削器总开关。关闭后只走插件刮削器。</summary>
        public bool EnableBuiltinCrawler {
            get { return _EnableBuiltinCrawler; }
            set {
                _EnableBuiltinCrawler = value;
                RaisePropertyChanged();
            }
        }

        private bool _EnableDoubanCrawler;
        /// <summary>豆瓣源（免 Key）。</summary>
        public bool EnableDoubanCrawler {
            get { return _EnableDoubanCrawler; }
            set {
                _EnableDoubanCrawler = value;
                RaisePropertyChanged();
            }
        }

        private bool _EnableTmdbCrawler;
        /// <summary>TMDB 源（需 API Key）。</summary>
        public bool EnableTmdbCrawler {
            get { return _EnableTmdbCrawler; }
            set {
                _EnableTmdbCrawler = value;
                RaisePropertyChanged();
            }
        }

        private bool _EnableBangumiCrawler;
        /// <summary>Bangumi 源（动画 / 日剧，免 Key）。</summary>
        public bool EnableBangumiCrawler {
            get { return _EnableBangumiCrawler; }
            set {
                _EnableBangumiCrawler = value;
                RaisePropertyChanged();
            }
        }

        private string _TmdbApiKey;
        /// <summary>TMDB API Key（v3）。为空时 TMDB 源自动跳过。</summary>
        public string TmdbApiKey {
            get { return _TmdbApiKey; }
            set {
                _TmdbApiKey = value ?? string.Empty;
                RaisePropertyChanged();
            }
        }

        private string _TmdbLanguage;
        /// <summary>TMDB 语言，默认 zh-CN。</summary>
        public string TmdbLanguage {
            get { return _TmdbLanguage; }
            set {
                _TmdbLanguage = string.IsNullOrWhiteSpace(value) ? "zh-CN" : value.Trim();
                RaisePropertyChanged();
            }
        }

        private string _DoubanCookie;
        /// <summary>豆瓣 Cookie（可选）。豆瓣频繁请求会要求验证码，登录后填 Cookie 更稳。</summary>
        public string DoubanCookie {
            get { return _DoubanCookie; }
            set {
                _DoubanCookie = value ?? string.Empty;
                RaisePropertyChanged();
            }
        }

        #endregion

    }
}
