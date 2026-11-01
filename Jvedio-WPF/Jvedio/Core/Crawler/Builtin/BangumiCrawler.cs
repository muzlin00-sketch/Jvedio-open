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
    /// Bangumi 番组计划内置刮削器（动画 / 日剧，无需 API Key）。
    /// <para>
    /// 搜索 <c>api.bgm.tv/search/subject/{keyword}?type=2</c>，详情 <c>api.bgm.tv/subject/{id}</c>。
    /// Bangumi 要求请求带 User-Agent，否则 403；图片地址是协议相对的（//lain.bgm.tv/...），需要补 https。
    /// </para>
    /// </summary>
    public class BangumiCrawler : IBuiltinCrawler
    {
        private const string SEARCH_URL = "https://api.bgm.tv/search/subject/{0}?type=2&responseGroup=large&max_results=10";
        // 详情必须用 v0 接口：老接口 /subject/{id} 不返回 infobox 与 tags，
        // 导演 / 动画制作 / 题材标签全靠它们。
        private const string DETAIL_URL = "https://api.bgm.tv/v0/subjects/{0}";
        private const string WEB_URL = "https://bgm.tv/subject/{0}";
        private const string USER_AGENT = "Jvedio/5.4 (https://github.com/hitchao/Jvedio)";

        public string Id => "bangumi";

        public string Name => "Bangumi";

        public bool RequiresKey => false;

        public bool IsEnabled {
            get {
                DownloadConfig config = ConfigManager.DownloadConfig;
                return config != null && config.EnableBangumiCrawler;
            }
        }

        public async Task<Dictionary<string, object>> GetInfo(BuiltinCrawlerQuery query, RequestHeader header, CancellationToken token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Title))
                return null;

            JObject best = await Search(query, header, token);
            if (best == null)
                return null;

            string id = BuiltinJson.Str(best, "id");
            if (string.IsNullOrWhiteSpace(id))
                return null;

            await BuiltinHttp.Delay(300, token);
            JObject detail = await GetDetail(id, header, token);
            if (detail == null)
                return null;

            return BuildDict(detail, best, id);
        }

        private async Task<JObject> Search(BuiltinCrawlerQuery query, RequestHeader header, CancellationToken token)
        {
            string url = string.Format(SEARCH_URL, Uri.EscapeDataString(query.Title));
            RequestHeader requestHeader = BuiltinHttp.CreateHeader(header, "https://bgm.tv/", USER_AGENT);
            JToken result = await BuiltinHttp.GetJson(url, requestHeader, token);

            JToken list = BuiltinJson.At(result, "list");
            if (!(list is JArray array) || array.Count == 0)
                return null;

            JObject best = null;
            double bestScore = -1;
            string target = Normalize(query.Title);

            foreach (JToken item in array) {
                if (!(item is JObject obj))
                    continue;

                string name = BuiltinJson.Str(obj, "name");
                string nameCn = BuiltinJson.Str(obj, "name_cn");
                double score = Math.Max(
                    TitleScore(target, Normalize(name)),
                    TitleScore(target, Normalize(nameCn)));

                string date = BuiltinJson.Str(obj, "air_date");
                int year = 0;
                if (date.Length >= 4)
                    int.TryParse(date.Substring(0, 4), out year);

                if (query.Year.HasValue) {
                    // 年份明确时，差两年以上直接排除：Bangumi 里同名短片/OVA/真人版太多，
                    // 放宽会让《The Matrix》匹配到 1971 年的实验短片。
                    if (year > 0 && Math.Abs(year - query.Year.Value) > 1)
                        continue;
                    if (year == query.Year.Value)
                        score += 0.35;
                    else if (year > 0)
                        score -= 0.15;
                } else {
                    int currentYear = DateTime.Now.Year;
                    if (year <= 0)
                        score -= 0.2;
                    else if (year > currentYear + 1)
                        score -= 0.5;
                }

                if (score > bestScore) {
                    bestScore = score;
                    best = obj;
                }
            }

            // Bangumi 的模糊匹配更危险（片库杂），阈值比豆瓣高
            return bestScore >= 0.7 ? best : null;
        }

        private async Task<JObject> GetDetail(string id, RequestHeader header, CancellationToken token)
        {
            string url = string.Format(DETAIL_URL, id);
            RequestHeader requestHeader = BuiltinHttp.CreateHeader(header, "https://bgm.tv/", USER_AGENT);
            JToken result = await BuiltinHttp.GetJson(url, requestHeader, token);
            if (result is JObject obj && obj["id"] != null)
                return obj;
            return null;
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

            HashSet<char> targetSet = new HashSet<char>(target);
            HashSet<char> candidateSet = new HashSet<char>(candidate);
            int common = targetSet.Intersect(candidateSet).Count();
            int total = Math.Max(targetSet.Count, candidateSet.Count);
            return total == 0 ? 0 : (double)common / total * 0.7;
        }

        private static Dictionary<string, object> BuildDict(JObject detail, JObject searchItem, string id)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            string nameCn = BuiltinJson.Str(detail, "name_cn");
            if (string.IsNullOrWhiteSpace(nameCn))
                nameCn = BuiltinJson.Str(searchItem, "name_cn");
            string name = BuiltinJson.Str(detail, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = BuiltinJson.Str(searchItem, "name");

            string title = string.IsNullOrWhiteSpace(nameCn) ? name : nameCn;
            if (!string.IsNullOrWhiteSpace(title))
                dict["Title"] = title;

            string summary = BuiltinJson.Str(detail, "summary");
            if (string.IsNullOrWhiteSpace(summary))
                summary = BuiltinJson.Str(searchItem, "summary");
            if (!string.IsNullOrWhiteSpace(summary)) {
                dict["Plot"] = summary;
                dict["Outline"] = summary.Length > 120 ? summary.Substring(0, 120) + "……" : summary;
            }

            string date = BuiltinJson.Str(detail, "date");
            if (string.IsNullOrWhiteSpace(date))
                date = BuiltinJson.Str(searchItem, "air_date");
            // "0000-00-00" 是 Bangumi 的占位值，不能当日期写进去
            if (!string.IsNullOrWhiteSpace(date) && !date.StartsWith("0000")) {
                dict["ReleaseDate"] = date.Length > 10 ? date.Substring(0, 10) : date;
                if (date.Length >= 4 && int.TryParse(date.Substring(0, 4), out int year) && year > 0)
                    dict["ReleaseYear"] = year;
            }

            int duration = BuiltinJson.Int(detail, "duration");
            if (duration > 0)
                dict["Duration"] = duration;

            float rating = BuiltinJson.Float(detail, "rating", "score");
            if (rating <= 0)
                rating = BuiltinJson.Float(searchItem, "rating", "score");
            if (rating > 0)
                dict["Rating"] = rating;

            // infobox 里的导演 / 动画制作
            JArray infobox = BuiltinJson.At(detail, "infobox") as JArray;
            if (infobox != null) {
                string director = ReadInfobox(infobox, "导演", "監督", "监督");
                if (!string.IsNullOrWhiteSpace(director))
                    dict["Director"] = director;

                // 只认"动画制作"：infobox 里的"製作"往往是制作委员会全名单，写进 Studio 会很难看
                string studio = ReadInfobox(infobox, "动画制作");
                if (!string.IsNullOrWhiteSpace(studio))
                    dict["Studio"] = studio;
            }

            JArray tags = BuiltinJson.At(detail, "tags") as JArray;
            if (tags != null) {
                List<string> genres = tags
                    .OfType<JObject>()
                    .OrderByDescending(item => BuiltinJson.Int(item, "count"))
                    .Select(item => BuiltinJson.Str(item, "name"))
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    // 最高频标签常常就是作品名本身，写进题材没有意义
                    .Where(tag => !string.Equals(tag, title, StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .ToList();
                if (genres.Count > 0)
                    dict["Genre"] = string.Join(" ", genres);
            }

            string poster = BuiltinJson.Str(detail, "images", "large");
            if (string.IsNullOrWhiteSpace(poster))
                poster = BuiltinJson.Str(searchItem, "images", "large");
            if (!string.IsNullOrWhiteSpace(poster)) {
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
                else if (poster.StartsWith("http://"))
                    poster = "https://" + poster.Substring("http://".Length);
                dict["BigImageUrl"] = poster;
                dict["SmallImageUrl"] = poster;
            }

            dict["WebUrl"] = string.Format(WEB_URL, id);
            dict["DataCode"] = id;
            dict["WebType"] = "bangumi";
            return dict;
        }

        private static string ReadInfobox(JArray infobox, params string[] keys)
        {
            foreach (JToken item in infobox) {
                if (!(item is JObject obj))
                    continue;
                string key = BuiltinJson.Str(obj, "key");
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (!keys.Any(k => key.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                JToken value = obj["value"];
                if (value is JArray array) {
                    List<string> parts = array
                        .OfType<JObject>()
                        .Select(v => BuiltinJson.Str(v, "v"))
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();
                    if (parts.Count > 0)
                        return string.Join(" / ", parts);
                }
                string text = BuiltinJson.Str(obj, "value");
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            return string.Empty;
        }
    }
}
