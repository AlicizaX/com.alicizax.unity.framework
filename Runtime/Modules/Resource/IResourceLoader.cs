using System;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    internal interface IResourceLoader
    {
        bool IsAvailable { get; }
        AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous, uint priority);
        SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous);
    }

    internal sealed class YooResourceLoader : IResourceLoader
    {
        public bool IsAvailable => YooAssets.IsInitialized;

        public AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous, uint priority)
        {
            ResourcePackage package = YooAssets.GetPackage(packageName);
            return synchronous ? package.LoadAssetSync(location, assetType) : package.LoadAssetAsync(location, assetType, priority);
        }

        public SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous)
        {
            ResourcePackage package = YooAssets.GetPackage(packageName);
            return synchronous ? package.LoadSubAssetsSync<UnityEngine.Sprite>(location) : package.LoadSubAssetsAsync<UnityEngine.Sprite>(location);
        }
    }
}
