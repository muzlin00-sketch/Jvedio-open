using Jvedio.Entity;
using Jvedio.Entity.Data;
using Jvedio.Mapper;
using Jvedio.Core.Tasks;
using Jvedio.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Jvedio.MapperManager;

namespace Jvedio.Core.Scan
{
    public class GameScan : ScanTask
    {
        public GameScan(List<string> scanPaths, IEnumerable<string> fileExt = null) : base(scanPaths, null, fileExt)
        {
            pathDict = new Dictionary<string, List<string>>();
        }

        public Dictionary<string, List<string>> pathDict;

        private (List<Game>, List<string>) parseGame()
        {
            // 仅支持根目录
            List<Game> import = new List<Game>();
            List<string> notImport = new List<string>();
            if (pathDict == null || pathDict.Keys.Count == 0)
                return (null, null);
            foreach (string path in pathDict.Keys) {
                List<string> list = pathDict[path];

                Game game = new Game();
                game.DataType = Enums.DataType.Game;
                game.Title = Path.GetFileName(path);
                game.Path = getRealExe(list);
                game.Size = Jvedio.Common.DirHelper.getDirSize(new DirectoryInfo(path));
                game.Hash = Jvedio.Common.FileHash.Compute(game.Path); // 计算哈希
                import.Add(game);
            }

            return (import, notImport);
        }

        private string getRealExe(List<string> exeList)
        {
            return exeList[0];
        }

        public override void DoWork()
        {
            LogFile($"开始游戏扫描：目录 {ScanPaths.Count} 个");
            Task.Run(() => {
                // 与 ScanTask 一致：整个后台体包 try，避免后台异常无人观察
                try {
                    try {
                        TimeWatch.Start();
                        foreach (string path in ScanPaths) {
                            LogFile($"枚举目录 {path}");
                            List<string> list = Jvedio.Common.FileHelper.TryGetAllFiles(path, "*.exe")?.ToList();
                            if (list != null && list.Count > 0)
                                // 扫描根目录重复/重叠时 Add 会抛 ArgumentException
                                pathDict[path] = list;
                        }

                        CheckStatus();

                        (List<Game> import, List<string> notImport) = parseGame();

                        CheckStatus();

                        if (import == null) {
                            Success = true;
                            FinishScan();
                            return;
                        }

                        handleImport(import);
                        handleNotImport(notImport);

                        Success = true;
                        FinishScan();
                    } catch (TaskCanceledException ex) {
                        Logger.Warning(ex.Message);
                        LogFile("游戏扫描被取消");
                        SnapshotLogs();
                        FinalizeWithCancel();
                    } catch (Exception ex) {
                        FailScan(ex);
                    }
                } catch (Exception ex) {
                    LogFileError("游戏扫描收尾异常：" + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            });
        }

        private void handleImport(List<Game> import)
        {
            string sql = GameMapper.BASE_SQL;
            sql = "select metadata.DataID,Hash,Path,GID " + sql;
            List<Dictionary<string, object>> list = gameMapper.Select(sql);
            List<Game> existDatas = gameMapper.ToEntity<Game>(list, typeof(Game).GetProperties(), false);

            // 1.1 不需要导入
            // 存在同路径、同哈希的 exe
            foreach (var item in import.Where(arg => existDatas.Where(t => arg.Path.Equals(t.Path) && arg.Hash.Equals(t.Hash)).Any())) {
                AddNotImport(item.Path, "同路径、同哈希");
            }

            import.RemoveAll(arg => existDatas.Where(t => arg.Path.Equals(t.Path) && arg.Hash.Equals(t.Hash)).Any());

            // 1.2 需要 update
            // 哈希相同，路径不同
            List<Game> toUpdate = new List<Game>();
            foreach (Game game in import) {
                Game existData = existDatas.Where(t => game.Hash.Equals(t.Hash) && !game.Path.Equals(t.Path)).FirstOrDefault();
                if (existData != null) {
                    game.DataID = existData.DataID;
                    game.GID = existData.GID;
                    game.LastScanDate = Jvedio.Common.DateHelper.Now();
                    toUpdate.Add(game);
                    AddUpdate(game.Path, "哈希相同，路径不同");
                }
            }

            import.RemoveAll(arg => existDatas.Where(t => arg.Hash.Equals(t.Hash) && !arg.Path.Equals(t.Path)).Any());

            // 1.3 需要 update
            // 哈希不同，路径相同
            foreach (Game data in import) {
                Game existData = existDatas.Where(t => data.Path.Equals(t.Path) && !data.Hash.Equals(t.Hash)).FirstOrDefault();
                if (existData != null) {
                    data.DataID = existData.DataID;
                    data.GID = existData.GID;
                    data.LastScanDate = Jvedio.Common.DateHelper.Now();
                    toUpdate.Add(data);
                    AddUpdate(data.Path, " 哈希不同，路径相同");
                }
            }

            import.RemoveAll(arg => existDatas.Where(t => arg.Path.Equals(t.Path) && !arg.Hash.Equals(t.Hash)).Any());

            // 1.3 需要 insert
            List<Game> toInsert = import;

            // 1.更新
            if (toUpdate.Count > 0) {
                List<string> updatePaths = toUpdate.Select(arg => arg.Path).ToList();
                try {
                    List<MetaData> toUpdateData = toUpdate.Select(arg => arg.toMetaData()).ToList();
                    if (toUpdateData.Count > 0)
                        metaDataMapper.UpdateBatch(toUpdateData, "Title", "Size", "Hash", "Path", "LastScanDate");
                } catch (Exception ex) {
                    Logger.Error($"更新 {toUpdate.Count} 条记录失败：{ex.Message}");
                    LogFileError($"更新 {toUpdate.Count} 条记录失败（首个 {updatePaths.FirstOrDefault()}）：{ex.Message}");
                    MoveUpdateToNotImport(updatePaths, "更新记录失败：" + ex.Message);
                }
            }

            // 2.导入
            InsertGames(toInsert);
        }

        /// <summary>
        /// 批量导入游戏：先 metadata 拿真实 DataID，再写 metadata_game，全部成功才记账。
        /// <para>
        /// 原实现与 ScanTask 是同一套"先记账 + before++ 推算 DataID + 伪事务"的写法，
        /// 这里同样改为用 <c>InsertBatch</c> 回写的真实 ID，子表失败时补偿删除刚插入的 metadata。
        /// </para>
        /// </summary>
        private void InsertGames(List<Game> toInsert)
        {
            if (toInsert == null || toInsert.Count == 0)
                return;

            foreach (Game data in toInsert) {
                data.DBId = ConfigManager.Main.CurrentDBId;
                data.FirstScanDate = Jvedio.Common.DateHelper.Now();
                data.LastScanDate = Jvedio.Common.DateHelper.Now();
            }

            List<MetaData> toInsertData = toInsert.Select(arg => arg.toMetaData()).ToList();
            if (toInsertData.Count <= 0)
                return;

            Exception metaError = null;
            try {
                metaDataMapper.InsertBatch(toInsertData);
            } catch (Exception ex) {
                metaError = ex;
                Logger.Error($"写入 metadata 失败：{ex.Message}");
                LogFileError($"写入 metadata 失败（本批 {toInsertData.Count} 条）：{ex.Message}");
            }

            List<Game> ready = new List<Game>();
            List<Game> metaFailed = new List<Game>();
            for (int i = 0; i < toInsertData.Count; i++) {
                long dataId = toInsertData[i].DataID;
                if (dataId <= 0) {
                    metaFailed.Add(toInsert[i]);
                    continue;
                }
                toInsert[i].DataID = dataId;
                ready.Add(toInsert[i]);
            }

            if (metaFailed.Count > 0) {
                string reason = metaError == null ? "写库失败：没有取到自增 DataID" : "写库失败：" + metaError.Message;
                foreach (Game data in metaFailed)
                    AddNotImport(data.Path, reason);
                if (metaError == null)
                    LogFileError($"有 {metaFailed.Count} 条 metadata 已写入但没取到自增 DataID，可能留下孤儿行，需要人工核对");
            }

            if (ready.Count == 0) {
                // 一条都没进去 = 写库整体失败；不能当成"没有新游戏"静默收场，交给上层报错
                if (metaError != null)
                    throw new InvalidOperationException(
                        $"写入 metadata 全部失败（{metaFailed.Count} 条）：{metaError.Message}", metaError);
                return;
            }

            Exception childError = null;
            try {
                gameMapper.InsertBatch(ready);
            } catch (Exception ex) {
                childError = ex;
            }

            List<Game> childOk = ready.Where(arg => arg.GID > 0).ToList();
            List<Game> childFailed = ready.Where(arg => arg.GID <= 0).ToList();

            if (childFailed.Count > 0) {
                string ids = string.Join(",", childFailed.Select(arg => arg.DataID));
                if (!metaDataMapper.TryExecuteNonQuery($"delete from metadata where DataID in ({ids})"))
                    LogFileError($"补偿删除失败，可能残留 {childFailed.Count} 条孤儿 metadata：{ids}");
                else
                    LogFile($"已补偿删除 {childFailed.Count} 条刚插入的 metadata");

                string reason = childError == null ? "写入游戏表失败" : "写入游戏表失败：" + childError.Message;
                foreach (Game data in childFailed)
                    AddNotImport(data.Path, reason);
            }

            foreach (Game data in childOk)
                ScanResult.Import.Add(data.Path);

            if (childError != null) {
                Logger.Error($"写入 metadata_game 失败：{childError.Message}");
                LogFileError($"写入 metadata_game 失败，成功 {childOk.Count} 条、失败 {childFailed.Count} 条：{childError.Message}");
                throw new InvalidOperationException(
                    $"写入游戏表失败（成功 {childOk.Count} 条，失败 {childFailed.Count} 条已回滚主表）：{childError.Message}", childError);
            }
        }

        private void handleNotImport(List<string> notImport)
        {
            if (notImport == null)
                return;
            foreach (string path in notImport) {
                AddNotImport(path, "不导入");
            }
        }
    }
}
