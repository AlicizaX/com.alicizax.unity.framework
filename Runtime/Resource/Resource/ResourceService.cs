using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using AlicizaX;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 资源管理器。
    /// </summary>
    internal sealed partial class ResourceService : ServiceBase, IResourceService
    {
        /// <summary>
        /// 默认资源包名称。
        /// </summary>
        public string DefaultPackageName { get; set; } = "DefaultPackage";

        /// <summary>
        /// 资源系统运行模式。
        /// </summary>
        public EPlayMode PlayMode { get; set; } = EPlayMode.OfflinePlayMode;

        public string DecryptionServices { get; set; }

        /// <summary>
        /// 自动释放资源引用计数。。的资源包
        /// </summary>
        public bool AutoUnloadBundleWhenUnused { get; set; } = false;

        /// <summary>
        /// 设置异步系统参数，每帧执行消耗的最大时间切片（单位：毫秒）
        /// </summary>
        public long Milliseconds { get; set; } = 30;

        private string _applicableGameVersion;

        private int _internalResourceVersion;

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion => _applicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion => _internalResourceVersion;

        public IResourceBindingService BindingService => _bindingService;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public string PackageVersion { set; get; }

        public int DownloadingMaxNum { get; set; }

        public int FailedTryAgain { get; set; }

        #region internal

        /// <summary>
        /// 默认资源包。
        /// </summary>
        internal ResourcePackage DefaultPackage { private set; get; }

        /// <summary>
        /// 资源包列表。
        /// </summary>
        private Dictionary<string, ResourcePackage> PackageMap { get; } = new Dictionary<string, ResourcePackage>();

        /// <summary>
        /// 资源信息列表。
        /// </summary>
        private readonly Dictionary<AssetCacheKey, AssetInfo> _assetInfoMap = new Dictionary<AssetCacheKey, AssetInfo>(AssetCacheKeyComparer.Instance);

        /// <summary>
        /// 正在加载的资源任务。
        /// </summary>
        private readonly Dictionary<string, LoadingOperationState> _assetLoadingOperations = new Dictionary<string, LoadingOperationState>();

        private readonly Dictionary<AssetCacheKey, string> _assetLoadingKeyMap = new Dictionary<AssetCacheKey, string>(AssetCacheKeyComparer.Instance);

        private readonly Dictionary<AssetCacheKey, string> _subAssetsLoadingKeyMap = new Dictionary<AssetCacheKey, string>(AssetCacheKeyComparer.Instance);

        private const float ProgressCallbackThreshold = 0.01f;

        private readonly List<UnloadUnusedAssetsOperation> _unloadUnusedAssetsOperations = new List<UnloadUnusedAssetsOperation>();

        private readonly List<UnloadAllAssetsOperation> _unloadAllAssetsOperations = new List<UnloadAllAssetsOperation>();

        private bool _isDestroying;

        private int _assetUnloadGeneration;

        private ResourceBindingService _bindingService;

        private readonly struct AssetCacheKey
        {
            public readonly string PackageName;

            public readonly string Location;

            public AssetCacheKey(string packageName, string location)
            {
                PackageName = packageName ?? string.Empty;
                Location = location ?? string.Empty;
            }
        }

        private sealed class AssetCacheKeyComparer : IEqualityComparer<AssetCacheKey>
        {
            public static readonly AssetCacheKeyComparer Instance = new AssetCacheKeyComparer();

            private AssetCacheKeyComparer()
            {
            }

            public bool Equals(AssetCacheKey x, AssetCacheKey y)
            {
                return string.Equals(x.PackageName, y.PackageName, StringComparison.Ordinal) &&
                       string.Equals(x.Location, y.Location, StringComparison.Ordinal);
            }

            public int GetHashCode(AssetCacheKey obj)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(obj.PackageName ?? string.Empty);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(obj.Location ?? string.Empty);
                    return hash;
                }
            }
        }

        private sealed class LoadingOperationState : MemoryObject
        {
            public bool IsDone { get; private set; }
            public bool Succeeded { get; private set; }
            public int WaiterCount { get; private set; }
            public bool ReleaseRequested { get; private set; }

            public void AddWaiter()
            {
                WaiterCount++;
            }

            public void RemoveWaiter()
            {
                if (WaiterCount > 0)
                {
                    WaiterCount--;
                }
            }

            public void Complete(bool succeeded)
            {
                IsDone = true;
                Succeeded = succeeded;
            }

            public void RequestRelease()
            {
                ReleaseRequested = true;
            }

            public override void Clear()
            {
                IsDone = false;
                Succeeded = false;
                WaiterCount = 0;
                ReleaseRequested = false;
            }
        }

        #endregion

        public void Initialize()
        {
            _isDestroying = false;
            InitializeAssetRecords();
            _bindingService = new ResourceBindingService(this);
            // 初始化资源系。。
            YooAssets.Initialize(new ResourceLogger());

            YooAssets.SetOperationSystemMaxTimeSlice(Milliseconds);

            // 创建默认的资源包
            string packageName = DefaultPackageName;

            var defaultPackage = YooAssets.TryGetPackage(packageName);

            if (defaultPackage == null)

            {
                defaultPackage = YooAssets.CreatePackage(packageName);

                YooAssets.SetDefaultPackage(defaultPackage);
            }

            DefaultPackage = defaultPackage;

            PackageMap[packageName] = defaultPackage;

        }

        protected override void OnInitialize()
        {
        }

        protected override void OnDestroyService()
        {
            _isDestroying = true;
            PackageMap.Clear();
            ShutdownLoadingOperations();
            _unloadUnusedAssetsOperations.Clear();
            _unloadAllAssetsOperations.Clear();
            _assetInfoMap.Clear();
            _assetLoadingKeyMap.Clear();
            _subAssetsLoadingKeyMap.Clear();
            _bindingService?.Shutdown();
            _bindingService = null;
            ShutdownAssetRecords();
        }

        public UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                packageName = DefaultPackageName;
            }

            if (PackageMap.TryGetValue(packageName, out var resPackage))
            {
                if (resPackage.InitializeStatus is EOperationStatus.Processing or EOperationStatus.Succeed)
                {
                    Log.Error(ZString.Format("ResourceSystem has already init package : {0}", packageName));
                    return new UniTask<bool>(false);
                }
                else
                {
                    PackageMap.Remove(packageName);
                }
            }

            var taskCompletionSource = new UniTaskCompletionSource<bool>();
            GameFrameworkGuard.NotNull(packageName, nameof(packageName));
            GameFrameworkGuard.NotNull(hostServerURL, nameof(hostServerURL));
            GameFrameworkGuard.NotNull(fallbackHostServerURL, nameof(fallbackHostServerURL));
            // 创建默认的资源包
            var resourcePackage = YooAssets.TryGetPackage(packageName);
            if (resourcePackage == null)
            {
                resourcePackage = YooAssets.CreatePackage(packageName);
            }

            PackageMap[packageName] = resourcePackage;
            var initializationOperationHandler = CreateInitializationOperationHandler(resourcePackage, hostServerURL, fallbackHostServerURL, DecryptionServices);
            initializationOperationHandler.Completed += asyncOperationBase =>
            {
                if (asyncOperationBase.Error == null && asyncOperationBase.Status == EOperationStatus.Succeed && asyncOperationBase.IsDone)
                {
                    taskCompletionSource.TrySetResult(true);
                }
                else
                {
                    taskCompletionSource.TrySetException(new Exception(asyncOperationBase.Error));
                }
            };

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// 获取当前资源包版本。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源包版本。</returns>
        public string GetPackageVersion(string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);

            if (package == null)
            {
                return string.Empty;
            }

            return package.GetPackageVersion();
        }

        /// <summary>
        /// 异步更新最新包的版本。
        /// </summary>
        /// <param name="appendTimeTicks">请求URL是否需要带时间戳。</param>
        /// <param name="timeout">超时时间。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>请求远端包裹的最新版本操作句柄。</returns>
        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60,
            string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.RequestPackageVersionAsync(appendTimeTicks, timeout);
        }

        /// <summary>
        /// 向网络端请求并更新清。。
        /// </summary>
        /// <param name="packageVersion">更新的包裹版。。</param>
        /// <param name="timeout">超时时间（默认值：60秒）</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public UpdatePackageManifestOperation UpdatePackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.UpdatePackageManifestAsync(packageVersion, timeout);
        }

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            ResourcePackage package = GetPackageOrThrow(customPackageName);
            return package.CreateResourceDownloader(DownloadingMaxNum, FailedTryAgain);
        }

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        /// <param name="clearMode">文件清理方式。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ClearCacheFilesOperation ClearCacheFilesAsync(
            EFileClearMode clearMode = EFileClearMode.ClearUnusedBundleFiles,
            string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.ClearCacheFilesAsync(clearMode);
        }

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public void ClearAllBundleFiles(string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            package.ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles);
        }

        #region 资源回收

        public void OnLowMemory()
        {
            Log.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        /// <param name="action">低内存行为。</param>
        public void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public void UnloadUnusedAssets()
        {
            ReleaseAllUnusedAssetRecords();
            RemoveCompletedUnloadUnusedOperations();
            if (_unloadUnusedAssetsOperations.Count > 0)
            {
                return;
            }

            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeed })
                {
                    _unloadUnusedAssetsOperations.Add(package.UnloadUnusedAssetsAsync());
                }
            }
        }

        /// <summary>
        /// 强制回收所有资源。
        /// </summary>
        public void ForceUnloadAllAssets()
        {
#if UNITY_WEBGL
            Log.Warning(ZString.Format("WebGL not support invoke {0}", nameof(ForceUnloadAllAssets)));
			return;
#else
            RemoveCompletedUnloadAllOperations();
            if (_unloadAllAssetsOperations.Count > 0)
            {
                return;
            }

            unchecked
            {
                _assetUnloadGeneration++;
            }

            ShutdownLoadingOperations();
            _bindingService?.Shutdown();
            _bindingService = new ResourceBindingService(this);
            WarmupBindingRecords();
            ForceReleaseAllAssetRecords();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeed })
                {
                    _unloadAllAssetsOperations.Add(package.UnloadAllAssetsAsync());
                }
            }
#endif
        }

        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        private ResourcePackage GetPackageOrThrow(string packageName)
        {
            ResourcePackage package = string.IsNullOrEmpty(packageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(packageName);
            if (package == null)
            {
                throw new GameFrameworkException(ZString.Format("The package does not exist. Package Name :{0}", string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName));
            }

            return package;
        }

        private void RemoveCompletedUnloadUnusedOperations()
        {
            for (int i = _unloadUnusedAssetsOperations.Count - 1; i >= 0; i--)
            {
                UnloadUnusedAssetsOperation operation = _unloadUnusedAssetsOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _unloadUnusedAssetsOperations.RemoveAt(i);
                }
            }
        }

        private void RemoveCompletedUnloadAllOperations()
        {
            for (int i = _unloadAllAssetsOperations.Count - 1; i >= 0; i--)
            {
                UnloadAllAssetsOperation operation = _unloadAllAssetsOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _unloadAllAssetsOperations.RemoveAt(i);
                }
            }
        }

        #region Public Methods

        #region 获取资源信息

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.IsNeedDownloadFromRemote(location);
        }

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="assetInfo">资源信息。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(assetInfo);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.IsNeedDownloadFromRemote(assetInfo);
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tag">资源标签。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tag);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.GetAssetInfos(tag);
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tags">资源标签列表。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tags);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.GetAssetInfos(tags);
        }

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息。</returns>
        public AssetInfo GetAssetInfo(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (string.IsNullOrEmpty(packageName))
            {
                AssetCacheKey key = new AssetCacheKey(DefaultPackageName, location);
                if (_assetInfoMap.TryGetValue(key, out AssetInfo assetInfo))
                {
                    return assetInfo;
                }

                assetInfo = YooAssets.GetAssetInfo(location);
                _assetInfoMap[key] = assetInfo;
                return assetInfo;
            }
            else
            {
                AssetCacheKey key = new AssetCacheKey(packageName, location);
                if (_assetInfoMap.TryGetValue(key, out AssetInfo assetInfo))
                {
                    return assetInfo;
                }

                var package = YooAssets.GetPackage(packageName);
                if (package == null)
                {
                    throw new GameFrameworkException(ZString.Format("The package does not exist. Package Name :{0}", packageName));
                }

                assetInfo = package.GetAssetInfo(location);
                _assetInfoMap[key] = assetInfo;
                return assetInfo;
            }
        }

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>检查资源是否存在的结果。</returns>
        public HasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            if (!CheckLocationValid(location, packageName))
            {
                return HasAssetResult.NotExist;
            }

            if (assetInfo == null)
            {
                return HasAssetResult.NotExist;
            }

            if (IsNeedDownloadFromRemote(assetInfo))
            {
                return HasAssetResult.AssetOnline;
            }

            return HasAssetResult.AssetOnDisk;
        }

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        /// <param name="location">资源的定位地址</param>
        /// <param name="packageName">资源包名称。</param>
        public bool CheckLocationValid(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.CheckLocationValid(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package != null && package.CheckLocationValid(location);
        }

        #endregion

        #region 资源加载

        #region 获取资源句柄

        /// <summary>
        /// 获取同步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, assetType);
        }

        /// <summary>
        /// 获取异步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleAsync<T>(string location, string packageName = "", uint priority = 0) where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName, priority);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "", uint priority = 0)
        {
            if (string.IsNullOrEmpty(packageName))

            {
                return YooAssets.LoadAssetAsync(location, assetType, priority);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType, priority);
        }

        private static uint NormalizePriority(int priority)
        {
            return priority > 0 ? (uint)priority : 0u;
        }

        #endregion

        /// <summary>
        /// 获取资源定位地址的加载去重Key。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源定位地址的加载去重Key。</returns>
        private string GetLoadingOperationKey(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName) || packageName.Equals(DefaultPackageName))
            {
                return location;
            }

            AssetCacheKey key = new AssetCacheKey(packageName, location);

            if (_assetLoadingKeyMap.TryGetValue(key, out string cacheKey))
            {
                return cacheKey;
            }

            cacheKey = ZString.Concat(packageName, "/", location);
            _assetLoadingKeyMap[key] = cacheKey;
            return cacheKey;
        }

        private string GetSubAssetsLoadingOperationKey(string location, string packageName)
        {
            AssetCacheKey key = new AssetCacheKey(NormalizePackageName(packageName), location);
            if (_subAssetsLoadingKeyMap.TryGetValue(key, out string cacheKey))
            {
                return cacheKey;
            }

            cacheKey = ZString.Concat(key.PackageName, "/@subassets/", location);
            _subAssetsLoadingKeyMap[key] = cacheKey;
            return cacheKey;
        }

        public T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error(ZString.Format("Could not found location [{0}].", location));
                return null;
            }

            string normalizedPackageName = NormalizePackageName(packageName);
            Type assetType = typeof(T);
            ResourceAssetKind assetKind = InferAssetKind(assetType);
            if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind, ResourceHandleKind.AssetHandle, out int cachedAssetId, out UnityEngine.Object cachedAsset))
            {
                ref AssetSlot cachedSlot = ref GetAssetSlotRef(cachedAssetId);
                TryAddLegacyDirectRef(cachedAssetId, cachedSlot.Generation);
                return cachedAsset as T;
            }

            AssetHandle handle = GetHandleSync<T>(location, packageName: packageName);
            if (handle == null)
            {
                return null;
            }

            T ret = handle.AssetObject as T;
            if (ret == null)
            {
                DisposeHandle(handle);
                return null;
            }

            int assetId = GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                ResourceHandleKind.AssetHandle, handle.AssetObject, handle);
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            TryAddLegacyDirectRef(assetId, slot.Generation);

            return ret;
        }

        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error(ZString.Format("Could not found location [{0}].", location));
                return null;
            }

            ResourceLeaseHandle prefabLease = AcquirePrefabSourceLease(location, packageName);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (!TryGetLeaseAssetObject(prefabLease, out UnityEngine.Object prefabObject) || prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            ResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != ResourceBindStatus.Success)
            {
                UnityEngine.Object.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        public async UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Asset name is invalid.");
                return;
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error(ZString.Format("Could not found location [{0}].", location));
                callback?.Invoke(null);
                return;
            }

            string assetLoadingKey = GetLoadingOperationKey(location, packageName);
            var asset = await GetOrLoadAssetAsync(location, typeof(T), InferAssetKind(typeof(T)), packageName, assetLoadingKey);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(packageName, location, typeof(T), asset);
            }

            callback?.Invoke(asset as T);
        }

        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error(ZString.Format("Could not found location [{0}].", location));
                return null;
            }

            string assetLoadingKey = GetLoadingOperationKey(location, packageName);
            var asset = await GetOrLoadAssetAsync(location, typeof(T), InferAssetKind(typeof(T)), packageName, assetLoadingKey, cancellationToken: cancellationToken);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(packageName, location, typeof(T), asset);
            }

            return asset as T;
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error(ZString.Format("Could not found location [{0}].", location));
                return null;
            }

            ResourceLeaseHandle prefabLease = await AcquirePrefabSourceLeaseAsync(location, packageName, cancellationToken);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Release(prefabLease);
                return null;
            }

            if (!TryGetLeaseAssetObject(prefabLease, out UnityEngine.Object prefabObject) || prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            ResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != ResourceBindStatus.Success)
            {
                UnityEngine.Object.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        #endregion

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        public async UniTask LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                string errorMessage = ZString.Format("Could not found location [{0}].", location);
                Log.Error(errorMessage);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                }

                return;
            }

            string assetLoadingKey = GetLoadingOperationKey(location, packageName);
            float duration = Time.time;
            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            if (!string.IsNullOrEmpty(assetInfo.Error))
            {
                string errorMessage = ZString.Format("Can not load asset '{0}' because :'{1}'.", location, assetInfo.Error);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                    return;
                }

                throw new GameFrameworkException(errorMessage);
            }

            var asset = await GetOrLoadAssetAsync(location, assetType, InferAssetKind(assetType), packageName, assetLoadingKey, NormalizePriority(priority), default,
                loadAssetCallbacks.LoadAssetUpdateCallback, userData);

            if (asset == null)
            {
                string errorMessage = ZString.Format("Can not load asset '{0}'.", location);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, LoadResourceStatus.NotReady, errorMessage, userData);
                return;
            }

            TryAddLegacyDirectRefByKey(packageName, location, assetType, asset);
            loadAssetCallbacks.LoadAssetSuccessCallback?.Invoke(location, asset, Time.time - duration, userData);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        public async UniTask LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                string errorMessage = ZString.Format("Could not found location [{0}].", location);
                Log.Error(errorMessage);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                }

                return;
            }

            string assetLoadingKey = GetLoadingOperationKey(location, packageName);
            float duration = Time.time;
            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            if (!string.IsNullOrEmpty(assetInfo.Error))
            {
                string errorMessage = ZString.Format("Can not load asset '{0}' because :'{1}'.", location, assetInfo.Error);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                    return;
                }

                throw new GameFrameworkException(errorMessage);
            }

            var asset = await GetOrLoadAssetAsync(location, assetInfo.AssetType, InferAssetKind(assetInfo.AssetType), packageName, assetLoadingKey, NormalizePriority(priority), default,
                loadAssetCallbacks.LoadAssetUpdateCallback, userData);

            if (asset == null)
            {
                string errorMessage = ZString.Format("Can not load asset '{0}'.", location);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, LoadResourceStatus.NotReady, errorMessage, userData);
                return;
            }

            TryAddLegacyDirectRefByKey(packageName, location, assetInfo.AssetType, asset);
            loadAssetCallbacks.LoadAssetSuccessCallback?.Invoke(location, asset, Time.time - duration, userData);
        }

        private async UniTaskVoid InvokeProgress(string location, AssetHandle assetHandle, LoadAssetUpdateCallback loadAssetUpdateCallback, object userData, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetUpdateCallback != null)
            {
                float lastReportedProgress = -1f;
                while (assetHandle is { IsValid: true, IsDone: false })
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await UniTask.Yield();
                    float progress = assetHandle.Progress;
                    if (lastReportedProgress < 0f || progress - lastReportedProgress >= ProgressCallbackThreshold)
                    {
                        lastReportedProgress = progress;
                        loadAssetUpdateCallback.Invoke(location, progress, userData);
                    }
                }

                if (!cancellationToken.IsCancellationRequested && assetHandle is { IsValid: true } && lastReportedProgress < 1f)
                {
                    loadAssetUpdateCallback.Invoke(location, 1f, userData);
                }
            }
        }

        /// <summary>
        /// 获取同步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync<T>(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync<T>(location);
        }

        /// <summary>
        /// 获取异步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetAsync<T>(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync<T>(location);
        }

        #endregion

        private async UniTask<UnityEngine.Object> GetOrLoadAssetAsync(string location, Type assetType, ResourceAssetKind assetKind, string packageName,
            string assetLoadingKey, uint priority = 0, CancellationToken cancellationToken = default, LoadAssetUpdateCallback loadAssetUpdateCallback = null, object userData = null)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (_isDestroying)
                {
                    return null;
                }

                if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind, ResourceHandleKind.AssetHandle, out _, out UnityEngine.Object cachedAsset))
                {
                    return cachedAsset;
                }

                if (!TryBeginLoading(assetLoadingKey))
                {
                    if (!await WaitForLoadingAsync(assetLoadingKey, cancellationToken))
                    {
                        return null;
                    }

                    continue;
                }

                int loadGeneration = _assetUnloadGeneration;
                if (!IsLoadingStateCurrent(loadGeneration))
                {
                    FailLoading(assetLoadingKey, null);
                    return null;
                }

                AssetHandle handle = GetHandleAsync(location, assetType, packageName: packageName, priority: priority);
                if (handle == null)
                {
                    FailLoading(assetLoadingKey, null);
                    return null;
                }

                StartProgressTask(location, handle, loadAssetUpdateCallback, userData, cancellationToken);
                bool callerCancellationRequested = false;
                while (handle is { IsValid: true, IsDone: false })
                {
                    if (!IsLoadingStateCurrent(loadGeneration))
                    {
                        DisposeHandle(handle);
                        FailLoading(assetLoadingKey, null);
                        return null;
                    }

                    if (ShouldAbortLoadingAfterCallerCancellation(assetLoadingKey, cancellationToken, ref callerCancellationRequested))
                    {
                        DisposeHandle(handle);
                        FailLoading(assetLoadingKey, null);
                        return null;
                    }

                    await UniTask.Yield();
                }

                if (!IsLoadingStateCurrent(loadGeneration))
                {
                    DisposeHandle(handle);
                    FailLoading(assetLoadingKey, null);
                    return null;
                }

                if (handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                {
                    DisposeHandle(handle);
                    FailLoading(assetLoadingKey, null);
                    return null;
                }

                if (_isDestroying)
                {
                    DisposeHandle(handle);
                    FailLoading(assetLoadingKey, null);
                    return null;
                }

                if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind, ResourceHandleKind.AssetHandle, out _, out cachedAsset))
                {
                    DisposeHandle(handle);
                    CompleteLoading(assetLoadingKey);
                    return cachedAsset;
                }

                GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                    ResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                CompleteLoading(assetLoadingKey);
                return callerCancellationRequested ? null : handle.AssetObject as UnityEngine.Object;
            }
        }

        private bool TryBeginLoading(string assetObjectKey)
        {
            if (_assetLoadingOperations.TryGetValue(assetObjectKey, out _))
            {
                return false;
            }

            _assetLoadingOperations.Add(assetObjectKey, MemoryPool.Acquire<LoadingOperationState>());
            return true;
        }

        private async UniTask<bool> WaitForLoadingAsync(string assetObjectKey, CancellationToken cancellationToken = default)
        {
            if (!_assetLoadingOperations.TryGetValue(assetObjectKey, out var loadingOperation))
            {
                return true;
            }

            loadingOperation.AddWaiter();
            while (!loadingOperation.IsDone)
            {
                if (cancellationToken.IsCancellationRequested || _isDestroying)
                {
                    loadingOperation.RemoveWaiter();
                    ReleaseLoadingOperationIfReady(loadingOperation);
                    return false;
                }

                await UniTask.Yield();
            }

            bool succeeded = loadingOperation.Succeeded;
            loadingOperation.RemoveWaiter();
            ReleaseLoadingOperationIfReady(loadingOperation);
            return succeeded;
        }

        private void CompleteLoading(string assetObjectKey)
        {
            if (!_assetLoadingOperations.TryGetValue(assetObjectKey, out var loadingOperation))
            {
                return;
            }

            _assetLoadingOperations.Remove(assetObjectKey);
            loadingOperation.Complete(true);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private void FailLoading(string assetObjectKey, Exception exception)
        {
            if (!_assetLoadingOperations.TryGetValue(assetObjectKey, out var loadingOperation))

            {
                return;
            }

            _assetLoadingOperations.Remove(assetObjectKey);
            loadingOperation.Complete(false);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private void ShutdownLoadingOperations()
        {
            foreach (LoadingOperationState loadingOperation in _assetLoadingOperations.Values)
            {
                loadingOperation.Complete(false);
                loadingOperation.RequestRelease();
                ReleaseLoadingOperationIfReady(loadingOperation);
            }

            _assetLoadingOperations.Clear();
        }

        private bool IsLoadingStateCurrent(int loadGeneration)
        {
            return !_isDestroying && loadGeneration == _assetUnloadGeneration;
        }

        private bool ShouldAbortLoadingAfterCallerCancellation(string assetObjectKey, CancellationToken cancellationToken, ref bool callerCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                callerCancellationRequested = true;
            }

            return callerCancellationRequested && !HasLoadingWaiters(assetObjectKey);
        }

        private bool HasLoadingWaiters(string assetObjectKey)
        {
            return _assetLoadingOperations.TryGetValue(assetObjectKey, out LoadingOperationState loadingOperation) &&
                   loadingOperation.WaiterCount > 0;
        }

        private static void ReleaseLoadingOperationIfReady(LoadingOperationState loadingOperation)
        {
            if (loadingOperation is { ReleaseRequested: true, WaiterCount: 0 })
            {
                MemoryPool.Release(loadingOperation);
            }
        }

        private void DisposeHandle(AssetHandle handle)
        {
            if (handle is { IsValid: true })
            {
                handle.Dispose();
            }
        }

        private void StartProgressTask(string location, AssetHandle handle, LoadAssetUpdateCallback loadAssetUpdateCallback, object userData, CancellationToken cancellationToken)
        {
            if (loadAssetUpdateCallback != null && handle is { IsValid: true, IsDone: false })
            {
                InvokeProgress(location, handle, loadAssetUpdateCallback, userData, cancellationToken).Forget();
            }
        }

        private ResourceOwner EnsureResourceOwner(GameObject root)
        {
            ResourceOwner owner = root.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = root.AddComponent<ResourceOwner>();
            }

            _bindingService.RegisterOwner(owner);
            return owner;
        }

        #endregion

        #region 设置下载系统参数，自定义下载请求

        /// <summary>
        /// 设置下载系统参数，自定义下载请求。
        /// </summary>
        /// <param name="downloadSystemUnityWebRequest">自定义下载器的请求委托，<see cref="UnityWebRequestDelegate"/></param>
        public void SetDownloadSystemUnityWebRequest(UnityWebRequestDelegate downloadSystemUnityWebRequest)
        {
            YooAssets.SetDownloadSystemUnityWebRequest(downloadSystemUnityWebRequest);
        }

        public UnityEngine.Networking.UnityWebRequest CustomWebRequester(string url)
        {
            var request = new UnityEngine.Networking.UnityWebRequest(url, UnityEngine.Networking.UnityWebRequest.kHttpVerbGET);
            var authorization = GetAuthorization("Admin", "12345");
            request.SetRequestHeader("AUTHORIZATION", authorization);
            return request;
        }

        private string GetAuthorization(string userName, string password)
        {
            string auth = ZString.Concat(userName, ":", password);
            var bytes = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(auth);
            return ZString.Concat("Basic ", Convert.ToBase64String(bytes));
        }

        #endregion
    }
}
