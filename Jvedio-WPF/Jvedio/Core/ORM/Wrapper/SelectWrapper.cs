using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Jvedio.Core.ORM.Entity;
using Jvedio.Core.ORM.Enums;
using Jvedio.Core.ORM.Exceptions;
using Jvedio.Core.ORM.Utils;

namespace Jvedio.Core.ORM.Wrapper
{
    /// <summary>
    /// SELECT 条件构造器，与原 <c>SuperUtils.Framework.ORM.Wrapper.SelectWrapper&lt;T&gt;</c> 一致。
    /// <para>
    /// 输出格式是历史包袱，逐字符被外面拼进 SQL，不能"顺手美化"：
    /// <list type="bullet">
    /// <item><c>ToWhere(false)</c> → <c>" where  a = '1' and b = '2' "</c>（where 后两个空格）；</item>
    /// <item>取值一律单引号包裹且<b>不</b>转义（数字也是 <c>'42'</c>，null 变成 <c>''</c>）；</item>
    /// <item><c>Like</c> 自动补 <c>%</c>；<c>NotIn</c> 是 <c>" NOT  in "</c>（双空格）；</item>
    /// <item>排序只支持<b>一个</b>字段，后一次 <c>Asc</c>/<c>Desc</c> 覆盖前一次；</item>
    /// <item><c>Limit(row_count)</c> 这个重载在原件里直接抛 <see cref="NotImplementedException"/>，
    ///       只有 <c>Limit(offset, row_count)</c> 可用。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <c>ToWhere()</c> / <c>ToSelect()</c> 默认按 <see cref="existFields"/> 过滤：
    /// 该列表为空时退化成按实体 T 的属性名过滤，因此 <c>T=object</c> 时会得到空条件 / <c>SELECT * </c>。
    /// </para>
    /// </summary>
    public class SelectWrapper<T> : IWrapper<T>
    {
        /// <summary>查询列，未指定时为 <c>*</c>。</summary>
        public HashSet<string> SelectColumns { get; set; }

        /// <summary>LIMIT 子句的参数部分（<c>offset,count</c>）。</summary>
        public string LimitSql { get; set; }

        /// <summary>条件列表。</summary>
        public List<Where> Wheres { get; set; }

        /// <summary>排序字段，只会有一个。</summary>
        public string OrderField { get; set; }

        /// <summary>排序是否降序。</summary>
        public bool SortDesc { get; set; }

        /// <summary>附加 SQL，由 Mapper 拼接。</summary>
        public string ExtraSql { get; set; }

        /// <summary>
        /// 实际存在（表里确实有对应列）的实体属性。
        /// <para>由 <see cref="Join"/> 按条件字段回填，可能包含 <c>null</c>（字段名不是实体属性）。</para>
        /// </summary>
        public List<PropertyInfo> existFields;

        private bool pendingOr;

        public SelectWrapper()
        {
            Init();
        }

        /// <summary>重置所有状态。</summary>
        public void Init()
        {
            SelectColumns = new HashSet<string>();
            Wheres = new List<Where>();
            existFields = new List<PropertyInfo>();
            OrderField = string.Empty;
            LimitSql = string.Empty;
            ExtraSql = string.Empty;
            SortDesc = false;
            pendingOr = false;
        }

        #region 条件

        private IWrapper<T> AddWhere(string field, WhereCondition condition, params object[] values)
        {
            Where where = new Where() {
                Field = field,
                Condition = condition,
                Or = pendingOr,
            };
            if (values != null)
                where.Values.AddRange(values);

            Wheres.Add(where);
            pendingOr = false;
            return this;
        }

        public IWrapper<T> Eq(string field, object value) => AddWhere(field, WhereCondition.Equal, value);

        public IWrapper<T> NotEq(string field, object value) => AddWhere(field, WhereCondition.NotEqual, value);

        public IWrapper<T> Gt(string field, object value) => AddWhere(field, WhereCondition.GreaterThen, value);

        public IWrapper<T> Ge(string field, object value) => AddWhere(field, WhereCondition.GreaterEqual, value);

        public IWrapper<T> Lt(string field, object value) => AddWhere(field, WhereCondition.LessThen, value);

        public IWrapper<T> Le(string field, object value) => AddWhere(field, WhereCondition.LessEqual, value);

        public IWrapper<T> Like(string field, object value) => AddWhere(field, WhereCondition.Like, value);

        public IWrapper<T> IsNull(string field) => AddWhere(field, WhereCondition.IS);

        public IWrapper<T> NotNull(string field) => AddWhere(field, WhereCondition.ISNot);

        public IWrapper<T> Between(string field, object value1, object value2) =>
            AddWhere(field, WhereCondition.Between, value1, value2);

        public IWrapper<T> In(string field, IEnumerable<string> items)
        {
            if (items == null || !items.Any())
                return this;

            Where where = new Where() {
                Field = field,
                Condition = WhereCondition.Equal,
                Or = pendingOr,
            };
            foreach (string item in items)
                where.Values.Add(item);

            Wheres.Add(where);
            pendingOr = false;
            return this;
        }

        public IWrapper<T> NotIn(string field, IEnumerable<string> items)
        {
            if (items == null || !items.Any())
                return this;

            Where where = new Where() {
                Field = field,
                Condition = WhereCondition.NotEqual,
                Or = pendingOr,
            };
            foreach (string item in items)
                where.Values.Add(item);

            Wheres.Add(where);
            pendingOr = false;
            return this;
        }

        public IWrapper<T> Or()
        {
            pendingOr = true;
            return this;
        }

        public IWrapper<T> LeftBracket()
        {
            if (Wheres.Count == 0)
                throw new WhereConditionException();

            Wheres[Wheres.Count - 1].LeftBracket = true;
            return this;
        }

        public IWrapper<T> RightBracket()
        {
            if (Wheres.Count == 0)
                throw new WhereConditionException();

            Wheres[Wheres.Count - 1].RightBracket = true;
            return this;
        }

        #endregion

        #region 列 / 排序 / 分页

        public IWrapper<T> Select(params string[] columns)
        {
            if (columns == null || columns.Length == 0)
                return this;

            foreach (string column in columns)
                SelectColumns.Add(column);

            return this;
        }

        public IWrapper<T> GroupBy(string column)
        {
            throw new NotImplementedException();
        }

        public IWrapper<T> Asc(string field)
        {
            OrderField = field;
            SortDesc = false;
            return this;
        }

        public IWrapper<T> Desc(string field)
        {
            OrderField = field;
            SortDesc = true;
            return this;
        }

        public IWrapper<T> Limit(long offset, long row_count)
        {
            LimitSql = offset + "," + row_count;
            return this;
        }

        public IWrapper<T> Limit(long row_count)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region 输出

        /// <summary>
        /// 合并另一个 wrapper 的条件（用于列表页把多个筛选器叠在一起）。
        /// <para>只合并条件与 <see cref="existFields"/>，列、排序、分页保持自己的。</para>
        /// </summary>
        public void Join(SelectWrapper<T> wrapper)
        {
            if (wrapper == null)
                return;

            if (wrapper.Wheres != null && wrapper.Wheres.Count > 0) {
                foreach (Where where in wrapper.Wheres)
                    Wheres.Add(where);
            }

            if (existFields == null)
                existFields = new List<PropertyInfo>();

            foreach (Where where in wrapper.Wheres ?? new List<Where>())
                existFields.Add(typeof(T).GetProperty(where.Field));
        }

        private IEnumerable<string> GetExistFieldNames()
        {
            if (existFields != null && existFields.Count > 0)
                return existFields.Select(p => p.Name);

            return typeof(T).GetProperties().Select(p => p.Name);
        }

        private bool FieldExist(string field, out IEnumerable<string> names)
        {
            names = GetExistFieldNames();
            return names.Contains(field);
        }

        public string ToSelect(bool existField = true)
        {
            if (SelectColumns == null || SelectColumns.Count == 0)
                return "SELECT * ";

            IEnumerable<string> columns = SelectColumns;
            if (existField) {
                List<string> names = GetExistFieldNames().ToList();
                columns = SelectColumns.Where(c => names.Contains(c));
            }

            string joined = string.Join(",", columns);
            if (string.IsNullOrEmpty(joined))
                return "SELECT * ";

            return "SELECT " + joined;
        }

        public string ToWhere(bool exist = true)
        {
            if (Wheres == null || Wheres.Count == 0)
                return string.Empty;

            List<Where> wheres = Wheres;
            if (exist) {
                List<string> names = GetExistFieldNames().ToList();
                wheres = Wheres.Where(w => names.Contains(w.Field)).ToList();
            }

            if (wheres.Count == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append(" where ");
            for (int i = 0; i < wheres.Count; i++) {
                Where where = wheres[i];
                // 连接符自带前导空格，因此条件之间只有一个空格；只有最后一项才补尾空格
                sb.Append(i == 0 ? " " : (where.Or ? " or " : " and "));
                if (where.LeftBracket)
                    sb.Append("(  ");

                sb.Append(ToCondition(where));
                if (where.RightBracket)
                    sb.Append(" ) ");

                if (i == wheres.Count - 1)
                    sb.Append(" ");
            }

            return sb.ToString();
        }

        public string ToOrder()
        {
            if (string.IsNullOrEmpty(OrderField))
                return string.Empty;

            return " ORDER BY " + OrderField + (SortDesc ? " DESC" : " ASC");
        }

        public string ToLimit()
        {
            if (string.IsNullOrEmpty(LimitSql))
                return string.Empty;

            return " Limit " + LimitSql;
        }

        #endregion

        #region 条件文本

        private static string Quote(object value)
        {
            return "'" + (value == null ? string.Empty : value.ToString()) + "'";
        }

        private static string ToCondition(Where where)
        {
            string field = where.Field ?? string.Empty;
            object value = where.Values != null && where.Values.Count > 0 ? where.Values[0] : null;

            switch (where.Condition) {
                case WhereCondition.Equal:
                    if (where.Values != null && where.Values.Count > 1) {
                        // In：多值（不是等值，原实现借用 Equal 表示 IN）
                        return field + " in (" + string.Join(",", where.Values.Select(Quote)) + ")";
                    }

                    return field + " = " + Quote(value);

                case WhereCondition.NotEqual:
                    if (where.Values != null && where.Values.Count > 1) {
                        // NotIn：注意原实现是 "NOT  in"（双空格）
                        return field + " NOT  in (" + string.Join(",", where.Values.Select(Quote)) + ")";
                    }

                    return field + " != " + Quote(value);

                case WhereCondition.GreaterThen:
                    return field + " > " + Quote(value);

                case WhereCondition.GreaterEqual:
                    return field + " >= " + Quote(value);

                case WhereCondition.LessThen:
                    return field + " < " + Quote(value);

                case WhereCondition.LessEqual:
                    return field + " <= " + Quote(value);

                case WhereCondition.Like:
                    return field + " like '%" + (value == null ? string.Empty : value.ToString()) + "%'";

                case WhereCondition.NotLike:
                    return field + " NOT  like '%" + (value == null ? string.Empty : value.ToString()) + "%'";

                case WhereCondition.IS:
                    return field + " IS NULL";

                case WhereCondition.ISNot:
                    return field + " IS NOT NULL";

                case WhereCondition.Between:
                    object first = where.Values != null && where.Values.Count > 0 ? where.Values[0] : null;
                    object second = where.Values != null && where.Values.Count > 1 ? where.Values[1] : null;
                    return field + " BETWEEN " + Quote(first) + " AND " + Quote(second);

                default:
                    throw new WhereConditionException();
            }
        }

        #endregion
    }
}
