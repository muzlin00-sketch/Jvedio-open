using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Tasks
{
    /// <summary>
    /// 任务调度器，取代 <c>SuperUtils.Framework.Tasks.TaskDispatcher&lt;T&gt;</c>。
    /// <para>
    /// 把任务放进等待队列，由 <see cref="BeginWork"/> 启动一个后台循环：
    /// 按 <see cref="TaskConfig.TaskCount"/> 限制并发、每轮派发前抖动静默一段时间、
    /// 收集已完成/已取消的任务、计算总进度并触发事件。下载与截图的管理器都靠它串起来。
    /// </para>
    /// <para>
    /// 调度循环的写法（含 200ms 轮询间隔、进度公式、长延迟触发条件）是照原实现复刻的，
    /// 改动会让界面上的进度跳变节奏和长延迟提示时机发生变化。
    /// </para>
    /// </summary>
    public class TaskDispatcher<T> where T : AbstractTask
    {
        private const string LOG_TAG = "[TASK DISPATCHER]";
        private const int POLL_INTERVAL_MS = 200;

        private static readonly object QueueLock = new object();

        private int _Working;
        private int _Cancel;
        private float _Progress;

        private bool Working {
            get { return Interlocked.CompareExchange(ref _Working, 1, 1) == 1; }
            set {
                if (value)
                    Interlocked.CompareExchange(ref _Working, 1, 0);
                else
                    Interlocked.CompareExchange(ref _Working, 0, 1);
            }
        }

        private bool Cancel {
            get { return Interlocked.CompareExchange(ref _Cancel, 1, 1) == 1; }
            set {
                if (value)
                    Interlocked.CompareExchange(ref _Cancel, 1, 0);
                else
                    Interlocked.CompareExchange(ref _Cancel, 0, 1);
            }
        }

        public float Progress {
            get { return _Progress; }
            set { _Progress = value; }
        }

        public TaskConfig TaskConfig { get; set; }

        public int BeforeTaskCount { get; set; }

        public SafeQueue<T> WaitingQueue { get; set; }

        public List<T> WorkingList { get; set; }

        public List<T> DoneList { get; set; }

        public List<T> CanceledList { get; set; }

        public event EventHandler onWorking;

        public event EventHandler onComplete;

        public event EventHandler onLongDelay;

        public TaskDispatcher() : this(null)
        {
        }

        public TaskDispatcher(TaskConfig config)
        {
            TaskConfig = config ?? TaskConfig.DEFAULT;
        }

        public static TaskDispatcher<T> CreateInstance(TaskConfig config)
        {
            TaskDispatcher<T> dispatcher = new TaskDispatcher<T>(config) {
                WaitingQueue = new SafeQueue<T>(),
                WorkingList = new List<T>(),
                DoneList = new List<T>(),
                CanceledList = new List<T>(),
            };
            return dispatcher;
        }

        public void Enqueue(T task)
        {
            WaitingQueue.Enqueue(task);
        }

        /// <summary>停止调度并取消所有正在运行的任务。</summary>
        public void CancelWork()
        {
            Cancel = true;
            Working = false;
            foreach (T task in WorkingList)
                task?.Cancel();
        }

        /// <summary>清空已完成与已取消列表（不影响正在运行和排队的任务）。</summary>
        public void ClearDoneList()
        {
            DoneList.Clear();
            CanceledList.Clear();
        }

        /// <summary>启动调度循环；已经在跑就忽略。</summary>
        public void BeginWork()
        {
            if (Working)
                return;

            Working = true;
            BeforeTaskCount = 0;
            Task.Run(() => WorkLoop());
        }

        private async Task WorkLoop()
        {
            do {
                // ① 把已结束的任务从运行区挪走
                for (int i = WorkingList.Count - 1; i >= 0; i--) {
                    if (Cancel)
                        return;

                    T task = WorkingList[i];
                    if (task.Status == TaskStatus.RanToCompletion) {
                        DoneList.Add(task);
                        WorkingList.RemoveAt(i);
                    } else if (task.Status == TaskStatus.Canceled) {
                        CanceledList.Add(task);
                        WorkingList.RemoveAt(i);
                    }
                    task = null;
                }

                // ② 完成区/取消区里被重新置为待运行的，回到队列
                for (int i = DoneList.Count - 1; i >= 0; i--) {
                    if (DoneList[i].Status == TaskStatus.WaitingToRun) {
                        Enqueue(DoneList[i]);
                        DoneList.RemoveAt(i);
                    }
                }
                for (int i = CanceledList.Count - 1; i >= 0; i--) {
                    if (CanceledList[i].Status == TaskStatus.WaitingToRun) {
                        Enqueue(CanceledList[i]);
                        CanceledList.RemoveAt(i);
                    }
                }

                // ③ 补满并发额度
                while (WorkingList.Count < TaskConfig.TaskCount && WaitingQueue.GetCount() > 0) {
                    T task = WaitingQueue.Dequeue();
                    if (task.Status == TaskStatus.WaitingToRun)
                        WorkingList.Add(task);
                    else if (task.Status == TaskStatus.Canceled)
                        CanceledList.Add(task);
                    else if (task.Status == TaskStatus.RanToCompletion)
                        DoneList.Add(task);
                    task = null;
                }

                // ④ 启动所有待运行任务
                foreach (T task in WorkingList) {
                    if (task.Status == TaskStatus.WaitingToRun)
                        task.Start();
                }

                // ⑤ 进度：已完成 + 已取消 占总数的百分比
                float total = CanceledList.Count + DoneList.Count + WaitingQueue.GetCount() + WorkingList.Count;
                Progress = (float)Math.Round((float)(CanceledList.Count + DoneList.Count) / total * 100f, 2);

                if (Progress == 100f)
                    onComplete?.Invoke(this, null);
                onWorking?.Invoke(this, null);

                // ⑥ 队列空了就退出循环
                if (WorkingList.Count == 0 && WaitingQueue.GetCount() == 0) {
                    Working = false;
                    return;
                }

                // ⑦ 长延迟 或 普通抖动
                if (TaskConfig.EnableLongTaskDelay
                    && DoneList.Count > 0
                    && BeforeTaskCount != DoneList.Count
                    && TaskConfig.LongTaskCount > 0
                    && DoneList.Count % TaskConfig.LongTaskCount == 0) {
                    BeforeTaskCount = DoneList.Count;
                    int delay = Jvedio.Common.NumberHelper.GenerateRandomMS((int)TaskConfig.LongTaskDelay, 10000);
                    onLongDelay?.Invoke(this, new MessageCallBackEventArgs(delay.ToString()));
                    await Task.Delay(delay);
                } else if (WorkingList.Count != 0 && TaskConfig.TaskDelay > 0) {
                    await Task.Delay(Jvedio.Common.NumberHelper.GenerateRandomMS((int)TaskConfig.TaskDelay, 500));
                }

                // ⑧ 每轮都报一次"延迟结束"并固定歇 200ms
                onLongDelay?.Invoke(this, new MessageCallBackEventArgs("0"));
                await Task.Delay(POLL_INTERVAL_MS);
            } while (!Cancel);
        }

        /// <summary>线程安全的简单队列。取空时返回 <c>default(T)</c> 而不是抛异常。</summary>
        public class SafeQueue<Q>
        {
            private readonly Queue<Q> _queue = new Queue<Q>();
            private readonly object _lock = new object();

            public void Enqueue(Q item)
            {
                lock (_lock) {
                    _queue.Enqueue(item);
                }
            }

            public Q Dequeue()
            {
                lock (_lock) {
                    return _queue.Count > 0 ? _queue.Dequeue() : default(Q);
                }
            }

            public int GetCount()
            {
                lock (_lock) {
                    return _queue.Count;
                }
            }
        }
    }
}
