using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        public T LoadAsset<T>(ResourceOwner owner, string location, string packageName = "") where T : Object
        {
            ValidateLocation(location);
            if (!CanLoadResources || _bindingService.RegisterOwner(owner) != ResourceBindStatus.Success)
                return null;
            ResourceLoadLifetime lifetime = new ResourceLoadLifetime(_bindingService, owner);
            ResourceLeaseHandle handle = AcquireDirect(new ResourceKey(location, packageName, typeof(T)));
            return TransferToOwner<T>(lifetime, handle);
        }

        public async UniTask<T> LoadAssetAsync<T>(ResourceOwner owner, string location, CancellationToken cancellationToken = default, string packageName = "") where T : Object
        {
            ValidateLocation(location);
            if (cancellationToken.IsCancellationRequested || !CanLoadResources || _bindingService.RegisterOwner(owner) != ResourceBindStatus.Success)
                return null;
            ResourceLoadLifetime lifetime = new ResourceLoadLifetime(_bindingService, owner);
            ResourceLeaseHandle handle = await AcquireLeaseAsync(new ResourceKey(location, packageName, typeof(T)), ResourceLeaseKind.Direct, cancellationToken, lifetime: lifetime);
            return TransferToOwner<T>(lifetime, handle);
        }

        private T TransferToOwner<T>(in ResourceLoadLifetime lifetime, ResourceLeaseHandle handle) where T : Object
        {
            if (!lifetime.IsCurrent || !TryGetLeaseAsset(handle, out Object asset) || asset is not T typedAsset)
            {
                Release(handle);
                return null;
            }
            lifetime.Bindings.RetainAsset(lifetime.OwnerId, handle);
            return typedAsset;
        }

        internal void RetainOwnerLease(int ownerId, ref int head, ResourceLeaseHandle handle)
        {
            ref LeaseSlot lease = ref GetLeaseSlotRef(handle.Index);
            ulong key = ((ulong)(uint)ownerId << 32) | (uint)GetAssetSlotRef(lease.AssetId).LoadKeyId;
            if (_ownerLeaseByKey.TryGetValue(key, out _))
            {
                Release(handle);
                return;
            }
            _ownerLeaseByKey.Set(key, handle.Index);
            lease.NextOwned = head;
            head = handle.Index;
        }

        internal void ReleaseOwnerLeases(int ownerId, ref int head)
        {
            while (head >= 0)
            {
                int index = head;
                ref LeaseSlot lease = ref GetLeaseSlotRef(index);
                head = lease.NextOwned;
                ulong key = ((ulong)(uint)ownerId << 32) | (uint)GetAssetSlotRef(lease.AssetId).LoadKeyId;
                _ownerLeaseByKey.Remove(key);
                Release(new ResourceLeaseHandle(index, lease.Generation));
            }
        }
    }
}
