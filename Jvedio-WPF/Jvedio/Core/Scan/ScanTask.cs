using Jvedio.Core.DataBase;
using Jvedio.Core.Enums;
using Jvedio.Entity;
using Jvedio.Mapper;
using SuperControls.Style;
using Jvedio.Core.Tasks;
using Jvedio.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Jvedio.MapperManager;

namespace Jvedio.Core.Scan
{
    public class ScanTask : AbstractTask
    {

        private const string DEFAULT_VIDEO_EXT = "3g2,3gp,3gp2,3gpp,amr,amv,asf,avi,bdmv,bik,d2v,divx,drc,dsa,dsm,dss,dsv,evo,f4v,flc,fli,flic,flv,hdmov,ifo,ivf,m1v,m2p,m2t,m2ts,m2v,m4b,m4p,m4v,mkv,mp2v,mp4,mp4v,mpe,mpeg,mpg,mpls,mpv2,mpv4,mov,mts,ogm,ogv,pss,pva,qt,ram,ratdvd,rm,rmm,rmvb,roq,rpm,smil,smk,swf,tp,tpr,ts,vob,vp6,webm,wm,wmp,wmv";
        private const string DEFAULT_IMAGE_EXT = "png,jpg,jpeg,bmp,jpe,ico,gif";


        #region "事件"

        public event EventHandler onScanning;



        #endregion

        #region "静态属性"


        public static Dictionary<NotImportReason, string> ReasonToString { get; set; } = new Dictionary<NotImportReason, string>()
        {
            { NotImportReason.NotInExtension, LangManager.GetValueByKey("NotSupportedExt") },
            { NotImportReason.RepetitiveVideo, LangManager.GetValueByKey("RepeatedVideo") },
            { NotImportReason.RepetitiveVID, LangManager.GetValueByKey("RepeatedVID") },
            { NotImportReason.SizeTooSmall, LangManager.GetValueByKey("FileSizeTooSmall") },
            { NotImportReason.SizeTooLarge, LangManager.GetValueByKey("FileSizeTooBig") },
        };


        private static string[] NFOUpdateMetaProps { get; set; } = new string[]
        {
            "Title",
            "ReleaseYear",
            "ReleaseDate",
            "Country",
            "Genre",
            "Rating",
            "LastScanDate",
            "PathExist",
        };

        private static string[] NFOUpdateVideoProps { get; set; } = new string[]
        {
            "Plot",
            "Director",
            "Duration",
            "Studio",
            "Series",
            "Outline",
        };

        #endregion

        #region "属性"

        public static string VIDEO_EXTENSIONS { get; set; }

        public static string PICTURE_EXTENSIONS { get; set; }

        public static List<string> VIDEO_EXTENSIONS_LIST { get; set; }

        public static List<string> PICTURE_EXTENSIONS_LIST { get; set; }

        public ScanResult ScanResult { get; set; }

        /// <summary>
        /// 需要扫描的目录
        /// </summary>
        public List<string> ScanPaths { get; set; }

        public List<string> FilePaths { get; set; }

        public List<string> FileExt { get; set; }

        private List<Video> existVideos { get; set; }

        private List<ActorInfo> existActors { get; set; }

        /// <summary>
        /// 整次扫描的致命失败原因。为空表示没有走到失败收尾。
        /// <para>
        /// 与 <see cref="AbstractTask.Success"/> 分开：<c>Success</c> 只表示"整趟跑完没有致命错误"，
        /// 失败原因单独放这里，界面（onError）据此显示"失败原因 + 已入库统计"。
        /// </para>
        /// </summary>
        public string FailureMessage { get; set; }

        #endregion

        #region "日志与记账"

        /// <summary>
        /// 写一条落盘日志（<see cref="Jvedio.App.Logger"/>）。
        /// <para>
        /// 任务自带的 <see cref="AbstractTask.Logger"/> 只写内存里的 <see cref="AbstractTask.Logs"/>，
        /// 不落盘，排查"扫描为什么没反应"时事后什么都查不到，所以关键节点一律再写一份到文件。
        /// </para>
        /// </summary>
        protected static void LogFile(string message)
        {
            Jvedio.App.Logger?.Info("[scan] " + message);
        }

        /// <summary>写一条落盘的错误日志（失败分支必须带文件路径）。</summary>
        protected static void LogFileError(string message)
        {
            Jvedio.App.Logger?.Error("[scan] " + message);
        }

        /// <summary>
        /// 记一条"未导入"。重复路径不重复记、不抛异常。
        /// <para>
        /// 原来各处直接 <c>ScanResult.NotImport.Add</c>：<see cref="Dictionary{TKey, TValue}.Add"/>
        /// 遇到重复键会抛 <see cref="ArgumentException"/>，在扫描中段把整趟扫描打断且界面只显示"取消"。
        /// </para>
        /// </summary>
        protected void AddNotImport(string path, string reason, string detail = "")
        {
            if (string.IsNullOrEmpty(path) || ScanResult?.NotImport == null)
                return;
            if (ScanResult.NotImport.ContainsKey(path))
                return;
            ScanResult.NotImport.Add(path, new ScanDetailInfo(reason, detail));
        }

        /// <summary>
        /// 记一条"更新"。同样不允许因重复键抛异常。
        /// </summary>
        protected void AddUpdate(string path, string reason = "")
        {
            if (string.IsNullOrEmpty(path) || ScanResult?.Update == null)
                return;
            if (ScanResult.Update.ContainsKey(path))
                return;
            ScanResult.Update.Add(path, reason);
        }

        /// <summary>把已登记为"更新"的路径改记为"未导入"（更新失败时用，避免界面谎报更新成功）。</summary>
        protected void MoveUpdateToNotImport(IEnumerable<string> paths, string reason)
        {
            if (paths == null)
                return;
            foreach (string path in paths) {
                if (string.IsNullOrEmpty(path))
                    continue;
                ScanResult.Update.Remove(path);
                AddNotImport(path, reason);
            }
        }

        #endregion



        static ScanTask()
        {
            VIDEO_EXTENSIONS = DEFAULT_VIDEO_EXT;
            PICTURE_EXTENSIONS = DEFAULT_IMAGE_EXT;
            VIDEO_EXTENSIONS_LIST = VIDEO_EXTENSIONS.Split(',').Select(arg => "." + arg).ToList();
            PICTURE_EXTENSIONS_LIST = PICTURE_EXTENSIONS.Split(',').Select(arg => "." + arg).ToList();
            STATUS_TO_TEXT_DICT[TaskStatus.Running] = $"{LangManager.GetValueByKey("Scanning")}...";
        }


        public ScanTask(List<string> scanPaths, List<string> filePaths,
            IEnumerable<string> fileExt = null) : base()
        {
            if (scanPaths != null && scanPaths.Count > 0)
                // 去重（忽略大小写）：重复的扫描根目录会让同一文件被枚举两次，
                // 后面按路径判重、记账、写库全部按"出现两次"处理。
                ScanPaths = scanPaths.Where(arg => Directory.Exists(arg))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (filePaths != null && filePaths.Count > 0)
                FilePaths = filePaths.Where(arg => File.Exists(arg))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileExt != null) {
                FileExt = new List<string>();
                foreach (var item in fileExt) {
                    string ext = item.Trim();
                    if (string.IsNullOrEmpty(ext))
                        continue;
                    if (!item.StartsWith("."))
                        FileExt.Add("." + ext);
                    else
                        FileExt.Add(ext);
                }
            }

            if (ScanPaths == null)
                ScanPaths = new List<string>();
            if (FilePaths == null)
                FilePaths = new List<string>();
            if (FileExt == null)
                FileExt = VIDEO_EXTENSIONS_LIST; // 默认导入视频
            ScanResult = new ScanResult();
            Logs.Add($"scan task init, paths count: {FilePaths.Count}, dir count: {ScanPaths.Count}");
        }

        private void ScanDir()
        {
            int total = ScanPaths.Count;
            int count = 0;
            // 扫描根目录可能重叠（父目录 + 子目录）或被重复加入，必须按全路径去重：
            // 否则同一个文件被枚举两次，后面按路径判重/记账全部错位。
            HashSet<string> seen = new HashSet<string>(FilePaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (string path in ScanPaths) {
                LogFile($"枚举目录 {path}");
                IEnumerable<string> paths = Jvedio.Common.DirHelper.GetFileList(path, "*.*", (ex) => {
                    // 发生异常
                    Logger.Error(ex.Message);
                    LogFileError($"枚举目录失败 {path}：{ex.Message}");
                }, (dir) => {
                    Message = dir;
                    // 每个目录留一条落盘痕迹：Debug 级别（Release 下被级别过滤掉，
                    // 全盘扫描几万个目录时不让日志文件爆炸），排查时用 Debug 构建即可看到全链路
                    Jvedio.App.Logger?.Debug("[scan] 进入目录 " + dir);
                    onScanning?.Invoke(this, new MessageCallBackEventArgs(dir));
                }, TokenCTS);

                int added = 0;
                foreach (string file in paths) {
                    if (seen.Add(file)) {
                        FilePaths.Add(file);
                        added++;
                    }
                }

                LogFile($"目录 {path} 新增文件 {added} 个（累计 {FilePaths.Count}）");
                count++;
                Progress = (float)Math.Round(((float)count) / total, 4) * 100;
            }
            Progress = 30;
        }

        /// <summary>
        /// 在 UI 中显示要导入的文件
        /// </summary>
        private void NotifyScanPath()
        {
            if (ScanPaths.Count == 0 && FilePaths.Count != 0) {
                string path = FilePaths[FilePaths.Count - 1];
                Message = path;
                onScanning?.Invoke(this, new MessageCallBackEventArgs(path));
            }
        }

        public override void DoWork()
        {
            LogFile($"开始扫描：目录 {ScanPaths.Count} 个，文件 {FilePaths.Count} 个，后缀 {string.Join(",", FileExt ?? new List<string>())}");
            Logger.Info(LangManager.GetValueByKey("BeginScan"));
            Task.Run(() => {
                // 整个 Task.Run 都包在 try 里：原来 StartWatch/NotifyScanPath/ScanDir 在 try 之外，
                // 它们在后台线程抛出的异常没有任何观察者，表现为"扫描条一直转、日志一行没有"。
                try {
                    try {
                        RunScan();
                        Success = true;
                        FinishScan();
                    } catch (TaskCanceledException ex) {
                        Logger.Warning(ex.Message);
                        LogFile("扫描被取消");
                        SnapshotLogs();
                        FinalizeWithCancel();
                    } catch (Exception ex) {
                        FailScan(ex);
                    }
                } catch (Exception ex) {
                    // 收尾/事件回调本身出错：只落盘，绝不让 Task 的异常无人观察
                    LogFileError("扫描收尾异常：" + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            });
        }

        /// <summary>
        /// 真正的扫描流程：枚举 → 解析 → 写库。
        /// <para>
        /// 任何一步抛出都交给 <see cref="DoWork"/> 的 catch 统一收尾，
        /// 中途不再 <c>catch</c> 后"当成功继续跑"（原来写库失败会被吞掉，界面照样报"已导入 N 条"）。
        /// </para>
        /// </summary>
        private void RunScan()
        {
            StartWatch();
            NotifyScanPath();
            ScanDir();

            CheckStatus();

            WarnInvisibleRows();

            VideoParser videoParser = new VideoParser((msg) => {
                if (Logs != null)
                    Logs.Add(msg);
            });

            (List<Video> import, Dictionary<string, NotImportReason> notImport, List<string> failNFO)
            parseResult = videoParser.ParseMovie(FilePaths, FileExt, Token, ConfigManager.ScanConfig.ScanNfo, callBack: (msg) => {
                Logger.Error(msg);
                LogFileError("解析影片失败：" + msg);
            });

            Progress = 60;

            ScanResult.TotalCount = parseResult.import.Count + parseResult.notImport.Count + parseResult.failNFO.Count;
            CheckStatus();

            // 将 NFO 提取出来
            List<Video> importNFO = parseResult.import
                .Where(arg => arg.Path != null && arg.Path.ToLower().EndsWith(".nfo")).ToList();
            parseResult.import.RemoveAll(arg => arg.Path != null && arg.Path.ToLower().EndsWith(".nfo"));
            HandleImport(parseResult.import);

            Progress = 70;

            HandleImportNFO(importNFO);          // 导入视频后再导入 NFO

            Progress = 80;

            HandleNotImport(parseResult.notImport);

            Progress = 90;

            HandleFailNFO(parseResult.failNFO);
            Progress = 100;
        }

        /// <summary>成功收尾：统计、耗时、日志快照、完成事件。</summary>
        protected void FinishScan()
        {
            Running = false;
            StopWatch();
            ScanResult.ElapsedMilliseconds = ElapsedMilliseconds;
            SnapshotLogs();
            Status = System.Threading.Tasks.TaskStatus.RanToCompletion;
            LogFile($"扫描完成：{BuildSummary()}");
            base.OnCompleted(null);
        }

        /// <summary>
        /// 失败收尾。与"取消"分开走：
        /// <list type="bullet">
        /// <item>不再调 <see cref="AbstractTask.FinalizeWithCancel"/>——那条路把状态置为 Canceled，
        /// 界面只当"取消"记一行日志，用户看不到失败原因；</item>
        /// <item>改发 <c>onError</c>（界面弹 MessageCard），文案里同时带失败原因和<b>已入库统计</b>；</item>
        /// <item>仍然发 <c>onCompleted</c>，否则 <c>BaseManager.RunningCount</c> 减不下去、任务列表一直显示在跑。</item>
        /// </list>
        /// </summary>
        protected void FailScan(Exception ex)
        {
            Running = false;
            StopWatch();
            ScanResult.ElapsedMilliseconds = ElapsedMilliseconds;
            SnapshotLogs();

            FailureMessage = ex.Message;
            Success = false;
            string message = $"扫描失败：{ex.Message}{Environment.NewLine}{BuildSummary()}";

            Logger.Error(ex.Message);
            LogFileError($"{ex.Message}{Environment.NewLine}{BuildSummary()}{Environment.NewLine}{ex.StackTrace}");

            // 状态先置结束态再改文案：Status 的 setter 会用 STATUS_TO_TEXT_DICT 覆盖 StatusText
            Status = System.Threading.Tasks.TaskStatus.RanToCompletion;
            StatusText = "扫描失败";

            try {
                OnError(new MessageCallBackEventArgs(message));
            } catch (Exception e) {
                LogFileError("错误回调异常：" + e.Message);
            }

            base.OnCompleted(null);
        }

        /// <summary>把任务内存日志快照给 <see cref="ScanResult.Logs"/>（扫描详情窗口的"查看日志"读它）。</summary>
        protected void SnapshotLogs()
        {
            if (ScanResult == null)
                return;
            ScanResult.Logs = new List<string>(Logs ?? new List<string>());
        }

        protected string BuildSummary()
        {
            if (ScanResult == null)
                return string.Empty;
            return $"总数 {ScanResult.TotalCount}，已导入 {ScanResult.Import.Count}，更新 {ScanResult.Update.Count}，"
                + $"未导入 {ScanResult.NotImport.Count}，NFO 失败 {ScanResult.FailNFO.Count}";
        }

        /// <summary>
        /// 判重自检：主表有行、子表缺行的残留（历史失败导入留下的孤儿）。
        /// <para>
        /// 这类行任何 <c>metadata_video JOIN metadata</c> 都选不出来、界面上永远不可见，
        /// 但 <c>metadata.DataID</c> 已经占号。只做提示与落盘，不自动删——
        /// 删主表可能连带别的子表数据，交给用户判断。
        /// </para>
        /// </summary>
        private void WarnInvisibleRows()
        {
            try {
                string sql = "select count(*) from metadata m "
                    + "left join metadata_video v on m.DataID=v.DataID "
                    + $"where m.DataType={(int)DataType.Video} and m.DBId={ConfigManager.Main.CurrentDBId} and v.DataID is null";
                long count = metaDataMapper.SelectCount(sql);
                if (count > 0) {
                    string msg = $"发现 {count} 条不可见残留（metadata 有记录、metadata_video 缺行），它们不会出现在影片列表里；"
                        + $"清理：delete from metadata where DataID in (select m.DataID from metadata m left join metadata_video v on m.DataID=v.DataID where v.DataID is null and m.DataType={(int)DataType.Video} and m.DBId={ConfigManager.Main.CurrentDBId});";
                    Logger.Warning(msg);
                    Logs?.Add(msg);
                    LogFileError(msg);
                }
            } catch (Exception ex) {
                Logger.Error(ex.Message);
                LogFileError("不可见残留自检失败：" + ex.Message);
            }
        }


        private List<Video> GetExistVideos()
        {
            // 判重必须用 LEFT JOIN：metadata_video 缺行的残留（历史失败导入留下的孤儿）在 INNER JOIN 下
            // 永远选不出来，重扫时会被当成新片反复插入，越扫重复越多。
            string sql = "select metadata.DataID,VID,Hash,Size,Path,MVID,SubSection "
                + "FROM metadata LEFT JOIN metadata_video on metadata.DataID=metadata_video.DataID "
                + $"where metadata.DBId={ConfigManager.Main.CurrentDBId}";
            List<Dictionary<string, object>> list = videoMapper.Select(sql);
            return videoMapper.ToEntity<Video>(list, typeof(Video).GetProperties(), false);
        }

        private void HandleImport(List<Video> import)
        {
            if (import == null || import.Count == 0) {
                return;
            }
            // 分为 2 部分，有识别码和无识别码
            List<Video> noVidList = import.Where(arg => string.IsNullOrEmpty(arg.VID)).ToList();
            List<Video> vidList = import.Where(arg => !string.IsNullOrEmpty(arg.VID)).ToList();

            existVideos = GetExistVideos();

            // 1. 处理有识别码的
            // 1.1 不需要导入
            // 存在同路径、相同大小的影片
            foreach (var item in vidList.Where(arg => existVideos.Where(t => arg.Size.Equals(t.Size) && arg.Path.Equals(t.Path)).Any())) {
                AddNotImport(item.Path, LangManager.GetValueByKey("SamePathFileSize"));
            }

            vidList.RemoveAll(arg => existVideos.Where(t => arg.Size.Equals(t.Size) && arg.Path.Equals(t.Path)).Any());

            // 存在不同路径、相同大小、相同 VID、且原路径也存在的影片
            foreach (var item in vidList.Where(arg => existVideos
                .Where(t => arg.Size.Equals(t.Size) && arg.VID.Equals(t.VID) && !arg.Path.Equals(t.Path) && File.Exists(t.Path))
                .Any())) {
                ScanDetailInfo scanDetailInfo = new ScanDetailInfo(LangManager.GetValueByKey("NotSamePathSameFileSize"));
                StringBuilder builder = new StringBuilder($"和 {item.Path} 存在重复：");
                builder.AppendLine();
                builder.AppendLine();
                List<Video> _videos = existVideos
                .Where(t => item.Size.Equals(t.Size) && item.VID.Equals(t.VID) && !item.Path.Equals(t.Path) && File.Exists(t.Path)).ToList();
                foreach (var _video in _videos)
                    builder.AppendLine(_video.Path);
                scanDetailInfo.Detail = builder.ToString();
                AddNotImport(item.Path, scanDetailInfo.Reason, scanDetailInfo.Detail);
            }

            vidList.RemoveAll(arg => existVideos.Where(t => arg.Size.Equals(t.Size) && arg.VID.Equals(t.VID) && !arg.Path.Equals(t.Path) && File.Exists(t.Path)).Any());

            // 存在不同路径，相同 VID，不同大小，且原路径存在（可能是剪辑的视频），且分段视频不相等
            foreach (var item in vidList
                .Where(arg => existVideos.Where(t => arg.VID.Equals(t.VID) &&
                !arg.Path.Equals(t.Path) &&
                !arg.Size.Equals(t.Size) &&
                File.Exists(t.Path) &&
                t.SubSection.Equals(arg.SubSection))
                .Any())) {
                ScanDetailInfo scanDetailInfo = new ScanDetailInfo(LangManager.GetValueByKey("SamePathNotSameFileSize"));
                StringBuilder builder = new StringBuilder($"和 {item.Path} 存在重复：");
                builder.AppendLine();
                builder.AppendLine();
                List<Video> _videos = existVideos
                    .Where(t => item.VID.Equals(t.VID) && !item.Path.Equals(t.Path) && !item.Size.Equals(t.Size) && File.Exists(t.Path)).ToList();
                foreach (var _video in _videos)
                    builder.AppendLine(_video.Path);
                scanDetailInfo.Detail = builder.ToString();
                AddNotImport(item.Path, scanDetailInfo.Reason, scanDetailInfo.Detail);
            }

            vidList.RemoveAll(arg => existVideos.Where(t => arg.VID.Equals(t.VID)
            && !arg.Path.Equals(t.Path) &&
            !arg.Size.Equals(t.Size) &&
            File.Exists(t.Path) &&
            t.SubSection.Equals(arg.SubSection)).Any());

            // 1.2 需要 update 路径
            // VID 相同，原路径（或分段视频路径）不同
            List<Video> toUpdate = new List<Video>();
            HashSet<string> updatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Video video in vidList) {
                Video existVideo = existVideos.Where(t => video.VID.Equals(t.VID) && (!video.Path.Equals(t.Path) || !video.SubSection.Equals(t.SubSection))).FirstOrDefault();
                if (existVideo != null) {
                    video.DataID = existVideo.DataID;
                    video.MVID = existVideo.MVID; // 下面使用 videoMapper 更新的时候会使用到
                    video.LastScanDate = Jvedio.Common.DateHelper.Now();
                    video.PathExist = 1;
                    toUpdate.Add(video);
                    updatePaths.Add(video.Path);
                    AddUpdate(video.Path, string.Empty);
                }
            }

            // 1.3 同路径但大小/指纹变了：既不是"不需要导入"（大小不同），也不满足 update 条件（路径相同），
            // 下面按 VID 整体 RemoveAll 时会被丢掉——原来这批文件在 导入/更新/未导入 三处都不出现。
            foreach (Video video in vidList) {
                if (updatePaths.Contains(video.Path))
                    continue;
                Video existVideo = existVideos.FirstOrDefault(t => video.VID.Equals(t.VID) && video.Path.Equals(t.Path));
                if (existVideo == null)
                    continue;
                AddNotImport(video.Path, "同路径的文件已变化（大小或指纹不同），未自动更新",
                    $"原记录：{existVideo.Path}{Environment.NewLine}原大小：{existVideo.Size}，本次大小：{video.Size}"
                    + $"{Environment.NewLine}原指纹：{existVideo.Hash}，本次指纹：{video.Hash}");
            }

            vidList.RemoveAll(arg => existVideos.Where(t => arg.VID.Equals(t.VID)).Any());

            // 1.3 需要 insert
            List<Video> toInsert = vidList;

            // 2. 处理无识别码的
            // 存在相同 HASH ，相同路径的影片
            foreach (var item in noVidList.Where(arg => existVideos.Where(t => arg.Hash.Equals(t.Hash) && arg.Path.Equals(t.Path)).Any())) {
                ScanDetailInfo scanDetailInfo = new ScanDetailInfo(LangManager.GetValueByKey("SameHashSamePath"));
                StringBuilder builder = new StringBuilder($"和 {item.Path} 存在重复：");
                builder.AppendLine();
                builder.AppendLine();
                List<Video> _videos = existVideos
                    .Where(t => item.Hash.Equals(t.Hash) && item.Path.Equals(t.Path)).ToList();
                foreach (var _video in _videos)
                    builder.AppendLine(_video.Path);
                scanDetailInfo.Detail = builder.ToString();
                AddNotImport(item.Path, scanDetailInfo.Reason, scanDetailInfo.Detail);
            }

            noVidList.RemoveAll(arg => existVideos.Where(t => arg.Hash.Equals(t.Hash) && arg.Path.Equals(t.Path)).Any());

            // hash 相同，原路径不同则需要更新
            foreach (Video video in noVidList) {
                Video existVideo = existVideos.Where(t => video.Hash.Equals(t.Hash) && !video.Path.Equals(t.Path)).FirstOrDefault();
                if (existVideo != null) {
                    video.DataID = existVideo.DataID;
                    video.MVID = existVideo.MVID; // 下面使用 videoMapper 更新的时候会使用到
                    video.LastScanDate = Jvedio.Common.DateHelper.Now();
                    video.PathExist = 1;
                    toUpdate.Add(video);
                    AddUpdate(video.Path, LangManager.GetValueByKey("SameHashNotSamePath"));
                }
            }

            // 剩余的导入
            noVidList.RemoveAll(arg => existVideos.Where(t => arg.Hash.Equals(t.Hash) && !arg.Path.Equals(t.Path)).Any());

            // 无识别码 + 同路径 + 指纹不同：直接插入会在同一路径下再插一条重复记录，
            // 所以这里拦下来记成"未导入"并说明原因。
            foreach (Video video in noVidList) {
                Video existVideo = existVideos.FirstOrDefault(t => video.Path.Equals(t.Path));
                if (existVideo == null)
                    continue;
                AddNotImport(video.Path, "同路径的文件已变化（指纹不同），未自动更新",
                    $"原记录：{existVideo.Path}{Environment.NewLine}原大小：{existVideo.Size}，本次大小：{video.Size}"
                    + $"{Environment.NewLine}原指纹：{existVideo.Hash}，本次指纹：{video.Hash}");
            }

            noVidList.RemoveAll(arg => existVideos.Where(t => arg.Path.Equals(t.Path)).Any());
            toInsert.AddRange(noVidList);

            // 1.更新（失败不能当"已更新"报给用户，也不该把后面的导入整段打断）
            if (toUpdate.Count > 0) {
                List<string> updatePaths2 = toUpdate.Select(arg => arg.Path).ToList();
                try {
                    videoMapper.UpdateBatch(toUpdate, "SubSection"); // 分段视频
                    List<MetaData> toUpdateData = toUpdate.Select(arg => arg.toMetaData()).ToList();
                    if (toUpdateData.Count > 0)
                        metaDataMapper.UpdateBatch(toUpdateData, "Path", "LastScanDate", "PathExist");
                    AddTags(toUpdate);
                } catch (Exception ex) {
                    Logger.Error($"更新 {toUpdate.Count} 条记录失败：{ex.Message}");
                    LogFileError($"更新 {toUpdate.Count} 条记录失败（首个 {updatePaths2.FirstOrDefault()}）：{ex.Message}");
                    MoveUpdateToNotImport(updatePaths2, "更新记录失败：" + ex.Message);
                }
            }

            // 2.导入
            InsertData(toInsert);
        }

        private void HandleNotImport(Dictionary<string, NotImportReason> notImport)
        {
            if (notImport == null)
                return;
            foreach (var key in notImport.Keys) {
                if (ScanResult.NotImport.ContainsKey(key))
                    continue;
                NotImportReason reason = notImport[key];
                if (reason == NotImportReason.RepetitiveVID) {
                    string vid = Jvedio.Identity.Identify.GetVID(Path.GetFileNameWithoutExtension(key));
                    AddNotImport(key, $"{ReasonToString[reason]} => {vid}");
                } else {
                    AddNotImport(key, ReasonToString[reason]);
                }
            }
        }

        private void HandleFailNFO(List<string> failNFO)
        {
            if (failNFO == null)
                return;
            // 原来是 AddRange(failNFO)：外层已经逐条遍历，内层再整份追加一遍 → n×n 条重复
            foreach (string path in failNFO) {
                if (string.IsNullOrEmpty(path) || ScanResult.FailNFO.Contains(path))
                    continue;
                ScanResult.FailNFO.Add(path);
            }
        }

        private void CopyNfoImage(Dictionary<string, string> dict, List<Video> import, ImageType imageType)
        {
            if (dict == null)
                return;

            switch (imageType) {
                case ImageType.Big:
                    if (dict.ContainsKey("BigImagePath") && !string.IsNullOrEmpty(dict["BigImagePath"])) {
                        string path = dict["BigImagePath"];
                        string dirname = path.ToLower();
                        CopyImage(import, dirname, imageType);
                    }
                    break;
                case ImageType.Small:
                    if (dict.ContainsKey("SmallImagePath") && !string.IsNullOrEmpty(dict["SmallImagePath"])) {
                        string path = dict["SmallImagePath"];
                        string dirname = path.ToLower();
                        CopyImage(import, dirname, imageType);
                    }
                    break;
                case ImageType.Actor:
                case ImageType.Preview:
                case ImageType.ScreenShot:
                    CopyImages(import, imageType);
                    break;
                default:

                    break;
            }
        }


        private string GetImagePathByType(Video video, ImageType type)
        {
            switch (type) {
                case ImageType.Big:
                    return video.GetBigImage();
                case ImageType.Small:
                    return video.GetSmallImage();
                case ImageType.ScreenShot:
                    return video.GetScreenShot();
                case ImageType.Preview:
                    return video.GetExtraImage();
                case ImageType.Actor:
                    return video.GetBigImage();
            }
            return "";
        }

        private void CopyImage(List<Video> import, string dirName, ImageType imageType)
        {
            if (string.IsNullOrEmpty(dirName))
                return;

            foreach (Video item in import) {
                if (string.IsNullOrEmpty(item.Path))
                    continue;
                string dir = Path.GetDirectoryName(item.Path);
                if (!Directory.Exists(dir))
                    continue;
                string[] arr = Jvedio.Common.FileHelper.TryGetAllFiles(dir, "*.*");
                if (arr == null || arr.Length == 0)
                    continue;
                List<string> list = arr.ToList();
                list = list.Where(arg => ScanTask.PICTURE_EXTENSIONS_LIST
                                    .Contains(System.IO.Path.GetExtension(arg).ToLower())).ToList();
                string filename = Path.GetFileName(dirName);
                string originPath = list.Where(arg =>
                    Path.GetFileNameWithoutExtension(arg).ToLower().IndexOf(filename) >= 0).FirstOrDefault();
                if (File.Exists(originPath)) {
                    string targetImagePath = GetImagePathByType(item, imageType);
                    if (!File.Exists(targetImagePath)) {
                        targetImagePath = Path.Combine(Path.GetDirectoryName(targetImagePath),
                            Path.GetFileNameWithoutExtension(targetImagePath) + Path.GetExtension(originPath));
                        Jvedio.Common.FileHelper.TryCopyFile(originPath, targetImagePath, true);
                    } else if (ConfigManager.ScanConfig.CopyNFOOverwriteImage) {
                        Jvedio.Common.FileHelper.TryCopyFile(originPath, targetImagePath, true);
                    }
                }
            }
        }

        private void CopyImages(List<Video> import, ImageType type)
        {
            foreach (Video item in import) {
                if (string.IsNullOrEmpty(item.Path))
                    continue;
                string dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(item.Path), ConfigManager.ScanConfig.CopyNFOPreviewPath));
                if (type == ImageType.ScreenShot)
                    dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(item.Path), ConfigManager.ScanConfig.CopyNFOScreenShotPath));
                else if (type == ImageType.Actor)
                    dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(item.Path), ConfigManager.ScanConfig.CopyNFOActorPath));
                if (!Directory.Exists(dir))
                    continue;
                string[] arr = Jvedio.Common.FileHelper.TryGetAllFiles(dir, "*.*");
                if (arr == null || arr.Length == 0)
                    continue;
                List<string> list = arr.ToList();
                list = list.Where(arg => ScanTask.PICTURE_EXTENSIONS_LIST.Contains(System.IO.Path.GetExtension(arg).ToLower())).ToList();
                // 预览图目录
                string targetPath = item.GetExtraImage();
                if (type == ImageType.ScreenShot)
                    targetPath = item.GetScreenShot();
                else if (type == ImageType.Actor)
                    targetPath = item.GetActorPath();
                Jvedio.Common.DirHelper.TryCreateDirectory(targetPath);
                foreach (var path in list) {
                    string targetFilePath = Path.Combine(targetPath, Path.GetFileName(path));
                    Jvedio.Common.FileHelper.TryCopyFile(path, targetFilePath, ConfigManager.ScanConfig.CopyNFOOverwriteImage);
                }

            }
        }

        private void HandleImportNFO(List<Video> import)
        {
            if (import == null || import.Count <= 0)
                return;

            existVideos = GetExistVideos();
            existActors = actorMapper.SelectList();

            // 解析图片路径

            Dictionary<string, object> picPaths = ConfigManager.Settings.PicPaths;
            if (picPaths != null && picPaths.ContainsKey(PathType.RelativeToData.ToString())) {
                Dictionary<string, string> dict = null;
                try {
                    dict = (Dictionary<string, string>)picPaths[PathType.RelativeToData.ToString()];
                } catch (Exception ex) {
                    Logger.Error(ex.Message);
                }

                // 复制图片
                if (ConfigManager.ScanConfig.CopyNFOPicture) {
                    CopyNfoImage(dict, import, ImageType.Big);
                    CopyNfoImage(dict, import, ImageType.Small);
                }

                if (ConfigManager.ScanConfig.CopyNFOActorPicture)
                    CopyNfoImage(dict, import, ImageType.Actor);

                if (ConfigManager.ScanConfig.CopyNFOPreview)
                    CopyNfoImage(dict, import, ImageType.Preview);

                if (ConfigManager.ScanConfig.CopyNFOScreenShot)
                    CopyNfoImage(dict, import, ImageType.ScreenShot);
            }

            // 1. 需要更新的
            List<Video> toUpdate = new List<Video>();
            List<string> nfoUpdatePaths = new List<string>();
            foreach (Video video in import) {
                // VID 为空时不能参与匹配：existVideos 里也有 VID 为空的无识别码影片，
                // 那样会把 NFO 的信息写到某条毫无关系的影片上。
                if (string.IsNullOrEmpty(video.VID))
                    continue;
                Video existVideo = existVideos.Where(t => video.VID.Equals(t.VID)).FirstOrDefault();
                if (existVideo != null) {
                    video.DataID = existVideo.DataID;
                    video.MVID = existVideo.MVID; // 下面使用 videoMapper 更新的时候会使用到
                    nfoUpdatePaths.Add(video.Path);
                    AddUpdate(video.Path, LangManager.GetValueByKey("UpdateNFO"));
                    video.Path = null;
                    video.PathExist = 1;
                    video.LastScanDate = Jvedio.Common.DateHelper.Now();
                    toUpdate.Add(video);
                    HandleActor(video);
                }
            }

            import.RemoveAll(arg => !string.IsNullOrEmpty(arg.VID) && existVideos.Where(t => arg.VID.Equals(t.VID)).Any());
            // VID 为空的 NFO 也一并从"待导入"里摘掉（无法确定它对应哪个视频）
            import.RemoveAll(arg => string.IsNullOrEmpty(arg.VID));

            if (toUpdate.Count > 0) {
                try {
                    videoMapper.UpdateBatch(toUpdate, NFOUpdateVideoProps);
                    List<MetaData> toUpdateData = toUpdate.Select(arg => arg.toMetaData()).ToList();
                    if (toUpdateData.Count > 0)
                        metaDataMapper.UpdateBatch(toUpdateData, NFOUpdateMetaProps);
                } catch (Exception ex) {
                    Logger.Error($"NFO 更新 {toUpdate.Count} 条记录失败：{ex.Message}");
                    LogFileError($"NFO 更新 {toUpdate.Count} 条记录失败（首个 {nfoUpdatePaths.FirstOrDefault()}）：{ex.Message}");
                    MoveUpdateToNotImport(nfoUpdatePaths, "NFO 更新失败：" + ex.Message);
                }
            }

            // 2. 剩下的都是「只找到 NFO、没找到对应视频」的：
            //    原来直接把 .nfo 路径当视频插库，界面上就多出一条打不开的"影片"。
            foreach (Video video in import) {
                AddNotImport(video.Path, "仅找到 NFO，视频未入库", "没有找到 VID 相同的视频记录");
                LogFileError($"仅找到 NFO，视频未入库：{video.Path}");
            }
        }

        private void HandleActor(Video video)
        {
            // 更新演员
            if (!string.IsNullOrEmpty(video.ActorNames)) {
                List<string> list = video.ActorNames.Split(Jvedio.Common.ConstValues.Separator).ToList();
                List<string> urls = video.ActorThumbs;
                for (int i = 0; i < list.Count; i++) {
                    string name = list[i];
                    string url = i < urls.Count ? urls[i] : string.Empty;
                    ActorInfo actorInfo = existActors?.Where(arg => arg.ActorName.Equals(name)).FirstOrDefault();
                    if (actorInfo == null || actorInfo.ActorID <= 0) {
                        actorInfo = new ActorInfo();
                        actorInfo.ActorName = name;
                        actorInfo.ImageUrl = url;
                        actorMapper.Insert(actorInfo);
                        existActors.Add(actorInfo);
                    } else {
                        actorInfo.ImageUrl = url;
                        actorMapper.UpdateFieldById("ImageUrl", url, actorInfo.ActorID);
                    }

                    // 保存信息
                    string sql = $"insert or ignore into metadata_to_actor (ActorID,DataID) values ({actorInfo.ActorID},{video.DataID})";
                    metaDataMapper.ExecuteNonQuery(sql);
                }
            }
        }

        /// <summary>
        /// 批量导入影片：先写 metadata 拿真实 DataID，再写 metadata_video，全部成功后才记账。
        /// <para>
        /// 改造前的三个致命问题（2026-11-11 事故）：
        /// <list type="number">
        /// <item><b>先记账后写库</b>：进循环就 <c>ScanResult.Import.Add</c>，写库失败被 catch 吞掉，
        ///       界面照样显示"已导入 N 条"；</item>
        /// <item><b>没有事务也没有回滚</b>：metadata 写成功、metadata_video 失败就留下孤儿行
        ///       （列表里看不见、判重也选不到，重扫继续重复插）；</item>
        /// <item><b>DataID 靠 <c>before++</c> 推算</b>：自增起点或并发插入一变就整体错位，
        ///       可能写出 <c>DataID=0</c> 或指向别人的外键。</item>
        /// </list>
        /// 现在：<c>InsertBatch</c> 逐条把真实自增 ID 回写到实体（见 <c>BaseMapper.InsertAndWriteBackId</c>），
        /// 回写值 <c>&lt;=0</c> 视为该条失败；子表写失败时按 DataID 补偿删除刚插入的 metadata 行再抛出。
        /// </para>
        /// </summary>
        private void InsertData(ICollection<Video> toInsert)
        {
            if (toInsert == null || toInsert.Count == 0)
                return;

            List<Video> videos = toInsert.ToList();
            foreach (Video video in videos) {
                video.DBId = ConfigManager.Main.CurrentDBId;
                video.FirstScanDate = Jvedio.Common.DateHelper.Now();
                video.LastScanDate = Jvedio.Common.DateHelper.Now();
                video.PathExist = 1;
            }

            List<MetaData> toInsertData = videos.Select(arg => arg.toMetaData()).ToList();
            if (toInsertData.Count <= 0)
                return;

            // 1. 写 metadata。失败会抛，但 InsertBatch 是逐条执行，前面几条可能已经进库，
            //    所以后面按"DataID 是否被回写"来判断哪些真的成功，而不是整批当成功或整批当失败。
            Exception metaError = null;
            try {
                metaDataMapper.InsertBatch(toInsertData);
            } catch (Exception ex) {
                metaError = ex;
                Logger.Error($"写入 metadata 失败：{ex.Message}");
                LogFileError($"写入 metadata 失败（本批 {toInsertData.Count} 条）：{ex.Message}");
            }

            List<Video> ready = new List<Video>();
            List<Video> metaFailed = new List<Video>();
            for (int i = 0; i < toInsertData.Count; i++) {
                long dataId = toInsertData[i].DataID;
                if (dataId <= 0) {
                    metaFailed.Add(videos[i]);
                    continue;
                }
                videos[i].DataID = dataId;      // 用回写的真实值，不再 before++
                ready.Add(videos[i]);
            }

            if (metaFailed.Count > 0) {
                string reason = metaError == null
                    ? "写库失败：没有取到自增 DataID"
                    : "写库失败：" + metaError.Message;
                foreach (Video video in metaFailed)
                    AddNotImport(video.Path, reason);

                if (metaError == null)
                    LogFileError($"有 {metaFailed.Count} 条 metadata 已写入但没取到自增 DataID，可能留下孤儿行，需要人工核对");
            }

            if (ready.Count == 0) {
                // 一条都没进去 = 写库整体失败；不能当成"没有新片"静默收场，交给上层报错
                if (metaError != null)
                    throw new InvalidOperationException(
                        $"写入 metadata 全部失败（{metaFailed.Count} 条）：{metaError.Message}", metaError);
                return;
            }

            // 2. 写 metadata_video。失败时按 DataID 补偿删除刚插入的 metadata 行，别留孤儿。
            try {
                videoMapper.InsertBatch(ready);
            } catch (Exception ex) {
                List<Video> videoInserted = ready.Where(arg => arg.MVID > 0).ToList();
                List<Video> videoFailed = ready.Where(arg => arg.MVID <= 0).ToList();

                if (videoFailed.Count > 0) {
                    string ids = string.Join(",", videoFailed.Select(arg => arg.DataID));
                    if (!metaDataMapper.TryExecuteNonQuery($"delete from metadata where DataID in ({ids})"))
                        LogFileError($"补偿删除失败，可能残留 {videoFailed.Count} 条孤儿 metadata：{ids}");
                    else
                        LogFile($"已补偿删除 {videoFailed.Count} 条刚插入的 metadata");
                    foreach (Video video in videoFailed)
                        AddNotImport(video.Path, "写入视频表失败：" + ex.Message);
                }

                // 已经写进去的那部分照样记账（它们的数据是完整的）
                foreach (Video video in videoInserted)
                    ScanResult.Import.Add(video.Path);
                ScanResult.InsertVideos.AddRange(videoInserted);

                Logger.Error($"写入 metadata_video 失败：{ex.Message}");
                LogFileError($"写入 metadata_video 失败，成功 {videoInserted.Count} 条、失败 {videoFailed.Count} 条：{ex.Message}");
                throw new InvalidOperationException(
                    $"写入视频表失败（成功 {videoInserted.Count} 条，失败 {videoFailed.Count} 条已回滚主表）：{ex.Message}", ex);
            }

            if (metaError != null) {
                // 部分成功：其余条目已在 metaFailed 里记了原因，这里补一条明确的落盘记录
                LogFileError($"metadata 部分写入失败：本批 {toInsertData.Count} 条，成功 {ready.Count} 条，失败 {metaFailed.Count} 条");
            }

            // 3. 主表 + 子表都写成功了才记账（第 1、2 步失败的行已在 NotImport 里）
            foreach (Video video in ready)
                ScanResult.Import.Add(video.Path);
            ScanResult.InsertVideos.AddRange(ready);

            // 4. 关联数据：标记与演员。失败会抛（数据性写入不吞），但此时影片已经在库里，
            //    调用方 catch 后会把"已导入 N 条"一起报出来。
            AddTags(ready);

            existActors = actorMapper.SelectList();
            foreach (Video video in ready)
                HandleActor(video);
        }

        private void AddTags(ICollection<Video> videos)
        {
            // 处理标记
            List<string> list = new List<string>();
            foreach (Video video in videos) {
                // 高清
                if (video.IsHDV())
                    list.Add($"({video.DataID},1)");

                // 中文
                if (video.IsCHS())
                    list.Add($"({video.DataID},2)");

                // 新加入
                // todo 由于设计之初未考虑到多的空余位置添加标记，因此新标记以 ID 10000开头
                list.Add($"({video.DataID},10000)");
            }

            if (list.Count > 0) {
                string sql = $"insert or ignore into metadata_to_tagstamp (DataID,TagID) values {string.Join(",", list)}";
                videoMapper.ExecuteNonQuery(sql);
            }
        }

        public void CheckStatus()
        {
            if (Status == TaskStatus.Canceled) {
                StopWatch();
                Running = false;
                throw new TaskCanceledException();
            }
        }
    }
}
