using System;
using System.Collections.Generic;

namespace Jvedio.Entity.Record
{
    /// <summary>
    /// 影片记录的纯数据表示。
    /// <para>
    /// 与 <see cref="Jvedio.Entity.Video"/> 的关系：
    /// <c>Video</c> 是「数据库映射 + WPF 绑定 + 运行时状态」三合一的胖实体，
    /// 含 <c>BitmapSource</c>、<c>ObservableCollection</c> 等不可序列化成员，
    /// 无法直接跨进程 / 跨平台传输。
    /// </para>
    /// <para>
    /// <c>VideoRecord</c> 只保留**可持久化、可序列化**的字段，
    /// 字段集合与数据库 <c>metadata</c> + <c>metadata_video</c> 两表一致。
    /// 它是「数据真相」，<c>Video</c> 是它在桌面端的 UI 投影。
    /// </para>
    /// <para>
    /// 设计约束：本类**不得**引用 WPF 类型，也不得引用闭源 DLL，
    /// 以便后续迁入跨平台的 <c>Jvedio.Core</c>。
    /// </para>
    /// </summary>
    public class VideoRecord
    {
        #region "metadata 表"

        public long DataID { get; set; }

        public long DBId { get; set; }

        public string Title { get; set; } = string.Empty;

        /// <summary>文件大小（字节）</summary>
        public long Size { get; set; }

        /// <summary>视频文件完整路径</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>文件 MD5，用于去重</summary>
        public string Hash { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        /// <summary>发行日期，格式 yyyy-MM-dd</summary>
        public string ReleaseDate { get; set; } = string.Empty;

        public int ReleaseYear { get; set; }

        public int ViewCount { get; set; }

        /// <summary>对应 <c>Jvedio.Core.Enums.DataType</c>：0=Video 1=Picture 2=Game 3=Comics</summary>
        public int DataType { get; set; }

        public float Rating { get; set; }

        public int RatingCount { get; set; }

        public int FavoriteCount { get; set; }

        /// <summary>类别，以分隔符连接的字符串</summary>
        public string Genre { get; set; } = string.Empty;

        /// <summary>评分 0-5</summary>
        public float Grade { get; set; }

        /// <summary>标签，以分隔符连接的字符串</summary>
        public string Label { get; set; } = string.Empty;

        public string ViewDate { get; set; } = string.Empty;

        public string FirstScanDate { get; set; } = string.Empty;

        public string LastScanDate { get; set; } = string.Empty;

        public string CreateDate { get; set; } = string.Empty;

        public string UpdateDate { get; set; } = string.Empty;

        /// <summary>1=文件存在 0=已丢失</summary>
        public int PathExist { get; set; }

        /// <summary>标签 ID，逗号分隔（由子查询聚合，非物理列）</summary>
        public string TagIDs { get; set; } = string.Empty;

        #endregion

        #region "metadata_video 表"

        public long MVID { get; set; }

        /// <summary>识别码，如 IPZ-229</summary>
        public string VID { get; set; } = string.Empty;

        public string Series { get; set; } = string.Empty;

        /// <summary>对应 <c>Jvedio.Core.Enums.VideoType</c>：0=Normal 1=UnCensored 2=Censored 3=Europe</summary>
        public int VideoType { get; set; }

        public string Director { get; set; } = string.Empty;

        public string Studio { get; set; } = string.Empty;

        public string Publisher { get; set; } = string.Empty;

        public string Plot { get; set; } = string.Empty;

        public string Outline { get; set; } = string.Empty;

        /// <summary>时长（秒）</summary>
        public int Duration { get; set; }

        /// <summary>分段视频路径，以分隔符连接的字符串</summary>
        public string SubSection { get; set; } = string.Empty;

        /// <summary>图片 URL，以分隔符连接的字符串</summary>
        public string ImageUrls { get; set; } = string.Empty;

        public string WebType { get; set; } = string.Empty;

        public string WebUrl { get; set; } = string.Empty;

        public string ExtraInfo { get; set; } = string.Empty;

        #endregion

        #region "派生字段（由子查询或运行时计算，不落库）"

        /// <summary>演员名，以分隔符连接的字符串（子查询聚合）</summary>
        public string ActorNames { get; set; } = string.Empty;

        #endregion

        /// <summary>
        /// 是否有分段视频。
        /// </summary>
        public bool HasSubSection {
            get { return !string.IsNullOrEmpty(SubSection); }
        }

        /// <summary>
        /// 是否存在可播放的源文件。
        /// <para>注意：会访问文件系统，批量场景下建议由调用方缓存结果。</para>
        /// </summary>
        public bool Playable {
            get { return !string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path); }
        }

        public VideoRecord Clone()
        {
            return (VideoRecord)MemberwiseClone();
        }
    }
}
