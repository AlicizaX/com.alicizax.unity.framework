using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    public interface IPrefabLoader
    {
        GameObject LoadPrefab(string location);
        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);
        void UnloadPrefab(GameObject prefab);
    }

    internal sealed class YooAssetPrefabLoader : IPrefabLoader
    {
        private IResourceService _resourceService;

        private IResourceService ResourceService
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

        public UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            return ResourceService.LoadAssetAsync<GameObject>(location, cancellationToken);
        }

        public void UnloadPrefab(GameObject prefab)
        {
            if (prefab != null)
            {
                ResourceService.UnloadAsset(prefab);
            }
        }
    }
}
