using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Jvedio.Core.WPF
{
    /// <summary>
    /// 视图模型基类，取代 <c>SuperUtils.WPF.VieModel.ViewModelBase</c>。
    /// <para>
    /// <see cref="Init"/> 是抽象方法：所有派生类都必须实现它（现有 9 个视图模型都已有
    /// <c>public override void Init()</c>），调用方靠基类引用统一触发初始化。
    /// </para>
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>初始化视图模型。由窗口在数据绑定前调用。</summary>
        public abstract void Init();

        /// <summary>
        /// 触发属性变更通知。参数默认取调用方成员名，因此 <c>RaisePropertyChanged()</c>
        /// 不带参数时也能正确指向属性。
        /// </summary>
        public void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
