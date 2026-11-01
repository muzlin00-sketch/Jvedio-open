using System;
using System.Diagnostics;
using System.Text;

namespace Jvedio.Core.Systems
{
    /// <summary>
    /// 命令行调用封装，取代 <c>SuperUtils.Windows.WindowCmd.CmdHelper</c>。
    /// <para>
    /// 输出按行（<c>OutputDataReceived</c>）回调，UTF-8 解码——Java 服务端的
    /// 「jvedio server start ok!」就靠这行回调被识别出来。
    /// </para>
    /// </summary>
    public static class CmdHelper
    {
        /// <param name="onOutput">标准输出每一行。</param>
        /// <param name="onErrorOutput">标准错误每一行。</param>
        /// <param name="onError">启动/运行抛出的异常。</param>
        /// <param name="onCompleted">进程退出后（含异常退出）调用。</param>
        /// <param name="onCreated">进程启动后立即回调，调用方可拿到 Process 以便后续结束它。</param>
        public static void Run(
            string fileName,
            string cmdParam,
            Action<string> onOutput = null,
            Action<string> onErrorOutput = null,
            Action<Exception> onError = null,
            Action onCompleted = null,
            bool createNoWindow = true,
            bool useShellExecute = false,
            Action<Process> onCreated = null)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(cmdParam))
                return;

            Process process = new Process();
            try {
                process.StartInfo = new ProcessStartInfo() {
                    FileName = fileName,
                    Arguments = cmdParam,
                    CreateNoWindow = createNoWindow,
                    UseShellExecute = useShellExecute,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    RedirectStandardError = true,
                    Verb = "runas",
                };
                process.EnableRaisingEvents = true;
                process.ErrorDataReceived += (sender, e) => onErrorOutput?.Invoke(e.Data);
                process.OutputDataReceived += (sender, e) => onOutput?.Invoke(e.Data);
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                onCreated?.Invoke(process);
                process.WaitForExit();
            } catch (Exception ex) {
                onError?.Invoke(ex);
            } finally {
                process.Dispose();
                onCompleted?.Invoke();
            }
        }

        /// <summary>按端口找占用进程的 PID，找不到返回 -1。</summary>
        public static int GetPidByPort(int port)
        {
            Console.WriteLine(string.Format("-ano|findstr \"{0}\"", port));
            int pid = -1;
            Run(
                "netstat.exe",
                string.Format("-ano|findstr \"{0}\"", port),
                (line) => {
                    string[] parts = line.Split(' ');
                    if (parts.Length > 4)
                        int.TryParse(parts[parts.Length - 1], out pid);
                },
                null,
                null,
                null,
                false,
                false,
                null);

            return pid;
        }
    }
}
