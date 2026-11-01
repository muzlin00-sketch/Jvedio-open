using System.Net;
using System.Net.Http.Headers;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// 一次 HTTP 请求的结果，取代 <c>SuperUtils.NetWork.Entity.HttpResult</c>。
    /// <para>
    /// <b>注意错误判定方式</b>：这里沿用原实现的"哨兵值"设计——
    /// <see cref="Error"/> 初始就是 <see cref="DEFAULT_ERROR_MSG"/>，只有真正出错时才被覆盖成
    /// 异常消息。所以调用方要判断"这次请求有没有出错"，应该比较
    /// <c>Error != DEFAULT_ERROR_MSG</c>，而不是看 <see cref="Success"/>。
    /// </para>
    /// <para>
    /// <see cref="Success"/> 在原实现里从未被赋过值（恒为 false），属于遗留字段，
    /// 保留只为兼容引用它的代码，不要拿它做判断。
    /// </para>
    /// </summary>
    public class HttpResult
    {
        /// <summary>未出错时的占位错误信息。真实错误会替换掉它。</summary>
        public const string DEFAULT_ERROR_MSG = "HTTP NOT OK";

        public bool Success { get; set; }

        public string Error { get; set; } = DEFAULT_ERROR_MSG;

        public string SourceCode { get; set; } = string.Empty;

        public byte[] FileByte { get; set; }

        /// <summary>未知状态默认按 403 处理（与原实现一致）。</summary>
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.Forbidden;

        public HttpResponseHeaders Headers { get; set; }
    }
}
