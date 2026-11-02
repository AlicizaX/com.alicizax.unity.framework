using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService : ServiceBase, IResourceService, IResourcePackageService, IServiceTickable
    {
        private enum ServiceState : byte
        {
            Running = 0,
            Resetting = 1,
            Shutdown = 2,
        }

        private const int MaintenanceSweepBudget = 32;
        private const int AssetInfoCacheCapacity = 256;

        private readonly ResourceKeyRegistry _keys = new ResourceKeyRegistry();
        private readonly ResourceBindingService _bindings;
        private readonly ResourceScope _appScope;
        private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<bool>> _packageInitTasks = new Dictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);
        private readonly List<UnloadUnusedAssetsOperation> _unloadUnusedAssetsOperations = new List<UnloadUnusedAssetsOperation>();
        private readonly List<UnloadAllAssetsOperation> _unloadAllAssetsOperations = new List<UnloadAllAssetsOperation>();
        private readonly ResourceUlongIntMap _assetInfoSlots = new ResourceUlongIntMap();
        private readonly ulong[] _assetInfoKeys = new ulong[AssetInfoCacheCapacity];
        private readonly AssetInfo[] _assetInfos = new AssetInfo[AssetInfoCacheCapacity];
        private int _assetInfoCount;
        private int _assetInfoNextEvict;
        private ServiceState _state;
        private int _resetEpoch;
        private float _now;
        private string _defaultPackageName = "DefaultPackage";
        private int _defaultPackageId;
        private string _applicableGameVersion;
        private int _internalResourceVersion;
        private Action<bool> _forceUnloadUnusedAssetsAction;

        internal ResourceService()
        {
            _records.Allocate();
            _defaultPackageId = _keys.PackageId(_defaultPackageName);
            _bindings = new ResourceBindingService(this);
            _appScope = CreateScope("App");
        }

        internal IResourceLoader Loader { get; set; } = new YooResourceLoader();

        internal ResourceBindingService Bindings => _bindings;

        public bool CanLoadResources => _state == ServiceState.Running && Loader.IsAvailable;

        internal bool IsRunning => _state == ServiceState.Running;

        public ResourceScope App => _appScope;

        public string DefaultPackageName
        {
            get => _defaultPackageName;
            set
            {
                _defaultPackageName = value;
                _defaultPackageId = _keys.PackageId(value);
            }
        }

        public EPlayMode PlayMode { get; set; } = EPlayMode.OfflinePlayMode;

        public string DecryptionServices { get; set; }

        public bool AutoUnloadBundleWhenUnused { get; set; }

        public long Milliseconds { get; set; } = 30;

        public int DownloadingMaxNum { get; set; }

        public int FailedTryAgain { get; set; }

        public string PackageVersion { get; set; }

        public string ApplicableGameVersion => _applicableGameVersion;

        public int InternalResourceVersion => _internalResourceVersion;

        public AssetRef<T> Key<T>(string location, string packageName = null) where T : UnityEngine.Object
        {
            return new AssetRef<T>(KeyId<T>(location, packageName));
        }

        public void Initialize()
        {
            if (!YooAssets.IsInitialized)
                YooAssets.Initialize(new ResourceLogger());
            YooAssets.SetAsyncOperationMaxTimeSlice(Milliseconds);
            if (!YooAssets.TryGetPackage(_defaultPackageName, out ResourcePackage defaultPackage))
                defaultPackage = YooAssets.CreatePackage(_defaultPackageName);
            _packages[_defaultPackageName] = defaultPackage;
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            ProcessFrame(Time.unscaledTime);
        }

        internal void ProcessFrame(float unscaledTime)
        {
            _now = unscaledTime;
            if (_state == ServiceState.Resetting)
            {
                RemoveCompletedOperations(_unloadAllAssetsOperations);
                if (_unloadAllAssetsOperations.Count == 0)
                    _state = ServiceState.Running;
            }

            if (_loadingCount > 0)
                CompleteFinishedLoads();
            if (_cancelableWaiterCount > 0)
                AbortCanceledWaiters();
            ReleaseExpiredIdleAssets();
            ReleaseDestroyedOwnerScopes();
            _bindings.ReleaseDestroyedTargets();
        }

        public void ForceUnloadAllAssets()
        {
#if UNITY_WEBGL
            Log.Warning(ZString.Format("WebGL not support invoke {0}", nameof(ForceUnloadAllAssets)));
#else
            if (_state != ServiceState.Running)
                return;
            _state = ServiceState.Resetting;
            LoadWaiter waiters = ReleaseAllResources();
            foreach (KeyValuePair<string, ResourcePackage> pair in _packages)
            {
                if (pair.Value is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true } package)
                    _unloadAllAssetsOperations.Add(package.UnloadAllAssetsAsync());
            }

            if (_unloadAllAssetsOperations.Count == 0)
                _state = ServiceState.Running;
            try
            {
                _bindings.ReleaseAll();
            }
            finally
            {
                AbortWaiters(waiters);
            }
#endif
        }

        protected override void OnDestroyService()
        {
            Shutdown();
        }

        internal void Shutdown()
        {
            _state = ServiceState.Shutdown;
            LoadWaiter waiters = ReleaseAllResources();
            DisposeAllScopes();
            _unloadUnusedAssetsOperations.Clear();
            _unloadAllAssetsOperations.Clear();
            ClearAssetInfoCache();
            _forceUnloadUnusedAssetsAction = null;
            foreach (KeyValuePair<string, ResourcePackage> pair in _packages)
            {
                if (pair.Value is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true } package)
                    package.UnloadUnusedAssetsAsync();
            }

            _packages.Clear();
            try
            {
                _bindings.ReleaseAll();
            }
            finally
            {
                foreach (KeyValuePair<string, TaskCompletionSource<bool>> pair in _packageInitTasks)
                    pair.Value.TrySetResult(false);
                _packageInitTasks.Clear();
                AbortWaiters(waiters);
            }
        }

        private LoadWaiter ReleaseAllResources()
        {
            _resetEpoch++;
            LoadWaiter waiters = ReleaseAllLoading();
            ClearAllScopeEntries();
            ReleaseAllStorage();
            _bindings.DropReferences();
            return waiters;
        }

        public void ShutdownBackend()
        {
            _packages.Clear();
            if (YooAssets.IsInitialized)
                YooAssets.Destroy();
        }

        public UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
        {
            if (!CanLoadResources)
                return new UniTask<bool>(false);
            if (string.IsNullOrEmpty(packageName))
                packageName = _defaultPackageName;

            if (_packageInitTasks.TryGetValue(packageName, out TaskCompletionSource<bool> runningTask))
                return runningTask.Task.AsUniTask();

            if (_packages.TryGetValue(packageName, out ResourcePackage existingPackage))
            {
                if (existingPackage.InitializeStatus == EOperationStatus.Succeeded)
                {
                    RefreshPackageVersion(existingPackage);
                    return new UniTask<bool>(true);
                }

                if (existingPackage.InitializeStatus == EOperationStatus.Processing)
                {
                    TaskCompletionSource<bool> waitSource = CreatePackageInitSource(packageName);
                    AwaitExistingPackageInitialization(packageName, existingPackage, waitSource).Forget();
                    return waitSource.Task.AsUniTask();
                }

                _packages.Remove(packageName);
            }

            if (PlayMode is EPlayMode.HostPlayMode or EPlayMode.WebPlayMode)
                GameFrameworkGuard.NotNullOrEmpty(hostServerURL, nameof(hostServerURL));

            hostServerURL ??= string.Empty;
            fallbackHostServerURL ??= string.Empty;
            if (!YooAssets.TryGetPackage(packageName, out ResourcePackage resourcePackage))
                resourcePackage = YooAssets.CreatePackage(packageName);

            _packages[packageName] = resourcePackage;
            InitializePackageOperation initializationOperation = CreateInitializationOperationHandler(resourcePackage, hostServerURL, fallbackHostServerURL, DecryptionServices);
            if (initializationOperation == null)
            {
                _packages.Remove(packageName);
                return new UniTask<bool>(false);
            }

            TaskCompletionSource<bool> initSource = CreatePackageInitSource(packageName);
            AwaitPackageInitialization(packageName, resourcePackage, initializationOperation, initSource).Forget();
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
                while (_state != ServiceState.Shutdown && resourcePackage.InitializeStatus == EOperationStatus.Processing)
                    await UniTask.Yield();

                bool succeeded = resourcePackage.InitializeStatus == EOperationStatus.Succeeded;
                if (succeeded)
                    RefreshPackageVersion(resourcePackage);
                completionSource.TrySetResult(succeeded);
            }
            finally
            {
                _packageInitTasks.Remove(packageName);
            }
        }

        private async UniTaskVoid AwaitPackageInitialization(string packageName, ResourcePackage resourcePackage, InitializePackageOperation initializationOperation, TaskCompletionSource<bool> completionSource)
        {
            try
            {
                await initializationOperation.ToUniTask();
                if (initializationOperation.Status == EOperationStatus.Succeeded)
                {
                    RefreshPackageVersion(resourcePackage);
                    completionSource.TrySetResult(true);
                    return;
                }

                Log.Error(initializationOperation.Error);
                _packages.Remove(packageName);
                completionSource.TrySetResult(false);
            }
            catch (Exception exception)
            {
                Log.Error(exception.Message);
                _packages.Remove(packageName);
                completionSource.TrySetResult(false);
            }
            finally
            {
                _packageInitTasks.Remove(packageName);
            }
        }

        private void RefreshPackageVersion(ResourcePackage resourcePackage)
        {
            if (!resourcePackage.PackageValid)
                return;

            string packageVersion = resourcePackage.GetPackageVersion();
            PackageVersion = packageVersion;
            _applicableGameVersion = Application.version;
            _internalResourceVersion = ParseInternalResourceVersion(packageVersion);
        }

        private static int ParseInternalResourceVersion(string packageVersion)
        {
            if (string.IsNullOrEmpty(packageVersion))
                return 0;

            int hash = 23;
            for (int i = 0; i < packageVersion.Length; i++)
                hash = hash * 31 + packageVersion[i];
            return hash;
        }

        public string GetPackageVersion(string customPackageName = "")
        {
            ResourcePackage package = YooAssets.GetPackage(string.IsNullOrEmpty(customPackageName) ? _defaultPackageName : customPackageName);
            return package == null ? string.Empty : package.GetPackageVersion();
        }

        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
        {
            return GetPackageOrThrow(customPackageName).RequestPackageVersionAsync(new RequestPackageVersionOptions(appendTimeTicks, timeout));
        }

        public LoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            ResourcePackage package = GetPackageOrThrow(customPackageName);
            ClearAssetInfoCache();
            LoadPackageManifestOperation operation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(packageVersion, timeout));
            WatchManifestUpdateOperation(operation).Forget();
            return operation;
        }

        private async UniTaskVoid WatchManifestUpdateOperation(LoadPackageManifestOperation operation)
        {
            while (_state != ServiceState.Shutdown && !operation.IsDone)
                await UniTask.Yield();

            ClearAssetInfoCache();
            if (operation.Status == EOperationStatus.Succeeded && YooAssets.TryGetPackage(_defaultPackageName, out ResourcePackage defaultPackage))
                RefreshPackageVersion(defaultPackage);
        }

        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            return GetPackageOrThrow(customPackageName).CreateResourceDownloader(new ResourceDownloaderOptions(DownloadingMaxNum, FailedTryAgain));
        }

        public ClearCacheOperation ClearCacheAsync(ClearCacheOptions options, string customPackageName = "")
        {
            return GetPackageOrThrow(customPackageName).ClearCacheAsync(options);
        }

        public ClearCacheOperation ClearAllBundleFiles(string customPackageName = "")
        {
            return GetPackageOrThrow(customPackageName).ClearCacheAsync(new ClearCacheOptions(ClearCacheMethods.ClearAllBundleFiles));
        }

        public AssetInfo[] GetAssetInfos(string resTag, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetAssetInfos(resTag);
        }

        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetAssetInfos(tags);
        }

        public AssetInfo GetAssetInfo(string location, string packageName = "")
        {
            ulong key = ((ulong)(uint)PackageId(packageName) << 32) | (uint)_keys.LocationId(location);
            if (_assetInfoSlots.TryGetValue(key, out int slot))
                return _assetInfos[slot];

            AssetInfo assetInfo = GetPackageOrThrow(packageName).GetAssetInfo(location);
            if (string.IsNullOrEmpty(assetInfo.Error))
                CacheAssetInfo(key, assetInfo);
            return assetInfo;
        }

        public HasAssetResult HasAsset(string location, string packageName = "")
        {
            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            if (!assetInfo.IsValid || !string.IsNullOrEmpty(assetInfo.Error))
                return HasAssetResult.NotExist;
            return GetPackageOrThrow(packageName).GetDownloadSize(assetInfo) > 0 ? HasAssetResult.AssetOnline : HasAssetResult.AssetOnDisk;
        }

        public bool IsLocationValid(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).IsLocationValid(location);
        }

        private void CacheAssetInfo(ulong key, AssetInfo assetInfo)
        {
            int slot;
            if (_assetInfoCount < AssetInfoCacheCapacity)
            {
                slot = _assetInfoCount++;
            }
            else
            {
                slot = _assetInfoNextEvict;
                _assetInfoNextEvict = (_assetInfoNextEvict + 1) % AssetInfoCacheCapacity;
                _assetInfoSlots.Remove(_assetInfoKeys[slot]);
            }

            _assetInfoKeys[slot] = key;
            _assetInfos[slot] = assetInfo;
            _assetInfoSlots.Set(key, slot);
        }

        private void ClearAssetInfoCache()
        {
            Array.Clear(_assetInfos, 0, _assetInfoCount);
            _assetInfoSlots.Clear();
            _assetInfoCount = 0;
            _assetInfoNextEvict = 0;
        }

        private ResourcePackage GetPackageOrThrow(string packageName)
        {
            string name = string.IsNullOrEmpty(packageName) ? _defaultPackageName : packageName;
            ResourcePackage package = YooAssets.GetPackage(name);
            if (package == null)
                throw new GameFrameworkException(ZString.Format("The package does not exist. Package Name :{0}", name));
            return package;
        }

        private static void RemoveCompletedOperations<T>(List<T> operations) where T : AsyncOperationBase
        {
            for (int i = operations.Count - 1; i >= 0; i--)
            {
                if (operations[i].IsDone)
                    operations.RemoveAt(i);
            }
        }
    }
}
