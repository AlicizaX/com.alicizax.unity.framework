using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    internal sealed class GameObjectPoolService : ServiceBase, IServiceTickable, IGameObjectPoolService, IGameObjectPoolDebugService
    {
        private struct MaintenanceNode
        {
            public float dueTime;
            public int poolIndex;
        }

        private static readonly Comparison<GameObjectPoolSnapshot> SnapshotComparer = CompareSnapshot;

        private readonly IPrefabLoader _loader;
        private readonly Transform _containerRoot;
        private readonly List<GameObjectPoolSnapshot> _debugSnapshots = new List<GameObjectPoolSnapshot>(16);
        private readonly HashSet<string> _unregisteredWarned = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _groupRootMap = new Dictionary<string, int>(8, StringComparer.Ordinal);
        private readonly Dictionary<string, int> _poolByLocation = new Dictionary<string, int>(32, StringComparer.Ordinal);
        private readonly Dictionary<string, int> _poolByRequestedLocation = new Dictionary<string, int>(32, StringComparer.Ordinal);

        private RuntimeGameObjectPool[] _pools = new RuntimeGameObjectPool[8];
        private int _poolCount;
        private PoolCompiledCatalog _catalog = PoolCompiledCatalog.Empty();
        private Transform[] _groupRoots = new Transform[4];
        private int _groupRootCount;
        private MaintenanceNode[] _maintenanceHeap = new MaintenanceNode[8];
        private int[] _duePools = new int[8];
        private int _maintenanceCount;
        private bool _enabled;

        public GameObjectPoolService(Transform transform)
            : this(transform, new YooAssetPrefabLoader())
        {
        }

        internal GameObjectPoolService(Transform transform, IPrefabLoader loader)
        {
            _containerRoot = transform;
            _loader = loader;
        }

        protected override void OnInitialize()
        {
            Application.lowMemory += OnLowMemory;
        }

        protected override void OnDispose()
        {
            Application.lowMemory -= OnLowMemory;
            ClearAllPools();
            _catalog = null;
        }

        public void Tick(float deltaTime)
        {
            if (!_enabled)
            {
                return;
            }

            ProcessDueMaintenance(Time.unscaledTime);
            _enabled = _maintenanceCount > 0;
        }

        public GameObject Spawn(string location, Transform parent = null)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : pool.Spawn(parent);
        }

        public T Spawn<T>(string location, Transform parent = null) where T : Component
        {
            GameObject instance = Spawn(location, parent);
            if (instance == null)
            {
                return null;
            }

            T component = instance.GetComponent<T>();
            if (component == null)
            {
                Despawn(instance);
                return null;
            }

            return component;
        }

        public async UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : await pool.SpawnAsync(parent, cancellationToken);
        }

        public async UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            GameObject instance = await SpawnAsync(location, parent, cancellationToken);
            if (instance == null)
            {
                return null;
            }

            T component = instance.GetComponent<T>();
            if (component == null)
            {
                Despawn(instance);
                return null;
            }

            return component;
        }

        public GameObject LoadPrefab(string location)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : pool.LoadPrefab();
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : await pool.LoadPrefabAsync(cancellationToken);
        }

        public async UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            if (pool != null)
            {
                await pool.WarmupAsync(count, cancellationToken);
            }
        }

        public void Despawn(GameObject instance)
        {
            if (!instance.TryGetComponent(out GameObjectPoolHandle handle))
            {
                throw new InvalidOperationException(ZString.Format("[GameObjectPool] {0} is not a pooled instance.", instance.name));
            }

            handle.Despawn();
        }

        public void Despawn(GameObjectPoolHandle handle)
        {
            handle.Despawn();
        }

        public void Flush(string location)
        {
            RuntimeGameObjectPool pool = FindPool(location);
            pool?.Flush();
        }

        public void FlushGroup(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DefaultGroup : group.Trim();
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
                if (string.Equals(pool.Group, groupName, StringComparison.Ordinal))
                {
                    pool.Flush();
                }
            }
        }

        public void FlushAll()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i].Flush();
            }
        }

        public void LoadCatalog(PoolConfigScriptableObject config)
        {
            ClearAllPools();
            _catalog = config == null ? PoolCompiledCatalog.Empty() : config.BuildCatalog();
            _enabled = false;
        }

        public void LoadCatalog(string poolConfigPath)
        {
            IResourceService resourceService = AppServices.Require<IResourceService>();
            using AssetLease<PoolConfigScriptableObject> lease =
                resourceService.LoadLease<PoolConfigScriptableObject>(poolConfigPath);
            LoadCatalog(lease.Asset);
        }

        public GameObjectPoolSummarySnapshot GetDebugSummary()
        {
            int loadedPrefabCount = 0;
            int totalInstanceCount = 0;
            int activeInstanceCount = 0;
            int inactiveInstanceCount = 0;
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
                if (pool.IsPrefabLoaded)
                {
                    loadedPrefabCount++;
                }

                totalInstanceCount += pool.TotalCount;
                activeInstanceCount += pool.ActiveCount;
                inactiveInstanceCount += pool.InactiveCount;
            }

            return new GameObjectPoolSummarySnapshot(
                true,
                _poolCount,
                loadedPrefabCount,
                totalInstanceCount,
                activeInstanceCount,
                inactiveInstanceCount,
                _maintenanceCount);
        }

        public int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots)
        {
            ReleaseDebugSnapshots();
            if (snapshots.Length == 0)
            {
                return 0;
            }

            for (int i = 0; i < _poolCount; i++)
            {
                _debugSnapshots.Add(_pools[i].CreateSnapshot(false));
            }

            _debugSnapshots.Sort(SnapshotComparer);
            int copyCount = Mathf.Min(snapshots.Length, _debugSnapshots.Count);
            for (int i = 0; i < copyCount; i++)
            {
                snapshots[i] = _debugSnapshots[i];
            }

            return copyCount;
        }

        public void FillDebugInstances(GameObjectPoolSnapshot snapshot)
        {
            FindPool(snapshot.location)?.FillInstances(snapshot);
        }

        internal float GetMaintenanceDueTime(int heapIndex)
        {
            return heapIndex < 0 ? float.MaxValue : _maintenanceHeap[heapIndex].dueTime;
        }

        internal void ScheduleMaintenance(RuntimeGameObjectPool pool, float dueTime)
        {
            int heapIndex = pool.MaintenanceHeapIndex;
            if (dueTime >= float.MaxValue)
            {
                if (heapIndex >= 0)
                {
                    RemoveMaintenanceAt(heapIndex);
                }

                return;
            }

            if (heapIndex >= 0)
            {
                _maintenanceHeap[heapIndex].dueTime = dueTime;
                SiftMaintenanceUp(heapIndex);
                SiftMaintenanceDown(heapIndex);
                _enabled = true;
                return;
            }

            if (_maintenanceCount == _maintenanceHeap.Length)
            {
                Array.Resize(ref _maintenanceHeap, _maintenanceCount << 1);
            }

            int insertIndex = _maintenanceCount++;
            _maintenanceHeap[insertIndex].dueTime = dueTime;
            _maintenanceHeap[insertIndex].poolIndex = pool.PoolIndex;
            pool.MaintenanceHeapIndex = insertIndex;
            SiftMaintenanceUp(insertIndex);
            _enabled = true;
        }

        private RuntimeGameObjectPool ResolvePool(string location)
        {
            if (location == null)
            {
                return null;
            }

            if (_poolByRequestedLocation.TryGetValue(location, out int requestedIndex))
            {
                return _pools[requestedIndex];
            }

            string normalized = PoolEntry.NormalizeLocation(location);
            if (normalized.Length == 0)
            {
                return null;
            }

            if (!_poolByLocation.TryGetValue(normalized, out int poolIndex))
            {
                int ruleIndex = _catalog.Resolve(normalized);
                if (ruleIndex < 0)
                {
                    WarnUnregistered(normalized);
                    return null;
                }

                poolIndex = CreatePool(ruleIndex, normalized);
            }

            _poolByRequestedLocation.Add(location, poolIndex);
            return _pools[poolIndex];
        }

        private RuntimeGameObjectPool FindPool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            return normalized.Length != 0 && _poolByLocation.TryGetValue(normalized, out int poolIndex)
                ? _pools[poolIndex]
                : null;
        }

        private int CreatePool(int ruleIndex, string location)
        {
            if (_poolCount == _pools.Length)
            {
                Array.Resize(ref _pools, _poolCount << 1);
            }

            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            int poolIndex = _poolCount++;
            _pools[poolIndex] = new RuntimeGameObjectPool(this, poolIndex, rule, location, _loader, GetOrCreateGroupRoot(rule.Group));
            _poolByLocation.Add(location, poolIndex);
            return poolIndex;
        }

        private Transform GetOrCreateGroupRoot(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DefaultGroup : group.Trim();
            if (_groupRootMap.TryGetValue(groupName, out int groupIndex))
            {
                Transform existing = _groupRoots[groupIndex];
                if (existing != null)
                {
                    return existing;
                }
            }

            if (_groupRootCount >= _groupRoots.Length)
            {
                Array.Resize(ref _groupRoots, _groupRoots.Length << 1);
            }

            var rootObject = new GameObject(ZString.Format("[{0}]", groupName));
            Transform root = rootObject.transform;
            root.SetParent(_containerRoot, false);
            int newIndex = _groupRootCount++;
            _groupRoots[newIndex] = root;
            _groupRootMap[groupName] = newIndex;
            return root;
        }

        private void ClearAllPools()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i].Shutdown();
                _pools[i] = null;
            }

            _poolCount = 0;
            _poolByLocation.Clear();
            _poolByRequestedLocation.Clear();
            _unregisteredWarned.Clear();
            ClearGroupRoots();
            ReleaseDebugSnapshots();
        }

        private void ClearGroupRoots()
        {
            for (int i = 0; i < _groupRootCount; i++)
            {
                Transform root = _groupRoots[i];
                if (root != null)
                {
                    root.gameObject.SafeDestroySelf();
                }

                _groupRoots[i] = null;
            }

            _groupRootCount = 0;
            _groupRootMap.Clear();
        }

        private void WarnUnregistered(string location)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_unregisteredWarned.Add(location))
            {
                Log.Error(ZString.Format("[GameObjectPool] Location is not in PoolConfig: {0}", location));
            }
#endif
        }

        private void OnLowMemory()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i].ExecuteMaintenance(now, true);
            }
        }

        private void ProcessDueMaintenance(float now)
        {
            int dueCount = 0;
            while (_maintenanceCount > 0)
            {
                MaintenanceNode node = _maintenanceHeap[0];
                if (node.dueTime > now)
                {
                    break;
                }

                if (dueCount == _duePools.Length)
                {
                    Array.Resize(ref _duePools, _duePools.Length << 1);
                }

                _duePools[dueCount++] = node.poolIndex;
                RemoveMaintenanceAt(0);
            }

            for (int i = 0; i < dueCount; i++)
            {
                _pools[_duePools[i]].ExecuteMaintenance(now, false);
            }
        }

        private void RemoveMaintenanceAt(int heapIndex)
        {
            _pools[_maintenanceHeap[heapIndex].poolIndex].MaintenanceHeapIndex = -1;
            int lastIndex = _maintenanceCount - 1;
            if (heapIndex != lastIndex)
            {
                MaintenanceNode moved = _maintenanceHeap[lastIndex];
                _maintenanceHeap[heapIndex] = moved;
                _pools[moved.poolIndex].MaintenanceHeapIndex = heapIndex;
            }

            _maintenanceHeap[lastIndex] = default;
            _maintenanceCount = lastIndex;
            if (heapIndex < _maintenanceCount)
            {
                SiftMaintenanceUp(heapIndex);
                SiftMaintenanceDown(heapIndex);
            }
        }

        private void SiftMaintenanceUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_maintenanceHeap[parent].dueTime <= _maintenanceHeap[index].dueTime)
                {
                    break;
                }

                SwapMaintenance(parent, index);
                index = parent;
            }
        }

        private void SiftMaintenanceDown(int index)
        {
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= _maintenanceCount)
                {
                    return;
                }

                int right = left + 1;
                int smallest = right < _maintenanceCount && _maintenanceHeap[right].dueTime < _maintenanceHeap[left].dueTime
                    ? right
                    : left;
                if (_maintenanceHeap[index].dueTime <= _maintenanceHeap[smallest].dueTime)
                {
                    return;
                }

                SwapMaintenance(index, smallest);
                index = smallest;
            }
        }

        private void SwapMaintenance(int left, int right)
        {
            MaintenanceNode temp = _maintenanceHeap[left];
            _maintenanceHeap[left] = _maintenanceHeap[right];
            _maintenanceHeap[right] = temp;
            _pools[_maintenanceHeap[left].poolIndex].MaintenanceHeapIndex = left;
            _pools[_maintenanceHeap[right].poolIndex].MaintenanceHeapIndex = right;
        }

        private void ReleaseDebugSnapshots()
        {
            for (int i = 0; i < _debugSnapshots.Count; i++)
            {
                MemoryPool.Release(_debugSnapshots[i]);
            }

            _debugSnapshots.Clear();
        }

        private static int CompareSnapshot(GameObjectPoolSnapshot left, GameObjectPoolSnapshot right)
        {
            int groupCompare = string.CompareOrdinal(left.group, right.group);
            return groupCompare != 0 ? groupCompare : string.CompareOrdinal(left.location, right.location);
        }
    }
}
