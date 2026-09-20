using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.ObjectPool;
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
        private StringOpenHashMap _unregisteredWarned = new StringOpenHashMap(8);
        private StringOpenHashMap _unhandledDespawnWarned = new StringOpenHashMap(8);
        private StringOpenHashMap _groupRootMap = new StringOpenHashMap(8);
        private StringOpenHashMap _poolByLocation = new StringOpenHashMap(32);

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

        protected override void OnDestroyService()
        {
            Application.lowMemory -= OnLowMemory;
            ClearAllPools();
            _catalog.Dispose();
            _catalog = null;
            _unregisteredWarned.Dispose();
            _unhandledDespawnWarned.Dispose();
            _groupRootMap.Dispose();
            _poolByLocation.Dispose();
        }

        public void Tick(float deltaTime)
        {
            if (!_enabled)
            {
                return;
            }

            ProcessDueMaintenance(Time.time);
            _enabled = _maintenanceCount > 0;
        }

        public bool TrySpawn(string location, Transform parent, out GameObject instance)
        {
            instance = Spawn(location, parent);
            return instance != null;
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
            if (instance == null)
            {
                return;
            }

            if (instance.TryGetComponent(out GameObjectPoolHandle handle) && handle.TryRelease())
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnUnhandledDespawn(instance);
#endif
            instance.SafeDestroySelf();
        }

        public void Despawn(GameObjectPoolHandle handle)
        {
            if (handle == null || handle.TryRelease())
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnUnhandledDespawn(handle.gameObject);
#endif
            if (handle != null)
            {
                handle.gameObject.SafeDestroySelf();
            }
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
                if (pool != null && string.Equals(pool.Group, groupName, StringComparison.Ordinal))
                {
                    pool.Flush();
                }
            }
        }

        public void FlushAll()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i]?.Flush();
            }
        }

        public void LoadCatalog(PoolConfigScriptableObject config)
        {
            ClearAllPools();
            _catalog.Dispose();
            _catalog = config == null ? PoolCompiledCatalog.Empty() : config.BuildCatalog();
            _enabled = false;
        }

        public void LoadCatalog(string poolConfigPath)
        {
            IResourceService resourceService = AppServices.App.Require<IResourceService>();
            using ResourceAssetLease<PoolConfigScriptableObject> lease =
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
                if (pool == null)
                {
                    continue;
                }

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
            if (snapshots == null || snapshots.Length == 0)
            {
                ReleaseDebugSnapshots();
                return 0;
            }

            ReleaseDebugSnapshots();
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
                if (pool != null)
                {
                    _debugSnapshots.Add(pool.CreateSnapshot(false));
                }
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
            if (snapshot == null || string.IsNullOrEmpty(snapshot.location))
            {
                return;
            }

            FindPool(snapshot.location)?.FillInstances(snapshot);
        }

        internal void ScheduleMaintenance(int poolIndex, float dueTime, ref int heapIndex)
        {
            if (dueTime >= float.MaxValue)
            {
                RemoveMaintenance(ref heapIndex);
                return;
            }

            if (heapIndex >= 0)
            {
                _maintenanceHeap[heapIndex].dueTime = dueTime;
                _maintenanceHeap[heapIndex].poolIndex = poolIndex;
                SiftMaintenanceUp(heapIndex);
                SiftMaintenanceDown(heapIndex);
                _enabled = true;
                return;
            }

            EnsureMaintenanceCapacity(_maintenanceCount + 1);
            int insertIndex = _maintenanceCount++;
            _maintenanceHeap[insertIndex].dueTime = dueTime;
            _maintenanceHeap[insertIndex].poolIndex = poolIndex;
            heapIndex = insertIndex;
            _pools[poolIndex].SetMaintenanceHeapIndex(insertIndex);
            SiftMaintenanceUp(insertIndex);
            _enabled = true;
        }

        internal void RemoveMaintenance(ref int heapIndex)
        {
            if (heapIndex < 0 || heapIndex >= _maintenanceCount)
            {
                heapIndex = -1;
                return;
            }

            RemoveMaintenanceAt(heapIndex);
            heapIndex = -1;
        }

        private RuntimeGameObjectPool ResolvePool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            if (_poolByLocation.TryGetValue(normalized, out int poolIndex))
            {
                return _pools[poolIndex];
            }

            int ruleIndex = _catalog.Resolve(normalized);
            if (ruleIndex < 0)
            {
                WarnUnregistered(normalized);
                return null;
            }

            return GetOrCreatePool(ruleIndex, normalized);
        }

        private RuntimeGameObjectPool FindPool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            return !string.IsNullOrEmpty(normalized) && _poolByLocation.TryGetValue(normalized, out int poolIndex)
                ? _pools[poolIndex]
                : null;
        }

        private RuntimeGameObjectPool GetOrCreatePool(int ruleIndex, string location)
        {
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                return _pools[existing];
            }

            EnsurePoolCapacity(_poolCount + 1);
            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            var pool = MemoryPool.Acquire<RuntimeGameObjectPool>();
            pool.Initialize(this, _poolCount, rule, location, _loader, GetOrCreateGroupRoot(rule.Group));
            _pools[_poolCount] = pool;
            _poolByLocation.AddOrUpdate(location, _poolCount);
            _poolCount++;
            return pool;
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
            _groupRootMap.AddOrUpdate(groupName, newIndex);
            return root;
        }

        private void ClearAllPools()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
                if (pool == null)
                {
                    continue;
                }

                pool.Shutdown();
                MemoryPool.Release(pool);
                _pools[i] = null;
            }

            _poolCount = 0;
            _maintenanceCount = 0;
            _poolByLocation.Clear();
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
                    _groupRoots[i] = null;
                }
            }

            _groupRootCount = 0;
            _groupRootMap.Clear();
        }

        private void WarnUnregistered(string location)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_unregisteredWarned.TryGetValue(location, out _))
            {
                return;
            }

            _unregisteredWarned.AddOrUpdate(location, 1);
            Log.Error(ZString.Format("[GameObjectPool] Location is not in PoolConfig: {0}", location));
#endif
        }

        private void WarnUnhandledDespawn(GameObject instance)
        {
            string name = instance == null ? "<null>" : instance.name;
            if (_unhandledDespawnWarned.TryGetValue(name, out _))
            {
                return;
            }

            _unhandledDespawnWarned.AddOrUpdate(name, 1);
            Log.Warning(ZString.Format("[GameObjectPool] Despawn target is not a pooled instance and will be destroyed: {0}", name));
        }

        private void OnLowMemory()
        {
            float now = Time.time;
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i]?.ExecuteMaintenance(now, true);
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
                _pools[_duePools[i]]?.ExecuteMaintenance(now, false);
            }
        }

        private void EnsurePoolCapacity(int required)
        {
            if (_pools.Length >= required)
            {
                return;
            }

            Array.Resize(ref _pools, Mathf.Max(required, _pools.Length << 1));
        }

        private void EnsureMaintenanceCapacity(int required)
        {
            if (_maintenanceHeap.Length >= required)
            {
                return;
            }

            Array.Resize(ref _maintenanceHeap, Mathf.Max(required, _maintenanceHeap.Length << 1));
        }

        private void RemoveMaintenanceAt(int heapIndex)
        {
            MaintenanceNode removed = _maintenanceHeap[heapIndex];
            _pools[removed.poolIndex]?.SetMaintenanceHeapIndex(-1);
            int lastIndex = _maintenanceCount - 1;
            if (heapIndex != lastIndex)
            {
                MaintenanceNode moved = _maintenanceHeap[lastIndex];
                _maintenanceHeap[heapIndex] = moved;
                _pools[moved.poolIndex]?.SetMaintenanceHeapIndex(heapIndex);
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
            _pools[_maintenanceHeap[left].poolIndex]?.SetMaintenanceHeapIndex(left);
            _pools[_maintenanceHeap[right].poolIndex]?.SetMaintenanceHeapIndex(right);
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
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int groupCompare = string.CompareOrdinal(left.group, right.group);
            return groupCompare != 0 ? groupCompare : string.CompareOrdinal(left.location, right.location);
        }
    }
}
