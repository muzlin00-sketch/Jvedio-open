using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Jvedio.Common
{
    /// <summary>
    /// 可绑定到 WPF 的字符串包装。取代 <c>SuperUtils.WPF.Entity.ObservableString</c>。
    /// <para>
    /// 用于把 <c>ObservableCollection&lt;string&gt;</c> 换成
    /// <c>ObservableCollection&lt;ObservableString&gt;</c> 后仍能在编辑时刷新界面——
    /// 集合只通知增删，不通知"某项内容变了"，所以需要一个自带 <see cref="INotifyPropertyChanged"/> 的字符串壳。
    /// </para>
    /// </summary>
    public class ObservableString : INotifyPropertyChanged
    {
        private string _value;

        public ObservableString(string value)
        {
            _value = value;
        }

        /// <summary>实际字符串内容，赋值时触发变更通知。</summary>
        public string Value {
            get { return _value; }
            set {
                if (_value == value)
                    return;
                _value = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>按内容比较，便于在集合/字典里查重。</summary>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
                return true;

            ObservableString other = obj as ObservableString;
            if (other == null)
                return false;

            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        /// <summary>与 <see cref="Value"/> 的哈希一致，因此可以和裸字符串混用同一份字典。</summary>
        public override int GetHashCode()
        {
            return _value == null ? 0 : _value.GetHashCode();
        }

        public override string ToString()
        {
            return _value ?? string.Empty;
        }
    }
}
