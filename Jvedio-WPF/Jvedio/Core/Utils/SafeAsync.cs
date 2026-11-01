using System;
using System.Threading.Tasks;

namespace Jvedio.Core.Utils
{
    /// <summary>
    /// 异步调用的统一兜底。
    /// <para>
    /// 背景：项目里有 30 多个 <c>async void</c> 方法（WPF 事件处理器**必须**是 async void）。
    /// <c>async void</c> 的异常不会传到任何 <c>Task</c>，而是直接抛到
    /// <c>SynchronizationContext</c>，最终触发 <c>AppDomain.UnhandledException</c> —— 程序崩溃。
    /// </para>
    /// <para>
    /// 用法：把 <c>Foo();</c> 改成 <c>SafeAsync.Fire(Foo);</c>，异常会被记录而不是崩溃。
    /// 对需要等待结果的场景用 <see cref="Run"/>。
    /// </para>
    /// </summary>
    public static class SafeAsync
    {
        /// <summary>
        /// 异常记录回调。宿主（桌面端 / 移动端）自行注入，未注入时退回 <see cref="Console.Error"/>。
        /// <para>
        /// 用委托而非直接引用 <c>App.Logger</c>，是为了让本类可迁入无 UI 依赖的跨平台库。
        /// </para>
        /// </summary>
        public static Action<Exception> ErrorHandler { get; set; }

        /// <summary>
        /// 触发一个异步方法并吞掉异常（记录到日志）。
        /// 用于「发起后不关心结果」的场景，如界面初始化、后台刷新。
        /// </summary>
        public static void Fire(Func<Task> action, string tag = null)
        {
            if (action == null)
                return;

            try {
                Task task = action();
                if (task == null)
                    return;

                task.ContinueWith(t => {
                    if (t.IsFaulted && t.Exception != null)
                        LogFault(tag, t.Exception);
                }, TaskContinuationOptions.OnlyOnFaulted);
            } catch (Exception ex) {
                // 同步抛出（方法体内第一个 await 之前）也要兜住
                LogFault(tag, ex);
            }
        }

        /// <summary>
        /// 执行并等待，异常转成 <c>false</c> 而不是抛出。
        /// </summary>
        public static async Task<bool> Run(Func<Task> action, string tag = null)
        {
            if (action == null)
                return true;

            try {
                await action().ConfigureAwait(false);
                return true;
            } catch (Exception ex) {
                LogFault(tag, ex);
                return false;
            }
        }

        /// <summary>
        /// 执行并等待返回值，异常时返回 <paramref name="fallback"/>。
        /// </summary>
        public static async Task<T> Run<T>(Func<Task<T>> action, T fallback = default(T), string tag = null)
        {
            if (action == null)
                return fallback;

            try {
                return await action().ConfigureAwait(false);
            } catch (Exception ex) {
                LogFault(tag, ex);
                return fallback;
            }
        }

        private static void LogFault(string tag, Exception ex)
        {
            try {
                string prefix = string.IsNullOrEmpty(tag) ? string.Empty : $"[{tag}] ";
                Action<Exception> handler = ErrorHandler;

                if (handler != null) {
                    handler(new InvalidOperationException($"{prefix}未捕获的异步异常", ex));
                } else {
                    Console.Error.WriteLine($"{prefix}未捕获的异步异常: {ex}");
                }
            } catch {
                // 记录失败时不能再抛，否则又回到崩溃路径
            }
        }
    }
}
