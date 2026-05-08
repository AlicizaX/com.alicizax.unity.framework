using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    public interface IResourceLoader
    {
        GameObject LoadPrefab(string location);
        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);
        GameObject LoadGameObject(string location, Transform parent = null);
        UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);
        void UnloadAsset(GameObject gameObject);
    }

    public class UnityResourcesLoader : IResourceLoader
    {
        public GameObject LoadPrefab(string location)
        {
            if (TryGetResourceService(out var resourceService))
            {
                var prefab = resourceService.LoadAsset<GameObject>(location);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            return null;
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            if (TryGetResourceService(out var resourceService))
            {
                var prefab = await resourceService.LoadAssetAsync<GameObject>(location, cancellationToken);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            return null;
        }

        public GameObject LoadGameObject(string location, Transform parent = null)
        {
            if (TryGetResourceService(out var resourceService))
            {
                var managedObject = resourceService.LoadGameObject(location, parent);
                if (managedObject != null)
                {
                    return managedObject;
                }
            }

            return null;
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (TryGetResourceService(out var resourceService))
            {
                var managedObject = await resourceService.LoadGameObjectAsync(location, parent, cancellationToken);
                if (managedObject != null)
                {
                    return managedObject;
                }
            }

            return null;
        }

        public void UnloadAsset(GameObject gameObject)
        {
            // Resources.UnloadAsset cannot unload GameObjects.
            // The prefab reference is nulled by the caller; Unity handles cleanup via UnloadUnusedAssets.
        }

        private static bool TryGetResourceService(out IResourceService resourceService)
        {
            return AppServices.TryGet(out resourceService);
        }
    }

    public class AssetBundleResourceLoader : IResourceLoader
    {
        private IResourceService _resourceService;

        IResourceService ResourceService
        {
            get
            {
                if (_resourceService == null)
                {
                    _resourceService = AppServices.Require<IResourceService>();
                }

                return _resourceService;
            }
        }


        public GameObject LoadPrefab(string location)
        {
            return ResourceService.LoadAsset<GameObject>(location);
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            return await ResourceService.LoadAssetAsync<GameObject>(location, cancellationToken);
        }

        public GameObject LoadGameObject(string location, Transform parent = null)
        {
            return ResourceService.LoadGameObject(location, parent);
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            return await ResourceService.LoadGameObjectAsync(location, parent, cancellationToken);
        }

        public void UnloadAsset(GameObject gameObject)
        {
            ResourceService.UnloadAsset(gameObject);
        }
    }
}
