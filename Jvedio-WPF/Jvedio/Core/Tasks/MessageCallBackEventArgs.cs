using System;

namespace Jvedio.Core.Tasks
{
    /// <summary>
    /// 携带一条消息的回调参数，取代 <c>SuperUtils.CustomEventArgs.MessageCallBackEventArgs</c>。
    /// <para>
    /// <see cref="Message"/> 是<b>字段</b>而不是属性——原实现就是字段，
    /// 外部有代码按字段访问，改成属性会破坏兼容。
    /// </para>
    /// </summary>
    public class MessageCallBackEventArgs : EventArgs
    {
        public string Message;

        public MessageCallBackEventArgs(string message)
        {
            Message = message;
        }
    }
}
