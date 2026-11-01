using Jvedio.Core.Crawler;
using static Jvedio.App;

namespace Jvedio.Core.Config
{
    public class UtilsManager
    {
        public static void OnUtilSettingChange()
        {
            Jvedio.Core.Net.NetSetting.HttpTimeout = (int)ConfigManager.ProxyConfig.HttpTimeout;
            Jvedio.Core.Net.NetSetting.IgnoreCertVal = ConfigManager.Settings.IgnoreCertVal;

            // 代理设置
            CrawlerHeader.Init();

            Logger.Info("util setting changed");
        }
    }
}
