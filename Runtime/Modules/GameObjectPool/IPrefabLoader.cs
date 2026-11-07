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
        private struct PrefabLease
        {
            public AssetLease<GameObject> Lease;
            public int Count;
        }

        private readonly Dictionary<ulong, PrefabLease> _leases = new Dictionary<ulong, PrefabLease>();

        private IResourceService _resourceService;

        private IResourceService ResourceService => _resourceService ??= AppServices.Require<IResourceService>();

        public GameObject LoadPrefab(string location)
        {
            return RetainLease(ResourceService.LoadLease<GameObject>(location));
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            return RetainLease(await ResourceService.LoadLeaseAsync<GameObject>(location, cancellationToken));
        }

        public void UnloadPrefab(GameObject prefab)
        {
            ulong instanceId = UnityObjectId.Get(prefab);
            if (!_leases.TryGetValue(instanceId, out PrefabLease entry))
            {
                return;
            }

            if (--entry.Count > 0)
            {
                _leases[instanceId] = entry;
                return;
            }

            _leases.Remove(instanceId);
            entry.Lease.Dispose();
        }

        private GameObject RetainLease(AssetLease<GameObject> lease)
        {
            GameObject prefab = lease.Asset;
            if (prefab == null)
            {
                return null;
            }

            ulong instanceId = UnityObjectId.Get(prefab);
            if (_leases.TryGetValue(instanceId, out PrefabLease entry))
            {
                entry.Count++;
                _leases[instanceId] = entry;
                lease.Dispose();
                return prefab;
            }

            _leases.Add(instanceId, new PrefabLease { Lease = lease, Count = 1 });
            return prefab;
        }
    }
}
