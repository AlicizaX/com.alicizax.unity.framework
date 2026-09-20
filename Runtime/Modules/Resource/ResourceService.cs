using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ResourceUlongIntMap _assetInfoByKey = new ResourceUlongIntMap();

        private AssetInfoSlot[][] _assetInfoSlotPages;

        private int _assetInfoSlotNextIndex;

        /// <summary>
        /// 正在加载的资源任务。
        /// </summary>
        private readonly Dictionary<ulong, LoadingOperationState> _assetLoadingOperationByKey = new Dictionary<ulong, LoadingOperationState>();

        private readonly Dictionary<string, TaskCompletionSource<bool>> _packageInitTasks = new Dictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _resourcePackageIds = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _resourceLocationIds = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly Dictionary<Type, int> _resourceTypeIds = new Dictionary<Type, int>();

        private string[] _resourcePackagesById;

        private string[] _resourceLocationsById;

        private Type[] _resourceTypesById;

        private int[] _resourcePackageRefCounts;

        private int[] _resourceLocationRefCounts;

        private int[] _resourceTypeRefCounts;

        private int _nextResourcePackageId = 1;

        private int _nextResourceLocationId = 1;

        private int _nextResourceTypeId = 1;

        private int _freeResourcePackageId;

        private int _freeResourceLocationId;

        private int _freeResourceTypeId;

        private const float ProgressCallbackThreshold = 0.01f;

        private const int ResourceKeyHandleBits = 4;
        private const int ResourceKeyAssetKindBits = 4;
        private const int ResourceKeyTypeBits = 12;
        private const int ResourceKeyLocationBits = 32;
        private const int ResourceKeyPackageBits = 12;
        private const int ResourceKeyHandleShift = 0;
        private const int ResourceKeyAssetKindShift = ResourceKeyHandleShift + ResourceKeyHandleBits;
        private const int ResourceKeyTypeShift = ResourceKeyAssetKindShift + ResourceKeyAssetKindBits;
        private const int ResourceKeyLocationShift = ResourceKeyTypeShift + ResourceKeyTypeBits;
        private const int ResourceKeyPackageShift = ResourceKeyLocationShift + ResourceKeyLocationBits;
        private const int ResourceKeyPackageMax = (1 << ResourceKeyPackageBits) - 1;
        private const int ResourceKeyLocationMax = int.MaxValue;
        private const int ResourceKeyTypeMax = (1 << ResourceKeyTypeBits) - 1;
        private const int ResourceKeyAssetKindMax = (1 << ResourceKeyAssetKindBits) - 1;
        private const int ResourceKeyHandleMax = (1 << ResourceKeyHandleBits) - 1;

        private readonly List<UnloadUnusedAssetsOperation> _unloadUnusedAssetsOperations = new List<UnloadUnusedAssetsOperation>();

        private readonly List<UnloadAllAssetsOperation> _unloadAllAssetsOperations = new List<UnloadAllAssetsOperation>();

        private readonly List<LoadPackageManifestOperation> _manifestUpdateOperations = new List<LoadPackageManifestOperation>();

        private bool _isDestroying;
        private bool _isResetting;

        public bool CanLoadResources => _bindingService != null && !_isDestroying && !_isResetting && Loader.IsAvailable;

        private int _assetUnloadGeneration;

        private ResourceBindingService _bindingService;

        internal IResourceLoader Loader { get; set; } = new YooResourceLoader();

        #endregion

        public void Initialize()
        {
            if (_bindingService != null)
                throw new InvalidOperationException("Resource service is already initialized.");
            _isDestroying = false;
            _isResetting = false;
            InitializeAssetRecords();
            _bindingService = new ResourceBindingService(this);
            // 初始化资源系。。
            if (!YooAssets.IsInitialized)
                YooAssets.Initialize(new ResourceLogger());

            YooAssets.SetAsyncOperationMaxTimeSlice(Milliseconds);

            // 创建默认的资源包
            string packageName = DefaultPackageName;

            if (!YooAssets.TryGetPackage(packageName, out var defaultPackage))

            {
                defaultPackage = YooAssets.CreatePackage(packageName);
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
            _assetUnloadGeneration++;
            foreach (var source in _packageInitTasks.Values)
                source.TrySetResult(false);
            _packageInitTasks.Clear();
            ShutdownLoadingOperations();
            _unloadUnusedAssetsOperations.Clear();
            _unloadAllAssetsOperations.Clear();
            _manifestUpdateOperations.Clear();
            try
            {
                _bindingService?.Shutdown();
            }
            finally
            {
                _bindingService = null;
                ShutdownAssetRecords();
                ClearAssetInfoCache();
                ClearResourceKeyRegistry();
                _forceUnloadUnusedAssetsAction = null;
                UnloadUnusedAssets();
                PackageMap.Clear();
                DefaultPackage = null;
                _unloadUnusedAssetsOperations.Clear();
            }
        }

        public UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
        {
            if (!CanLoadResources)
                return new UniTask<bool>(false);
            if (string.IsNullOrEmpty(packageName))
            {
                packageName = DefaultPackageName;
            }

            if (_packageInitTasks.TryGetValue(packageName, out TaskCompletionSource<bool> runningTask))
            {
                return runningTask.Task.AsUniTask();
            }

            if (PackageMap.TryGetValue(packageName, out var resPackage))
            {
                if (resPackage.InitializeStatus == EOperationStatus.Succeeded)
                {
                    RefreshPackageVersion(resPackage);
                    return new UniTask<bool>(true);
                }

                if (resPackage.InitializeStatus == EOperationStatus.Processing)
                {
                    TaskCompletionSource<bool> waitSource = CreatePackageInitSource(packageName);
                    AwaitExistingPackageInitialization(packageName, resPackage, waitSource).Forget();
                    return waitSource.Task.AsUniTask();
                }

                PackageMap.Remove(packageName);
            }

            if (PlayMode is EPlayMode.HostPlayMode or EPlayMode.WebPlayMode)
            {
                GameFrameworkGuard.NotNullOrEmpty(hostServerURL, nameof(hostServerURL));
            }

            hostServerURL ??= string.Empty;
            fallbackHostServerURL ??= string.Empty;
            if (!YooAssets.TryGetPackage(packageName, out var resourcePackage))
            {
                resourcePackage = YooAssets.CreatePackage(packageName);
            }

            PackageMap[packageName] = resourcePackage;
            var initializationOperationHandler = CreateInitializationOperationHandler(resourcePackage, hostServerURL, fallbackHostServerURL, DecryptionServices);
            if (initializationOperationHandler == null)
            {
                PackageMap.Remove(packageName);
                return new UniTask<bool>(false);
            }

            TaskCompletionSource<bool> initSource = CreatePackageInitSource(packageName);
            AwaitPackageInitialization(packageName, resourcePackage, initializationOperationHandler, initSource).Forget();
            return initSource.Task.AsUniTask();
        }

        private TaskCompletionSource<bool> CreatePackageInitSource(string packageName)
        {
            TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();
            _packageInitTasks[packageName] = source;
            return source;
        }

        private async UniTaskVoid AwaitExistingPackageInitialization(string packageName, ResourcePackage resourcePackage, TaskCompletionSource<bool> completionSource)
        {
            try
            {
                while (!_isDestroying && resourcePackage != null && resourcePackage.InitializeStatus == EOperationStatus.Processing)
                {
                    await UniTask.Yield();
                }

                if (resourcePackage != null && resourcePackage.InitializeStatus == EOperationStatus.Succeeded)
                {
                    RefreshPackageVersion(resourcePackage);
                    completionSource.TrySetResult(true);
                    return;
                }

                completionSource.TrySetResult(false);
            }
            finally
            {
                _packageInitTasks.Remove(packageName);
            }
        }

        private async UniTaskVoid AwaitPackageInitialization(string packageName, ResourcePackage resourcePackage, InitializePackageOperation initializationOperationHandler, TaskCompletionSource<bool> completionSource)
        {
            try
            {
                await initializationOperationHandler.ToUniTask();
                if (initializationOperationHandler.Status == EOperationStatus.Succeeded)
                {
                    RefreshPackageVersion(resourcePackage);
                    completionSource.TrySetResult(true);
                    return;
                }

                Log.Error(initializationOperationHandler.Error);
                PackageMap.Remove(packageName);
                completionSource.TrySetResult(false);
            }
            catch (Exception exception)
            {
                Log.Error(exception.Message);
                PackageMap.Remove(packageName);
                completionSource.TrySetResult(false);
            }
            finally
            {
                _packageInitTasks.Remove(packageName);
            }
        }

        private void RefreshPackageVersion(ResourcePackage resourcePackage)
        {
            if (resourcePackage == null || !resourcePackage.PackageValid)
            {
                return;
            }

            string packageVersion = resourcePackage.GetPackageVersion();
            PackageVersion = packageVersion;
            _applicableGameVersion = Application.version;
            _internalResourceVersion = ParseInternalResourceVersion(packageVersion);
        }

        private static int ParseInternalResourceVersion(string packageVersion)
        {
            if (string.IsNullOrEmpty(packageVersion))
            {
                return 0;
            }

            int hash = 23;
            for (int i = 0; i < packageVersion.Length; i++)
            {
                hash = hash * 31 + packageVersion[i];
            }

            return hash;
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
            var options = new RequestPackageVersionOptions(appendTimeTicks, timeout);
            return package.RequestPackageVersionAsync(options);
        }

        /// <summary>
        /// 向网络端请求并更新清。。
        /// </summary>
        /// <param name="packageVersion">更新的包裹版。。</param>
        /// <param name="timeout">超时时间（默认值：60秒）</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public LoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            ClearAssetInfoCache();
            var options = new LoadPackageManifestOptions(packageVersion, timeout);
            LoadPackageManifestOperation operation = package.LoadPackageManifestAsync(options);
            TrackManifestUpdateOperation(operation);
            WatchManifestUpdateOperation(operation).Forget();
            return operation;
        }

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            ResourcePackage package = GetPackageOrThrow(customPackageName);
            var options = new ResourceDownloaderOptions(DownloadingMaxNum, FailedTryAgain);
            return package.CreateResourceDownloader(options);
        }

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        /// <param name="clearMode">文件清理方式。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ClearCacheOperation ClearCacheAsync(ClearCacheOptions options, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.ClearCacheAsync(options);
        }

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public void ClearAllBundleFiles(string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            package.ClearCacheAsync(new ClearCacheOptions(ClearCacheMethods.ClearAllBundleFiles));
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
            UnloadUnusedAssets(false);
        }

        public void UnloadUnusedAssets(bool force)
        {
            if (force)
            {
                ReleaseAllUnusedAssetRecords();
            }

            RemoveCompletedUnloadUnusedOperations();
            if (_unloadUnusedAssetsOperations.Count > 0)
            {
                return;
            }

            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true })
                {
                    _unloadUnusedAssetsOperations.Add(package.UnloadUnusedAssetsAsync());
                }
            }
        }

        private struct AssetInfoSlot
        {
            public ulong Key;
            public AssetInfo AssetInfo;
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
            if (_isDestroying || _isResetting) return;
            _isResetting = true;
            try
            {
                unchecked { _assetUnloadGeneration++; }
                ShutdownLoadingOperations();
                try
                {
                    _bindingService?.Reset();
                }
                finally
                {
                    if (_isDestroying)
                        _bindingService = null;
                    ForceReleaseAllAssetRecords();
                    WarmupBindingRecords();
                    foreach (var package in PackageMap.Values)
                    {
                        if (package is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true })
                            _unloadAllAssetsOperations.Add(package.UnloadAllAssetsAsync());
                    }
                }
            }
            finally
            {
                _isResetting = _unloadAllAssetsOperations.Count > 0;
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
            if (_unloadAllAssetsOperations.Count == 0) _isResetting = false;
        }

        private void TrackManifestUpdateOperation(LoadPackageManifestOperation operation)
        {
            if (operation == null || operation.IsDone)
            {
                return;
            }

            _manifestUpdateOperations.Add(operation);
        }

        private bool IsManifestUpdateInProgress()
        {
            bool inProgress = false;
            for (int i = _manifestUpdateOperations.Count - 1; i >= 0; i--)
            {
                LoadPackageManifestOperation operation = _manifestUpdateOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _manifestUpdateOperations.RemoveAt(i);
                    continue;
                }

                inProgress = true;
            }

            return inProgress;
        }

        private async UniTaskVoid WatchManifestUpdateOperation(LoadPackageManifestOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            while (!_isDestroying && !operation.IsDone)
            {
                await UniTask.Yield();
            }

            _manifestUpdateOperations.Remove(operation);
            ClearAssetInfoCache();
            if (operation.Status == EOperationStatus.Succeeded &&
                YooAssets.TryGetPackage(DefaultPackageName, out ResourcePackage defaultPackage))
            {
                RefreshPackageVersion(defaultPackage);
            }
        }

        #region Public Methods

        #region 获取资源信息

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        public long GetDownloadSize(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(location);
        }

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="assetInfo">资源信息。</param>
        /// <param name="packageName">资源包名称。</param>
        public long GetDownloadSize(AssetInfo assetInfo, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(assetInfo);
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tag">资源标签。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetAssetInfos(tag);
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tags">资源标签列表。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetAssetInfos(tags);
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

            bool cacheEnabled = !IsManifestUpdateInProgress();
            string normalizedPackageName = NormalizePackageName(packageName);
            if (cacheEnabled &&
                TryGetAssetInfoKey(normalizedPackageName, location, out ulong assetInfoKey) &&
                TryGetCachedAssetInfo(assetInfoKey, out AssetInfo cachedAssetInfo))
            {
                return cachedAssetInfo;
            }

            AssetInfo assetInfo = GetPackageOrThrow(packageName).GetAssetInfo(location);
            if (cacheEnabled && CanCacheAssetInfo(assetInfo))
            {
                assetInfoKey = GetAssetInfoKey(normalizedPackageName, location);
                SetCachedAssetInfo(assetInfoKey, assetInfo);
            }

            return assetInfo;
        }

        private static bool CanCacheAssetInfo(AssetInfo assetInfo)
        {
            return assetInfo != null && string.IsNullOrEmpty(assetInfo.Error);
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
            if (assetInfo == null || !assetInfo.IsValid || !string.IsNullOrEmpty(assetInfo.Error))
            {
                return HasAssetResult.NotExist;
            }

            if (GetDownloadSize(assetInfo, packageName) > 0)
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
        public bool IsLocationValid(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).IsLocationValid(location);
        }

        #endregion

        #region 资源加载

        #region 获取资源句柄

        private static uint NormalizePriority(int priority)
        {
            return priority > 0 ? (uint)priority : 0u;
        }

        #endregion

        private ulong GetAssetRecordKey(string packageName, string location, Type assetType, ResourceAssetKind assetKind, ResourceHandleKind handleKind)
        {
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            return PackResourceKey(
                GetOrAddPackageId(NormalizePackageName(packageName)),
                GetOrAddLocationId(location),
                GetOrAddTypeId(assetType),
                assetKind,
                handleKind);
        }

        private ulong GetAssetInfoKey(string packageName, string location)
        {
            return GetAssetRecordKey(packageName, location, typeof(UnityEngine.Object), ResourceAssetKind.Asset, ResourceHandleKind.None);
        }

        private bool TryGetAssetInfoKey(string packageName, string location, out ulong key)
        {
            return TryGetResourceKey(packageName, location, typeof(UnityEngine.Object), ResourceAssetKind.Asset, ResourceHandleKind.None, out key);
        }

        private bool TryGetResourceKey(string packageName, string location, Type assetType, ResourceAssetKind assetKind, ResourceHandleKind handleKind, out ulong key)
        {
            key = 0;
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            if (!_resourcePackageIds.TryGetValue(NormalizePackageName(packageName), out int packageId) ||
                !_resourceLocationIds.TryGetValue(location ?? string.Empty, out int locationId) ||
                !_resourceTypeIds.TryGetValue(assetType, out int typeId))
            {
                return false;
            }

            key = PackResourceKey(packageId, locationId, typeId, assetKind, handleKind);
            return true;
        }

        private int GetOrAddPackageId(string packageName)
        {
            packageName = NormalizePackageName(packageName);
            if (_resourcePackageIds.TryGetValue(packageName, out int id))
            {
                return id;
            }

            id = AllocateResourceId(ref _nextResourcePackageId, ResourceKeyPackageMax, ref _freeResourcePackageId, _resourcePackageRefCounts);
            _resourcePackageIds.Add(packageName, id);
            ReserveResourceName(ref _resourcePackagesById, ref _resourcePackageRefCounts, id);
            _resourcePackagesById[id] = packageName;
            return id;
        }

        private int GetOrAddLocationId(string location)
        {
            location ??= string.Empty;
            if (_resourceLocationIds.TryGetValue(location, out int id))
            {
                return id;
            }

            id = AllocateResourceId(ref _nextResourceLocationId, ResourceKeyLocationMax, ref _freeResourceLocationId, _resourceLocationRefCounts);
            _resourceLocationIds.Add(location, id);
            ReserveResourceName(ref _resourceLocationsById, ref _resourceLocationRefCounts, id);
            _resourceLocationsById[id] = location;
            return id;
        }

        private int GetOrAddTypeId(Type assetType)
        {
            assetType ??= typeof(UnityEngine.Object);
            if (_resourceTypeIds.TryGetValue(assetType, out int id))
            {
                return id;
            }

            id = AllocateResourceId(ref _nextResourceTypeId, ResourceKeyTypeMax, ref _freeResourceTypeId, _resourceTypeRefCounts);
            _resourceTypeIds.Add(assetType, id);
            ReserveResourceType(id);
            _resourceTypesById[id] = assetType;
            return id;
        }

        private void ClearResourceKeyRegistry()
        {
            _resourcePackageIds.Clear();
            _resourceLocationIds.Clear();
            _resourceTypeIds.Clear();
            _resourcePackagesById = null;
            _resourceLocationsById = null;
            _resourceTypesById = null;
            _resourcePackageRefCounts = null;
            _resourceLocationRefCounts = null;
            _resourceTypeRefCounts = null;
            _freeResourcePackageId = 0;
            _freeResourceLocationId = 0;
            _freeResourceTypeId = 0;
            _nextResourcePackageId = 1;
            _nextResourceLocationId = 1;
            _nextResourceTypeId = 1;
        }

        private void RetainResourceKey(ulong key)
        {
            _resourcePackageRefCounts[UnpackPackageId(key)]++;
            _resourceLocationRefCounts[UnpackLocationId(key)]++;
            _resourceTypeRefCounts[UnpackTypeId(key)]++;
        }

        private void ReleaseResourceKey(ulong key)
        {
            ReleasePackageId(UnpackPackageId(key));
            ReleaseLocationId(UnpackLocationId(key));
            ReleaseTypeId(UnpackTypeId(key));
        }

        private void ReleasePackageId(int id)
        {
            if (--_resourcePackageRefCounts[id] != 0)
            {
                return;
            }

            _resourcePackageIds.Remove(_resourcePackagesById[id]);
            _resourcePackagesById[id] = null;
            FreeResourceId(id, _resourcePackageRefCounts, ref _freeResourcePackageId);
        }

        private void ReleaseLocationId(int id)
        {
            if (--_resourceLocationRefCounts[id] != 0)
            {
                return;
            }

            _resourceLocationIds.Remove(_resourceLocationsById[id]);
            _resourceLocationsById[id] = null;
            FreeResourceId(id, _resourceLocationRefCounts, ref _freeResourceLocationId);
        }

        private void ReleaseTypeId(int id)
        {
            if (--_resourceTypeRefCounts[id] != 0)
            {
                return;
            }

            _resourceTypeIds.Remove(_resourceTypesById[id]);
            _resourceTypesById[id] = null;
            FreeResourceId(id, _resourceTypeRefCounts, ref _freeResourceTypeId);
        }

        private static int UnpackPackageId(ulong key)
        {
            return (int)((key >> ResourceKeyPackageShift) & ResourceKeyPackageMax);
        }

        private static int UnpackLocationId(ulong key)
        {
            return (int)((key >> ResourceKeyLocationShift) & ResourceKeyLocationMax);
        }

        private static int UnpackTypeId(ulong key)
        {
            return (int)((key >> ResourceKeyTypeShift) & ResourceKeyTypeMax);
        }

        private string GetPackageNameById(int id)
        {
            return _resourcePackagesById != null && id > 0 && id < _resourcePackagesById.Length ? _resourcePackagesById[id] : string.Empty;
        }

        private string GetLocationNameById(int id)
        {
            return _resourceLocationsById != null && id > 0 && id < _resourceLocationsById.Length ? _resourceLocationsById[id] : string.Empty;
        }

        private Type GetAssetTypeById(int id)
        {
            return _resourceTypesById != null && id > 0 && id < _resourceTypesById.Length ? _resourceTypesById[id] : null;
        }

        private static void ReserveResourceName(ref string[] values, ref int[] refCounts, int id)
        {
            GrowResourceArray(ref values, id);
            GrowResourceArray(ref refCounts, id);
        }

        private void ReserveResourceType(int id)
        {
            GrowResourceArray(ref _resourceTypesById, id);
            GrowResourceArray(ref _resourceTypeRefCounts, id);
        }

        private static void GrowResourceArray<T>(ref T[] array, int index)
        {
            if (array == null)
            {
                array = new T[Math.Max(16, index + 1)];
                return;
            }

            if (index < array.Length)
            {
                return;
            }

            Array.Resize(ref array, Math.Max(index + 1, array.Length << 1));
        }

        private static int AllocateResourceId(ref int nextId, int maxId, ref int freeId, int[] refCounts)
        {
            if (freeId > 0)
            {
                int id = freeId;
                freeId = -refCounts[id];
                refCounts[id] = 0;
                return id;
            }

            if (nextId <= 0 || nextId > maxId)
            {
                throw new GameFrameworkException("Resource key id range exceeded.");
            }

            return nextId++;
        }

        private static void FreeResourceId(int id, int[] refCounts, ref int freeId)
        {
            refCounts[id] = -freeId;
            freeId = id;
        }

        private static ulong PackResourceKey(int packageId, int locationId, int typeId, ResourceAssetKind assetKind, ResourceHandleKind handleKind)
        {
            if (packageId <= 0 ||
                locationId <= 0 ||
                typeId <= 0 ||
                packageId > ResourceKeyPackageMax ||
                locationId > ResourceKeyLocationMax ||
                typeId > ResourceKeyTypeMax ||
                (uint)assetKind > ResourceKeyAssetKindMax ||
                (uint)handleKind > ResourceKeyHandleMax)
            {
                throw new GameFrameworkException("Resource key id range exceeded.");
            }

            return ((ulong)(uint)packageId << ResourceKeyPackageShift) |
                   ((ulong)(uint)locationId << ResourceKeyLocationShift) |
                   ((ulong)(uint)typeId << ResourceKeyTypeShift) |
                   ((ulong)(byte)assetKind << ResourceKeyAssetKindShift) |
                   ((ulong)(byte)handleKind << ResourceKeyHandleShift);
        }

        private bool TryGetCachedAssetInfo(ulong key, out AssetInfo assetInfo)
        {
            assetInfo = default;
            if (!_assetInfoByKey.TryGetValue(key, out int slotIndex))
            {
                return false;
            }
            assetInfo = GetAssetInfoSlotRef(slotIndex).AssetInfo;
            return true;
        }

        private void SetCachedAssetInfo(ulong key, AssetInfo assetInfo)
        {
            int slotIndex = _assetInfoSlotNextIndex++;
            ReserveAssetInfoPage(slotIndex);
            ref AssetInfoSlot slot = ref GetAssetInfoSlotRef(slotIndex);
            slot.Key = key;
            slot.AssetInfo = assetInfo;
            _assetInfoByKey.Set(key, slotIndex);
            RetainResourceKey(key);
        }

        private void ClearAssetInfoCache()
        {
            for (int i = 0; i < _assetInfoSlotNextIndex; i++)
                ReleaseResourceKey(GetAssetInfoSlotRef(i).Key);
            _assetInfoByKey.Clear();
            _assetInfoSlotPages = null;
            _assetInfoSlotNextIndex = 0;
        }

        private ref AssetInfoSlot GetAssetInfoSlotRef(int index)
        {
            return ref _assetInfoSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private void ReserveAssetInfoPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
            if (_assetInfoSlotPages == null)
            {
                _assetInfoSlotPages = new AssetInfoSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _assetInfoSlotPages.Length)
            {
                Array.Resize(ref _assetInfoSlotPages, Math.Max(pageIndex + 1, _assetInfoSlotPages.Length << 1));
            }

            if (_assetInfoSlotPages[pageIndex] == null)
            {
                _assetInfoSlotPages[pageIndex] = new AssetInfoSlot[RecordPageSize];
            }
        }

        public T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            ValidateLocation(location);
            ResourceLeaseHandle handle = AcquireLeaseSync(new ResourceKey(location, packageName, typeof(T)), ResourceLeaseKind.Legacy);
            if (TryGetLeaseAsset(handle, out UnityEngine.Object asset) && asset is T typedAsset)
                return typedAsset;
            Release(handle);
            return null;
        }

        public ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : UnityEngine.Object
        {
            return CreateTypedLease<T>(AcquireDirect(TypedKey<T>(key)));
        }

        public ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadLease<T>(new ResourceKey(location, packageName, typeof(T)));
        }

        public async UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            ResourceLeaseHandle handle = await AcquireDirectAsync(TypedKey<T>(key), cancellationToken);
            return CreateTypedLease<T>(handle);
        }

        public UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object
        {
            return LoadLeaseAsync<T>(new ResourceKey(location, packageName, typeof(T)), cancellationToken);
        }

        private static ResourceKey TypedKey<T>(in ResourceKey key) where T : UnityEngine.Object
        {
            return key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(T), key.AssetKind)
                : key;
        }

        private ResourceAssetLease<T> CreateTypedLease<T>(ResourceLeaseHandle handle) where T : UnityEngine.Object
        {
            if (TryGetLeaseAsset(handle, out UnityEngine.Object asset) && asset is T typedAsset)
                return new ResourceAssetLease<T>(this, handle, typedAsset);
            Release(handle);
            return default;
        }

        public async UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object
        {
            ValidateLocation(location);
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            ResourceLeaseHandle handle = await AcquireLeaseAsync(new ResourceKey(location, packageName, typeof(T)), ResourceLeaseKind.Legacy, default);
            try
            {
                TryGetLeaseAsset(handle, out UnityEngine.Object asset);
                callback(asset as T);
            }
            catch
            {
                Release(handle);
                throw;
            }
        }

        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object
        {
            ValidateLocation(location);
            ResourceLeaseHandle handle = await AcquireLeaseAsync(new ResourceKey(location, packageName, typeof(T)), ResourceLeaseKind.Legacy, cancellationToken);
            if (TryGetLeaseAsset(handle, out UnityEngine.Object asset) && asset is T typedAsset)
                return typedAsset;
            Release(handle);
            return null;
        }

        public UniTask LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            return LoadAssetAsync(location, typeof(UnityEngine.Object), priority, loadAssetCallbacks, userData, packageName);
        }

        public async UniTask LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            ValidateLocation(location);
            if (loadAssetCallbacks == null)
                throw new ArgumentNullException(nameof(loadAssetCallbacks));
            float started = Time.unscaledTime;
            ResourceLeaseHandle handle = await AcquireLeaseAsync(new ResourceKey(location, packageName, assetType), ResourceLeaseKind.Legacy, default,
                NormalizePriority(priority), loadAssetCallbacks.LoadAssetUpdateCallback, userData);
            if (!TryGetLeaseAsset(handle, out UnityEngine.Object asset))
            {
                Release(handle);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, LoadResourceStatus.NotReady, ZString.Format("Can not load asset '{0}'.", location), userData);
                return;
            }
            try
            {
                loadAssetCallbacks.LoadAssetSuccessCallback(location, asset, Time.unscaledTime - started, userData);
            }
            catch
            {
                Release(handle);
                throw;
            }
        }

        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            ValidateLocation(location);
            return InstantiateOwnedPrefab(AcquirePrefabSourceLease(location, packageName), parent, default);
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            ValidateLocation(location);
            ResourceLeaseHandle handle = await AcquirePrefabSourceLeaseAsync(location, packageName, cancellationToken);
            return InstantiateOwnedPrefab(handle, parent, cancellationToken);
        }

        private GameObject InstantiateOwnedPrefab(ResourceLeaseHandle handle, Transform parent, CancellationToken cancellationToken)
        {
            GameObject instance = null;
            bool transferred = false;
            ResourceBindingService bindings = _bindingService;
            int unloadGeneration = _assetUnloadGeneration;
            try
            {
                if (cancellationToken.IsCancellationRequested || (!ReferenceEquals(parent, null) && parent == null) ||
                    !TryGetLeaseAsset(handle, out UnityEngine.Object source) || source is not GameObject prefab)
                    return null;
                instance = UnityEngine.Object.Instantiate(prefab, parent);
                if (instance == null || cancellationToken.IsCancellationRequested || _isDestroying || unloadGeneration != _assetUnloadGeneration)
                    return null;
                ResourceOwner owner = instance.GetComponent<ResourceOwner>();
                if (owner == null)
                    owner = instance.AddComponent<ResourceOwner>();
                if (bindings.RegisterPrefabSource(owner, handle, prefab) != ResourceBindStatus.Success)
                    return null;
                transferred = true;
                return instance;
            }
            finally
            {
                if (!transferred)
                {
                    Release(handle);
                    DestroyRuntimeObject(instance);
                }
            }
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                throw new GameFrameworkException("Asset name is invalid.");
        }

        #endregion
        #endregion
        #endregion
    }
}
