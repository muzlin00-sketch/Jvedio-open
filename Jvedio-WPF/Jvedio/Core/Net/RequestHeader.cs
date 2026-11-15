using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Jvedio.Core.Net
{
    /// <summary>
    /// 一次请求的配置（方法、超时、请求头、代理、请求体）。
    /// <para>
    /// 继承 <c>SuperUtils.NetWork.Entity.RequestHeader</c>，而不是另外定义一份同形类型：
    /// 插件（<c>plugins\crawlers\*.dll</c>）与闭源控件库的方法签名写死了那个类型，反射调用
    /// 传本项目自己的同形对象会抛 <c>InvalidCastException</c>——
    /// 「类型 "Jvedio.Core.Net.RequestHeader" 的对象无法转换为类型 "SuperUtils.NetWork.Entity.RequestHeader"」。
    /// 做成子类后，任何接收基类的接口都能直接接住它，不必在每个调用点手工复制字段。
    /// </para>
    /// <para>
    /// <see cref="SuperUtils.NetWork.Entity.RequestHeader.TimeOut"/> 的单位是<b>毫秒</b>（默认 10000），
    /// 而 <see cref="Jvedio.Core.Net.NetSetting.HttpTimeout"/> 的单位是<b>秒</b>，
    /// 两者在调用处相乘转换，不要混用。
    /// </para>
    /// </summary>
    public class RequestHeader : SuperUtils.NetWork.Entity.RequestHeader
    {
        public RequestHeader()
        {
            // 基类默认 WebProxy = null；本项目约定默认走系统代理，
            // 配置里关掉代理的场景由调用处显式置 null。
            WebProxy = SuperWebProxy.SystemWebProxy;
        }

        public override string ToString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("method: " + Method);
            sb.AppendLine("allow redirect: " + AllowAutoRedirect);
            sb.AppendLine("TimeOut: " + TimeOut);
            sb.AppendLine("Headers: ");
            if (Headers != null) {
                foreach (var kv in Headers)
                    sb.AppendLine(kv.Key + ": " + kv.Value);
            }
            return sb.ToString();
        }
    }
}
