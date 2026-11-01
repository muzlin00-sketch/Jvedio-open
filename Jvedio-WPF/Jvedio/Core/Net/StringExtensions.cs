using System;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// 字符串形式的 URL 扩展（原先由 SuperUtils 提供同名扩展方法）。
    /// <para>保留它们是为了让 <c>url.IsProperUrl()</c> 这类既有调用不用改写。</para>
    /// </summary>
    public static class StringExtensions
    {
        public static bool IsProperUrl(this string source)
        {
            return NetUtils.IsProperUrl(source);
        }

        public static string ToProperUrl(this string url, bool appendSplit = true)
        {
            return NetUtils.ToProperUrl(url, appendSplit);
        }

        public static Uri TryGetUri(this string url)
        {
            return NetUtils.TryGetUri(url);
        }

        public static Uri TryGetUri(this string baseurl, string url)
        {
            return NetUtils.TryGetUri(baseurl, url);
        }

        public static bool IsProperIPV4(this string ipString)
        {
            return NetUtils.IsProperIPV4(ipString);
        }
    }
}
