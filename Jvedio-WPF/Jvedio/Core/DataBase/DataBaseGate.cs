using System;
using System.Threading;

namespace Jvedio.Core.DataBase
{
    /// <summary>
    /// 数据库访问闸门。
    /// <para>
    /// 背景：Jvedio 的 Mapper 全部是静态的（<c>MapperManager.xxxMapper</c>），
    /// <c>BaseMapper&lt;T&gt;</c> 内部还静态持有一个闭源的 <c>SqliteMapper&lt;T&gt;</c> 实例。
    /// 桌面端单线程 UI 场景下没问题，但局域网 API 会让后台线程与 UI 线程
    /// 同时读写同一个连接，可能产生「database is locked」或数据错乱。
    /// </para>
    /// <para>
    /// 这里提供进程级的读写分离闸门：
    /// - 读操作可并发（<see cref="EnterRead"/>），避免列表/图片请求互相排队；
    /// - 写操作独占（<see cref="EnterWrite"/>），保证扫描入库、编辑保存等不被读到中间态。
    /// </para>
    /// <para>
    /// 注意：本闸门只解决**同进程内**的并发。跨进程（同时开两个 Jvedio）仍由 SQLite 自身的
    /// 文件锁处理，那条路径本来就会抛「database is locked」，属预期行为。
    /// </para>
    /// </summary>
    public static class DataBaseGate
    {
        private static readonly ReaderWriterLockSlim _Lock =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        /// <summary>是否启用闸门。单线程调试时可关闭以便定位死锁。</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// 进入读区（可并发）。返回 <see cref="IDisposable"/>，用 <c>using</c> 释放。
        /// </summary>
        public static IDisposable EnterRead()
        {
            if (Enabled)
                _Lock.EnterReadLock();
            return new Releaser(true);
        }

        /// <summary>
        /// 进入写区（独占）。返回 <see cref="IDisposable"/>，用 <c>using</c> 释放。
        /// </summary>
        public static IDisposable EnterWrite()
        {
            if (Enabled)
                _Lock.EnterWriteLock();
            return new Releaser(false);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly bool _IsRead;

            private bool _Disposed;

            public Releaser(bool isRead)
            {
                _IsRead = isRead;
            }

            public void Dispose()
            {
                if (_Disposed)
                    return;
                _Disposed = true;

                if (!Enabled)
                    return;

                try {
                    if (_IsRead)
                        _Lock.ExitReadLock();
                    else
                        _Lock.ExitWriteLock();
                } catch (SynchronizationLockException) {
                    // 极少数情况下（如异常路径重复释放）忽略，避免掩盖真实业务异常
                }
            }
        }
    }
}
