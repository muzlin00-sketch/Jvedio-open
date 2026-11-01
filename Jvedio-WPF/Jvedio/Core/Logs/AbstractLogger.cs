using System;
using System.Runtime.CompilerServices;

namespace Jvedio.Core.Logs
{
    /// <summary>
    /// 日志器基类。取代 <c>SuperUtils.Framework.Logger.AbstractLogger</c>。
    /// <para>
    /// 子类只需要实现 <see cref="LogPrint"/>（真正落地的地方），
    /// 格式化与级别过滤在这里统一完成。
    /// </para>
    /// <para>
    /// 输出格式是<b>既有日志文件的契约</b>，改动会让按行解析日志的脚本失效：
    /// <code>[2026-10-29 20:13:33.336][E] [AsyncSend/124] 发送请求时出错。</code>
    /// 依次是时间（与数据库时间字段同格式）、级别字母、调用成员/行号、内容，
    /// 每条以 <c>\r\n</c> 结尾。
    /// </para>
    /// </summary>
    public abstract class AbstractLogger
    {
        /// <summary>
        /// 日志级别。数值越大越详细，过滤规则是"配置级别 ≥ 本条级别就输出"，
        /// 因此 <see cref="None"/> 等于关闭所有日志。
        /// </summary>
        public enum Level
        {
            None = 0,
            Error = 1,
            Warn = 2,
            Info = 3,
            Debug = 4,
        }

        public Level LogLevel { get; set; } = Level.Debug;

        /// <summary>实际输出一条已格式化的日志（含结尾换行）。</summary>
        public abstract void LogPrint(string str);

        public void Debug(string str, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null)
        {
            Write(Level.Debug, "D", str, lineNumber, caller);
        }

        public void Info(string str, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null)
        {
            Write(Level.Info, "I", str, lineNumber, caller);
        }

        public void Warn(string str, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null)
        {
            Write(Level.Warn, "W", str, lineNumber, caller);
        }

        public void Error(string str, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null)
        {
            Write(Level.Error, "E", str, lineNumber, caller);
        }

        /// <summary>
        /// 记录异常，输出两行：<see cref="Exception.Message"/> 与 <see cref="Exception.StackTrace"/>。
        /// <para>
        /// 未真正抛出的异常 <c>StackTrace</c> 为 null，第二行就是空的——这与原实现一致。
        /// 两行的调用点都会显示为 <c>Error</c> 加本方法内部行号，而不是外部调用处，
        /// 因为这里是间接调用、没有转发调用方信息。
        /// </para>
        /// </summary>
        public void Error(Exception e)
        {
            if (e == null)
                return;

            Error(e.Message);
            Error(e.StackTrace);
        }

        private void Write(Level level, string tag, string str, int lineNumber, string caller)
        {
            if (LogLevel < level)
                return;

            LogPrint($"[{Jvedio.Common.DateHelper.Now()}][{tag}] [{caller}/{lineNumber}] {str ?? string.Empty}\r\n");
        }
    }
}
