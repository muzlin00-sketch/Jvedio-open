using Jvedio.Core.DataBase;
using Jvedio.Core.Enums;
using Jvedio.Core.ORM.Enums;
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

        /// <summary>
        /// 执行一条写语句（Insert / InsertBatch / Update / UpdateById / UpdateField / Delete 全部经这里）。
        /// <para>
        /// <b>失败一律抛异常，不再吞成 -1。</b>改造前这里是
        /// <c>catch (Exception ex) { Logger.Error(ex); } return -1;</c>：
        /// 受影响行数变成 -1，而调用方普遍拿它当行数用（或干脆不看返回值），
        /// 于是「已导入 11 条」可以和「一条都没进库」同时成立（2026-11-11 真实事故）。
        /// </para>
        /// <para>
        /// 允许失败的场景（幂等 DDL、事务边界、清理与统计语句）请显式改用
        /// <see cref="AbstractMapper{T}.TryExecuteNonQuery(string)"/>，它会返回 bool 并写日志。
        /// </para>
        /// </summary>
        public override int ExecuteNonQuery(string sql)
        {
            using (DataBaseGate.EnterWrite()) {
                if (Main.CurrentDataBaseType == DataBaseType.SQLite) {
                    if (SqliteMapper == null)
                        throw new InvalidOperationException(
                            "SQLite Mapper 未初始化（Init 未调用或已 Dispose），写语句未执行：" + sql);
                    return SqliteMapper.ExecuteNonQuery(sql);
                }

                if (Main.CurrentDataBaseType == DataBaseType.MySQL) {
                    if (MySQLMapper == null)
                        throw new InvalidOperationException(
                            "MySQL Mapper 未初始化（Init 未调用或已 Dispose），写语句未执行：" + sql);
                    return MySQLMapper.ExecuteNonQuery(sql);
                }
            }

            throw new NotSupportedException(
                $"不支持的数据源类型 {Main.CurrentDataBaseType}，写语句未执行：" + sql);
        }

        /// <summary>
        /// 插入一条并回写主键：整段持有同一个写锁，避免「插完再读 rowid」被别的线程插队。
        /// <para>
        /// <see cref="Jvedio.Core.DataBase.DataBaseGate"/> 支持同线程重入，
        /// 因此内层 <see cref="AbstractMapper{T}.Insert(T, InsertMode)"/>
        /// 再取写锁不会死锁，锁粒度仍限制在「一条插入」而不是整批，
        /// 不会把导入上千条时的整个批量都变成独占、拖死界面读操作。
        /// </para>
        /// </summary>
        protected override int InsertAndWriteBackId(T entity, InsertMode mode)
        {
            using (DataBaseGate.EnterWrite()) {
                return base.InsertAndWriteBackId(entity, mode);
            }
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
