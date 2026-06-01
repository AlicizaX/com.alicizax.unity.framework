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
        private readonly ResourceUlongIntMap _assetInfoByKey = new ResourceUlongIntMap();

        private AssetInfoSlot[][] _assetInfoSlotPages;

        private int _assetInfoSlotNextIndex;

        /// <summary>
        /// 正在加载的资源任务。
        /// </summary>
        private readonly ResourceUlongIntMap _assetLoadingOperationByKey = new ResourceUlongIntMap();

        private LoadingOperationSlot[][] _loadingOperationSlotPages;

        private int _loadingOperationSlotNextIndex;

        private int _loadingOperationSlotFreeHead = -1;

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

        private readonly List<UpdatePackageManifestOperation> _manifestUpdateOperations = new List<UpdatePackageManifestOperation>();

        private bool _isDestroying;

        private int _assetUnloadGeneration;

        private ResourceBindingService _bindingService;

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
            _manifestUpdateOperations.Clear();
            ClearAssetInfoCache();
            ClearResourceKeyRegistry();
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
            ClearAssetInfoCache();
            UpdatePackageManifestOperation operation = package.UpdatePackageManifestAsync(packageVersion, timeout);
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

        private struct LoadingOperationSlot
        {
            public ulong Key;
            public LoadingOperationState Operation;
            public byte State;
            public int NextFree;
        }

        private struct AssetInfoSlot
        {
            public ulong Key;
            public AssetInfo AssetInfo;
            public byte State;
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

        private void TrackManifestUpdateOperation(UpdatePackageManifestOperation operation)
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
                UpdatePackageManifestOperation operation = _manifestUpdateOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _manifestUpdateOperations.RemoveAt(i);
                    continue;
                }

                inProgress = true;
            }

            return inProgress;
        }

        private async UniTaskVoid WatchManifestUpdateOperation(UpdatePackageManifestOperation operation)
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

            bool cacheEnabled = !IsManifestUpdateInProgress();
            string normalizedPackageName = NormalizePackageName(packageName);
            if (cacheEnabled &&
                TryGetAssetInfoKey(normalizedPackageName, location, out ulong assetInfoKey) &&
                TryGetCachedAssetInfo(assetInfoKey, out AssetInfo cachedAssetInfo))
            {
                return cachedAssetInfo;
            }

            AssetInfo assetInfo = GetAssetInfoUncached(location, packageName);
            if (cacheEnabled && CanCacheAssetInfo(assetInfo))
            {
                assetInfoKey = GetAssetInfoKey(normalizedPackageName, location);
                SetCachedAssetInfo(assetInfoKey, assetInfo);
            }

            return assetInfo;
        }

        private AssetInfo GetAssetInfoUncached(string location, string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfo(location);
            }

            var package = YooAssets.GetPackage(packageName);
            if (package == null)
            {
                throw new GameFrameworkException(ZString.Format("The package does not exist. Package Name :{0}", packageName));
            }

            return package.GetAssetInfo(location);
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

        private ulong GetLoadingOperationKey(string location, string packageName, Type assetType, ResourceAssetKind assetKind)
        {
            return GetAssetRecordKey(packageName, location, assetType, assetKind, ResourceHandleKind.AssetHandle);
        }

        private ulong GetSubAssetsLoadingOperationKey(string location, string packageName)
        {
            return GetAssetRecordKey(packageName, location, typeof(Sprite), ResourceAssetKind.SubAssets, ResourceHandleKind.SubAssetsHandle);
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

            id = AllocateResourceId(ref _nextResourcePackageId, ResourceKeyPackageMax);
            _resourcePackageIds.Add(packageName, id);
            EnsureResourceNameSlot(ref _resourcePackagesById, ref _resourcePackageRefCounts, id);
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

            id = AllocateResourceId(ref _nextResourceLocationId, ResourceKeyLocationMax);
            _resourceLocationIds.Add(location, id);
            EnsureResourceNameSlot(ref _resourceLocationsById, ref _resourceLocationRefCounts, id);
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

            id = AllocateResourceId(ref _nextResourceTypeId, ResourceKeyTypeMax);
            _resourceTypeIds.Add(assetType, id);
            EnsureResourceTypeSlot(id);
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
            _nextResourcePackageId = 1;
            _nextResourceLocationId = 1;
            _nextResourceTypeId = 1;
        }

        private void TrimResourceKeyRegistryIfUnused()
        {
            if (_assetRecordsByKey.Count != 0 ||
                _assetLoadingOperationByKey.Count != 0 ||
                _assetInfoByKey.Count != 0)
            {
                return;
            }

            ClearResourceKeyRegistry();
        }

        private void RetainResourceKey(ulong key)
        {
            int packageId = UnpackPackageId(key);
            int locationId = UnpackLocationId(key);
            int typeId = UnpackTypeId(key);
            IncrementResourceRef(_resourcePackageRefCounts, packageId);
            IncrementResourceRef(_resourceLocationRefCounts, locationId);
            IncrementResourceRef(_resourceTypeRefCounts, typeId);
        }

        private void ReleaseResourceKey(ulong key)
        {
            ReleasePackageId(UnpackPackageId(key));
            ReleaseLocationId(UnpackLocationId(key));
            ReleaseTypeId(UnpackTypeId(key));
            TrimResourceKeyRegistryIfUnused();
        }

        private void ReleaseAllResourceKeysFromMap(ResourceUlongIntMap map)
        {
            map.ForEachKey(ReleaseResourceKeyNoTrim);
        }

        private void ReleaseResourceKeyNoTrim(ulong key)
        {
            ReleasePackageId(UnpackPackageId(key));
            ReleaseLocationId(UnpackLocationId(key));
            ReleaseTypeId(UnpackTypeId(key));
        }

        private void ReleasePackageId(int id)
        {
            if (!DecrementResourceRef(_resourcePackageRefCounts, id))
            {
                return;
            }

            string value = id < _resourcePackagesById.Length ? _resourcePackagesById[id] : null;
            if (value != null)
            {
                _resourcePackageIds.Remove(value);
                _resourcePackagesById[id] = null;
            }
        }

        private void ReleaseLocationId(int id)
        {
            if (!DecrementResourceRef(_resourceLocationRefCounts, id))
            {
                return;
            }

            string value = id < _resourceLocationsById.Length ? _resourceLocationsById[id] : null;
            if (value != null)
            {
                _resourceLocationIds.Remove(value);
                _resourceLocationsById[id] = null;
            }
        }

        private void ReleaseTypeId(int id)
        {
            if (!DecrementResourceRef(_resourceTypeRefCounts, id))
            {
                return;
            }

            Type value = id < _resourceTypesById.Length ? _resourceTypesById[id] : null;
            if (value != null)
            {
                _resourceTypeIds.Remove(value);
                _resourceTypesById[id] = null;
            }
        }

        private static void IncrementResourceRef(int[] refCounts, int id)
        {
            if (refCounts == null || id <= 0 || id >= refCounts.Length)
            {
                return;
            }

            refCounts[id]++;
        }

        private static bool DecrementResourceRef(int[] refCounts, int id)
        {
            if (refCounts == null || id <= 0 || id >= refCounts.Length || refCounts[id] <= 0)
            {
                return false;
            }

            refCounts[id]--;
            return refCounts[id] == 0;
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

        private static void EnsureResourceNameSlot(ref string[] values, ref int[] refCounts, int id)
        {
            EnsureResourceArray(ref values, id);
            EnsureResourceArray(ref refCounts, id);
        }

        private void EnsureResourceTypeSlot(int id)
        {
            EnsureResourceArray(ref _resourceTypesById, id);
            EnsureResourceArray(ref _resourceTypeRefCounts, id);
        }

        private static void EnsureResourceArray<T>(ref T[] array, int index)
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

        private static int AllocateResourceId(ref int nextId, int maxId)
        {
            if (nextId <= 0 || nextId > maxId)
            {
                throw new GameFrameworkException("Resource key id range exceeded.");
            }

            int id = nextId++;
            return id;
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
            if (!_assetInfoByKey.TryGetValue(key, out int slotIndex) || !IsValidAssetInfoSlotId(slotIndex))
            {
                return false;
            }

            ref AssetInfoSlot slot = ref GetAssetInfoSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != key)
            {
                return false;
            }

            assetInfo = slot.AssetInfo;
            return true;
        }

        private void SetCachedAssetInfo(ulong key, AssetInfo assetInfo)
        {
            bool keyAlreadyRetained = false;
            if (_assetInfoByKey.TryGetValue(key, out int slotIndex) && IsValidAssetInfoSlotId(slotIndex))
            {
                ref AssetInfoSlot existing = ref GetAssetInfoSlotRef(slotIndex);
                if (existing.State == 1 && existing.Key == key)
                {
                    existing.AssetInfo = assetInfo;
                    return;
                }

                _assetInfoByKey.Remove(key);
                keyAlreadyRetained = true;
            }

            slotIndex = AllocateAssetInfoSlot();
            ref AssetInfoSlot slot = ref GetAssetInfoSlotRef(slotIndex);
            slot.Key = key;
            slot.AssetInfo = assetInfo;
            slot.State = 1;
            _assetInfoByKey.Set(key, slotIndex);
            if (!keyAlreadyRetained)
            {
                RetainResourceKey(key);
            }
        }

        private void ClearAssetInfoCache()
        {
            ReleaseAllResourceKeysFromMap(_assetInfoByKey);
            _assetInfoByKey.Clear();
            _assetInfoSlotPages = null;
            _assetInfoSlotNextIndex = 0;
            TrimResourceKeyRegistryIfUnused();
        }

        private int AllocateAssetInfoSlot()
        {
            int index = _assetInfoSlotNextIndex++;
            EnsureAssetInfoSlotPage(index);
            ref AssetInfoSlot slot = ref GetAssetInfoSlotRef(index);
            slot = default;
            return index;
        }

        private bool IsValidAssetInfoSlotId(int index)
        {
            return index >= 0 && index < _assetInfoSlotNextIndex && _assetInfoSlotPages != null;
        }

        private ref AssetInfoSlot GetAssetInfoSlotRef(int index)
        {
            return ref _assetInfoSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private void EnsureAssetInfoSlotPage(int index)
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

            Type assetType = typeof(T);
            ResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            var asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(packageName, location, assetType, asset);
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

            Type assetType = typeof(T);
            ResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            var asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey, cancellationToken: cancellationToken);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(packageName, location, assetType, asset);
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

            ResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
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

            var asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey, NormalizePriority(priority), default,
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

            Type assetType = assetInfo.AssetType;
            ResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            var asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey, NormalizePriority(priority), default,
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
            ulong assetLoadingKey, uint priority = 0, CancellationToken cancellationToken = default, LoadAssetUpdateCallback loadAssetUpdateCallback = null, object userData = null)
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

        private bool TryBeginLoading(ulong assetObjectKey)
        {
            bool keyAlreadyRetained = false;
            if (_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int existingSlotIndex))
            {
                if (IsValidLoadingOperationSlotId(existingSlotIndex))
                {
                    ref LoadingOperationSlot existingSlot = ref GetLoadingOperationSlotRef(existingSlotIndex);
                    if (existingSlot.State == 1 && existingSlot.Key == assetObjectKey && existingSlot.Operation != null)
                    {
                        return false;
                    }
                }

                _assetLoadingOperationByKey.Remove(assetObjectKey);
                keyAlreadyRetained = true;
            }

            int slotIndex = AllocateLoadingOperationSlot();
            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            slot.Key = assetObjectKey;
            slot.Operation = MemoryPool.Acquire<LoadingOperationState>();
            slot.State = 1;
            _assetLoadingOperationByKey.Set(assetObjectKey, slotIndex);
            if (!keyAlreadyRetained)
            {
                RetainResourceKey(assetObjectKey);
            }
            return true;
        }

        private async UniTask<bool> WaitForLoadingAsync(ulong assetObjectKey, CancellationToken cancellationToken = default)
        {
            if (!TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
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

        private void CompleteLoading(ulong assetObjectKey)
        {
            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(true);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private void FailLoading(ulong assetObjectKey, Exception exception)
        {
            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(false);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private void ShutdownLoadingOperations()
        {
            int total = _loadingOperationSlotNextIndex;
            for (int i = 0; i < total; i++)
            {
                ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(i);
                if (slot.State != 1 || slot.Operation == null)
                {
                    continue;
                }

                LoadingOperationState loadingOperation = slot.Operation;
                loadingOperation.Complete(false);
                loadingOperation.RequestRelease();
                ReleaseLoadingOperationIfReady(loadingOperation);
                ClearLoadingOperationSlot(ref slot);
            }

            ReleaseAllResourceKeysFromMap(_assetLoadingOperationByKey);
            _assetLoadingOperationByKey.Clear();
            _loadingOperationSlotPages = null;
            _loadingOperationSlotNextIndex = 0;
            _loadingOperationSlotFreeHead = -1;
            TrimResourceKeyRegistryIfUnused();
        }

        private bool IsLoadingStateCurrent(int loadGeneration)
        {
            return !_isDestroying && loadGeneration == _assetUnloadGeneration;
        }

        private bool ShouldAbortLoadingAfterCallerCancellation(ulong assetObjectKey, CancellationToken cancellationToken, ref bool callerCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                callerCancellationRequested = true;
            }

            return callerCancellationRequested && !HasLoadingWaiters(assetObjectKey);
        }

        private bool HasLoadingWaiters(ulong assetObjectKey)
        {
            return TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation) &&
                   loadingOperation.WaiterCount > 0;
        }

        private bool TryGetLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
        {
            loadingOperation = null;
            if (!_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int slotIndex) || !IsValidLoadingOperationSlotId(slotIndex))
            {
                return false;
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != assetObjectKey || slot.Operation == null)
            {
                return false;
            }

            loadingOperation = slot.Operation;
            return true;
        }

        private bool TryRemoveLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
        {
            loadingOperation = null;
            if (!_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int slotIndex) || !IsValidLoadingOperationSlotId(slotIndex))
            {
                return false;
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != assetObjectKey || slot.Operation == null)
            {
                _assetLoadingOperationByKey.Remove(assetObjectKey);
                ReleaseResourceKey(assetObjectKey);
                return false;
            }

            loadingOperation = slot.Operation;
            _assetLoadingOperationByKey.Remove(assetObjectKey);
            ReleaseResourceKey(assetObjectKey);
            FreeLoadingOperationSlot(slotIndex);
            return true;
        }

        private int AllocateLoadingOperationSlot()
        {
            int index;
            if (_loadingOperationSlotFreeHead >= 0)
            {
                index = _loadingOperationSlotFreeHead;
                ref LoadingOperationSlot freeSlot = ref GetLoadingOperationSlotRef(index);
                _loadingOperationSlotFreeHead = freeSlot.NextFree;
            }
            else
            {
                index = _loadingOperationSlotNextIndex++;
                EnsureLoadingOperationSlotPage(index);
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(index);
            slot = default;
            slot.NextFree = -1;
            return index;
        }

        private void FreeLoadingOperationSlot(int index)
        {
            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(index);
            ClearLoadingOperationSlot(ref slot);
            slot.NextFree = _loadingOperationSlotFreeHead;
            _loadingOperationSlotFreeHead = index;
        }

        private static void ClearLoadingOperationSlot(ref LoadingOperationSlot slot)
        {
            slot.Key = 0;
            slot.Operation = null;
            slot.State = 0;
            slot.NextFree = -1;
        }

        private bool IsValidLoadingOperationSlotId(int index)
        {
            return index >= 0 && index < _loadingOperationSlotNextIndex && _loadingOperationSlotPages != null;
        }

        private ref LoadingOperationSlot GetLoadingOperationSlotRef(int index)
        {
            return ref _loadingOperationSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private void EnsureLoadingOperationSlotPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
            if (_loadingOperationSlotPages == null)
            {
                _loadingOperationSlotPages = new LoadingOperationSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _loadingOperationSlotPages.Length)
            {
                Array.Resize(ref _loadingOperationSlotPages, Math.Max(pageIndex + 1, _loadingOperationSlotPages.Length << 1));
            }

            if (_loadingOperationSlotPages[pageIndex] == null)
            {
                _loadingOperationSlotPages[pageIndex] = new LoadingOperationSlot[RecordPageSize];
            }
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
