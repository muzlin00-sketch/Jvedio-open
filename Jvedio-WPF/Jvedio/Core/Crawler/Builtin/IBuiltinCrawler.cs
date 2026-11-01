using Jvedio.Core.Net;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Crawler.Builtin
{
    /// <summary>
    /// 内置刮削器（源码实现，不依赖插件 DLL）。
    /// <para>
    /// 与插件刮削器的契约保持一致：返回的字典键名直接对应 <see cref="Jvedio.Entity.Video"/> 的属性名
    /// （Title / Plot / ReleaseYear / Duration / Director / ActorNames / Genre / Studio / Rating /
    /// BigImageUrl / SmallImageUrl / WebType / WebUrl / DataCode 等），
    /// 由 <c>Video.ParseDictInfo</c> 反射写入实体。
    /// </para>
    /// <para>
    /// 实现类不要吞掉异常：网络/解析失败直接抛，由 <see cref="BuiltinCrawlerManager"/> 记日志并换下一个源。
    /// </para>
    /// </summary>
    public interface IBuiltinCrawler
    {
        /// <summary>站点标识，写入 WebType 与 DataCode，如 douban / tmdb / bangumi。</summary>
        string Id { get; }

        /// <summary>显示名，用于日志与设置页。</summary>
        string Name { get; }

        /// <summary>是否启用（读配置）。</summary>
        bool IsEnabled { get; }

        /// <summary>是否需要用户先配置 API Key 才能用。</summary>
        bool RequiresKey { get; }

        /// <summary>按标题/年份查询，返回可写入实体的字典；未命中返回 null。</summary>
        Task<Dictionary<string, object>> GetInfo(BuiltinCrawlerQuery query, RequestHeader header, CancellationToken token);
    }
}
