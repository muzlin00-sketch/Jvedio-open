using System;
using Jvedio.Core.ORM.Enums;

namespace Jvedio.Core.ORM.Attributes
{
    /// <summary>
    /// 标注实体对应的表名，与原 <c>SuperUtils.Framework.ORM.Attributes.TableAttribute</c> 一致。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class TableAttribute : Attribute
    {
        public string TableName;

        public TableAttribute(string tableName)
        {
            TableName = tableName;
        }
    }

    /// <summary>
    /// 标注实体字段在表中的存在性，与原 <c>SuperUtils.Framework.ORM.Attributes.TableFieldAttribute</c> 一致。
    /// <para>
    /// <c>exist=false</c> 的字段不参与建表与读写（用于"内存里要用、表里没有"的字段）；
    /// <c>select=false</c> 的字段查询时不取（用于大字段，避免拖慢列表查询）。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class TableFieldAttribute : Attribute
    {
        public bool exist;

        public bool select;

        public TableFieldAttribute(bool exist)
        {
            this.exist = exist;
            select = exist;
        }

        public TableFieldAttribute(bool exist, bool select)
        {
            this.exist = exist;
            this.select = select;
        }
    }

    /// <summary>
    /// 标注主键生成方式，与原 <c>SuperUtils.Framework.ORM.Attributes.TableIdAttribute</c> 一致。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class TableIdAttribute : Attribute
    {
        public IdType type;

        public TableIdAttribute(IdType type)
        {
            this.type = type;
        }
    }
}
