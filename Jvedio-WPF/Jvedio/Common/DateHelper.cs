using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jvedio.Common
{
    /// <summary>
    /// 时间格式化与解析。取代 <c>SuperUtils.Time.DateHelper</c>。
    /// <para>
    /// 输出格式是与存量数据绑定的契约：数据库里的 <c>FirstScanDate</c>、<c>LastScanDate</c>、
    /// <c>ViewDate</c>、<c>CreateDate</c> 全部由 <see cref="Now"/> 写入，
    /// 日志文件名由 <see cref="NowDate"/> 生成。改动其中一个格式会让旧记录排不进时间范围查询。
    /// </para>
    /// </summary>
    public static class DateHelper
    {
        /// <summary>数据库时间字段的存取格式，精确到毫秒。</summary>
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>只保留日期的格式，用于日志文件名与按天聚合。</summary>
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>当前时间，写入数据库的默认值。</summary>
        public static string Now()
        {
            return DateTime.Now.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>当前日期（yyyy-MM-dd），用于日志文件名。</summary>
        public static string NowDate()
        {
            return DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 把 <see cref="DateTime"/> 格式化成数据库时间串（含毫秒）。
        /// </summary>
        public static string ToLocalDate(this DateTime date)
        {
            return date.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 把任意可解析的时间串归一化成 <c>yyyy-MM-dd</c>，解析失败返回空串。
        /// <para>
        /// 注意两个重载返回的粒度不同（这里只到日，另一个到毫秒），这是沿用原实现的契约：
        /// 调用方拿它做按天分组和 BETWEEN 查询的边界。
        /// </para>
        /// </summary>
        public static string ToLocalDate(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return string.Empty;

            if (!DateTime.TryParse(str, out DateTime date))
                return string.Empty;

            return date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 把毫秒数渲染成便于阅读的耗时，用于日志与扫描详情。
        /// <para>
        /// 分段规则（按原实现逐值对拍得出，不是猜的）：
        /// 不足 1 秒直接 <c>999ms</c>；一旦出现分钟或小时，
        /// 秒与毫秒各自独立成段（<c>59min 59s 999ms</c>）；
        /// 只有秒时才把毫秒拼成小数且<b>不补零</b>（<c>1.500s</c>、<c>1.1s</c>）。
        /// 最后那种写法读起来像 1.1 秒而实际是 1.001 秒，属原实现的固有问题；
        /// 因为只用于展示，保留原样以免历史日志口径不一致。
        /// </para>
        /// <para>
        /// 已知怪癖：整 24 小时会输出 <c>0h</c>，因为只取 <see cref="TimeSpan.Hours"/>
        /// 而不累计天数。
        /// </para>
        /// </summary>
        public static string ToReadableTime(long ms)
        {
            if (ms < 1000)
                return ms + "ms";

            TimeSpan ts = TimeSpan.FromMilliseconds(ms);
            List<string> parts = new List<string>();

            // 小时只在总时长达到 1 小时时才出现，因此 1 天会显示成 0h 而不是 24h
            bool hasHour = ms >= 3600000;
            if (hasHour)
                parts.Add(ts.Hours + "h");

            if (ts.Minutes > 0)
                parts.Add(ts.Minutes + "min");

            if (hasHour || ts.Minutes > 0) {
                // 已经有更大的单位时，秒与毫秒分开写
                if (ts.Seconds > 0)
                    parts.Add(ts.Seconds + "s");
                if (ts.Milliseconds > 0)
                    parts.Add(ts.Milliseconds + "ms");
            } else if (ts.Seconds > 0) {
                // 纯秒级：毫秒作为小数部分，不补零
                parts.Add(ts.Milliseconds > 0
                    ? ts.Seconds + "." + ts.Milliseconds + "s"
                    : ts.Seconds + "s");
            }

            return string.Join(" ", parts);
        }

        /// <summary>Unix 时间戳转本地时间，<paramref name="second"/> 为真时按秒解释。</summary>
        public static DateTime UnixTimeStampToDateTime(long unixTimeStamp, bool second)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return second
                ? epoch.AddSeconds(unixTimeStamp).ToLocalTime()
                : epoch.AddMilliseconds(unixTimeStamp).ToLocalTime();
        }

        /// <summary>本地时间转 Unix 时间戳，<paramref name="second"/> 为真时以秒为单位。</summary>
        public static double DateTimeToUnixTimeStamp(DateTime dateTime, bool second)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double ms = (dateTime.ToUniversalTime() - epoch).TotalMilliseconds;
            return second ? Math.Floor(ms / 1000) : ms;
        }
    }
}
