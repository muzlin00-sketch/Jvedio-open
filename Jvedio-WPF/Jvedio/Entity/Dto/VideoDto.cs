using System.Collections.Generic;

namespace Jvedio.Entity.Dto
{
    /// <summary>
    /// 影片列表项，供局域网 API / 平板端消费。
    /// <para>
    /// 与 <see cref="Jvedio.Entity.Video"/> 的区别：本类只含可序列化的纯数据，
    /// 图片以 URL 形式给出而非 <c>BitmapSource</c>，因此可直接 JSON 序列化。
    /// 字段与桌面端卡片显示的信息保持一致。
    /// </para>
    /// </summary>
    public class VideoSummaryDto
    {
        public long DataID { get; set; }

        public long DBId { get; set; }

        /// <summary>识别码，如 IPZ-229</summary>
        public string VID { get; set; }

        /// <summary>
        /// 文件名（不含目录与扩展名）。
        /// <para>
        /// 用于「没有识别码的短片」——这类记录 VID 为空，
        /// 前端若只显示识别码会全是"未知编号"，用它作为兜底显示名。
        /// </para>
        /// </summary>
        public string FileName { get; set; }

        public string Title { get; set; }

        /// <summary>发行日期，格式 yyyy-MM-dd</summary>
        public string ReleaseDate { get; set; }

        public int ReleaseYear { get; set; }

        /// <summary>文件大小（字节）</summary>
        public long Size { get; set; }

        /// <summary>时长（秒）</summary>
        public int Duration { get; set; }

        /// <summary>评分 0-5</summary>
        public float Grade { get; set; }

        public float Rating { get; set; }

        public int ViewCount { get; set; }

        public int FavoriteCount { get; set; }

        /// <summary>标签戳名称列表（HD、中文字幕等）</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>类别</summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>演员名列表</summary>
        public List<string> Actors { get; set; } = new List<string>();

        /// <summary>小图（卡片封面）URL</summary>
        public string SmallImageUrl { get; set; }

        /// <summary>大图（海报）URL</summary>
        public string BigImageUrl { get; set; }

        /// <summary>是否存在分段视频</summary>
        public bool HasSubSection { get; set; }

        /// <summary>是否有可播放的源文件</summary>
        public bool Playable { get; set; }
    }

    /// <summary>
    /// 影片详情，供平板端详情页使用。
    /// </summary>
    public class VideoDetailDto : VideoSummaryDto
    {
        public string Plot { get; set; }

        public string Outline { get; set; }

        public string Director { get; set; }

        public string Studio { get; set; }

        public string Publisher { get; set; }

        public string Series { get; set; }

        public string Country { get; set; }

        /// <summary>预览截图 URL 列表</summary>
        public List<string> PreviewImageUrls { get; set; } = new List<string>();

        /// <summary>演员详情（含头像 URL）</summary>
        public List<ActorDto> ActorList { get; set; } = new List<ActorDto>();

        /// <summary>视频流地址</summary>
        public string StreamUrl { get; set; }

        /// <summary>原始文件路径（桌面端调试用，平板端可忽略）</summary>
        public string FilePath { get; set; }
    }

    /// <summary>
    /// 演员信息，供列表与详情页展示。
    /// </summary>
    public class ActorDto
    {
        public long ActorID { get; set; }

        public string ActorName { get; set; }

        /// <summary>头像 URL</summary>
        public string ImageUrl { get; set; }

        public int Age { get; set; }

        public string Country { get; set; }

        public string Birthday { get; set; }

        public int Height { get; set; }

        public int Weight { get; set; }

        public string Cup { get; set; }
    }

    /// <summary>
    /// 分页列表响应。
    /// </summary>
    public class PagedResult<T>
    {
        public int Page { get; set; }

        public int PageSize { get; set; }

        public long TotalCount { get; set; }

        public int TotalPages { get; set; }

        public List<T> Items { get; set; } = new List<T>();
    }

    /// <summary>
    /// 影片库信息。
    /// </summary>
    public class LibraryDto
    {
        public long DBId { get; set; }

        public string Name { get; set; }

        public long Count { get; set; }

        /// <summary>库封面 URL</summary>
        public string ImageUrl { get; set; }
    }
}
