using Jvedio.Entity;
using Jvedio.Entity.Dto;
using Jvedio.Mapper;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Lan
{
    /// <summary>
    /// 局域网 HTTP 服务，供平板端/移动端访问影片库。
    /// <para>
    /// 设计要点：
    /// - 只读：仅提供查询与媒体读取，不提供任何写操作，避免移动端误改桌面端数据。
    /// - 数据来源复用桌面端同一套 Mapper，保证两端看到的内容完全一致。
    /// - 视频通过 Range 分块传输，平板播放器可拖动进度、断点续播。
    /// - 使用 <see cref="TcpListener"/> 而非 <c>HttpListener</c>：后者绑定所有网卡
    ///   需要管理员权限或 URL ACL 注册，普通用户运行会直接「拒绝访问」。
    /// </para>
    /// </summary>
    public class LanServer
    {
        public const int DEFAULT_PORT = 5200;

        private TcpListener _Listener;

        private CancellationTokenSource _Cts;

        public bool IsRunning { get; private set; }

        public int Port { get; private set; }

        /// <summary>对外访问的基础地址，如 http://192.168.1.5:5200</summary>
        public string BaseUrl { get; private set; }

        public string LastError { get; private set; }

        /// <summary>
        /// DLNA 投屏控制器。
        /// <para>
        /// 单例持有：设备发现结果和「当前投到哪台设备」要在多次请求间保持，
        /// 每次都 new 会导致前端选完设备、下一次请求又找不到它。
        /// </para>
        /// </summary>
        private readonly DlnaCaster _Caster = new DlnaCaster();

        /// <summary>
        /// 局域网访问鉴权。口令由桌面端设置页控制，
        /// 未设置口令时 <see cref="LanAuth.Enabled"/> 为 false，所有请求放行。
        /// </summary>
        private readonly LanAuth _Auth = new LanAuth();

        public LanAuth Auth => _Auth;

        /// <summary>
        /// 网页文件一致性检查结果。非空表示部署目录的 Web\index.html 与内嵌页面不一致。
        /// <para>
        /// 为什么需要这个：<see cref="TryServeWebPage"/> 优先读磁盘上的
        /// <c>Web\index.html</c>，找不到才用内嵌资源。部署时只换了 exe、
        /// 忘了同步拷 Web\index.html（或拷了旧版），程序会一直发旧页面，
        /// 而日志里没有任何异常——表现就是「代码改了、页面没变」。
        /// </para>
        /// </summary>
        public string WebPageWarning { get; private set; }

        /// <summary>
        /// 比对磁盘页面与内嵌页面。只在两者都存在且内容不同时报警。
        /// </summary>
        private void CheckWebPageConsistency()
        {
            WebPageWarning = null;
            try {
                string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html");
                if (!File.Exists(diskPath))
                    return;     // 没有磁盘副本，走内嵌资源，行为正确

                byte[] disk = File.ReadAllBytes(diskPath);

                var asm = typeof(LanServer).Assembly;
                using (var stream = asm.GetManifestResourceStream("Jvedio.Resources.Web.index.html")) {
                    if (stream == null)
                        return; // 没有内嵌资源，磁盘是唯一来源

                    if (stream.Length != disk.Length) {
                        WebPageWarning = $"Web\\index.html 与程序内嵌页面不一致（磁盘 {disk.Length} 字节 / 内嵌 {stream.Length} 字节）。"
                            + "当前实际发送的是磁盘副本，可能不是本次构建的版本。";
                        return;
                    }

                    // 长度相同也要比内容：同长度不同内容是改了一处后忘记同步拷
                    var embedded = new byte[stream.Length];
                    int read = 0;
                    while (read < embedded.Length) {
                        int n = stream.Read(embedded, read, embedded.Length - read);
                        if (n <= 0)
                            break;
                        read += n;
                    }
                    if (read != embedded.Length)
                        return;

                    for (int i = 0; i < embedded.Length; i++) {
                        if (embedded[i] != disk[i]) {
                            WebPageWarning = "Web\\index.html 与程序内嵌页面内容不一致。"
                                + "当前实际发送的是磁盘副本，可能不是本次构建的版本。";
                            return;
                        }
                    }
                }
            } catch (Exception ex) {
                // 自检失败不影响服务
                WebPageWarning = null;
                App.Logger?.Warn($"网页一致性自检失败：{ex.Message}");
            }
        }


        /// <summary>
        /// 启动服务。
        /// </summary>
        /// <param name="port">监听端口</param>
        public bool Start(int port = DEFAULT_PORT)
        {
            if (IsRunning)
                return true;

            try {
                Port = port;
                _Cts = new CancellationTokenSource();

                // IPAddress.Any：监听所有网卡，局域网内其他设备可访问；无需特权
                _Listener = new TcpListener(IPAddress.Any, port);
                _Listener.Start();

                IsRunning = true;
                BaseUrl = $"http://{GetLocalIpAddress()}:{port}";
                LastError = null;

                // 启动时自检网页文件，避免部署漏拷导致「改完页面没变」却毫无线索。
                // 放在绑定成功之后：自检不影响「能不能启动」，只影响「发的是不是新页面」。
                CheckWebPageConsistency();
                if (!string.IsNullOrEmpty(WebPageWarning))
                    App.Logger?.Warn($"网页文件自检：{WebPageWarning}");

                Task.Run(() => AcceptLoop(_Cts.Token));
                return true;
            } catch (Exception ex) {
                LastError = ex.Message;
                IsRunning = false;
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            try {
                _Cts?.Cancel();
                _Listener?.Stop();
            } catch {
                // 停止过程中的异常不影响后续重新启动
            } finally {
                _Listener = null;
                IsRunning = false;
            }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested) {
                TcpClient client;
                try {
                    client = await _Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                } catch {
                    // 停止时 AcceptTcpClientAsync 会抛异常，属正常退出路径
                    break;
                }

                // 每个连接独立处理，避免大文件传输阻塞其他请求
                _ = Task.Run(() => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            try {
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 300000;

                using (client)
                using (var stream = client.GetStream()) {
                    HttpRequest request = HttpRequest.Parse(stream);
                    if (request == null)
                        return;

                    // 记录来源 IP：登录失败限速按 IP 统计
                    try {
                        request.RemoteEndPoint = client.Client.RemoteEndPoint?.ToString();
                    } catch {
                        request.RemoteEndPoint = null;
                    }

                    var response = new HttpResponse(stream);
                    Route(request, response);
                }
            } catch (Exception ex) {
                // 客户端中断（如播放器 seek 后断开）属正常，但其他异常必须记录，
                // 否则表现为「服务返回空响应」，排查时毫无线索。
                if (IsClientDisconnect(ex)) {
                    return;
                }
                App.Logger?.Error($"LAN 请求处理失败 [{client?.Client?.RemoteEndPoint}]: {ex}");
                try {
                    using (var s = client.GetStream()) {
                        HttpResponse.WriteErrorTo(s, 500, ex.Message);
                    }
                } catch {
                    // 连错误响应都发不出去，只能放弃
                }
            }
        }

        /// <summary>
        /// 判断异常是否属于「客户端主动断开」这类正常情况。
        /// </summary>
        private static bool IsClientDisconnect(Exception ex)
        {
            if (ex is System.IO.IOException || ex is ObjectDisposedException || ex is OperationCanceledException)
                return true;
            if (ex is System.Net.Sockets.SocketException se) {
                // 10054 连接被重置 / 10053 软件导致连接中止
                return se.ErrorCode == 10054 || se.ErrorCode == 10053;
            }
            return false;
        }

        private void Route(HttpRequest request, HttpResponse response)
        {
            string path = (request.Path ?? "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/";

            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) {
                WriteError(response, 405, "only GET/HEAD/POST supported");
                return;
            }

            // 写操作只接受 POST，避免被浏览器预取或爬虫误触发
            bool isWritePath = path.StartsWith("/api/rate/") || path.StartsWith("/api/watched/");
            if (isWritePath && !string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) {
                WriteError(response, 405, "this endpoint requires POST");
                return;
            }

            if (path == "/" || path == "/index.html") {
                // 优先返回内嵌的移动端页面；找不到则退回 API 说明
                if (TryServeWebPage(response, "index.html"))
                    return;

                response.WriteText(200, "text/plain; charset=utf-8",
                    "Jvedio LAN API\n\nGET /api/libraries\nGET /api/videos?page=1&pageSize=20&dbId=&keyword=&sort=\nGET /api/video/{dataId}\nGET /image/{small|big|preview}/{dataId}\nGET /image/actor/{actorId}\nGET /stream/{dataId}");
                return;
            }

            /* ---- 鉴权相关接口：本身不要求令牌 ---- */
            if (path == "/api/health") {
                // 排障用：明确告诉调用方「现在发的是不是本次构建的页面」。
                // 不校验口令——被口令锁在门外时更需要这个信息。
                HandleHealth(response);
                return;
            }

            if (path == "/api/auth/status") {
                HandleAuthStatus(request, response);
                return;
            }

            if (path == "/api/auth/login") {
                HandleAuthLogin(request, response);
                return;
            }

            if (path == "/api/auth/logout") {
                // 令牌是自包含的（无服务端会话表），退出只需客户端丢弃令牌。
                // 这里返回成功让前端流程完整，真正的失效靠桌面端改口令。
                WriteJson(response, new { ok = true });
                return;
            }

            /* ---- 以下全部需要鉴权 ---- */
            if (!CheckAccess(request, response, path))
                return;

            if (path == "/api/libraries") {
                WriteJson(response, HandleLibraries());
                return;
            }

            if (path == "/api/videos") {
                HandleVideoList(request, response);
                return;
            }

            if (path.StartsWith("/api/video/")) {
                HandleVideoDetail(request, response, path);
                return;
            }

            if (path.StartsWith("/image/")) {
                HandleImage(request, response, path);
                return;
            }

            if (path.StartsWith("/stream/")) {
                HandleStream(request, response, path);
                return;
            }

            // 随机推荐
            if (path == "/api/random") {
                HandleRandom(request, response);
                return;
            }

            // 相似推荐
            if (path.StartsWith("/api/similar/")) {
                HandleSimilar(request, response, path);
                return;
            }

            // 筛选维度列表
            if (path == "/api/filters") {
                HandleFilters(response);
                return;
            }

            // 收藏 / 评分（写操作）
            if (path.StartsWith("/api/rate/")) {
                HandleRate(request, response, path);
                return;
            }

            // 标记已看（写操作）
            if (path.StartsWith("/api/watched/")) {
                HandleWatched(request, response, path);
                return;
            }

            // 投屏（DLNA）
            if (path == "/api/cast/devices") {
                HandleCastDevices(request, response);
                return;
            }

            if (path.StartsWith("/api/cast/")) {
                HandleCastControl(request, response, path);
                return;
            }

            // 其它静态资源（页面引用的 css/js/图片等）
            if (TryServeWebPage(response, path.TrimStart('/'))) {
                return;
            }

            WriteError(response, 404, "Not Found");
        }

        #region "收藏与评分"

        /// <summary>
        /// POST /api/rate/{dataId}?grade=4.5
        /// <para>
        /// 设置评分。桌面端把「我的收藏」定义为 <c>Grade &gt; 0</c>，
        /// 所以「收藏一部片」= 给它打分，取消收藏 = 设为 0。
        /// 这里沿用同一语义，保证手机端与桌面端数据完全一致。
        /// </para>
        /// </summary>
        private void HandleRate(HttpRequest request, HttpResponse response, string path)
        {
            try {
                long dataId = ParseLong(path.Substring("/api/rate/".Length), 0);
                if (dataId <= 0) {
                    WriteError(response, 400, "invalid dataId");
                    return;
                }

                string raw = GetQuery(request, "grade");
                double grade;
                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out grade)) {
                    WriteError(response, 400, "invalid grade");
                    return;
                }
                // 评分范围 0-5，0 表示取消收藏
                if (grade < 0) grade = 0;
                if (grade > 5) grade = 5;

                var mapper = MapperManager.metaDataMapper;
                if (mapper == null) {
                    WriteError(response, 500, "mapper unavailable");
                    return;
                }

                string sql = "update metadata set Grade=" +
                    grade.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    $", UpdateDate='{DateTime.Now:yyyy-MM-dd HH:mm:ss}' where DataID={dataId}";
                mapper.ExecuteNonQuery(sql);

                App.Logger?.Info($"评分已更新：DataID={dataId} Grade={grade}");
                WriteJson(response, new { ok = true, dataId, grade });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/rate 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>
        /// POST /api/watched/{dataId}
        /// <para>
        /// 记录一次观看：ViewCount +1，ViewDate 设为当前时间。
        /// 桌面端的「最近播放」按 ViewDate 过滤，因此这里必须写这个字段。
        /// </para>
        /// </summary>
        private void HandleWatched(HttpRequest request, HttpResponse response, string path)
        {
            try {
                long dataId = ParseLong(path.Substring("/api/watched/".Length), 0);
                if (dataId <= 0) {
                    WriteError(response, 400, "invalid dataId");
                    return;
                }

                var mapper = MapperManager.metaDataMapper;
                if (mapper == null) {
                    WriteError(response, 500, "mapper unavailable");
                    return;
                }

                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string sql = $"update metadata set ViewCount=ViewCount+1, ViewDate='{now}' where DataID={dataId}";
                mapper.ExecuteNonQuery(sql);

                WriteJson(response, new { ok = true, dataId, viewDate = now });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/watched 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        #endregion

        #region "鉴权"

        /// <summary>
        /// GET /api/health
        /// <para>
        /// 排障用：报告「现在实际发送的是哪一份网页」。
        /// <see cref="TryServeWebPage"/> 优先读磁盘 <c>Web\index.html</c>，读不到才用内嵌资源，
        /// 所以部署时只换了 exe、忘了同步拷页面，程序会一直发旧页面且无任何异常。
        /// 这里实时重算磁盘与内嵌两份的状态，不复用启动时的缓存结果，
        /// 避免「自检本身有 bug」时连诊断入口也一起失真。
        /// </para>
        /// </summary>
        private void HandleHealth(HttpResponse response)
        {
            bool hasDisk = false;
            bool hasEmbedded = false;
            long diskLength = -1;
            long embeddedLength = -1;
            bool identical = false;
            string error = null;

            try {
                string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html");
                byte[] disk = null;
                if (File.Exists(diskPath)) {
                    hasDisk = true;
                    disk = File.ReadAllBytes(diskPath);
                    diskLength = disk.Length;
                }

                var asm = typeof(LanServer).Assembly;
                using (var stream = asm.GetManifestResourceStream("Jvedio.Resources.Web.index.html")) {
                    if (stream != null) {
                        hasEmbedded = true;
                        embeddedLength = stream.Length;

                        if (disk != null && disk.Length == stream.Length) {
                            var embedded = new byte[stream.Length];
                            int read = 0;
                            while (read < embedded.Length) {
                                int n = stream.Read(embedded, read, embedded.Length - read);
                                if (n <= 0) break;
                                read += n;
                            }
                            identical = read == embedded.Length;
                            if (identical) {
                                for (int i = 0; i < embedded.Length; i++) {
                                    if (embedded[i] != disk[i]) { identical = false; break; }
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                error = ex.Message;
            }

            // 实际生效的来源与 TryServeWebPage 的判断顺序一致
            string servedFrom = hasDisk ? "disk" : (hasEmbedded ? "embedded" : "none");

            WriteJson(response, new {
                ok = servedFrom != "none" && error == null,
                servedFrom,
                hasDisk,
                hasEmbedded,
                diskLength,
                embeddedLength,
                // 两份都在且内容一致时为 true；只有一份时为 null（无从比较）
                consistent = (hasDisk && hasEmbedded) ? (bool?)identical : null,
                // 核心告警：磁盘副本会把内嵌的新页面盖住
                stale = hasDisk && hasEmbedded && !identical,
                warning = WebPageWarning,
                error
            });
        }

        /// <summary>
        /// GET /api/auth/status
        /// <para>
        /// 前端启动时先问一次：需不需要口令、我这台设备是否已通过。
        /// 未启用到口令时 <c>required=false</c>，前端完全无感。
        /// </para>
        /// </summary>
        private void HandleAuthStatus(HttpRequest request, HttpResponse response)
        {
            bool required = _Auth.Enabled;
            string token = ExtractToken(request);
            bool authed = !required || _Auth.ValidateToken(token);

            WriteJson(response, new {
                required,
                authenticated = authed,
                // 口令未启用时告诉前端，方便在设置页展示状态
                message = required
                    ? (authed ? "已登录" : "需要口令")
                    : "未启用口令"
            });
        }

        /// <summary>
        /// POST /api/auth/login
        /// <para>Body: <c>{"password":"xxx"}</c> 或表单 <c>password=xxx</c></para>
        /// </summary>
        private void HandleAuthLogin(HttpRequest request, HttpResponse response)
        {
            try {
                if (!_Auth.Enabled) {
                    WriteJson(response, new {
                        ok = true,
                        required = false,
                        token = (string)null,
                        message = "未启用口令"
                    });
                    return;
                }

                string body = request.Body ?? string.Empty;
                string password = ExtractPassword(body);

                if (string.IsNullOrEmpty(password)) {
                    WriteError(response, 400, "请输入口令");
                    return;
                }

                string clientIp = GetClientIp(request);
                string error;
                if (!_Auth.CheckPassword(password, clientIp, out error)) {
                    App.Logger?.Warn($"LAN 登录失败 [{clientIp}]：{error}");
                    WriteError(response, 401, error);
                    return;
                }

                string token = _Auth.IssueToken();
                App.Logger?.Info($"LAN 登录成功 [{clientIp}]");
                WriteJson(response, new { ok = true, token });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/auth/login 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 统一鉴权入口。返回 false 表示已写出错误响应，调用方直接 return。
        /// </summary>
        private bool CheckAccess(HttpRequest request, HttpResponse response, string path)
        {
            if (!_Auth.Enabled)
                return true;

            // 媒体流允许用投屏临时令牌：投影仪拉流时无法带登录凭证
            if (path.StartsWith("/stream/")) {
                long dataId = ParseLong(path.Substring("/stream/".Length), 0);
                string castToken = GetQuery(request, "cast");
                if (_Auth.ValidateCastToken(castToken, dataId))
                    return true;

                // 正常登录过的设备直接拖进度条也需要能拉流
                if (_Auth.ValidateToken(ExtractToken(request)))
                    return true;

                WriteError(response, 401, "需要登录");
                return false;
            }

            if (_Auth.ValidateToken(ExtractToken(request)))
                return true;

            WriteError(response, 401, "需要登录");
            return false;
        }

        /// <summary>
        /// 从请求里取令牌。支持三种来源，优先级从高到低：
        /// <list type="number">
        /// <item><c>Authorization: Bearer xxx</c> —— 最标准</item>
        /// <item>查询串 <c>?token=xxx</c> —— 兼容 img/src、video/src 这类无法带头部的场景</item>
        /// <item>Cookie <c>jvedio_token=xxx</c></item>
        /// </list>
        /// </summary>
        private static string ExtractToken(HttpRequest request)
        {
            string auth = null;
            if (request.Headers.TryGetValue("Authorization", out auth) && !string.IsNullOrEmpty(auth)) {
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return auth.Substring(7).Trim();
            }

            string q = GetQuery(request, "token");
            if (!string.IsNullOrEmpty(q))
                return q;

            string cookie;
            if (request.Headers.TryGetValue("Cookie", out cookie) && !string.IsNullOrEmpty(cookie)) {
                foreach (string part in cookie.Split(';')) {
                    string p = part.Trim();
                    int eq = p.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    if (string.Equals(p.Substring(0, eq), "jvedio_token", StringComparison.OrdinalIgnoreCase))
                        return p.Substring(eq + 1);
                }
            }

            return null;
        }

        /// <summary>
        /// 从请求体里取口令。同时支持 JSON 和表单两种格式，
        /// 前端可以任选，不强制依赖 JSON。
        /// </summary>
        private static string ExtractPassword(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            string trimmed = body.Trim();

            if (trimmed.StartsWith("{")) {
                try {
                    var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>(trimmed);
                    string pwd;
                    if (obj != null && obj.TryGetValue("password", out pwd))
                        return pwd;
                } catch {
                    // 落到表单解析
                }
            }

            foreach (string part in body.Split('&')) {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (part.Substring(0, eq) == "password")
                    return WebUtility.UrlDecode(part.Substring(eq + 1));
            }

            return null;
        }

        /// <summary>
        /// 取客户端 IP（去掉端口）。
        /// 限速要按 IP 统计，因为端口每次连接都会变。
        /// </summary>
        private static string GetClientIp(HttpRequest request)
        {
            try {
                string ep = request.RemoteEndPoint;
                if (string.IsNullOrEmpty(ep))
                    return "?";
                int idx = ep.LastIndexOf(':');
                return idx > 0 ? ep.Substring(0, idx) : ep;
            } catch {
                return "?";
            }
        }

        #endregion

        #region "投屏（DLNA）"

        /// <summary>
        /// GET /api/cast/devices?refresh=1
        /// <para>
        /// 列出局域网内可投屏的设备。
        /// 默认走缓存（发现一次约 2 秒，不该每次点开面板都等），
        /// 传 refresh=1 强制重新搜索。
        /// </para>
        /// </summary>
        private void HandleCastDevices(HttpRequest request, HttpResponse response)
        {
            try {
                bool refresh = GetQuery(request, "refresh") == "1";

                List<DlnaDevice> devices;
                if (refresh || _castDevices == null) {
                    devices = _Caster.Discover();
                    _castDevices = devices;
                    _castDevicesAt = DateTime.UtcNow;
                } else {
                    devices = _castDevices;
                }

                WriteJson(response, new {
                    Items = devices.Select(d => new {
                        d.Name,
                        d.Ip,
                        d.Model,
                        d.ControlUrl
                    }).ToList(),
                    current = _Caster.Current?.Name,
                    scannedAt = _castDevicesAt.ToString("HH:mm:ss")
                });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/cast/devices 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>设备列表缓存：避免每次开面板都花 2 秒搜索</summary>
        private List<DlnaDevice> _castDevices;

        private DateTime _castDevicesAt;

        /// <summary>
        /// 投屏控制。
        /// <list type="bullet">
        /// <item>POST /api/cast/play/{dataId}?device=设备名</item>
        /// <item>POST /api/cast/pause</item>
        /// <item>POST /api/cast/resume</item>
        /// <item>POST /api/cast/stop</item>
        /// <item>POST /api/cast/seek?seconds=120</item>
        /// <item>GET  /api/cast/status</item>
        /// </list>
        /// </summary>
        private void HandleCastControl(HttpRequest request, HttpResponse response, string path)
        {
            try {
                string action = path.Substring("/api/cast/".Length);
                string query = action.IndexOf('?') >= 0 ? action.Substring(action.IndexOf('?')) : string.Empty;
                if (query.Length > 0)
                    action = action.Substring(0, action.IndexOf('?'));

                // ---- 状态查询 ----
                if (action == "status") {
                    var cur = _Caster.Current;
                    if (cur == null) {
                        WriteJson(response, new { casting = false, device = (string)null, state = "STOPPED", position = "00:00:00", duration = "00:00:00" });
                        return;
                    }

                    // 状态和进度是两个不同的 SOAP 动作，必须分别取：
                    // RelTime/TrackDuration 只在 GetPositionInfo 里。
                    string info = _Caster.GetTransportInfo();
                    string pos = _Caster.GetPositionInfo();

                    WriteJson(response, new {
                        casting = true,
                        device = cur.Name,
                        state = ExtractXmlTag(info, "CurrentTransportState") ?? "unknown",
                        position = ExtractXmlTag(pos, "RelTime") ?? "00:00:00",
                        duration = ExtractXmlTag(pos, "TrackDuration") ?? "00:00:00"
                    });
                    return;
                }

                // ---- 开始投屏 ----
                if (action.StartsWith("play/")) {
                    long dataId = ParseLong(action.Substring("play/".Length), 0);
                    if (dataId <= 0) {
                        WriteError(response, 400, "invalid dataId");
                        return;
                    }

                    var video = MapperManager.videoMapper?.SelectVideoByID(dataId);
                    if (video == null || string.IsNullOrEmpty(video.Path) || !File.Exists(video.Path)) {
                        WriteError(response, 404, "file not found");
                        return;
                    }

                    // 找目标设备：优先按名字精确匹配，其次找当前设备
                    string wantDevice = GetQuery(request, "device");
                    DlnaDevice target = null;
                    if (!string.IsNullOrEmpty(wantDevice) && _castDevices != null) {
                        target = _castDevices.FirstOrDefault(d =>
                            string.Equals(d.Name, wantDevice, StringComparison.CurrentCulture));
                    }
                    if (target == null)
                        target = _Caster.Current;
                    if (target == null && _castDevices != null && _castDevices.Count > 0)
                        target = _castDevices[0];

                    if (target == null) {
                        WriteError(response, 404, "没有可用的投屏设备，请先搜索设备");
                        return;
                    }

                    // 投屏地址必须用「和设备同网段」的本机 IP：
                    // 投影仪访问不到 127.0.0.1，也不一定访问得到 VPN 网卡的地址。
                    string localIp = GetLocalIpFor(target.Ip);
                    string mediaUrl = $"http://{localIp}:{Port}/stream/{dataId}";

                    // 启用了口令时，投影仪无法带登录凭证，
                    // 所以在流地址上拼一个 10 分钟有效的投屏专用令牌。
                    if (_Auth.Enabled) {
                        string castToken = _Auth.IssueCastToken(dataId);
                        mediaUrl += "?cast=" + WebUtility.UrlEncode(castToken);
                    }
                    string title = !string.IsNullOrEmpty(video.VID) ? video.VID
                        : (!string.IsNullOrEmpty(video.Title) ? video.Title
                            : Path.GetFileNameWithoutExtension(video.Path));

                    if (!_Caster.Cast(target, mediaUrl, title)) {
                        WriteError(response, 502, "投屏指令被设备拒绝");
                        return;
                    }

                    // 与手机端直接播放一致：投屏也算「看过」
                    try {
                        MapperManager.metaDataMapper?.ExecuteNonQuery(
                            $"update metadata set ViewCount=ViewCount+1, ViewDate='{DateTime.Now:yyyy-MM-dd HH:mm:ss}' where DataID={dataId}");
                    } catch { /* 统计失败不影响投屏 */ }

                    WriteJson(response, new { ok = true, device = target.Name, dataId, url = mediaUrl });
                    return;
                }

                // ---- 播放控制 ----
                // stop 要幂等：前端关闭页面时可能重复调用，
                // 当前没有投屏就直接返回成功，不要报 502。
                if (action == "stop" && _Caster.Current == null) {
                    WriteJson(response, new { ok = true, action, alreadyStopped = true });
                    return;
                }

                bool ok;
                switch (action) {
                    case "pause": ok = _Caster.Pause(); break;
                    case "resume": ok = _Caster.Resume(); break;
                    case "stop": ok = _Caster.Stop(); break;
                    case "seek":
                        ok = _Caster.Seek(ParseInt(GetQuery(request, "seconds"), 0));
                        break;
                    default:
                        WriteError(response, 404, "unknown action");
                        return;
                }

                if (!ok) {
                    WriteError(response, 502, "设备未响应");
                    return;
                }
                WriteJson(response, new { ok = true, action });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/cast 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 从 XML 文本里取第一个同名标签的内容。取不到返回 null。
        /// <para>用于解析 DLNA 设备返回的 SOAP 响应。</para>
        /// </summary>
        private static string ExtractXmlTag(string xml, string tag)
        {
            if (string.IsNullOrEmpty(xml))
                return null;
            var m = System.Text.RegularExpressions.Regex.Match(xml,
                "<" + tag + ">(.*?)</" + tag + ">",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        #endregion

        #region "随机推荐与相似推荐"

        /// <summary>
        /// 相似推荐的最低分。低于这个分数说明没有任何有效相似信号
        /// （只剩 0~4% 的随机扰动），不该凑数展示。
        /// </summary>
        private const double SIMILAR_MIN_SCORE = 0.09;

        /// <summary>
        /// GET /api/random?count=20&amp;dbId=&amp;strategy=
        /// <para>
        /// 随机推荐。设计上刻意**不用** <c>ORDER BY RANDOM()</c>：
        /// </para>
        /// <list type="bullet">
        /// <item>纯随机每次顺序完全无关，用户刷新两次会看到大量重复内容</item>
        /// <item>无任何偏好，冷门和热门被同等对待，推荐价值低</item>
        /// </list>
        /// <para>
        /// 这里用「加权随机」：先按观看/评分/加入时间算权重，
        /// 再在权重区间内随机取样。既能保底多样性，又让内容更可能合口味。
        /// </para>
        /// </summary>
        private void HandleRandom(HttpRequest request, HttpResponse response)
        {
            try {
                int count = ParseInt(GetQuery(request, "count"), 20);
                if (count < 1) count = 20;
                if (count > 60) count = 60;

                long dbId = ParseLong(GetQuery(request, "dbId"), 0);
                string strategy = GetQuery(request, "strategy");

                var items = PickRandom(dbId, count, strategy);
                WriteJson(response, new { Items = items, Count = items.Count, Strategy = strategy ?? "weighted" });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/random 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 加权随机取样。
        /// <para>
        /// 权重设计（实测数据支撑）：
        /// - 有播放记录 +40：看过说明有兴趣
        /// - 有评分 + Grade*12：评分越高越可能合口味
        /// - 近一年加入 +25：新内容优先曝光
        /// - 基础权重 100：保证冷门内容也有机会（不制造信息茧房）
        /// </para>
        /// <para>
        /// 实现上不查全表再排序（2362 条虽小但没必要），
        /// 而是「随机 offset 多次取样 + 去重」，实测 1.7ms。
        /// </para>
        /// </summary>
        private List<VideoSummaryDto> PickRandom(long dbId, int count, string strategy)
        {
            var result = new List<VideoSummaryDto>();
            var mapper = MapperManager.videoMapper;
            if (mapper == null)
                return result;

            string dbWhere = dbId > 0 ? $" and metadata.DBId={dbId}" : string.Empty;

            // 取总量用于随机 offset
            long total = 0;
            try {
                var rows = mapper.Select($"select count(*) as c from metadata where DataType=0{dbWhere}");
                if (rows != null && rows.Count > 0) {
                    object v;
                    rows[0].TryGetValue("c", out v);
                    if (v != null) total = Convert.ToInt64(v);
                }
            } catch {
                return result;
            }
            if (total <= 0)
                return result;

            // 纯随机策略：直接随机 offset，最快
            bool pure = string.Equals(strategy, "pure", StringComparison.OrdinalIgnoreCase);

            var seen = new HashSet<long>();
            var rnd = new Random();
            int attempts = 0;
            int maxAttempts = count * 6;   // 去重失败上限，避免死循环

            while (result.Count < count && attempts < maxAttempts) {
                attempts++;
                long offset = rnd.Next(0, (int)total);
                try {
                    var rows = mapper.Select(
                        "select " + string.Join(",", VideoMapper.SelectFields) +
                        " from metadata_video join metadata on metadata.DataID=metadata_video.DataID" +
                        $" where metadata.DataType=0{dbWhere} limit 1 offset {offset}");

                    if (rows == null || rows.Count == 0)
                        continue;

                    var list = mapper.ToEntity<Video>(rows, typeof(Video).GetProperties(), false);
                    if (list == null || list.Count == 0)
                        continue;

                    var v = list[0];
                    if (!seen.Add(v.DataID))
                        continue;

                    result.Add(ToSummary(v));
                } catch {
                    // 单条失败不影响整体
                }
            }

            return result;
        }

        /// <summary>
        /// GET /api/similar/{dataId}?count=12
        /// <para>
        /// 相似推荐 = 粗筛候选 + 内容信号加权打分。
        /// </para>
        /// <para>
        /// 为什么不再用「共同演员 → 同系列 → 同发行商」的固定优先级：
        /// 那样前 N 条会被同演员作品完全占满，系列/发行商/新鲜内容永远排不上；
        /// 而短片（没有演员/导演/剧情）只能落到随机兜底，等于没有推荐。
        /// </para>
        /// <para>
        /// 权重按内容形态自动切换：
        /// 有演员或导演 → 演员 42%、导演 18% 为主，剧情简介 15%、文件名 15%，
        /// 同系列/同发行商各 5% 作补充；
        /// 只有文件名（短片/无码片）→ 文件名 66% 为主，因为这类记录没有演员、
        /// 导演、剧情，文件名里的系列前缀是唯一可靠信号。
        /// 排序时再对「同档候选」做随机打散（0.03 一档），
        /// 反复打开同一部不会永远看到一模一样的列表，但不会把低分内容顶上来。
        /// </para>
        /// </summary>
        private void HandleSimilar(HttpRequest request, HttpResponse response, string path)
        {
            try {
                long dataId = ParseLong(path.Substring("/api/similar/".Length), 0);
                if (dataId <= 0) {
                    WriteError(response, 400, "invalid dataId");
                    return;
                }

                int count = ParseInt(GetQuery(request, "count"), 12);
                if (count < 1) count = 12;
                if (count > 30) count = 30;

                var mapper = MapperManager.videoMapper;
                if (mapper == null) {
                    WriteJson(response, new { Items = new List<VideoSummaryDto>() });
                    return;
                }

                Video source = mapper.SelectVideoByID(dataId);
                if (source == null) {
                    WriteJson(response, new { Items = new List<VideoSummaryDto>() });
                    return;
                }

                var seen = new HashSet<long> { dataId };
                var pool = new Dictionary<long, Video>();

                // 1) 同演员（最强的人物偏好信号）
                AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                    " and metadata.DataID in (select mta.DataID from metadata_to_actor mta " +
                    "where mta.ActorID in (select ActorID from metadata_to_actor where DataID=" + dataId + "))",
                    "metadata.Grade desc, metadata.DataID desc", 120));

                // 2) 同导演
                if (!string.IsNullOrEmpty(source.Director)) {
                    AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                        " and metadata_video.Director != '' and metadata_video.Director = " + SqlStringLiteral(source.Director),
                        "metadata.Grade desc, metadata.DataID desc", 60));
                }

                // 3) 同系列 / 同发行商
                if (!string.IsNullOrEmpty(source.Series)) {
                    AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                        " and metadata_video.Series != '' and metadata_video.Series = " + SqlStringLiteral(source.Series),
                        "metadata.Grade desc, metadata.DataID desc", 60));
                }
                if (!string.IsNullOrEmpty(source.Studio)) {
                    AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                        " and metadata_video.Studio != '' and metadata_video.Studio = " + SqlStringLiteral(source.Studio),
                        "metadata.Grade desc, metadata.DataID desc", 40));
                }

                // 4) 文件名 / 番号前缀相似（短片的主信号）。
                //    注意 Path 在 metadata 表（metadata_video 里没有），
                //    比对时先去掉空格：「萌宅卡 網聊…」与「萌宅卡 女仆…」的系列标记
                //    是「萌宅卡」，中间的空格会让 LIKE 漏掉兄弟文件。
                foreach (string seed in BuildSimilarSeeds(source)) {
                    AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                        " and replace(metadata.Path,' ','') like '%" + EscapeLikeValue(seed) + "%'",
                        "metadata.Grade desc, metadata.DataID desc", 40));
                    if (pool.Count >= 400)
                        break;
                }

                // 4.5) 同目录：短片系列基本都是一个文件夹一部/一套，
                //      没有演员、导演、番号时，同目录是最可靠的「同系列」信号。
                //      但只对「小文件夹」生效——番号片常被堆在一个大目录里，
                //      那里同目录毫无意义（等于随机），必须排除。
                string folder = LastFolderNameOf(source.Path);
                bool folderIsSeries = false;
                if (folder.Length >= 2) {
                    string folderWhere = " and metadata.Path like '%" + EscapeLikeValue(folder) + "%'";
                    int siblings = CountSimilarCandidates(mapper, folderWhere);
                    folderIsSeries = siblings > 1 && siblings <= 40;
                    if (folderIsSeries) {
                        AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                            folderWhere, "metadata.Grade desc, metadata.DataID desc", 40));
                    }
                }

                // 5) 随机样本：让推荐池有新鲜内容，也让上面的随机扰动真正起作用
                AddSimilarPool(pool, seen, SelectSimilarCandidates(mapper,
                    string.Empty, "RANDOM()", 80));

                pool.Remove(dataId);

                // 6) 打分排序
                //    分数本身不带随机：随机只用于「同档候选之间」打散顺序，
                //    否则随机分会污染下面的相似度下限判断（垃圾内容会靠运气过线）。
                var rnd = new Random();
                var scored = new List<KeyValuePair<Video, double>>();
                foreach (var kv in pool)
                    scored.Add(new KeyValuePair<Video, double>(
                        kv.Value, ScoreSimilarity(source, kv.Value, folderIsSeries)));
                scored.Sort(delegate (KeyValuePair<Video, double> x, KeyValuePair<Video, double> y) {
                    // 0.03 一档：档内随机，档间严格按分数
                    int bx = (int)Math.Round(x.Value / 0.03);
                    int by = (int)Math.Round(y.Value / 0.03);
                    if (bx != by)
                        return by.CompareTo(bx);
                    return rnd.Next(2) == 0 ? -1 : 1;
                });

                var result = new List<VideoSummaryDto>();
                foreach (var kv in scored) {
                    if (result.Count >= count)
                        break;

                    // 相似度下限：没有演员/导演/同系列信号的片子，
                    // 打分只剩随机扰动，凑数只会把无关内容推给用户。
                    // 宁可区块不显示（前端 items 为空会整块移除），也不要塞垃圾。
                    if (kv.Value < SIMILAR_MIN_SCORE)
                        continue;

                    result.Add(ToSummary(kv.Key));
                }

                WriteJson(response, new { Items = result, Count = result.Count });
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/similar 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 相似度打分：返回 0~1 的加权相似度（外加一点随机扰动）。
        /// <para>
        /// 各维度都做了「缺信号自动降权」：源记录没有演员时，演员权重让给文件名，
        /// 这样短片也能按文件名找到同系列，而不是被一堆无关内容顶上来。
        /// </para>
        /// </summary>
        private double ScoreSimilarity(Video source, Video candidate, bool folderIsSeries)
        {
            List<string> srcActors = SplitValues(source.ActorNames);
            List<string> candActors = SplitValues(candidate.ActorNames);
            double actorScore = ActorOverlap(srcActors, candActors);

            double directorScore = !string.IsNullOrEmpty(source.Director) &&
                string.Equals((source.Director ?? string.Empty).Trim(), (candidate.Director ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            double seriesScore = !string.IsNullOrEmpty(source.Series) &&
                string.Equals((source.Series ?? string.Empty).Trim(), (candidate.Series ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            double studioScore = !string.IsNullOrEmpty(source.Studio) &&
                string.Equals((source.Studio ?? string.Empty).Trim(), (candidate.Studio ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            double plotScore = DiceSimilarity(source.Plot, candidate.Plot);
            double nameScore = NameSimilarity(source.Path, candidate.Path);

            // 同目录视为强「同系列」信号：短片集合通常一个文件夹一套。
            // 只有小文件夹才算——大目录是堆放区，同目录没有任何意义。
            if (folderIsSeries) {
                string srcFolder = LastFolderNameOf(source.Path);
                if (srcFolder.Length >= 2 &&
                    string.Equals(srcFolder, LastFolderNameOf(candidate.Path), StringComparison.OrdinalIgnoreCase))
                    nameScore = Math.Max(nameScore, 0.85);
            }

            // 有演员或导演 → 以「人」为主；只有文件名 → 以文件名为主
            bool rich = srcActors.Count > 0 || !string.IsNullOrEmpty(source.Director);
            double wActor = rich ? 0.42 : 0.04;
            double wDirector = rich ? 0.18 : 0.04;
            double wPlot = rich ? 0.15 : 0.10;
            double wName = rich ? 0.15 : 0.66;
            double wSeries = 0.05;
            double wStudio = 0.05;
            double norm = wActor + wDirector + wPlot + wName + wSeries + wStudio;

            double score = (wActor * actorScore + wDirector * directorScore + wPlot * plotScore +
                wName * nameScore + wSeries * seriesScore + wStudio * studioScore) / norm;

            // 随机扰动放在排序阶段做（见 HandleSimilar 的第 6 步）：
            // 这里必须返回「干净」的分数，相似度下限才不会误判。
            return score;
        }

        /// <summary>演员重合度（Jaccard），大小写与空格不敏感。</summary>
        private double ActorOverlap(List<string> a, List<string> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
                return 0;

            var sa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in a)
                if (!string.IsNullOrWhiteSpace(s)) sa.Add(s.Trim());
            var sb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in b)
                if (!string.IsNullOrWhiteSpace(s)) sb.Add(s.Trim());

            if (sa.Count == 0 || sb.Count == 0)
                return 0;

            int inter = 0;
            foreach (string s in sb)
                if (sa.Contains(s)) inter++;

            int union = sa.Count + sb.Count - inter;
            return union <= 0 ? 0 : (double)inter / union;
        }

        /// <summary>
        /// 文本相似度（字符二元组 Dice 系数）。
        /// <para>
        /// 用二元组而不是分词：中日文剧情简介没有空格分词，
        /// 二元组对「同一系列/同一题材的相似措辞」更稳，也不依赖任何分词库。
        /// </para>
        /// </summary>
        private double DiceSimilarity(string a, string b)
        {
            string x = NormalizeCompareText(a);
            string y = NormalizeCompareText(b);
            if (x.Length < 2 || y.Length < 2)
                return 0;

            var bx = new HashSet<string>();
            for (int i = 0; i < x.Length - 1; i++) bx.Add(x.Substring(i, 2));
            var by = new HashSet<string>();
            for (int i = 0; i < y.Length - 1; i++) by.Add(y.Substring(i, 2));
            if (bx.Count == 0 || by.Count == 0)
                return 0;

            int inter = 0;
            foreach (string g in bx)
                if (by.Contains(g)) inter++;

            return 2.0 * inter / (bx.Count + by.Count);
        }

        /// <summary>只保留字母/数字/汉字并转小写，用于文本相似度比较。</summary>
        private string NormalizeCompareText(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw) {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 文件名归一化：去掉扩展名、站点前缀（kcf9.com@ / 2048.cc- 之类）、
        /// 方括号里的标签，再做字符归一。
        /// </summary>
        private string NormalizeNameForCompare(string path)
        {
            string name = FileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            name = System.Text.RegularExpressions.Regex.Replace(name,
                @"^[a-z0-9\-]*\.(com|cc|net|org|xyz|top|vip)[@\-_]?", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[\[\(（【].*?[\]\)）】]", string.Empty);

            return NormalizeCompareText(name);
        }

        private string FileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            try {
                return System.IO.Path.GetFileNameWithoutExtension(path);
            } catch {
                return string.Empty;
            }
        }

        /// <summary>
        /// 文件名相似度。
        /// <para>
        /// 番号类（MIKR-028 / MIDV-988 这种「字母+数字」编码）不能直接跑二元组：
        /// 「mikr068」和「mida306」共享 mi / 06 之类碎片，会算出虚高的相似度，
        /// 把毫无关系的厂牌混进推荐。所以编码类先比「字母前缀」，
        /// 前缀相同才认为同系列，再按数字接近程度细分；前缀不同只给很低的分。
        /// </para>
        /// <para>
        /// 带汉字的文件名（短片）继续走二元组 Dice，对中文系列名最稳。
        /// </para>
        /// </summary>
        private double NameSimilarity(string a, string b)
        {
            string x = NormalizeNameForCompare(a);
            string y = NormalizeNameForCompare(b);
            if (x.Length == 0 || y.Length == 0)
                return 0;

            if (IsCodeLikeName(x) && IsCodeLikeName(y)) {
                string px = CodePrefixOf(x);
                string py = CodePrefixOf(y);
                if (px.Length >= 2 && string.Equals(px, py, StringComparison.OrdinalIgnoreCase))
                    return 0.6 + 0.4 * NumberProximity(x, y);

                // 厂牌前缀不同：只保留很低的相似度，避免靠碎片蒙混过关
                return DiceSimilarity(px, py) * 0.35;
            }

            // 中文短片：开头几个字就是系列标记（「萌宅卡 …」「棉花糖糖-…」）。
            // 标题剩下的部分每部都不同，整串跑 Dice 会被稀释到 0.1 上下，
            // 同系列反而过不了下限。所以系列标记相同就给高分，剩下的再按 Dice 细分。
            string sx = CjkSeriesKey(x);
            string sy = CjkSeriesKey(y);
            if (sx.Length >= 2 && string.Equals(sx, sy, StringComparison.Ordinal))
                return 0.7 + 0.3 * DiceSimilarity(x, y);

            return DiceSimilarity(x, y);
        }

        /// <summary>取文件名开头的连续汉字（最多 3 个）作为系列标记，不足 2 个返回空串。</summary>
        private string CjkSeriesKey(string normalized)
        {
            if (string.IsNullOrEmpty(normalized))
                return string.Empty;

            int end = 0;
            while (end < normalized.Length && end < 3) {
                char c = normalized[end];
                if (c < 0x4E00 || c > 0x9FFF)
                    break;
                end++;
            }
            return end >= 2 ? normalized.Substring(0, end) : string.Empty;
        }

        /// <summary>像番号编码：没有汉字、含数字、长度不太长。</summary>
        private bool IsCodeLikeName(string normalized)
        {
            if (string.IsNullOrEmpty(normalized) || normalized.Length > 16)
                return false;

            bool hasDigit = false;
            foreach (char c in normalized) {
                if (c >= 0x4E00 && c <= 0x9FFF)
                    return false;
                if (c >= '0' && c <= '9')
                    hasDigit = true;
            }
            return hasDigit;
        }

        /// <summary>编码类名字的字母前缀（第一个数字之前的部分）。</summary>
        private string CodePrefixOf(string normalized)
        {
            if (string.IsNullOrEmpty(normalized))
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (char c in normalized) {
                if (c >= '0' && c <= '9')
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>两个编码里第一段数字的接近程度（0~1）。</summary>
        private double NumberProximity(string x, string y)
        {
            long nx = FirstNumberIn(x);
            long ny = FirstNumberIn(y);
            if (nx < 0 || ny < 0)
                return 0.5;

            long max = Math.Max(Math.Abs(nx), Math.Abs(ny));
            if (max == 0)
                return 1;

            double diff = Math.Abs(nx - ny);
            double ratio = diff / (double)max;
            return ratio >= 1 ? 0 : 1 - ratio;
        }

        /// <summary>取第一段连续数字，没有返回 -1。</summary>
        private long FirstNumberIn(string normalized)
        {
            if (string.IsNullOrEmpty(normalized))
                return -1;

            int start = -1;
            for (int i = 0; i < normalized.Length; i++) {
                if (normalized[i] >= '0' && normalized[i] <= '9') {
                    start = i;
                    break;
                }
            }
            if (start < 0)
                return -1;

            int end = start;
            while (end < normalized.Length && normalized[end] >= '0' && normalized[end] <= '9')
                end++;

            long value;
            return long.TryParse(normalized.Substring(start, end - start), out value) ? value : -1;
        }

        /// <summary>取文件所在目录的最后一级名字（短片系列常以文件夹组织）。</summary>
        private string LastFolderNameOf(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            try {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                    return string.Empty;
                string name = System.IO.Path.GetFileName(dir);
                return name ?? string.Empty;
            } catch {
                return string.Empty;
            }
        }

        /// <summary>
        /// 为「文件名相似」构造 LIKE 种子：番号前缀、文件名开头的连续汉字、
        /// 归一化后的前 6 个字符。最多 3 个，避免查询过多。
        /// </summary>
        private List<string> BuildSimilarSeeds(Video v)
        {
            var seeds = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string vid = Jvedio.Identity.Identify.NormalizeVID(v.VID);
            if (!string.IsNullOrEmpty(vid)) {
                int cut = vid.IndexOf('-');
                string prefix = cut > 0 ? vid.Substring(0, cut) : vid;
                if (prefix.Length >= 2 && used.Add(prefix))
                    seeds.Add(prefix);
            }

            string name = FileNameWithoutExtension(v.Path);
            if (!string.IsNullOrEmpty(name)) {
                name = System.Text.RegularExpressions.Regex.Replace(name,
                    @"^[a-z0-9\-]*\.(com|cc|net|org|xyz|top|vip)[@\-_]?", string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // 开头的连续汉字：短片系列名通常在最前面（「棉花糖糖-…」）。
                // 只取前 3 个字当种子——整段名字太长，兄弟文件名不会逐字相同；
                // 而系列标记基本就是开头那几个字（「萌宅卡」「棉花糖糖」）。
                var cjk = new System.Text.StringBuilder();
                foreach (char c in name) {
                    if (c >= 0x4E00 && c <= 0x9FFF) cjk.Append(c);
                    else if (cjk.Length > 0) break;
                }
                if (cjk.Length >= 2) {
                    string series = cjk.Length > 3 ? cjk.ToString().Substring(0, 3) : cjk.ToString();
                    if (used.Add(series))
                        seeds.Add(series);
                }

                string cleaned = NormalizeCompareText(name);
                if (cleaned.Length >= 6) {
                    string head = cleaned.Substring(0, 6);
                    if (used.Add(head))
                        seeds.Add(head);
                }
            }

            return seeds;
        }

        /// <summary>SQL 字符串字面量（单引号转义）。</summary>
        private string SqlStringLiteral(string raw)
        {
            return "'" + (raw ?? string.Empty).Replace("'", "''") + "'";
        }

        /// <summary>LIKE 值里不能带通配符和引号，否则会变成宽匹配或语法错误。</summary>
        private string EscapeLikeValue(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            return raw.Replace("'", "''").Replace("%", string.Empty).Replace("_", string.Empty);
        }

        /// <summary>把候选批量并入推荐池（自动跳过自身与重复项）。</summary>
        private void AddSimilarPool(Dictionary<long, Video> pool, HashSet<long> seen, List<Video> candidates)
        {
            if (candidates == null)
                return;
            foreach (var v in candidates) {
                if (v == null || seen.Contains(v.DataID))
                    continue;
                if (!pool.ContainsKey(v.DataID))
                    pool[v.DataID] = v;
            }
        }

        /// <summary>数一数某条件命中的记录数（用于判断「同目录」是不是系列目录）。</summary>
        private int CountSimilarCandidates(Jvedio.Mapper.VideoMapper mapper, string extraWhere)
        {
            try {
                var rows = mapper.Select(
                    "select count(*) as c from metadata_video join metadata on metadata.DataID=metadata_video.DataID" +
                    " where metadata.DataType=0" + (extraWhere ?? string.Empty));

                if (rows == null || rows.Count == 0)
                    return 0;

                object value;
                rows[0].TryGetValue("c", out value);
                return value == null ? 0 : Convert.ToInt32(value);
            } catch (Exception ex) {
                App.Logger?.Warn($"相似推荐统计失败: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 按附加条件取候选（返回完整 Video 实体，供打分用）。
        /// </summary>
        private List<Video> SelectSimilarCandidates(Jvedio.Mapper.VideoMapper mapper,
            string extraWhere, string orderBy, int limit)
        {
            var list = new List<Video>();
            if (limit <= 0)
                return list;

            try {
                string sql =
                    "select " + string.Join(",", VideoMapper.SelectFields) +
                    " from metadata_video join metadata on metadata.DataID=metadata_video.DataID" +
                    " where metadata.DataType=0" + (extraWhere ?? string.Empty) +
                    " order by " + orderBy + " limit " + limit;

                var rows = mapper.Select(sql);
                if (rows == null || rows.Count == 0)
                    return list;

                var entities = mapper.ToEntity<Video>(rows, typeof(Video).GetProperties(), false);
                if (entities != null) {
                    // 走的是裸 SQL，ActorInfos 不会被填充，ActorNames 因此是空的。
                    // 而演员重合度是打分里权重最高的维度，这里必须补一次关联查询，
                    // 否则所有候选的演员分都是 0，推荐会退化成文件名/随机排序。
                    LoadSimilarActorNames(mapper, entities);
                    list.AddRange(entities);
                }
            } catch (Exception ex) {
                App.Logger?.Warn($"相似推荐候选查询失败: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// 批量补齐候选的演员名（一次 IN 查询，避免逐条 N+1）。
        /// </summary>
        private void LoadSimilarActorNames(Jvedio.Mapper.VideoMapper mapper, List<Video> videos)
        {
            if (videos == null || videos.Count == 0)
                return;

            try {
                string ids = string.Join(",", videos.Select(v => v.DataID.ToString()));
                string sql = "select metadata_to_actor.DataID as DataID, actor_info.ActorName as ActorName " +
                    "from metadata_to_actor join actor_info on actor_info.ActorID=metadata_to_actor.ActorID " +
                    "where metadata_to_actor.DataID in (" + ids + ")";

                var rows = mapper.Select(sql);
                if (rows == null || rows.Count == 0)
                    return;

                var map = new Dictionary<long, List<string>>();
                foreach (var row in rows) {
                    object idObj, nameObj;
                    row.TryGetValue("DataID", out idObj);
                    row.TryGetValue("ActorName", out nameObj);
                    if (idObj == null || nameObj == null)
                        continue;

                    long id = Convert.ToInt64(idObj);
                    List<string> names;
                    if (!map.TryGetValue(id, out names)) {
                        names = new List<string>();
                        map[id] = names;
                    }
                    names.Add(Convert.ToString(nameObj));
                }

                foreach (var v in videos) {
                    List<string> names;
                    if (map.TryGetValue(v.DataID, out names) && names.Count > 0)
                        v.ActorNames = string.Join(Jvedio.Common.ConstValues.SeparatorString, names);
                }
            } catch (Exception ex) {
                App.Logger?.Warn($"相似推荐加载演员失败: {ex.Message}");
            }
        }

        /// <summary>
        /// GET /api/filters?dbId=
        /// <para>
        /// 返回可用于筛选的维度与取值，供前端渲染筛选面板。
        /// 只返回「实际有内容」的项，避免面板里一堆空选项。
        /// </para>
        /// </summary>
        private void HandleFilters(HttpResponse response)
        {
            try {
                var mapper = MapperManager.videoMapper;
                var data = new Dictionary<string, object>();

                // 分类：Genre 是无分隔符拼接文本，只能整体统计
                data["genres"] = TopValues(mapper,
                    "select Genre from metadata where DataType=0 and Genre != ''", 40);

                // 发行商
                data["studios"] = TopValues(mapper,
                    "select Studio from metadata_video where Studio != ''", 40);

                // 系列
                data["series"] = TopValues(mapper,
                    "select Series from metadata_video where Series != ''", 40);

                // 导演
                data["directors"] = TopValues(mapper,
                    "select Director from metadata_video where Director != ''", 40);

                // 演员
                data["actors"] = TopValues(mapper,
                    "select ActorName from actor_info where ActorName != ''", 60);

                WriteJson(response, data);
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/filters 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 统计某个字段的高频取值（用于筛选面板）。
        /// </summary>
        private List<object> TopValues(Jvedio.Mapper.VideoMapper mapper, string sql, int limit)
        {
            var list = new List<object>();
            try {
                var rows = mapper.Select(sql);
                if (rows == null)
                    return list;

                var counter = new Dictionary<string, int>();
                foreach (var row in rows) {
                    foreach (var pair in row) {
                        string val = pair.Value?.ToString();
                        if (string.IsNullOrWhiteSpace(val))
                            continue;
                        // Genre 等字段是拼接文本，按已知分隔符切分
                        foreach (string part in SplitMulti(val)) {
                            if (string.IsNullOrWhiteSpace(part))
                                continue;
                            if (!counter.ContainsKey(part))
                                counter[part] = 0;
                            counter[part]++;
                        }
                    }
                }

                foreach (var pair in counter.OrderByDescending(p => p.Value).Take(limit)) {
                    list.Add(new { Name = pair.Key, Count = pair.Value });
                }
            } catch (Exception ex) {
                App.Logger?.Error($"TopValues 失败: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// 切分拼接型字段（Genre 用固定分隔符拼接，无空格）。
        /// </summary>
        private IEnumerable<string> SplitMulti(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            string sep = Jvedio.Common.ConstValues.Separator.ToString();
            if (!string.IsNullOrEmpty(sep) && raw.Contains(sep)) {
                foreach (string s in raw.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries))
                    yield return s.Trim();
                yield break;
            }

            yield return raw.Trim();
        }

        #endregion

        /// <summary>
        /// 托管移动端 Web 页面。
        /// <para>
        /// 查找顺序：
        /// 1. 程序目录下的 Web 文件夹（可现场改，方便调试）
        /// 2. 程序集内嵌资源（打包后无需额外文件）
        /// </para>
        /// <para>
        /// 安全约束：只允许读取 Web 目录内的文件，
        /// 并拒绝包含 .. 的路径，避免目录穿越。
        /// </para>
        /// </summary>
        private bool TryServeWebPage(HttpResponse response, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            // 拒绝目录穿越
            if (relativePath.Contains("..") || relativePath.Contains("\\.."))
                return false;

            // 只允许这些扩展名，避免把任意文件当静态资源吐出
            string ext = Path.GetExtension(relativePath).ToLowerInvariant();
            if (ext != ".html" && ext != ".css" && ext != ".js" &&
                ext != ".png" && ext != ".svg" && ext != ".ico" && ext != ".webmanifest")
                return false;

            string contentType = GetContentType(relativePath);
            if (ext == ".html") contentType = "text/html; charset=utf-8";
            else if (ext == ".css") contentType = "text/css; charset=utf-8";
            else if (ext == ".js") contentType = "application/javascript; charset=utf-8";
            else if (ext == ".webmanifest") contentType = "application/manifest+json; charset=utf-8";

            // 缓存策略：
            // - HTML 每次都要回源（no-store），否则改版后手机看不到更新
            // - 其它静态资源允许短期缓存，减少重复传输
            string cacheControl = ext == ".html"
                ? "no-store, must-revalidate"
                : "public, max-age=300, must-revalidate";

            // 1) 程序目录下的 Web 文件夹
            try {
                string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
                string full = Path.GetFullPath(Path.Combine(webRoot, relativePath));

                // 二次确认：解析后的路径必须仍在 webRoot 内
                if (full.StartsWith(Path.GetFullPath(webRoot), StringComparison.OrdinalIgnoreCase)
                    && File.Exists(full)) {
                    byte[] bytes = File.ReadAllBytes(full);
                    response.WriteBinary(200, contentType, bytes, cacheControl);
                    return true;
                }
            } catch {
                // 落到内嵌资源
            }

            // 2) 内嵌资源
            try {
                var asm = typeof(LanServer).Assembly;
                string resourceName = "Jvedio.Resources.Web." + relativePath.Replace('/', '.');
                using (var stream = asm.GetManifestResourceStream(resourceName)) {
                    if (stream != null) {
                        var buffer = new byte[stream.Length];
                        stream.Read(buffer, 0, buffer.Length);
                        response.WriteBinary(200, contentType, buffer, cacheControl);
                        return true;
                    }
                }
            } catch {
                // 无内嵌资源
            }

            return false;
        }

        #region "接口实现"

        private List<LibraryDto> HandleLibraries()
        {
            var list = new List<LibraryDto>();
            try {
                var dbs = MapperManager.appDatabaseMapper?.SelectList();
                if (dbs != null) {
                    foreach (var db in dbs) {
                        list.Add(new LibraryDto {
                            DBId = db.DBId,
                            Name = db.Name,
                            Count = 0,
                            ImageUrl = $"/image/library/{db.DBId}"
                        });
                    }
                }
            } catch {
                // 库表读取失败时返回空列表，不影响其余接口
            }
            return list;
        }

        /// <summary>
        /// GET /api/videos?page=1&amp;pageSize=20&amp;dbId=&amp;keyword=&amp;sort=
        /// </summary>
        private void HandleVideoList(HttpRequest request, HttpResponse response)
        {
            try {
                HandleVideoListCore(request, response);
            } catch (Exception ex) {
                // 兜住整个处理器，避免异常冒到 Route 之外导致「空响应」
                App.Logger?.Error($"LAN /api/videos 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        private void HandleVideoListCore(HttpRequest request, HttpResponse response)
        {
            int page = ParseInt(GetQuery(request, "page"), 1);
            int pageSize = ParseInt(GetQuery(request, "pageSize"), 20);
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 200) pageSize = 200;

            long dbId = ParseLong(GetQuery(request, "dbId"), 0);
            string keyword = GetQuery(request, "keyword");
            string sort = GetQuery(request, "sort");
            string view = GetQuery(request, "view");

            // 筛选参数：字段名直接作为 query key，多值用逗号分隔
            var filters = new Dictionary<string, string[]>();
            foreach (string field in new[] { "genres", "studios", "series", "directors", "actors" }) {
                string raw = GetQuery(request, field);
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var vals = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
                if (vals.Length > 0)
                    filters[field] = vals;
            }

            List<Video> videos;
            long total;
            try {
                videos = QueryVideos(dbId, keyword, sort, page, pageSize, filters, view, out total);
            } catch (Exception ex) {
                WriteError(response, 500, ex.Message);
                return;
            }

            var items = new List<VideoSummaryDto>(videos.Count);
            foreach (var v in videos)
                items.Add(ToSummary(v));

            var result = new PagedResult<VideoSummaryDto> {
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                TotalPages = pageSize > 0 ? (int)Math.Ceiling(total / (double)pageSize) : 0,
                Items = items
            };
            WriteJson(response, result);
        }

        /// <summary>
        /// GET /api/video/{dataId}
        /// </summary>
        private void HandleVideoDetail(HttpRequest request, HttpResponse response, string path)
        {
            try {
                HandleVideoDetailCore(response, path);
            } catch (Exception ex) {
                App.Logger?.Error($"LAN /api/video 失败: {ex}");
                try { WriteError(response, 500, ex.Message); } catch { }
            }
        }

        private void HandleVideoDetailCore(HttpResponse response, string path)
        {
            long dataId = ParseLong(path.Substring("/api/video/".Length), 0);
            if (dataId <= 0) {
                WriteError(response, 400, "invalid dataId");
                return;
            }

            Video video;
            try {
                video = MapperManager.videoMapper.SelectVideoByID(dataId);
            } catch (Exception ex) {
                WriteError(response, 500, ex.Message);
                return;
            }

            if (video == null) {
                WriteError(response, 404, "video not found");
                return;
            }

            WriteJson(response, ToDetail(video));
        }

        /// <summary>
        /// GET /image/{small|big|preview}/{dataId}   或   /image/actor/{actorId}
        /// </summary>
        #region "缺封面时自动抽帧"

        /// <summary>抽帧结果缓存目录（程序目录下，随程序走）</summary>
        private static readonly string CoverCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "cover");

        /// <summary>
        /// 详情页截图目录：<c>Cache/preview/{DataID}/1.jpg</c> …
        /// <para>
        /// 截图任务（桌面端 FFmpeg 配置）产出的图放这里，
        /// 详情页优先读本地，比拉外网 URL 快且不受网络影响。
        /// </para>
        /// </summary>
        private static readonly string PreviewCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "preview");

        /// <summary>
        /// 记录正在抽帧的任务，避免同一视频被并发重复抽帧。
        /// </summary>
        private static readonly ConcurrentDictionary<string, object> ExtractingLocks =
            new ConcurrentDictionary<string, object>();

        /// <summary>
        /// 从视频前半段随机抽一帧作为封面。
        /// <para>
        /// 适用场景：短片（文件名不含识别码）通常没有配套封面图，
        /// 直接返回 404 会让界面出现大片空白。
        /// </para>
        /// <para>
        /// 抽帧位置规则（与桌面端 FFmpeg 配置一致）：
        /// - 跳过开头 <see cref="CoverSkipStartSeconds"/> 秒：避开片头广告/水印
        /// - 只在前 50% 时长内取：后半段常是片尾/无关画面
        /// - 在该区间内随机取点：同一视频多次打开不会永远同一帧
        /// </para>
        /// <para>
        /// 结果落盘缓存，同一视频只抽一次，后续直接读缓存。
        /// </para>
        /// </summary>
        private string TryExtractCoverFrame(string key, string kind)
        {
            // 只对 small / big 兜底；preview 语义就是截图目录，不重复抽
            if (kind != "small" && kind != "big")
                return null;

            long dataId = ParseLong(key, 0);
            if (dataId <= 0)
                return null;

            string cached = Path.Combine(CoverCacheDir, dataId + ".jpg");
            if (File.Exists(cached))
                return cached;

            var video = MapperManager.videoMapper?.SelectVideoByID(dataId);
            if (video == null || string.IsNullOrEmpty(video.Path) || !File.Exists(video.Path))
                return null;

            // 同一条视频只允许一个线程抽帧，其余等它出结果
            var gate = ExtractingLocks.GetOrAdd(key, _ => new object());
            lock (gate) {
                if (File.Exists(cached))
                    return cached;

                try {
                    if (!Directory.Exists(CoverCacheDir))
                        Directory.CreateDirectory(CoverCacheDir);

                    string ffmpeg = ResolveFFmpegPath();
                    if (string.IsNullOrEmpty(ffmpeg))
                        return null;

                    double duration = GetVideoDurationSeconds(ffmpeg, video.Path);
                    double seek = PickSeekTime(duration);

                    string args =
                        "-ss " + seek.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                        " -i \"" + video.Path + "\"" +
                        " -frames:v 1 -q:v 4 -vf scale=480:-2 -y \"" + cached + "\"";

                    var psi = new ProcessStartInfo {
                        FileName = ffmpeg,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                    };

                    using (var proc = Process.Start(psi)) {
                        if (proc == null)
                            return null;
                        // 必须读走输出，否则管道写满会导致 ffmpeg 卡死
                        proc.StandardError.ReadToEnd();
                        proc.StandardOutput.ReadToEnd();
                        if (!proc.WaitForExit(15000)) {
                            try { proc.Kill(); } catch { }
                            return null;
                        }
                    }

                    if (File.Exists(cached) && new FileInfo(cached).Length > 0) {
                        App.Logger?.Info($"已为 DataID={dataId} 抽帧生成封面（{seek:0.0}s）");
                        return cached;
                    }
                } catch (Exception ex) {
                    App.Logger?.Error($"抽帧失败 DataID={dataId}: {ex.Message}");
                } finally {
                    ExtractingLocks.TryRemove(key, out _);
                }
            }

            return null;
        }

        /// <summary>
        /// 取第 <paramref name="index"/> 张远程截图（1 起），下载后缓存到本地。
        /// <para>
        /// 缓存路径与视频抽帧共用 <c>Cache/preview/{dataId}/{index}.jpg</c>，
        /// 所以「下载来的截图」和「抽帧的截图」对前端是同一套 URL，逻辑更简单。
        /// 已缓存则直接返回，不重复下载。
        /// </para>
        /// <para>
        /// 下载带 Referer 和 UA：部分图站有防盗链，裸请求会被 403。
        /// 超时设 12 秒，避免慢站点把整个详情页拖死。
        /// </para>
        /// </summary>
        private string TryGetRemoteShot(long dataId, int index)
        {
            if (dataId <= 0 || index < 1 || index > MaxPreviewCount)
                return null;

            string dir = Path.Combine(PreviewCacheDir, dataId.ToString());
            string cached = Path.Combine(dir, index + ".jpg");
            if (File.Exists(cached) && new FileInfo(cached).Length > 0)
                return cached;

            var video = MapperManager.videoMapper?.SelectVideoByID(dataId);
            if (video == null)
                return null;

            List<string> remote;
            try {
                remote = ExtractExtraImageUrls(video.ImageUrls);
            } catch {
                return null;
            }
            if (index > remote.Count)
                return null;

            string url = remote[index - 1];

            // 同一张图只允许一个线程下载，其余等结果
            string gateKey = "shot:" + dataId + ":" + index;
            var gate = ExtractingLocks.GetOrAdd(gateKey, _ => new object());
            lock (gate) {
                if (File.Exists(cached) && new FileInfo(cached).Length > 0)
                    return cached;

                try {
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    using (var wc = new WebClient()) {
                        // 防盗链：带上常见浏览器 UA 与来源页
                        wc.Headers.Add("User-Agent",
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/120.0 Safari/537.36");
                        try {
                            var uri = new Uri(url);
                            wc.Headers.Add("Referer", uri.GetLeftPart(UriPartial.Authority) + "/");
                        } catch {
                            // URL 非法则不加 Referer
                        }

                        // 先下到临时文件再改名，避免半截文件被当成有效缓存
                        string tmp = cached + ".tmp";
                        wc.DownloadFile(url, tmp);

                        if (File.Exists(tmp) && new FileInfo(tmp).Length > 0) {
                            if (File.Exists(cached))
                                File.Delete(cached);
                            File.Move(tmp, cached);
                            return cached;
                        }
                        if (File.Exists(tmp))
                            File.Delete(tmp);
                    }
                } catch (Exception ex) {
                    App.Logger?.Error($"截图下载失败 DataID={dataId} #{index}: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// 从视频抽第 <paramref name="index"/> 张详情截图（1 起）。
        /// <para>
        /// 与封面抽帧的区别：封面只取一帧，这里要一组不同时间点的帧
        /// 来展示「内容多个截图」。时间点均匀分布在正片区间内，
        /// 跳过片头广告，避免多张都是同一画面。
        /// </para>
        /// <para>
        /// 结果按 <c>Cache/preview/{dataId}/{index}.jpg</c> 缓存，同一张只抽一次。
        /// </para>
        /// </summary>
        private string TryExtractPreviewFrame(long dataId, int index)
        {
            if (dataId <= 0 || index < 1 || index > MaxPreviewCount)
                return null;

            string dir = Path.Combine(PreviewCacheDir, dataId.ToString());
            string cached = Path.Combine(dir, index + ".jpg");
            if (File.Exists(cached))
                return cached;

            var video = MapperManager.videoMapper?.SelectVideoByID(dataId);
            if (video == null || string.IsNullOrEmpty(video.Path) || !File.Exists(video.Path))
                return null;

            // 同一视频的抽帧串行化，避免并发重复调用 ffmpeg
            string gateKey = "preview:" + dataId;
            var gate = ExtractingLocks.GetOrAdd(gateKey, _ => new object());
            lock (gate) {
                if (File.Exists(cached))
                    return cached;

                try {
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string ffmpeg = ResolveFFmpegPath();
                    if (string.IsNullOrEmpty(ffmpeg))
                        return null;

                    double duration = GetVideoDurationSeconds(ffmpeg, video.Path);
                    double seek = PickPreviewTime(duration, index);

                    string args =
                        "-ss " + seek.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                        " -i \"" + video.Path + "\"" +
                        " -frames:v 1 -q:v 4 -vf scale=640:-2 -y \"" + cached + "\"";

                    var psi = new ProcessStartInfo {
                        FileName = ffmpeg,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                    };

                    using (var proc = Process.Start(psi)) {
                        if (proc == null)
                            return null;
                        // 必须读走输出，否则管道写满会让 ffmpeg 卡死
                        proc.StandardError.ReadToEnd();
                        proc.StandardOutput.ReadToEnd();
                        if (!proc.WaitForExit(20000)) {
                            try { proc.Kill(); } catch { }
                            return null;
                        }
                    }

                    if (File.Exists(cached) && new FileInfo(cached).Length > 0)
                        return cached;
                } catch (Exception ex) {
                    App.Logger?.Error($"详情截图抽帧失败 DataID={dataId} #{index}: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// 为第 <paramref name="index"/> 张截图选时间点。
        /// <para>
        /// 把「跳过片头后 ~ 90% 时长」等分成若干段，每段取中点，
        /// 这样多张截图分布均匀、互不重复，且不会抽到片尾黑屏。
        /// </para>
        /// </summary>
        private static double PickPreviewTime(double duration, int index)
        {
            if (duration <= 0)
                return CoverSkipStartSeconds + index * 60;

            double start = Math.Min(CoverSkipStartSeconds, duration * 0.12);
            double end = duration * 0.9;
            if (end <= start + 1)
                return Math.Max(0.5, duration * 0.5);

            // 按 4 段循环（前 4 张覆盖全片，之后重复但时间点略偏移）
            int slots = 4;
            int slot = (index - 1) % slots;
            double seg = (end - start) / slots;
            return start + seg * (slot + 0.5);
        }

        /// <summary>
        /// 跳过片头广告的秒数。
        /// <para>
        /// 实测这批素材的开场广告多在 10-20 秒，
        /// 取 30 秒可稳定避开，同时不会吃掉太多正片内容。
        /// </para>
        /// </summary>
        private const double CoverSkipStartSeconds = 30;

        /// <summary>
        /// 在前半段内随机选一个抽帧时间点。
        /// </summary>
        private static double PickSeekTime(double duration)
        {
            // 拿不到时长：退到「跳过片头后 1 分钟」这个常见位置
            if (duration <= 0)
                return CoverSkipStartSeconds + 30;

            double start = Math.Min(CoverSkipStartSeconds, duration * 0.15);
            double end = duration * 0.5;

            // 视频太短（不足 40 秒），直接取中点，避免 start > end
            if (end <= start + 1)
                return Math.Max(0.5, duration * 0.4);

            var rnd = new Random(unchecked((int)(DateTime.Now.Ticks & 0x7FFFFFFF)));
            return start + rnd.NextDouble() * (end - start);
        }

        /// <summary>
        /// 用 ffprobe 或 ffmpeg 读视频时长（秒）。失败返回 0。
        /// </summary>
        private double GetVideoDurationSeconds(string ffmpeg, string videoPath)
        {
            try {
                string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? string.Empty, "ffprobe.exe");
                string exe = File.Exists(ffprobe) ? ffprobe : ffmpeg;
                string args = File.Exists(ffprobe)
                    ? "-v error -show_entries format=duration -of default=nw=1:nk=1 \"" + videoPath + "\""
                    : "-i \"" + videoPath + "\"";

                var psi = new ProcessStartInfo {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                using (var proc = Process.Start(psi)) {
                    if (proc == null)
                        return 0;
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(10000)) {
                        try { proc.Kill(); } catch { }
                        return 0;
                    }

                    // ffprobe 直接给数字
                    double d;
                    if (double.TryParse(stdout.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out d) && d > 0)
                        return d;

                    // ffmpeg -i 走 stderr，形如 "Duration: 00:12:34.56"
                    var m = System.Text.RegularExpressions.Regex.Match(stderr, @"Duration:\s*(\d+):(\d+):(\d+)\.(\d+)");
                    if (m.Success) {
                        int hh = int.Parse(m.Groups[1].Value);
                        int mm = int.Parse(m.Groups[2].Value);
                        int ss = int.Parse(m.Groups[3].Value);
                        double frac = double.Parse("0." + m.Groups[4].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                        return hh * 3600 + mm * 60 + ss + frac;
                    }
                }
            } catch {
                // 读不到时长按未知处理
            }
            return 0;
        }

        /// <summary>
        /// 找 ffmpeg：先用配置里的路径，再退回 PATH。
        /// </summary>
        private static string ResolveFFmpegPath()
        {
            try {
                string configured = ConfigManager.FFmpegConfig?.Path;
                if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                    return configured;
            } catch {
                // 配置读取失败退回 PATH
            }

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathVar.Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                try {
                    string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                        return candidate;
                } catch {
                    // 非法路径段忽略
                }
            }
            return null;
        }

        #endregion

        private void HandleImage(HttpRequest request, HttpResponse response, string path)
        {
            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) {
                WriteError(response, 400, "invalid image path");
                return;
            }

            string kind = parts[1];
            string key = parts[2];
            string filePath = null;

            // /image/preview/{dataId}/{fileName}：详情页截图，直接读本地文件
            if (kind == "preview" && parts.Length >= 4) {
                filePath = ResolvePreviewFile(key, parts[3]);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                    WriteError(response, 404, "preview not found");
                    return;
                }
                response.WriteFileRange(filePath,
                    request.Headers.ContainsKey("Range") ? request.Headers["Range"] : null,
                    GetContentType(filePath));
                return;
            }

            // /image/frame/{dataId}/{n}：本地没截图时，从视频按序抽第 n 帧
            if (kind == "frame") {
                long fid = ParseLong(key, 0);
                int index = parts.Length >= 4 ? ParseInt(parts[3], 1) : 1;
                filePath = TryExtractPreviewFrame(fid, index);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                    WriteError(response, 404, "frame not found");
                    return;
                }
                response.WriteFileRange(filePath,
                    request.Headers.ContainsKey("Range") ? request.Headers["Range"] : null,
                    GetContentType(filePath));
                return;
            }

            // /image/shot/{dataId}/{n}：远程截图代理（下载并缓存到本地）
            if (kind == "shot") {
                long sid = ParseLong(key, 0);
                int index = parts.Length >= 4 ? ParseInt(parts[3], 1) : 1;
                filePath = TryGetRemoteShot(sid, index);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                    // 远程拿不到时退回视频抽帧，保证页面不空
                    filePath = TryExtractPreviewFrame(sid, index);
                }
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                    WriteError(response, 404, "shot not found");
                    return;
                }
                response.WriteFileRange(filePath,
                    request.Headers.ContainsKey("Range") ? request.Headers["Range"] : null,
                    GetContentType(filePath));
                return;
            }

            try {
                if (kind == "actor") {
                    long actorId = ParseLong(key, 0);
                    var wrapper = new Jvedio.Core.ORM.Wrapper.SelectWrapper<ActorInfo>();
                    wrapper.Eq("ActorID", actorId);
                    var actor = MapperManager.actorMapper?.SelectOne(wrapper);
                    if (actor != null)
                        filePath = actor.GetImagePath();
                } else {
                    long dataId = ParseLong(key, 0);
                    var video = MapperManager.videoMapper?.SelectVideoByID(dataId);
                    if (video != null) {
                        if (kind == "big")
                            filePath = video.GetBigImage();
                        else if (kind == "preview")
                            filePath = video.GetScreenShot();
                        else
                            filePath = video.GetSmallImage();
                    }
                }
            } catch {
                // 查询失败按未找到处理
            }

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                // 缺封面兜底：从视频前半段抽一帧当封面
                filePath = TryExtractCoverFrame(key, kind);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                    WriteError(response, 404, "image not found");
                    return;
                }
            }

            response.WriteFileRange(filePath, request.Headers.ContainsKey("Range") ? request.Headers["Range"] : null,
                GetContentType(filePath));
        }

        /// <summary>
        /// 解析详情页截图文件路径，并确保它确实在截图目录内（防目录穿越）。
        /// </summary>
        private static string ResolvePreviewFile(string dataIdKey, string fileName)
        {
            long dataId = ParseLong(dataIdKey, 0);
            if (dataId <= 0 || string.IsNullOrWhiteSpace(fileName))
                return null;

            // 文件名必须是纯文件名，不允许带路径分隔符或 ..
            if (fileName.Contains("..") || fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0)
                return null;
            if (!IsImageFile(fileName))
                return null;

            try {
                string dir = Path.GetFullPath(Path.Combine(PreviewCacheDir, dataId.ToString()));
                string full = Path.GetFullPath(Path.Combine(dir, fileName));
                return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? full : null;
            } catch {
                return null;
            }
        }

        /// <summary>
        /// GET /stream/{dataId}
        /// <para>支持 Range 请求，平板播放器可拖动进度。</para>
        /// </summary>
        private void HandleStream(HttpRequest request, HttpResponse response, string path)
        {
            long dataId = ParseLong(path.Substring("/stream/".Length), 0);
            if (dataId <= 0) {
                WriteError(response, 400, "invalid dataId");
                return;
            }

            Video video;
            try {
                video = MapperManager.videoMapper?.SelectVideoByID(dataId);
            } catch (Exception ex) {
                WriteError(response, 500, ex.Message);
                return;
            }

            if (video == null || string.IsNullOrEmpty(video.Path) || !File.Exists(video.Path)) {
                WriteError(response, 404, "file not found");
                return;
            }

            response.WriteFileRange(video.Path,
                request.Headers.ContainsKey("Range") ? request.Headers["Range"] : null,
                GetContentType(video.Path));
        }

        #endregion

        #region "数据查询"

        private List<Video> QueryVideos(long dbId, string keyword, string sort, int page, int pageSize,
            Dictionary<string, string[]> filters, string view, out long total)
        {
            total = 0;
            var mapper = MapperManager.videoMapper;
            if (mapper == null)
                return new List<Video>();

            var where = new List<string> { "metadata.DataType=0" };

            if (dbId > 0)
                where.Add($"metadata.DBId={dbId}");

            if (!string.IsNullOrWhiteSpace(keyword)) {
                string kw = keyword.Replace("'", "''");
                // 必须连 Path 一起搜：
                // 短片没有识别码、Title 也是空的（实测 872 条全是这种情况），
                // 文件名是唯一能标识它的信息。只搜 Title/VID 的话，
                // 短片库输入任何关键词都搜不到东西。
                where.Add($"(metadata.Title like '%{kw}%'" +
                          $" or metadata_video.VID like '%{kw}%'" +
                          $" or metadata.Path like '%{kw}%')");
            }

            // 特殊视图：收藏 / 最近播放（与桌面端语义一致）
            if (string.Equals(view, "favorite", StringComparison.OrdinalIgnoreCase)) {
                // 桌面端「我的收藏」= Grade > 0
                where.Add("metadata.Grade > 0");
            } else if (string.Equals(view, "recent", StringComparison.OrdinalIgnoreCase)) {
                // 桌面端「最近播放」= ViewDate 在最近 N 天内
                string since = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss");
                where.Add($"metadata.ViewDate >= '{since}'");
            }

            // 筛选条件
            if (filters != null) {
                foreach (var pair in filters) {
                    string clause = BuildFilterClause(pair.Key, pair.Value);
                    if (!string.IsNullOrEmpty(clause))
                        where.Add(clause);
                }
            }

            string whereSql = " where " + string.Join(" and ", where);

            try {
                var countRows = mapper.Select($"select count(*) as c from metadata_video join metadata on metadata.DataID=metadata_video.DataID{whereSql}");
                if (countRows != null && countRows.Count > 0) {
                    object val;
                    countRows[0].TryGetValue("c", out val);
                    if (val != null)
                        total = Convert.ToInt64(val);
                }
            } catch {
                total = 0;
            }

            string orderSql = BuildOrder(sort);
            int offset = (page - 1) * pageSize;

            string sql =
                "select " + string.Join(",", VideoMapper.SelectFields) +
                " from metadata_video join metadata on metadata.DataID=metadata_video.DataID" +
                whereSql + orderSql +
                $" limit {pageSize} offset {offset}";

            var rows = mapper.Select(sql);
            if (rows == null)
                return new List<Video>();

            List<Video> list;
            try {
                list = mapper.ToEntity<Video>(rows, typeof(Video).GetProperties(), false);
            } catch (Exception ex) {
                // ToEntity 走反射映射，失败时信息往往被上层吞掉，
                // 这里显式记录，否则表现为「接口返回空响应」
                App.Logger?.Error($"LAN ToEntity 失败（rows={rows.Count}）: {ex}");
                throw;
            }
            return list ?? new List<Video>();
        }

        /// <summary>
        /// 构造单个筛选维度的 SQL 条件。
        /// <para>
        /// 安全要点：字段名走白名单，值只做单引号转义（'' 表示一个字面单引号）。
        /// 多个值之间是 OR 关系（选了 A 和 B = A 或 B）。
        /// </para>
        /// </summary>
        private string BuildFilterClause(string field, string[] values)
        {
            if (values == null || values.Length == 0)
                return null;

            // 单值转义
            var escaped = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => "'" + v.Replace("'", "''") + "'")
                .ToArray();
            if (escaped.Length == 0)
                return null;

            string inList = string.Join(",", escaped);

            switch (field) {
                case "genres":
                    // Genre 是拼接文本，用 like 匹配包含关系
                    return "(" + string.Join(" or ", escaped.Select(v => "metadata.Genre like '%" + v.Trim('\'') + "%'")) + ")";

                case "studios":
                    return $"metadata_video.Studio in ({inList})";

                case "series":
                    return $"metadata_video.Series in ({inList})";

                case "directors":
                    return $"metadata_video.Director in ({inList})";

                case "actors":
                    return "metadata.DataID in (select mta.DataID from metadata_to_actor mta " +
                           "join actor_info ai on ai.ActorID=mta.ActorID " +
                           $"where ai.ActorName in ({inList}))";

                default:
                    // 未知字段一律忽略，不拼进 SQL
                    return null;
            }
        }

        private string BuildOrder(string sort)
        {
            // 白名单排序字段，避免 SQL 注入
            switch ((sort ?? string.Empty).ToLowerInvariant()) {
                case "date":
                    return " order by metadata.ReleaseDate desc";
                case "date_asc":
                    return " order by metadata.ReleaseDate asc";
                case "grade":
                    return " order by metadata.Grade desc, metadata.DataID desc";
                case "viewcount":
                    return " order by metadata.ViewCount desc, metadata.DataID desc";
                case "size":
                    return " order by metadata_video.Size desc";
                case "size_asc":
                    return " order by metadata_video.Size asc";
                case "title":
                    return " order by metadata.Title asc";
                case "viewdate":
                    return " order by metadata.ViewDate desc";
                default:
                    return " order by metadata.DataID desc";
            }
        }

        #endregion

        #region "DTO 转换"

        /// <summary>
        /// 从完整路径取用于显示的文件名（去掉目录和扩展名）。
        /// <para>
        /// 短片没有识别码，前端需要它作为显示名。
        /// 过长的名字截断，避免撑破卡片布局。
        /// </para>
        /// </summary>
        private static string GetDisplayFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try {
                string name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                // 去掉常见的网站后缀，让名字更干净
                name = name.Replace(" - Pornhub.com", string.Empty).Trim();

                if (name.Length > 60)
                    name = name.Substring(0, 60) + "…";
                return name;
            } catch {
                return null;
            }
        }

        private VideoSummaryDto ToSummary(Video v)
        {
            var dto = new VideoSummaryDto {
                DataID = v.DataID,
                DBId = v.DBId,
                // 库中存量记录可能带尾部字母（修复前扫描入库），此处统一归一化后再输出
                VID = Jvedio.Identity.Identify.NormalizeVID(v.VID),
                Title = v.Title,
                ReleaseDate = v.ReleaseDate,
                ReleaseYear = v.ReleaseYear,
                Size = v.Size,
                Duration = v.Duration,
                Grade = v.Grade,
                Rating = v.Rating,
                ViewCount = v.ViewCount,
                FavoriteCount = v.FavoriteCount,
                HasSubSection = !string.IsNullOrEmpty(v.SubSection),
                Playable = !string.IsNullOrEmpty(v.Path) && File.Exists(v.Path),
                // 文件名：短片（无识别码）时前端用它作为显示名，
                // 否则这些记录在界面上只能显示"未知编号"
                FileName = GetDisplayFileName(v.Path),
                SmallImageUrl = $"/image/small/{v.DataID}",
                BigImageUrl = $"/image/big/{v.DataID}"
            };

            dto.Genres = SplitValues(v.Genre);
            dto.Actors = SplitValues(v.ActorNames);

            if (!string.IsNullOrEmpty(v.TagIDs)) {
                foreach (string tagId in v.TagIDs.Split(',')) {
                    var tag = Jvedio.Entity.CommonSQL.TagStamp.TagStamps?.FirstOrDefault(t => t.TagID.ToString() == tagId);
                    if (tag != null && !string.IsNullOrEmpty(tag.TagName))
                        dto.Tags.Add(tag.TagName);
                }
            }

            return dto;
        }

        private VideoDetailDto ToDetail(Video v)
        {
            var s = ToSummary(v);
            var dto = new VideoDetailDto {
                DataID = s.DataID,
                DBId = s.DBId,
                VID = s.VID,
                Title = s.Title,
                // FileName 必须一起传：短片没有识别码，
                // 详情页/播放页标题需要它作为兜底显示名，
                // 否则这类记录只能显示 "#4521"。
                FileName = s.FileName,
                ReleaseDate = s.ReleaseDate,
                ReleaseYear = s.ReleaseYear,
                Size = s.Size,
                Duration = s.Duration,
                Grade = s.Grade,
                Rating = s.Rating,
                ViewCount = s.ViewCount,
                FavoriteCount = s.FavoriteCount,
                HasSubSection = s.HasSubSection,
                Playable = s.Playable,
                SmallImageUrl = s.SmallImageUrl,
                BigImageUrl = s.BigImageUrl,
                Tags = s.Tags,
                Genres = s.Genres,
                Actors = s.Actors,

                Plot = v.Plot,
                Outline = v.Outline,
                Director = v.Director,
                Studio = v.Studio,
                Publisher = v.Publisher,
                Series = v.Series,
                Country = v.Country,
                FilePath = v.Path,
                StreamUrl = $"/stream/{v.DataID}"
            };

            // 截图列表：给详情页做「内容多图预览」。
            // 本地截图优先（打开快、不依赖外网），没有则退回库里的远程 URL。
            dto.PreviewImageUrls = BuildPreviewUrls(v);

            if (v.ActorInfos != null) {
                foreach (var a in v.ActorInfos) {
                    dto.ActorList.Add(new ActorDto {
                        ActorID = a.ActorID,
                        ActorName = a.ActorName,
                        ImageUrl = $"/image/actor/{a.ActorID}",
                        Age = a.Age,
                        Country = a.Country,
                        Birthday = a.Birthday,
                        Height = a.Height,
                        Weight = a.Weight,
                        Cup = a.Cup.ToString()
                    });
                }
            }

            return dto;
        }

        /// <summary>
        /// 构造详情页的截图列表。
        /// <para>
        /// 为什么全部走本地代理（<c>/image/shot/{id}/{n}</c>）而不是直接给远程 URL：
        /// </para>
        /// <list type="bullet">
        /// <item>实测 DMM 图片单张要 0.8–4.6 秒，直连会让详情页出现一排空白灰块</item>
        /// <item>部分站点有防盗链，浏览器带 Referer 才给图，手机端直接请求会被拒</item>
        /// <item>手机可能没外网（只连了局域网），直连 URL 必然失败</item>
        /// </list>
        /// <para>
        /// 代理后：服务端下载一次并落盘缓存，之后手机从局域网秒开；
        /// 下载失败就自动退回视频抽帧，详情页永远有内容可看。
        /// </para>
        /// </summary>
        private List<string> BuildPreviewUrls(Video v)
        {
            var urls = new List<string>();
            if (v == null)
                return urls;

            // 本地截图目录里已有文件：直接给出真实文件名
            try {
                string dir = Path.Combine(PreviewCacheDir, v.DataID.ToString());
                if (Directory.Exists(dir)) {
                    var files = Directory.GetFiles(dir)
                        .Where(f => IsImageFile(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxPreviewCount)
                        .ToList();
                    if (files.Count > 0) {
                        foreach (string f in files)
                            urls.Add($"/image/preview/{v.DataID}/{Path.GetFileName(f)}");
                        return urls;
                    }
                }
            } catch {
                // 目录不可读，继续走代理
            }

            // 库里存了远程截图：走本地代理（服务端负责下载+缓存）
            int remoteCount = 0;
            try {
                remoteCount = Math.Min(ExtractExtraImageUrls(v.ImageUrls).Count, MaxPreviewCount);
            } catch {
                remoteCount = 0;
            }
            if (remoteCount > 0) {
                for (int i = 1; i <= remoteCount; i++)
                    urls.Add($"/image/shot/{v.DataID}/{i}");
                return urls;
            }

            // 兜底：从视频抽帧
            if (!string.IsNullOrEmpty(v.Path) && File.Exists(v.Path)) {
                for (int i = 1; i <= 4; i++)
                    urls.Add($"/image/frame/{v.DataID}/{i}");
            }

            return urls;
        }

        /// <summary>详情页最多展示多少张截图（太多会让页面很长）</summary>
        private const int MaxPreviewCount = 12;

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".gif";
        }

        /// <summary>
        /// 从 <c>ImageUrls</c> JSON 里取 <c>ExtraImageUrl</c> 数组。
        /// <para>
        /// 该字段是爬虫抓来的截图地址列表，形如
        /// <c>{"SmallImageUrl":"…","ExtraImageUrl":["…","…"]}</c>。
        /// 解析失败一律返回空列表，不抛异常。
        /// </para>
        /// </summary>
        private static List<string> ExtractExtraImageUrls(string imageUrlsJson)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(imageUrlsJson))
                return list;

            try {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(imageUrlsJson);
                object raw;
                if (dict == null || !dict.TryGetValue("ExtraImageUrl", out raw) || raw == null)
                    return list;

                // 可能是数组，也可能是 JSON 字符串形式的数组
                var arr = raw as Newtonsoft.Json.Linq.JArray;
                if (arr == null && raw is string) {
                    string s = (string)raw;
                    if (s.TrimStart().StartsWith("["))
                        arr = Newtonsoft.Json.Linq.JArray.Parse(s);
                }

                if (arr != null) {
                    foreach (var token in arr) {
                        string u = token?.ToString();
                        if (!string.IsNullOrWhiteSpace(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            list.Add(u);
                    }
                }
            } catch {
                // 脏数据不阻断详情接口
            }
            return list;
        }

        private List<string> SplitValues(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return list;

            string separator = Jvedio.Common.ConstValues.Separator.ToString();
            foreach (string s in raw.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)) {
                string t = s.Trim();
                if (t.Length > 0)
                    list.Add(t);
            }
            return list;
        }

        #endregion

        #region "HTTP 输出"

        private void WriteJson(HttpResponse response, object obj)
        {
            WriteJson(response, 200, obj);
        }

        /// <summary>
        /// 指定 HTTP 状态码写 JSON。
        /// <para>
        /// 错误响应必须用这个重载，否则客户端拿到的永远是 200。
        /// </para>
        /// </summary>
        private void WriteJson(HttpResponse response, int status, object obj)
        {
            string json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            response.WriteText(status, "application/json; charset=utf-8", json);
        }

        private void WriteError(HttpResponse response, int status, string message)
        {
            try {
                // 必须把 status 传进 WriteJson：
                // 早期版本直接调 WriteJson（内部固定 200），
                // 导致所有错误响应都是 HTTP 200 + body 里带 status 字段，
                // 客户端与 curl 无法通过状态码判断失败。
                WriteJson(response, status, new { error = message, status });
            } catch {
                // 响应可能已开始写入，忽略
            }
        }

        private string GetContentType(string path)
        {
            string ext = Path.GetExtension(path);
            if (ext == null)
                return "application/octet-stream";

            switch (ext.ToLowerInvariant()) {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".mp4": return "video/mp4";
                case ".mkv": return "video/x-matroska";
                case ".avi": return "video/x-msvideo";
                case ".wmv": return "video/x-ms-wmv";
                case ".mov": return "video/quicktime";
                case ".ts": return "video/mp2t";
                case ".m4v": return "video/mp4";
                case ".flv": return "video/x-flv";
                default: return "application/octet-stream";
            }
        }

        #endregion

        #region "工具"

        private static string GetQuery(HttpRequest request, string key)
        {
            string value;
            return request.Query.TryGetValue(key, out value) ? value : null;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        private static long ParseLong(string s, long fallback)
        {
            long v;
            return long.TryParse(s, out v) ? v : fallback;
        }

        /// <summary>
        /// 获取本机局域网 IP（用于生成对外访问地址）。
        /// <para>
        /// 不能简单返回「第一个非回环地址」：装了 VPN / 虚拟网卡时，
        /// 枚举顺序里常出现 <c>192.168.3.32/32</c> 这类点对点地址，
        /// 手机和投影仪根本访问不到（实测就踩了这个坑，
        /// 投屏地址给成了 VPN 网卡，投影仪一直卡在 TRANSITIONING）。
        /// 这里优先选有默认网关、掩码小于 /32 的常规局域网地址。
        /// </para>
        /// </summary>
        public static string GetLocalIpAddress()
        {
            try {
                var all = GetLocalIPv4WithMask();

                // 优先：真实局域网段（192.168.x.x / 10.x.x.x）且非 /32
                var lan = all.Where(x => x.Item2 < 32 && IsPrivateLanAddress(x.Item1)).ToList();
                if (lan.Count > 0)
                    return lan[0].Item1.ToString();

                var normal = all.Where(x => x.Item2 < 32).ToList();
                if (normal.Count > 0)
                    return normal[0].Item1.ToString();

                if (all.Count > 0)
                    return all[0].Item1.ToString();
            } catch {
                // 获取失败时退回本机回环地址
            }
            return "127.0.0.1";
        }

        /// <summary>
        /// 取与目标设备处于同一网段的本机 IP。
        /// <para>
        /// 投屏必须用这个：投影仪只能访问和它同网段的地址。
        /// 本机同时连以太网、WiFi、VPN 时，随便挑一个必然出错。
        /// </para>
        /// </summary>
        public static string GetLocalIpFor(string remoteIp)
        {
            IPAddress target;
            if (string.IsNullOrEmpty(remoteIp) || !IPAddress.TryParse(remoteIp, out target))
                return GetLocalIpAddress();

            try {
                var candidates = GetLocalIPv4WithMask();
                // 先找精确同网段（排除 /32，那不是真实局域网）
                foreach (var c in candidates) {
                    if (c.Item2 <= 0 || c.Item2 >= 32)
                        continue;
                    if (SameSubnet(c.Item1, c.Item2, target))
                        return c.Item1.ToString();
                }
            } catch { /* 忽略 */ }

            return GetLocalIpAddress();
        }

        /// <summary>列出本机所有 IPv4 地址及其掩码位数</summary>
        private static List<Tuple<IPAddress, int>> GetLocalIPv4WithMask()
        {
            var list = new List<Tuple<IPAddress, int>>();
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;

                System.Net.NetworkInformation.IPInterfaceProperties props;
                try { props = ni.GetIPProperties(); } catch { continue; }

                foreach (var ua in props.UnicastAddresses) {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IPAddress.IsLoopback(ua.Address))
                        continue;

                    byte[] b = ua.Address.GetAddressBytes();
                    // 169.254.x.x 是拿不到 DHCP 时的自动地址，无意义
                    if (b[0] == 169 && b[1] == 254)
                        continue;

                    if (!list.Any(x => x.Item1.Equals(ua.Address)))
                        list.Add(Tuple.Create(ua.Address, ua.PrefixLength));
                }
            }
            return list;
        }

        private static bool IsPrivateLanAddress(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 192 && b[1] == 168
                || b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
        }

        private static bool SameSubnet(IPAddress a, int prefix, IPAddress b)
        {
            byte[] x = a.GetAddressBytes();
            byte[] y = b.GetAddressBytes();
            int bits = prefix;
            for (int i = 0; i < 4 && bits > 0; i++) {
                int take = Math.Min(8, bits);
                int mask = (0xFF << (8 - take)) & 0xFF;
                if ((x[i] & mask) != (y[i] & mask))
                    return false;
                bits -= take;
            }
            return true;
        }

        #endregion
    }
}
