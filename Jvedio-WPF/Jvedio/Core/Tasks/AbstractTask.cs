using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Jvedio.Core.Tasks
{
    /// <summary>
    /// 任务基类，取代 <c>SuperUtils.Framework.Tasks.AbstractTask</c>。
    /// <para>
    /// 提供标题、进度、状态、耗时、日志、取消令牌与三个完成/失败/取消事件，
    /// 由 <c>BaseManager</c> 收集进任务列表并绑定到界面。
    /// </para>
    /// <para>
    /// 子类只需要重写 <see cref="DoWork"/>，在里面启动自己的异步流程，
    /// 结束时调用 <see cref="OnCompleted"/> 或 <see cref="FinalizeWithCancel"/>。
    /// </para>
    /// </summary>
    public class AbstractTask : INotifyPropertyChanged
    {
        /// <summary>
        /// 状态到默认提示文案的映射。子类可以在自己的静态构造函数里覆盖某一项
        /// （例如把 <c>Running</c> 改成"扫描中……"）。
        /// <para>
        /// 注意这是<b>全类型共享</b>的静态字典，后加载的子类会覆盖前面设过的同名键——
        /// 这是原实现的行为，保留不动。
        /// </para>
        /// </summary>
        public static Dictionary<System.Threading.Tasks.TaskStatus, string> STATUS_TO_TEXT_DICT { get; } =
            new Dictionary<System.Threading.Tasks.TaskStatus, string> {
                { System.Threading.Tasks.TaskStatus.WaitingToRun, "Waiting..." },
                { System.Threading.Tasks.TaskStatus.Running, "Running..." },
                { System.Threading.Tasks.TaskStatus.Canceled, "Cancel" },
                { System.Threading.Tasks.TaskStatus.RanToCompletion, "Done" },
            };

        private string _ID;
        private string _Title;
        private string _StatusText;
        private float _Progress;
        private System.Threading.Tasks.TaskStatus _Status;
        private string _Message;
        private string _CreateTime;
        private long _ElapsedMilliseconds;

        public string ID {
            get { return _ID; }
            set { _ID = value; RaisePropertyChanged(); }
        }

        public string Title {
            get { return _Title; }
            set { _Title = value; RaisePropertyChanged(); }
        }

        /// <summary>状态提示文案，随 <see cref="Status"/> 自动改写。</summary>
        public string StatusText {
            get { return _StatusText; }
            set { _StatusText = value; RaisePropertyChanged(); }
        }

        public float Progress {
            get { return _Progress; }
            set { _Progress = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// 任务状态。用的是 BCL 的 <see cref="System.Threading.Tasks.TaskStatus"/>（不是自定义枚举）。
        /// <para>
        /// <b>赋值会覆盖 <see cref="StatusText"/></b>：命中字典就用字典文案，否则置空。
        /// 所以"先设 StatusText = 错误信息，再调 FinalizeWithCancel()"会把错误信息冲掉，
        /// 这是原实现行为，调用方依赖它的一致性，不要"顺手修好"。
        /// </para>
        /// </summary>
        public System.Threading.Tasks.TaskStatus Status {
            get { return _Status; }
            set {
                _Status = value;
                StatusText = STATUS_TO_TEXT_DICT.ContainsKey(value) ? STATUS_TO_TEXT_DICT[value] : string.Empty;
                RaisePropertyChanged();
            }
        }

        public string Message {
            get { return _Message; }
            set { _Message = value; RaisePropertyChanged(); }
        }

        public string CreateTime {
            get { return _CreateTime; }
            set { _CreateTime = value; RaisePropertyChanged(); }
        }

        public long ElapsedMilliseconds {
            get { return _ElapsedMilliseconds; }
            set { _ElapsedMilliseconds = value; RaisePropertyChanged(); }
        }

        public bool Canceled { get; set; }

        public bool Success { get; set; }

        public bool Running { get; set; }

        public Stopwatch TimeWatch { get; set; }

        public CancellationTokenSource TokenCTS { get; set; }

        public CancellationToken Token { get; set; }

        /// <summary>任务日志，界面绑定这个列表实时刷新。</summary>
        public List<string> Logs { get; set; }

        public TaskLogger Logger { get; set; }

        public event EventHandler onCompleted;

        public event EventHandler onError;

        public event EventHandler onCanceled;

        public event PropertyChangedEventHandler PropertyChanged;

        public AbstractTask()
        {
            ID = Guid.NewGuid().ToString();
            Status = System.Threading.Tasks.TaskStatus.WaitingToRun;
            CreateTime = Jvedio.Common.DateHelper.Now();

            TokenCTS = new CancellationTokenSource();
            // 外部取消时补发一次 onCanceled，界面才能刷新运行计数
            TokenCTS.Token.Register(() => OnCanceled(null));
            Token = TokenCTS.Token;

            TimeWatch = new Stopwatch();
            Logs = new List<string>();
            Logger = new TaskLogger(Logs);
        }

        public void OnCompleted(EventArgs e)
        {
            onCompleted?.Invoke(this, e);
        }

        public void OnError(EventArgs e)
        {
            onError?.Invoke(this, e);
        }

        public void OnCanceled(EventArgs e)
        {
            onCanceled?.Invoke(this, e);
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 启动任务。已经在跑就直接返回，避免重复进入。
        /// <para>
        /// <see cref="DoWork"/> 里抛出的异常会被这里捕获并转成一条错误日志，
        /// 然后走 <see cref="FinalizeWithCancel"/> 收尾，不会让异常逃逸到调用方。
        /// </para>
        /// </summary>
        public void Start()
        {
            if (Running)
                return;

            Status = System.Threading.Tasks.TaskStatus.Running;
            CreateTime = Jvedio.Common.DateHelper.Now();
            Running = true;
            try {
                DoWork();
            } catch (Exception e) {
                Logger?.Error(e.ToString());
                Logger?.Error(e.StackTrace);
                FinalizeWithCancel();
            }
        }

        /// <summary>只在运行中或待运行时可取消；取消会触发令牌，并补发 <c>onCanceled</c>。</summary>
        public void Cancel()
        {
            if (Status == System.Threading.Tasks.TaskStatus.Running
                || Status == System.Threading.Tasks.TaskStatus.WaitingToRun) {
                Status = System.Threading.Tasks.TaskStatus.Canceled;
                Running = false;
                TokenCTS?.Cancel();
                Canceled = true;
            }
        }

        /// <summary>复位后重新开始：清空消息、状态文案、成功与取消标记。</summary>
        public void Restart()
        {
            Message = string.Empty;
            StatusText = string.Empty;
            Success = false;
            Canceled = false;
            Status = System.Threading.Tasks.TaskStatus.WaitingToRun;
            Start();
        }

        /// <summary>异常收尾：置为已取消、记录耗时、补发 <c>onCanceled</c>。</summary>
        public void FinalizeWithCancel()
        {
            Running = false;
            Status = System.Threading.Tasks.TaskStatus.Canceled;
            StopWatch();
            Success = false;
            Logger?.Info(Jvedio.Common.DateHelper.ToReadableTime(ElapsedMilliseconds));
            OnCanceled(null);
        }

        public void StartWatch()
        {
            TimeWatch.Start();
        }

        public void StopWatch()
        {
            TimeWatch.Stop();
            ElapsedMilliseconds = TimeWatch.ElapsedMilliseconds;
        }

        /// <summary>子类实现具体工作。基类不提供默认实现，误用会直接抛异常（与原实现一致）。</summary>
        public virtual void DoWork()
        {
            throw new NotImplementedException();
        }

        public void Finished()
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }

        public void Pause()
        {
            throw new NotImplementedException();
        }
    }
}
