using System;

namespace Jvedio.Core.ORM.Exceptions
{
    /// <summary>实体没有无参构造函数，与原 <c>SuperUtils.Framework.ORM.Exceptions.NoArgumentConstructorException</c> 一致。</summary>
    public class NoArgumentConstructorException : Exception
    {
        public NoArgumentConstructorException()
            : base("The current entity has no argument-free constructor")
        {
        }
    }

    /// <summary>主键类型不受支持，与原 <c>SuperUtils.Framework.ORM.Exceptions.PrimaryKeyTypeException</c> 一致。</summary>
    public class PrimaryKeyTypeException : Exception
    {
        public PrimaryKeyTypeException()
            : base("Unsupported primary key type")
        {
        }
    }

    /// <summary>实体缺少 <c>[Table]</c> 标注，与原 <c>SuperUtils.Framework.ORM.Exceptions.TableNameNullException</c> 一致。</summary>
    public class TableNameNullException : Exception
    {
        public TableNameNullException()
            : base("Table name is null, please add [Table] to the entity")
        {
        }
    }

    /// <summary>WHERE 条件非法，与原 <c>SuperUtils.Framework.ORM.Exceptions.WhereConditionException</c> 一致。</summary>
    public class WhereConditionException : Exception
    {
        public WhereConditionException()
            : base("Illegal where condition")
        {
        }
    }
}
