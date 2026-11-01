
using Jvedio.Common;
using Jvedio.Core.WPF;
using System;

namespace Jvedio.Core.Command
{
    public static class File
    {
        public static RelayCommand<string> OpenBaseDir { get; set; }

        public static RelayCommand<string> OpenDir { get; set; }

        public static RelayCommand<string> OpenSelectFile { get; set; }

        static File()
        {
            OpenBaseDir = new RelayCommand<string>(dir => Jvedio.Common.FileHelper.TryOpenPath(AppDomain.CurrentDomain.BaseDirectory));
            OpenDir = new RelayCommand<string>(dir => Jvedio.Common.FileHelper.TryOpenPath(dir));
            OpenSelectFile = new RelayCommand<string>(path => Jvedio.Common.FileHelper.TryOpenSelectPath(path));
        }
    }
}
