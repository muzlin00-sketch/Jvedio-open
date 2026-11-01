using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Lan
{
    /// <summary>
    /// DLNA / UPnP 投屏设备。
    /// </summary>
    public class DlnaDevice
    {
        /// <summary>设备友好名称，如「极米投影仪」</summary>
        public string Name { get; set; }

        /// <summary>设备描述 URL（SSDP LOCATION 头）</summary>
        public string Location { get; set; }

        /// <summary>AVTransport 控制地址，投屏指令发到这里</summary>
        public string ControlUrl { get; set; }

        /// <summary>设备 IP，用于去重与展示</summary>
        public string Ip { get; set; }

        /// <summary>设备型号描述，辅助用户辨认</summary>
        public string Model { get; set; }
    }

    /// <summary>
    /// 极简 DLNA 投屏（UPnP AVTransport）。
    ///
    /// 为什么自己写而不引 NuGet 包：
    /// - 项目目标 .NET Framework 4.7.2，主流 DLNA 库（如 Mono.Upnp）已多年停更，
    ///   且会拖入大量 Sankara 依赖，与现有 netstandard2.0 组合容易版本冲突。
    /// - 投屏只需三个动作：SSDP 发现设备、读设备描述拿 controlURL、
    ///   发 SOAP 指令（SetAVTransportURI / Play / Pause / Stop）。协议本身很小。
    ///
    /// 协议要点（对应 Jellyfin / MiniDLNA 的做法）：
    /// - SSDP：向 239.255.255.250:1900 发 M-SEARCH，搜
    ///   urn:schemas-upnp-org:device:MediaRenderer:1。
    /// - 设备描述：GET LOCATION 拿 XML，从中取 AVTransport 服务的 controlURL，
    ///   注意 controlURL 可能是相对路径，要与 LOCATION 的 host 拼起来。
    /// - 控制：POST 到 controlURL，body 是 SOAP 信封，
    ///   SOAPACTION 头必须带双引号，设备普遍对这个格式很严格。
    /// </summary>
    public class DlnaCaster
    {
        private const string SSDP_ADDR = "239.255.255.250";
        private const int SSDP_PORT = 1900;

        /// <summary>MediaRenderer 是「能被投屏的播放设备」标准设备类型</summary>
        private const string SEARCH_TARGET = "urn:schemas-upnp-org:device:MediaRenderer:1";

        private const string AVT_NS = "urn:schemas-upnp-org:service:AVTransport:1";

        /// <summary>已发现的设备，按 Location 去重</summary>
        private readonly ConcurrentDictionary<string, DlnaDevice> _devices =
            new ConcurrentDictionary<string, DlnaDevice>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前投屏目标（一台设备同时只投一个片源）</summary>
        private DlnaDevice _current;

        public DlnaDevice Current => _current;

        #region "设备发现"

        /// <summary>
        /// 搜索局域网内的投屏设备。
        /// <para>
        /// 会同时发多条不同 ST 的 M-SEARCH：有些设备只回应
        /// MediaRenderer，有些只回应 MediaServer 或 ssdp:all。
        /// 只发一种会漏设备（实测极米只回 MediaRenderer，部分电视只回 ssdp:all）。
        /// </para>
        /// <para>
        /// 多网卡必须逐个发：本机同时有以太网和 WLAN 时，
        /// 默认路由只会从其中一个网卡发出广播，
        /// 投影仪接在另一个网段就完全发现不了（实测本机有 .2 和 .34 两个地址）。
        /// </para>
        /// </summary>
        /// <param name="timeoutMs">等待回应时长，2 秒足够局域网</param>
        public List<DlnaDevice> Discover(int timeoutMs = 2000)
        {
            var found = new ConcurrentDictionary<string, DlnaDevice>(StringComparer.OrdinalIgnoreCase);

            string[] searchTargets = {
                SEARCH_TARGET,
                "urn:schemas-upnp-org:device:MediaServer:1",
                "ssdp:all"
            };

            // 逐个本机 IPv4 地址发广播：每个网卡绑一个独立 socket
            var localIps = GetLocalIPv4Addresses();
            if (localIps.Count == 0)
                localIps.Add(IPAddress.Any);

            var tasks = localIps.Select(ip => Task.Run(() => {
                var list = new List<DlnaDevice>();
                try {
                    using (var udp = new UdpClient(AddressFamily.InterNetwork)) {
                        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        // 绑定到具体网卡，保证广播从这张网卡出去
                        if (!ip.Equals(IPAddress.Any))
                            udp.Client.Bind(new IPEndPoint(ip, 0));
                        else
                            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

                        udp.Client.ReceiveTimeout = 500;

                        // 多播出口也指定为该网卡
                        try {
                            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                                ip.GetAddressBytes());
                        } catch { /* 部分系统不允许，忽略 */ }

                        var endpoint = new IPEndPoint(IPAddress.Parse(SSDP_ADDR), SSDP_PORT);
                        foreach (string st in searchTargets) {
                            byte[] payload = BuildMSearch(st);
                            try { udp.Send(payload, payload.Length, endpoint); }
                            catch { /* 单条发失败不影响其它 */ }
                        }

                        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                        while (DateTime.UtcNow < deadline) {
                            try {
                                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                                byte[] data = udp.Receive(ref remote);
                                string text = Encoding.UTF8.GetString(data);

                                string location = ExtractHeader(text, "LOCATION");
                                if (string.IsNullOrEmpty(location))
                                    continue;

                                var dev = LoadDevice(location, remote.Address.ToString());
                                if (dev != null)
                                    list.Add(dev);
                            } catch (SocketException) {
                                // 接收超时，继续等到达 deadline
                            }
                        }
                    }
                } catch (Exception ex) {
                    App.Logger?.Debug($"DLNA 发现在 {ip} 失败: {ex.Message}");
                }
                return list;
            })).ToArray();

            try {
                Task.WaitAll(tasks, timeoutMs + 3000);
            } catch { /* 忽略 */ }

            foreach (var t in tasks) {
                if (t.Status != TaskStatus.RanToCompletion)
                    continue;
                foreach (var d in t.Result)
                    found[d.Location] = d;
            }

            // 合并进缓存，方便前端按名字再次选择
            foreach (var kv in found)
                _devices[kv.Key] = kv.Value;

            return found.Values
                .OrderBy(d => d.Name, StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>
        /// 取本机所有可用 IPv4 地址（去重、排除回环与自动专用地址）。
        /// </summary>
        private static List<IPAddress> GetLocalIPv4Addresses()
        {
            var result = new List<IPAddress>();
            try {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
                        var a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        if (IPAddress.IsLoopback(a))
                            continue;
                        // 169.254.x.x 是没拿到 DHCP 时的自动地址，发广播没意义
                        byte[] b = a.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254)
                            continue;
                        if (!result.Any(x => x.Equals(a)))
                            result.Add(a);
                    }
                }
            } catch { /* 忽略 */ }
            return result;
        }

        private static byte[] BuildMSearch(string searchTarget)
        {
            var sb = new StringBuilder();
            sb.Append("M-SEARCH * HTTP/1.1\r\n");
            sb.Append("HOST: ").Append(SSDP_ADDR).Append(':').Append(SSDP_PORT).Append("\r\n");
            sb.Append("MAN: \"ssdp:discover\"\r\n");
            sb.Append("MX: 2\r\n");
            sb.Append("ST: ").Append(searchTarget).Append("\r\n");
            sb.Append("USER-AGENT: Windows/10 UPnP/1.1 Jvedio/1.0\r\n");
            sb.Append("\r\n");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string ExtractHeader(string response, string name)
        {
            foreach (string line in response.Split(new[] { "\r\n" }, StringSplitOptions.None)) {
                int idx = line.IndexOf(':');
                if (idx <= 0)
                    continue;
                if (string.Equals(line.Substring(0, idx).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(idx + 1).Trim();
            }
            return null;
        }

        /// <summary>
        /// 读设备描述 XML，取出 AVTransport 的 controlURL。
        /// 描述里有多个 service（AVTransport / RenderingControl / ConnectionManager），
        /// 只有 AVTransport 能用来投屏。
        /// </summary>
        private DlnaDevice LoadDevice(string location, string ip)
        {
            try {
                string xml = HttpGet(location, 3000);
                if (string.IsNullOrEmpty(xml))
                    return null;

                // 只保留真正能放视频的设备：必须有 AVTransport 服务
                string control = FindAvTransportControlUrl(xml, location);
                if (string.IsNullOrEmpty(control))
                    return null;

                string name = MatchTag(xml, "friendlyName");
                string model = MatchTag(xml, "modelName");

                return new DlnaDevice {
                    Name = string.IsNullOrWhiteSpace(name) ? ip : WebUtility.HtmlDecode(name.Trim()),
                    Location = location.Trim(),
                    ControlUrl = control,
                    Ip = ip,
                    Model = string.IsNullOrWhiteSpace(model) ? string.Empty : WebUtility.HtmlDecode(model.Trim())
                };
            } catch (Exception ex) {
                App.Logger?.Debug($"DLNA 读取设备描述失败 {location}: {ex.Message}");
                return null;
            }
        }

        private static string MatchTag(string xml, string tag)
        {
            var m = Regex.Match(xml, "<" + tag + ">(.*?)</" + tag + ">",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// 在设备描述里定位 AVTransport 服务的 controlURL，并把相对路径补成绝对地址。
        /// </summary>
        private static string FindAvTransportControlUrl(string xml, string location)
        {
            // 匹配每个 <service> 块，找到 serviceType 含 AVTransport 的那个
            var blocks = Regex.Matches(xml, "<service>(.*?)</service>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match block in blocks) {
                string body = block.Groups[1].Value;
                string type = MatchTag(body, "serviceType");
                if (type == null || type.IndexOf("AVTransport", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string control = MatchTag(body, "controlURL");
                if (string.IsNullOrWhiteSpace(control))
                    continue;

                control = WebUtility.HtmlDecode(control.Trim());
                return MakeAbsolute(location, control);
            }
            return null;
        }

        private static string MakeAbsolute(string location, string controlUrl)
        {
            if (controlUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return controlUrl;

            var baseUri = new Uri(location);
            // 以 / 开头是主机根路径，否则相对描述文件所在目录
            return new Uri(baseUri, controlUrl).ToString();
        }

        #endregion

        #region "投屏控制"

        /// <summary>
        /// 把视频投到指定设备并开始播放。
        /// </summary>
        /// <param name="device">目标设备（通常来自 Discover 的结果）</param>
        /// <param name="mediaUrl">视频的绝对 URL，必须是投影仪能访问的局域网地址</param>
        /// <param name="title">显示在投影仪上的标题</param>
        public bool Cast(DlnaDevice device, string mediaUrl, string title)
        {
            if (device == null || string.IsNullOrEmpty(device.ControlUrl))
                return false;

            // 1) 设置播放源
            string metadata = BuildMetadata(mediaUrl, title);
            bool ok = Soap(device, "SetAVTransportURI",
                "<InstanceID>0</InstanceID>" +
                "<CurrentURI>" + Escape(mediaUrl) + "</CurrentURI>" +
                "<CurrentURIMetaData>" + Escape(metadata) + "</CurrentURIMetaData>");

            if (!ok)
                return false;

            // 2) 起播。部分设备 SetAVTransportURI 后会自动播，
            // 再发一次 Play 是幂等的，不会出错。
            Soap(device, "Play", "<InstanceID>0</InstanceID><Speed>1</Speed>");

            _current = device;
            App.Logger?.Info($"投屏开始：{device.Name} <- {title}");
            return true;
        }

        /// <summary>
        /// DIDL-Lite 元数据。设备靠它知道这是视频而不是音乐/图片。
        /// 不传也能播，但部分电视会当成 unknown 而拒绝或显示错误图标。
        /// </summary>
        private static string BuildMetadata(string url, string title)
        {
            string safeTitle = Escape(title ?? string.Empty);
            return "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\"" +
                   " xmlns:dc=\"http://purl.org/dc/elements/1.1/\"" +
                   " xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">" +
                   "<item id=\"0\" parentID=\"-1\" restricted=\"1\">" +
                   "<dc:title>" + safeTitle + "</dc:title>" +
                   "<upnp:class>object.item.videoItem</upnp:class>" +
                   "<res protocolInfo=\"http-get:*:video/mp4:*\">" + Escape(url) + "</res>" +
                   "</item></DIDL-Lite>";
        }

        public bool Pause(DlnaDevice device = null)
        {
            var d = device ?? _current;
            if (d == null) return false;
            return Soap(d, "Pause", "<InstanceID>0</InstanceID>");
        }

        public bool Resume(DlnaDevice device = null)
        {
            var d = device ?? _current;
            if (d == null) return false;
            return Soap(d, "Play", "<InstanceID>0</InstanceID><Speed>1</Speed>");
        }

        public bool Stop(DlnaDevice device = null)
        {
            var d = device ?? _current;
            if (d == null) return false;
            bool ok = Soap(d, "Stop", "<InstanceID>0</InstanceID>");
            if (ok && device == null)
                _current = null;
            return ok;
        }

        /// <summary>
        /// 跳到指定秒数（拖动进度条）。
        /// </summary>
        public bool Seek(int seconds, DlnaDevice device = null)
        {
            var d = device ?? _current;
            if (d == null) return false;

            string target = TimeSpan.FromSeconds(Math.Max(0, seconds))
                .ToString(@"hh\:mm\:ss");
            return Soap(d, "Seek",
                "<InstanceID>0</InstanceID><Unit>REL_TIME</Unit><Target>" + target + "</Target>");
        }

        /// <summary>
        /// 查询传输状态（是否在播）。
        /// <para>
        /// 注意：这里只能拿到 CurrentTransportState。
        /// 播放进度 RelTime 属于另一个动作 GetPositionInfo，
        /// 在这取永远是空的（曾经踩过：进度一直显示 00:00:00）。
        /// </para>
        /// </summary>
        public string GetTransportInfo(DlnaDevice device = null)
        {
            var d = device ?? _current;
            if (d == null) return null;
            return SoapRaw(d, "GetTransportInfo", "<InstanceID>0</InstanceID>");
        }

        /// <summary>
        /// 查询播放进度与总时长。
        /// <para>返回 RelTime 与 TrackDuration，形如 00:12:34。</para>
        /// </summary>
        public string GetPositionInfo(DlnaDevice device = null)
        {
            var d = device ?? _current;
            if (d == null) return null;
            return SoapRaw(d, "GetPositionInfo", "<InstanceID>0</InstanceID>");
        }

        /// <summary>
        /// 发送 AVTransport SOAP 动作。
        /// </summary>
        private bool Soap(DlnaDevice device, string action, string body)
        {
            string resp = SoapRaw(device, action, body);
            // 设备返回 200 即成功；失败时 body 里会带 UPnPError
            return resp != null && resp.IndexOf("UPnPError", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private string SoapRaw(DlnaDevice device, string action, string body)
        {
            string envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"" +
                " s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body><u:" + action + " xmlns:u=\"" + AVT_NS + "\">" +
                body +
                "</u:" + action + "></s:Body></s:Envelope>";

            try {
                var req = (HttpWebRequest)WebRequest.Create(device.ControlUrl);
                req.Method = "POST";
                req.ContentType = "text/xml; charset=\"utf-8\"";
                // SOAPACTION 的双引号是协议硬要求，少了会被大量设备拒绝
                req.Headers.Add("SOAPACTION", "\"" + AVT_NS + "#" + action + "\"");
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.KeepAlive = false;

                byte[] bytes = Encoding.UTF8.GetBytes(envelope);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) {
                    s.Write(bytes, 0, bytes.Length);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    return reader.ReadToEnd();
                }
            } catch (WebException wex) {
                // 400/500 时设备仍会返回带 UPnPError 的正文，读出来便于排查
                try {
                    if (wex.Response != null) {
                        using (var reader = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8)) {
                            string err = reader.ReadToEnd();
                            App.Logger?.Warn($"DLNA {action} 失败: {Truncate(err, 300)}");
                            return err;
                        }
                    }
                } catch { /* 忽略 */ }
                App.Logger?.Warn($"DLNA {action} 失败: {wex.Message}");
                return null;
            } catch (Exception ex) {
                App.Logger?.Warn($"DLNA {action} 异常: {ex.Message}");
                return null;
            }
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.KeepAlive = false;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                return reader.ReadToEnd();
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }

        #endregion
    }
}
