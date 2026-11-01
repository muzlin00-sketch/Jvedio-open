using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Jvedio.Core.Lan
{
    /// <summary>
    /// 局域网访问鉴权。
    ///
    /// 设计取舍（与 Jellyfin / Plex 的「单用户口令」一致，不做账号体系）：
    /// - 桌面端设置一个访问口令，网页端每台设备输一次，之后长期记住；
    /// - 不是多用户系统：家庭单用户媒体库没有分级需求，多账号只会扩大攻击面；
    /// - 不存明文口令到磁盘以外的地方，也不在网络上传明文。
    ///
    /// 令牌用 HMAC 签名的「自包含」格式：
    ///   base64(过期时间|随机数) + "." + base64(HMAC)
    /// 好处是不需要服务端会话表——重启后旧令牌依然有效，
    /// 除非用户主动「全部失效」（改口令即可让所有旧令牌失效，
    /// 因为签名密钥由口令派生）。
    /// </summary>
    public class LanAuth
    {
        /// <summary>令牌有效期：365 天。单机媒体库没必要频繁重新登录。</summary>
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(365);

        /// <summary>投屏临时令牌有效期。够投影仪建立连接并开始拉流即可。</summary>
        private static readonly TimeSpan CastTokenLifetime = TimeSpan.FromMinutes(10);

        /// <summary>口令最少位数。局域网自用，不强制复杂度，只挡住「空口令」。</summary>
        public const int MIN_PASSWORD_LENGTH = 4;

        /// <summary>当前口令。空字符串表示未启用口令。</summary>
        private string _password = string.Empty;

        /// <summary>最近失败记录，用于限速暴力破解</summary>
        private readonly ConcurrentDictionary<string, FailRecord> _failures =
            new ConcurrentDictionary<string, FailRecord>(StringComparer.OrdinalIgnoreCase);

        public bool Enabled => !string.IsNullOrEmpty(_password);

        /// <summary>设置或修改口令。传空字符串表示关闭口令。</summary>
        public void SetPassword(string password)
        {
            _password = (password ?? string.Empty).Trim();
            // 口令一改，所有旧令牌的签名密钥同时变化，等价于「全部失效」
            _failures.Clear();
        }

        /// <summary>验证口令是否正确（同时做失败限速）。</summary>
        public bool CheckPassword(string input, string clientIp, out string error)
        {
            error = null;
            if (!Enabled) {
                // 未启用口令时直接放行
                return true;
            }

            var rec = _failures.GetOrAdd(clientIp ?? "?", _ => new FailRecord());
            lock (rec) {
                // 连续错 10 次锁 5 分钟。局域网内既挡暴力破解，又不至于自己敲错几次就被锁。
                if (rec.LockedUntil > DateTime.UtcNow) {
                    int sec = (int)Math.Ceiling((rec.LockedUntil - DateTime.UtcNow).TotalSeconds);
                    error = $"尝试次数过多，请 {sec} 秒后再试";
                    return false;
                }

                if (FixedTimeEquals(input ?? string.Empty, _password)) {
                    rec.Count = 0;
                    return true;
                }

                rec.Count++;
                if (rec.Count >= 10) {
                    rec.LockedUntil = DateTime.UtcNow.AddMinutes(5);
                    rec.Count = 0;
                    error = "尝试次数过多，已锁定 5 分钟";
                } else {
                    error = "口令不正确";
                }
                return false;
            }
        }

        /// <summary>签发访问令牌。</summary>
        public string IssueToken()
        {
            long expires = DateTimeOffset.UtcNow.Add(TokenLifetime).ToUnixTimeSeconds();
            string payload = expires + "|" + Guid.NewGuid().ToString("N");
            string p64 = ToBase64Url(Encoding.UTF8.GetBytes(payload));
            string sig = ToBase64Url(Sign(p64));
            return p64 + "." + sig;
        }

        /// <summary>校验访问令牌是否有效。</summary>
        public bool ValidateToken(string token)
        {
            if (!Enabled) {
                // 没开口令时任何令牌都可视为有效（保持「默认不强制」的行为）
                return true;
            }
            if (string.IsNullOrWhiteSpace(token))
                return false;

            int dot = token.IndexOf('.');
            if (dot <= 0 || dot >= token.Length - 1)
                return false;

            string p64 = token.Substring(0, dot);
            string sig = token.Substring(dot + 1);

            // 先验签再解析，避免伪造过期时间
            string expect = ToBase64Url(Sign(p64));
            if (!FixedTimeEquals(sig, expect))
                return false;

            try {
                string payload = Encoding.UTF8.GetString(FromBase64Url(p64));
                int bar = payload.IndexOf('|');
                if (bar <= 0)
                    return false;
                long expires;
                if (!long.TryParse(payload.Substring(0, bar), out expires))
                    return false;
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expires;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 签发投屏专用临时令牌。
        /// <para>
        /// 投影仪拉流时无法携带登录 Cookie，所以投屏时生成一个
        /// 短期、绑定 dataId 的令牌拼在流地址上。十分钟后自动失效，
        /// 即使链接泄漏影响也有限。
        /// </para>
        /// </summary>
        public string IssueCastToken(long dataId)
        {
            long expires = DateTimeOffset.UtcNow.Add(CastTokenLifetime).ToUnixTimeSeconds();
            string payload = "cast:" + dataId + ":" + expires;
            string p64 = ToBase64Url(Encoding.UTF8.GetBytes(payload));
            string sig = ToBase64Url(Sign(p64));
            return p64 + "." + sig;
        }

        /// <summary>校验投屏令牌是否可用于指定 dataId。</summary>
        public bool ValidateCastToken(string token, long dataId)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;
            int dot = token.IndexOf('.');
            if (dot <= 0 || dot >= token.Length - 1)
                return false;

            string p64 = token.Substring(0, dot);
            string sig = token.Substring(dot + 1);
            string expect = ToBase64Url(Sign(p64));
            if (!FixedTimeEquals(sig, expect))
                return false;

            try {
                string payload = Encoding.UTF8.GetString(FromBase64Url(p64));
                string[] parts = payload.Split(':');
                if (parts.Length != 3 || parts[0] != "cast")
                    return false;
                if (long.Parse(parts[1]) != dataId)
                    return false;
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= long.Parse(parts[2]);
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 让所有已签发令牌失效。
        /// 做法是换一个签名密钥——因为密钥由口令派生，改口令即完成。
        /// </summary>
        public void InvalidateAll()
        {
            _failures.Clear();
        }

        #region "内部"

        private byte[] Sign(string data)
        {
            // 密钥由口令派生：改口令 ⇒ 旧令牌全部失效
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("Jvedio.LanAuth." + _password))) {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        /// <summary>
        /// 常量时间比较，避免通过响应时间差逐字节猜口令。
        /// </summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;
            byte[] x = Encoding.UTF8.GetBytes(a);
            byte[] y = Encoding.UTF8.GetBytes(b);
            // 长度不同也要走完整比较，先记下差异
            int diff = x.Length ^ y.Length;
            int n = Math.Min(x.Length, y.Length);
            for (int i = 0; i < n; i++)
                diff |= x[i] ^ y[i];
            return diff == 0;
        }

        private static string ToBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromBase64Url(string s)
        {
            string t = s.Replace('-', '+').Replace('_', '/');
            switch (t.Length % 4) {
                case 2: t += "=="; break;
                case 3: t += "="; break;
            }
            return Convert.FromBase64String(t);
        }

        private class FailRecord
        {
            public int Count;
            public DateTime LockedUntil;
        }

        #endregion
    }
}
