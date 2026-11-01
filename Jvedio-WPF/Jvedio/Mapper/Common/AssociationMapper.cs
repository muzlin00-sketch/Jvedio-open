using Jvedio.Entity.Base;
using Jvedio.Entity.CommonSQL;
using Jvedio.Mapper.BaseMapper;
using System.Collections.Generic;
using System.Linq;

namespace Jvedio.Mapper
{
    public class AssociationMapper : BaseMapper<Association>
    {
        public List<Association> Associations { get; set; }

        public Dictionary<long, ListNode<long>> AdjacencyList { get; set; }

        /// <summary>
        /// 反向索引：DataID -> 直接关联的其他 DataID 集合。
        /// <para>
        /// 原实现在每次 <see cref="GetAssociationDatas"/> 时重建整张邻接表，
        /// 并在渲染循环中逐条影片调用，构成 N 次全表查询 + O(n²) 遍历。
        /// 改为一次性构建双向映射后，单次查询退化为哈希查找。
        /// </para>
        /// </summary>
        private Dictionary<long, HashSet<long>> _AdjacencyMap { get; set; }

        private bool _AdjacencyLoaded { get; set; }

        private readonly object _AdjacencyLock = new object();

        /// <summary>
        /// 标记邻接数据已过期，下次查询时重新加载。
        /// <para>新增/删除关联后调用。</para>
        /// </summary>
        public void InvalidateAdjacency()
        {
            lock (_AdjacencyLock) {
                _AdjacencyLoaded = false;
                _AdjacencyMap = null;
                AdjacencyList = null;
            }
        }

        public void InitAdjacencyList()
        {
            lock (_AdjacencyLock) {
                LoadAdjacencyCore();
            }
        }

        private void LoadAdjacencyCore()
        {
            Associations = SelectList();
            AdjacencyList = new Dictionary<long, ListNode<long>>();
            _AdjacencyMap = new Dictionary<long, HashSet<long>>();

            if (Associations == null || Associations.Count == 0) {
                _AdjacencyLoaded = true;
                return;
            }

            foreach (Association item in Associations) {
                long main = item.MainDataID;
                long sub = item.SubDataID;

                // 保留原有邻接链表结构（其他代码可能依赖）
                if (AdjacencyList.ContainsKey(main)) {
                    ListNode<long> node = AdjacencyList[main];
                    while (true) {
                        if (node.Next == null) {
                            node.Next = new ListNode<long>(sub);
                            break;
                        } else {
                            node = node.Next;
                        }
                    }
                } else {
                    ListNode<long> node = new ListNode<long>(sub);
                    ListNode<long> head = new ListNode<long>(-1);
                    head.Next = node;
                    node.Head = head;
                    AdjacencyList.Add(main, node);
                }

                // 构建双向映射：关联是无向的（A 关联 B，则 B 也关联 A）
                AddEdge(main, sub);
                AddEdge(sub, main);
            }

            _AdjacencyLoaded = true;
        }

        private void AddEdge(long from, long to)
        {
            if (!_AdjacencyMap.ContainsKey(from))
                _AdjacencyMap[from] = new HashSet<long>();
            _AdjacencyMap[from].Add(to);
        }

        /// <summary>
        /// 获取与指定影片关联的所有 DataID（含传递关联）。
        /// </summary>
        public HashSet<long> GetAssociationDatas(long dataID)
        {
            lock (_AdjacencyLock) {
                if (!_AdjacencyLoaded)
                    LoadAdjacencyCore();
            }

            HashSet<long> result = new HashSet<long>();
            if (_AdjacencyMap == null || !_AdjacencyMap.ContainsKey(dataID))
                return result;

            // 广度优先遍历收集连通分量
            HashSet<long> visited = new HashSet<long> { dataID };
            Queue<long> queue = new Queue<long>();
            queue.Enqueue(dataID);

            while (queue.Count > 0) {
                long current = queue.Dequeue();
                HashSet<long> neighbors;
                if (!_AdjacencyMap.TryGetValue(current, out neighbors) || neighbors == null)
                    continue;

                foreach (long next in neighbors) {
                    if (visited.Add(next)) {
                        result.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            result.Remove(dataID);
            return result;
        }

        /// <summary>
        /// 批量获取多条影片的关联数据，避免逐条查询。
        /// <para>渲染一页影片前调用一次即可。</para>
        /// </summary>
        public Dictionary<long, HashSet<long>> GetAssociationDatasBatch(IEnumerable<long> dataIds)
        {
            var dict = new Dictionary<long, HashSet<long>>();
            if (dataIds == null)
                return dict;

            lock (_AdjacencyLock) {
                if (!_AdjacencyLoaded)
                    LoadAdjacencyCore();
            }

            foreach (long id in dataIds) {
                if (!dict.ContainsKey(id))
                    dict[id] = GetAssociationDatas(id);
            }

            return dict;
        }
    }
}
