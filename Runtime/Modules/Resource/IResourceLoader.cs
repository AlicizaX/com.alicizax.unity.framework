using System;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    internal interface IResourceLoader
    {
        bool IsAvailable { get; }
        AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous);
        SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous);
        void WaitForCompletion(HandleBase handle);
    }

    internal sealed class YooResourceLoader : IResourceLoader
    {
        public bool IsAvailable => YooAssets.IsInitialized;

        public AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous)
        {
            ResourcePackage package = YooAssets.GetPackage(packageName);
            return synchronous ? package.LoadAssetSync(location, assetType) : package.LoadAssetAsync(location, assetType);
        }

        public SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous)
        {
            ResourcePackage package = YooAssets.GetPackage(packageName);
            return synchronous ? package.LoadSubAssetsSync<UnityEngine.Sprite>(location) : package.LoadSubAssetsAsync<UnityEngine.Sprite>(location);
        }

        public void WaitForCompletion(HandleBase handle)
        {
            if (handle is AssetHandle assetHandle)
                assetHandle.WaitForAsyncComplete();
            else
                ((SubAssetsHandle)handle).WaitForAsyncComplete();
        }
    }
}
