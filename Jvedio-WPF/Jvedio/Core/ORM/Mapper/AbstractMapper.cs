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

            /// <summary>写路径列：本类声明的属性（不含主键、含子表共享外键），用于 INSERT / UPDATE 拼 SQL。</summary>
            public PropertyInfo[] Columns;

            /// <summary>读路径列：结果集 → 实体映射用的属性（含继承与主键）。</summary>
            public PropertyInfo[] SelectColumns;
        }

        private static readonly Dictionary<Type, EntityMeta> metaCache = new Dictionary<Type, EntityMeta>();

        /// <summary>只取本类声明的成员。</summary>
        private static readonly BindingFlags DeclaredFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>本类 + 继承链上的成员。</summary>
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.Instance;

        /// <summary>解析实体元数据（带缓存）。</summary>
        protected static EntityMeta GetMeta()
        {
            Type type = typeof(T);
            if (metaCache.TryGetValue(type, out EntityMeta cached))
                return cached;

            TableAttribute table = type.GetCustomAttribute<TableAttribute>();
            if (table == null || string.IsNullOrEmpty(table.TableName))
                throw new TableNameNullException();

            PropertyInfo idProperty = FindIdProperty(type);
            EntityMeta meta = new EntityMeta() {
                TableName = table.TableName,
                IdProperty = idProperty,
                IdType = idProperty == null ? IdType.NONE : idProperty.GetCustomAttribute<TableIdAttribute>().type,
                Columns = BuildWriteColumns(type, idProperty),
                SelectColumns = BuildSelectColumns(type),
            };
            metaCache[type] = meta;
            return meta;
        }

        /// <summary>主键：以本类声明为准，本类没声明才退回继承链。</summary>
        private static PropertyInfo FindIdProperty(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties(DeclaredFlags)) {
                if (property.GetCustomAttribute<TableIdAttribute>() != null)
                    return property;
            }
            foreach (PropertyInfo property in type.GetProperties(InstanceFlags)) {
                if (property.GetCustomAttribute<TableIdAttribute>() != null)
                    return property;
            }
            return null;
        }

        /// <summary>
        /// 写路径列（INSERT / UPDATE）：**只取本类声明的属性** + 子表共享外键。
        /// <para>
        /// 表结构由标了 <c>[Table]</c> 的类自己声明的属性定义，继承来的属性属于基类那张表。
        /// 改造前这里取的是全量属性（含继承），于是 <c>Video : MetaData</c> 拼出的 INSERT 变成
        /// <c>insert into metadata_video (DBId,Title,Size,...)</c>，而 metadata_video 没有这些列，
        /// 整条语句报 <c>table metadata_video has no column named DBId</c>（UPDATE 同理，会更新不存在的列）。
        /// </para>
        /// <para>
        /// 反证同一约定：Video / Picture / Game 都用 <c>public new long DataID</c> 重新声明了外键列，
        /// 说明子表本来就该自己声明自己的列；没重新声明的（Comic）由 <see cref="FindSharedForeignKey"/> 补。
        /// </para>
        /// </summary>
        private static PropertyInfo[] BuildWriteColumns(Type type, PropertyInfo idProperty)
        {
            List<PropertyInfo> candidates = new List<PropertyInfo>(type.GetProperties(DeclaredFlags));

            PropertyInfo sharedKey = FindSharedForeignKey(type, candidates);
            if (sharedKey != null)
                candidates.Insert(0, sharedKey);

            List<PropertyInfo> columns = new List<PropertyInfo>();
            foreach (PropertyInfo property in candidates) {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                // 主键不写进 INSERT / UPDATE，交给自增
                if (idProperty != null && property.Name == idProperty.Name)
                    continue;

                TableFieldAttribute field = property.GetCustomAttribute<TableFieldAttribute>();
                if (field != null && !field.exist)
                    continue;

                columns.Add(property);
            }
            return columns.ToArray();
        }

        /// <summary>
        /// 读路径列（结果集 → 实体）：沿用改造前的全量属性（含继承）。
        /// <para>
        /// 列表 / 详情页的 SQL 是 <c>metadata join metadata_video</c> 这种显式列查询，
        /// 结果字典里可能带 <c>metadata.*</c> 字段，映射时要按属性名灌回实体；
        /// 这里不做收窄，避免列表漏字段。
        /// </para>
        /// </summary>
        private static PropertyInfo[] BuildSelectColumns(Type type)
        {
            List<PropertyInfo> selectColumns = new List<PropertyInfo>();
            foreach (PropertyInfo property in type.GetProperties(InstanceFlags)) {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                TableFieldAttribute field = property.GetCustomAttribute<TableFieldAttribute>();
                if (field != null && (!field.exist || !field.select))
                    continue;

                selectColumns.Add(property);
            }
            return selectColumns.ToArray();
        }

        /// <summary>
        /// 找出「子表共享外键」：所有 metadata_* 子表都用 metadata.DataID 指向主表。
        /// <para>
        /// Video / Picture / Game 都重新声明了这一列，只有 <c>Comic : Picture</c> 没有；
        /// 严格「只取本类声明」会让 metadata_comic 的 INSERT 丢掉 DataID，写出孤儿行。
        /// 规则：取继承链最上层 <c>[Table]</c> 类的主键名，本类没声明同名列时用最近的继承声明。
        /// </para>
        /// </summary>
        private static PropertyInfo FindSharedForeignKey(Type type, List<PropertyInfo> declared)
        {
            Type rootTable = null;
            for (Type baseType = type.BaseType; baseType != null && baseType != typeof(object); baseType = baseType.BaseType) {
                if (baseType.GetCustomAttribute<TableAttribute>() != null)
                    rootTable = baseType;
            }
            if (rootTable == null)
                return null;

            PropertyInfo rootId = null;
            foreach (PropertyInfo property in rootTable.GetProperties(DeclaredFlags)) {
                if (property.GetCustomAttribute<TableIdAttribute>() != null) {
                    rootId = property;
                    break;
                }
            }
            if (rootId == null)
                return null;

            if (declared.Any(p => p.Name == rootId.Name))
                return null;

            for (Type baseType = type.BaseType; baseType != null && baseType != typeof(object); baseType = baseType.BaseType) {
                foreach (PropertyInfo property in baseType.GetProperties(DeclaredFlags)) {
                    if (property.Name == rootId.Name && property.CanRead && property.CanWrite)
                        return property;
                }

                // 越过最近一张自有表就不再往上找，避免把更上层表的列也带进来
                if (baseType.GetCustomAttribute<TableAttribute>() != null)
                    break;
            }
            return null;
        }

        /// <summary>实体对应的表名。</summary>
        protected static string TableName => GetMeta().TableName;

        /// <summary>主键属性名（实体没有标注时为 null）。</summary>
        protected static string IdFieldName => GetMeta().IdProperty?.Name;

        /// <summary>
        /// 把对象转成 SQL 里的字面量：bool → 1/0，枚举 → 整数（不加引号），null → <c>''</c>，
        /// 其余加单引号并转义。
        /// <para>
        /// 枚举必须落成整数：库里的 <c>metadata.DataType</c>、<c>metadata_video.VideoType</c>、
        /// <c>actor_info.Gender</c> 都是 INT，契约本来就是整数。改造前走
        /// <see cref="SqlStringFormat.Format"/> 的 <c>ToString()</c>，DataType 会被写成文本
        /// <c>'Video'</c>，列表查询条件 <c>metadata.DataType=0</c> 就查不到这些行。
        /// </para>
        /// </summary>
        protected static string Quote(object value)
        {
            if (value == null)
                return "''";

            if (value is bool flag)
                return flag ? "1" : "0";

            if (value is Enum)
                return Convert.ToInt64(value).ToString();

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

        /// <summary>
        /// 执行一条写语句，返回受影响行数。
        /// <para>
        /// <b>契约：失败必须抛异常。</b>不允许 catch 之后返回 <c>-1</c> / <c>0</c> 之类
        /// 「看起来像受影响行数」的值——调用方无从区分「SQL 跑成功了但没命中行」和
        /// 「SQL 直接失败」，界面就会把失败报成成功（2026-11-11「已导入 11 条但一条没进库」）。
        /// </para>
        /// <para>
        /// 确实允许失败的调用点用 <see cref="TryExecuteNonQuery(string)"/>，让它显式返回 bool。
        /// </para>
        /// </summary>
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

        /// <summary>
        /// 批量插入，逐条执行并回写每条的 id。
        /// <para>
        /// 回写 id 必须和插入在同一次独占里完成（见 <see cref="InsertAndWriteBackId"/>），
        /// 否则共享连接上 <c>last_insert_rowid()</c> 会被别的线程插队改掉，id 写到错误的实体上。
        /// </para>
        /// </summary>
        public int InsertBatch(ICollection<T> collections, InsertMode mode = InsertMode.Replace)
        {
            if (collections == null || collections.Count == 0)
                return 0;

            int total = 0;
            foreach (T entity in collections)
                total += InsertAndWriteBackId(entity, mode);
            return total;
        }

        /// <summary>
        /// 插入一条，并把新主键写回实体，返回受影响行数。
        /// <para>
        /// 默认实现是两步（先 INSERT，再 <see cref="SelectLastInsertRowId"/>），在共享连接上
        /// <b>不是原子的</b>：中间若另一线程往同一连接插了别的行，读到的 rowid 是别人的。
        /// <see cref="BaseMapper{T}"/> 覆盖本方法，把「插入 + 读 rowid」放进同一个写锁内。
        /// </para>
        /// </summary>
        protected virtual int InsertAndWriteBackId(T entity, InsertMode mode)
        {
            int rows = Insert(entity, mode);
            if (rows > 0)
                WriteBackId(entity, SelectLastInsertRowId());
            return rows;
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

        /// <summary>
        /// 执行写语句，失败时记录日志并返回 false（不抛异常）。
        /// <para>
        /// <b>只给「允许失败」的调用点用</b>，判断标准有两条，缺一不可：
        /// <list type="number">
        /// <item>语句本身可以失败而业务仍然正确，例如幂等 DDL（建表 / 补列）、
        /// 事务边界语句（<c>BEGIN</c>/<c>END TRANSACTION</c>）、统计与清理类写入；</item>
        /// <item>失败后调用方<b>仍能如实告诉用户结果</b>，且不会留下需要回滚的业务状态。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 业务写路径（导入、编辑保存、删除）一律用 <see cref="ExecuteNonQuery"/> 让异常冒泡。
        /// 更不允许写成 <c>try { ... } catch { }</c> 这种既不抛、也不返回状态的形态。
        /// </para>
        /// </summary>
        /// <returns>执行成功返回 true；失败返回 false（已写日志）。</returns>
        public bool TryExecuteNonQuery(string sql)
        {
            try {
                ExecuteNonQuery(sql);
                return true;
            } catch (Exception ex) {
                // Logger 在 App 早期可能还是 null，用 ?. 避免「记录日志本身把异常换掉」
                Jvedio.App.Logger?.Error(ex);
                return false;
            }
        }

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
