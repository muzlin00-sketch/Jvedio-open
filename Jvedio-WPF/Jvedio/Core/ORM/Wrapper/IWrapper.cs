using System;
using System.Collections.Generic;

namespace Jvedio.Core.ORM.Wrapper
{
    /// <summary>
    /// 查询条件包装器，与原 <c>SuperUtils.Framework.ORM.Wrapper.IWrapper&lt;T&gt;</c> 一致。
    /// </summary>
    public interface IWrapper<T>
    {
        IWrapper<T> Select(params string[] columns);

        IWrapper<T> Eq(string field, object value);

        IWrapper<T> NotEq(string field, object value);

        IWrapper<T> Gt(string field, object value);

        IWrapper<T> Ge(string field, object value);

        IWrapper<T> Lt(string field, object value);

        IWrapper<T> Le(string field, object value);

        IWrapper<T> Like(string field, object value);

        IWrapper<T> IsNull(string field);

        IWrapper<T> NotNull(string field);

        IWrapper<T> Or();

        IWrapper<T> LeftBracket();

        IWrapper<T> RightBracket();

        IWrapper<T> GroupBy(string column);

        IWrapper<T> Desc(string field);

        IWrapper<T> Asc(string field);

        IWrapper<T> Limit(long offset, long row_count);

        IWrapper<T> Limit(long row_count);

        IWrapper<T> In(string field, IEnumerable<string> items);

        IWrapper<T> NotIn(string field, IEnumerable<string> items);

        IWrapper<T> Between(string field, object value1, object value2);

        string ToSelect(bool existField = true);

        string ToWhere(bool existField = true);

        string ToOrder();
    }
}
