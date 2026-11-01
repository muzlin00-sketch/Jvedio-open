using Jvedio.Core.Config.Base;

namespace Jvedio.Core.Config
{
    public class ScanConfig : AbstractConfig
    {
        /// <summary>
        /// 最小文件大小默认值，单位 MB。0 表示不限制。
        /// <para>
        /// 原注释写的是「1 MB」，但值是 0，会让人误以为默认会过滤小文件。
        /// 以值为准：默认不过滤。
        /// </para>
        /// </summary>
        public const double DEFAULT_MIN_FILE_SIZE = 0;

        private ScanConfig() : base("ScanConfig")
        {
            CopyNFOPicture = true;
            CopyNFOPreview = true;
            CopyNFOActorPicture = true;
            CopyNFOScreenShot = true;

            CopyNFOActorPath = ".actor";
            CopyNFOPreviewPath = ".preview";
            CopyNFOScreenShotPath = ".screenshot";

            FetchVID = true;
            LoadDataAfterScan = true;
            DataExistsIndexAfterScan = true;
            ImageExistsIndexAfterScan = true;
            ScanNfo = true;
        }

        private static ScanConfig _instance = null;

        public static ScanConfig CreateInstance()
        {
            if (_instance == null)
                _instance = new ScanConfig();

            return _instance;
        }

        public double _MinFileSize = DEFAULT_MIN_FILE_SIZE;

        public double MinFileSize {
            get {
                return _MinFileSize;
            }

            set {
                if (value < 0)
                    _MinFileSize = DEFAULT_MIN_FILE_SIZE;
                else
                    _MinFileSize = value;
            }
        }

        public bool ScanOnStartUp { get; set; }

        public bool CopyNFOOverwriteImage { get; set; }
        public bool CopyNFOPicture { get; set; }
        public bool CopyNFOActorPicture { get; set; }
        public bool CopyNFOPreview { get; set; }
        public bool CopyNFOScreenShot { get; set; }
        public string CopyNFOActorPath { get; set; }
        public string CopyNFOScreenShotPath { get; set; }
        public string CopyNFOPreviewPath { get; set; }

        public bool FetchVID { get; set; }
        public bool LoadDataAfterScan { get; set; }
        public bool DataExistsIndexAfterScan { get; set; }
        public bool ImageExistsIndexAfterScan { get; set; }
        public string NFOParseConfig { get; set; }
        public bool _ScanNfo;
        public bool ScanNfo {
            get { return _ScanNfo; }
            set {
                _ScanNfo = value;
                RaisePropertyChanged();
            }
        }
    }
}
