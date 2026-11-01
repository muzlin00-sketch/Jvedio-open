using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Jvedio.Common
{
    /// <summary>
    /// Win32 与性能计数封装，取代 <c>SuperUtils.Systems.Win32Helper</c>。
    /// <para>
    /// 热键与单实例消息是这里的关键部分：程序启动时若已有实例，就通过
    /// <see cref="SendArgs"/> 发 <c>WM_COPYDATA</c> 把 <see cref="WIN_CUSTOM_MSG_OPEN_WINDOW"/>
    /// 传给老实例，让对方把主窗口调到前台。
    /// </para>
    /// </summary>
    public static class Win32Helper
    {
        /// <summary>单实例消息正文。老实例在 WndProc 里按这个字符串判断「把窗口打开」。</summary>
        public const string WIN_CUSTOM_MSG_OPEN_WINDOW = "OpenWindow";

        public const int WM_COPYDATA = 0x004A;

        public const int SW_RESTORE = 9;

        private const uint EXECUTION_STATE_CONTINUOUS = 0x80000000;
        private const uint EXECUTION_STATE_DISPLAY_REQUIRED = 0x00000002;

        private static PerformanceCounter _cpuCounter;
        private static PerformanceCounter _ramCounter;

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyDataStruct : IDisposable
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;

            public void Dispose()
            {
                if (lpData != IntPtr.Zero) {
                    LocalFree(lpData);
                    lpData = IntPtr.Zero;
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(HandleRef hWnd, int nCmdShow);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalAlloc(int uFlags, int uBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        /// <summary>把一段字符串发给另一个进程窗口（WM_COPYDATA，UTF-16）。</summary>
        public static void SendArgs(IntPtr hWnd, string message)
        {
            if (message == null)
                message = string.Empty;

            CopyDataStruct data = new CopyDataStruct();
            try {
                data.cbData = (message.Length + 1) * 2;
                data.lpData = LocalAlloc(0x0040, data.cbData);
                char[] chars = message.ToCharArray();
                Marshal.Copy(chars, 0, data.lpData, chars.Length);
                data.dwData = (IntPtr)1;

                SendMessage(hWnd, WM_COPYDATA, IntPtr.Zero, ref data);
            } finally {
                data.Dispose();
            }
        }

        /// <summary>阻止系统休眠（下载/刮削任务期间用）。</summary>
        public static void PreventSleep()
        {
            SetThreadExecutionState(EXECUTION_STATE_CONTINUOUS | EXECUTION_STATE_DISPLAY_REQUIRED);
        }

        /// <summary>恢复系统休眠。</summary>
        public static void CancelPreventSleep()
        {
            SetThreadExecutionState(EXECUTION_STATE_CONTINUOUS);
        }

        /// <summary>把指定进程的窗口调到前台并恢复。</summary>
        public static void FocusProcess(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0) {
                IntPtr handle = processes[0].MainWindowHandle;
                ShowWindowAsync(new HandleRef(null, handle), SW_RESTORE);
                SetForegroundWindow(handle);
            }
        }

        /// <summary>取进程 CPU 占用百分比（需采样间隔，调用会阻塞约 500ms）。</summary>
        public static double GetCpuUsage(Process process)
        {
            if (process == null)
                return 0;

            string instanceName = GetPerformanceCounterProcessName(process.Id);
            using (PerformanceCounter counter = new PerformanceCounter("Process", "% Processor Time", instanceName, true)) {
                counter.NextValue();
                Thread.Sleep(500);
                float value = counter.NextValue();
                return Math.Round(value / Environment.ProcessorCount, 2);
            }
        }

        /// <summary>把进程名映射成性能计数器实例名（同名进程会带 #1、#2 后缀）。</summary>
        public static string GetPerformanceCounterProcessName(int processId)
        {
            try {
                return GetPerformanceCounterProcessName(Process.GetProcessById(processId).ProcessName);
            } catch (Exception) {
                return string.Empty;
            }
        }

        /// <summary>
        /// 把进程名映射成性能计数器实例名。
        /// <para>
        /// 第一个同名进程的实例名<b>不带</b>后缀（<c>chrome</c>），之后才是
        /// <c>chrome#1</c>、<c>chrome#2</c>，所以要从无后缀的名字开始试。
        /// 找不到返回 <see cref="string.Empty"/>。
        /// </para>
        /// </summary>
        public static string GetPerformanceCounterProcessName(string processName)
        {
            if (string.IsNullOrEmpty(processName))
                return string.Empty;

            string[] instanceNames;
            try {
                instanceNames = new PerformanceCounterCategory("Process").GetInstanceNames();
            } catch (Exception) {
                return string.Empty;
            }

            foreach (string instanceName in instanceNames) {
                if (!instanceName.StartsWith(processName))
                    continue;

                try {
                    using (PerformanceCounter counter = new PerformanceCounter("Process", "ID Process", instanceName, true)) {
                        if ((int)counter.NextValue() != 0)
                            return instanceName;
                    }
                } catch (Exception) {
                    // 实例刚消失或不可读，继续找下一个
                }
            }

            return string.Empty;
        }

        /// <summary>可用物理内存，形如 <c>1234 MB</c>。</summary>
        public static string GetAvailableRAM()
        {
            if (_ramCounter == null)
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes", true);

            return _ramCounter.NextValue().ToString() + " MB";
        }

        /// <summary>整机 CPU 占用百分比。</summary>
        public static float GetCurrentCpuUsage(string _)
        {
            try {
                if (_cpuCounter == null)
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);

                return _cpuCounter.NextValue();
            } catch (Exception) {
                return 0;
            }
        }
    }
}
