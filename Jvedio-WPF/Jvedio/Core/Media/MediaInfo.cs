using System;
using System.Runtime.InteropServices;

namespace Jvedio.Core.Media
{
    /// <summary>媒体流类型（对应原生 MediaInfo 的 stream_kind）。</summary>
    public enum StreamKind
    {
        General = 0,
        Video = 1,
        Audio = 2,
        Text = 3,
        Chapters = 4,
        Image = 5,
    }

    /// <summary>取值种类（对应原生 MediaInfo 的 info_kind）。</summary>
    public enum InfoKind
    {
        Name = 0,
        Text = 1,
        Measure = 2,
        Options = 3,
        NameText = 4,
        MeasureText = 5,
        Info = 6,
        HowTo = 7,
    }

    /// <summary>
    /// MediaInfo 原生库的托管封装，取代 <c>SuperUtils.Media.MediaInfo</c>。
    /// <para>
    /// 原生 <c>MediaInfo.dll</c>（x86）仍在输出目录，本项目只接管托管侧封装：
    /// 典型用法是 <c>using (var mi = new MediaInfo()) { mi.Open(path); mi.Get(...); }</c>。
    /// </para>
    /// <para>
    /// 保留 <c>MustUseAnsi</c> 分支：项目历史上在某些系统上必须走 ANSI 入口
    /// （<c>MediaInfoA_*</c>），行为与原来的封装保持一致。
    /// </para>
    /// </summary>
    public class MediaInfo : IDisposable
    {
        private IntPtr _handle;

        private bool _mustUseAnsi;

        public IntPtr Handle {
            get { return _handle; }
        }

        public bool MustUseAnsi {
            get { return _mustUseAnsi; }
            set { _mustUseAnsi = value; }
        }

        public MediaInfo()
        {
            _handle = MediaInfo_New();
        }

        ~MediaInfo()
        {
            try {
                Close();
                MediaInfo_Delete(_handle);
            } catch (Exception) {
                // 析构阶段不抛
            }
        }

        public void Dispose()
        {
            Close();
            MediaInfo_Delete(_handle);
            GC.SuppressFinalize(this);
        }

        public int Open(string fileName)
        {
            if (_mustUseAnsi) {
                IntPtr ptr = Marshal.StringToHGlobalAnsi(fileName);
                int result = (int)MediaInfoA_Open(_handle, ptr);
                Marshal.FreeHGlobal(ptr);
                return result;
            }

            return (int)MediaInfo_Open(_handle, fileName);
        }

        public void Close()
        {
            MediaInfo_Close(_handle);
        }

        public string Inform()
        {
            return _mustUseAnsi
                ? Marshal.PtrToStringAnsi(MediaInfoA_Inform(_handle, IntPtr.Zero))
                : Marshal.PtrToStringUni(MediaInfo_Inform(_handle, IntPtr.Zero));
        }

        public string Get(StreamKind streamKind, int streamNumber, string parameter, InfoKind kindOfInfo, InfoKind kindOfSearch)
        {
            if (_mustUseAnsi) {
                IntPtr ptr = Marshal.StringToHGlobalAnsi(parameter);
                string result = Marshal.PtrToStringAnsi(MediaInfoA_Get(
                    _handle,
                    (IntPtr)streamKind,
                    (IntPtr)streamNumber,
                    ptr,
                    (IntPtr)kindOfInfo,
                    (IntPtr)kindOfSearch));
                Marshal.FreeHGlobal(ptr);
                return result;
            }

            return Marshal.PtrToStringUni(MediaInfo_Get(
                _handle,
                (IntPtr)streamKind,
                (IntPtr)streamNumber,
                parameter,
                (IntPtr)kindOfInfo,
                (IntPtr)kindOfSearch));
        }

        public string Get(StreamKind streamKind, int streamNumber, string parameter, InfoKind kindOfInfo)
        {
            return Get(streamKind, streamNumber, parameter, kindOfInfo, InfoKind.Name);
        }

        public string Get(StreamKind streamKind, int streamNumber, string parameter)
        {
            return Get(streamKind, streamNumber, parameter, InfoKind.Text, InfoKind.Name);
        }

        public string Option(string option, string value)
        {
            if (_mustUseAnsi) {
                IntPtr optionPtr = Marshal.StringToHGlobalAnsi(option);
                IntPtr valuePtr = Marshal.StringToHGlobalAnsi(value);
                string result = Marshal.PtrToStringAnsi(MediaInfoA_Option(_handle, optionPtr, valuePtr));
                Marshal.FreeHGlobal(optionPtr);
                Marshal.FreeHGlobal(valuePtr);
                return result;
            }

            return Marshal.PtrToStringUni(MediaInfo_Option(_handle, option, value));
        }

        public int State_Get()
        {
            return (int)MediaInfo_State_Get(_handle);
        }

        public int Count_Get(StreamKind streamKind, int streamNumber)
        {
            return (int)MediaInfo_Count_Get(_handle, (IntPtr)streamKind, (IntPtr)streamNumber);
        }

        public int Count_Get(StreamKind streamKind)
        {
            return Count_Get(streamKind, -1);
        }

        [DllImport("MediaInfo.dll")]
        private static extern IntPtr MediaInfo_New();

        [DllImport("MediaInfo.dll")]
        private static extern void MediaInfo_Delete(IntPtr handle);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr MediaInfo_Open(IntPtr handle, string fileName);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr MediaInfoA_Open(IntPtr handle, IntPtr fileName);

        [DllImport("MediaInfo.dll")]
        private static extern IntPtr MediaInfo_Close(IntPtr handle);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr MediaInfo_Inform(IntPtr handle, IntPtr reserved);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr MediaInfoA_Inform(IntPtr handle, IntPtr reserved);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr MediaInfo_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber, string parameter, IntPtr kindOfInfo, IntPtr kindOfSearch);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr MediaInfoA_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber, IntPtr parameter, IntPtr kindOfInfo, IntPtr kindOfSearch);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr MediaInfo_Option(IntPtr handle, string option, string value);

        [DllImport("MediaInfo.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr MediaInfoA_Option(IntPtr handle, IntPtr option, IntPtr value);

        [DllImport("MediaInfo.dll")]
        private static extern IntPtr MediaInfo_State_Get(IntPtr handle);

        [DllImport("MediaInfo.dll")]
        private static extern IntPtr MediaInfo_Count_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber);
    }
}
