using Jvedio.Core.DataBase;
using Jvedio.Core.Enums;
using Jvedio.Core.ORM.Mapper;
using Jvedio.Core.ORM.Wrapper;
using System;
using System.Collections.Generic;
using static Jvedio.App;

namespace Jvedio.Mapper.BaseMapper
{
    /// <summary>
    /// 实体 Mapper 基类。
    /// <para>
    /// 所有读写都经 <see cref="DataBaseGate"/> 串行化：读并发、写独占。
    /// 桌面端单线程时几乎没有额外开销（读锁可重入且无竞争），
    /// 但局域网 API 并发访问时能避免共享连接被同时使用。
    /// </para>
    /// </summary>
    public class BaseMapper<T> : AbstractMapper<T>
    {
        #region "属性"

        static SqliteMapper<T> SqliteMapper { get; set; }

        static MySQLMapper<T> MySQLMapper { get; set; }


        #endregion


        public override void Init()
        {
            using (DataBaseGate.EnterWrite()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        SqliteMapper = new SqliteMapper<T>(SqlManager.DEFAULT_SQLITE_PATH);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        MySQLMapper = new MySQLMapper<T>();
                }
            }
        }

        public override void Dispose()
        {
            using (DataBaseGate.EnterWrite()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    SqliteMapper?.Dispose();
                    SqliteMapper = null;
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    MySQLMapper?.Dispose();
                    MySQLMapper = null;
                }
            }
        }

        public override int ExecuteNonQuery(string sql)
        {
            try {
                using (DataBaseGate.EnterWrite()) {
                    if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                        if (SqliteMapper == null)
                            return -1;
                        return SqliteMapper.ExecuteNonQuery(sql);
                    } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                        if (MySQLMapper == null)
                            return -1;
                        return MySQLMapper.ExecuteNonQuery(sql);
                    }
                }
            } catch (Exception ex) {
                Logger.Error(ex);
            }

            return -1;
        }



        public void InitSqlite(string sqlitePath)
        {
            using (DataBaseGate.EnterWrite()) {
                if (SqliteMapper == null)
                    SqliteMapper = new SqliteMapper<T>(sqlitePath);
            }
        }

        public override bool IsTableExists(string tableName)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        return false;
                    return SqliteMapper.IsTableExists(tableName);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        return false;
                    return MySQLMapper.IsTableExists(tableName);
                }
            }

            return false;
        }

        public override List<T> SelectByDict(Dictionary<string, object> dict, IWrapper<T> wrapper)
        {
            throw new NotImplementedException();
        }

        public override T SelectById(IWrapper<T> wrapper)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        return default(T);
                    return SqliteMapper.SelectById(wrapper);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        return default(T);
                    return MySQLMapper.SelectById(wrapper);
                }
            }

            return default(T);
        }

        public override long SelectCount(IWrapper<T> wrapper = null)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        return 0;
                    return SqliteMapper.SelectCount(wrapper);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        return 0;
                    return MySQLMapper.SelectCount(wrapper);
                }
            }

            return 0;
        }

        public override long SelectCount(string sql)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        return 0;
                    return SqliteMapper.SelectCount(sql);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        return 0;
                    return MySQLMapper.SelectCount(sql);
                }
            }

            return 0;
        }

        public override string SelectLastInsertRowId()
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    return SqliteMapper?.SelectLastInsertRowId();
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    return MySQLMapper?.SelectLastInsertRowId();
                }
            }

            return null;
        }

        public override List<T> SelectList(IWrapper<T> wrapper = null)
        {
            try {
                using (DataBaseGate.EnterRead()) {
                    if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                        return SqliteMapper?.SelectList(wrapper);
                    } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                        return MySQLMapper?.SelectList(wrapper);
                    }
                }
            } catch (Exception ex) {
                Logger.Error(ex);
            }

            return null;
        }

        public override T SelectOne(IWrapper<T> wrapper = null)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        return default(T);
                    return SqliteMapper.SelectOne(wrapper);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        return default(T);
                    return MySQLMapper.SelectOne(wrapper);
                }
            }

            return default(T);
        }

        public override List<Dictionary<string, object>> Select(IWrapper<T> wrapper)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    return SqliteMapper?.Select(wrapper);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    return MySQLMapper?.Select(wrapper);
                }
            }

            return null;
        }

        public override List<Dictionary<string, object>> Select(string sql)
        {
            using (DataBaseGate.EnterRead()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    return SqliteMapper?.Select(sql);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    return MySQLMapper?.Select(sql);
                }
            }

            return null;
        }

        public override object InsertAndGetID(T entity)
        {
            using (DataBaseGate.EnterWrite()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    return SqliteMapper?.InsertAndGetID(entity);
                } else if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        return null;
                    return MySQLMapper?.InsertAndGetID(entity);
                }
            }

            return null;
        }

        public override bool DeleteDataBase(string db_name)
        {
            throw new NotImplementedException();
        }

        public override int Update(IWrapper<T> wrapper)
        {
            throw new NotImplementedException();
        }
    }
}
