using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using Jvedio.Core.ORM.Attributes;
using Jvedio.Core.ORM.Enums;
using Jvedio.Core.ORM.Utils;
using Jvedio.Core.ORM.Wrapper;
using Jvedio.Core.Sql;

namespace Jvedio.Core.ORM.Mapper
{
    /// <summary>
    /// SQLite 版实体 Mapper，取代 <c>SuperUtils.Framework.ORM.Mapper.SqliteMapper&lt;T&gt;</c>。
    /// </summary>
    public class SqliteMapper<T> : AbstractMapper<T>
    {
        private readonly string sqlitePath;

        private Sqlite sqlite;

        private SQLiteConnection Connection => sqlite == null ? null : sqlite.cn;

        public SqliteMapper(string sqlitePath)
        {
            this.sqlitePath = sqlitePath;
            Init();
        }

        public override void Init()
        {
            if (sqlite != null)
                return;

            sqlite = new Sqlite(sqlitePath);
            sqlite.Open();
        }

        public override void Dispose()
        {
            if (sqlite == null)
                return;

            sqlite.Dispose();
            sqlite = null;
        }

        public override bool IsTableExists(string tableName)
        {
            return sqlite.IsTableExist(tableName);
        }

        public override int ExecuteNonQuery(string sql)
        {
            if (string.IsNullOrEmpty(sql))
                return 0;

            using (SQLiteCommand command = new SQLiteCommand(sql, Connection))
                return command.ExecuteNonQuery();
        }

        public override string SelectLastInsertRowId()
        {
            using (SQLiteCommand command = new SQLiteCommand("select last_insert_rowid()", Connection)) {
                object result = command.ExecuteScalar();
                return result == null ? null : result.ToString();
            }
        }

        public override List<Dictionary<string, object>> Select(string sql)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            if (string.IsNullOrEmpty(sql))
                return result;

            using (SQLiteCommand command = new SQLiteCommand(sql, Connection))
            using (SQLiteDataReader reader = command.ExecuteReader()) {
                while (reader.Read()) {
                    Dictionary<string, object> dict = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        dict[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(dict);
                }
            }
            return result;
        }

        public override List<Dictionary<string, object>> Select(IWrapper<T> wrapper)
        {
            return Select(BuildSelectSql(wrapper));
        }

        private static string BuildSelectSql(IWrapper<T> wrapper)
        {
            string select = wrapper == null ? "SELECT * " : wrapper.ToSelect(false);
            string where = wrapper == null ? string.Empty : wrapper.ToWhere(false);
            string order = wrapper == null ? string.Empty : wrapper.ToOrder();
            string limit = wrapper is SelectWrapper<T> selectWrapper ? selectWrapper.ToLimit() : string.Empty;
            return select + " from " + TableName + where + order + limit;
        }

        public override List<T> SelectList(IWrapper<T> wrapper = null)
        {
            List<Dictionary<string, object>> dicts = Select(BuildSelectSql(wrapper));
            return ToEntity<T>(dicts, GetMeta().SelectColumns);
        }

        public override T SelectOne(IWrapper<T> wrapper = null)
        {
            List<T> list = SelectList(wrapper);
            return list == null || list.Count == 0 ? default : list[0];
        }

        public override T SelectById(IWrapper<T> wrapper)
        {
            return SelectOne(wrapper);
        }

        public override List<T> SelectByDict(Dictionary<string, object> dict, IWrapper<T> wrapper)
        {
            string where = string.Empty;
            if (dict != null && dict.Count > 0) {
                List<string> conditions = new List<string>();
                foreach (KeyValuePair<string, object> kv in dict)
                    conditions.Add(kv.Key + " = " + Quote(kv.Value));
                where = " where " + string.Join(" and ", conditions);
            }

            if (wrapper != null)
                where += wrapper.ToWhere(false);

            string sql = "SELECT *  from " + TableName + where;
            return ToEntity<T>(Select(sql), GetMeta().SelectColumns);
        }

        public override long SelectCount(string sql)
        {
            if (string.IsNullOrEmpty(sql))
                return 0;

            using (SQLiteCommand command = new SQLiteCommand(sql, Connection)) {
                object result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }
        }

        public override long SelectCount(IWrapper<T> wrapper)
        {
            string where = wrapper == null ? string.Empty : wrapper.ToWhere(false);
            return SelectCount("select count(*) from " + TableName + where);
        }

        public override object InsertAndGetID(T entity)
        {
            if (entity == null)
                return null;

            ExecuteNonQuery(BuildInsertSql(entity, InsertMode.Normal, true));
            string id = SelectLastInsertRowId();
            WriteBackId(entity, id);
            if (string.IsNullOrEmpty(id))
                return null;

            return long.TryParse(id, out long parsed) ? (object)parsed : id;
        }

        public override int Update(IWrapper<T> wrapper)
        {
            if (!(wrapper is UpdateWrapper<T> updateWrapper) || updateWrapper.UpdateDict == null || updateWrapper.UpdateDict.Count == 0)
                return 0;

            List<string> sets = new List<string>();
            foreach (KeyValuePair<string, object> kv in updateWrapper.UpdateDict)
                sets.Add(kv.Key + " = " + Quote(kv.Value));

            string sql = "update " + TableName + " set " + string.Join(",", sets) + updateWrapper.ToWhere(false);
            return ExecuteNonQuery(sql);
        }

        public override bool DeleteDataBase(string db_name)
        {
            return true;
        }
    }
}
