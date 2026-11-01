using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Jvedio.Core.ORM.Attributes;
using Jvedio.Core.ORM.Enums;
using Jvedio.Core.ORM.Exceptions;
using Jvedio.Core.ORM.Utils;
using Jvedio.Core.ORM.Wrapper;

namespace Jvedio.Core.ORM.Mapper
{
    /// <summary>
    /// 实体 Mapper 基类：负责「实体 ↔ 表」的 SQL 拼装与通用增删改，
    /// 具体执行交给子类（SQLite / MySQL），取代 <c>SuperUtils.Framework.ORM.Mapper.AbstractMapper&lt;T&gt;</c>。
    /// </summary>
    public abstract class AbstractMapper<T> : IMapper<T>
    {
        /// <summary>写操作互斥用的静态锁对象。</summary>
        public static object WriteLock { get; set; } = new object();

        #region 实体元数据

        protected class EntityMeta
        {
            public string TableName;

            public PropertyInfo IdProperty;

            public IdType IdType;

            /// <summary>表里真实存在的属性（不含主键）。</summary>
            public PropertyInfo[] Columns;

            /// <summary>查询时允许取的属性（含主键）。</summary>
            public PropertyInfo[] SelectColumns;
        }

        private static readonly Dictionary<Type, EntityMeta> metaCache = new Dictionary<Type, EntityMeta>();

        /// <summary>解析实体元数据（带缓存）。</summary>
        protected static EntityMeta GetMeta()
        {
            Type type = typeof(T);
            if (metaCache.TryGetValue(type, out EntityMeta cached))
                return cached;

            TableAttribute table = type.GetCustomAttribute<TableAttribute>();
            if (table == null || string.IsNullOrEmpty(table.TableName))
                throw new TableNameNullException();

            EntityMeta meta = new EntityMeta() {
                TableName = table.TableName,
                IdType = IdType.NONE,
            };

            List<PropertyInfo> columns = new List<PropertyInfo>();
            List<PropertyInfo> selectColumns = new List<PropertyInfo>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                TableIdAttribute id = property.GetCustomAttribute<TableIdAttribute>();
                if (id != null) {
                    meta.IdProperty = property;
                    meta.IdType = id.type;
                }

                TableFieldAttribute field = property.GetCustomAttribute<TableFieldAttribute>();
                bool exist = field == null || field.exist;
                bool select = field == null || field.select;

                if (property == meta.IdProperty) {
                    // 主键始终可查，但写 INSERT 时不一定带
                    if (select)
                        selectColumns.Add(property);
                    continue;
                }

                if (exist) {
                    columns.Add(property);
                    if (select)
                        selectColumns.Add(property);
                }
            }

            meta.Columns = columns.ToArray();
            meta.SelectColumns = selectColumns.ToArray();
            metaCache[type] = meta;
            return meta;
        }

        /// <summary>实体对应的表名。</summary>
        protected static string TableName => GetMeta().TableName;

        /// <summary>主键属性名（实体没有标注时为 null）。</summary>
        protected static string IdFieldName => GetMeta().IdProperty?.Name;

        /// <summary>把对象转成 SQL 里的字面量：bool → 1/0，null → <c>''</c>，其余加单引号并转义。</summary>
        protected static string Quote(object value)
        {
            if (value == null)
                return "''";

            if (value is bool flag)
                return flag ? "1" : "0";

            return "'" + SqlStringFormat.Format(value) + "'";
        }

        /// <summary>同上，但 null 输出 <c>NULL</c>（InsertAndGetID 这条路径的写法）。</summary>
        protected static string QuoteOrNull(object value)
        {
            if (value == null)
                return "NULL";

            return Quote(value);
        }

        /// <summary>拼 <c>insert into 表 (列...) values (值...)</c>。</summary>
        protected static string BuildInsertSql(T entity, InsertMode mode, bool nullAsLiteral)
        {
            EntityMeta meta = GetMeta();
            List<string> names = new List<string>();
            List<string> values = new List<string>();
            foreach (PropertyInfo property in meta.Columns) {
                names.Add(property.Name);
                object value = property.GetValue(entity);
                values.Add(nullAsLiteral ? QuoteOrNull(value) : Quote(value));
            }

            string verb;
            switch (mode) {
                case InsertMode.Ignore:
                    verb = "INSERT OR IGNORE";
                    break;
                case InsertMode.Replace:
                    verb = "INSERT OR REPLACE";
                    break;
                case InsertMode.Update:
                    // 原实现就是这样拼的，SQLite 会报 near "UPDATE": syntax error
                    verb = "INSERT OR UPDATE";
                    break;
                default:
                    verb = "INSERT";
                    break;
            }

            if (names.Count == 0)
                return verb + " INTO " + meta.TableName + " DEFAULT VALUES";

            return verb + " INTO " + meta.TableName
                + " (" + string.Join(",", names) + ") values (" + string.Join(",", values) + ")";
        }

        /// <summary>拼 <c>update 表 set ... where 主键 = 值</c>。</summary>
        protected static string BuildUpdateSql(T entity, PropertyInfo[] updateColumns)
        {
            EntityMeta meta = GetMeta();
            if (meta.IdProperty == null)
                throw new PrimaryKeyTypeException();

            List<string> sets = new List<string>();
            foreach (PropertyInfo property in updateColumns)
                sets.Add(property.Name + " = " + Quote(property.GetValue(entity)));

            if (sets.Count == 0)
                return null;

            object id = meta.IdProperty.GetValue(entity);
            return "update " + meta.TableName + " set " + string.Join(",", sets)
                + " where " + meta.IdProperty.Name + " = " + Quote(id);
        }

        #endregion

        #region 抽象：由具体数据库实现

        public abstract string SelectLastInsertRowId();

        public abstract List<Dictionary<string, object>> Select(IWrapper<T> wrapper);

        public abstract List<Dictionary<string, object>> Select(string sql);

        public abstract List<T> SelectList(IWrapper<T> wrapper);

        public abstract T SelectById(IWrapper<T> wrapper);

        public abstract T SelectOne(IWrapper<T> wrapper);

        public abstract List<T> SelectByDict(Dictionary<string, object> dict, IWrapper<T> wrapper);

        public abstract long SelectCount(IWrapper<T> wrapper);

        public abstract long SelectCount(string sql);

        public abstract object InsertAndGetID(T entity);

        public abstract void Init();

        public abstract void Dispose();

        public abstract bool IsTableExists(string tableName);

        public abstract int ExecuteNonQuery(string sql);

        public abstract bool DeleteDataBase(string db_name);

        public abstract int Update(IWrapper<T> wrapper);

        #endregion

        #region 增

        /// <summary>插入（默认 Replace 语义），返回受影响行数。</summary>
        public int Insert(T entity, InsertMode mode = InsertMode.Replace)
        {
            if (entity == null)
                return 0;

            return ExecuteNonQuery(BuildInsertSql(entity, mode, false));
        }

        /// <summary>
        /// 插入并回写主键。
        /// <para>
        /// 与 <see cref="Insert(T, InsertMode)"/> 的差异是原实现就有的：
        /// 这条路径把 null 写成 <c>NULL</c>（而非空串），且会把 <c>last_insert_rowid</c> 写回实体的主键属性。
        /// </para>
        /// </summary>
        public bool Insert(T entity)
        {
            object id = InsertAndGetID(entity);
            return id != null;
        }

        /// <summary>批量插入，逐条执行并回写每条的 id。</summary>
        public int InsertBatch(ICollection<T> collections, InsertMode mode = InsertMode.Replace)
        {
            if (collections == null || collections.Count == 0)
                return 0;

            int total = 0;
            foreach (T entity in collections) {
                int rows = Insert(entity, mode);
                total += rows;
                if (rows > 0)
                    WriteBackId(entity, SelectLastInsertRowId());
            }
            return total;
        }

        /// <summary>把字符串形式的 id 写回实体主键属性。</summary>
        protected void WriteBackId(T entity, string idText)
        {
            EntityMeta meta = GetMeta();
            if (meta.IdProperty == null || entity == null || string.IsNullOrEmpty(idText))
                return;

            try {
                object converted = Convert.ChangeType(idText, meta.IdProperty.PropertyType);
                meta.IdProperty.SetValue(entity, converted);
            } catch (Exception) {
                // 主键类型不支持就不回写，不影响插入结果
            }
        }

        #endregion

        #region 改

        public virtual int Update(T entity)
        {
            if (entity == null)
                return 0;

            string sql = BuildUpdateSql(entity, GetMeta().Columns);
            return sql == null ? 0 : ExecuteNonQuery(sql);
        }

        public virtual int UpdateById(T entity)
        {
            return Update(entity);
        }

        /// <summary>批量更新指定字段。</summary>
        public int UpdateBatch(ICollection<T> collections, params string[] updateFields)
        {
            if (collections == null || collections.Count == 0 || updateFields == null || updateFields.Length == 0)
                return 0;

            EntityMeta meta = GetMeta();
            List<PropertyInfo> properties = new List<PropertyInfo>();
            foreach (string field in updateFields) {
                PropertyInfo property = typeof(T).GetProperty(field);
                if (property != null)
                    properties.Add(property);
            }

            int total = 0;
            foreach (T entity in collections) {
                string sql = BuildUpdateSql(entity, properties.ToArray());
                if (sql != null)
                    total += ExecuteNonQuery(sql);
            }
            return total;
        }

        /// <summary>按条件更新单个字段。</summary>
        public int UpdateField(string field, string value, IWrapper<T> wrapper = null)
        {
            if (string.IsNullOrEmpty(field))
                return 0;

            string sql = "update " + TableName + " set " + field + " = " + Quote(value);
            if (wrapper != null)
                sql += wrapper.ToWhere(false);

            return ExecuteNonQuery(sql);
        }

        /// <summary>按主键更新单个字段。</summary>
        public bool UpdateFieldById(string field, string value, object id)
        {
            string sql = "update " + TableName + " set " + field + " = " + Quote(value)
                + " where " + IdFieldName + " = " + Quote(id);
            return ExecuteNonQuery(sql) > 0;
        }

        /// <summary>按主键自增/自减某个数值字段。</summary>
        public bool IncreaseFieldById(string field, object id, int value = 1)
        {
            string sql = "update " + TableName + " set " + field + " = " + field + " + " + value
                + " where " + IdFieldName + " = " + Quote(id);
            return ExecuteNonQuery(sql) > 0;
        }

        #endregion

        #region 删

        /// <summary>删除：原实现在 AbstractMapper 里抛 <see cref="NotImplementedException"/>。</summary>
        public virtual int DeleteByDict(Dictionary<string, object> dict)
        {
            throw new NotImplementedException();
        }

        public int DeleteById(object id)
        {
            string sql = "delete from " + TableName + " where " + IdFieldName + " = " + Quote(id);
            return ExecuteNonQuery(sql);
        }

        public int DeleteByIds(List<string> idList)
        {
            if (idList == null || idList.Count == 0)
                return 0;

            string sql = "delete from " + TableName + " where " + IdFieldName
                + " in (" + string.Join(",", idList.Select(x => Quote(x))) + ")";
            return ExecuteNonQuery(sql);
        }

        #endregion

        #region 工具

        /// <summary>把字典结果集映射成实体列表。</summary>
        public List<V> ToEntity<V>(List<Dictionary<string, object>> list, PropertyInfo[] Properties, bool existField = true)
        {
            List<V> result = new List<V>();
            if (list == null || list.Count == 0)
                return result;

            foreach (Dictionary<string, object> dict in list) {
                V entity = Activator.CreateInstance<V>();
                foreach (PropertyInfo property in Properties) {
                    if (!dict.TryGetValue(property.Name, out object value) && !dict.TryGetValue(property.Name.ToLower(), out value))
                        continue;

                    // 库里的 NULL 映射到字符串属性时是空串而不是 null（原实现如此，界面据此判断"无值"）
                    if (value == null || value == DBNull.Value) {
                        if (property.PropertyType == typeof(string))
                            property.SetValue(entity, string.Empty);
                        continue;
                    }

                    try {
                        property.SetValue(entity, Convert.ChangeType(value, property.PropertyType));
                    } catch (Exception) {
                        // 单个字段转换失败不影响其它字段
                    }
                }
                result.Add(entity);
            }

            return result;
        }

        /// <summary>
        /// 建表：**表已存在时直接跳过**（启动时对每张表都会调一次，必须幂等）。
        /// <para>
        /// 注意只跳过"已存在"，不吞其它异常——脚本本身写错时要能立刻炸出来。
        /// </para>
        /// </summary>
        public void CreateTable(string tableName, string createTableSql)
        {
            if (string.IsNullOrEmpty(createTableSql))
                return;

            if (IsTableExists(tableName))
                return;

            ExecuteNonQuery(createTableSql);
        }

        #endregion
    }
}
