using Jvedio.Core.Config.Base;
using Jvedio.Core.Crawler;
using Jvedio.Core.Plugins.Crawler;
using Jvedio.Entity.CommonSQL;
using Newtonsoft.Json;
using Jvedio.Common;
using Jvedio.Core.ORM.Enums;
using Jvedio.Core.ORM.Wrapper;
using System.Collections.Generic;
using System.Linq;

namespace Jvedio.Core.Config
{
    public class ServerConfig : AbstractConfig
    {
        private ServerConfig() : base("Servers")
        {
            //DownloadInfo = true;
            //DownloadThumbNail = true;
            //DownloadPoster = true;
            //DownloadPreviewImage = false;
            //DownloadActor = true;
        }

        //public bool DownloadInfo { get; set; }
        //public bool DownloadThumbNail { get; set; }
        //public bool DownloadPoster { get; set; }
        //public bool DownloadPreviewImage { get; set; }
        //public bool DownloadActor { get; set; }

        private static ServerConfig instance = null;

        public static ServerConfig CreateInstance()
        {
            if (instance == null)
                instance = new ServerConfig();
            return instance;
        }

        public List<CrawlerServer> CrawlerServers { get; set; }

        public override void Read()
        {
            CrawlerServers = new List<CrawlerServer>();
            SelectWrapper<AppConfig> wrapper = new SelectWrapper<AppConfig>();
            wrapper.Eq("ConfigName", ConfigName);
            AppConfig appConfig = MapperManager.appConfigMapper.SelectOne(wrapper);
            if (appConfig == null || appConfig.ConfigId == 0) {
                // 全新安装：数据库里还没有 Servers 记录。
                // 仓库自带的 JavBus 插件必须有至少一条「源」记录才会被用上，否则
                // VideoDownLoader.GetCrawlerServer 直接抛 CrawlerNotFoundException，
                // 表现就是「插件明明装好了，却提示没有可用的刮削器」。
                CrawlerServers = DefaultCrawlerServers();
                return;
            }
            List<Dictionary<object, object>> dicts = Jvedio.Common.JsonUtils.TryDeserializeObject<List<Dictionary<object, object>>>(appConfig.ConfigValue);

            if (dicts == null || CrawlerManager.PluginMetaDatas == null)
                return;
            foreach (Dictionary<object, object> d in dicts) {
                CrawlerServer server = new CrawlerServer();
                //if (!server.HasAllKeys(d))
                //    continue;
                if (d.ContainsKey("PluginID"))
                    server.PluginID = d["PluginID"].ToString();
                if (string.IsNullOrEmpty(server.PluginID))
                    continue;
                if (!CrawlerManager.PluginMetaDatas.Where(arg => arg.PluginID.Equals(server.PluginID)).Any())
                    continue;
                if (d.ContainsKey("Url") && d["Url"] is string url)
                    server.Url = url;
                if (d.ContainsKey("Cookies") && d["Cookies"] is string Cookies)
                    server.Cookies = Cookies;
                if (d.ContainsKey("Enabled") && d["Enabled"] is bool enabled)
                    server.Enabled = enabled;
                if (d.ContainsKey("LastRefreshDate") && d["LastRefreshDate"] is string LastRefreshDate)
                    server.LastRefreshDate = LastRefreshDate;
                if (d.ContainsKey("Headers") && d["Headers"] is string Headers)
                    server.Headers = Headers;
                if (d.ContainsKey("Available") && int.TryParse(d["Available"].ToString(), out int available))
                    server.Available = available;
                CrawlerServers.Add(server);
            }
        }

        /// <summary>
        /// 全新安装时的默认刮削源。
        /// <para>
        /// 插件目录名就是 PluginID 的后半段（<c>crawler/bus</c> 对应 <c>plugins/crawlers/bus/</c>）。
        /// 仓库自带四个番号插件，这里把它们各填一条源：JavBus 默认启用（免 Cookie 就能出结果），
        /// 其余三个预填地址但默认关闭——它们的站点常需要 Cookie 或会挡直连，
        /// 让用户自己在「设置 → 刮削器」里勾选、补 Cookie 更稳妥。
        /// </para>
        /// <para>
        /// 用户在设置里改过之后会被 <see cref="Save"/> 覆盖写库，不会重复添加。
        /// </para>
        /// </summary>
        private static List<CrawlerServer> DefaultCrawlerServers()
        {
            return new List<CrawlerServer>() {
                new CrawlerServer {
                    PluginID = "crawler/bus",
                    Url = "https://www.javbus.com/",
                    Enabled = true,
                    Available = 1,
                },
                new CrawlerServer {
                    PluginID = "crawler/db",
                    Url = "https://javdb.com/",
                    Enabled = false,
                    Available = 0,
                },
                new CrawlerServer {
                    PluginID = "crawler/library",
                    Url = "https://www.javlibrary.com/cn/",
                    Enabled = false,
                    Available = 0,
                },
                new CrawlerServer {
                    PluginID = "crawler/fc2",
                    Url = "https://fc2ppvdb.com/",
                    Enabled = false,
                    Available = 0,
                }
            };
        }

        public override void Save()
        {
            if (CrawlerServers != null) {
                AppConfig appConfig = new AppConfig();
                appConfig.ConfigName = ConfigName;
                appConfig.ConfigValue = JsonConvert.SerializeObject(CrawlerServers);
                MapperManager.appConfigMapper.Insert(appConfig, InsertMode.Replace);
            }
        }
    }
}
