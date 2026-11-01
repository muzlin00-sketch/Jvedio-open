using System;
using System.Threading.Tasks;

namespace Jvedio.Core.Net
{
    /// <summary>任务超时包装（取代 <c>SuperUtils.Framework.Tasks.TaskUtils.TimeoutAfter</c>）。</summary>
    internal static class TaskExtensions
    {
        /// <summary>
        /// 超过 <paramref name="timeout"/> 还没完成就抛 <see cref="TimeoutException"/>。
        /// 注意这里只是"不再等"，底层请求仍会在后台跑到它自己的超时为止。
        /// </summary>
        public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            if (timeout <= TimeSpan.Zero)
                return await task;

            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task)
                throw new TimeoutException();

            return await task;
        }
    }
}
