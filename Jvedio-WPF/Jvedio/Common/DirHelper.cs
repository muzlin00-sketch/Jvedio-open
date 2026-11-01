using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Jvedio.Common
{
    /// <summary>
    /// 目录操作的容错封装。取代 <c>SuperUtils.IO.DirHelper</c>。
    /// <para>
    /// 与 <see cref="FileHelper"/> 同样的约定：异常不外抛，失败通过返回值或回调告知。
    /// </para>
    /// </summary>
    public static class DirHelper
    {
        /// <summary>拼接路径，任一段为空时退化为另一段。</summary>
        public static string CombinePath(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1))
                return path2;
            if (string.IsNullOrEmpty(path2))
                return path1;
            return Path.Combine(path1, path2);
        }

        /// <summary>创建目录（含中间层级）；已存在也算成功，失败交给回调。</summary>
        public static bool TryCreateDirectory(string dir, Action<Exception> errorCallBack = null)
        {
            try {
                if (string.IsNullOrEmpty(dir))
                    return false;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return true;
            } catch (Exception ex) {
                errorCallBack?.Invoke(ex);
                return false;
            }
        }

        /// <summary>删除目录；不存在视为成功。</summary>
        public static bool TryDelete(string dir, bool recursive = true)
        {
            try {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return true;
                Directory.Delete(dir, recursive);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>列出直接子目录；失败返回空数组。</summary>
        public static string[] TryGetDirList(string dir)
        {
            try {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return new string[0];
                return Directory.GetDirectories(dir);
            } catch (Exception) {
                return new string[0];
            }
        }

        /// <summary>
        /// 递归枚举文件。逐目录 <c>try/catch</c>：某个子目录无权限时只跳过它，
        /// 不会让整趟枚举中断（<c>SearchOption.AllDirectories</c> 做不到这点）。
        /// </summary>
        /// <param name="rootFolderPath">起始目录。</param>
        /// <param name="fileSearchPattern">文件通配符，默认 <c>*.*</c>。</param>
        /// <param name="callBack">遇到异常时的回调，异常不向上抛。</param>
        /// <param name="scanDir">每进入一个目录时的回调，可用于上报进度。</param>
        /// <param name="cts">取消令牌，扫描中途可停。</param>
        public static IEnumerable<string> GetFileList(
            string rootFolderPath,
            string fileSearchPattern = "*.*",
            Action<Exception> callBack = null,
            Action<string> scanDir = null,
            CancellationTokenSource cts = null)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(rootFolderPath) || !Directory.Exists(rootFolderPath))
                return result;

            string pattern = string.IsNullOrEmpty(fileSearchPattern) ? "*.*" : fileSearchPattern;
            Stack<string> pending = new Stack<string>();
            pending.Push(rootFolderPath);

            while (pending.Count > 0) {
                if (cts != null && cts.IsCancellationRequested)
                    break;

                string current = pending.Pop();
                try {
                    scanDir?.Invoke(current);

                    foreach (string file in Directory.GetFiles(current, pattern))
                        result.Add(file);

                    foreach (string sub in Directory.GetDirectories(current)) {
                        try {
                            // 跳过 junction / 符号链接，避免成环与重复收集
                            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                                continue;
                        } catch (Exception ex) {
                            callBack?.Invoke(ex);
                            continue;
                        }
                        pending.Push(sub);
                    }
                } catch (Exception ex) {
                    callBack?.Invoke(ex);
                }
            }
            return result;
        }

        /// <summary>递归复制目录；失败交给回调，成功项交给 <paramref name="doWork"/>。</summary>
        public static bool TryCopy(
            string sourcePath,
            string targetPath,
            Action<Exception> errorBack = null,
            Action<string> doWork = null)
        {
            try {
                if (!Directory.Exists(sourcePath))
                    return false;
                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);

                foreach (string file in Directory.GetFiles(sourcePath)) {
                    try {
                        string target = Path.Combine(targetPath, Path.GetFileName(file));
                        File.Copy(file, target, true);
                        doWork?.Invoke(target);
                    } catch (Exception ex) {
                        errorBack?.Invoke(ex);
                    }
                }

                foreach (string sub in Directory.GetDirectories(sourcePath)) {
                    string target = Path.Combine(targetPath, Path.GetFileName(sub));
                    if (!TryCopy(sub, target, errorBack, doWork))
                        return false;
                }
                return true;
            } catch (Exception ex) {
                errorBack?.Invoke(ex);
                return false;
            }
        }

        /// <summary>
        /// 移动目录。<paramref name="sub"/> 为真时尝试合并：目标已存在则把源内容并入，
        /// 否则直接整体移动。
        /// </summary>
        public static bool TryMoveDir(string source, string target, bool sub = false)
        {
            try {
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                    return false;
                if (!Directory.Exists(source))
                    return false;

                if (!sub || !Directory.Exists(target)) {
                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        Directory.CreateDirectory(parent);
                    if (Directory.Exists(target))
                        Directory.Delete(target, true);
                    Directory.Move(source, target);
                    return true;
                }

                // 合并模式
                foreach (string file in Directory.GetFiles(source)) {
                    string dest = Path.Combine(target, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                    File.Delete(file);
                }
                foreach (string dir in Directory.GetDirectories(source)) {
                    string dest = Path.Combine(target, Path.GetFileName(dir));
                    TryMoveDir(dir, dest, true);
                }
                Directory.Delete(source, true);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>递归统计目录占用字节数；读不到的项跳过。</summary>
        public static long getDirSize(DirectoryInfo d)
        {
            if (d == null || !d.Exists)
                return 0;

            long size = 0;
            try {
                foreach (FileInfo file in d.GetFiles())
                    size += file.Length;
            } catch (Exception) {
                // 单个目录读不到就跳过
            }

            try {
                foreach (DirectoryInfo sub in d.GetDirectories()) {
                    try {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                    } catch (Exception) {
                        continue;
                    }
                    size += getDirSize(sub);
                }
            } catch (Exception) {
                // 同上
            }
            return size;
        }
    }
}
