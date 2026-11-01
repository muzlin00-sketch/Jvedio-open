using System;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Jvedio.Core.WPF
{
    /// <summary>
    /// 按名字找/开窗口，取代 <c>SuperUtils.WPF.VisualTools.WindowHelper</c>。
    /// <para>
    /// 名字匹配用的是<b>窗口类的类型名</b>（<c>Window_Details</c> 这类），不是
    /// <c>Window.Name</c> 属性——所以 WPF 里没设 x:Name 也能找到。
    /// </para>
    /// </summary>
    public static class WindowHelper
    {
        /// <summary>在当前打开的窗口里找类型名为 <paramref name="name"/> 的那个。</summary>
        public static Window GetWindowByName(string name, WindowCollection windowCollection)
        {
            if (string.IsNullOrEmpty(name) || windowCollection == null || windowCollection.Count == 0)
                return null;

            foreach (Window window in windowCollection) {
                if (window.GetType().Name.Equals(name))
                    return window;
            }

            return null;
        }

        /// <summary>
        /// 打开窗口：已打开就激活它，没打开就按类型名反射创建。
        /// <para>名字以 <c>Dialog_</c> 开头的当对话框处理（阻塞式 <c>ShowDialog</c>）。</para>
        /// </summary>
        public static void OpenWindowByName(string name, WindowCollection windowCollection, string nameSpace, Window parent = null)
        {
            if (string.IsNullOrEmpty(name))
                return;

            Window window = GetWindowByName(name, windowCollection);
            if (window == null) {
                Assembly assembly = Assembly.GetExecutingAssembly();
                foreach (Type type in assembly.GetTypes()) {
                    if (type.Namespace != nameSpace || !type.Name.Equals(name))
                        continue;

                    try {
                        window = (parent != null
                            ? Activator.CreateInstance(type, parent)
                            : Activator.CreateInstance(type)) as Window;
                    } catch (Exception) {
                        // 构造失败当作没找到，交给调用方的后续逻辑
                    }

                    if (window != null)
                        break;
                }
            }

            if (window == null)
                return;

            if (name.StartsWith("Dialog_")) {
                if (!window.IsVisible)
                    window.ShowDialog();
            } else {
                window.Show();
                window.BringIntoView();
                window.Focus();
            }
        }
    }
}
