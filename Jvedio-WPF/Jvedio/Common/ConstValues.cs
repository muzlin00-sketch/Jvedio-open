namespace Jvedio.Common
{
    /// <summary>
    /// 跨模块共用的常量。取代 <c>SuperUtils.Values.ConstValues</c>。
    /// </summary>
    public static class ConstValues
    {
        /// <summary>
        /// 多值字段的分隔符：ASCII 0x07（BEL）。
        /// <para>
        /// <b>这是数据格式，不是显示细节，绝对不能改。</b>
        /// 数据库里演员名、标签、视频路径、图片路径等多值字段都是用这个字符连接的
        /// （见 <c>string.Join(ConstValues.SeparatorString, ...)</c> 与
        /// <c>value.Split(ConstValues.Separator)</c>）。改成逗号、分号或换行，
        /// 存量记录会整体拆不开，而且不会报错——只会表现为"演员名突然变成一个长串"。
        /// </para>
        /// <para>
        /// 选它是因为几乎不可能出现在文件名和演员名里，比逗号安全。
        /// </para>
        /// </summary>
        public const char Separator = '\u0007';

        /// <summary><see cref="Separator"/> 的字符串形式，供 <c>string.Join</c> 使用。</summary>
        public const string SeparatorString = "\u0007";

        /// <summary>文件对话框的 JSON 过滤器。</summary>
        public const string FILTER_JSON = "*.json|*.json";
    }
}
