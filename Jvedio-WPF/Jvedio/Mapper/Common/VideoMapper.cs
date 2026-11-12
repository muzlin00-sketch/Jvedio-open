using Jvedio.Entity;
using Jvedio.Mapper.BaseMapper;
using Jvedio.Core.ORM.Wrapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jvedio.Mapper
{
    // todo 代码检视
    public class VideoMapper : BaseMapper<Video>
    {

        public const string SQL_BASE =
            " FROM metadata_video JOIN metadata on metadata.DataID=metadata_video.DataID ";

        public const string SQL_JOIN_ACTOR =
            " JOIN metadata_to_actor on metadata_to_actor.DataID=metadata.DataID" +
            " JOIN actor_info on metadata_to_actor.ActorID=actor_info.ActorID ";

        public const string SQL_JOIN_LABEL =
            " JOIN metadata_to_label on metadata_to_label.DataID=metadata.DataID ";

        public const string SQL_JOIN_TAGSTAMP =
            " JOIN metadata_to_tagstamp on metadata_to_tagstamp.DataID=metadata.DataID ";

        public const string SQL_LEFT_JOIN_TAGSTAMP =
            " LEFT JOIN metadata_to_tagstamp on metadata_to_tagstamp.DataID=metadata.DataID ";

        public const string SQL_JOIN_COMMON_PICTURE_EXIST =
            " JOIN common_picture_exist on common_picture_exist.DataID=metadata.DataID ";


        public static string[] SelectFields { get; set; } =
        {
            "metadata.DataID",
            "DBId",
            "Title",
            "Size",
            "Path",
            "Hash",
            "Country",
            "ReleaseDate",
            "ReleaseYear",
            "ViewCount",
            "DataType",
            "Rating",
            "RatingCount",
            "FavoriteCount",
            "Genre",
            "Grade",
            $"(select group_concat(LabelName,'{Jvedio.Common.ConstValues.Separator}') from metadata_to_label where metadata_to_label.DataID=metadata.DataID) as Label",
            "ViewDate",
            "FirstScanDate",
            "LastScanDate",
            "CreateDate",
            "UpdateDate",
            // PathExist / ExtraInfo 必须查出来：编辑保存是「整实体写回」，
            // 少了这两列就会把 PathExist 刷成 0、ExtraInfo 清空
            "PathExist",
            "ExtraInfo",
            "(select group_concat(TagID,',') from metadata_to_tagstamp where metadata_to_tagstamp.DataID=metadata.DataID)  as TagIDs ",

            "VID",
            "MVID",
            "VideoType",
            "Series",
            "Director",
            "Studio",
            "Publisher",
            "Plot",
            "Outline",
            "Duration",
            "SubSection",
            "ImageUrls",
            "WebType",
            "WebUrl",
        };

        public Video SelectVideoByID(long dataId)
        {
            SelectWrapper<Video> wrapper = new SelectWrapper<Video>();

            wrapper.Select(SelectFields).Eq("metadata.DataID", dataId);
            string sql = $"{wrapper.ToSelect(false)} FROM metadata_video " +
                        "JOIN metadata " +
                        "on metadata.DataID=metadata_video.DataID " + wrapper.ToWhere(false);
            List<Dictionary<string, object>> list = Select(sql);
            List<Video> videos = ToEntity<Video>(list, typeof(Video).GetProperties(), false);
            if (videos != null && videos.Count > 0) {
                Video video = videos[0];
                string actor_sql = "select actor_info.* from actor_info " +
                    "JOIN metadata_to_actor on metadata_to_actor.ActorID=actor_info.ActorID " +
                    "join metadata_video on metadata_video.DataID=metadata_to_actor.DataID " +
                    $"where metadata_video.DataID={dataId};";
                List<Dictionary<string, object>> actor_list = Select(actor_sql);
                List<ActorInfo> actorInfos = ToEntity<ActorInfo>(actor_list, typeof(ActorInfo).GetProperties());
                video.ActorInfos = actorInfos;
                return video;
            }

            return null;
        }

        public void SaveActor(Video video, List<ActorInfo> newActorInfos)
        {
            List<ActorInfo> oldActorInfos = video.ActorInfos;
            if (oldActorInfos == null)
                oldActorInfos = new List<ActorInfo>();
            if (newActorInfos == null)
                newActorInfos = new List<ActorInfo>();
            if (newActorInfos.SequenceEqual(oldActorInfos))
                return;

            // 删除，新增
            oldActorInfos = oldActorInfos.OrderBy(arg => arg.ActorID).ToList();
            newActorInfos = newActorInfos.OrderBy(arg => arg.ActorID).ToList();
            List<ActorInfo> to_delete = oldActorInfos.Except(newActorInfos).ToList();
            List<ActorInfo> to_create = newActorInfos.Except(oldActorInfos).ToList();

            // 删除
            if (to_delete.Count > 0) {
                // ('1','2','3')
                string sql = $"delete from metadata_to_actor " +
                    $"where DataID={video.DataID} " +
                    $"and ActorID in ('{string.Join("','", to_delete.Select(arg => arg.ActorID))}')";
                ExecuteNonQuery(sql);
            }

            // 新增
            if (to_create.Count > 0) {
                List<string> create = new List<string>();
                to_create.ForEach(arg => {
                    create.Add($"({video.DataID},{arg.ActorID})");
                });

                string sql = $"insert or ignore into metadata_to_actor(DataID,ActorID) " +
                    $"values {string.Join(",", create)}";
                ExecuteNonQuery(sql);
            }
        }

        /// <summary>
        /// 按 <c>DataID</c> 更新 <c>metadata_video</c> 的单个字段。
        /// <para>
        /// <b>不能用 <c>UpdateFieldById</c></b>：<see cref="Video"/> 的 <c>[TableId]</c> 是 <c>MVID</c>，
        /// 而调用方手上只有 <c>DataID</c>。两表的自增序列已经不同步
        /// （<c>sqlite_sequence</c>: metadata=4533 / metadata_video=4522），
        /// 一旦导入过就会 <c>MVID != DataID</c>，按主键更新会改到<b>别的影片</b>上。
        /// </para>
        /// </summary>
        public bool UpdateFieldByDataID(string field, object value, long dataID)
        {
            if (string.IsNullOrEmpty(field) || dataID <= 0)
                return false;

            SelectWrapper<Video> wrapper = new SelectWrapper<Video>();
            wrapper.Eq("DataID", dataID);
            return UpdateField(field, value == null ? string.Empty : value.ToString(), wrapper) > 0;
        }

        /// <summary>
        /// 多值字段（当前只有 <c>Genre</c>）按「分隔符 token」精确匹配，把条件写进 wrapper。
        /// <para>
        /// 字段值形如 <c>A\u0007B</c>（首尾没有分隔符）。<c>like '%A%'</c> 是子串匹配，
        /// 「高」会命中「高畫質」（全库 17 条 → 1176 条）。正确做法是先把字段两侧补上分隔符
        /// （<c>'\u0007' || Genre || '\u0007'</c>），再匹配 <c>'\u0007A\u0007'</c>：
        /// wrapper 的 <c>Like</c> 会自动在两侧再加 <c>%</c>，只有补上分隔符才不会退化。
        /// </para>
        /// <para>
        /// <c>||</c> 是 SQLite 的字符串拼接。本程序只会连 SQLite
        /// （<c>Window_Main</c> 里 <c>CurrentDataBaseType</c> 固定写死 SQLite），
        /// 所以不必为 MySQL 的 <c>||</c>=OR 语义做兼容。
        /// </para>
        /// <para>
        /// <b>桌面端（点侧边栏类别）与手机端（<c>/api/videos?genres=</c>）必须共用本方法</b>：
        /// 以前两端各写一套（桌面 token、手机子串），同一个类别点出来的条数不一样。
        /// </para>
        /// </summary>
        public static void ApplyTokenFilter<T>(IWrapper<T> wrapper, string field, IEnumerable<string> values)
        {
            if (wrapper == null || string.IsNullOrEmpty(field) || values == null)
                return;

            List<string> tokens = values
                .Where(arg => !string.IsNullOrWhiteSpace(arg))
                // SelectWrapper 的取值不做单引号转义，值里带 ' 时在这里双写
                .Select(arg => arg.Trim().Replace("'", "''"))
                .Distinct()
                .ToList();
            if (tokens.Count == 0)
                return;

            char sep = Jvedio.Common.ConstValues.Separator;
            string paddedField = "'" + sep + "' || " + field + " || '" + sep + "'";

            bool first = true;
            foreach (string token in tokens) {
                if (!first)
                    wrapper.Or();
                wrapper.Like(paddedField, sep + token + sep);
                if (first)
                    wrapper.LeftBracket();      // 括号带在本组第一个条件上
                first = false;
            }
            wrapper.RightBracket();             // 括号带在本组最后一个条件上
        }

        /// <summary>
        /// 取 <see cref="ApplyTokenFilter{T}"/> 生成的裸 SQL 谓词（已带括号），供手机端拼 where 列表。
        /// <para>与客户端同一个实现，避免两边口径再次分叉。</para>
        /// </summary>
        public static string TokenPredicate(string field, IEnumerable<string> values)
        {
            SelectWrapper<Video> wrapper = new SelectWrapper<Video>();
            ApplyTokenFilter(wrapper, field, values);
            string where = wrapper.ToWhere(false).Trim();
            if (where.Length == 0)
                return string.Empty;

            return where.StartsWith("where", StringComparison.OrdinalIgnoreCase)
                ? where.Substring("where".Length).Trim()
                : where;
        }

        public void deleteVideoByIds(List<string> idList)
        {
            if (idList == null || idList.Count == 0)
                return;
            StringBuilder builder = new StringBuilder();
            string ids = string.Join(",", idList);
            builder.Append("begin;");
            builder.Append($"delete from metadata where DataID in ({ids});");
            builder.Append($"delete from metadata_video where DataID in ({ids});");
            builder.Append($"delete from metadata_to_translation where DataID in ({ids});");
            builder.Append($"delete from metadata_to_tagstamp where DataID in ({ids});");
            builder.Append($"delete from metadata_to_actor where DataID in ({ids});");
            builder.Append($"delete from metadata_to_label where DataID in ({ids});");
            builder.Append("commit;");

            // 中途失败时必须回滚：连接会停在未结束的事务里，之后所有写都落进同一个事务，
            // 下一次 begin 直接报「cannot start a transaction within a transaction」，
            // 于是「删不掉影片」会扩散成「整个软件再也写不进库」。
            try {
                MapperManager.videoMapper.ExecuteNonQuery(builder.ToString());
            } catch (Exception ex) {
                Jvedio.App.Logger?.Error($"删除影片失败（DataID: {ids}）：{ex}");
                // 回滚本身也允许失败（事务可能早已结束），失败只记日志，不掩盖原始异常
                MapperManager.videoMapper.TryExecuteNonQuery("rollback;");
                throw;
            }
        }

    }
}
