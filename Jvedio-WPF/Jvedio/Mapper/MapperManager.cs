using Jvedio.Core.DataBase;
using Jvedio.Core.DataBase.Tables;
using Jvedio.Mapper;
using System;
using System.Text.RegularExpressions;
using static Jvedio.App;

namespace Jvedio
{
    public static class MapperManager
    {
        public static AppConfigMapper appConfigMapper { get; set; } = new AppConfigMapper();

        public static AppDatabaseMapper appDatabaseMapper { get; set; } = new AppDatabaseMapper();
        public static TranslationMapper translationMapper { get; set; } = new TranslationMapper();
        public static MagnetsMapper magnetsMapper { get; set; } = new MagnetsMapper();
        public static AIFaceMapper aIFaceMapper { get; set; } = new AIFaceMapper();
        public static TagStampMapper tagStampMapper { get; set; } = new TagStampMapper();
        public static SearchHistoryMapper searchHistoryMapper { get; set; } = new SearchHistoryMapper();

        public static MetaDataMapper metaDataMapper { get; set; } = new MetaDataMapper();
        public static VideoMapper videoMapper { get; set; } = new VideoMapper();
        public static PictureMapper pictureMapper { get; set; } = new PictureMapper();
        public static ComicMapper comicMapper { get; set; } = new ComicMapper();
        public static GameMapper gameMapper { get; set; } = new GameMapper();
        public static ActorMapper actorMapper { get; set; } = new ActorMapper();
        public static UrlCodeMapper urlCodeMapper { get; set; } = new UrlCodeMapper();
        public static AssociationMapper associationMapper { get; set; } = new AssociationMapper();

        private static bool Loaded { get; set; }

        public static void ResetInitState()
        {
            Loaded = false;
        }

        public static bool Init()
        {
            if (Loaded)
                return true;

            // todo 泛型似乎无法使用多态进行反射加载

            // 初始化数据库连接
            appDatabaseMapper.Init();
            translationMapper.Init();
            magnetsMapper.Init();
            aIFaceMapper.Init();
            tagStampMapper.Init();
            searchHistoryMapper.Init();

            foreach (string key in Sqlite.AppData.TABLES.Keys) {
                appDatabaseMapper.CreateTable(key, Sqlite.AppData.TABLES[key]);
            }

            appConfigMapper.InitSqlite(SqlManager.DEFAULT_SQLITE_CONFIG_PATH);

            foreach (string key in Sqlite.AppConfig.TABLES.Keys) {
                appConfigMapper.CreateTable(key, Sqlite.AppConfig.TABLES[key]);
            }

            metaDataMapper.Init();
            videoMapper.Init();
            pictureMapper.Init();
            comicMapper.Init();
            gameMapper.Init();
            actorMapper.Init();
            urlCodeMapper.Init();
            associationMapper.Init();

            foreach (string key in Sqlite.Actor.TABLES.Keys) {
                actorMapper.CreateTable(key, Sqlite.Actor.TABLES[key]);
            }

            foreach (string key in Sqlite.Data.TABLES.Keys) {
                metaDataMapper.CreateTable(key, Sqlite.Data.TABLES[key]);
            }

            // 新增列
            // SQLite 不支持 ADD COLUMN IF NOT EXISTS，字段已存在时执行会抛
            // 「duplicate column name」，原实现直接吞掉异常并写一条错误日志，
            // 导致每次启动日志里都有几条假错误。这里先检查再执行。
            foreach (string sql in Sqlite.SQL.SqlCommands) {
                try {
                    if (IsAddColumnDuplicated(sql))
                        continue;
                    metaDataMapper.ExecuteNonQuery(sql);
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
            }

            Loaded = true;
            Logger.Info("init mapper ok");
            return Loaded;
        }

        /// <summary>
        /// 判断一条 <c>ALTER TABLE ... ADD COLUMN</c> 是否属于「字段已存在」。
        /// <para>
        /// 仅识别 <c>ALTER TABLE {表} ADD COLUMN {列}</c> 形式；其它语句一律返回 false（照常执行）。
        /// 通过 <c>PRAGMA table_info</c> 读取现有列，避免依赖异常信息文本（SQLite 的错误文案随版本变化）。
        /// </para>
        /// </summary>
        private static bool IsAddColumnDuplicated(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return false;

            var match = Regex.Match(sql,
                @"^\s*ALTER\s+TABLE\s+([A-Za-z_][A-Za-z0-9_]*)\s+ADD\s+COLUMN\s+([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            string table = match.Groups[1].Value;
            string column = match.Groups[2].Value;

            try {
                var rows = metaDataMapper.Select($"PRAGMA table_info({table})");
                if (rows == null)
                    return false;

                foreach (var row in rows) {
                    object name;
                    if (row.TryGetValue("name", out name) && name != null
                        && string.Equals(name.ToString(), column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            } catch {
                // 检查失败时按「不存在」处理，让原语句照常执行并走原有异常处理
            }

            return false;
        }

        public static void Dispose()
        {
            appConfigMapper.Dispose();

            appDatabaseMapper.Dispose();
            translationMapper.Dispose();
            magnetsMapper.Dispose();
            aIFaceMapper.Dispose();
            tagStampMapper.Dispose();
            searchHistoryMapper.Dispose();

            metaDataMapper.Dispose();
            videoMapper.Dispose();
            pictureMapper.Dispose();
            comicMapper.Dispose();
            gameMapper.Dispose();
            actorMapper.Dispose();
            urlCodeMapper.Dispose();
            associationMapper.Dispose();

            Logger.Info("dispose mapper ok");
        }
    }
}
