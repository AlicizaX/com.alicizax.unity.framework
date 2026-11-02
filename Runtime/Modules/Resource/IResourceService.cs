using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    public interface IResourceService : IService
    {
        bool CanLoadResources { get; }

        float IdleAssetExpireTime { get; set; }

        int IdleAssetCapacity { get; set; }

        int IdleReleaseCountPerTick { get; set; }

        ResourceScope App { get; }

        ResourceScope CreateScope(string debugName = null);

        AssetRef<T> Key<T>(string location, string packageName = null) where T : Object;

        AssetLease<T> LoadLease<T>(AssetRef<T> key) where T : Object;

        AssetLease<T> LoadLease<T>(string location, string packageName = null) where T : Object;

        UniTask<AssetLease<T>> LoadLeaseAsync<T>(AssetRef<T> key, CancellationToken cancellationToken = default) where T : Object;

        UniTask<AssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = null) where T : Object;

        GameObject Instantiate(string location, Transform parent = null, string packageName = null);

        UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = null);

        void UnloadUnusedAssets(bool releaseIdleAssets);

        void ForceUnloadAllAssets();

        void ForceUnloadUnusedAssets(bool performGCCollect);

        void SetForceUnloadUnusedAssetsAction(Action<bool> action);

        void OnLowMemory();
    }

    public interface IResourcePackageService : IService
    {
        string DefaultPackageName { get; set; }

        EPlayMode PlayMode { get; set; }

        string DecryptionServices { get; set; }

        bool AutoUnloadBundleWhenUnused { get; set; }

        long Milliseconds { get; set; }

        int DownloadingMaxNum { get; set; }

        int FailedTryAgain { get; set; }

        string PackageVersion { get; set; }

        string ApplicableGameVersion { get; }

        int InternalResourceVersion { get; }

        void Initialize();

        UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "");

        string GetPackageVersion(string customPackageName = "");

        RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "");

        LoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "");

        ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "");

        ClearCacheOperation ClearCacheAsync(ClearCacheOptions options, string customPackageName = "");

        ClearCacheOperation ClearAllBundleFiles(string customPackageName = "");

        HasAssetResult HasAsset(string location, string packageName = "");

        bool IsLocationValid(string location, string packageName = "");

        AssetInfo GetAssetInfo(string location, string packageName = "");

        AssetInfo[] GetAssetInfos(string resTag, string packageName = "");

        AssetInfo[] GetAssetInfos(string[] tags, string packageName = "");

        void ShutdownBackend();
    }
}
