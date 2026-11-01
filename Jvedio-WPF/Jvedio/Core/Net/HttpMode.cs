namespace Jvedio.Core.Net
{
    /// <summary>
    /// 请求的返回形态，取代 <c>SuperUtils.NetWork.Enums.HttpMode</c>。
    /// <para>数值必须保持 0/1/2，历史调用方按整数传参的对拍结果依赖它。</para>
    /// </summary>
    public enum HttpMode
    {
        /// <summary>把响应体读成字符串，放进 <see cref="HttpResult.SourceCode"/>。</summary>
        String = 0,

        /// <summary>只取响应头，不读响应体。</summary>
        Header = 1,

        /// <summary>把响应体读成字节数组，放进 <see cref="HttpResult.FileByte"/>。</summary>
        Stream = 2,
    }
}
