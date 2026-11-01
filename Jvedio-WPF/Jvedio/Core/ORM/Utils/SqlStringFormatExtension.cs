namespace Jvedio.Core.ORM.Utils
{
    /// <summary>
    /// <see cref="SqlStringFormat"/> 的扩展方法形式，取代原
    /// <c>SuperUtils.Framework.ORM.Utils</c> 里同名的字符串扩展。
    /// <para>行为与静态方法完全一致：删掉单引号后整串转大写。</para>
    /// </summary>
    public static class SqlStringFormatExtension
    {
        public static string ToProperSql(this string sql, bool toUpper = true)
        {
            return SqlStringFormat.ToProperSql(sql, toUpper);
        }
    }
}
