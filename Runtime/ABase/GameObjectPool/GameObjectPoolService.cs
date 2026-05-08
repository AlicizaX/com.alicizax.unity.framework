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
    public readonly struct PoolAssetId
    {
        internal readonly int RuleIndex;
        internal readonly int CatalogVersion;
        internal readonly string RequestPath;
        internal readonly string LogicalPath;
        internal readonly string LoadPath;
        internal readonly string PoolKey;
        internal readonly PoolResourceLoaderType LoaderType;

        public bool IsValid => RuleIndex >= 0;

        internal PoolAssetId(
            int ruleIndex,
            int catalogVersion,
            string requestPath,
            string logicalPath,
            string loadPath,
            string poolKey,
            PoolResourceLoaderType loaderType)
        {
            RuleIndex = ruleIndex;
            CatalogVersion = catalogVersion;
            RequestPath = requestPath;
            LogicalPath = logicalPath;
            LoadPath = loadPath;
            PoolKey = poolKey;
            LoaderType = loaderType;
        }
    }

    internal sealed class GameObjectPoolService : ServiceBase, IServiceTickable, IGameObjectPoolService, IGameObjectPoolDebugService
    {
        public GameObjectPoolService(Transform transform)
        {
            _containerRoot = transform;
        }

        internal readonly struct ResolvedAssetRequest
        {
            public readonly int RuleIndex;
            public readonly string RequestPath;
            public readonly string LogicalPath;
            public readonly string LoadPath;
            public readonly string PoolKey;
            public readonly PoolResourceLoaderType LoaderType;

            public ResolvedAssetRequest(
                int ruleIndex,
                string requestPath,
                string logicalPath,
                string loadPath,
                string poolKey,
                PoolResourceLoaderType loaderType)
            {
                RuleIndex = ruleIndex;
                RequestPath = requestPath;
                LogicalPath = logicalPath;
                LoadPath = loadPath;
                PoolKey = poolKey;
                LoaderType = loaderType;
            }
        }

        private readonly struct ParsedRequestPath
        {
            public readonly string OriginalPath;
            public readonly string LogicalPath;
            public readonly bool HasExplicitLoaderType;
            public readonly PoolResourceLoaderType LoaderType;

            public ParsedRequestPath(
                string originalPath,
                string logicalPath,
                bool hasExplicitLoaderType,
                PoolResourceLoaderType loaderType)
            {
                OriginalPath = originalPath;
                LogicalPath = logicalPath;
                HasExplicitLoaderType = hasExplicitLoaderType;
                LoaderType = loaderType;
            }
        }

        private struct MaintenanceNode
        {
            public float dueTime;
            public int poolIndex;
        }

        private static readonly Comparison<GameObjectPoolSnapshot> SnapshotComparer = CompareSnapshot;

        private readonly IResourceLoader[] _resourceLoaders = new IResourceLoader[2];
        private readonly List<GameObjectPoolSnapshot> _debugSnapshots = new List<GameObjectPoolSnapshot>(16);

        private RuntimeGameObjectPool[] _pools = new RuntimeGameObjectPool[8];
        private int _poolCount;
        private PoolCompiledCatalog _catalog = PoolCompiledCatalog.Empty();
        private int _catalogVersion;
        private StringOpenHashMap[] _rulePoolMaps = Array.Empty<StringOpenHashMap>();
        private bool[] _rulePoolMapInitialized = Array.Empty<bool>();
        private StringOpenHashMap _directLoadWarnedPaths = new StringOpenHashMap(8);
        private StringOpenHashMap _groupRootMap = new StringOpenHashMap(8);
        private Transform[] _groupRoots = new Transform[4];
        private int _groupRootCount;
        private CancellationTokenSource _shutdownTokenSource;

        private MaintenanceNode[] _maintenanceHeap = new MaintenanceNode[8];
        private int _maintenanceCount;

        internal CancellationToken ShutdownToken => _shutdownTokenSource == null ? default : _shutdownTokenSource.Token;

        private Transform _containerRoot;

        private bool _enabled;

        protected override void OnInitialize()
        {
            _shutdownTokenSource = new CancellationTokenSource();
            EnsureDefaultResourceLoaders();
            Application.lowMemory += OnLowMemory;
        }

        protected override void OnDestroyService()
        {
            Application.lowMemory -= OnLowMemory;
            _shutdownTokenSource?.Cancel();
            ClearAllPools();
            ReleaseRulePoolMaps();
            _catalog.Dispose();
            _catalog = null;
            _directLoadWarnedPaths.Dispose();
            _groupRootMap.Dispose();

            _shutdownTokenSource?.Dispose();
            _shutdownTokenSource = null;
        }

        public void Tick(float deltaTime)
        {
            if (!_enabled) return;
            float now = Time.time;
            ProcessDueMaintenance(now);
            _enabled = _maintenanceCount > 0;
        }

        internal GameObject GetGameObject(string assetPath, Transform parent = null)
        {
            string normalizedAssetPath = PoolEntry.NormalizeAssetPath(assetPath);
            PoolSpawnContext context = PoolSpawnContext.Create(normalizedAssetPath, parent);
            return GetGameObjectInternal(normalizedAssetPath, context);
        }

        public bool TryGetPoolAssetId(string assetPath, out PoolAssetId assetId)
        {
            string normalizedAssetPath = PoolEntry.NormalizeAssetPath(assetPath);
            ResolvedAssetRequest request = ResolveAssetRequest(normalizedAssetPath);
            assetId = new PoolAssetId(
                request.RuleIndex,
                _catalogVersion,
                request.RequestPath,
                request.LogicalPath,
                request.LoadPath,
                request.PoolKey,
                request.LoaderType);
            return assetId.IsValid;
        }

        public GameObject GetGameObject(PoolAssetId assetId, Transform parent = null)
        {
            if (!IsUsableAssetId(assetId))
            {
                return null;
            }

            PoolSpawnContext context = PoolSpawnContext.Create(assetId.LogicalPath, parent);
            return GetGameObjectInternal(assetId, context);
        }

        internal async UniTask<GameObject> GetGameObjectAsync(
            string assetPath,
            Transform parent = null,
            CancellationToken cancellationToken = default)
        {
            string normalizedAssetPath = PoolEntry.NormalizeAssetPath(assetPath);
            PoolSpawnContext context = PoolSpawnContext.Create(normalizedAssetPath, parent);
            return await GetGameObjectInternalAsync(normalizedAssetPath, context, cancellationToken);
        }

        public async UniTask<GameObject> GetGameObjectAsync(
            PoolAssetId assetId,
            Transform parent = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsUsableAssetId(assetId))
            {
                return null;
            }

            PoolSpawnContext context = PoolSpawnContext.Create(assetId.LogicalPath, parent);
            return await GetGameObjectInternalAsync(assetId, context, cancellationToken);
        }

        public void Release(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.TryGetComponent(out GameObjectPoolHandle handle) && handle.TryRelease())
            {
                return;
            }

            DestroyRuntimeObject(gameObject);
        }

        internal async UniTask PreloadAsync(string assetPath, int count = 1, CancellationToken cancellationToken = default)
        {
            await PreloadInternalAsync(PoolEntry.NormalizeAssetPath(assetPath), count, cancellationToken);
        }

        public async UniTask PreloadAsync(PoolAssetId assetId, int count = 1, CancellationToken cancellationToken = default)
        {
            await PreloadInternalAsync(assetId, count, cancellationToken);
        }

        private bool IsUsableAssetId(in PoolAssetId assetId)
        {
            return assetId.IsValid &&
                   assetId.CatalogVersion == _catalogVersion &&
                   (uint)assetId.RuleIndex < (uint)_catalog.RuleCount;
        }

        public void ForceCleanup()
        {
            float now = Time.time;
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
                if (pool != null)
                {
                    pool.ExecuteMaintenance(now, false);
                }
            }
        }

        public void ClearAllPools()
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

            for (int i = 0; i < _rulePoolMapInitialized.Length; i++)
            {
                if (_rulePoolMapInitialized[i])
                {
                    _rulePoolMaps[i].Clear();
                }
            }

            ClearGroupRoots();
            _directLoadWarnedPaths.Clear();
            ReleaseDebugSnapshots();
        }

        private void ReleaseRulePoolMaps()
        {
            for (int i = 0; i < _rulePoolMapInitialized.Length; i++)
            {
                if (_rulePoolMapInitialized[i])
                {
                    _rulePoolMaps[i].Dispose();
                }
            }

            _rulePoolMaps = Array.Empty<StringOpenHashMap>();
            _rulePoolMapInitialized = Array.Empty<bool>();
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
                false,
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
                    _debugSnapshots.Add(pool.CreateSnapshot());
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


        public void LoadCatalog(string poolConfigPath)
        {
            ClearAllPools();
            ReleaseRulePoolMaps();
            _catalog.Dispose();

            IResourceService resourceService = AppServices.Require<IResourceService>();
            PoolConfigScriptableObject configAsset = resourceService.LoadAsset<PoolConfigScriptableObject>(poolConfigPath);
            _catalog = configAsset == null ? PoolCompiledCatalog.Empty() : configAsset.BuildCatalog();
            if (configAsset != null)
            {
                resourceService.UnloadAsset(configAsset);
            }

            _rulePoolMaps = _catalog.RuleCount == 0 ? Array.Empty<StringOpenHashMap>() : new StringOpenHashMap[_catalog.RuleCount];
            _rulePoolMapInitialized = _catalog.RuleCount == 0 ? Array.Empty<bool>() : new bool[_catalog.RuleCount];
            unchecked
            {
                _catalogVersion++;
            }

            _enabled = _maintenanceCount > 0;
        }

        private GameObject GetGameObjectInternal(string assetPath, in PoolSpawnContext context)
        {
            ResolvedAssetRequest request = ResolveAssetRequest(assetPath);
            if (request.RuleIndex < 0)
            {
                WarnDirectLoadFallback(request.RequestPath, request.LogicalPath, request.LoaderType);
                return LoadDirect(request.LoadPath, context.Parent, request.LoaderType);
            }

            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(request.RuleIndex);
            RuntimeGameObjectPool pool = GetOrCreatePool(request.RuleIndex, request.PoolKey, request.LoadPath);
            return pool == null ? null : pool.Acquire(context.WithGroup(rule.group));
        }

        private GameObject GetGameObjectInternal(PoolAssetId assetId, in PoolSpawnContext context)
        {
            if (!IsUsableAssetId(assetId))
            {
                return null;
            }

            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(assetId.RuleIndex);
            RuntimeGameObjectPool pool = GetOrCreatePool(assetId.RuleIndex, assetId.PoolKey, assetId.LoadPath);
            return pool == null ? null : pool.Acquire(context.WithGroup(rule.group));
        }

        private async UniTask<GameObject> GetGameObjectInternalAsync(
            string assetPath,
            PoolSpawnContext context,
            CancellationToken cancellationToken)
        {
            ResolvedAssetRequest request = ResolveAssetRequest(assetPath);
            if (request.RuleIndex < 0)
            {
                WarnDirectLoadFallback(request.RequestPath, request.LogicalPath, request.LoaderType);
                return await LoadDirectAsync(request.LoadPath, context.Parent, request.LoaderType, cancellationToken);
            }

            string ruleGroup = _catalog.GetRule(request.RuleIndex).group;
            RuntimeGameObjectPool pool = GetOrCreatePool(request.RuleIndex, request.PoolKey, request.LoadPath);
            return pool == null ? null : await pool.AcquireAsync(context.WithGroup(ruleGroup), cancellationToken);
        }

        private async UniTask<GameObject> GetGameObjectInternalAsync(
            PoolAssetId assetId,
            PoolSpawnContext context,
            CancellationToken cancellationToken)
        {
            if (!IsUsableAssetId(assetId))
            {
                return null;
            }

            string ruleGroup = _catalog.GetRule(assetId.RuleIndex).group;
            RuntimeGameObjectPool pool = GetOrCreatePool(assetId.RuleIndex, assetId.PoolKey, assetId.LoadPath);
            return pool == null ? null : await pool.AcquireAsync(context.WithGroup(ruleGroup), cancellationToken);
        }

        private async UniTask PreloadInternalAsync(
            string assetPath,
            int count,
            CancellationToken cancellationToken)
        {
            if (count <= 0)
            {
                return;
            }

            ResolvedAssetRequest request = ResolveAssetRequest(assetPath);
            if (request.RuleIndex < 0)
            {
                WarnDirectLoadFallback(request.RequestPath, request.LogicalPath, request.LoaderType);
                return;
            }

            RuntimeGameObjectPool pool = GetOrCreatePool(request.RuleIndex, request.PoolKey, request.LoadPath);
            if (pool != null)
            {
                await pool.WarmupAsync(count, cancellationToken);
            }
        }

        private async UniTask PreloadInternalAsync(
            PoolAssetId assetId,
            int count,
            CancellationToken cancellationToken)
        {
            if (count <= 0 || !IsUsableAssetId(assetId))
            {
                return;
            }

            RuntimeGameObjectPool pool = GetOrCreatePool(assetId.RuleIndex, assetId.PoolKey, assetId.LoadPath);
            if (pool != null)
            {
                await pool.WarmupAsync(count, cancellationToken);
            }
        }

        private RuntimeGameObjectPool GetOrCreatePool(int ruleIndex, string poolKey, string loadPath)
        {
            if (!_rulePoolMapInitialized[ruleIndex])
            {
                _rulePoolMaps[ruleIndex] = new StringOpenHashMap(4);
                _rulePoolMapInitialized[ruleIndex] = true;
            }

            if (_rulePoolMaps[ruleIndex].TryGetValue(poolKey, out int poolIndex))
            {
                return _pools[poolIndex];
            }

            EnsurePoolCapacity(_poolCount + 1);
            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            var pool = MemoryPool.Acquire<RuntimeGameObjectPool>();
            IResourceLoader loader = GetResourceLoader(rule.loaderType);
            if (loader == null)
            {
                MemoryPool.Release(pool);
                return null;
            }

            pool.Initialize(this, _poolCount, rule, poolKey, loadPath, loader, GetOrCreateGroupRoot(rule.group));
            _pools[_poolCount] = pool;
            _rulePoolMaps[ruleIndex].AddOrUpdate(poolKey, _poolCount);
            _poolCount++;
            _enabled = true;
            return pool;
        }

        private GameObject LoadDirect(string assetPath, Transform parent, PoolResourceLoaderType loaderType)
        {
            IResourceLoader loader = GetResourceLoader(loaderType);
            return loader == null ? null : loader.LoadGameObject(assetPath, parent);
        }

        private async UniTask<GameObject> LoadDirectAsync(
            string assetPath,
            Transform parent,
            PoolResourceLoaderType loaderType,
            CancellationToken cancellationToken)
        {
            IResourceLoader loader = GetResourceLoader(loaderType);
            return loader == null ? null : await loader.LoadGameObjectAsync(assetPath, parent, cancellationToken);
        }

        private IResourceLoader GetResourceLoader(PoolResourceLoaderType loaderType)
        {
            int loaderIndex = (int)loaderType;
            if ((uint)loaderIndex >= (uint)_resourceLoaders.Length)
            {
                return null;
            }

            IResourceLoader loader = _resourceLoaders[(int)loaderType];
            return loader;
        }

        private void EnsureDefaultResourceLoaders()
        {
            if (_resourceLoaders[(int)PoolResourceLoaderType.AssetBundle] == null)
            {
                _resourceLoaders[(int)PoolResourceLoaderType.AssetBundle] = new AssetBundleResourceLoader();
            }

            if (_resourceLoaders[(int)PoolResourceLoaderType.Resources] == null)
            {
                _resourceLoaders[(int)PoolResourceLoaderType.Resources] = new UnityResourcesLoader();
            }
        }


        private Transform GetOrCreateGroupRoot(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DefaultGroup : group.Trim();
            if (_groupRootMap.TryGetValue(groupName, out int groupIndex))
            {
                Transform existingRoot = _groupRoots[groupIndex];
                if (existingRoot != null)
                {
                    return existingRoot;
                }
            }

            EnsureGroupRootCapacity(_groupRootCount + 1);
            GameObject rootObject = new GameObject(ZString.Format("[{0}]", groupName));
            Transform root = rootObject.transform;
            root.SetParent(_containerRoot, false);
            rootObject.SetActive(true);

            int newIndex = _groupRootCount++;
            _groupRoots[newIndex] = root;
            _groupRootMap.AddOrUpdate(groupName, newIndex);
            return root;
        }

        private void EnsureGroupRootCapacity(int required)
        {
            if (_groupRoots.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _groupRoots.Length << 1);
            var newRoots = new Transform[newCapacity];
            Array.Copy(_groupRoots, 0, newRoots, 0, _groupRootCount);
            _groupRoots = newRoots;
        }

        private void ClearGroupRoots()
        {
            for (int i = 0; i < _groupRootCount; i++)
            {
                Transform root = _groupRoots[i];
                if (root != null)
                {
                    DestroyRuntimeObject(root.gameObject);
                    _groupRoots[i] = null;
                }
            }

            _groupRootCount = 0;
            _groupRootMap.Clear();
        }

        private void WarnDirectLoadFallback(string assetPath, string logicalPath, PoolResourceLoaderType loaderType)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            return;
#else
            if (_directLoadWarnedPaths.TryGetValue(assetPath, out _))
            {
                return;
            }

            _directLoadWarnedPaths.AddOrUpdate(assetPath, 1);
            Log.Warning(ZString.Format(
                "[GameObjectPool] Asset not found in PoolConfig. Fallback to direct load and Release() will destroy it. Request:{0}, Logical:{1}, Loader:{2}",
                assetPath,
                string.IsNullOrEmpty(logicalPath) ? "<none>" : logicalPath,
                loaderType));
#endif
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(target);
                return;
            }
#endif
            UnityEngine.Object.Destroy(target);
        }

        private ResolvedAssetRequest ResolveAssetRequest(string requestPath)
        {
            ParsedRequestPath parsedRequest = ParseRequestPath(requestPath);
            if (parsedRequest.HasExplicitLoaderType)
            {
                return ResolveAssetRequest(parsedRequest.LogicalPath, parsedRequest.OriginalPath, parsedRequest.LoaderType);
            }

            ResolvedAssetRequest resourcesRequest = ResolveAssetRequest(parsedRequest.LogicalPath, parsedRequest.OriginalPath, PoolResourceLoaderType.Resources);
            bool resourcesMatched = resourcesRequest.RuleIndex >= 0;

            ResolvedAssetRequest assetBundleRequest = ResolveAssetRequest(parsedRequest.LogicalPath, parsedRequest.OriginalPath, PoolResourceLoaderType.AssetBundle);
            bool assetBundleMatched = assetBundleRequest.RuleIndex >= 0;

            if (resourcesMatched && assetBundleMatched)
            {
                Log.Error(ZString.Format(
                    "[GameObjectPool] Ambiguous logical path '{0}'. Both Resources and AssetBundle rules matched. Use 'res:' or 'ab:' prefix.",
                    parsedRequest.LogicalPath));
                return new ResolvedAssetRequest(-1, parsedRequest.OriginalPath, parsedRequest.LogicalPath, parsedRequest.LogicalPath, parsedRequest.LogicalPath, PoolResourceLoaderType.Resources);
            }

            if (resourcesMatched)
            {
                return resourcesRequest;
            }

            if (assetBundleMatched)
            {
                return assetBundleRequest;
            }

            if (TryResolveAssetBundleAssetPath(parsedRequest.LogicalPath, out _))
            {
                return new ResolvedAssetRequest(
                    -1,
                    parsedRequest.OriginalPath,
                    parsedRequest.LogicalPath,
                    parsedRequest.LogicalPath,
                    parsedRequest.LogicalPath,
                    PoolResourceLoaderType.AssetBundle);
            }

            return new ResolvedAssetRequest(
                -1,
                parsedRequest.OriginalPath,
                parsedRequest.LogicalPath,
                parsedRequest.LogicalPath,
                parsedRequest.LogicalPath,
                GuessDirectLoaderType(parsedRequest.LogicalPath));
        }

        private ResolvedAssetRequest ResolveAssetRequest(string logicalPath, string originalPath, PoolResourceLoaderType loaderType)
        {
            string resolvedLogicalPath = logicalPath;
            if (loaderType == PoolResourceLoaderType.AssetBundle &&
                TryResolveAssetBundleAssetPath(logicalPath, out string assetBundleAssetPath))
            {
                string normalizedAssetBundlePath = PoolEntry.NormalizeConfigAssetPath(assetBundleAssetPath, PoolResourceLoaderType.AssetBundle);
                if (!string.IsNullOrEmpty(normalizedAssetBundlePath))
                {
                    resolvedLogicalPath = normalizedAssetBundlePath;
                }
            }

            int ruleIndex = _catalog.Resolve(resolvedLogicalPath, loaderType, null);
            if (ruleIndex < 0)
            {
                return new ResolvedAssetRequest(-1, originalPath, resolvedLogicalPath, logicalPath, resolvedLogicalPath, loaderType);
            }

            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            return new ResolvedAssetRequest(
                ruleIndex,
                originalPath,
                resolvedLogicalPath,
                logicalPath,
                resolvedLogicalPath,
                rule.loaderType);
        }

        private static ParsedRequestPath ParseRequestPath(string requestPath)
        {
            const string assetBundlePrefix = "ab:";
            const string resourcesPrefix = "res:";

            if (requestPath.StartsWith(assetBundlePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string logicalPath = PoolEntry.NormalizeAssetPath(requestPath.Substring(assetBundlePrefix.Length));
                return new ParsedRequestPath(requestPath, logicalPath, true, PoolResourceLoaderType.AssetBundle);
            }

            if (requestPath.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string logicalPath = PoolEntry.NormalizeAssetPath(requestPath.Substring(resourcesPrefix.Length));
                return new ParsedRequestPath(requestPath, logicalPath, true, PoolResourceLoaderType.Resources);
            }

            return new ParsedRequestPath(requestPath, requestPath, false, default);
        }

        private bool TryResolveAssetBundleAssetPath(string location, out string assetPath)
        {
            assetPath = null;
            if (!AppServices.TryGet(out IResourceService resourceService))
            {
                return false;
            }

            var assetInfo = resourceService.GetAssetInfo(location);
            if (assetInfo == null || !string.IsNullOrEmpty(assetInfo.Error) || string.IsNullOrEmpty(assetInfo.AssetPath))
            {
                return false;
            }

            assetPath = assetInfo.AssetPath;
            return true;
        }

        private static PoolResourceLoaderType GuessDirectLoaderType(string requestPath)
        {
            if (requestPath.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase) ||
                requestPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PoolResourceLoaderType.Resources;
            }

            return requestPath.StartsWith("Assets/", StringComparison.Ordinal)
                ? PoolResourceLoaderType.AssetBundle
                : PoolResourceLoaderType.Resources;
        }


        private void OnLowMemory()
        {
            float now = Time.time;
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
                if (pool != null)
                {
                    pool.ExecuteMaintenance(now, true);
                }
            }
        }

        private void ProcessDueMaintenance(float now)
        {
            while (_maintenanceCount > 0)
            {
                MaintenanceNode node = _maintenanceHeap[0];
                if (node.dueTime > now)
                {
                    return;
                }

                RemoveMaintenanceAt(0);
                RuntimeGameObjectPool pool = _pools[node.poolIndex];
                pool?.ExecuteMaintenance(now, false);
            }
        }

        private void EnsurePoolCapacity(int required)
        {
            if (_pools.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _pools.Length << 1);
            var newPools = new RuntimeGameObjectPool[newCapacity];
            Array.Copy(_pools, 0, newPools, 0, _poolCount);
            _pools = newPools;
        }

        private void EnsureMaintenanceCapacity(int required)
        {
            if (_maintenanceHeap.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _maintenanceHeap.Length << 1);
            var newHeap = new MaintenanceNode[newCapacity];
            Array.Copy(_maintenanceHeap, 0, newHeap, 0, _maintenanceCount);
            _maintenanceHeap = newHeap;
        }

        private void RemoveMaintenanceAt(int heapIndex)
        {
            MaintenanceNode removed = _maintenanceHeap[heapIndex];
            RuntimeGameObjectPool removedPool = _pools[removed.poolIndex];
            removedPool?.SetMaintenanceHeapIndex(-1);

            int lastIndex = _maintenanceCount - 1;
            if (heapIndex != lastIndex)
            {
                MaintenanceNode moved = _maintenanceHeap[lastIndex];
                _maintenanceHeap[heapIndex] = moved;
                RuntimeGameObjectPool movedPool = _pools[moved.poolIndex];
                movedPool?.SetMaintenanceHeapIndex(heapIndex);
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

            int groupCompare = string.Compare(left.group, right.group, StringComparison.Ordinal);
            if (groupCompare != 0)
            {
                return groupCompare;
            }

            return string.Compare(left.assetPath, right.assetPath, StringComparison.Ordinal);
        }
    }
}
