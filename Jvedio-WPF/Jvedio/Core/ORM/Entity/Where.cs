using System;
using System.Collections.Generic;
using Jvedio.Core.ORM.Enums;

namespace Jvedio.Core.ORM.Entity
{
    /// <summary>
    /// 一个 WHERE 条件，与原 <c>SuperUtils.Framework.ORM.Entity.Where</c> 一致。
    /// <para>
    /// 字段是数据载体（Mapper 与 Wrapper 共享），<see cref="Equals"/> 按
    /// 「字段 + 运算符 + 取值」比较，用于 <c>existFields</c> 去重。
    /// </para>
    /// </summary>
    public class Where
    {
        /// <summary>与前一个条件之间用 OR 连接（默认 AND）。</summary>
        public bool Or { get; set; }

        /// <summary>是否在本条件左侧输出左括号。</summary>
        public bool LeftBracket { get; set; }

        /// <summary>是否在本条件右侧输出右括号。</summary>
        public bool RightBracket { get; set; }

        /// <summary>取值是否加引号（原实现保留，默认行为由 Wrapper 决定）。</summary>
        public bool AddQuote { get; set; }

        /// <summary>字段名，可以是 <c>表名.列名</c>。</summary>
        public string Field { get; set; }

        /// <summary>取值；<c>In</c> 为多值，<c>Between</c> 为两个值。</summary>
        public List<object> Values { get; set; }

        /// <summary>比较运算符。</summary>
        public WhereCondition Condition { get; set; }

        public Where()
        {
            Values = new List<object>();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Where other))
                return false;
            if (!string.Equals(Field, other.Field, StringComparison.Ordinal))
                return false;
            if (Condition != other.Condition)
                return false;
            if (Or != other.Or || LeftBracket != other.LeftBracket || RightBracket != other.RightBracket)
                return false;
            if (Values == null || other.Values == null)
                return Values == other.Values;

            if (Values.Count != other.Values.Count)
                return false;

            for (int i = 0; i < Values.Count; i++) {
                if (!Equals(Values[i], other.Values[i]))
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            unchecked {
                int hash = 17;
                hash = hash * 31 + (Field == null ? 0 : Field.GetHashCode());
                hash = hash * 31 + (int)Condition;
                return hash;
            }
        }
    }
}
