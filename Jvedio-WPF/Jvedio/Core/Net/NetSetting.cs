namespace Jvedio.Core.Net
{
    /// <summary>
    /// 全局网络参数，取代 <c>SuperUtils.Config.SettingManager</c>。
    /// <para>
    /// 由 <c>Jvedio.Core.Config.UtilsManager.OnUtilSettingChange</c> 在设置变化时写入，
    /// 设置界面改超时/忽略证书后会走到这里。
    /// </para>
    /// </summary>
    public static class NetSetting
    {
        /// <summary>整次请求的外层超时，单位<b>秒</b>。默认与 <c>ProxyConfig.DEFAULT_TIMEOUT</c> 一致。</summary>
        public static int HttpTimeout { get; set; } = 10;

        /// <summary>是否忽略服务端证书错误。项目默认开启（大量刮削站点证书不规范）。</summary>
        public static bool IgnoreCertVal { get; set; } = true;
    }
}
