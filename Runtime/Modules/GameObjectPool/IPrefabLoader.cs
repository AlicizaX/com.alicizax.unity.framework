using System.Collections.Generic;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
#if UNITY_6000_5_OR_NEWER
    using UnityObjectIdValue = System.UInt64;
#else
    using UnityObjectIdValue = System.Int32;
#endif

    public interface IPrefabLoader
    {
        GameObject LoadPrefab(string location);
        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);
        void UnloadPrefab(GameObject prefab);
    }

    internal sealed class YooAssetPrefabLoader : IPrefabLoader
    {
        private readonly Dictionary<UnityObjectIdValue, List<ResourceAssetLease<GameObject>>> _leases =
            new Dictionary<UnityObjectIdValue, List<ResourceAssetLease<GameObject>>>();

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

            UnityObjectIdValue instanceId = UnityObjectId.Get(prefab);
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

            UnityObjectIdValue instanceId = UnityObjectId.Get(lease.Asset);
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
