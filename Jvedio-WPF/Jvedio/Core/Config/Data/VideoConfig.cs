using Jvedio.Core.Config.Base;
using System;

namespace Jvedio.Core.Config.Data
{
    public class VideoConfig : AbstractConfig
    {
        private VideoConfig() : base("VideoConfig")
        {
            ImageMode = 1;
            GlobalImageWidth = 300;
            SmallImage_Width = 180;
            SmallImage_Height = 150;
            BigImage_Height = 200;
            BigImage_Width = 250;
            GifImage_Width = 250;
            GifImage_Height = 200;
            SortType = 0;
            SortDescending = false;
            OnlyShowSubSection = false;
            PageSize = 20;

            DisplayID = true;
            DisplayTitle = true;
            DisplayDate = true;
            DisplayStamp = true;
            DisplayFavorites = true;
            MainImageAutoMode = true;
            MovieOpacity = 1;

            ShowCreateDateIfReleaseDateEmpty = true;
            ShowFileNameIfTitleEmpty = true;

            ActorEditMode = false;
            ActorSortDescending = true;
            ShowFilter = true;
            ActorShowCount = true;
            BlurBackground = true;

        }

        private long _PageSize;
        public long PageSize {
            get { return _PageSize; }
            set {
                _PageSize = value;
                RaisePropertyChanged();
            }
        }


        #region "Actor"

        private bool _ActorShowCount;
        public bool ActorShowCount {
            get { return _ActorShowCount; }
            set {
                _ActorShowCount = value;
                RaisePropertyChanged();
            }
        }
        private long _ActorViewMode;
        public long ActorViewMode {
            get { return _ActorViewMode; }
            set {
                _ActorViewMode = value;
                RaisePropertyChanged();
            }
        }
        private long _ActorSortType;
        public long ActorSortType {
            get { return _ActorSortType; }
            set {
                _ActorSortType = value;
                RaisePropertyChanged();
            }
        }

        private bool _ActorSortDescending;
        public bool ActorSortDescending {
            get { return _ActorSortDescending; }
            set {
                _ActorSortDescending = value;
                RaisePropertyChanged();
            }
        }
        private bool _ActorEditMode;
        public bool ActorEditMode {
            get { return _ActorEditMode; }
            set {
                _ActorEditMode = value;
                RaisePropertyChanged();
            }
        }

        #endregion

        private double _MovieOpacity;
        public double MovieOpacity {
            get { return _MovieOpacity; }
            set {
                _MovieOpacity = value;
                RaisePropertyChanged();
            }
        }
        private bool _MainImageAutoMode;
        public bool MainImageAutoMode {
            get { return _MainImageAutoMode; }
            set {
                _MainImageAutoMode = value;
                RaisePropertyChanged();
            }
        }
        private bool _ShowCreateDateIfReleaseDateEmpty;
        public bool ShowCreateDateIfReleaseDateEmpty {
            get { return _ShowCreateDateIfReleaseDateEmpty; }
            set {
                _ShowCreateDateIfReleaseDateEmpty = value;
                RaisePropertyChanged();
            }
        }
        private bool _ShowFileNameIfTitleEmpty;
        public bool ShowFileNameIfTitleEmpty {
            get { return _ShowFileNameIfTitleEmpty; }
            set {
                _ShowFileNameIfTitleEmpty = value;
                RaisePropertyChanged();
            }
        }

        private bool _DisplayFavorites;
        public bool DisplayFavorites {
            get { return _DisplayFavorites; }
            set {
                _DisplayFavorites = value;
                RaisePropertyChanged();
            }
        }

        private bool _DisplayStamp;
        public bool DisplayStamp {
            get { return _DisplayStamp; }
            set {
                _DisplayStamp = value;
                RaisePropertyChanged();
            }
        }


        private bool _DisplayDate;
        public bool DisplayDate {
            get { return _DisplayDate; }
            set {
                _DisplayDate = value;
                RaisePropertyChanged();
            }
        }

        private bool _DisplayTitle;
        public bool DisplayTitle {
            get { return _DisplayTitle; }
            set {
                _DisplayTitle = value;
                RaisePropertyChanged();
            }
        }


        private bool _DisplayID;
        public bool DisplayID {
            get { return _DisplayID; }
            set {
                _DisplayID = value;
                RaisePropertyChanged();
            }
        }

        private bool _OnlyShowSubSection;
        public bool OnlyShowSubSection {
            get { return _OnlyShowSubSection; }
            set {
                _OnlyShowSubSection = value;
                RaisePropertyChanged();
            }
        }

        private bool _SortDescending;
        public bool SortDescending {
            get { return _SortDescending; }
            set {
                _SortDescending = value;
                RaisePropertyChanged();
            }
        }


        private long _SortType;
        public long SortType {
            get { return _SortType; }
            set {
                _SortType = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 排序方向一次性迁移标记。
        /// <para>
        /// 老版本在时间类排序（导入时间/创建时间/发行日期）下存的是升序，
        /// 导致打开软件时最新影片在最后一页。启动读配置时若命中该组合，
        /// 强制改为降序并置此标记；之后用户手动选回升序不再被覆盖。
        /// </para>
        /// </summary>
        private bool _SortDirMigrated;
        public bool SortDirMigrated {
            get { return _SortDirMigrated; }
            set {
                _SortDirMigrated = value;
                RaisePropertyChanged();
            }
        }

        public override void Read()
        {
            base.Read();
            MigrateSortDirection();
        }

        private void MigrateSortDirection()
        {
            if (SortDirMigrated)
                return;

            // 先标记再改值：无论本次是否命中，都只迁移一次
            SortDirMigrated = true;
            if (Jvedio.Core.UserControls.ViewModels.VieModel_VideoList.IsTimeSortField((int)SortType) && !SortDescending)
                SortDescending = true;

            // 迁移标记与 SortDescending 都已经在内存里生效，写库只是让「下次启动不必再迁」。
            // Read() 是在启动早期被调用的，此时 SQLITE_BUSY 很常见；而 Save() 失败会抛异常，
            // 让它冒出去会把整个启动带崩——所以这里只记日志：
            // 标记没落盘只是下次启动再迁一次，迁移本身幂等，无害。
            try {
                Save();
            } catch (Exception ex) {
                Jvedio.App.Logger?.Error($"保存排序方向迁移标记失败（下次启动会重新迁移一次，不影响本次运行）：{ex}");
            }
        }

        private long _ImageMode;
        public long ImageMode {
            get { return _ImageMode; }
            set {
                _ImageMode = value;
                RaisePropertyChanged();
            }
        }

        private long _GlobalImageWidth;
        public long GlobalImageWidth {
            get { return _GlobalImageWidth; }
            set {
                _GlobalImageWidth = value;
                RaisePropertyChanged();
            }
        }
        private long _SmallImage_Width;
        public long SmallImage_Width {
            get { return _SmallImage_Width; }
            set {
                _SmallImage_Width = value;
                RaisePropertyChanged();
            }
        }

        private long _SmallImage_Height;
        public long SmallImage_Height {
            get { return _SmallImage_Height; }
            set {
                _SmallImage_Height = value;
                RaisePropertyChanged();
            }
        }
        private long _BigImage_Height;
        public long BigImage_Height {
            get { return _BigImage_Height; }
            set {
                _BigImage_Height = value;
                RaisePropertyChanged();
            }
        }
        private long _BigImage_Width;
        public long BigImage_Width {
            get { return _BigImage_Width; }
            set {
                _BigImage_Width = value;
                RaisePropertyChanged();
            }
        }
        private long _GifImage_Height;
        public long GifImage_Height {
            get { return _GifImage_Height; }
            set {
                _GifImage_Height = value;
                RaisePropertyChanged();
            }
        }
        private long _GifImage_Width;
        public long GifImage_Width {
            get { return _GifImage_Width; }
            set {
                _GifImage_Width = value;
                RaisePropertyChanged();
            }
        }
        private bool _ShowFilter;
        public bool ShowFilter {
            get { return _ShowFilter; }
            set {
                _ShowFilter = value;
                RaisePropertyChanged();
            }
        }
        private bool _BlurBackground;
        public bool BlurBackground {
            get { return _BlurBackground; }
            set {
                _BlurBackground = value;
                RaisePropertyChanged();
            }
        }


        private static VideoConfig _instance = null;

        public static VideoConfig CreateInstance()
        {
            if (_instance == null)
                _instance = new VideoConfig();

            return _instance;
        }

    }
}
