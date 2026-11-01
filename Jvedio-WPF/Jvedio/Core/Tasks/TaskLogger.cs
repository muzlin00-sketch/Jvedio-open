using System.Collections.Generic;

namespace Jvedio.Core.Tasks
{
    /// <summary>
    /// 把日志写进任务自带的 <see cref="AbstractTask.Logs"/> 列表的日志器，
    /// 取代 <c>SuperUtils.Framework.Tasks.TaskLogger</c>。
    /// <para>
    /// 与 <see cref="Logs.AbstractLogger"/> 的区别：这里不落盘、不过滤级别，
    /// 每条追加进列表由界面绑定显示。行格式 <c>[时间][级别] 内容</c>，不带结尾换行——
    /// 列表里一项就是一行。
    /// </para>
    /// </summary>
    public class TaskLogger
    {
        private readonly List<string> _logs;

        /// <summary>传入承载日志的列表；传 null 时所有写入静默丢弃。</summary>
        public TaskLogger(List<string> logs)
        {
            _logs = logs;
        }

        public void Info(string str)
        {
            Append("I", str);
        }

        public void Warning(string str)
        {
            Append("W", str);
        }

        public void Error(string str)
        {
            Append("E", str);
        }

        private void Append(string tag, string str)
        {
            _logs?.Add($"[{Jvedio.Common.DateHelper.Now()}][{tag}] {str}");
        }
    }
}