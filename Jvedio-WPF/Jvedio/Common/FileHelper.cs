using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace Jvedio.Common
{
    /// <summary>
    /// 文件操作的容错封装。取代 <c>SuperUtils.IO.FileHelper</c>。
    /// <para>
    /// 这里的每个方法都遵循同一个约定：<b>不把异常抛给调用方</b>，失败时返回一个
    /// 可继续使用的值（<c>false</c>、<c>0</c>、空串、<c>null</c>）。因为调用点大多是
    /// 扫描、刮削、拖拽这类"单个文件失败不该中断整批"的场景。
    /// </para>
    /// <para>
    /// 两个方法名有历史误导，已按实测行为实现，不要按名字想当然：
    /// <see cref="TryGetFullName"/> 返回的是<b>所在目录</b>而不是文件名；
    /// <see cref="IsFile"/> 对<b>不存在的路径返回 true</b>。
    /// </para>
    /// </summary>
    public static class FileHelper
    {
        /// <summary>取绝对路径并消掉 <c>..</c>；失败时原样返回入参。</summary>
        public static string TryGetFullPath(string path)
        {
            try {
                return Path.GetFullPath(path);
            } catch (Exception) {
                return path;
            }
        }

        /// <summary>
        /// 取该路径<b>所在的目录</b>（不是文件名）。
        /// <para>对目录入参返回其父目录，因此不能用来"把路径换成文件名"。</para>
        /// </summary>
        public static string TryGetFullName(string path)
        {
            try {
                return Path.GetDirectoryName(Path.GetFullPath(path));
            } catch (Exception) {
                return path;
            }
        }

        /// <summary>
        /// 判断路径是否指向文件。只要不是已存在的目录就返回 <c>true</c>，
        /// 其中包括<b>不存在的路径</b>——调用方用它区分"拖进来的是文件还是文件夹"，
        /// 那个场景下把未知路径当作文件才是期望行为。
        /// </summary>
        public static bool IsFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            if (Directory.Exists(path))
                return false;
            if (File.Exists(path))
                return true;

            // 不存在的路径按文件处理，与原实现一致
            return true;
        }

        /// <summary>
        /// 枚举文件，异常一律吞掉。当 <paramref name="pattern"/> 为空时返回
        /// <c>null</c>（原实现如此，调用方需要判空）。
        /// <para>
        /// <paramref name="option"/> 默认只枚举当前目录：现有调用点都是"列出这一个文件夹里的文件"，
        /// 需要递归的会自己带参数或改用 <see cref="DirHelper.GetFileList"/>。
        /// </para>
        /// </summary>
        public static string[] TryGetAllFiles(string path, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            try {
                if (!Directory.Exists(path))
                    return new string[0];
                return Directory.GetFiles(path, pattern, option);
            } catch (Exception) {
                return new string[0];
            }
        }

        /// <summary>
        /// 与 <see cref="TryGetAllFiles"/> 等价。两个方法在原库里行为一致，
        /// 保留双入口只是为了让调用点不用改名。
        /// </summary>
        public static string[] TryScanDIr(string path, string searchPattern, SearchOption searchOption)
        {
            return TryGetAllFiles(path, searchPattern, searchOption);
        }

        /// <summary>
        /// 给定不含扩展名的路径，逐个尝试候选扩展名，返回第一个存在的文件；都不存在返回 <c>null</c>。
        /// <para>用于"数据库里存的是 <c>a/b</c>，实际文件是 <c>a/b.jpg</c>"这类图片路径。</para>
        /// </summary>
        public static string FindWithExt(string path, List<string> exts)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try {
                if (File.Exists(path))
                    return path;
                if (exts == null)
                    return null;

                foreach (string ext in exts) {
                    if (string.IsNullOrEmpty(ext))
                        continue;
                    string candidate = path + ext;
                    if (File.Exists(candidate))
                        return candidate;
                }
            } catch (Exception) {
                // 落到下面的 null
            }
            return null;
        }

        /// <summary>文件字节数；不存在或读不到返回 0。</summary>
        public static long TryGetFileLength(string path)
        {
            try {
                FileInfo info = new FileInfo(path);
                return info.Exists ? info.Length : 0;
            } catch (Exception) {
                return 0;
            }
        }

        /// <summary>读出整个文件的文本；失败返回空串。</summary>
        public static string TryReadFile(string path)
        {
            try {
                return File.ReadAllText(path);
            } catch (Exception) {
                return string.Empty;
            }
        }

        /// <summary>写整个文件，覆盖已有内容；失败返回 <c>false</c>。</summary>
        public static bool TryWriteToFile(string filePath, string content, Encoding encoding)
        {
            try {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, content, encoding ?? Encoding.UTF8);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>追加到文件末尾，不存在则创建；失败返回 <c>false</c>。</summary>
        public static bool TryAppendToFile(string filePath, string content, Encoding encoding)
        {
            try {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(filePath, content, encoding ?? Encoding.UTF8);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>把字节数组落盘；失败返回 <c>false</c>。</summary>
        public static bool ByteArrayToFile(byte[] byteArray, string fileName)
        {
            if (byteArray == null || string.IsNullOrEmpty(fileName))
                return false;

            try {
                string dir = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(fileName, byteArray);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>复制单个文件；失败返回 <c>false</c>。</summary>
        public static bool TryCopyFile(string src, string target, bool overwrite = false)
        {
            try {
                if (!File.Exists(src))
                    return false;
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(src, target, overwrite);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>移动文件（跨卷由系统复制后删除）；失败返回 <c>false</c>。</summary>
        public static bool TryMoveFile(string originPath, string targetPath)
        {
            try {
                if (!File.Exists(originPath))
                    return false;
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Move(originPath, targetPath);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>创建目录（含中间层级）；已存在也算成功。</summary>
        public static bool TryCreateDir(string path)
        {
            try {
                if (Directory.Exists(path))
                    return true;
                Directory.CreateDirectory(path);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>删除目录及其内容；失败返回 <c>false</c>。</summary>
        public static bool TryDeleteDir(string path)
        {
            try {
                if (!Directory.Exists(path))
                    return true;
                Directory.Delete(path, true);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>删除文件；失败时把异常交给 <paramref name="errorCallBack"/>（可为 null）。</summary>
        public static bool TryDeleteFile(string path, Action<Exception> errorCallBack = null)
        {
            try {
                if (!File.Exists(path))
                    return true;
                File.Delete(path);
                return true;
            } catch (Exception ex) {
                errorCallBack?.Invoke(ex);
                return false;
            }
        }

        /// <summary>
        /// 移到回收站（可撤销）。<paramref name="delay"/> 是操作前的等待毫秒数，
        /// 用于让刚关闭的文件句柄释放；现有调用点都传 0。
        /// </summary>
        public static bool TryMoveToRecycleBin(string path, int delay = 0)
        {
            try {
                if (string.IsNullOrEmpty(path))
                    return false;
                if (delay > 0)
                    Thread.Sleep(delay);

                if (Directory.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else
                    return false;

                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>用资源管理器打开所在目录并选中该文件。</summary>
        public static void TryOpenSelectPath(string filePath)
        {
            try {
                if (string.IsNullOrEmpty(filePath))
                    return;

                string full = TryGetFullPath(filePath);
                if (File.Exists(full) || Directory.Exists(full)) {
                    Process.Start("explorer.exe", "/select,\"" + full + "\"");
                } else {
                    // 文件不存在时退化为打开所在目录，避免用户什么都看不到
                    string dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", "\"" + dir + "\"");
                }
            } catch (Exception) {
                // 打开失败不应影响调用方
            }
        }

        /// <summary>用资源管理器打开目录。</summary>
        public static bool TryOpenPath(string dir)
        {
            try {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return false;
                Process.Start("explorer.exe", "\"" + dir + "\"");
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>用系统默认程序打开文件。</summary>
        public static bool TryOpenFile(string filename)
        {
            return TryOpenByDefaultApp(filename);
        }

        /// <summary>用指定程序打开文件。</summary>
        public static bool TryOpenFile(string processPath, string filename)
        {
            try {
                if (string.IsNullOrEmpty(processPath) || string.IsNullOrEmpty(filename))
                    return false;
                Process.Start(processPath, "\"" + filename + "\"");
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>用系统默认程序打开文件或目录。</summary>
        public static bool TryOpenByDefaultApp(string path)
        {
            try {
                if (string.IsNullOrEmpty(path))
                    return false;
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>用默认浏览器打开网址；失败时把异常交给回调。</summary>
        public static bool TryOpenUrl(string url, Action<Exception> callBack = null)
        {
            try {
                if (string.IsNullOrEmpty(url))
                    return false;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            } catch (Exception ex) {
                callBack?.Invoke(ex);
                return false;
            }
        }

        /// <summary>
        /// 弹出文件夹选择对话框，返回选中的目录；取消返回 <c>null</c>。
        /// </summary>
        public static string SelectPath(Window window = null, string title = null, string initialDirectory = null)
        {
            try {
                using (var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog()) {
                    dialog.IsFolderPicker = true;
                    dialog.Title = string.IsNullOrEmpty(title) ? "选择文件夹" : title;
                    if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                        dialog.InitialDirectory = initialDirectory;

                    var result = window == null ? dialog.ShowDialog() : dialog.ShowDialog(window);
                    return result == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok
                        ? dialog.FileName
                        : null;
                }
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>
        /// 把字节数渲染成 <c>1.5 MB</c> 这样的可读大小，取代 <c>SuperUtils.IO.FileHelper.ToProperFileSize</c>。
        /// <para>
        /// 按原实现逐值对拍确定：
        /// 以 1024 为步长逐级换算（<c>1048575</c> 显示 <c>1024 KB</c> 而不是 <c>1 MB</c>）；
        /// 保留 1 位小数并去掉无意义的 <c>.0</c>；负数按 0 处理。
        /// </para>
        /// </summary>
        public static string ToProperFileSize(this long byteCount)
        {
            if (byteCount < 0)
                return "0 B";

            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            double value = byteCount;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) {
                value /= 1024;
                unit++;
            }

            double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
        }

        /// <summary>
        /// 把非法文件名字符替换成下划线，取代 <c>SuperUtils.IO.FileHelper.ToProperFileName</c>。
        /// <para>null / 空串返回空串。</para>
        /// </summary>
        public static string ToProperFileName(this string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }
    }
}
