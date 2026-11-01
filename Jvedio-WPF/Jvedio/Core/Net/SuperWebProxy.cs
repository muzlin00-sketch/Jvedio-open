using System.Net;
using MihaZupan;

namespace Jvedio.Core.Net
{
    public enum ProxyMode
    {
        None = 0,
        System = 1,
        Custom = 2,
    }

    public enum ProxyProtocol
    {
        HTTP = 0,
        SOCKS = 1,
    }

    /// <summary>
    /// 代理配置与转换，取代 <c>SuperUtils.NetWork.SuperWebProxy</c>。
    /// <para>
    /// HTTP 代理转成 <see cref="WebProxy"/>；SOCKS 代理 .NET 的 <see cref="WebProxy"/> 不支持，
    /// 走 <c>MihaZupan.HttpToSocks5Proxy</c>。这个库已经是项目的既有依赖，不要为它改包。
    /// </para>
    /// </summary>
    public class SuperWebProxy
    {
        private static IWebProxy _systemWebProxy;

        public ProxyMode ProxyMode { get; set; } = ProxyMode.None;

        public string Server { get; set; } = string.Empty;

        public int Port { get; set; }

        public string UserName { get; set; }

        public string Pwd { get; set; }

        public ProxyProtocol ProxyProtocol { get; set; } = ProxyProtocol.HTTP;

        public ICredentials Credentials { get; set; }

        /// <summary>系统代理（读 IE/Windows 的代理设置）。进程内只取一次。</summary>
        public static IWebProxy SystemWebProxy {
            get {
                if (_systemWebProxy == null)
                    _systemWebProxy = WebRequest.GetSystemWebProxy();
                return _systemWebProxy;
            }
            set { _systemWebProxy = value; }
        }

        public IWebProxy GetWebProxy()
        {
            switch (ProxyMode) {
                case ProxyMode.System:
                    return SystemWebProxy;

                case ProxyMode.Custom:
                    // HttpToSocks5Proxy 的重载对 null 用户名会抛 ArgumentNullException，
                    // 没配账号密码时必须走两参数版本。
                    if (ProxyProtocol == ProxyProtocol.SOCKS) {
                        if (string.IsNullOrEmpty(UserName))
                            return new HttpToSocks5Proxy(Server, Port);

                        return new HttpToSocks5Proxy(Server, Port, UserName, Pwd);
                    }

                    WebProxy webProxy = new WebProxy(Server, Port);
                    if (!string.IsNullOrEmpty(UserName) || !string.IsNullOrEmpty(Pwd))
                        webProxy.Credentials = new NetworkCredential(UserName, Pwd);
                    else if (Credentials != null)
                        webProxy.Credentials = Credentials;
                    return webProxy;

                default:
                    return null;
            }
        }
    }
}
