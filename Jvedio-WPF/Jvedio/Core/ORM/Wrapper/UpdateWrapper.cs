using System;
using System.Collections.Generic;

namespace Jvedio.Core.ORM.Wrapper
{
    /// <summary>
    /// UPDATE 用的条件构造器，与原 <c>SuperUtils.Framework.ORM.Wrapper.UpdateWrapper&lt;T&gt;</c> 一致。
    /// <para>在 <see cref="SelectWrapper{T}"/> 之上多一个"要改的字段字典"，条件部分完全复用。</para>
    /// </summary>
    public class UpdateWrapper<T> : SelectWrapper<T>
    {
        /// <summary>字段名 → 新值。</summary>
        public Dictionary<string, object> UpdateDict { get; set; }

        public UpdateWrapper()
        {
            UpdateDict = new Dictionary<string, object>();
        }

        /// <summary>
        /// 设置一个待更新字段。
        /// <para>
        /// 同名字段<b>重复</b>设置会抛 <see cref="ArgumentException"/>（原实现用 <c>Dictionary.Add</c>），
        /// 调用方需要先自行判重。
        /// </para>
        /// </summary>
        public IWrapper<T> update(string field, object value)
        {
            if (UpdateDict == null)
                UpdateDict = new Dictionary<string, object>();

            UpdateDict.Add(field, value);
            return this;
        }
    }
}
