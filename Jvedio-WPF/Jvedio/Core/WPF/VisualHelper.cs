using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Jvedio.Core.WPF
{
    /// <summary>
    /// 可视化树与颜色转换工具，取代 <c>SuperUtils.WPF.VisualTools.VisualHelper</c>。
    /// <para>
    /// <b>颜色字符串格式是持久化契约</b>：<see cref="SerializeBrush"/> 输出
    /// <c>R,G,B,A</c>（各分量 0-255），与 <see cref="RGBAToBrush"/> 的入参顺序一致；
    /// 标签颜色存在数据库里，改动格式会让老数据读成错色。
    /// </para>
    /// </summary>
    public static class VisualHelper
    {
        private static readonly BrushConverter Converter = new BrushConverter();

        public static Brush HexStringToBrush(string hexString)
        {
            try {
                return (Brush)Converter.ConvertFromString(hexString);
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>按 <c>R,G,B[,A]</c> 生成画刷；元素不足 3 个返回 null，省略 A 时按不透明处理。</summary>
        public static SolidColorBrush RGBAToBrush(string[] args)
        {
            if (args == null || args.Length < 3)
                return null;

            byte.TryParse(args[0].Trim(), out byte r);
            byte.TryParse(args[1].Trim(), out byte g);
            byte.TryParse(args[2].Trim(), out byte b);

            byte a = 255;
            if (args.Length == 4)
                byte.TryParse(args[3].Trim(), out a);

            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }

        /// <summary>把画刷序列化为 <c>R,G,B,A</c>；null 输出 <c>0,0,0,0</c>。</summary>
        public static string SerializeBrush(SolidColorBrush brush)
        {
            if (brush == null)
                return "0,0,0,0";

            Color color = brush.Color;
            return string.Format("{0},{1},{2},{3}", color.R, color.G, color.B, color.A);
        }

        /// <summary>
        /// 从 <paramref name="child"/> 往上找第一个类型为 <typeparamref name="T"/> 的父元素。
        /// <paramref name="name"/> 非空时还要匹配元素名。找不到返回 <c>default(T)</c>。
        /// <para>注意查找从<b>父级</b>开始，不含 <paramref name="child"/> 自身。</para>
        /// </summary>
        public static T FindParentOfType<T>(this FrameworkElement child, string name = null) where T : FrameworkElement
        {
            if (string.IsNullOrEmpty(name) && child == null)
                return default(T);

            while (child != null) {
                child = VisualTreeHelper.GetParent(child) as FrameworkElement;
                T result = child as T;

                if (string.IsNullOrEmpty(name)) {
                    if (result != null)
                        return result;
                } else if (result != null && result.Name != null && result.Name.Equals(name)) {
                    return result;
                }

                if (child == null)
                    return default(T);
            }

            return default(T);
        }

        /// <summary>在 <paramref name="element"/> 的子树里找名称为 <paramref name="childName"/> 的 <typeparamref name="T"/>。</summary>
        public static T FindElementByName<T>(this FrameworkElement element, string childName) where T : FrameworkElement
        {
            if (element == null || string.IsNullOrEmpty(childName))
                return default(T);

            T result = default(T);
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(element, i);
                FrameworkElement fe = child as FrameworkElement;
                if (fe == null)
                    continue;

                if (child is T typed && typed.Name != null && typed.Name.Equals(childName))
                    return typed;

                result = FindElementByName<T>(fe, childName);
                if (result != null)
                    return result;
            }

            return result;
        }

        /// <summary>按名称在可视化树里找子元素（会先 ApplyTemplate）。</summary>
        public static DependencyObject FindChild(DependencyObject parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;

            FrameworkElement parentElement = parent as FrameworkElement;
            if (parentElement != null && parentElement.Name != null && parentElement.Name.Equals(name))
                return parent;

            if (parent is FrameworkElement fe)
                fe.ApplyTemplate();

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                DependencyObject found = FindChild(VisualTreeHelper.GetChild(parent, i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>在可视化树里找第一个 <typeparamref name="T"/> 类型的子元素。</summary>
        public static T FindChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return default(T);

            if (parent is T typed)
                return typed;

            if (parent is FrameworkElement element)
                element.ApplyTemplate();

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                T result = FindChild<T>(VisualTreeHelper.GetChild(parent, i));
                if (result != null)
                    return result;
            }

            return default(T);
        }

        /// <summary>深度优先遍历所有 <typeparamref name="T"/> 类型的子元素。</summary>
        public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject obj) where T : DependencyObject
        {
            if (obj == null)
                yield break;

            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T typed)
                    yield return typed;

                foreach (T descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}
