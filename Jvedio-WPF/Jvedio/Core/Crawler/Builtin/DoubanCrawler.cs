using Jvedio.Core.Config;
using Jvedio.Core.Net;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Crawler.Builtin
{
    /// <summary>
    /// 豆瓣内置刮削器（无需 API Key）。
    /// <para>
    /// 走两个公开接口：
    /// 1. 搜索 <c>movie.douban.com/j/subject_suggest</c>（返回标题、年份、条目 id、类型）；
    /// 2. 详情 <c>m.douban.com/rexxar/api/v2/{movie|tv}/{id}</c>（简介、时长、评分、导演、演员、海报）。
    /// 详情接口要求 <c>Referer: https://m.douban.com/</c>，否则 403。
    /// </para>
    /// <para>
    /// 豆瓣有反爬：连续请求容易 403/需要验证码。这里只做单次搜索 + 单次详情，并限速 400ms；
    /// 失败就返回 null 交给下一个源，不重试。
    /// </para>
    /// </summary>
    public class DoubanCrawler : IBuiltinCrawler
    {
        private const string SUGGEST_URL = "https://movie.douban.com/j/subject_suggest?q=";
        private const string REXXAR_URL = "https://m.douban.com/rexxar/api/v2/{0}/{1}";
        private const string WEB_URL = "https://movie.douban.com/subject/{0}/";
        private const int RATE_LIMIT_MS = 400;

        public string Id => "douban";

        public string Name => "豆瓣";

        public bool RequiresKey => false;

        public bool IsEnabled {
            get {
                DownloadConfig config = ConfigManager.DownloadConfig;
                return config != null && config.EnableDoubanCrawler;
            }
        }

        public async Task<Dictionary<string, object>> GetInfo(BuiltinCrawlerQuery query, RequestHeader header, CancellationToken token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Title))
                return null;

            JArray suggestions = await Search(query, header, token);
            if (suggestions == null || suggestions.Count == 0)
                return null;

            JObject best = PickBest(suggestions, query);
            if (best == null)
                return null;

            string id = BuiltinJson.Str(best, "id");
            if (string.IsNullOrWhiteSpace(id))
                return null;

            string type = BuiltinJson.Str(best, "type");
            string subjectType = string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";

            await BuiltinHttp.Delay(RATE_LIMIT_MS, token);
            JObject detail = await GetDetail(subjectType, id, header, token);
            if (detail == null)
                return null;

            Dictionary<string, object> dict = BuildDict(detail, best, subjectType, id);
            return dict;
        }

        private async Task<JArray> Search(BuiltinCrawlerQuery query, RequestHeader header, CancellationToken token)
        {
            string url = SUGGEST_URL + Uri.EscapeDataString(query.Title);
            RequestHeader requestHeader = BuiltinHttp.CreateHeader(header, "https://movie.douban.com/", BuiltinHttp.DesktopUserAgent);
            ApplyCookie(requestHeader);

            JToken result = await BuiltinHttp.GetJson(url, requestHeader, token);
            if (result is JArray array)
                return array;
            return null;
        }

        private async Task<JObject> GetDetail(string subjectType, string id, RequestHeader header, CancellationToken token)
        {
            string url = string.Format(REXXAR_URL, subjectType, id);
            RequestHeader requestHeader = BuiltinHttp.CreateHeader(header, "https://m.douban.com/", BuiltinHttp.MobileUserAgent);
            ApplyCookie(requestHeader);

            JToken result = await BuiltinHttp.GetJson(url, requestHeader, token);
            if (result is JObject obj && obj["title"] != null)
                return obj;
            return null;
        }

        private void ApplyCookie(RequestHeader header)
        {
            DownloadConfig config = ConfigManager.DownloadConfig;
            if (config == null || string.IsNullOrWhiteSpace(config.DoubanCookie))
                return;
            header.Headers["Cookie"] = config.DoubanCookie.Trim();
        }

        /// <summary>在候选里挑标题最像、年份最对的一个。</summary>
        private static JObject PickBest(JArray suggestions, BuiltinCrawlerQuery query)
        {
            JObject best = null;
            double bestScore = -1;
            string target = Normalize(query.Title);

            foreach (JToken item in suggestions) {
                if (!(item is JObject obj))
                    continue;
                string title = BuiltinJson.Str(obj, "title");
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                string type = BuiltinJson.Str(obj, "type");
                if (type == "book" || type == "music" || type == "game")
                    continue;

                double score = TitleScore(target, Normalize(title));

                // 文件名带单集标记时，优先剧集条目，避免匹配到同名剧场版
                if (query.IsEpisode) {
                    if (string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase))
                        score += 0.3;
                    else
                        score -= 0.2;
                }

                int itemYear = BuiltinJson.Int(obj, "year");
                if (query.Year.HasValue) {
                    if (itemYear == query.Year.Value)
                        score += 0.35;
                    else if (itemYear > 0)
                        score -= 0.15;
                } else {
                    // 文件名没有年份时，同名片子可能有好几部（含未上映的真人版企划），
                    // 用"别选还没上映的、别选太老的"来兜底。
                    int currentYear = DateTime.Now.Year;
                    if (itemYear > currentYear + 1)
                        score -= 0.5;
                    else if (itemYear > 0 && itemYear < currentYear - 60)
                        score -= 0.2;
                }
                if (score > bestScore) {
                    bestScore = score;
                    best = obj;
                }
            }

            // 完全不像的候选不要：宁可不刮，也别把别的片子写进去
            return bestScore >= 0.5 ? best : null;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string lower = text.ToLowerInvariant();
            return new string(lower.Where(c => char.IsLetterOrDigit(c) || c > 0x4e00).ToArray());
        }

        private static double TitleScore(string target, string candidate)
        {
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(candidate))
                return 0;
            if (target == candidate)
                return 1.0;
            if (candidate.Contains(target) || target.Contains(candidate))
                return 0.8;

            // 字符集合重叠度，够用即可（不引第三方字符串相似度库）
            HashSet<char> targetSet = new HashSet<char>(target);
            HashSet<char> candidateSet = new HashSet<char>(candidate);
            int common = targetSet.Intersect(candidateSet).Count();
            int total = Math.Max(targetSet.Count, candidateSet.Count);
            return total == 0 ? 0 : (double)common / total * 0.7;
        }

        private static Dictionary<string, object> BuildDict(JObject detail, JObject suggestion, string subjectType, string id)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            string title = BuiltinJson.Str(detail, "title");
            if (string.IsNullOrWhiteSpace(title))
                title = BuiltinJson.Str(suggestion, "title");
            dict["Title"] = title;

            string intro = BuiltinJson.Str(detail, "intro");
            if (!string.IsNullOrWhiteSpace(intro)) {
                dict["Plot"] = intro;
                dict["Outline"] = intro.Length > 120 ? intro.Substring(0, 120) + "……" : intro;
            }

            int year = BuiltinJson.Int(detail, "year");
            if (year <= 0)
                year = BuiltinJson.Int(suggestion, "year");
            if (year > 0)
                dict["ReleaseYear"] = year;

            string pubdate = BuiltinJson.Str(detail, "pubdate");
            if (!string.IsNullOrWhiteSpace(pubdate))
                dict["ReleaseDate"] = pubdate.Length > 10 ? pubdate.Substring(0, 10) : pubdate;

            int duration = BuiltinJson.Int(detail, "durations");
            if (duration > 0)
                dict["Duration"] = duration;

            List<string> directors = BuiltinJson.Strings(detail, "name", "directors");
            if (directors.Count > 0)
                dict["Director"] = string.Join(" / ", directors);

            List<string> actors = BuiltinJson.Strings(detail, "name", "actors");
            if (actors.Count > 0)
                dict["ActorNames"] = actors;

            List<string> genres = BuiltinJson.Strings(detail, null, "genres");
            if (genres.Count > 0)
                dict["Genre"] = string.Join(" ", genres);

            List<string> countries = BuiltinJson.Strings(detail, null, "countries");
            if (countries.Count > 0)
                dict["Country"] = string.Join(" ", countries);

            float rating = BuiltinJson.Float(detail, "rating", "average");
            if (rating <= 0)
                rating = BuiltinJson.Float(detail, "rating", "value");
            if (rating > 0)
                dict["Rating"] = rating;

            string poster = BuiltinJson.Str(detail, "pic", "large");
            if (string.IsNullOrWhiteSpace(poster))
                poster = BuiltinJson.Str(suggestion, "img");
            if (!string.IsNullOrWhiteSpace(poster))
                dict["BigImageUrl"] = poster;

            string smallPoster = BuiltinJson.Str(detail, "pic", "normal");
            if (string.IsNullOrWhiteSpace(smallPoster))
                smallPoster = poster;
            if (!string.IsNullOrWhiteSpace(smallPoster))
                dict["SmallImageUrl"] = smallPoster;

            string cover = BuiltinJson.Str(detail, "cover_url");
            if (!string.IsNullOrWhiteSpace(cover))
                dict["ExtraImageUrl"] = cover;

            dict["WebUrl"] = string.Format(WEB_URL, id);
            dict["DataCode"] = id;
            dict["WebType"] = "douban";
            return dict;
        }
    }
}
