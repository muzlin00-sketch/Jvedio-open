using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// URL / IP 相关的小工具，取代 <c>SuperUtils.NetWork.NetUtils</c>。
    /// </summary>
    public static class NetUtils
    {
        /// <summary>是否是一个可以直接发请求的 http(s) 绝对地址。</summary>
        public static bool IsProperUrl(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return Uri.TryCreate(source, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public static Uri TryGetUri(string url)
        {
            try {
                return new Uri(url);
            } catch (Exception) {
                return null;
            }
        }

        public static Uri TryGetUri(string baseurl, string url)
        {
            try {
                return new Uri(new Uri(baseurl), url);
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>
        /// 补全成 http(s) 地址。<c>appendSplit</c> 为真时补尾部斜杠。
        /// <para>
        /// 原实现对空串会抛异常、对 <c>//host/path</c> 这种协议相对地址会得到
        /// <c>https:////host/path</c>。这里按"补协议头"这个本意实现，不复制那两个畸形结果——
        /// 调用方传入的地址都来自配置或刮削结果，不会是这两类。
        /// </para>
        /// </summary>
        public static string ToProperUrl(string url, bool appendSplit)
        {
            if (url == null)
                url = string.Empty;

            if (!IsProperUrl(url))
                url = "https://" + url;

            if (appendSplit && !url.EndsWith("/"))
                url += "/";

            return url;
        }

        /// <summary>
        /// 取本机对外可用的 IPv4。
        /// <para>
        /// 优先用"默认路由出口"那个地址（连一个外部地址但不真正发包，由系统选出出口网卡），
        /// 这样拿到的就是其它设备能访问到的 IP；取不到再退回网卡枚举。
        /// 投屏/局域网服务对这个值敏感，不能随手取第一个非回环地址（可能落到 VPN 虚拟网卡）。
        /// </para>
        /// </summary>
        public static string GetLocalIPAddress()
        {
            try {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                        return endPoint.Address.ToString();
                }
            } catch (Exception) {
                // 没有可用出口时退回枚举
            }

            try {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation info in ni.GetIPProperties().UnicastAddresses) {
                        if (info.Address.AddressFamily == AddressFamily.InterNetwork
                            && !IPAddress.IsLoopback(info.Address))
                            return info.Address.ToString();
                    }
                }
            } catch (Exception) {
                // 忽略，返回空串
            }

            return string.Empty;
        }

        public static bool IsPortInUse(int port)
        {
            try {
                TcpListener listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return false;
            } catch (SocketException) {
                return true;
            }
        }

        public static bool IsProperIPV4(string ipString)
        {
            if (string.IsNullOrWhiteSpace(ipString))
                return false;

            return IPAddress.TryParse(ipString, out IPAddress address)
                && address.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
