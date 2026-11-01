using System;
using System.Collections.Generic;
using Jvedio.Core.ORM.Wrapper;

namespace Jvedio.Core.ORM.Mapper
{
    /// <summary>
    /// 实体 Mapper 的公共接口，与原 <c>SuperUtils.Framework.ORM.Mapper.IMapper&lt;T&gt;</c> 一致。
    /// </summary>
    public interface IMapper<T>
    {
        string SelectLastInsertRowId();

        List<Dictionary<string, object>> Select(IWrapper<T> wrapper);

        List<Dictionary<string, object>> Select(string sql);

        List<T> SelectList(IWrapper<T> wrapper);

        T SelectById(IWrapper<T> wrapper);

        List<T> SelectByDict(Dictionary<string, object> dict, IWrapper<T> wrapper);

        long SelectCount(IWrapper<T> wrapper);

        long SelectCount(string sql);

        object InsertAndGetID(T entity);

        bool Insert(T entity);

        int DeleteById(object id);

        int DeleteByDict(Dictionary<string, object> dict);

        int UpdateById(T entity);

        int Update(T entity);

        int Update(IWrapper<T> wrapper);

        bool DeleteDataBase(string db_name);
    }
}
