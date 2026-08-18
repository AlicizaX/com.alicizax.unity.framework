using System.Collections.Generic;
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
        private readonly Dictionary<int, List<ResourceAssetLease<GameObject>>> _leases =
            new Dictionary<int, List<ResourceAssetLease<GameObject>>>();

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
            ResourceAssetLease<GameObject> lease = ResourceService.LoadLease<GameObject>(location);
            return RetainLease(lease);
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            ResourceAssetLease<GameObject> lease = await ResourceService.LoadLeaseAsync<GameObject>(location, cancellationToken);
            return RetainLease(lease);
        }

        public void UnloadPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            int instanceId = prefab.GetInstanceID();
            if (!_leases.TryGetValue(instanceId, out List<ResourceAssetLease<GameObject>> leases) || leases.Count == 0)
            {
                return;
            }

            int last = leases.Count - 1;
            ResourceAssetLease<GameObject> lease = leases[last];
            leases.RemoveAt(last);
            if (leases.Count == 0)
            {
                _leases.Remove(instanceId);
            }

            lease.Dispose();
        }

        private GameObject RetainLease(ResourceAssetLease<GameObject> lease)
        {
            if (!lease.IsValid)
            {
                return null;
            }

            int instanceId = lease.Asset.GetInstanceID();
            if (!_leases.TryGetValue(instanceId, out List<ResourceAssetLease<GameObject>> leases))
            {
                leases = new List<ResourceAssetLease<GameObject>>(1);
                _leases.Add(instanceId, leases);
            }

            leases.Add(lease);
            return lease.Asset;
        }
    }
}
