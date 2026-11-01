using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Jvedio.Common
{
    /// <summary>
    /// 数据集相关的扩展方法，取代 <c>SuperUtils.Common.DataHelper</c>。
    /// <para>
    /// 这些方法都是界面层随手用的"小工具"，语义按原实现逐值对拍得出（见各方法注释），
    /// 不要按"看起来更合理"的方式重写。
    /// </para>
    /// </summary>
    public static class DataHelper
    {
        /// <summary>按元素删除：把 <paramref name="to_remove"/> 里的元素从 <paramref name="data"/> 中移除。</summary>
        public static void RemoveMany<T>(this ICollection<T> data, ICollection<T> to_remove)
        {
            if (data == null || to_remove == null || to_remove.Count == 0)
                return;

            // 先物化一份，允许 to_remove 就是 data 本身
            foreach (T item in to_remove.ToList())
                data.Remove(item);
        }

        /// <summary>
        /// 判断 <paramref name="dir"/> 是否与集合里某个目录"有交集"（同一目录或互为上下级）。
        /// <para>
        /// 原实现在<b>带尾分隔符</b>的路径上判断不一致（<c>C:\a\</c> 与 <c>C:\a\b</c> 判为不相交），
        /// 这里按规范化路径判断——对正常的绝对路径结果一致，带尾分隔符时更严谨。
        /// </para>
        /// </summary>
        public static bool IsIntersectWith(this ICollection<string> collections, string dir)
        {
            if (collections == null || collections.Count == 0 || string.IsNullOrEmpty(dir))
                return false;

            string normalizedDir = NormalizeDir(dir);
            foreach (string item in collections) {
                if (string.IsNullOrEmpty(item))
                    continue;

                string normalizedItem = NormalizeDir(item);
                if (normalizedDir.Equals(normalizedItem, StringComparison.Ordinal))
                    return true;

                if (normalizedDir.StartsWith(normalizedItem + "\\", StringComparison.Ordinal))
                    return true;

                if (normalizedItem.StartsWith(normalizedDir + "\\", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string NormalizeDir(string dir)
        {
            string result = dir.Replace('/', '\\');
            return result.Length > 1 ? result.TrimEnd('\\') : result;
        }

        /// <summary>
        /// 自然排序：数字段按数值比较（<c>1,2,10</c>），其余按当前区域性比较。
        /// <para>首批用例：<c>第1集 第2集 第10集</c>、<c>a1 a2 a10</c>、<c>3 03 003</c>（数值相等时短的在前）。</para>
        /// </summary>
        public static IEnumerable<string> CustomSort(this IEnumerable<string> list)
        {
            if (list == null)
                return null;

            return list.OrderBy(x => x, NaturalStringComparer.Instance);
        }

        private sealed class NaturalStringComparer : IComparer<string>
        {
            public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y))
                    return 0;
                if (x == null)
                    return -1;
                if (y == null)
                    return 1;

                // 先忽略大小写做自然比较，完全相等时再用区分大小写的区域性比较兜底。
                // 这样 x/X/x2/x10 的次序是 x、X、x2、x10（原实现如此，不是 x2 插在 X 前面）。
                int result = CompareNatural(x, y, true);
                return result != 0 ? result : string.Compare(x, y, StringComparison.CurrentCulture);
            }

            private static int CompareNatural(string x, string y, bool ignoreCase)
            {
                StringComparison textComparison = ignoreCase
                    ? StringComparison.CurrentCultureIgnoreCase
                    : StringComparison.CurrentCulture;

                int i = 0, j = 0;
                while (i < x.Length && j < y.Length) {
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j])) {
                        int iStart = i, jStart = j;
                        while (i < x.Length && char.IsDigit(x[i]))
                            i++;
                        while (j < y.Length && char.IsDigit(y[j]))
                            j++;

                        string numX = x.Substring(iStart, i - iStart);
                        string numY = y.Substring(jStart, j - jStart);
                        string trimmedX = numX.TrimStart('0');
                        string trimmedY = numY.TrimStart('0');

                        if (trimmedX.Length != trimmedY.Length)
                            return trimmedX.Length.CompareTo(trimmedY.Length);

                        int numeric = string.CompareOrdinal(trimmedX, trimmedY);
                        if (numeric != 0)
                            return numeric;

                        // 数值相等时，位数少的排前面（3 < 03 < 003）
                        if (numX.Length != numY.Length)
                            return numX.Length.CompareTo(numY.Length);
                        continue;
                    }

                    int compare = string.Compare(x.Substring(i, 1), y.Substring(j, 1), textComparison);
                    if (compare != 0)
                        return compare;

                    i++;
                    j++;
                }

                return (x.Length - i).CompareTo(y.Length - j);
            }
        }

        /// <summary>
        /// 返回第一个能在字符串中找到的参数的下标（按 <paramref name="parameter"/> 顺序，不是取最小下标）。
        /// <para>空串参数会被忽略（原实现如此，否则 <c>IndexOf("")</c> 恒为 0）。</para>
        /// </summary>
        public static int IndexOfAnyString(this string str, string[] parameter)
        {
            if (string.IsNullOrEmpty(str) || parameter == null || parameter.Length == 0)
                return -1;

            foreach (string item in parameter) {
                if (string.IsNullOrEmpty(item))
                    continue;

                int index = str.IndexOf(item, StringComparison.Ordinal);
                if (index >= 0)
                    return index;
            }
            return -1;
        }

        /// <summary>读 WPF 图片元数据查询项，缺失或类型不符时返回默认值。</summary>
        public static T GetQueryOrDefault<T>(this BitmapMetadata metadata, string query, T defaultValue)
        {
            if (metadata == null || string.IsNullOrEmpty(query))
                return defaultValue;

            try {
                if (!metadata.ContainsQuery(query))
                    return defaultValue;

                object value = metadata.GetQuery(query);
                if (value == null)
                    return defaultValue;

                if (value is T typed)
                    return typed;

                Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            } catch (Exception) {
                return defaultValue;
            }
        }

        /// <summary>取字典值，缺失时返回默认值（键为 null 也不抛）。</summary>
        public static T2 Get<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 defaultValue = default)
        {
            if (dict == null || key == null)
                return defaultValue;

            try {
                return dict.TryGetValue(key, out T2 value) ? value : defaultValue;
            } catch (Exception) {
                return defaultValue;
            }
        }

        public static string GetString<T1>(this Dictionary<T1, object> dict, T1 key, string defaultValue = "")
        {
            object value = dict.Get(key);
            return value == null ? defaultValue : value.ToString();
        }

        public static int GetInt<T1>(this Dictionary<T1, object> dict, T1 key, int defaultValue = 0)
        {
            object value = dict.Get(key);
            if (value == null)
                return defaultValue;

            try {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            } catch (Exception) {
                return defaultValue;
            }
        }

        public static long GetLong<T1>(this Dictionary<T1, object> dict, T1 key, long defaultValue = 0)
        {
            object value = dict.Get(key);
            if (value == null)
                return defaultValue;

            try {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            } catch (Exception) {
                return defaultValue;
            }
        }

        public static bool GetBool<T1>(this Dictionary<T1, object> dict, T1 key, bool defaultValue = false)
        {
            object value = dict.Get(key);
            if (value == null)
                return defaultValue;

            try {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            } catch (Exception) {
                return defaultValue;
            }
        }

        public static double GetDouble<T1>(this Dictionary<T1, object> dict, T1 key, double defaultValue = 0)
        {
            object value = dict.Get(key);
            if (value == null)
                return defaultValue;

            try {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            } catch (Exception) {
                return defaultValue;
            }
        }

        /// <summary>字典是否同时包含全部键。</summary>
        public static bool ContainsKeys<T1, T2>(this Dictionary<T1, T2> dict, params T1[] keys)
        {
            if (dict == null || keys == null)
                return false;

            foreach (T1 key in keys) {
                if (key == null || !dict.ContainsKey(key))
                    return false;
            }
            return true;
        }

        /// <summary>取出现次数最多的键，空字典返回 null（相同次数取先出现的）。</summary>
        public static object TryGetMaxCountKey<T>(Dictionary<T, int> dict)
        {
            if (dict == null || dict.Count == 0)
                return null;

            object key = null;
            int max = int.MinValue;
            foreach (KeyValuePair<T, int> kv in dict) {
                if (kv.Value > max) {
                    max = kv.Value;
                    key = kv.Key;
                }
            }
            return key;
        }

        /// <summary>把 <c>\uXXXX</c> 形式的串还原成中文等字符。</summary>
        public static string Unicode2String(string unicode)
        {
            if (string.IsNullOrEmpty(unicode))
                return string.Empty;

            return System.Text.RegularExpressions.Regex.Unescape(unicode);
        }
    }
}
