using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Jvedio.Core.Crawler.Builtin
{
    /// <summary>
    /// 内置刮削器的一次查询：从文件名解析出的标题 / 年份，以及原始路径信息。
    /// </summary>
    public class BuiltinCrawlerQuery
    {
        /// <summary>清洗后的标题（用于搜索）。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>从文件名里识别出的年份，识别不到为 null。</summary>
        public int? Year { get; set; }

        /// <summary>看起来像单集（S01E02 / EP05 / 第3话 / [01]），用于在剧集与剧场版之间取舍。</summary>
        public bool IsEpisode { get; set; }

        /// <summary>番号（普通影片通常为空）。</summary>
        public string VID { get; set; } = string.Empty;

        /// <summary>原始文件名（含扩展名）。</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>完整路径。</summary>
        public string FullPath { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Title}{(Year.HasValue ? " (" + Year.Value + ")" : string.Empty)}";
        }
    }

    /// <summary>
    /// 从视频文件名里解析「标题 + 年份」。
    /// <para>
    /// 普通影片没有番号，只能靠文件名。常见形态：
    /// <c>流浪地球2.2023.1080p.BluRay.x264-GROUP.mkv</c>、
    /// <c>The.Matrix.1999.1080p.BluRay.x264.mkv</c>、
    /// <c>【高清】让子弹飞 (2010).mp4</c>。
    /// </para>
    /// <para>
    /// 规则：先摘掉不含中文的方括号/花括号（多为压制组名），再取年份之前的文本做标题，
    /// 最后剔除分辨率、编码、音轨等噪声词。宁可保守：清洗后标题为空时回退到原文件名。
    /// </para>
    /// </summary>
    public static class FileNameParser
    {
        private static readonly Regex YearRegex =
            new Regex(@"(?<!\d)(19\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled);

        private static readonly Regex BracketRegex =
            new Regex(@"\[[^\[\]]*\]|\{[^{}]*\}|\([^()]*\)|（[^（）]*）", RegexOptions.Compiled);

        private static readonly Regex SpaceRegex =
            new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly Regex CjkRegex =
            new Regex(@"[\u4e00-\u9fff]", RegexOptions.Compiled);

        /// <summary>单集标记：S01E02 / EP05 / 第3话 / [01] / [12v2]。</summary>
        private static readonly Regex EpisodeRegex = new Regex(
            @"(?:[Ss]\d{1,2}[Ee]\d{1,3})|(?:[Ee][Pp]?\.?\s?\d{1,4})|(?:第\s?\d{1,4}\s?[话話集])|(?:\[\d{1,4}(?:v\d)?\])",
            RegexOptions.Compiled);

        private static readonly Regex NoiseRegex = new Regex(
            @"(?<![A-Za-z0-9])(1080p|2160p|720p|480p|4k|8k|uhd|bluray|blu-ray|bdrip|brrip|webrip|web-dl|webdl|hdtv|dvdrip|remux|x264|x265|h264|h265|hevc|avc|aac|ac3|eac3|dts|ddp5\.?1|dd5\.?1|truehd|atmos|hdr10|hdr|dolbyvision|dolby\.vision|10bit|8bit|proper|repack|extended|remastered|uncut|limited|internal|multi|subs|chs|cht|eng|jpn|kor|ita|fre|ger|spa|directors\.cut|unrated)(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static BuiltinCrawlerQuery Parse(string path, string vid)
        {
            string fileName = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);
            string name = string.IsNullOrEmpty(fileName) ? string.Empty : Path.GetFileNameWithoutExtension(fileName);
            string original = name;

            var query = new BuiltinCrawlerQuery() {
                FileName = fileName,
                FullPath = path ?? string.Empty,
                VID = vid ?? string.Empty,
            };

            if (string.IsNullOrWhiteSpace(name)) {
                return query;
            }

            query.IsEpisode = EpisodeRegex.IsMatch(name);

            // 1. 先找年份：标题取年份之前的文本。
            //    顺序很重要——"让子弹飞 (2010)" 里的年份在括号内，先摘括号会把年份一起丢掉。
            string titlePart = name;
            Match yearMatch = YearRegex.Match(name);
            if (yearMatch.Success) {
                if (int.TryParse(yearMatch.Value, out int year)) {
                    query.Year = year;
                }
                titlePart = name.Substring(0, yearMatch.Index);
            }

            // 2. 去掉方括号 / 花括号 / 圆括号：含中文的保留内部文本（可能是中文片名），否则整段丢弃
            titlePart = BracketRegex.Replace(titlePart, m => {
                string inner = m.Value.Substring(1, m.Value.Length - 2);
                return CjkRegex.IsMatch(inner) ? " " + inner + " " : " ";
            });

            // 3. 分隔符统一成空格
            titlePart = titlePart.Replace('.', ' ').Replace('_', ' ').Replace('　', ' ');

            // 4. 去掉噪声词（分辨率 / 编码 / 音轨 / 语言标记等）
            titlePart = NoiseRegex.Replace(titlePart, " ");
            titlePart = SpaceRegex.Replace(titlePart, " ").Trim(' ', '-', '.', '·', '(', ')', '（', '）');

            // 5. 兜底：清洗后为空就退回原文件名（去掉扩展名）
            if (string.IsNullOrWhiteSpace(titlePart)) {
                titlePart = SpaceRegex.Replace(original.Replace('.', ' ').Replace('_', ' '), " ").Trim();
            }

            query.Title = titlePart;
            return query;
        }
    }
}
