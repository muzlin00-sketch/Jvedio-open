using System;
using System.IO;
using System.Reflection;

namespace Jvedio.Common
{
    /// <summary>
    /// 反射相关的容错封装，取代 <c>SuperUtils.Reflections.ReflectionHelper</c>。
    /// <para>所有方法失败都返回 null 而不抛异常——调用方（插件载入、窗口反射创建）靠 null 判断失败。</para>
    /// </summary>
    public static class ReflectionHelper
    {
        /// <summary>从文件加载程序集；文件不存在或不是有效程序集时返回 null。</summary>
        public static Assembly TryLoadAssembly(string path)
        {
            try {
                return Assembly.Load(File.ReadAllBytes(path));
            } catch (Exception) {
                return null;
            }
        }

        public static object TryCreateInstance(Type type, params object[] args)
        {
            if (type == null)
                return null;

            try {
                return Activator.CreateInstance(type, args);
            } catch (Exception) {
                return null;
            }
        }
    }
}
