using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.Resource.Runtime
{
    public static class ResourceBindingExtensions
    {
        public static void SetSprite(this Image image, in ResourceScope scope, AssetRef<Sprite> key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            scope.Service.Bindings.Bind(scope, image, ResourceBindingSlot.ImageSprite, key.Id, null, options);
        }

        public static void SetSprite(this Image image, in ResourceScope scope, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(scope, image, ResourceBindingSlot.ImageSprite, location, options);
        }

        public static void SetSprite(this Image image, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(OwnerScope(image), image, ResourceBindingSlot.ImageSprite, location, options);
        }

        public static void SetSprite(this SpriteRenderer spriteRenderer, in ResourceScope scope, AssetRef<Sprite> key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            scope.Service.Bindings.Bind(scope, spriteRenderer, ResourceBindingSlot.SpriteRendererSprite, key.Id, null, options);
        }

        public static void SetSprite(this SpriteRenderer spriteRenderer, in ResourceScope scope, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(scope, spriteRenderer, ResourceBindingSlot.SpriteRendererSprite, location, options);
        }

        public static void SetSprite(this SpriteRenderer spriteRenderer, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(OwnerScope(spriteRenderer), spriteRenderer, ResourceBindingSlot.SpriteRendererSprite, location, options);
        }

        public static void SetSubSprite(this Image image, in ResourceScope scope, string location, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            BindSubSprite(scope, image, ResourceBindingSlot.ImageSprite, location, spriteName, options);
        }

        public static void SetSubSprite(this Image image, string location, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            BindSubSprite(OwnerScope(image), image, ResourceBindingSlot.ImageSprite, location, spriteName, options);
        }

        public static void SetSubSprite(this SpriteRenderer spriteRenderer, in ResourceScope scope, string location, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            BindSubSprite(scope, spriteRenderer, ResourceBindingSlot.SpriteRendererSprite, location, spriteName, options);
        }

        public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            BindSubSprite(OwnerScope(spriteRenderer), spriteRenderer, ResourceBindingSlot.SpriteRendererSprite, location, spriteName, options);
        }

        public static void SetMaterial(this Image image, in ResourceScope scope, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(scope, image, ResourceBindingSlot.ImageMaterial, location, options);
        }

        public static void SetMaterial(this Image image, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(OwnerScope(image), image, ResourceBindingSlot.ImageMaterial, location, options);
        }

        public static void SetSharedMaterial(this Renderer renderer, in ResourceScope scope, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(scope, renderer, ResourceBindingSlot.RendererSharedMaterial, location, options);
        }

        public static void SetSharedMaterial(this Renderer renderer, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(OwnerScope(renderer), renderer, ResourceBindingSlot.RendererSharedMaterial, location, options);
        }

        public static void SetMaterialInstance(this Renderer renderer, in ResourceScope scope, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(scope, renderer, ResourceBindingSlot.RendererMaterialInstance, location, options);
        }

        public static void SetMaterialInstance(this Renderer renderer, string location, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            Bind(OwnerScope(renderer), renderer, ResourceBindingSlot.RendererMaterialInstance, location, options);
        }

        private static ResourceScope OwnerScope(Component target)
        {
            return ((ResourceService)AppServices.Require<IResourceService>()).OwnerScope(target.gameObject, ResourceScopeSource.Binding, null);
        }

        private static void Bind(in ResourceScope scope, Component target, ResourceBindingSlot slot, string location, ResourceBindingOptions options)
        {
            ResourceService service = scope.Service;
            int assetId = string.IsNullOrEmpty(location)
                ? 0
                : slot == ResourceBindingSlot.ImageSprite || slot == ResourceBindingSlot.SpriteRendererSprite
                    ? service.KeyId<Sprite>(location, string.Empty)
                    : service.KeyId<Material>(location, string.Empty);
            service.Bindings.Bind(scope, target, slot, assetId, null, options);
        }

        private static void BindSubSprite(in ResourceScope scope, Component target, ResourceBindingSlot slot, string location, string spriteName, ResourceBindingOptions options)
        {
            ResourceService service = scope.Service;
            int assetId = string.IsNullOrEmpty(location) ? 0 : service.SubAssetsKeyId(location, string.Empty);
            service.Bindings.Bind(scope, target, slot, assetId, spriteName, options);
        }
    }
}
