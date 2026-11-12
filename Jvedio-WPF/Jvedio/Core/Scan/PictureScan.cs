using Jvedio.Core.Enums;
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
    public class PictureScan : ScanTask
    {
        public Dictionary<string, List<string>> pathDict;

        public Enums.DataType dataType = Enums.DataType.Picture;

        public PictureScan(List<string> scanPaths, List<string> filePaths, IEnumerable<string> fileExt = null, Enums.DataType dataType = DataType.Picture) : base(scanPaths, filePaths, fileExt)
        {
            pathDict = new Dictionary<string, List<string>>();
            this.dataType = dataType;
        }

        private (List<Picture>, List<string>) parsePicture()
        {
            // 仅支持根目录
            List<Picture> import = new List<Picture>();
            List<string> notImport = new List<string>();

            if (pathDict == null || pathDict.Keys.Count == 0)
                return (null, null);

            foreach (string path in pathDict.Keys) {
                List<string> list = pathDict[path];

                // 过滤后缀
                List<string> videoPaths = list.Where(arg => VIDEO_EXTENSIONS_LIST.Contains(Path.GetExtension(arg).ToLower())).ToList();
                List<string> imgPaths = list.Where(arg => FileExt.Contains(Path.GetExtension(arg).ToLower())).ToList();

                Picture picture = new Picture();
                picture.DataType = dataType;
                picture.Title = Path.GetFileName(path);
                picture.PicCount = imgPaths.Count;
                picture.VideoPaths = string.Join(Jvedio.Common.ConstValues.SeparatorString, videoPaths);
                picture.PicPaths = string.Join(Jvedio.Common.ConstValues.SeparatorString, imgPaths.Select(arg => Path.GetFileName(arg)));
                picture.Path = path;
                long totalSize = 0;
                foreach (string imgPath in imgPaths) {
                    totalSize += new FileInfo(imgPath).Length;
                }

                picture.Size = totalSize;

                // 计算哈希
                List<string> allPaths = imgPaths.Concat(videoPaths).ToList();
                notImport.AddRange(list.Except(allPaths));

                // ComputeForFiles 只取固定几个采样位置（首、次、中、倒数第二、末），
                // 结果直接依赖传入顺序；枚举顺序随文件系统变化，排序后再算才能稳定命中"同哈希"判重。
                List<string> hashPaths = allPaths.OrderBy(arg => arg, StringComparer.OrdinalIgnoreCase).ToList();
                picture.Hash = Jvedio.Common.FileHash.ComputeForFiles(hashPaths);
                import.Add(picture);
            }

            return (import, notImport);
        }

        public override void DoWork()
        {
            LogFile($"开始图片扫描：目录 {ScanPaths.Count} 个，类型 {dataType}");
            Task.Run(() => {
                // 与 ScanTask 一致：整个后台体包 try，避免后台异常无人观察
                try {
                    try {
                        TimeWatch.Start();
                        foreach (string path in ScanPaths) {
                            LogFile($"枚举目录 {path}");
                            List<string> list = Jvedio.Common.FileHelper.TryGetAllFiles(path, "*.*")?.ToList();
                            if (list != null && list.Count > 0)
                                // 扫描根目录重复/重叠时 Add 会抛 ArgumentException
                                pathDict[path] = list;
                        }

                        CheckStatus();

                        (List<Picture> import, List<string> notImport) = parsePicture();

                        CheckStatus();

                        if (import == null) {
                            // 没有任何可扫描的目录内容，直接完成
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
                        LogFile("图片扫描被取消");
                        SnapshotLogs();
                        FinalizeWithCancel();
                    } catch (Exception ex) {
                        FailScan(ex);
                    }
                } catch (Exception ex) {
                    LogFileError("图片扫描收尾异常：" + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            });
        }

        /// <summary>
        /// 已存在的图片/漫画记录。
        /// <para>
        /// 漫画原来也走 <c>metadata_picture</c> 查询，而漫画只存在于 <c>metadata_comic</c>，
        /// 于是每次都"一条也匹配不上"——判重永远失败、每次都当新片重复插入，
        /// 判断"哈希相同/路径相同"的分支根本进不去（这就是"重复扫描更新永远是 0 行"的另一半原因）。
        /// </para>
        /// </summary>
        private List<Picture> GetExistPictures()
        {
            string sql;
            List<Dictionary<string, object>> list;
            if (dataType == DataType.Comics) {
                sql = "select metadata.DataID,Hash,Size,Path,CID " + ComicMapper.SQL_BASE
                    + $"and metadata.DBId={ConfigManager.Main.CurrentDBId}";
                list = comicMapper.Select(sql);
            } else {
                sql = "select metadata.DataID,Hash,Size,Path,PID " + PictureMapper.BASE_SQL
                    + $"and metadata.DBId={ConfigManager.Main.CurrentDBId}";
                list = pictureMapper.Select(sql);
            }
            return pictureMapper.ToEntity<Picture>(list, typeof(Picture).GetProperties(), false);
        }

        private void handleImport(List<Picture> import)
        {
            List<Picture> existPictures = GetExistPictures();

            // 1.1 不需要导入
            // 存在同路径、同哈希的图片路径
            foreach (var item in import.Where(arg => existPictures.Where(t => arg.Path.Equals(t.Path) && arg.Hash.Equals(t.Hash)).Any())) {
                AddNotImport(item.Path, "同路径、同哈希");
            }

            import.RemoveAll(arg => existPictures.Where(t => arg.Path.Equals(t.Path) && arg.Hash.Equals(t.Hash)).Any());

            // 1.2 需要 update
            // 哈希相同，路径不同
            List<Picture> toUpdate = new List<Picture>();
            foreach (Picture data in import) {
                Picture existData = existPictures.Where(t => data.Hash.Equals(t.Hash) && !data.Path.Equals(t.Path)).FirstOrDefault();
                if (existData != null) {
                    data.DataID = existData.DataID;
                    data.PID = existData.PID;
                    data.LastScanDate = Jvedio.Common.DateHelper.Now();
                    toUpdate.Add(data);
                    AddUpdate(data.Path, "哈希相同，路径不同");
                }
            }

            import.RemoveAll(arg => existPictures.Where(t => arg.Hash.Equals(t.Hash) && !arg.Path.Equals(t.Path)).Any());

            // 1.3 需要 update
            // 哈希不同，路径相同
            foreach (Picture data in import) {
                Picture existData = existPictures.Where(t => data.Path.Equals(t.Path) && !data.Hash.Equals(t.Hash)).FirstOrDefault();
                if (existData != null) {
                    data.DataID = existData.DataID;
                    data.PID = existData.PID;
                    data.LastScanDate = Jvedio.Common.DateHelper.Now();
                    toUpdate.Add(data);
                    AddUpdate(data.Path, "哈希不同，路径相同");
                }
            }

            import.RemoveAll(arg => existPictures.Where(t => arg.Path.Equals(t.Path) && !arg.Hash.Equals(t.Hash)).Any());

            // 1.3 需要 insert
            List<Picture> toInsert = import;

            // 1.更新
            if (toUpdate.Count > 0) {
                List<string> updatePaths = toUpdate.Select(arg => arg.Path).ToList();
                try {
                    List<MetaData> toUpdateData = toUpdate.Select(arg => arg.toMetaData()).ToList();
                    if (toUpdateData.Count > 0)
                        metaDataMapper.UpdateBatch(toUpdateData, "Title", "Size", "Hash", "Path", "LastScanDate");

                    if (dataType == DataType.Picture) {
                        pictureMapper.UpdateBatch(toUpdate, "PicCount", "PicPaths", "VideoPaths");
                    } else if (dataType == DataType.Comics) {
                        UpdateComics(toUpdate, updatePaths);
                    }
                } catch (Exception ex) {
                    Logger.Error($"更新 {toUpdate.Count} 条记录失败：{ex.Message}");
                    LogFileError($"更新 {toUpdate.Count} 条记录失败（首个 {updatePaths.FirstOrDefault()}）：{ex.Message}");
                    MoveUpdateToNotImport(updatePaths, "更新记录失败：" + ex.Message);
                }
            }

            // 2.导入
            InsertPictures(toInsert);
        }

        /// <summary>
        /// 漫画更新：<c>UpdateBatch</c> 的 where 用的是主键 <c>CID</c>，
        /// 而 <c>toSimpleComic()</c> 从来不设 CID，原实现拼出的是 <c>where CID = 0</c>，
        /// 于是"重复扫描永远更新 0 行"且没有任何报错。这里先按 DataID 取真实 CID 再更新。
        /// </summary>
        private void UpdateComics(List<Picture> toUpdate, List<string> updatePaths)
        {
            Dictionary<long, long> comicIds = GetComicIds();
            List<Comic> comics = new List<Comic>();
            List<string> missing = new List<string>();
            for (int i = 0; i < toUpdate.Count; i++) {
                Comic comic = toUpdate[i].toSimpleComic();
                if (comicIds.TryGetValue(comic.DataID, out long cid) && cid > 0) {
                    comic.CID = cid;
                    comics.Add(comic);
                } else {
                    missing.Add(updatePaths[i]);
                }
            }

            if (comics.Count > 0)
                comicMapper.UpdateBatch(comics, "PicCount", "PicPaths");
            if (missing.Count > 0) {
                LogFileError($"{missing.Count} 条漫画记录在 metadata_comic 里找不到对应行，更新未生效（首个 {missing[0]}）");
                MoveUpdateToNotImport(missing, "漫画记录缺少 CID，更新未生效");
            }
        }

        /// <summary>DataID → CID 映射（metadata_comic 的真实主键）。</summary>
        private Dictionary<long, long> GetComicIds()
        {
            Dictionary<long, long> result = new Dictionary<long, long>();
            List<Dictionary<string, object>> list = comicMapper.Select("select DataID,CID from metadata_comic");
            if (list == null)
                return result;
            foreach (Dictionary<string, object> dict in list) {
                if (!dict.TryGetValue("DataID", out object dataIdValue) || dataIdValue == null || dataIdValue == DBNull.Value)
                    continue;
                if (!dict.TryGetValue("CID", out object cidValue) || cidValue == null || cidValue == DBNull.Value)
                    continue;
                long dataId = Convert.ToInt64(dataIdValue);
                long cid = Convert.ToInt64(cidValue);
                if (dataId > 0 && cid > 0)
                    result[dataId] = cid;
            }
            return result;
        }

        /// <summary>
        /// 批量导入图片/漫画：先 metadata 拿真实 DataID，再写子表，全部成功才记账。
        /// <para>
        /// 原实现与 ScanTask 是同一套"先记账 + before++ 推算 DataID + 伪事务"的写法，
        /// 这里同样改为用 <c>InsertBatch</c> 回写的真实 ID，子表失败时补偿删除刚插入的 metadata。
        /// </para>
        /// </summary>
        private void InsertPictures(List<Picture> toInsert)
        {
            if (toInsert == null || toInsert.Count == 0)
                return;

            foreach (Picture data in toInsert) {
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

            List<Picture> ready = new List<Picture>();
            List<Picture> metaFailed = new List<Picture>();
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
                foreach (Picture data in metaFailed)
                    AddNotImport(data.Path, reason);
            }

            if (ready.Count == 0) {
                // 一条都没进去 = 写库整体失败；不能当成"没有新图"静默收场，交给上层报错
                if (metaError != null)
                    throw new InvalidOperationException(
                        $"写入 metadata 全部失败（{metaFailed.Count} 条）：{metaError.Message}", metaError);
                return;
            }

            // 2. 写子表
            List<Picture> childOk = new List<Picture>();
            List<Picture> childFailed = new List<Picture>();
            List<Comic> comics = null;
            Exception childError = null;
            try {
                if (dataType == DataType.Comics) {
                    comics = ready.Select(arg => arg.toSimpleComic()).ToList();
                    comicMapper.InsertBatch(comics);
                } else {
                    pictureMapper.InsertBatch(ready);
                }
            } catch (Exception ex) {
                childError = ex;
            }

            if (dataType == DataType.Comics) {
                if (comics == null) {
                    childFailed.AddRange(ready);
                } else {
                    for (int i = 0; i < comics.Count && i < ready.Count; i++) {
                        if (comics[i].CID > 0)
                            childOk.Add(ready[i]);
                        else
                            childFailed.Add(ready[i]);
                    }
                }
            } else {
                foreach (Picture data in ready) {
                    if (data.PID > 0)
                        childOk.Add(data);
                    else
                        childFailed.Add(data);
                }
            }

            if (childFailed.Count > 0) {
                string ids = string.Join(",", childFailed.Select(arg => arg.DataID));
                if (!metaDataMapper.TryExecuteNonQuery($"delete from metadata where DataID in ({ids})"))
                    LogFileError($"补偿删除失败，可能残留 {childFailed.Count} 条孤儿 metadata：{ids}");
                else
                    LogFile($"已补偿删除 {childFailed.Count} 条刚插入的 metadata");

                string reason = childError == null ? "写入子表失败" : "写入子表失败：" + childError.Message;
                foreach (Picture data in childFailed)
                    AddNotImport(data.Path, reason);
            }

            if (childOk.Count > 0) {
                foreach (Picture data in childOk)
                    ScanResult.Import.Add(data.Path);
            }

            if (childError != null) {
                Logger.Error($"写入子表失败：{childError.Message}");
                LogFileError($"写入子表失败，成功 {childOk.Count} 条、失败 {childFailed.Count} 条：{childError.Message}");
                throw new InvalidOperationException(
                    $"写入子表失败（成功 {childOk.Count} 条，失败 {childFailed.Count} 条已回滚主表）：{childError.Message}", childError);
            }

            if (metaFailed.Count > 0 && metaError == null)
                LogFileError($"有 {metaFailed.Count} 条 metadata 已写入但没取到自增 DataID，可能留下孤儿行，需要人工核对");
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
