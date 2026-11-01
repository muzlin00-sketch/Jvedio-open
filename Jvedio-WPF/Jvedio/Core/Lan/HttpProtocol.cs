using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Jvedio.Core.Lan
{
    /// <summary>
    /// 极简 HTTP 请求解析结果。
    /// <para>
    /// 不用 <c>HttpListener</c>：它在绑定 <c>+</c>（所有网卡）时需要管理员权限或预先注册
    /// URL ACL（<c>netsh http add urlacl</c>），普通用户运行时直接报「拒绝访问」。
    /// 改用 <see cref="System.Net.Sockets.TcpListener"/> 自行解析 HTTP，无需任何特权。
    /// </para>
    /// </summary>
    public class HttpRequest
    {
        public string Method { get; set; }

        /// <summary>不含查询串的路径，如 /api/videos</summary>
        public string Path { get; set; }

        /// <summary>原始查询串（不含 ?）</summary>
        public string RawQuery { get; set; }

        public string Version { get; set; }

        public Dictionary<string, string> Headers { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Query { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 请求体（仅在带 Content-Length 且方法允许时读取，最大 64KB）。
        /// <para>
        /// 早期版本不读请求体，所有参数都靠查询串传。
        /// 登录接口需要传口令，把口令放 URL 里会被写进访问日志、
        /// 也会留在浏览器历史里，所以这里补上请求体支持。
        /// </para>
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>客户端地址（IP:端口），由服务端在解析后填入。</summary>
        public string RemoteEndPoint { get; set; }

        /// <summary>
        /// 从网络流读取并解析一个 HTTP 请求。
        /// </summary>
        /// <returns>解析失败（连接断开/格式非法）时返回 null</returns>
        public static HttpRequest Parse(Stream stream)
        {
            // 逐字节读取直到 \r\n\r\n，避免 BufferedReader 越过请求边界
            var headerBytes = new List<byte>(1024);
            int state = 0;
            while (state < 4) {
                int b = stream.ReadByte();
                if (b < 0)
                    return null;
                headerBytes.Add((byte)b);

                if (state == 0 && b == '\r') state = 1;
                else if (state == 1 && b == '\n') state = 2;
                else if (state == 2 && b == '\r') state = 3;
                else if (state == 3 && b == '\n') state = 4;
                else if (b != '\r') state = 0;

                // 防御：请求头过大视为异常
                if (headerBytes.Count > 64 * 1024)
                    return null;
            }

            string headerText = Encoding.UTF8.GetString(headerBytes.ToArray());
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
                return null;

            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return null;

            var req = new HttpRequest {
                Method = requestLine[0],
                Version = requestLine.Length >= 3 ? requestLine[2] : "HTTP/1.1"
            };

            string target = requestLine[1];
            int qIndex = target.IndexOf('?');
            if (qIndex >= 0) {
                req.Path = target.Substring(0, qIndex);
                req.RawQuery = target.Substring(qIndex + 1);
            } else {
                req.Path = target;
                req.RawQuery = string.Empty;
            }

            req.Path = Uri.UnescapeDataString(req.Path);
            req.Query = ParseQuery(req.RawQuery);

            for (int i = 1; i < lines.Length; i++) {
                string line = lines[i];
                int colon = line.IndexOf(':');
                if (colon > 0) {
                    string key = line.Substring(0, colon).Trim();
                    string value = line.Substring(colon + 1).Trim();
                    if (!string.IsNullOrEmpty(key))
                        req.Headers[key] = value;
                }
            }

            // 读请求体。只在声明了 Content-Length 时读，
            // 且限制 64KB——登录口令、表单参数远小于这个量，
            // 限制大小可以防止有人用超大 body 占内存。
            string lenHeader;
            if (req.Headers.TryGetValue("Content-Length", out lenHeader)) {
                int contentLength;
                if (int.TryParse(lenHeader, out contentLength) && contentLength > 0) {
                    if (contentLength > 64 * 1024)
                        contentLength = 64 * 1024;

                    byte[] buf = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength) {
                        int n = stream.Read(buf, read, contentLength - read);
                        if (n <= 0)
                            break;          // 客户端提前断开
                        read += n;
                    }
                    if (read > 0)
                        req.Body = Encoding.UTF8.GetString(buf, 0, read);
                }
            }

            return req;
        }

        private static Dictionary<string, string> ParseQuery(string raw)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw))
                return dict;

            foreach (string pair in raw.Split('&')) {
                if (string.IsNullOrEmpty(pair))
                    continue;
                int eq = pair.IndexOf('=');
                if (eq < 0) {
                    dict[Uri.UnescapeDataString(pair)] = string.Empty;
                } else {
                    string k = Uri.UnescapeDataString(pair.Substring(0, eq));
                    string v = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    dict[k] = v;
                }
            }
            return dict;
        }
    }

    /// <summary>
    /// 极简 HTTP 响应写出器。
    /// </summary>
    public class HttpResponse
    {
        private readonly Stream _Stream;

        public HttpResponse(Stream stream)
        {
            _Stream = stream;
        }

        public void WriteText(int status, string contentType, string body)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(body ?? string.Empty);
            WriteHeader(status, contentType, buffer.Length);
            _Stream.Write(buffer, 0, buffer.Length);
            _Stream.Flush();
        }

        /// <summary>
        /// 写出二进制内容（静态资源：html/css/js/图片等）。
        /// <para>
        /// 与 <see cref="WriteText"/> 的区别：不做编码转换，原样输出字节。
        /// </para>
        /// </summary>
        public void WriteBinary(int status, string contentType, byte[] body)
        {
            WriteBinary(status, contentType, body, null);
        }

        /// <summary>
        /// 写二进制响应，可指定缓存策略。
        /// </summary>
        public void WriteBinary(int status, string contentType, byte[] body, string cacheControl)
        {
            if (body == null)
                body = new byte[0];

            WriteHeader(status, contentType, body.Length, cacheControl);
            if (body.Length > 0)
                _Stream.Write(body, 0, body.Length);
            _Stream.Flush();
        }

        /// <summary>
        /// 在异常兜底路径上直接写错误响应。
        /// <para>
        /// 用于 <see cref="HttpResponse"/> 尚未构造完成、或原响应对象已不可用时。
        /// 必须独立成静态方法，因为它拿不到实例状态。
        /// </para>
        /// </summary>
        public static void WriteErrorTo(Stream stream, int status, string message)
        {
            if (stream == null)
                return;

            byte[] body = Encoding.UTF8.GetBytes(message ?? string.Empty);
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(GetStatusText(status)).Append("\r\n");
            sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        public void WriteHeader(int status, string contentType, long contentLength)
        {
            WriteHeader(status, contentType, contentLength, null);
        }

        /// <summary>
        /// 写响应头，可指定缓存策略。
        /// <para>
        /// 之前所有响应都不带缓存控制头，浏览器会按启发式规则长期缓存静态资源，
        /// 导致程序升级后页面仍是旧的（实测已踩到：改了布局但浏览器不重新拉取）。
        /// </para>
        /// <para>
        /// <paramref name="cacheControl"/> 为 null 时按「不缓存」处理，这是安全默认值。
        /// </para>
        /// </summary>
        public void WriteHeader(int status, string contentType, long contentLength, string cacheControl)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(GetStatusText(status)).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(contentLength).Append("\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Cache-Control: ").Append(
                string.IsNullOrEmpty(cacheControl) ? "no-store, must-revalidate" : cacheControl).Append("\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            _Stream.Write(head, 0, head.Length);
        }

        /// <summary>
        /// 写出带 Range 支持的文件响应。
        /// </summary>
        public void WriteFileRange(string filePath, string rangeHeader, string contentType)
        {
            var info = new FileInfo(filePath);
            long fileLength = info.Length;
            long start = 0;
            long end = fileLength - 1;
            int status = 200;

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=")) {
                string spec = rangeHeader.Substring("bytes=".Length).Split(',')[0].Trim();
                string[] bounds = spec.Split('-');

                if (bounds.Length == 2) {
                    if (!string.IsNullOrEmpty(bounds[0])) {
                        long.TryParse(bounds[0], out start);
                    }
                    if (!string.IsNullOrEmpty(bounds[1])) {
                        long.TryParse(bounds[1], out end);
                    } else {
                        end = fileLength - 1;
                    }
                }

                if (start < 0) start = 0;
                if (end >= fileLength) end = fileLength - 1;

                if (start > end || start >= fileLength) {
                    var sb416 = new StringBuilder();
                    sb416.Append("HTTP/1.1 416 Range Not Satisfiable\r\n");
                    sb416.Append("Content-Range: bytes */").Append(fileLength).Append("\r\n");
                    sb416.Append("Content-Length: 0\r\n");
                    sb416.Append("Connection: close\r\n\r\n");
                    byte[] b416 = Encoding.ASCII.GetBytes(sb416.ToString());
                    _Stream.Write(b416, 0, b416.Length);
                    _Stream.Flush();
                    return;
                }

                status = 206;
            }

            long length = end - start + 1;

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(GetStatusText(status)).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(length).Append("\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            if (status == 206) {
                sb.Append("Content-Range: bytes ").Append(start).Append('-').Append(end)
                  .Append('/').Append(fileLength).Append("\r\n");
            }
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            _Stream.Write(head, 0, head.Length);

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                fs.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[256 * 1024];
                long remaining = length;

                while (remaining > 0) {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = fs.Read(buffer, 0, toRead);
                    if (read <= 0)
                        break;
                    _Stream.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
            _Stream.Flush();
        }

        private static string GetStatusText(int status)
        {
            switch (status) {
                case 200: return "OK";
                case 206: return "Partial Content";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 416: return "Range Not Satisfiable";
                case 500: return "Internal Server Error";
                default: return "OK";
            }
        }
    }
}
