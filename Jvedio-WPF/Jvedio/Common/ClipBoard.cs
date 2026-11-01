using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace Jvedio.Common
{
    /// <summary>
    /// 剪贴板操作的容错封装。取代 <c>SuperUtils.IO.ClipBoard</c>。
    /// <para>
    /// 注意类名是 <c>ClipBoard</c>（大写 B），与 WPF 的 <c>System.Windows.Clipboard</c>
    /// 仅差一个字母大小写。C# 区分大小写所以不冲突，但读代码时容易看错——
    /// 本类所有成员都是"带 try/catch 的版本"，直接用 WPF 那个才是没有保护的。
    /// </para>
    /// <para>
    /// 剪贴板是全局独占资源，被其他程序（剪贴板管理器、截图工具）占用时
    /// <c>SetDataObject</c> 会抛 <c>COMException</c>，因此这里一律吞掉并返回 <c>false</c>。
    /// </para>
    /// </summary>
    public static class ClipBoard
    {
        /// <summary>
        /// 放入任意数据对象，<c>copy: true</c> 让数据在程序退出后仍留在剪贴板。
        /// </summary>
        public static bool TrySetDataObject(object o)
        {
            try {
                if (o == null)
                    return false;
                Clipboard.SetDataObject(o, true);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>取剪贴板文本；失败返回空串。</summary>
        public static string GetText()
        {
            try {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            } catch (Exception) {
                return string.Empty;
            }
        }

        /// <summary>
        /// 放入文件列表（资源管理器里粘贴＝复制）。
        /// </summary>
        public static bool TrySetFileDropList(StringCollection filePaths, Action<string> callBack = null)
        {
            return SetDropList(filePaths, DragDropEffects.Copy, callBack);
        }

        /// <summary>
        /// 放入文件列表并标记为"剪切"（资源管理器里粘贴＝移动）。
        /// </summary>
        public static bool TryCutFileDropList(StringCollection filePaths, Action<string> callBack = null)
        {
            return SetDropList(filePaths, DragDropEffects.Move, callBack);
        }

        /// <summary>
        /// 写入文件列表 + <c>Preferred DropEffect</c>。
        /// <para>
        /// 后者是一个 4 字节小端整数，资源管理器读它来决定粘贴是复制还是移动；
        /// 缺了它就只能复制，所以剪切必须写。
        /// </para>
        /// </summary>
        private static bool SetDropList(StringCollection filePaths, DragDropEffects effect, Action<string> callBack)
        {
            try {
                if (filePaths == null || filePaths.Count == 0)
                    return false;

                string[] files = new string[filePaths.Count];
                filePaths.CopyTo(files, 0);
                if (files.Length == 0)
                    return false;

                DataObject data = new DataObject();
                data.SetData(DataFormats.FileDrop, files);

                using (MemoryStream stream = new MemoryStream()) {
                    stream.Write(BitConverter.GetBytes((int)effect), 0, 4);
                    data.SetData("Preferred DropEffect", stream);
                    Clipboard.SetDataObject(data, true);
                }
                return true;
            } catch (Exception ex) {
                callBack?.Invoke(ex.Message);
                return false;
            }
        }
    }
}
