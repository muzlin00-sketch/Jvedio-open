// 自建 SuperUtils.dll（装配名 SuperUtils，版本 1.2.0.0，无强名称）。
// 只实现闭源控件库 SuperControls.Style 实际引用的 26 个类型 / 58 个成员，
// 用来顶替输出目录里那份闭源 SuperUtils.dll（简单名绑定，忽略版本号）。
// 校验：_buildtools/probe_meta/refs.exe 枚举的成员必须全部能解析到。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using SuperUtils.NetWork.Entity;
using SuperUtils.NetWork.Enums;

namespace SuperUtils.Framework.Logger
{
    public enum Level { None = 0, Error = 1, Warn = 2, Info = 3, Debug = 4 }

    public interface ILogger
    {
        void Error(string str, int lineNumber = 0, string caller = null);
        void Warn(string str, int lineNumber = 0, string caller = null);
        void Info(string str, int lineNumber = 0, string caller = null);
        void Debug(string str, int lineNumber = 0, string caller = null);
    }

    /// <summary>日志基类：级别过滤 + 交给 <see cref="LogPrint"/> 输出（宿主负责接管）。</summary>
    public abstract class AbstractLogger : ILogger
    {
        /// <summary>级别枚举（原实现是嵌套在 AbstractLogger 里的，调用方按嵌套名引用）。</summary>
        public enum Level { None = 0, Error = 1, Warn = 2, Info = 3, Debug = 4 }

        public Level LogLevel { get; set; } = Level.Info;

        public virtual void LogPrint(string str) { }

        public void Error(string str, int lineNumber = 0, string caller = null)
        {
            if (LogLevel >= Level.Error) LogPrint(Format("E", str, lineNumber, caller));
        }

        public void Warn(string str, int lineNumber = 0, string caller = null)
        {
            if (LogLevel >= Level.Warn) LogPrint(Format("W", str, lineNumber, caller));
        }

        public void Info(string str, int lineNumber = 0, string caller = null)
        {
            if (LogLevel >= Level.Info) LogPrint(Format("I", str, lineNumber, caller));
        }

        public void Debug(string str, int lineNumber = 0, string caller = null)
        {
            if (LogLevel >= Level.Debug) LogPrint(Format("D", str, lineNumber, caller));
        }

        public void Error(Exception e)
        {
            if (e != null && LogLevel >= Level.Error) LogPrint(Format("E", e.ToString(), 0, null));
        }

        private static string Format(string level, string str, int lineNumber, string caller)
        {
            string where = string.IsNullOrEmpty(caller) ? string.Empty : caller + "/" + lineNumber;
            return "[" + level + "] [" + where + "] " + str;
        }
    }
}

namespace SuperUtils.Common
{
    public static class JsonUtils
    {
        public static T TryDeserializeObject<T>(string value)
        {
            try { return JsonConvert.DeserializeObject<T>(value); } catch (Exception) { return default; }
        }

        public static string TrySerializeObject(object obj)
        {
            return TrySerializeObject(obj, Formatting.None);
        }

        public static string TrySerializeObject(object value, Formatting formatting)
        {
            try { return JsonConvert.SerializeObject(value, formatting); } catch (Exception) { return string.Empty; }
        }

        public static string ClasstoJson(System.Reflection.TypeInfo[] types)
        {
            try { return JsonConvert.SerializeObject(types); } catch (Exception) { return string.Empty; }
        }
    }

    public static class DataHelper
    {
        public static bool ContainsKeys<T1, T2>(this Dictionary<T1, T2> dict, params T1[] keys)
        {
            if (dict == null || keys == null) return false;
            foreach (T1 key in keys) {
                if (key == null || !dict.ContainsKey(key)) return false;
            }
            return true;
        }
    }

    public static class KeyBoardHelper
    {
        public static bool IsDigitOrNumberKey(Key key)
        {
            return (key >= Key.D0 && key <= Key.D9) || (key >= Key.NumPad0 && key <= Key.NumPad9);
        }
    }
}

namespace SuperUtils.CustomEventArgs
{
    public class MessageCallBackEventArgs : EventArgs
    {
        public string Message;

        public MessageCallBackEventArgs(string message)
        {
            Message = message;
        }
    }
}

namespace SuperUtils.Framework.MarkDown
{
    /// <summary>极简 MarkDown → FlowDocument：按行切段，空行分段，保留原文。</summary>
    public class MarkDown
    {
        public const int DEFAULT_FONT_SIZE = 14;
        public List<int> FontSize { get; set; } = new List<int>();

        public static FlowDocument Parse(string str)
        {
            FlowDocument document = new FlowDocument();
            if (string.IsNullOrEmpty(str)) return document;

            string[] lines = str.Replace("\r\n", "\n").Split('\n');
            Paragraph paragraph = new Paragraph();
            foreach (string line in lines) {
                if (string.IsNullOrWhiteSpace(line)) {
                    if (paragraph.Inlines.Count > 0) {
                        document.Blocks.Add(paragraph);
                        paragraph = new Paragraph();
                    }
                    continue;
                }
                if (paragraph.Inlines.Count > 0) paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run(line.TrimEnd()));
            }
            if (paragraph.Inlines.Count > 0) document.Blocks.Add(paragraph);
            return document;
        }
    }
}

namespace SuperUtils.Framework.ORM.Utils
{
    public static class SqlStringFormat
    {
        public static string HandleNewLine(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        }

        public static string Format(object obj)
        {
            if (obj == null) return string.Empty;
            return HandleNewLine(obj.ToString()).Replace("'", "''");
        }

        public static string FormatQuote(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return "'" + Format(str) + "'";
        }

        public static string CleanSqlString(string str)
        {
            if (str == null) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(str, @"[\s']", "");
        }

        public static string ToProperSql(string sql, bool toUpper)
        {
            if (sql == null) return string.Empty;
            string result = sql.Replace("'", "");
            return toUpper ? result.ToUpper() : result;
        }
    }
}

namespace SuperUtils.IO
{
    public static class FileHelper
    {
        public static string DEFAULT_CONFIG_JSON_NAME = "config.json";

        // 注意：这四个在原实现里是**属性**（控件库按 get_XXX 调用），写成字段会 MissingMethodException。
        public static string PICTURE_EXTENSIONS { get; } = ".jpg|.jpeg|.png|.bmp|.gif|.webp";
        public static List<string> PICTURE_EXTENSIONS_LIST { get; } = new List<string>() { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
        public static string VIDEO_EXTENSIONS { get; } = ".mp4|.mkv|.avi|.wmv|.mov|.rmvb|.flv|.ts|.m2ts|.iso";
        public static List<string> VIDEO_EXTENSIONS_LIST { get; } = new List<string>() { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".rmvb", ".flv", ".ts", ".m2ts", ".iso" };

        public static bool ByteArrayToFile(byte[] byteArray, string fileName)
        {
            try {
                if (byteArray == null || string.IsNullOrEmpty(fileName)) return false;
                string dir = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(fileName, byteArray);
                return true;
            } catch (Exception) { return false; }
        }

        public static string FindWithExt(string path, List<string> exts)
        {
            try {
                if (string.IsNullOrEmpty(path) || exts == null) return null;
                foreach (string ext in exts) {
                    string candidate = path + ext;
                    if (File.Exists(candidate)) return candidate;
                }
                return null;
            } catch (Exception) { return null; }
        }

        public static bool TryGetAllFiles(string path, string pattern, SearchOption option, out string[] files)
        {
            files = new string[0];
            try {
                if (!Directory.Exists(path)) return false;
                files = Directory.GetFiles(path, string.IsNullOrEmpty(pattern) ? "*" : pattern, option);
                return true;
            } catch (Exception) { return false; }
        }

        public static string[] TryGetAllFiles(string path, string pattern, SearchOption option)
        {
            TryGetAllFiles(path, pattern, option, out string[] files);
            return files;
        }

        public static string TryReadFile(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; } catch (Exception) { return string.Empty; }
        }

        public static void TryWriteToFile(string filePath, string content, Encoding encoding)
        {
            try {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, content, encoding ?? Encoding.UTF8);
            } catch (Exception) { }
        }

        public static bool TryDeleteFile(string path, Action<Exception> errorCallBack = null)
        {
            try {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            } catch (Exception ex) {
                errorCallBack?.Invoke(ex);
                return false;
            }
        }

        public static bool TryOpenPath(string dir)
        {
            try {
                if (!Directory.Exists(dir)) return false;
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                return true;
            } catch (Exception) { return false; }
        }

        public static bool TryOpenFile(string filename)
        {
            try {
                if (!File.Exists(filename)) return false;
                Process.Start(new ProcessStartInfo(filename) { UseShellExecute = true });
                return true;
            } catch (Exception) { return false; }
        }

        public static bool TryOpenUrl(string url, Action<Exception> callBack = null)
        {
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            } catch (Exception ex) {
                callBack?.Invoke(ex);
                return false;
            }
        }
    }

    public static class DirHelper
    {
        public static bool TryCreateDirectory(string dir, Action<Exception> errorCallBack = null)
        {
            try {
                if (Directory.Exists(dir)) return true;
                Directory.CreateDirectory(dir);
                return true;
            } catch (Exception ex) {
                errorCallBack?.Invoke(ex);
                return false;
            }
        }

        public static bool TryDelete(string dir, bool recursive = false)
        {
            try {
                if (!Directory.Exists(dir)) return false;
                Directory.Delete(dir, recursive);
                return true;
            } catch (Exception) { return false; }
        }
    }

    public static class ClipBoard
    {
        public static bool TrySetDataObject(object o)
        {
            try {
                Clipboard.SetDataObject(o, true);
                return true;
            } catch (Exception) { return false; }
        }
    }
}

namespace SuperUtils.Media
{
    public static class ImageHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DestroyIcon(IntPtr handle);

        public static BitmapImage BitmapImageFromFile(string filepath, int DecodePixelWidth = 0)
        {
            try {
                if (!File.Exists(filepath)) return null;
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (DecodePixelWidth > 0) image.DecodePixelWidth = DecodePixelWidth;
                image.UriSource = new Uri(filepath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            } catch (Exception) { return null; }
        }

        public static BitmapImage ImageFromUri(string uri, double DecodePixelWidth = 0)
        {
            return ImageFromUri(uri, UriKind.RelativeOrAbsolute, DecodePixelWidth);
        }

        public static BitmapImage ImageFromUri(string uri, UriKind uriKind, double DecodePixelWidth = 0)
        {
            try {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (DecodePixelWidth > 0) image.DecodePixelWidth = (int)DecodePixelWidth;
                image.UriSource = new Uri(uri, uriKind);
                image.EndInit();
                image.Freeze();
                return image;
            } catch (Exception) { return null; }
        }

        public static Bitmap ImageSourceToBitmap(ImageSource imageSource)
        {
            try {
                BitmapSource source = imageSource as BitmapSource;
                if (source == null) return null;
                using (MemoryStream stream = new MemoryStream()) {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                    stream.Position = 0;
                    return new Bitmap(stream);
                }
            } catch (Exception) { return null; }
        }

        public static Icon BitmapToIcon(Bitmap bmp, ref IntPtr Hicon)
        {
            try {
                if (bmp == null) return null;
                Hicon = bmp.GetHicon();
                return Icon.FromHandle(Hicon);
            } catch (Exception) { return null; }
        }
    }
}

namespace SuperUtils.Reflections
{
    public static class ReflectionHelper
    {
        public static object TryCreateInstance(Type type, object[] args)
        {
            try { return type == null ? null : Activator.CreateInstance(type, args); } catch (Exception) { return null; }
        }

        public static object TryCreateInstance(Type type, int ctorIndex, object[] args)
        {
            try {
                if (type == null) return null;
                var ctors = type.GetConstructors();
                if (ctorIndex < 0 || ctorIndex >= ctors.Length) return null;
                return ctors[ctorIndex].Invoke(args);
            } catch (Exception) { return null; }
        }

        public static System.Reflection.Assembly TryLoadAssembly(string path)
        {
            try { return File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null; } catch (Exception) { return null; }
        }
    }
}

namespace SuperUtils.Security
{
    public enum SHAType { SHA1, SHA256, SHA384, SHA512, MD5 }

    public static class Encrypt
    {
        public static string TryGetFileMD5(string filename)
        {
            try {
                if (!File.Exists(filename)) return string.Empty;
                using (MD5 md5 = MD5.Create())
                using (FileStream stream = File.OpenRead(filename)) {
                    byte[] hash = md5.ComputeHash(stream);
                    StringBuilder builder = new StringBuilder();
                    foreach (byte b in hash) builder.Append(b.ToString("x2"));
                    return builder.ToString();
                }
            } catch (Exception) { return string.Empty; }
        }
    }
}

namespace SuperUtils.Systems
{
    public static class Win32Helper
    {
        public const int WM_COPYDATA = 0x004A;

        /// <summary>WM_COPYDATA 载荷。原实现是 Win32Helper 的嵌套结构，控件库按嵌套名引用。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CopyDataStruct
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref CopyDataStruct lParam);

        public static void SendArgs(IntPtr targetHWnd, string args)
        {
            if (string.IsNullOrEmpty(args)) return;
            CopyDataStruct data = new CopyDataStruct();
            data.lpData = Marshal.StringToHGlobalUni(args);
            data.cbData = (args.Length + 1) * 2;
            try {
                SendMessage(targetHWnd, WM_COPYDATA, IntPtr.Zero, ref data);
            } catch (Exception) { } finally {
                Marshal.FreeHGlobal(data.lpData);
            }
        }

        public static double GetCpuUsage(Process process)
        {
            try {
                if (process == null) return 0;
                return process.TotalProcessorTime.TotalMilliseconds;
            } catch (Exception) { return 0; }
        }
    }
}

namespace SuperUtils.NetWork.Enums
{
    public enum HttpMode { String = 0, Header = 1, Stream = 2 }
    public enum ProxyMode { None = 0, System = 1, Custom = 2 }
    public enum ProxyProtocol { HTTP = 0, SOCKS = 1 }
}

namespace SuperUtils.NetWork.Entity
{
    public class HttpResult
    {
        public const string DEFAULT_ERROR_MSG = "HTTP NOT OK";

        public bool Success { get; set; }
        public string Error { get; set; } = DEFAULT_ERROR_MSG;
        public string SourceCode { get; set; } = string.Empty;
        public byte[] FileByte { get; set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.Forbidden;
        public System.Net.Http.Headers.HttpResponseHeaders Headers { get; set; }
    }

    public class RequestHeader
    {
        public const double DEFAULT_TIMEOUT = 10000;

        public HttpMethod Method { get; set; } = HttpMethod.Get;
        public bool AllowAutoRedirect { get; set; } = true;
        public double TimeOut { get; set; } = DEFAULT_TIMEOUT;
        public HttpContent RequestBody { get; set; }
        public IWebProxy WebProxy { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public override string ToString()
        {
            return "Method: " + Method + " TimeOut: " + TimeOut + " Headers: " + Headers?.Count;
        }
    }
}

namespace SuperUtils.NetWork.Crawler
{
    public class CrawlerHeader
    {
        public static RequestHeader Default { get; } = new RequestHeader() {
            Headers = new Dictionary<string, string>() {
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36" },
            },
        };
    }
}

namespace SuperUtils.NetWork
{
    public static class NetUtils
    {
        public static bool IsProperUrl(string source)
        {
            return Uri.TryCreate(source, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public static Uri TryGetUri(string url)
        {
            try { return new Uri(url); } catch (Exception) { return null; }
        }

        public static Uri TryGetUri(string baseurl, string url)
        {
            try { return new Uri(new Uri(baseurl), url); } catch (Exception) { return null; }
        }

        public static string ToProperUrl(string url, bool appendSplit = false)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string result = url.Trim();
            if (appendSplit && !result.EndsWith("/")) result += "/";
            return result;
        }

        public static string GetLocalIPAddress() { return string.Empty; }
        public static bool IsPortInUse(int port) { return false; }
    }

    public class SuperWebProxy
    {
        public static IWebProxy SystemWebProxy { get; } = WebRequest.GetSystemWebProxy();

        public ProxyMode ProxyMode { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string UserName { get; set; }
        public string Pwd { get; set; }
        public ProxyProtocol ProxyProtocol { get; set; }
        public ICredentials Credentials { get; set; }

        public IWebProxy GetWebProxy()
        {
            try {
                switch (ProxyMode) {
                    case ProxyMode.System: return SystemWebProxy;
                    case ProxyMode.Custom:
                        WebProxy proxy = new WebProxy(Server, Port);
                        if (!string.IsNullOrEmpty(UserName))
                            proxy.Credentials = new NetworkCredential(UserName, Pwd);
                        return proxy;
                    default: return null;
                }
            } catch (Exception) { return null; }
        }
    }

    public static class HttpClient
    {
        public static Task<HttpResult> Get(string Url, RequestHeader header, HttpMode httpMode = HttpMode.String)
        {
            return Send(Url, header, httpMode, false, 0);
        }

        public static Task<HttpResult> Get(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal, int timeOut)
        {
            return Send(Url, header, httpMode, ignoreCertVal, timeOut);
        }

        public static Task<HttpResult> Post(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal = false)
        {
            if (header != null) header.Method = HttpMethod.Post;
            return Send(Url, header, httpMode, ignoreCertVal, 0);
        }

        public static Task<HttpResult> Post(string Url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal, int timeOut)
        {
            if (header != null) header.Method = HttpMethod.Post;
            return Send(Url, header, httpMode, ignoreCertVal, timeOut);
        }

        private static async Task<HttpResult> Send(string url, RequestHeader header, HttpMode httpMode, bool ignoreCertVal, int timeOut)
        {
            HttpResult result = new HttpResult();
            try {
                HttpClientHandler handler = new HttpClientHandler() { AllowAutoRedirect = header?.AllowAutoRedirect ?? true };
                if (ignoreCertVal) {
                    handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
                }
                if (header?.WebProxy != null) handler.Proxy = header.WebProxy;

                using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient(handler)) {
                    if (timeOut > 0) client.Timeout = TimeSpan.FromMilliseconds(timeOut);
                    else if (header != null && header.TimeOut > 0) client.Timeout = TimeSpan.FromMilliseconds(header.TimeOut);

                    HttpRequestMessage request = new HttpRequestMessage(header?.Method ?? HttpMethod.Get, url);
                    if (header?.RequestBody != null) request.Content = header.RequestBody;
                    if (header?.Headers != null) {
                        foreach (KeyValuePair<string, string> kv in header.Headers) {
                            if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                                request.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }
                    }

                    HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                    result.StatusCode = response.StatusCode;
                    result.Headers = response.Headers;
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    result.FileByte = bytes;
                    if (httpMode == HttpMode.String || httpMode == HttpMode.Header)
                        result.SourceCode = Encoding.UTF8.GetString(bytes);
                }
            } catch (Exception ex) {
                result.Error = ex.Message;
            }
            return result;
        }
    }

    public static class HttpHelper
    {
        public static void SetCertificatePolicy()
        {
            ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;
        }

        public static async Task<HttpResult> AsyncDownLoadFile(string Url, RequestHeader header)
        {
            HttpResult result = await HttpClient.Get(Url, header, HttpMode.Stream).ConfigureAwait(false);
            if (result.StatusCode != HttpStatusCode.OK)
                throw new WebException("下载失败: " + result.StatusCode);
            return result;
        }
    }
}

namespace SuperUtils.WPF.Entity
{
    public class ObservableString : INotifyPropertyChanged
    {
        private string _value;

        public ObservableString(string value) { _value = value; }

        public string Value
        {
            get { return _value; }
            set {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString() { return _value; }
        public override bool Equals(object obj) { return obj is ObservableString other && other._value == _value; }
        public override int GetHashCode() { return _value == null ? 0 : _value.GetHashCode(); }
    }
}

namespace SuperUtils.WPF.VieModel
{
    /// <summary>命令包装（控件库用 <c>RelayCommand&lt;T&gt;</c> 绑定按钮）。</summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute) : this(execute, null) { }

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(Cast(parameter));
        }

        public void Execute(object parameter)
        {
            _execute?.Invoke(Cast(parameter));
        }

        private static T Cast(object parameter)
        {
            if (parameter is T typed) return typed;
            return default;
        }
    }

    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void Init() { }

        public void RaisePropertyChanged(string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public static class DataExtension
    {
        public static void TryRemoveFirst<TSource>(this Collection<TSource> source, Func<TSource, bool> predicate)
        {
            if (source == null || predicate == null) return;
            for (int i = 0; i < source.Count; i++) {
                if (predicate(source[i])) {
                    source.RemoveAt(i);
                    return;
                }
            }
        }

        public static int FindIndex<TSource>(this Collection<TSource> source, Func<TSource, bool> predicate)
        {
            if (source == null || predicate == null) return -1;
            for (int i = 0; i < source.Count; i++) {
                if (predicate(source[i])) return i;
            }
            return -1;
        }
    }

    public class DataLoader
    {
        public static DispatcherOperation InvokeLoadData<T>(ICollection<T> collections, T data, DispatcherPriority priority = DispatcherPriority.Background)
        {
            return Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => {
                if (collections != null && data != null) collections.Add(data);
            }), priority);
        }
    }
}

namespace SuperUtils.WPF.VisualTools
{
    public static class VisualHelper
    {
        public const string ZH_CN = "zh-CN";
        public const string EN_US = "en-US";
        public static readonly Dictionary<string, System.Windows.Media.FontFamily> SYSTEM_FONT_FAMILIES = new Dictionary<string, System.Windows.Media.FontFamily>();

        public static T FindElementByName<T>(FrameworkElement element, string childName) where T : FrameworkElement
        {
            if (element == null || string.IsNullOrEmpty(childName)) return null;
            try {
                DependencyObject child = element.FindName(childName) as DependencyObject;
                if (child is T typed) return typed;
                return FindVisualChildByName<T>(element, childName);
            } catch (Exception) { return null; }
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && typed.Name == name) return typed;
                T result = FindVisualChildByName<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        public static System.Windows.Media.Brush HexStringToBrush(string hexString)
        {
            try {
                if (string.IsNullOrEmpty(hexString)) return null;
                object converted = System.Windows.Media.ColorConverter.ConvertFromString(hexString);
                return converted is System.Windows.Media.Color color ? new SolidColorBrush(color) : null;
            } catch (Exception) { return null; }
        }

        public static SolidColorBrush RGBAToBrush(string[] args)
        {
            try {
                if (args == null || args.Length < 3) return null;
                byte r = byte.Parse(args[0]);
                byte g = byte.Parse(args[1]);
                byte b = byte.Parse(args[2]);
                byte a = args.Length > 3 ? byte.Parse(args[3]) : (byte)255;
                return new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            } catch (Exception) { return null; }
        }

        public static void SetElementFocus(FrameworkElement element)
        {
            try { element?.Focus(); } catch (Exception) { }
        }

        public static DependencyObject FindChild(DependencyObject parent, string name)
        {
            return FindElementByName<FrameworkElement>(parent as FrameworkElement, name);
        }

        public static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                T result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T typed) yield return typed;
                foreach (T grandChild in FindVisualChildren<T>(child)) yield return grandChild;
            }
        }
    }
}
