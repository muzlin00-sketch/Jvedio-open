using Newtonsoft.Json;
using System;
using System.Reflection;

namespace Jvedio.Common
{
    /// <summary>
    /// JSON 序列化/反序列化的容错包装。取代 <c>SuperUtils.Common.JsonUtils</c>。
    /// <para>
    /// 项目里绝大多数 JSON 都是"配置项存一段字符串"这种用法，损坏时不应该让整个功能崩掉，
    /// 因此这里把所有异常都吞掉。调用方拿到 <c>null</c> 时必须自己判断——
    /// 这是沿用原实现的契约，不要指望它返回空集合。
    /// </para>
    /// </summary>
    public static class JsonUtils
    {
        /// <summary>
        /// 反序列化，失败返回 <c>default(T)</c>（引用类型即 <c>null</c>）。
        /// <para>
        /// 空串、空白串、非法 JSON 都返回 <c>null</c>，不会抛异常。
        /// </para>
        /// </summary>
        public static T TryDeserializeObject<T>(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default(T);

            try {
                return JsonConvert.DeserializeObject<T>(value);
            } catch (Exception) {
                return default(T);
            }
        }

        /// <summary>
        /// 序列化，失败返回 <c>null</c>。
        /// <para>
        /// 两个细节是逐值对拍原实现得出的，不要"顺手优化"：
        /// 传入 <c>null</c> 时返回 <c>null</c>（不是空串、也不是字符串 "null"）；
        /// 单参数版本默认使用缩进格式，落库的 JSON 因此是多行的。
        /// 改成紧凑格式会让同一份配置在"读出来再写回去"之后字节发生变化。
        /// </para>
        /// </summary>
        public static string TrySerializeObject(object obj)
        {
            return TrySerializeObject(obj, Formatting.Indented);
        }

        /// <summary>按指定缩进风格序列化。</summary>
        public static string TrySerializeObject(object value, Formatting formatting)
        {
            if (value == null)
                return null;

            try {
                return JsonConvert.SerializeObject(value, formatting);
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>
        /// 依据类型信息生成 JSON 骨架，用于导出配置结构。原实现即据此列出字段默认值。
        /// </summary>
        public static string ClasstoJson(TypeInfo[] types)
        {
            if (types == null)
                return string.Empty;

            try {
                var dict = new System.Collections.Generic.Dictionary<string, object>();
                foreach (TypeInfo type in types) {
                    if (type == null || type.AsType() == null)
                        continue;
                    object instance = null;
                    try {
                        instance = Activator.CreateInstance(type.AsType());
                    } catch (Exception) {
                        // 无法无参构造的类型只记录类型名
                    }
                    dict[type.Name] = instance;
                }
                return TrySerializeObject(dict);
            } catch (Exception) {
                return string.Empty;
            }
        }
    }
}
