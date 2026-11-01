namespace Jvedio.Core.Tasks
{
    /// <summary>
    /// 任务调度配置，取代 <c>SuperUtils.Framework.Tasks.TaskConfig</c>。
    /// <para>
    /// <see cref="TaskCount"/> 是同时运行的任务数上限，<see cref="TaskDelay"/> 控制派发下一个任务前的
    /// 抖动等待；<see cref="EnableLongTaskDelay"/> 打开后，每完成 <see cref="LongTaskCount"/> 个任务
    /// 额外长休息一次（用于降低被站点限流的概率）。
    /// </para>
    /// </summary>
    public class TaskConfig
    {
        public const uint DEFAULT_TASK_DELAY = 3000;
        public const uint DEFAULT_LONG_TASK_DELAY = 60000;
        public const uint DEFAULT_LONG_TASK_COUNT = 10;
        public const uint MAX_TASK_COUNT = 3;

        /// <summary>默认配置：3 个并发、3 秒抖动、长延迟关闭。值与原实现一致。</summary>
        public static TaskConfig DEFAULT { get; private set; }

        static TaskConfig()
        {
            DEFAULT = new TaskConfig {
                LongTaskDelay = DEFAULT_LONG_TASK_DELAY,
                TaskDelay = DEFAULT_TASK_DELAY,
                EnableLongTaskDelay = false,
                LongTaskCount = DEFAULT_LONG_TASK_COUNT,
                TaskCount = MAX_TASK_COUNT,
            };
        }

        public uint TaskDelay { get; set; }

        public uint TaskCount { get; set; }

        public uint LongTaskCount { get; set; }

        public uint LongTaskDelay { get; set; }

        public bool EnableLongTaskDelay { get; set; }

        public override string ToString()
        {
            return base.ToString();
        }
    }
}
