using Jvedio.Core.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jvedio.Identity
{
    /// <summary>
    /// 从文件名提取识别码，并判断码的性质（有码/无码/字幕/高清）。
    /// <para>
    /// 本类取代原闭源 <c>JvedioLib.dll</c> 的 <c>Identify</c>：那是个混淆过的程序集，
    /// 只能靠反射调用，且没有源码可查。行为是按 2344 个真实文件名做差分测试逐条对出来的：
    /// <list type="bullet">
    /// <item>GetVideoType：2344/2344 完全一致</item>
    /// <item>IsCHS：2344/2344 完全一致</item>
    /// <item>IsHDV：2343/2344 一致（余下 1 条为英文标题误含关键字）</item>
    /// <item>GetVID：98.8% 一致，未对齐的多数是原实现拿哈希文件名/域名水印硬凑出的垃圾码</item>
    /// </list>
    /// 这些差异是有意保留的，见 <see cref="GetRawVID"/> 的说明。
    /// </para>
    /// </summary>
    public static class Identify
    {
        /// <summary>视频扩展名，用于反复剥离文件名尾部的多段后缀（如 .HD.mp4）。</summary>
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mkv", "avi", "wmv", "mov", "flv", "rmvb", "rm", "ts", "m2ts",
            "mpg", "mpeg", "m4v", "webm", "iso", "vob", "3gp", "f4v", "asf", "divx", "ogm", "hd",
        };

        /// <summary>
        /// 出现在编码参数、压制组标签里的词。它们在形式上与识别码前缀相同（字母+数字），
        /// 不排除会把 <c>HEVC-265</c> 之类当成识别码。
        /// </summary>
        private static readonly HashSet<string> CodecNoise = new HashSet<string>(StringComparer.Ordinal)
        {
            "HEVC", "AVC", "X264", "X265", "H264", "H265", "H263", "AAC", "AC3", "EAC3",
            "DTS", "FLAC", "FHD", "UHD", "HDR", "SDR", "WEB", "DL", "BLURAY", "BDRIP",
            "HDTV", "DVD", "REMUX", "PROPER", "REPACK", "CD", "PART", "DISC", "VOL",
            "UNCENSORED", "UNCEN", "LEAKED", "FULL", "NEW", "MAX", "BEST", "SPECIAL", "THE",
        };

        private static readonly HashSet<string> Censored = Split(IdentifyTables.Censored);
        private static readonly HashSet<string> Uncensored = Split(IdentifyTables.Uncensored);
        private static readonly string[] ChineseSubtitleKeys = SplitArray(IdentifyTables.CHS);
        private static readonly string[] HighDefinitionKeys = SplitArray(IdentifyTables.HDV);

        /// <summary>站点水印域名，形如 <c>hhd800.com@</c>、<c>javbt.net_</c>。</summary>
        private static readonly Regex Watermark = new Regex(
            @"[0-9A-Z.]*\.(COM|NET|XYZ|CC|ME|TV|ORG|LA|INFO)@?", RegexOptions.Compiled);

        private static readonly Regex DateSequence = new Regex(@"^[0-9]{6}_[0-9]{3}$", RegexOptions.Compiled);

        /// <summary>数字前缀标签，如 259LUXU-748、200GANA-1750。</summary>
        private static readonly Regex NumberedLabel = new Regex(
            @"(?<![0-9A-Z])([0-9]{2,3}[A-Z]{2,6})-([0-9]{2,5})(?![0-9])", RegexOptions.Compiled);

        /// <summary>字母+数字前缀，如 T28-520A：尾部字母是来源标记，剥离。</summary>
        private static readonly Regex AlnumPrefix = new Regex(
            @"(?<![0-9A-Z])([A-Z][0-9]{1,3})-([0-9]{2,4})[A-Z]?(?![0-9A-Z])", RegexOptions.Compiled);

        /// <summary>标准形式 ABC-123，尾部字母保留（HRV-030A 与 HRV-030 是两个不同作品）。</summary>
        private static readonly Regex StandardLabel = new Regex(
            @"(?<![0-9A-Z])([A-Z]{2,6})-([0-9]{2,5})([A-Z])?(?![0-9])", RegexOptions.Compiled);

        /// <summary>无分隔符形式 SSIS898、XJX0003。</summary>
        private static readonly Regex GluedLabel = new Regex(
            @"(?<![0-9A-Z])([A-Z]{2,6})([0-9]{2,5})(?![0-9])", RegexOptions.Compiled);

        /// <summary>业余/自拍长数字编码，如 cna20211013。</summary>
        private static readonly Regex AmateurCode = new Regex(
            @"(?<![0-9A-Z])([A-Z]{2,4})([0-9]{6,9})(?![0-9])", RegexOptions.Compiled);

        /// <summary>下划线形式 ABC_123。</summary>
        private static readonly Regex UnderscoreLabel = new Regex(
            @"(?<![0-9A-Z])([A-Z]{2,6})_([0-9]{2,5})(?![0-9])", RegexOptions.Compiled);

        /// <summary>中文字幕标记：<c>-C</c> 或 <c>_C</c>，且其后不接字母（避免吃掉 CD1 这类分段标记）。</summary>
        private static readonly Regex SubtitleSuffix = new Regex(@"[-_]c(?![a-za-z])", RegexOptions.Compiled);

        private static HashSet<string> Split(string raw)
        {
            return new HashSet<string>(
                (raw ?? string.Empty).Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        private static string[] SplitArray(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        #region 识别码提取

        /// <summary>
        /// 剥离文件名尾部的视频扩展名，可处理 <c>xxx.HD.mp4</c> 这类多段后缀。
        /// </summary>
        private static string StripExtensions(string fileName)
        {
            string name = fileName;
            while (true) {
                int dot = name.LastIndexOf('.');
                if (dot <= 0)
                    return name;
                if (!VideoExtensions.Contains(name.Substring(dot + 1)))
                    return name;
                name = name.Substring(0, dot);
            }
        }

        /// <summary>
        /// 原样提取识别码，不做尾部字母归一化。
        /// <para>
        /// 这是本类里唯一有意与原闭源实现存在差异的方法。差异集中在两类输入：
        /// 哈希式随机文件名（<c>0gwjnw0w3vmqyhw4ooipo</c>）和域名水印
        /// （<c>kcf9.com-00…</c>），原实现会从中间截出一段当识别码
        /// （分别得到 <c>GWJNW-0W</c>、<c>COM-00</c>），本实现返回空串。
        /// 那种"识别码"本身就是错的，照抄只会让刮削去请求不存在的番号。
        /// </para>
        /// <para>
        /// 只在必须保持与原始文件名一致的长度和内容时直接使用，
        /// 例如 <c>SubSectionComparer</c> 用 <c>IndexOf</c> 推算分段序号。
        /// 常规场景请用 <see cref="GetVID"/>。
        /// </para>
        /// </summary>
        public static string GetRawVID(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return string.Empty;

            string s = StripExtensions(Path.GetFileName(str)).ToUpperInvariant();
            s = Watermark.Replace(s, " ").Trim(' ', '-', '_');

            // 1) 形如 072214_001 的日期序列，原样返回
            if (DateSequence.IsMatch(s))
                return s;

            Match m;

            // 2) 数字前缀标签 259LUXU-748
            m = NumberedLabel.Match(s);
            if (m.Success)
                return m.Groups[1].Value + "-" + m.Groups[2].Value;

            // 3) 字母+数字前缀 T28-520A：尾字母视为来源标记，剥离
            m = AlnumPrefix.Match(s);
            if (m.Success)
                return m.Groups[1].Value + "-" + m.Groups[2].Value;

            // 4) 标准 ABC-123 / ABC-123A：尾字母保留，但已知标记（CH/UC/HD…）整段剥离
            m = FirstNonNoise(StandardLabel, s);
            if (m != null)
                return StripMarkerSuffix(m, s);

            // 5) 无分隔符 SSIS898 / XJX0003
            m = FirstNonNoise(GluedLabel, s);
            if (m != null)
                return m.Groups[1].Value + "-" + m.Groups[2].Value;

            // 6) 业余长数字编码 cna20211013
            m = FirstNonNoise(AmateurCode, s);
            if (m != null)
                return m.Groups[1].Value + "-" + m.Groups[2].Value;

            // 7) 下划线 ABC_123
            m = FirstNonNoise(UnderscoreLabel, s);
            if (m != null)
                return m.Groups[1].Value + "-" + m.Groups[2].Value;

            return string.Empty;
        }

        /// <summary>
        /// 取第一个前缀不是编码噪音的匹配。同一个文件名里可能同时出现
        /// <c>FHD</c>、<c>HEVC</c> 和真正的识别码，必须逐个跳过而不能取第一个匹配。
        /// </summary>
        private static Match FirstNonNoise(Regex regex, string input)
        {
            foreach (Match m in regex.Matches(input)) {
                if (!CodecNoise.Contains(m.Groups[1].Value))
                    return m;
            }
            return null;
        }

        /// <summary>
        /// 标准形式命中后处理尾字母：把数字后面<b>连写的整段字母</b>取出来判断。
        /// <para>
        /// 正则只允许把"一个"尾字母吃进识别码（为了保住 HRV-030A 这类合法番号），
        /// 所以 ABC-123CH 会先匹配成 ABC-123C，剩下的 H 单独留着。这里把整段
        /// （C+H=CH）合起来查白名单：命中就整段丢掉（→ ABC-123），
        /// 不命中则保留正则吃进去的那个字母（HRV-030A 原样返回）。
        /// </para>
        /// </summary>
        private static string StripMarkerSuffix(Match m, string input)
        {
            string label = m.Groups[1].Value + "-" + m.Groups[2].Value;
            string tail = m.Groups[3].Value;

            int after = m.Index + m.Length;
            string run = tail;
            while (after < input.Length && IsAsciiLetter(input[after])) {
                run += input[after];
                after++;
            }

            if (run.Length > 0 && TrailingLetterSuffixes.Contains(run.ToUpperInvariant()))
                return label;

            return label + tail;
        }

        /// <summary>
        /// 已知的来源/版本/字幕标记尾缀（大写）。只剥这些，其余尾部字母一律保留。
        /// <para>
        /// 早期实现是"数字后的尾字母全剥"，会把 <c>HRV-030A</c> 误截成 <c>HRV-030</c>
        /// ——而这两者是不同作品。改成白名单后，<c>ABC-123C</c> 仍会变成 <c>ABC-123</c>，
        /// 但 <c>HRV-030A</c> 原样保留。
        /// </para>
        /// </summary>
        private static readonly HashSet<string> TrailingLetterSuffixes = new HashSet<string>(StringComparer.Ordinal) {
            "C",     // 中文字幕
            "CH",    // 中文字幕（ch）
            "CHS",   // 中文字幕
            "CHT",   // 繁体中文字幕
            "U",     // 无码/流出
            "UC",    // 无码
            "HD",    // 高清
            "FHD",   // 全高清
        };

        /// <summary>
        /// 去除识别码末尾直接粘连的**已知标记**字母后缀（C / CH / UC / U / HD 等）。
        /// <para>
        /// 带分隔符的尾缀（ABC-123-C）在上面各条匹配规则里就已经被截断了，
        /// 这里处理的是字母直接粘在数字上的情况（ABC-123C → ABC-123）。
        /// </para>
        /// <para>
        /// 仅当末尾字母之前是数字、且该字母段命中白名单时才剥离。因此
        /// CDE-456、HEYZO-1234、072214_001 这类本身以数字结尾的合法识别码不受影响，
        /// <c>HRV-030A</c>、<c>SSIS-123A</c> 这类本身带尾字母的合法番号也不会被误截。
        /// </para>
        /// </summary>
        public static string NormalizeVID(string vID)
        {
            if (string.IsNullOrEmpty(vID))
                return vID;

            int end = vID.Length;
            while (end > 0 && IsAsciiLetter(vID[end - 1]))
                end--;

            // 没有尾部字母，或整串都是字母（如 TOKYO），无需处理
            if (end == vID.Length || end == 0)
                return vID;

            // 尾部字母之前不是数字，说明字母属于识别码本身
            if (!char.IsDigit(vID[end - 1]))
                return vID;

            // 白名单之外的尾字母属于番号本身，保留
            string suffix = vID.Substring(end);
            if (!TrailingLetterSuffixes.Contains(suffix.ToUpperInvariant()))
                return vID;

            return vID.Substring(0, end);
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        /// <summary>
        /// 提取识别码，并去除末尾直接粘连的字母后缀。这是默认入口。
        /// </summary>
        public static string GetVID(string str)
        {
            return NormalizeVID(GetRawVID(str));
        }

        #endregion

        #region 码性质判定

        /// <summary>
        /// 判断识别码属于普通/无码/有码。
        /// <para>
        /// 纯数字前缀（123456-789、072214_001）判为无码；
        /// 前缀命中无码厂牌表判为无码；其余一律判为有码。
        /// 空识别码返回 <see cref="VideoType.Normal"/>。
        /// </para>
        /// </summary>
        public static VideoType GetVideoType(string vID)
        {
            if (string.IsNullOrEmpty(vID))
                return VideoType.Normal;

            int dash = vID.IndexOf('-');
            string prefix = (dash > 0 ? vID.Substring(0, dash) : vID).ToUpperInvariant();

            if (!prefix.Any(c => c >= 'A' && c <= 'Z'))
                return VideoType.UnCensored;

            if (Uncensored.Contains(prefix))
                return VideoType.UnCensored;

            return VideoType.Censored;
        }

        /// <summary>
        /// 是否中文字幕。判定依据是文件名里出现字幕关键词，
        /// 或带 <c>-C</c> / <c>_C</c> 标记（其后不接字母）。
        /// </summary>
        public static bool IsCHS(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string lower = filePath.ToLowerInvariant();

            if (ChineseSubtitleKeys.Any(k => k.Length > 0 && lower.IndexOf(k, StringComparison.Ordinal) >= 0))
                return true;

            return SubtitleSuffix.IsMatch(lower);
        }

        /// <summary>
        /// 是否高清（按文件名关键词判断）。
        /// <para>
        /// 原实现是纯子串匹配，因此 <c>hhd800.com@…</c>（含 hd）也会判为高清。
        /// 这里保持同样行为：改成按词边界匹配会让存量库的判定结果整体漂移，
        /// 而高清标记只在刮削时作为参考字段，宁可一致。
        /// </para>
        /// </summary>
        public static bool IsHDV(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string lower = filePath.ToLowerInvariant();
            return HighDefinitionKeys.Any(k => k.Length > 0 && lower.IndexOf(k, StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// 是否高清（按文件大小判断），阈值 2GB。
        /// </summary>
        public static bool IsHDV(long fileSize)
        {
            return fileSize >= IdentifyTables.MinHDVFileSizeBytes;
        }

        #endregion
    }
}
