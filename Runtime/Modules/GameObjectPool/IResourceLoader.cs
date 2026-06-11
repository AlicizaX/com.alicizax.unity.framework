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
        private static bool _unloadUnusedAssetsRequested;
        private static bool _unloadUnusedAssetsRunning;

        public GameObject LoadPrefab(string location)
        {
            string resourceLocation = NormalizeResourceLocation(location);
            return string.IsNullOrEmpty(resourceLocation) ? null : Resources.Load<GameObject>(resourceLocation);
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            string resourceLocation = NormalizeResourceLocation(location);
            if (string.IsNullOrEmpty(resourceLocation) || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            ResourceRequest request = Resources.LoadAsync<GameObject>(resourceLocation);
            try
            {
                UnityEngine.Object asset = await request.ToUniTask(cancellationToken: cancellationToken);
                return asset as GameObject;
            }
            catch (System.OperationCanceledException)
            {
                return null;
            }
        }

        public GameObject LoadGameObject(string location, Transform parent = null)
        {
            GameObject prefab = LoadPrefab(location);
            if (prefab == null)
            {
                return null;
            }

            return UnityEngine.Object.Instantiate(prefab, parent);
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            GameObject prefab = await LoadPrefabAsync(location, cancellationToken);
            if (prefab == null || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return UnityEngine.Object.Instantiate(prefab, parent);
        }

        public void UnloadAsset(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            RequestUnloadUnusedAssets();
        }

        private static string NormalizeResourceLocation(string location)
        {
            return PoolEntry.NormalizeConfigAssetPath(location, PoolResourceLoaderType.Resources);
        }

        private static void RequestUnloadUnusedAssets()
        {
            _unloadUnusedAssetsRequested = true;
            if (!_unloadUnusedAssetsRunning)
            {
                RunUnloadUnusedAssetsAsync().Forget();
            }
        }

        private static async UniTask RunUnloadUnusedAssetsAsync()
        {
            _unloadUnusedAssetsRunning = true;
            try
            {
                do
                {
                    _unloadUnusedAssetsRequested = false;
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                    await Resources.UnloadUnusedAssets().ToUniTask();
                } while (_unloadUnusedAssetsRequested);
            }
            finally
            {
                _unloadUnusedAssetsRunning = false;
            }
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
                    _resourceService = AppServices.App.Require<IResourceService>();
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
