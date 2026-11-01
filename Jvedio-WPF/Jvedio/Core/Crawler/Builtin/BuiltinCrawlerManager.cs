using Jvedio.Core.Config;
using Jvedio.Core.Net;
using Jvedio.Core.Tasks;
using Jvedio.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Crawler.Builtin
{
    /// <summary>
    /// 内置刮削器调度：按配置顺序依次尝试豆瓣 / TMDB / Bangumi，命中一个就返回。
    /// <para>
    /// 与插件体系的关系：内置源只负责「普通影片」（电影、剧集、动画），
    /// 番号类资源仍然走原来的插件刮削器；两者互不影响，内置失败会自然回落到插件。
    /// </para>
    /// </summary>
    public static class BuiltinCrawlerManager
    {
        private static readonly object _lock = new object();
        private static List<IBuiltinCrawler> _crawlers;

        public static List<IBuiltinCrawler> Crawlers {
            get {
                EnsureInitialized();
                return _crawlers;
            }
        }

        /// <summary>总开关，关闭时完全不参与刮削。</summary>
        public static bool Enabled {
            get {
                DownloadConfig config = ConfigManager.DownloadConfig;
                return config != null && config.EnableBuiltinCrawler;
            }
        }

        private static void EnsureInitialized()
        {
            if (_crawlers != null)
                return;
            lock (_lock) {
                if (_crawlers != null)
                    return;
                _crawlers = new List<IBuiltinCrawler>() {
                    new DoubanCrawler(),
                    new TmdbCrawler(),
                    new BangumiCrawler(),
                };
            }
        }

        /// <summary>
        /// 依次尝试已启用的内置源。全部未命中或出错返回 null，由调用方回落到插件刮削器。
        /// </summary>
        public static async Task<Dictionary<string, object>> GetInfo(Video video, RequestHeader header, TaskLogger logger, CancellationToken token)
        {
            if (!Enabled || video == null)
                return null;

            BuiltinCrawlerQuery query = FileNameParser.Parse(video.Path, video.VID);
            if (string.IsNullOrWhiteSpace(query.Title))
                return null;

            EnsureInitialized();

            // 单集文件通常是动画/剧集，Bangumi 的条目质量明显好于豆瓣（豆瓣 suggest 只返回电影类），
            // 所以这类文件先问 Bangumi；电影按 豆瓣 → TMDB → Bangumi 的顺序。
            List<IBuiltinCrawler> ordered = query.IsEpisode
                ? _crawlers.OrderBy(crawler => string.Equals(crawler.Id, "bangumi", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList()
                : _crawlers.ToList();

            foreach (IBuiltinCrawler crawler in ordered) {
                if (token.IsCancellationRequested)
                    break;

                bool enabled;
                try {
                    enabled = crawler.IsEnabled;
                } catch (Exception ex) {
                    logger?.Warning($"内置刮削器[{crawler.Name}] 读取配置失败：{ex.Message}");
                    continue;
                }
                if (!enabled)
                    continue;

                try {
                    Dictionary<string, object> dict = await crawler.GetInfo(query, header, token);
                    if (dict == null)
                        continue;

                    object titleObject = dict.ContainsKey("Title") ? dict["Title"] : null;
                    string title = titleObject == null ? string.Empty : titleObject.ToString();
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    dict["PluginID"] = "内置·" + crawler.Name;
                    dict["WebType"] = crawler.Id;
                    logger?.Info($"内置刮削器[{crawler.Name}] 命中：{title}（查询：{query}）");
                    return dict;
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    logger?.Warning($"内置刮削器[{crawler.Name}] 失败：{ex.Message}");
                }
            }

            return null;
        }
    }
}
