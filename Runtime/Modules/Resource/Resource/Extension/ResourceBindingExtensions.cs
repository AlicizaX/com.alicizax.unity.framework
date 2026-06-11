using System.Threading;
using AlicizaX;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public static class ResourceBindingExtensions
{
    private static IResourceService _resourceService;
    private static IResourceBindingService _bindingService;

    private static bool TryGetResourceService(out IResourceService resourceService, out IResourceBindingService bindingService)
    {
        resourceService = _resourceService;
        bindingService = _bindingService;
        if (resourceService != null && bindingService != null && ReferenceEquals(resourceService.BindingService, bindingService))
        {
            return true;
        }

        if (!AppServices.HasWorld || !AppServices.App.TryGet(out resourceService) || resourceService == null)
        {
            _resourceService = null;
            _bindingService = null;
            bindingService = null;
            return false;
        }

        bindingService = resourceService.BindingService;
        if (bindingService == null)
        {
            _resourceService = null;
            _bindingService = null;
            return false;
        }

        _resourceService = resourceService;
        _bindingService = bindingService;
        return true;
    }

    public static void SetSprite(this Image image, string location, bool setNativeSize = false, CancellationToken cancellationToken = default)
    {
        ResourceBindingOptions options = setNativeSize ? ResourceBindingOptions.SetNativeSize : ResourceBindingOptions.None;
        SetSprite(image, location, options, cancellationToken);
    }

    public static void SetSprite(this Image image, string location, ResourceBindingOptions options, CancellationToken cancellationToken = default)
    {
        if (image == null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = ResourceOwner.EnsureFor(image, bindingService);
        bindingService.BindSprite(owner, image, new ResourceKey(location, string.Empty, typeof(Sprite), ResourceAssetKind.Sprite), options);
    }

    public static void SetSprite(this SpriteRenderer spriteRenderer, string location, CancellationToken cancellationToken = default)
    {
        SetSprite(spriteRenderer, location, ResourceBindingOptions.None, cancellationToken);
    }

    public static void SetSprite(this SpriteRenderer spriteRenderer, string location, ResourceBindingOptions options, CancellationToken cancellationToken = default)
    {
        if (spriteRenderer == null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = ResourceOwner.EnsureFor(spriteRenderer, bindingService);
        bindingService.BindSprite(owner, spriteRenderer, new ResourceKey(location, string.Empty, typeof(Sprite), ResourceAssetKind.Sprite), options);
    }

    public static void SetSubSprite(this Image image, string location, string spriteName, bool setNativeSize = false, CancellationToken cancellationToken = default)
    {
        ResourceBindingOptions options = setNativeSize ? ResourceBindingOptions.SetNativeSize : ResourceBindingOptions.None;
        SetSubSprite(image, location, spriteName, options, cancellationToken);
    }

    public static void SetSubSprite(this Image image, string location, string spriteName, ResourceBindingOptions options, CancellationToken cancellationToken = default)
    {
        if (image == null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = ResourceOwner.EnsureFor(image, bindingService);
        bindingService.BindSubSpriteAsync(owner, image, new ResourceKey(location, string.Empty, typeof(Sprite), ResourceAssetKind.SubAssets), spriteName, options, cancellationToken).Forget();
    }

    public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName, CancellationToken cancellationToken = default)
    {
        SetSubSprite(spriteRenderer, location, spriteName, ResourceBindingOptions.None, cancellationToken);
    }

    public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName, ResourceBindingOptions options, CancellationToken cancellationToken = default)
    {
        if (spriteRenderer == null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = ResourceOwner.EnsureFor(spriteRenderer, bindingService);
        bindingService.BindSubSpriteAsync(owner, spriteRenderer, new ResourceKey(location, string.Empty, typeof(Sprite), ResourceAssetKind.SubAssets), spriteName, options, cancellationToken).Forget();
    }

    public static void SetMaterial(this Image image, string location, bool isAsync = false, string packageName = "")
    {
        SetMaterial(image, location, ResourceBindingOptions.None, isAsync, packageName);
    }

    public static void SetMaterial(this Image image, string location, ResourceBindingOptions options, bool isAsync = false, string packageName = "")
    {
        if (image == null)
        {
            throw new GameFrameworkException("SetMaterial failed. Because image is null.");
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = EnsureOwner(bindingService, image);
        if (isAsync)
        {
            bindingService.BindImageMaterialAsync(owner, image, MaterialKey(location, packageName), options).Forget();
            return;
        }

        bindingService.BindImageMaterial(owner, image, MaterialKey(location, packageName), options);
    }

    public static void SetMaterial(this SpriteRenderer spriteRenderer, string location, bool isAsync = false, string packageName = "")
    {
        SetMaterial(spriteRenderer, location, ResourceBindingOptions.None, isAsync, packageName);
    }

    public static void SetMaterial(this SpriteRenderer spriteRenderer, string location, ResourceBindingOptions options, bool isAsync = false, string packageName = "")
    {
        if (spriteRenderer == null)
        {
            throw new GameFrameworkException("SetMaterial failed. Because spriteRenderer is null.");
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = EnsureOwner(bindingService, spriteRenderer);
        if (isAsync)
        {
            bindingService.BindSharedMaterialAsync(owner, spriteRenderer, MaterialKey(location, packageName), options).Forget();
            return;
        }

        bindingService.BindSharedMaterial(owner, spriteRenderer, MaterialKey(location, packageName), options);
    }

    public static void SetMaterial(this MeshRenderer meshRenderer, string location, bool needInstance = true, bool isAsync = false, string packageName = "")
    {
        SetMaterial(meshRenderer, location, ResourceBindingOptions.None, needInstance, isAsync, packageName);
    }

    public static void SetMaterial(this MeshRenderer meshRenderer, string location, ResourceBindingOptions options, bool needInstance = true, bool isAsync = false, string packageName = "")
    {
        if (meshRenderer == null)
        {
            throw new GameFrameworkException("SetMaterial failed. Because meshRenderer is null.");
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = EnsureOwner(bindingService, meshRenderer);
        if (isAsync)
        {
            if (needInstance)
            {
                bindingService.BindMaterialInstanceAsync(owner, meshRenderer, MaterialKey(location, packageName), options).Forget();
            }
            else
            {
                bindingService.BindSharedMaterialAsync(owner, meshRenderer, MaterialKey(location, packageName), options).Forget();
            }

            return;
        }

        if (needInstance)
        {
            bindingService.BindMaterialInstance(owner, meshRenderer, MaterialKey(location, packageName), options);
        }
        else
        {
            bindingService.BindSharedMaterial(owner, meshRenderer, MaterialKey(location, packageName), options);
        }
    }

    public static void SetSharedMaterial(this MeshRenderer meshRenderer, string location, bool isAsync = false, string packageName = "")
    {
        SetSharedMaterial(meshRenderer, location, ResourceBindingOptions.None, isAsync, packageName);
    }

    public static void SetSharedMaterial(this MeshRenderer meshRenderer, string location, ResourceBindingOptions options, bool isAsync = false, string packageName = "")
    {
        if (meshRenderer == null)
        {
            throw new GameFrameworkException("SetSharedMaterial failed. Because meshRenderer is null.");
        }

        if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
        {
            return;
        }

        ResourceOwner owner = EnsureOwner(bindingService, meshRenderer);
        if (isAsync)
        {
            bindingService.BindSharedMaterialAsync(owner, meshRenderer, MaterialKey(location, packageName), options).Forget();
            return;
        }

        bindingService.BindSharedMaterial(owner, meshRenderer, MaterialKey(location, packageName), options);
    }

    private static ResourceOwner EnsureOwner(IResourceBindingService bindingService, Component target)
    {
        ResourceOwner owner = target.GetComponent<ResourceOwner>();
        if (owner == null)
        {
            owner = target.gameObject.AddComponent<ResourceOwner>();
        }

        bindingService.RegisterOwner(owner);
        return owner;
    }

    private static ResourceKey MaterialKey(string location, string packageName)
    {
        return new ResourceKey(location, packageName, typeof(Material), ResourceAssetKind.Material);
    }

}
