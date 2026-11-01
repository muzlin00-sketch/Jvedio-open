using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Jvedio.Core.Sql
{
    /// <summary>
    /// SQLite 连接与表级操作的薄封装，取代 <c>SuperUtils.Sql.Sqlite</c>。
    /// <para>
    /// <b>构造函数只记路径，不打开连接</b>——原实现如此，连接由子类
    /// （<see cref="Jvedio.Entity.MySqlite"/>）或 Mapper 自己打开，
    /// 所以直接 <c>new Sqlite(path)</c> 后调本类方法会得到 false 或空引用异常，这是既有行为。
    /// </para>
    /// </summary>
    public class Sqlite : IDisposable
    {
        /// <summary>表名 → 建表语句，供启动时补建新表。</summary>
        public static Dictionary<string, string> Tables { get; set; } = new Dictionary<string, string>();

        public string SqlitePath { get; set; }

        /// <summary>连接对象（公开字段：子类 MySqlite 直接赋值）。</summary>
        public SQLiteConnection cn;

        /// <summary>复用的命令对象（公开字段：子类 MySqlite 直接赋值）。</summary>
        public SQLiteCommand cmd;

        public Sqlite(string path)
        {
            SqlitePath = path;
        }

        /// <summary>打开连接（Mapper 内部使用；子类一般自己开）。</summary>
        internal void Open()
        {
            if (cn != null)
                return;

            cn = new SQLiteConnection("data source=" + SqlitePath);
            cn.Open();
            cmd = new SQLiteCommand();
            cmd.Connection = cn;
        }

        public void InitTables(Dictionary<string, string> tables)
        {
            Tables = tables;
        }

        public bool IsTableExist(string table)
        {
            try {
                cmd.CommandText = "select count(*) from sqlite_master where type='table' and name='" + table + "'";
                object result = cmd.ExecuteScalar();
                return result != null && Convert.ToInt64(result) > 0;
            } catch (Exception) {
                return false;
            }
        }

        public List<string> GetAllTable()
        {
            List<string> tables = new List<string>();
            cmd.CommandText = "select name from sqlite_master where type='table' order by name";
            using (SQLiteDataReader reader = cmd.ExecuteReader()) {
                while (reader.Read())
                    tables.Add(reader.GetString(0));
            }
            return tables;
        }

        public bool ExecuteSql(string sqltext)
        {
            try {
                cmd.CommandText = sqltext;
                cmd.ExecuteNonQuery();
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>执行查询并返回读取器（版本升级代码按行读老库就靠它）。</summary>
        public SQLiteDataReader RunSql(string sqltext)
        {
            cmd.CommandText = sqltext;
            return cmd.ExecuteReader();
        }

        /// <summary>
        /// 统计表行数。<paramref name="sql"/> 非空时被当作 <paramref name="field"/> 的**等值**去筛选
        /// （不是拼接 WHERE 子句——原实现如此，传进去的字符串会被直接当值）。
        /// </summary>
        public double SelectCountByTable(string table, string field = "id", string sql = "")
        {
            string where = string.IsNullOrEmpty(sql) ? string.Empty : " where " + field + " = '" + sql + "'";
            cmd.CommandText = "select count(*) from " + table + where;
            object result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : Convert.ToDouble(result);
        }

        /// <summary>取单列单值（找不到返回空串）。</summary>
        public string SelectByField(string info, string table, string value, string field = "id")
        {
            cmd.CommandText = "select " + info + " from " + table + " where " + field + " = '" + value + "'";
            object result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? string.Empty : result.ToString();
        }

        public bool DeleteByField(string table, string filed, string value)
        {
            try {
                cmd.CommandText = "delete from " + table + " where " + filed + " = '" + value + "'";
                return cmd.ExecuteNonQuery() > 0;
            } catch (Exception) {
                return false;
            }
        }

        public bool DeleteTable(string tablename)
        {
            return ExecuteSql("drop table if exists " + tablename);
        }

        public bool RenameTable(string oldName, string newName)
        {
            return ExecuteSql("alter table " + oldName + " rename to " + newName);
        }

        public void Vacuum()
        {
            cmd.CommandText = "vacuum";
            cmd.ExecuteNonQuery();
        }

        public void Close()
        {
            try {
                if (cmd != null)
                    cmd.Dispose();
            } catch (Exception) {
                // 已经释放过就忽略
            }

            try {
                if (cn != null && cn.State != System.Data.ConnectionState.Closed)
                    cn.Close();
            } catch (Exception) {
                // 已经释放过就忽略
            }
        }

        public virtual void Dispose()
        {
            Close();
        }
    }
}
