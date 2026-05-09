using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.Resource.Runtime
{
    public static class AssetsSetHelper
    {
        private enum MaterialTargetType
        {
            Image,
            SpriteRenderer,
            MeshRenderer,
            MeshRendererShared,
        }

        private sealed class MaterialSetRequest : IMemory
        {
            public MaterialTargetType TargetType;
            public Image Image;
            public SpriteRenderer SpriteRenderer;
            public MeshRenderer MeshRenderer;
            public bool NeedInstance;

            public static MaterialSetRequest Create(Image image)
            {
                MaterialSetRequest request = MemoryPool.Acquire<MaterialSetRequest>();
                request.TargetType = MaterialTargetType.Image;
                request.Image = image;
                return request;
            }

            public static MaterialSetRequest Create(SpriteRenderer spriteRenderer)
            {
                MaterialSetRequest request = MemoryPool.Acquire<MaterialSetRequest>();
                request.TargetType = MaterialTargetType.SpriteRenderer;
                request.SpriteRenderer = spriteRenderer;
                return request;
            }

            public static MaterialSetRequest Create(MeshRenderer meshRenderer, bool needInstance, bool sharedMaterial)
            {
                MaterialSetRequest request = MemoryPool.Acquire<MaterialSetRequest>();
                request.TargetType = sharedMaterial ? MaterialTargetType.MeshRendererShared : MaterialTargetType.MeshRenderer;
                request.MeshRenderer = meshRenderer;
                request.NeedInstance = needInstance;
                return request;
            }

            public void Apply(Material material)
            {
                if (material == null)
                {
                    MemoryPool.Release(this);
                    return;
                }

                switch (TargetType)
                {
                    case MaterialTargetType.Image:
                    {
                        if (Image == null || Image.gameObject == null)
                        {
                            _resourceService.UnloadAsset(material);
                            MemoryPool.Release(this);
                            return;
                        }

                        Image.material = material;
                        AssetsReference.Ref(material, Image.gameObject);
                        break;
                    }
                    case MaterialTargetType.SpriteRenderer:
                    {
                        if (SpriteRenderer == null || SpriteRenderer.gameObject == null)
                        {
                            _resourceService.UnloadAsset(material);
                            MemoryPool.Release(this);
                            return;
                        }

                        SpriteRenderer.material = material;
                        AssetsReference.Ref(material, SpriteRenderer.gameObject);
                        break;
                    }
                    case MaterialTargetType.MeshRenderer:
                    {
                        if (MeshRenderer == null || MeshRenderer.gameObject == null)
                        {
                            _resourceService.UnloadAsset(material);
                            MemoryPool.Release(this);
                            return;
                        }

                        SetMeshMaterial(MeshRenderer, material, NeedInstance);
                        break;
                    }
                    case MaterialTargetType.MeshRendererShared:
                    {
                        if (MeshRenderer == null || MeshRenderer.gameObject == null)
                        {
                            _resourceService.UnloadAsset(material);
                            MemoryPool.Release(this);
                            return;
                        }

                        MeshRenderer.sharedMaterial = material;
                        AssetsReference.Ref(material, MeshRenderer.gameObject);
                        break;
                    }
                }

                MemoryPool.Release(this);
            }

            public void Clear()
            {
                TargetType = MaterialTargetType.Image;
                Image = null;
                SpriteRenderer = null;
                MeshRenderer = null;
                NeedInstance = false;
            }
        }

        private sealed class MaterialLoadCallbacks
        {
            public static readonly LoadAssetCallbacks Instance = new LoadAssetCallbacks(OnSuccess, OnFailure);

            private static void OnSuccess(string assetName, object asset, float duration, object userData)
            {
                MaterialSetRequest request = (MaterialSetRequest)userData;
                request.Apply(asset as Material);
            }

            private static void OnFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
            {
                MaterialSetRequest request = (MaterialSetRequest)userData;
                MemoryPool.Release(request);
            }
        }

        private static IResourceService _resourceService;

        private static void CheckResourceManager()
        {
            if (_resourceService == null)
            {
                _resourceService = AppServices.App.Require<IResourceService>();
            }
        }

        private static void LoadMaterialAsync(string location, string packageName, MaterialSetRequest request)
        {
            _resourceService.LoadAssetAsync(location, typeof(Material), 0, MaterialLoadCallbacks.Instance, request, packageName).Forget();
        }

        private static void SetMeshMaterial(MeshRenderer meshRenderer, Material material, bool needInstance)
        {
            if (!needInstance)
            {
                meshRenderer.material = material;
                AssetsReference.Ref(material, meshRenderer.gameObject);
                return;
            }

            Material instance = Object.Instantiate(material);
            meshRenderer.material = instance;
            AssetsReference.Ref(material, meshRenderer.gameObject);
            var reference = meshRenderer.GetComponent<MaterialInstanceReference>();
            if (reference == null)
            {
                reference = meshRenderer.gameObject.AddComponent<MaterialInstanceReference>();
            }

            reference.Set(instance);
        }

        #region SetMaterial

        public static void SetMaterial(this Image image, string location, bool isAsync = false, string packageName = "")
        {
            if (image == null)
            {
                throw new GameFrameworkException("SetSprite failed. Because image is null.");
            }

            CheckResourceManager();

            if (!isAsync)
            {
                Material material = _resourceService.LoadAsset<Material>(location, packageName);
                image.material = material;
                AssetsReference.Ref(material, image.gameObject);
                return;
            }

            LoadMaterialAsync(location, packageName, MaterialSetRequest.Create(image));
        }

        public static void SetMaterial(this SpriteRenderer spriteRenderer, string location, bool isAsync = false, string packageName = "")
        {
            if (spriteRenderer == null)
            {
                throw new GameFrameworkException("SetSprite failed. Because image is null.");
            }

            CheckResourceManager();

            if (!isAsync)
            {
                Material material = _resourceService.LoadAsset<Material>(location, packageName);
                spriteRenderer.material = material;
                AssetsReference.Ref(material, spriteRenderer.gameObject);
                return;
            }

            LoadMaterialAsync(location, packageName, MaterialSetRequest.Create(spriteRenderer));
        }

        public static void SetMaterial(this MeshRenderer meshRenderer, string location, bool needInstance = true, bool isAsync = false, string packageName = "")
        {
            if (meshRenderer == null)
            {
                throw new GameFrameworkException("SetSprite failed. Because image is null.");
            }

            CheckResourceManager();

            if (!isAsync)
            {
                Material material = _resourceService.LoadAsset<Material>(location, packageName);
                SetMeshMaterial(meshRenderer, material, needInstance);
                return;
            }

            LoadMaterialAsync(location, packageName, MaterialSetRequest.Create(meshRenderer, needInstance, false));
        }

        public static void SetSharedMaterial(this MeshRenderer meshRenderer, string location, bool isAsync = false, string packageName = "")
        {
            if (meshRenderer == null)
            {
                throw new GameFrameworkException("SetSprite failed. Because image is null.");
            }

            CheckResourceManager();

            if (!isAsync)
            {
                Material material = _resourceService.LoadAsset<Material>(location, packageName);
                meshRenderer.sharedMaterial = material;
                AssetsReference.Ref(material, meshRenderer.gameObject);
                return;
            }

            LoadMaterialAsync(location, packageName, MaterialSetRequest.Create(meshRenderer, false, true));
        }

        #endregion
    }
}
