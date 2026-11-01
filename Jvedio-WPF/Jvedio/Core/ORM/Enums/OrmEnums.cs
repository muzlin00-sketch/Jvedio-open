using System;
using System.Collections.Generic;

namespace Jvedio.Core.ORM.Enums
{
    /// <summary>主键生成方式，与原 <c>SuperUtils.Framework.ORM.Enums.IdType</c> 一致。</summary>
    public enum IdType
    {
        AUTO = 0,
        NONE = 1,
        INPUT = 2,
        ID_WORKER = 3,
        UUID = 4,
        ID_WORKER_STR = 5,
    }

    /// <summary>插入模式，与原 <c>SuperUtils.Framework.ORM.Enums.InsertMode</c> 一致。</summary>
    public enum InsertMode
    {
        Normal = 0,
        Ignore = 1,
        Update = 2,
        Replace = 3,
    }

    /// <summary>
    /// WHERE 条件的比较运算符，与原 <c>SuperUtils.Framework.ORM.Enums.WhereCondition</c> 一致。
    /// <para>注意拼写：是 <c>GreaterThen</c> / <c>LessThen</c>（不是 Than），改名会破坏序列化与调用方。</para>
    /// </summary>
    public enum WhereCondition
    {
        Equal = 0,
        GreaterThen = 1,
        GreaterEqual = 2,
        LessThen = 3,
        LessEqual = 4,
        Like = 5,
        NotLike = 6,
        NotEqual = 7,
        Between = 8,
        IS = 9,
        ISNot = 10,
    }
}
