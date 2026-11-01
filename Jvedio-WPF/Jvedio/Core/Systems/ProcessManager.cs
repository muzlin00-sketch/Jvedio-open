using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jvedio.Core.Systems
{
    /// <summary>netstat 一行解析出来的连接项。</summary>
    public class PRC
    {
        public string Protocol { get; set; }

        public int Port { get; set; }

        public int PID { get; set; }

        public override string ToString()
        {
            return Protocol + " " + Port + " " + PID;
        }
    }

    /// <summary>
    /// 进程管理，取代 <c>SuperUtils.Windows.WindowCmd.ProcessManager</c>。
    /// <para>实现方式是跑 <c>netstat -a -n -o</c> 再按空格切列解析，不依赖 IP Helper API。</para>
    /// </summary>
    public class ProcessManager
    {
        /// <summary>按端口杀掉占用它的进程；没有进程占用该端口返回 false。</summary>
        public static bool KillByPort(int port)
        {
            List<PRC> processes = GetAllProcesses();
            if (processes == null)
                return false;

            if (processes.Any(p => p.Port == port)) {
                try {
                    PRC target = processes.First(p => p.Port == port);
                    Process.GetProcessById(target.PID).Kill();
                    return true;
                } catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                }
            } else {
                Console.WriteLine("No process to kill!");
            }

            return false;
        }

        /// <summary>解析 <c>netstat -a -n -o</c> 的每一行，取协议、端口、PID。</summary>
        public static List<PRC> GetAllProcesses()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo() {
                FileName = "netstat.exe",
                Arguments = "-a -n -o",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (Process process = new Process() { StartInfo = startInfo }) {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                if (process.ExitCode != 0)
                    throw new Exception("somethign broke");

                List<PRC> result = new List<PRC>();
                foreach (string line in Regex.Split(output, "\r\n")) {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length <= 2)
                        continue;

                    if (!int.TryParse(parts[1].Split(':').Last(), out int port))
                        continue;

                    if (!int.TryParse(parts[parts.Length - 1], out int pid))
                        continue;

                    result.Add(new PRC() {
                        Protocol = parts[0],
                        Port = port,
                        PID = pid,
                    });
                }

                return result;
            }
        }
    }
}
