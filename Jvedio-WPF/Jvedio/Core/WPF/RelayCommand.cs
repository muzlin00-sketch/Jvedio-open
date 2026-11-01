using System;
using System.Windows.Input;

namespace Jvedio.Core.WPF
{
    /// <summary>
    /// 带类型参数的命令，取代 <c>SuperUtils.WPF.VieModel.RelayCommand&lt;T&gt;</c>。
    /// <para>
    /// <see cref="CanExecuteChanged"/> 挂到 <see cref="CommandManager.RequerySuggested"/> 上，
    /// 这样界面交互后按钮的可用状态会自动刷新，不需要手工通知。
    /// </para>
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _action;
        private readonly Func<bool> _canExecute;

        /// <param name="action">执行体。</param>
        /// <param name="canExecute">
        /// 判据；为 null 表示始终可执行。<b>它是无参的</b>（<c>Func&lt;bool&gt;</c>），
        /// 拿不到命令参数——原实现就是这样的签名，别改成 <c>Func&lt;T, bool&gt;</c>。
        /// </param>
        public RelayCommand(Action<T> action, Func<bool> canExecute = null)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _action?.Invoke((T)parameter);
        }

        public event EventHandler CanExecuteChanged {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
