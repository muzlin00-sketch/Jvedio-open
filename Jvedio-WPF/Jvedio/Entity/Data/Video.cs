using Google.Protobuf.WellKnownTypes;
using Jvedio.Core.Enums;
using Jvedio.Core.Global;
using Jvedio.Core.Media;
using Jvedio.Core.Scan;
using Jvedio.Entity.CommonSQL;
using Jvedio.Mapper;
using Newtonsoft.Json;
using SuperControls.Style;
using Jvedio.Common;
using Jvedio.Core.ORM.Attributes;
using Jvedio.Core.ORM.Enums;
using Jvedio.Core.ORM.Wrapper;
using Jvedio.Core.Net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using static Jvedio.App;

namespace Jvedio.Entity
{
    // todo 检视
    [Table(tableName: "metadata_video")]
    public class Video : MetaData
    {
        #region "事件"
        public static Action onPlayVideo;

        #endregion


        #region "静态属性"

        public static string[] HDV { get; set; } =
            new string[] { "hd", "high_definition", "high definition", "高清", "2k", "4k", "8k", "16k", "32k" };


        #endregion


        #region "属性"


        private long _MVID;

        [TableId(IdType.AUTO)]
        public long MVID {
            get { return _MVID; }
            set {
                _MVID = value;
                RaisePropertyChanged();
            }
        }


        /// <summary>
        /// 此处不可删除，需要保证 select 查出 id
        /// </summary>
        public long _DataID;
        public new long DataID {
            get { return _DataID; }
            set {
                _DataID = value;
                RaisePropertyChanged();
            }
        }

        private string _VID;
        public string VID {
            get { return _VID; }
            set {
                _VID = value;
                RaisePropertyChanged();
            }
        }


        private string _Series;

        public string Series {
            get { return _Series; }

            set {
                _Series = value;
                SeriesList = new ObservableCollection<Jvedio.Common.ObservableString>();
                if (!string.IsNullOrEmpty(value))
                    foreach (var item in value.Split(new char[] { Jvedio.Common.ConstValues.Separator }, StringSplitOptions.RemoveEmptyEntries))
                        SeriesList.Add(new Jvedio.Common.ObservableString(item));

                RaisePropertyChanged();
            }
        }

        private ObservableCollection<Jvedio.Common.ObservableString> _SeriesList;
        [TableField(exist: false)]
        public ObservableCollection<Jvedio.Common.ObservableString> SeriesList {
            get { return _SeriesList; }
            set {
                _SeriesList = value;
                RaisePropertyChanged();
            }
        }



        private VideoType _VideoType;

        public VideoType VideoType {
            get { return _VideoType; }

            set {
                _VideoType = value;
                RaisePropertyChanged();
            }
        }

        public string Director { get; set; }

        public string Studio { get; set; }

        public string Publisher { get; set; }

        public string Plot { get; set; }

        public string Outline { get; set; }

        public int Duration { get; set; }


        private ObservableCollection<Jvedio.Common.ObservableString> _SubSectionList { get; set; }

        [TableField(exist: false)]
        public ObservableCollection<Jvedio.Common.ObservableString> SubSectionList {
            get { return _SubSectionList; }
            set {
                _SubSectionList = value;
                RaisePropertyChanged();
            }
        }

        private string _SubSection = string.Empty;

        public string SubSection {
            get { return _SubSection; }

            set {
                _SubSection = value;
                SubSectionList = SubSectionToList(value);
                if (SubSectionList.Count >= 2)
                    HasSubSection = true;
                else
                    HasSubSection = false;
                RaisePropertyChanged();
            }
        }


        private bool _HasSubSection;
        [TableField(exist: false)]
        public bool HasSubSection {
            get { return _HasSubSection; }
            set {
                _HasSubSection = value;
                RaisePropertyChanged();
            }
        }

        [TableField(exist: false)]
        public ObservableCollection<string> PreviewImagePathList { get; set; }

        [TableField(exist: false)]
        public ObservableCollection<BitmapSource> PreviewImageList { get; set; }

        public string ImageUrls { get; set; }

        public string WebType { get; set; }

        public string WebUrl { get; set; }

        public string ExtraInfo { get; set; }

        private BitmapSource _smallimage;

        [TableField(exist: false)]
        public BitmapSource SmallImage {
            get { return _smallimage; }

            set {
                _smallimage = value;
                RaisePropertyChanged();
            }
        }

        private BitmapSource _bigimage;

        [TableField(exist: false)]
        public BitmapSource BigImage {
            get { return _bigimage; }

            set {
                _bigimage = value;
                RaisePropertyChanged();
            }
        }
        private string _BigImagePath;

        [TableField(exist: false)]
        public string BigImagePath {
            get { return _BigImagePath; }

            set {
                _BigImagePath = value;
                RaisePropertyChanged();
            }
        }

        private Uri _GifUri;

        [TableField(exist: false)]
        public Uri GifUri {
            get { return _GifUri; }

            set {
                _GifUri = value;
                RaisePropertyChanged();
            }
        }

        private string _ActorNames;

        [TableField(exist: false)]
        public string ActorNames {
            get { return _ActorNames; }

            set {
                _ActorNames = value;
                if (!string.IsNullOrEmpty(value))
                    ActorNameList = value.Split(new char[] { Jvedio.Common.ConstValues.Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();

                RaisePropertyChanged();
            }
        }

        [TableField(exist: false)]
        public List<string> ActorNameList { get; set; }

        /// <summary>
        /// 旧数据库的 actorID 列表
        /// </summary>
        [TableField(exist: false)]
        public string OldActorIDs { get; set; }

        private List<ActorInfo> _ActorInfos;

        [TableField(exist: false)]
        public List<ActorInfo> ActorInfos {
            get { return _ActorInfos; }

            set {
                _ActorInfos = value;
                if (value != null) {
                    ActorNames = string.Join(Jvedio.Common.ConstValues.SeparatorString,
                        value.Select(arg => arg.ActorName).ToList());
                }

                RaisePropertyChanged();
            }
        }

        [TableField(exist: false)]
        public List<Magnet> Magnets { get; set; }


        private bool _HasAssociation;

        [TableField(exist: false)]
        public bool HasAssociation {

            get { return _HasAssociation; }


            set {
                _HasAssociation = value;
                RaisePropertyChanged();
            }

        }


        private ObservableCollection<long> _AssociationList;

        [TableField(exist: false)]
        public ObservableCollection<long> AssociationList {
            get { return _AssociationList; }
            set {
                _AssociationList = value;
                RaisePropertyChanged();
            }
        }

        // 仅用于 NFO 导入的时候的图片地址
        [TableField(exist: false)]
        public List<string> ActorThumbs { get; set; }

        #endregion



        public Video() : this(true)
        {
        }

        public Video(bool _initDefaultImage = true)
        {
            if (_initDefaultImage)
                InitDefaultImage();
        }

        #region "对外静态方法"


        public static void RefreshTagStamp(ref Video video, long newTagID, bool deleted)
        {
            if (video == null || newTagID <= 0)
                return;
            string tagIDs = video.TagIDs;
            if (!deleted && string.IsNullOrEmpty(tagIDs)) {
                video.TagStamp = new ObservableCollection<TagStamp>();
                video.TagStamp.Add(Jvedio.Entity.CommonSQL.TagStamp.TagStamps.Where(arg => arg.TagID == newTagID).FirstOrDefault());
                video.TagIDs = newTagID.ToString();
            } else {
                List<string> list = tagIDs.Split(',').ToList();
                if (!deleted && !list.Contains(newTagID.ToString()))
                    list.Add(newTagID.ToString());
                if (deleted && list.Contains(newTagID.ToString()))
                    list.Remove(newTagID.ToString());
                video.TagIDs = string.Join(",", list);
                video.TagStamp = new ObservableCollection<TagStamp>();
                foreach (var arg in list) {
                    long.TryParse(arg, out long id);
                    video.TagStamp.Add(Jvedio.Entity.CommonSQL.TagStamp.TagStamps.Where(item => item.TagID == id).FirstOrDefault());
                }
            }
        }


        public void OpenWeb()
        {
            string url = WebUrl;
            if (string.IsNullOrEmpty(url))
                return;
            if (url.IsProperUrl())
                Jvedio.Common.FileHelper.TryOpenUrl(url);
        }


        public static OpenPathType StringToImageType(string type)
        {
            if (type.Equals(SuperControls.Style.LangManager.GetValueByKey("Movie"))) {
                return OpenPathType.Video;
            } else if (type.Equals(SuperControls.Style.LangManager.GetValueByKey("Poster"))) {
                return OpenPathType.Poster;
            } else if (type.Equals(SuperControls.Style.LangManager.GetValueByKey("Thumbnail"))) {
                return OpenPathType.Thumnail;
            } else if (type.Equals(SuperControls.Style.LangManager.GetValueByKey("Preview"))) {
                return OpenPathType.Preview;
            } else if (type.Equals(SuperControls.Style.LangManager.GetValueByKey("ScreenShot"))) {
                return OpenPathType.ScreenShot;
            } else if (type.ToUpper().Equals("GIF")) {
                return OpenPathType.Gif;
            }
            return OpenPathType.Video;
        }

        public void OpenPath(OpenPathType type)
        {
            string target = "";
            if (type == OpenPathType.Video) {
                target = Path;
                if (!File.Exists(Path)) {
                    MessageCard.Error(SuperControls.Style.LangManager.GetValueByKey("Message_FileNotExist") + ": " + Path);
                } else {
                    Jvedio.Common.FileHelper.TryOpenSelectPath(Path);
                }
            } else if (type == OpenPathType.Poster) {
                Jvedio.Common.FileHelper.TryOpenSelectPath(GetBigImage());
            } else if (type == OpenPathType.Thumnail) {
                Jvedio.Common.FileHelper.TryOpenSelectPath(GetSmallImage());
            } else if (type == OpenPathType.Preview) {
                Jvedio.Common.FileHelper.TryOpenSelectPath(GetExtraImage());
            } else if (type == OpenPathType.ScreenShot) {
                Jvedio.Common.FileHelper.TryOpenSelectPath(GetScreenShot());
            } else if (type == OpenPathType.Gif) {
                Jvedio.Common.FileHelper.TryOpenSelectPath(GetGifPath());
            }
        }

        #endregion

        /// <summary>
        /// 延迟加载图片
        /// </summary>
        public void InitDefaultImage()
        {
            SmallImage = MetaData.DefaultSmallImage;
            BigImage = MetaData.DefaultBigImage;
            GifUri = new Uri("pack://application:,,,/Resources/Picture/NoPrinting_G.gif");
            PreviewImageList = new ObservableCollection<BitmapSource>();
        }

        public static void PlayVideoWithPlayer(string filepath, long dataID = 0)
        {
            if (File.Exists(filepath)) {
                bool success = false;
                if (!string.IsNullOrEmpty(ConfigManager.Settings.VideoPlayerPath) && File.Exists(ConfigManager.Settings.VideoPlayerPath)) {
                    success = Jvedio.Common.FileHelper.TryOpenFile(ConfigManager.Settings.VideoPlayerPath, filepath);
                } else {
                    // 使用默认播放器
                    success = Jvedio.Common.FileHelper.TryOpenFile(filepath);
                }

                if (success && dataID > 0) {
                    MapperManager.metaDataMapper.UpdateFieldById("ViewDate", Jvedio.Common.DateHelper.Now(), dataID);
                    onPlayVideo?.Invoke();
                }
            } else {
                MessageCard.Error(SuperControls.Style.LangManager.GetValueByKey("Message_OpenFail") + "：" + filepath);
            }
        }

        public static SelectWrapper<Video> InitWrapper()
        {
            SelectWrapper<Video> wrapper = new SelectWrapper<Video>();
            wrapper.Eq("metadata.DBId", ConfigManager.Main.CurrentDBId)
                .Eq("metadata.DataType", 0);
            return wrapper;
        }

        /// <summary>
        /// 设置标签戳
        /// </summary>
        /// <param name="video"></param>
        public static void SetTagStamps(ref Video video)
        {
            video.TagStamp = new ObservableCollection<TagStamp>();
            if (video == null || string.IsNullOrEmpty(video.TagIDs))
                return;
            List<long> list = video.TagIDs.Split(',').Select(arg => long.Parse(arg)).ToList();
            if (list != null && list.Count > 0) {
                foreach (var item in Jvedio.Entity.CommonSQL.TagStamp.TagStamps.Where(arg => list.Contains(arg.TagID)).ToList())
                    video.TagStamp.Add(item);
            }
        }

        /// <summary>
        /// 设置标题和发行日期
        /// </summary>
        /// <param name="video"></param>
        public static void SetTitleAndDate(ref Video video)
        {
            if (ConfigManager.VideoConfig.ShowFileNameIfTitleEmpty
                && !string.IsNullOrEmpty(video.Path) && string.IsNullOrEmpty(video.Title))
                video.Title = System.IO.Path.GetFileNameWithoutExtension(video.Path);
            if (ConfigManager.VideoConfig.ShowCreateDateIfReleaseDateEmpty
                && !string.IsNullOrEmpty(video.LastScanDate) && string.IsNullOrEmpty(video.ReleaseDate))
                video.ReleaseDate = Jvedio.Common.DateHelper.ToLocalDate(video.LastScanDate);

            //if (string.IsNullOrEmpty(video.VID) && !string.IsNullOrEmpty(video.Title))
            //    video.VID = video.Title;

        }


        public bool ToDownload()
        {
            if (ConfigManager.Settings.DownloadWhenTitleNull) {
                return string.IsNullOrEmpty(Title);
            }
            return string.IsNullOrEmpty(Title) ||
                string.IsNullOrEmpty(WebUrl) ||
                string.IsNullOrEmpty(ImageUrls);
        }

        /// <summary>
        /// ext 必须要带上 '.'
        /// </summary>
        /// <param name="imageType"></param>
        /// <param name="ext">ext 必须要带上 '.'</param>
        /// <returns></returns>
        private string GetImagePath(ImageType imageType, string ext = null)
        {
            string result = string.Empty;
            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            string basePicPath = ConfigManager.Settings.PicPaths[pathType.ToString()].ToString();
            if (pathType != PathType.RelativeToData) {
                if (pathType == PathType.RelativeToApp)
                    basePicPath = System.IO.Path.Combine(PathManager.CurrentUserFolder, basePicPath);
                string saveDir = string.Empty;
                if (imageType == ImageType.Big)
                    saveDir = System.IO.Path.Combine(basePicPath, "BigPic");
                else if (imageType == ImageType.Small)
                    saveDir = System.IO.Path.Combine(basePicPath, "SmallPic");
                else if (imageType == ImageType.Preview)
                    saveDir = System.IO.Path.Combine(basePicPath, "ExtraPic");
                else if (imageType == ImageType.ScreenShot)
                    saveDir = System.IO.Path.Combine(basePicPath, "ScreenShot");
                else if (imageType == ImageType.Gif)
                    saveDir = System.IO.Path.Combine(basePicPath, "Gif");
                else if (imageType == ImageType.Actor) {
                    saveDir = System.IO.Path.Combine(basePicPath, "Actresses");
                    return System.IO.Path.GetFullPath(saveDir);
                }
                if (!Directory.Exists(saveDir))
                    Jvedio.Common.FileHelper.TryCreateDir(saveDir);
                if (!string.IsNullOrEmpty(VID))
                    result = System.IO.Path.Combine(saveDir, $"{VID}{(string.IsNullOrEmpty(ext) ? string.Empty : ext)}");
                else
                    result = System.IO.Path.Combine(saveDir, $"{Hash}{(string.IsNullOrEmpty(ext) ? string.Empty : ext)}");
            } else {
                // todo 其它图片模式
            }

            if (!string.IsNullOrEmpty(result))
                return System.IO.Path.GetFullPath(result);
            return string.Empty;
        }

        public override string ToString()
        {
            return ClassUtils.ToString(this);
        }

        public MetaData toMetaData()
        {
            MetaData metaData = (MetaData)this;
            metaData.DataID = this.DataID;
            return metaData;
        }

        public Dictionary<string, object> ToDictionary()
        {
            List<string> fields = new List<string> { "VideoType", "DataID", "VID", "Size", "Path", "Hash", "DataType" };
            Dictionary<string, object> dict = new Dictionary<string, object>();
            PropertyInfo[] propertyInfos = this.GetType().GetProperties();
            foreach (PropertyInfo info in propertyInfos) {
                if (fields.Contains(info.Name)) {
                    object value = info.GetValue(this);
                    if (value == null)
                        value = string.Empty;
                    dict.Add(info.Name, value);
                }
            }

            return dict;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            Video video = obj as Video;
            return video != null && (video.DataID == this.DataID || video.MVID == this.MVID);
        }

        private static string ParseRelativeImageFileName(string path)
        {
            string dirName = System.IO.Path.GetDirectoryName(path);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path).ToLower();

            string[] arr = Jvedio.Common.FileHelper.TryGetAllFiles(dirName, "*.*");
            if (arr == null || arr.Length == 0)
                return "";
            List<string> list = arr.ToList();

            list = list.Where(arg => ScanTask.PICTURE_EXTENSIONS_LIST.Contains(System.IO.Path.GetExtension(arg).ToLower())).ToList();

            foreach (string item in list) {
                if (System.IO.Path.GetFileNameWithoutExtension(item).ToLower().IndexOf(fileName) >= 0)
                    return item;
            }

            return Jvedio.Common.FileHelper.TryGetFullPath(path);
        }

        private static string ParseRelativePath(string path)
        {
            string rootDir = System.IO.Path.GetDirectoryName(path);
            List<string> list = Jvedio.Common.DirHelper.TryGetDirList(rootDir).ToList();
            string dirName = System.IO.Path.GetFileName(path);
            foreach (string item in list) {
                if (System.IO.Path.GetFileName(item).ToLower().IndexOf(dirName.ToLower()) >= 0)
                    return item;
            }

            return Jvedio.Common.FileHelper.TryGetFullPath(path);
        }

        public string GetSmallImage(string ext = ".jpg", bool searchExt = true)
        {
            string smallImagePath = GetImagePath(ImageType.Small, ext);
            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            if (pathType == PathType.RelativeToData && !string.IsNullOrEmpty(Path) && File.Exists(Path)) {
                string basePicPath = System.IO.Path.GetDirectoryName(Path);
                Dictionary<string, string> dict = (Dictionary<string, string>)ConfigManager.Settings.PicPaths[pathType.ToString()];
                string smallPath = System.IO.Path.Combine(basePicPath, dict["SmallImagePath"]);
                if (string.IsNullOrEmpty(System.IO.Path.GetExtension(smallPath)))
                    smallPath += ext;
                smallImagePath = ParseRelativeImageFileName(smallPath);
            }

            // 替换成其他扩展名
            if (searchExt && !File.Exists(smallImagePath))
                smallImagePath = Jvedio.Common.FileHelper.FindWithExt(smallImagePath, ScanTask.PICTURE_EXTENSIONS_LIST);
            return Jvedio.Common.FileHelper.TryGetFullPath(smallImagePath);
        }

        public string GetBigImage(string ext = ".jpg", bool searchExt = true)
        {
            string bigImagePath = GetImagePath(ImageType.Big, ext);

            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            if (pathType == PathType.RelativeToData && !string.IsNullOrEmpty(Path) && File.Exists(Path)) {
                string basePicPath = System.IO.Path.GetDirectoryName(Path);
                Dictionary<string, string> dict = (Dictionary<string, string>)ConfigManager.Settings.PicPaths[pathType.ToString()];
                string bigPath = Jvedio.Common.FileHelper.TryGetFullPath(System.IO.Path.Combine(basePicPath, dict["BigImagePath"]));
                if (string.IsNullOrEmpty(System.IO.Path.GetExtension(bigPath)))
                    bigPath += ext;
                bigImagePath = ParseRelativeImageFileName(bigPath);
            }

            // 替换成其他扩展名
            if (searchExt && !File.Exists(bigImagePath))
                bigImagePath = Jvedio.Common.FileHelper.FindWithExt(bigImagePath, ScanTask.PICTURE_EXTENSIONS_LIST);
            return Jvedio.Common.FileHelper.TryGetFullPath(bigImagePath);
        }

        public string GetExtraImage()
        {
            string imagePath = GetImagePath(ImageType.Preview);

            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            if (pathType == PathType.RelativeToData && !string.IsNullOrEmpty(Path) && File.Exists(Path)) {
                string basePicPath = System.IO.Path.GetDirectoryName(Path);
                Dictionary<string, string> dict = (Dictionary<string, string>)ConfigManager.Settings.PicPaths[pathType.ToString()];
                string path = Jvedio.Common.FileHelper.TryGetFullPath(System.IO.Path.Combine(basePicPath, dict["PreviewImagePath"]));
                imagePath = ParseRelativePath(path);
            }

            return Jvedio.Common.FileHelper.TryGetFullPath(imagePath);
        }
        public string GetActorPath()
        {
            string imagePath = GetImagePath(ImageType.Actor);

            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            if (pathType == PathType.RelativeToData && !string.IsNullOrEmpty(Path) && File.Exists(Path)) {
                string basePicPath = System.IO.Path.GetDirectoryName(Path);
                Dictionary<string, string> dict = (Dictionary<string, string>)ConfigManager.Settings.PicPaths[pathType.ToString()];
                string path = Jvedio.Common.FileHelper.TryGetFullPath(System.IO.Path.Combine(basePicPath, dict["ActorImagePath"]));
                imagePath = ParseRelativePath(path);
            }

            return Jvedio.Common.FileHelper.TryGetFullPath(imagePath);
        }

        public string GetScreenShot()
        {
            string imagePath = GetImagePath(ImageType.ScreenShot);

            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            if (pathType == PathType.RelativeToData && !string.IsNullOrEmpty(Path) && File.Exists(Path)) {
                string basePicPath = System.IO.Path.GetDirectoryName(Path);
                Dictionary<string, string> dict = (Dictionary<string, string>)ConfigManager.Settings.PicPaths[pathType.ToString()];
                string path = Jvedio.Common.FileHelper.TryGetFullPath(System.IO.Path.Combine(basePicPath, dict["ScreenShotPath"]));
                imagePath = ParseRelativePath(path);
            }

            return imagePath;
        }

        /// <summary>
        /// 相对影片下，不支持 gif 截图，截图任务会提示给定关键字不在字典中
        /// </summary>
        /// <returns></returns>
        public string GetGifPath()
        {
            string imagePath = GetImagePath(ImageType.Gif, ".gif");

            PathType pathType = (PathType)ConfigManager.Settings.PicPathMode;
            if (pathType == PathType.RelativeToData && !string.IsNullOrEmpty(Path) && File.Exists(Path)) {
                string basePicPath = System.IO.Path.GetDirectoryName(Path);
                Dictionary<string, string> dict = (Dictionary<string, string>)ConfigManager.Settings.PicPaths[pathType.ToString()];
                string path = Jvedio.Common.FileHelper.TryGetFullPath(System.IO.Path.Combine(basePicPath, dict["Gif"]));
                imagePath = ParseRelativePath(path);
            }

            return Jvedio.Common.FileHelper.TryGetFullPath(imagePath);
        }



        public bool ParseDictInfo(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0)
                return false;
            PropertyInfo[] propertyInfos = this.GetType().GetProperties();
            foreach (PropertyInfo info in propertyInfos) {
                if (dict.ContainsKey(info.Name)) {
                    object value = dict[info.Name];
                    if (value == null)
                        continue;
                    if (value is List<string> list) {
                        info.SetValue(this, string.Join(Jvedio.Common.ConstValues.SeparatorString, list));
                    } else if (info.PropertyType == typeof(string)) {
                        info.SetValue(this, value.ToString());
                    } else if (info.PropertyType == typeof(int)) {
                        // 兼容 int / long / 数字字符串（如 "173分钟" 取不到就留 0）
                        if (value is int intValue)
                            info.SetValue(this, intValue);
                        else if (value is long longValue)
                            info.SetValue(this, (int)longValue);
                        else if (int.TryParse(value.ToString(), out int parsedInt))
                            info.SetValue(this, parsedInt);
                    } else if (info.PropertyType == typeof(float)) {
                        // 刮削器（内置源 / 插件）返回的评分可能是 double、decimal 或字符串
                        float parsedFloat;
                        if (value is float floatValue) {
                            info.SetValue(this, floatValue);
                        } else if (value is double doubleValue) {
                            info.SetValue(this, (float)doubleValue);
                        } else if (value is decimal decimalValue) {
                            info.SetValue(this, (float)decimalValue);
                        } else if (float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out parsedFloat)) {
                            info.SetValue(this, parsedFloat);
                        }
                    }
                }
            }

            // 图片地址
            ImageUrls = ParseImageUrlFromDict(dict);
            return true;
        }

        private string ParseImageUrlFromDict(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0)
                return string.Empty;
            Dictionary<string, object> result = Jvedio.Common.JsonUtils.TryDeserializeObject<Dictionary<string, object>>(ImageUrls);
            if (result == null)
                result = new Dictionary<string, object>();
            if (dict.ContainsKey("SmallImageUrl"))
                result["SmallImageUrl"] = dict["SmallImageUrl"];
            if (dict.ContainsKey("BigImageUrl"))
                result["BigImageUrl"] = dict["BigImageUrl"];
            if (dict.ContainsKey("ExtraImageUrl"))
                result["ExtraImageUrl"] = dict["ExtraImageUrl"];
            if (dict.ContainsKey("ActressImageUrl"))
                result["ActressImageUrl"] = dict["ActressImageUrl"];
            if (dict.ContainsKey("ActorNames"))
                result["ActorNames"] = dict["ActorNames"];
            return JsonConvert.SerializeObject(result);
        }

        public void SaveNfo()
        {
            if (!ConfigManager.Settings.SaveInfoToNFO)
                return;
            string dir = ConfigManager.Settings.NFOSavePath;
            bool overrideInfo = ConfigManager.DownloadConfig.OverrideInfo;

            string saveName = $"{VID.ToProperFileName()}.nfo";
            if (string.IsNullOrEmpty(VID))
                saveName = $"{System.IO.Path.GetFileNameWithoutExtension(Path)}.nfo";

            string saveFileName = string.Empty;

            if (Directory.Exists(dir))
                saveFileName = System.IO.Path.Combine(dir, saveName);

            // 与视频同路径，视频存在才行
            if (!Directory.Exists(dir) && File.Exists(Path))
                saveFileName = System.IO.Path.Combine(new FileInfo(Path).DirectoryName, saveName);

            if (string.IsNullOrEmpty(saveFileName))
                return;
            if (overrideInfo || !File.Exists(saveFileName))
                SaveToNFO(this, saveFileName);
        }

        public static string ToSqlField(string content)
        {
            if (content == SuperControls.Style.LangManager.GetValueByKey("ID")) {
                return "VID";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Title")) {
                return "Title";
            }

              // else if (content == SuperControls.Style.LangManager.GetValueByKey("TranslatedTitle"))
              // {
              //    return "chinesetitle";
              // }
              else if (content == SuperControls.Style.LangManager.GetValueByKey("VideoType")) {
                return "VideoType";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Tag")) {
                return "Series";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("ReleaseDate")) {
                return "ReleaseDate";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Year")) {
                return "ReleaseYear";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Duration")) {
                return "Duration";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Country")) {
                return "Country";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Director")) {
                return "Director";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Genre")) {
                return "Genre";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Label")) {
                return "Label";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Actor")) {
                return "ActorNames";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Studio")) {
                return "Studio";
            } else if (content == SuperControls.Style.LangManager.GetValueByKey("Rating")) {
                return "Rating";
            } else {
                return content;
            }
        }

        public string[] ToFileName()
        {
            bool addTag = ConfigManager.RenameConfig.AddRenameTag;
            string formatString = ConfigManager.RenameConfig.FormatString;
            FileInfo fileInfo = new FileInfo(Path);
            string name = System.IO.Path.GetFileNameWithoutExtension(Path);
            string dir = fileInfo.Directory.FullName;
            string ext = fileInfo.Extension;
            string newName = string.Empty;
            MatchCollection matches = Regex.Matches(formatString, "\\{[a-zA-Z]+\\}");
            PropertyInfo[] propertyList = this.GetType().GetProperties();

            if (matches != null && matches.Count > 0) {
                newName = formatString;
                foreach (Match match in matches) {
                    string property = match.Value.Replace("{", string.Empty).Replace("}", string.Empty);
                    try {
                        ReplaceWithValue(ref newName, property, propertyList);
                    } catch (Exception ex) {
                        throw ex;
                    }
                }
            }

            // 替换掉特殊字符
            foreach (char item in System.IO.Path.GetInvalidFileNameChars()) {
                newName = newName.Replace(item.ToString(), string.Empty);
            }

            if (ConfigManager.RenameConfig.RemoveTitleSpace)
                newName = newName.Trim();

            if (HasSubSection) {
                string[] result = new string[SubSectionList.Count];
                for (int i = 0; i < SubSectionList.Count; i++) {
                    if (addTag && Jvedio.Identity.Identify.IsCHS(Path))
                        result[i] = System.IO.Path.Combine(dir, $"{newName}-{i + 1}_{SuperControls.Style.LangManager.GetValueByKey("Translated")}{ext}");
                    else
                        result[i] = System.IO.Path.Combine(dir, $"{newName}-{i + 1}{ext}");
                }

                return result;
            } else {
                if (addTag && Jvedio.Identity.Identify.IsCHS(Path))
                    return new string[] { System.IO.Path.Combine(dir, $"{newName}_{SuperControls.Style.LangManager.GetValueByKey("Translated")}{ext}") };
                else
                    return new string[] { System.IO.Path.Combine(dir, $"{newName}{ext}") };
            }
        }

        private void ReplaceWithValue(ref string result, string property, PropertyInfo[] propertyList)
        {
            string inSplit = ConfigManager.RenameConfig.InSplit.Equals("[null]") ? string.Empty : ConfigManager.RenameConfig.InSplit;
            foreach (PropertyInfo item in propertyList) {
                string name = item.Name;
                if (name == property) {
                    object o = item.GetValue(this);
                    if (o != null) {
                        string value = o.ToString();

                        if (property == "ActorNames" || property == "Genre" || property == "Label")
                            value = value.Replace(Jvedio.Common.ConstValues.SeparatorString, inSplit);

                        if (property == "VideoType") {
                            int v = 0;
                            int.TryParse(value, out v);
                            if (v == 1)
                                value = SuperControls.Style.LangManager.GetValueByKey("Uncensored");
                            else if (v == 2)
                                value = SuperControls.Style.LangManager.GetValueByKey("Censored");
                            else if (v == 3)
                                value = SuperControls.Style.LangManager.GetValueByKey("Europe");
                        }

                        if (string.IsNullOrEmpty(value)) {
                            // 如果值为空，则删掉前面的分隔符
                            int idx = result.IndexOf("{" + property + "}");
                            if (idx >= 1) {
                                result = result.Remove(idx - 1, 1);
                            }

                            result = result.Replace("{" + property + "}", string.Empty);
                        } else
                            result = result.Replace("{" + property + "}", value);
                    } else {
                        int idx = result.IndexOf("{" + property + "}");
                        if (idx >= 1) {
                            result = result.Remove(idx - 1);
                        }

                        result = result.Replace("{" + property + "}", string.Empty);
                    }

                    break;
                }
            }
        }


        public bool IsHDV()
        {
            return Jvedio.Identity.Identify.IsHDV(Size) ||
                   Jvedio.Identity.Identify.IsHDV(Path) ||
                   Genre?.IndexOfAnyString(Main.TagStringHD) >= 0 ||
                   Series?.IndexOfAnyString(Main.TagStringHD) >= 0 ||
                   Label?.IndexOfAnyString(Main.TagStringHD) >= 0;
        }

        public bool IsCHS()
        {
            return Jvedio.Identity.Identify.IsCHS(Path) ||
                   Genre?.IndexOfAnyString(Main.TagStringTranslated) >= 0 ||
                   Series?.IndexOfAnyString(Main.TagStringTranslated) >= 0 ||
                   Label?.IndexOfAnyString(Main.TagStringTranslated) >= 0;
        }

        public static void SetImage(ref Video video, string imgPath)
        {
            if (video == null)
                return;
            BitmapImage image =
                ImageCache.Get(imgPath, Jvedio.Core.WindowConfig.Main.MAX_IMAGE_WIDTH);
            if (image == null)
                image = MetaData.DefaultBigImage;
            video.ViewImage = image;
        }

        private static string GetScreenShot(string[] arr)
        {
            return arr[arr.Length / 2];
        }


        private static void SetScreenShotImage(ref Video video)
        {

            if (ConfigManager.Settings.AutoGenScreenShot) {
                // 检查有无截图
                string path = video.GetScreenShot();
                if (Directory.Exists(path)) {
                    string[] array = Jvedio.Common.FileHelper.TryScanDIr(path, "*.*", System.IO.SearchOption.TopDirectoryOnly);
                    if (array.Length > 0) {
                        string targetPath = GetScreenShot(array);
                        Video.SetImage(ref video, targetPath);
                        video.BigImagePath = targetPath;
                    }
                }
            }
        }


        public static void SetImage(ref Video video, int imageMode = 0)
        {
            video.BigImagePath = video.GetBigImage();

            BitmapImage smallimage = ImageCache.Get(video.GetSmallImage(), Jvedio.Core.WindowConfig.Main.MAX_IMAGE_WIDTH);
            BitmapImage bigimage = ImageCache.Get(video.BigImagePath, Jvedio.Core.WindowConfig.Main.MAX_IMAGE_WIDTH);

            bool findScreenShot = false;

            video.SmallImage = null;
            video.BigImage = null;
            if (smallimage == null) {
                SetScreenShotImage(ref video);
                findScreenShot = true;
                if (video.ViewImage != null)
                    video.SmallImage = video.ViewImage;
                else
                    video.SmallImage = DefaultSmallImage;
            } else {
                video.SmallImage = smallimage;
            }

            if (bigimage == null) {
                if (!findScreenShot)
                    SetScreenShotImage(ref video);

                if (video.ViewImage != null)
                    video.BigImage = video.ViewImage;
                else
                    video.BigImage = DefaultBigImage;
            } else {
                video.BigImage = bigimage;
            }
        }

        /// <summary>
        /// 获取视频信息 （wmv  10ms，其他  100ms）
        /// </summary>
        public static VideoInfo GetMediaInfo(string videoPath)
        {
            VideoInfo videoInfo = new VideoInfo();
            if (File.Exists(videoPath)) {
                MediaInfo mI = null;
                try {
                    mI = new MediaInfo();
                    mI.Open(videoPath);

                    // 全局
                    string format = mI.Get(StreamKind.General, 0, "Format");
                    string bitrate = mI.Get(StreamKind.General, 0, "BitRate/String");
                    string duration = mI.Get(StreamKind.General, 0, "Duration/String1");
                    string fileSize = mI.Get(StreamKind.General, 0, "FileSize/String");

                    // 视频
                    string vid = mI.Get(StreamKind.Video, 0, "ID");
                    string video = mI.Get(StreamKind.Video, 0, "Format");
                    string vBitRate = mI.Get(StreamKind.Video, 0, "BitRate/String");
                    string vSize = mI.Get(StreamKind.Video, 0, "StreamSize/String");
                    string width = mI.Get(StreamKind.Video, 0, "Width");
                    string height = mI.Get(StreamKind.Video, 0, "Height");
                    string risplayAspectRatio = mI.Get(StreamKind.Video, 0, "DisplayAspectRatio/String");
                    string risplayAspectRatio2 = mI.Get(StreamKind.Video, 0, "DisplayAspectRatio");
                    string frameRate = mI.Get(StreamKind.Video, 0, "FrameRate/String");
                    string bitDepth = mI.Get(StreamKind.Video, 0, "BitDepth/String");
                    string pixelAspectRatio = mI.Get(StreamKind.Video, 0, "PixelAspectRatio");
                    string encodedLibrary = mI.Get(StreamKind.Video, 0, "Encoded_Library");
                    string encodeTime = mI.Get(StreamKind.Video, 0, "Encoded_Date");
                    string codecProfile = mI.Get(StreamKind.Video, 0, "Codec_Profile");
                    string frameCount = mI.Get(StreamKind.Video, 0, "FrameCount");

                    // 音频
                    string aid = mI.Get(StreamKind.Audio, 0, "ID");
                    string audio = mI.Get(StreamKind.Audio, 0, "Format");
                    string aBitRate = mI.Get(StreamKind.Audio, 0, "BitRate/String");
                    string samplingRate = mI.Get(StreamKind.Audio, 0, "SamplingRate/String");
                    string channel = mI.Get(StreamKind.Audio, 0, "Channel(s)");
                    string aSize = mI.Get(StreamKind.Audio, 0, "StreamSize/String");

                    string audioInfo = mI.Get(StreamKind.Audio, 0, "Inform") + mI.Get(StreamKind.Audio, 1, "Inform") + mI.Get(StreamKind.Audio, 2, "Inform") + mI.Get(StreamKind.Audio, 3, "Inform");
                    string vi = mI.Get(StreamKind.Video, 0, "Inform");

                    videoInfo = new VideoInfo() {
                        Format = format,
                        BitRate = vBitRate,
                        Duration = duration,
                        FileSize = fileSize,
                        Width = width,
                        Height = height,

                        DisplayAspectRatio = risplayAspectRatio,
                        FrameRate = frameRate,
                        BitDepth = bitDepth,
                        PixelAspectRatio = pixelAspectRatio,
                        Encoded_Library = encodedLibrary,
                        FrameCount = frameCount,
                        AudioFormat = audio,
                        AudioBitRate = aBitRate,
                        AudioSamplingRate = samplingRate,
                        Channel = channel,
                    };
                } catch (Exception ex) {
                    Logger.Error(ex);
                } finally {
                    mI?.Close();
                }
            }

            if (!string.IsNullOrEmpty(videoInfo.Width) && !string.IsNullOrEmpty(videoInfo.Height))
                videoInfo.Resolution = videoInfo.Width + "x" + videoInfo.Height;
            if (!string.IsNullOrEmpty(videoPath)) {
                videoInfo.Extension = System.IO.Path.GetExtension(videoPath)?.ToUpper().Replace(".", string.Empty);
                videoInfo.FileName = System.IO.Path.GetFileNameWithoutExtension(videoPath);
            }

            return videoInfo;
        }

        /// <summary>
        /// 保存信息到 NFO 文件
        /// </summary>
        /// <param name="video"></param>
        /// <param name="nfoPath"></param>
        public static void SaveToNFO(Video video, string nfoPath)
        {
            var nfo = new NFO(nfoPath, "movie");
            nfo.SetNodeText("source", video.WebUrl);
            nfo.SetNodeText("title", video.Title);
            nfo.SetNodeText("director", video.Director);
            nfo.SetNodeText("rating", video.Rating.ToString());
            nfo.SetNodeText("year", video.ReleaseYear.ToString());

            // nfo.SetNodeText("countrycode", video.Country.ToString());
            nfo.SetNodeText("release", video.ReleaseDate);
            nfo.SetNodeText("premiered", video.ReleaseDate);
            nfo.SetNodeText("runtime", video.Duration.ToString());
            nfo.SetNodeText("country", video.Country);
            nfo.SetNodeText("studio", video.Studio);
            nfo.SetNodeText("id", video.VID);
            nfo.SetNodeText("num", video.VID);

            // 类别
            foreach (var item in video.Genre?.Split(Jvedio.Common.ConstValues.Separator)) {
                if (!string.IsNullOrEmpty(item))
                    nfo.AppendNewNode("genre", item);
            }

            // 系列
            foreach (var item in video.Series?.Split(Jvedio.Common.ConstValues.Separator)) {
                if (!string.IsNullOrEmpty(item))
                    nfo.AppendNewNode("tag", item);
            }

            try {
                Dictionary<string, object> dict = Jvedio.Common.JsonUtils.TryDeserializeObject<Dictionary<string, object>>(video.ImageUrls);
                if (dict != null && dict.ContainsKey("ExtraImageUrl")) {
                    List<string> imageUrls = Jvedio.Common.JsonUtils.TryDeserializeObject<List<string>>(dict["ExtraImageUrl"].ToString());
                    if (imageUrls != null && imageUrls.Count > 0) {
                        nfo.AppendNewNode("fanart");
                        foreach (var item in imageUrls) {
                            if (!string.IsNullOrEmpty(item))
                                nfo.AppendNodeToNode("fanart", "thumb", item, "preview", item);
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error(ex);
            }

            if (video.ActorInfos != null && video.ActorInfos.Count > 0) {
                foreach (ActorInfo info in video.ActorInfos) {
                    if (!string.IsNullOrEmpty(info.ActorName)) {
                        nfo.AppendNewNode("actor");
                        nfo.AppendNodeToNode("actor", "name", info.ActorName);
                        nfo.AppendNodeToNode("actor", "type", "Actor");
                    }
                }
            }
        }

        public override int GetHashCode()
        {
            int hashCode = -485885450;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + DataID.GetHashCode();
            return hashCode;
        }

        /// <summary>
        /// 设置关联
        /// </summary>
        /// <param name="video"></param>
        /// <param name="dataID"></param>
        public static void SetAsso(ref Video video)
        {
            video.HasAssociation = false;
            video.AssociationList = new ObservableCollection<long>();

            HashSet<long> set = MapperManager.associationMapper.GetAssociationDatas(video.DataID);

            if (set != null) {
                video.HasAssociation = set.Count > 0;
                foreach (var item in set.ToArray()) {
                    video.AssociationList.Add(item);
                }
            }
        }

        /// <summary>
        /// 批量设置关联，渲染一页影片前调用一次。
        /// <para>
        /// 原实现逐条调用 <see cref="SetAsso"/>，每次都会重建整张邻接表，
        /// 一页 N 条影片即 N 次全表查询，是列表渲染卡顿的主要来源。
        /// </para>
        /// </summary>
        public static void SetAssoBatch(IList<Video> videos)
        {
            if (videos == null || videos.Count == 0)
                return;

            var ids = new List<long>(videos.Count);
            foreach (var v in videos) {
                if (v != null)
                    ids.Add(v.DataID);
            }

            Dictionary<long, HashSet<long>> batch =
                MapperManager.associationMapper.GetAssociationDatasBatch(ids);

            foreach (var video in videos) {
                if (video == null)
                    continue;

                video.HasAssociation = false;
                video.AssociationList = new ObservableCollection<long>();

                HashSet<long> set;
                if (batch != null && batch.TryGetValue(video.DataID, out set) && set != null) {
                    video.HasAssociation = set.Count > 0;
                    foreach (long item in set)
                        video.AssociationList.Add(item);
                }
            }
        }

        public static Video GetById(long dataID)
        {
            Video video = MapperManager.videoMapper.SelectVideoByID(dataID);
            SetImage(ref video);
            SetTagStamps(ref video);
            SetTitleAndDate(ref video);
            SetAsso(ref video);
            return video;
        }

        public static List<Video> GetAllByDBID(long dbid)
        {

            SelectWrapper<Video> wrapper = new SelectWrapper<Video>();
            wrapper.Eq("DBId", dbid).Eq("DataType", "0");
            string[] SelectFields =
            {
                "DISTINCT metadata.DataID",
                "MVID",
                "VID",
                "metadata.Grade",
                "metadata.Title",
            };

            wrapper.Select(SelectFields);
            string sql = wrapper.ToSelect(false) + VideoMapper.SQL_BASE + wrapper.ToWhere(false);

            List<Dictionary<string, object>> list = MapperManager.metaDataMapper.Select(sql);
            List<Video> videos = MapperManager.metaDataMapper.ToEntity<Video>(list, typeof(Video).GetProperties(), false);

            return videos;
        }

        private bool CanSubSectionSortByNum(List<string> list)
        {
            if (list == null || list.Count == 0)
                return false;

            bool[] result = new bool[list.Count];

            for (int i = 0; i < list.Count; i++) {
                for (int j = 0; j < list.Count; j++) {
                    string value = $"-{(i + 1)}";
                    if (list[j].IndexOf(value) >= 0) {
                        result[i] = true;
                        break;
                    }
                }
            }
            return result.All(arg => arg);
        }

        private ObservableCollection<Jvedio.Common.ObservableString> SubSectionToList(string subSection)
        {
            ObservableCollection<Jvedio.Common.ObservableString> result = new ObservableCollection<Jvedio.Common.ObservableString>();

            if (string.IsNullOrEmpty(subSection) || subSection.IndexOf(Jvedio.Common.ConstValues.Separator) <= 0)
                return result;

            List<string> list =
                subSection.Split(new char[] { Jvedio.Common.ConstValues.Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
            List<string> orderList;

            // 大于 10 才需要排序，其他的由用户添加顺序决定
            if (list.Count >= 10 && CanSubSectionSortByNum(list)) {
                orderList = list.OrderBy(arg => arg, new SubSectionComparer()).ToList();
            } else {
                orderList = list;
            }

            foreach (var item in orderList) {
                if (item == null || string.IsNullOrEmpty(item.Trim()))
                    continue;
                result.Add(new Jvedio.Common.ObservableString(item));
            }
            return result;
        }

        /// <summary>
        /// 投影为纯数据记录（丢弃所有 UI 状态与非持久化成员）。
        /// </summary>
        public Record.VideoRecord ToRecord()
        {
            return new Record.VideoRecord {
                // metadata
                DataID = this.DataID,
                DBId = this.DBId,
                Title = this.Title,
                Size = this.Size,
                Path = this.Path,
                Hash = this.Hash,
                Country = this.Country,
                ReleaseDate = this.ReleaseDate,
                ReleaseYear = this.ReleaseYear,
                ViewCount = this.ViewCount,
                DataType = (int)this.DataType,
                Rating = this.Rating,
                RatingCount = this.RatingCount,
                FavoriteCount = this.FavoriteCount,
                Genre = this.Genre,
                Grade = this.Grade,
                Label = this.Label,
                ViewDate = this.ViewDate,
                FirstScanDate = this.FirstScanDate,
                LastScanDate = this.LastScanDate,
                CreateDate = this.CreateDate,
                UpdateDate = this.UpdateDate,
                PathExist = this.PathExist,
                TagIDs = this.TagIDs,

                // metadata_video
                MVID = this.MVID,
                VID = this.VID,
                Series = this.Series,
                VideoType = (int)this.VideoType,
                Director = this.Director,
                Studio = this.Studio,
                Publisher = this.Publisher,
                Plot = this.Plot,
                Outline = this.Outline,
                Duration = this.Duration,
                SubSection = this.SubSection,
                ImageUrls = this.ImageUrls,
                WebType = this.WebType,
                WebUrl = this.WebUrl,
                ExtraInfo = this.ExtraInfo,

                ActorNames = this.ActorNames,
            };
        }

        /// <summary>
        /// 从纯数据记录还原为实体（仅填充持久化字段，UI 状态保持默认）。
        /// </summary>
        public static Video FromRecord(Record.VideoRecord record)
        {
            if (record == null)
                return null;

            return new Video {
                DataID = record.DataID,
                DBId = record.DBId,
                Title = record.Title,
                Size = record.Size,
                Path = record.Path,
                Hash = record.Hash,
                Country = record.Country,
                ReleaseDate = record.ReleaseDate,
                ReleaseYear = record.ReleaseYear,
                ViewCount = record.ViewCount,
                DataType = (DataType)record.DataType,
                Rating = record.Rating,
                RatingCount = record.RatingCount,
                FavoriteCount = record.FavoriteCount,
                Genre = record.Genre,
                Grade = record.Grade,
                Label = record.Label,
                ViewDate = record.ViewDate,
                FirstScanDate = record.FirstScanDate,
                LastScanDate = record.LastScanDate,
                CreateDate = record.CreateDate,
                UpdateDate = record.UpdateDate,
                PathExist = record.PathExist,
                TagIDs = record.TagIDs,

                MVID = record.MVID,
                VID = record.VID,
                Series = record.Series,
                VideoType = (VideoType)record.VideoType,
                Director = record.Director,
                Studio = record.Studio,
                Publisher = record.Publisher,
                Plot = record.Plot,
                Outline = record.Outline,
                Duration = record.Duration,
                SubSection = record.SubSection,
                ImageUrls = record.ImageUrls,
                WebType = record.WebType,
                WebUrl = record.WebUrl,
                ExtraInfo = record.ExtraInfo,

                ActorNames = record.ActorNames,
            };
        }
    }


    /// <summary>
    /// 仅支持形如 -1, -2, -3, ... 的分段视频
    /// </summary>
    public class SubSectionComparer : IComparer<string>
    {

        // ABCD-123-1
        // ABCD-123-2
        // ABCD-123-3
        // ABCD-123-11

        public int Compare(string path1, string path2)
        {
            string name1 = Path.GetFileNameWithoutExtension(path1);
            string name2 = Path.GetFileNameWithoutExtension(path2);
            // 必须用原始识别码：下方 IndexOf 依赖它与原文件名严格一致的长度和内容
            string vid = Jvedio.Identity.Identify.GetRawVID(name1);

            int idx1 = name1.IndexOf(vid) + vid.Length + "-".Length;
            int idx2 = name2.IndexOf(vid) + vid.Length + "-".Length;

            string v1 = name1.Substring(idx1);
            string v2 = name2.Substring(idx2);
            int.TryParse(v1, out int c1);
            int.TryParse(v2, out int c2);
            return c1 - c2;
        }
    }
}
