using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.Resource.Runtime
{
    public enum ResourceBindStatus : byte
    {
        Success = 0,
        InvalidKey = 1,
        MissingOwner = 2,
        MissingTarget = 3,
        StaleOwner = 4,
        CapacityExceeded = 5,
        LoadFailed = 6,
        ApplyFailed = 7,
        ServiceShutdown = 8,
        NotImplemented = 9,
    }

    [Flags]
    public enum ResourceBindingOptions : byte
    {
        None = 0,
        KeepAliveOnRelease = 1,
        SetNativeSize = 2,
    }

    public enum ResourceBindingSlotType : byte
    {
        None = 0,
        ImageSprite = 1,
        ImageMaterial = 2,
        SpriteRendererSprite = 3,
        RendererSharedMaterial = 4,
        RendererMaterialInstance = 5,
        PrefabSource = 6,
        SubSprite = 7,
    }

    public interface IResourceBindingService
    {
        ResourceBindStatus RegisterOwner(ResourceOwner owner);
        ResourceBindStatus ReleaseOwner(ResourceOwner owner);
        ResourceBindStatus ReleaseOwner(int ownerId, uint generation);
        void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity);
        ResourceBindStatus RegisterTarget(ResourceOwner owner, Component target);
        ResourceBindStatus UnregisterTarget(ResourceOwner owner, Component target);
        ResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None);
        ResourceBindStatus BindSprite(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None);
        UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, Image image, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default);
        UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default);
        ResourceBindStatus BindImageMaterial(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None);
        UniTask<ResourceBindStatus> BindImageMaterialAsync(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default);
        ResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None);
        UniTask<ResourceBindStatus> BindSharedMaterialAsync(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default);
        ResourceBindStatus BindMaterialInstance(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None);
        UniTask<ResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default);
        int GetOwnerInfos(ResourceOwnerInfo[] results, int startIndex, int maxCount);
        int GetBindingInfos(ResourceBindingInfo[] results, int startIndex, int maxCount);
    }
}
