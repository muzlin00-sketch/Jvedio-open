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
    /// TMDB 内置刮削器（需要 API Key，免费注册）。
    /// <para>
    /// 搜索 /movie 或 /tv，详情带 credits 一次取回导演与演员；图片走 image.tmdb.org。
    /// 中文信息依赖 <c>language=zh-CN</c>：TMDB 的中文简介/片名覆盖率一般，缺失时回落原名。
    /// </para>
    /// <para>
    /// 没有配置 Key 时 <see cref="IsEnabled"/> 返回 false，调度器会静默跳过，不产生告警。
    /// 国内直连 api.themoviedb.org 可能超时，走系统/软件里配置的代理。
    /// </para>
    /// </summary>
    public class TmdbCrawler : IBuiltinCrawler
    {
        private const string API_BASE = "https://api.themoviedb.org/3";
        private const string IMAGE_BASE = "https://image.tmdb.org/t/p";
        private const string WEB_URL = "https://www.themoviedb.org/{0}/{1}";

        public string Id => "tmdb";

        public string Name => "TMDB";

        public bool RequiresKey => true;

        public bool IsEnabled {
            get {
                DownloadConfig config = ConfigManager.DownloadConfig;
                return config != null
                    && config.EnableTmdbCrawler
                    && !string.IsNullOrWhiteSpace(config.TmdbApiKey);
            }
        }

        public async Task<Dictionary<string, object>> GetInfo(BuiltinCrawlerQuery query, RequestHeader header, CancellationToken token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Title))
                return null;

            DownloadConfig config = ConfigManager.DownloadConfig;
            string apiKey = config == null ? string.Empty : (config.TmdbApiKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(apiKey))
                return null;

            string language = string.IsNullOrWhiteSpace(config.TmdbLanguage) ? "zh-CN" : config.TmdbLanguage.Trim();

            // 先按电影找，找不到再按剧集找（普通影片以电影为主）
            JObject match = await SearchAsync("movie", query, apiKey, language, header, token);
            string mediaType = "movie";
            if (match == null) {
                match = await SearchAsync("tv", query, apiKey, language, header, token);
                mediaType = "tv";
            }
            if (match == null)
                return null;

            string id = BuiltinJson.Str(match, "id");
            if (string.IsNullOrWhiteSpace(id))
                return null;

            JObject detail = await GetDetailAsync(mediaType, id, apiKey, language, header, token);
            if (detail == null)
                return null;

            return BuildDict(detail, mediaType, id, apiKey, language);
        }

        private async Task<JObject> SearchAsync(string mediaType, BuiltinCrawlerQuery query, string apiKey, string language,
            RequestHeader header, CancellationToken token)
        {
            string url = $"{API_BASE}/search/{mediaType}?api_key={Uri.EscapeDataString(apiKey)}"
                + $"&language={Uri.EscapeDataString(language)}"
                + $"&query={Uri.EscapeDataString(query.Title)}"
                + "&include_adult=false&page=1";
            if (query.Year.HasValue) {
                url += mediaType == "movie"
                    ? $"&year={query.Year.Value}"
                    : $"&first_air_date_year={query.Year.Value}";
            }

            RequestHeader requestHeader = BuiltinHttp.CreateHeader(header, "https://www.themoviedb.org/", BuiltinHttp.DesktopUserAgent);
            JToken result = await BuiltinHttp.GetJson(url, requestHeader, token);
            JToken results = BuiltinJson.At(result, "results");
            if (!(results is JArray array) || array.Count == 0)
                return null;

            return PickBest(array, query);
        }

        private async Task<JObject> GetDetailAsync(string mediaType, string id, string apiKey, string language,
            RequestHeader header, CancellationToken token)
        {
            string url = $"{API_BASE}/{mediaType}/{id}?api_key={Uri.EscapeDataString(apiKey)}"
                + $"&language={Uri.EscapeDataString(language)}"
                + "&append_to_response=credits";
            RequestHeader requestHeader = BuiltinHttp.CreateHeader(header, "https://www.themoviedb.org/", BuiltinHttp.DesktopUserAgent);
            JToken result = await BuiltinHttp.GetJson(url, requestHeader, token);
            if (result is JObject obj && obj["id"] != null)
                return obj;
            return null;
        }

        private static JObject PickBest(JArray results, BuiltinCrawlerQuery query)
        {
            JObject best = null;
            double bestScore = -1;
            string target = Normalize(query.Title);

            foreach (JToken item in results) {
                if (!(item is JObject obj))
                    continue;

                string title = BuiltinJson.Str(obj, "title");
                if (string.IsNullOrWhiteSpace(title))
                    title = BuiltinJson.Str(obj, "name");
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string originalTitle = BuiltinJson.Str(obj, "original_title");
                if (string.IsNullOrWhiteSpace(originalTitle))
                    originalTitle = BuiltinJson.Str(obj, "original_name");

                double score = Math.Max(
                    TitleScore(target, Normalize(title)),
                    TitleScore(target, Normalize(originalTitle)));

                string date = BuiltinJson.Str(obj, "release_date");
                if (string.IsNullOrWhiteSpace(date))
                    date = BuiltinJson.Str(obj, "first_air_date");
                int year = 0;
                if (date.Length >= 4)
                    int.TryParse(date.Substring(0, 4), out year);
                if (query.Year.HasValue) {
                    if (year == query.Year.Value)
                        score += 0.35;
                    else if (year > 0)
                        score -= 0.15;
                }
                if (score > bestScore) {
                    bestScore = score;
                    best = obj;
                }
            }

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

            HashSet<char> targetSet = new HashSet<char>(target);
            HashSet<char> candidateSet = new HashSet<char>(candidate);
            int common = targetSet.Intersect(candidateSet).Count();
            int total = Math.Max(targetSet.Count, candidateSet.Count);
            return total == 0 ? 0 : (double)common / total * 0.7;
        }

        private static Dictionary<string, object> BuildDict(JObject detail, string mediaType, string id, string apiKey, string language)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            string title = BuiltinJson.Str(detail, "title");
            if (string.IsNullOrWhiteSpace(title))
                title = BuiltinJson.Str(detail, "name");
            if (!string.IsNullOrWhiteSpace(title))
                dict["Title"] = title;

            string originalTitle = BuiltinJson.Str(detail, "original_title");
            if (string.IsNullOrWhiteSpace(originalTitle))
                originalTitle = BuiltinJson.Str(detail, "original_name");

            string overview = BuiltinJson.Str(detail, "overview");
            if (!string.IsNullOrWhiteSpace(overview)) {
                dict["Plot"] = overview;
                dict["Outline"] = overview.Length > 120 ? overview.Substring(0, 120) + "……" : overview;
            }

            string date = BuiltinJson.Str(detail, "release_date");
            if (string.IsNullOrWhiteSpace(date))
                date = BuiltinJson.Str(detail, "first_air_date");
            if (!string.IsNullOrWhiteSpace(date)) {
                dict["ReleaseDate"] = date.Length > 10 ? date.Substring(0, 10) : date;
                if (date.Length >= 4 && int.TryParse(date.Substring(0, 4), out int year))
                    dict["ReleaseYear"] = year;
            }

            int runtime = BuiltinJson.Int(detail, "runtime");
            if (runtime <= 0)
                runtime = BuiltinJson.Int(detail, "episode_run_time");
            if (runtime > 0)
                dict["Duration"] = runtime;

            List<string> genres = BuiltinJson.Strings(detail, "name", "genres");
            if (genres.Count > 0)
                dict["Genre"] = string.Join(" ", genres);

            List<string> companies = BuiltinJson.Strings(detail, "name", "production_companies");
            if (companies.Count > 0)
                dict["Studio"] = string.Join(" / ", companies);

            List<string> directors = BuiltinJson.Strings(detail, "name", "credits", "crew")
                .Where(name => name.Length > 0)
                .ToList();
            // crew 里混着编剧/制片，只保留 job=Director 的
            JArray crew = BuiltinJson.At(detail, "credits", "crew") as JArray;
            if (crew != null) {
                directors = crew
                    .OfType<JObject>()
                    .Where(item => string.Equals(BuiltinJson.Str(item, "job"), "Director", StringComparison.OrdinalIgnoreCase))
                    .Select(item => BuiltinJson.Str(item, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
            }
            if (directors.Count > 0)
                dict["Director"] = string.Join(" / ", directors);

            JArray cast = BuiltinJson.At(detail, "credits", "cast") as JArray;
            if (cast != null) {
                List<string> actors = cast
                    .OfType<JObject>()
                    .OrderBy(item => BuiltinJson.Int(item, "order"))
                    .Select(item => BuiltinJson.Str(item, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Take(10)
                    .ToList();
                if (actors.Count > 0)
                    dict["ActorNames"] = actors;
            }

            float rating = BuiltinJson.Float(detail, "vote_average");
            if (rating > 0)
                dict["Rating"] = rating;

            string posterPath = BuiltinJson.Str(detail, "poster_path");
            if (!string.IsNullOrWhiteSpace(posterPath)) {
                dict["BigImageUrl"] = IMAGE_BASE + "/w500" + posterPath;
                dict["SmallImageUrl"] = IMAGE_BASE + "/w185" + posterPath;
            }

            string backdropPath = BuiltinJson.Str(detail, "backdrop_path");
            if (!string.IsNullOrWhiteSpace(backdropPath))
                dict["ExtraImageUrl"] = IMAGE_BASE + "/original" + backdropPath;

            dict["WebUrl"] = string.Format(WEB_URL, mediaType, id);
            dict["DataCode"] = id;
            dict["WebType"] = "tmdb";
            return dict;
        }
    }
}
