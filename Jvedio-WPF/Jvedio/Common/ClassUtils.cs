using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using static Jvedio.App;

namespace Jvedio.Common
{
    /// <summary>
    /// 对象的文本化工具，取代 <c>SuperUtils.Reflections.ClassUtils</c>。
    /// <para>
    /// 用途是给实体的 <c>ToString()</c> 覆盖和扫描结果导出用，输出结构固定：
    /// 首行 <c>[类型名]</c>，每行 <c>- 属性名: 值</c>；集合属性展开成
    /// <c>(Collection[n])</c> 后逐项 <c>  序号/总数 项</c>。
    /// </para>
    /// </summary>
    public static class ClassUtils
    {
        /// <summary>反射赋值，失败只记日志不抛（属性不存在/类型不匹配都算正常情况）。</summary>
        public static void TrySetValue(PropertyInfo info, object obj, object value)
        {
            try {
                info.SetValue(obj, value);
            } catch (Exception ex) {
                Logger?.Error(ex.Message);
            }
        }

        public static string ToString<T>(T obj, bool singleLine = false)
        {
            if (obj == null)
                return string.Empty;

            Type type = obj.GetType();
            PropertyInfo[] properties = type.GetProperties();
            string lineBreak = singleLine ? Environment.NewLine : ",";
            StringBuilder builder = new StringBuilder();
            builder.Append("[" + type.Name + "]" + Environment.NewLine);

            foreach (PropertyInfo property in properties) {
                builder.Append("- " + property.Name);
                object value = property.GetValue(obj);

                if (value == null) {
                    builder.Append(": null" + Environment.NewLine);
                    continue;
                }

                if (value is IDictionary dictionary && HasInterface(type, value, typeof(IDictionary))) {
                    builder.Append(string.Format("{0} (Dictionary[{1}]){2}", string.Empty, dictionary.Count, Environment.NewLine));
                    int index = 0;
                    foreach (object key in dictionary.Keys) {
                        index++;
                        builder.Append(string.Format("  {0}/{1} {2}: {3}{4}", index, dictionary.Count, key, dictionary[key], Environment.NewLine));
                    }
                } else if (value is ICollection collection && HasInterface(type, value, typeof(ICollection))) {
                    builder.Append(string.Format("{0} (Collection[{1}]){2}", string.Empty, collection.Count, Environment.NewLine));
                    int index = 0;
                    foreach (object item in collection) {
                        index++;
                        builder.Append(string.Format("  {0}/{1} {2}{3}", index, collection.Count, item, Environment.NewLine));
                    }
                } else {
                    builder.Append(string.Format("{0}: {1}{2}", string.Empty, value, lineBreak));
                }
            }

            return builder.ToString();
        }

        private static bool HasInterface(Type ownerType, object value, Type target)
        {
            return Array.IndexOf(value.GetType().GetInterfaces(), target) >= 0;
        }

        /// <summary>
        /// 把对象列表导成 CSV 文本：首行属性名，之后每行用双引号包裹每个值。
        /// 空列表返回空字符串。
        /// </summary>
        public static string ToCsv<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                return string.Empty;

            PropertyInfo[] properties = list[0].GetType().GetProperties();
            StringBuilder builder = new StringBuilder();
            List<string> headers = new List<string>();
            foreach (PropertyInfo property in properties)
                headers.Add(property.Name);

            builder.Append(string.Join(",", headers));
            builder.AppendLine();

            foreach (T item in list) {
                List<string> values = new List<string>();
                foreach (PropertyInfo property in properties)
                    values.Add(string.Format("\"{0}\"", property.GetValue(item)));

                builder.Append(string.Join(",", values));
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
