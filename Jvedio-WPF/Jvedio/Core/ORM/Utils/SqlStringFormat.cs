using System;
using System.Text.RegularExpressions;

namespace Jvedio.Core.ORM.Utils
{
    /// <summary>
    /// SQL 文本处理，与原 <c>SuperUtils.Framework.ORM.Utils.SqlStringFormat</c> 一致。
    /// <para>
    /// 这一组方法名容易望文生义，实际语义是踩过坑才确认的：
    /// <list type="bullet">
    /// <item><see cref="FormatQuote"/> 只做单引号转义，<b>不</b>加引号；</item>
    /// <item><see cref="HandleNewLine"/> 是<b>删除</b>换行而不是保留；</item>
    /// <item><see cref="CleanSqlString"/> 会删掉<b>所有</b>空白（连标识符之间的也删）；</item>
    /// <item><see cref="ToProperSql"/> 是整串统一大小写，不是只改关键字。</item>
    /// </list>
    /// 照着名字"修正"会让 SQL 变样，历史数据读写对不上。
    /// </para>
    /// </summary>
    public static class SqlStringFormat
    {
        /// <summary>把对象转成可拼进 SQL 的文本：null → 空串，字符串里的单引号翻倍。</summary>
        public static string Format(object obj)
        {
            if (obj == null)
                return string.Empty;

            if (obj is string str)
                return FormatQuote(str);

            return obj.ToString();
        }

        /// <summary>单引号转义（翻倍），不添加外层引号。</summary>
        public static string FormatQuote(string str)
        {
            if (string.IsNullOrEmpty(str))
                return string.Empty;

            return str.Replace("'", "''");
        }

        /// <summary>删除回车与换行。</summary>
        public static string HandleNewLine(string str)
        {
            if (string.IsNullOrEmpty(str))
                return string.Empty;

            return str.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        /// <summary>删除所有空白字符与单引号。</summary>
        public static string CleanSqlString(string str)
        {
            if (str == null)
                return null;

            // 注意：除了空白，单引号也会被删掉；双引号、反引号等一律保留
            return Regex.Replace(str, @"[\s']", string.Empty);
        }

        /// <summary>
        /// 整串转大写；<paramref name="toUpper"/> 为 false 时<b>原样返回</b>（不是转小写）。
        /// <para>
        /// 两个分支都会先删掉单引号——这是原实现的行为，别以为是笔误：
        /// <c>select 'abc' from t</c> → <c>SELECT ABC FROM T</c>。
        /// </para>
        /// </summary>
        public static string ToProperSql(string sql, bool toUpper = true)
        {
            if (sql == null)
                return null;

            string result = sql.Replace("'", string.Empty);
            return toUpper ? result.ToUpper() : result;
        }
    }
}
