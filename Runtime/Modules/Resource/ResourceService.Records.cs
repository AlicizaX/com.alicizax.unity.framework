using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        private const int RecordPageBits = 8;
        private const int RecordPageSize = 1 << RecordPageBits;
        private const int RecordPageMask = RecordPageSize - 1;

        private AssetSlot[][] _assetSlotPages;
        private LeaseSlot[][] _leaseSlotPages;
        private int _assetSlotNextIndex;
        private int _leaseSlotNextIndex;
        private int _loadKeyNextId = 1;
        private uint _nextLeaseGeneration;
        private int _assetSlotFreeHead = -1;
        private int _leaseSlotFreeHead = -1;
        private int _idleHead = -1;
        private int _idleTail = -1;
        private int _idleCursor = -1;
        private int _idleCount;
        private readonly ResourceUlongIntMap _assetRecordsByKey = new ResourceUlongIntMap();
        private readonly ResourceIndexMap<int, int> _assetRecordByLoadKeyId = new ResourceIndexMap<int, int>();
        private readonly ResourceIndexMap<ulong, int> _legacyLeaseHeadByUnityObjectId = new ResourceIndexMap<ulong, int>();
        private readonly ResourceUlongIntMap _ownerLeaseByKey = new ResourceUlongIntMap();

        private struct AssetSlot
        {
            public ulong Key;
            public int LoadKeyId;
            public Object Asset;
            public HandleBase Handle;
            public uint Generation;
            public int DirectRefCount;
            public int LegacyDirectRefCount;
            public int BindingRefCount;
            public int PendingRefCount;
            public float IdleExpireTime;
            public int IdlePrevious;
            public int IdleNext;
            public int NextFree;
            public ResourceAssetKind AssetKind;
            public ResourceAssetState State;
        }

        private struct LeaseSlot
        {
            public int AssetId;
            public uint AssetGeneration;
            public uint Generation;
            public ResourceLeaseKind Kind;
            public ResourceLeaseState State;
            public int PreviousLegacy;
            public int NextLegacy;
            public int NextOwned;
            public int NextFree;
        }

        private void InitializeAssetRecords()
        {
            _assetSlotFreeHead = -1;
            _leaseSlotFreeHead = -1;
            _idleHead = -1;
            _idleTail = -1;
            _idleCursor = -1;
        }

        public void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity)
        {
            for (int i = 0; i < assetCapacity; i += RecordPageSize)
                ReserveAssetPage(i);
            for (int i = 0; i < leaseCapacity; i += RecordPageSize)
                ReserveLeasePage(i);
            _assetRecordsByKey.ReserveCapacity(assetCapacity);
            _assetRecordByLoadKeyId.ReserveCapacity(assetCapacity);
            _assetLoadingOperationByKey.EnsureCapacity(assetCapacity);
            _assetInfoByKey.ReserveCapacity(assetCapacity);
            _legacyLeaseHeadByUnityObjectId.ReserveCapacity(unityObjectIndexCapacity);
            _ownerLeaseByKey.ReserveCapacity(leaseCapacity);
        }

        private void ShutdownAssetRecords()
        {
            ForceReleaseAllAssetRecords();
            _assetSlotPages = null;
            _leaseSlotPages = null;
            _assetSlotNextIndex = 0;
            _assetSlotFreeHead = -1;
        }

        public ResourceLeaseHandle AcquireDirect(ResourceKey key)
        {
            return AcquireLeaseSync(key, ResourceLeaseKind.Direct);
        }

        internal ResourceLeaseHandle AcquireBinding(in ResourceKey key)
        {
            return AcquireLeaseSync(key, ResourceLeaseKind.Binding);
        }

        public UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            return AcquireLeaseAsync(key, ResourceLeaseKind.Direct, cancellationToken);
        }

        internal UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken = default, ResourceLoadLifetime lifetime = default)
        {
            return AcquireLeaseAsync(key, ResourceLeaseKind.Binding, cancellationToken, lifetime: lifetime);
        }

        public bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            handle = AcquireDirect(key);
            return handle.IsValid;
        }

        private bool TryGetCachedAssetId(in ResourceKey key, out int assetId)
        {
            assetId = -1;
            if (key.HasResolvedIds)
            {
                if (!_assetRecordByLoadKeyId.TryGetValue(key.LoadKeyId, out assetId))
                    return false;
            }
            else
            {
                ResourceAssetKind kind = NormalizeAssetKind(key.AssetType, key.AssetKind);
                ResourceHandleKind handleKind = kind == ResourceAssetKind.SubAssets ? ResourceHandleKind.SubAssetsHandle : ResourceHandleKind.AssetHandle;
                if (!TryGetResourceKey(key.PackageName, key.Location, key.AssetType, kind, handleKind, out ulong recordKey) ||
                    !_assetRecordsByKey.TryGetValue(recordKey, out assetId))
                    return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            return slot.State != ResourceAssetState.Released && slot.Handle.IsValid &&
                   (slot.AssetKind == ResourceAssetKind.SubAssets || slot.Asset != null);
        }

        public void Release(ResourceLeaseHandle handle)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
                return;

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            ref AssetSlot asset = ref GetAssetSlotRef(lease.AssetId);
            int assetId = lease.AssetId;
            switch (lease.Kind)
            {
                case ResourceLeaseKind.Direct:
                    asset.DirectRefCount--;
                    break;
                case ResourceLeaseKind.Binding:
                    asset.BindingRefCount--;
                    break;
                case ResourceLeaseKind.Legacy:
                    UnlinkLegacyLease(leaseIndex, ref lease, ref asset);
                    asset.LegacyDirectRefCount--;
                    break;
            }

            FreeLeaseSlot(leaseIndex);
            UpdateAssetStateAndIdleQueue(assetId, ref asset);
        }

        internal void ProcessResourceMaintenance(float unscaledTime, int maxCount = 16)
        {
            if (_isResetting) RemoveCompletedUnloadAllOperations();
            _bindingService?.ProcessDestroyedObjects(64);
            int count = Math.Min(maxCount, _idleCount);
            for (int i = 0; i < count && _idleHead >= 0; i++)
            {
                int assetId = _idleCursor >= 0 ? _idleCursor : _idleHead;
                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                _idleCursor = slot.IdleNext >= 0 ? slot.IdleNext : _idleHead;
                if (slot.IdleExpireTime <= unscaledTime)
                    ReleaseAssetStorage(assetId);
            }
        }

        public bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)
        {
            asset = null;
            if (!TryGetLeaseAssetId(handle, out int assetId))
                return false;
            asset = GetAssetSlotRef(assetId).Asset;
            return asset != null;
        }

        internal bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            assetId = -1;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
                return false;
            assetId = GetLeaseSlotRef(leaseIndex).AssetId;
            return true;
        }

        internal bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(spriteName) || !TryGetLeaseAssetId(handle, out int assetId))
                return false;
            if (GetAssetSlotRef(assetId).Handle is not SubAssetsHandle subAssets)
                return false;
            sprite = subAssets.GetSubAssetObject<Sprite>(spriteName);
            return sprite != null;
        }

        public int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
        {
            int total = _assetSlotNextIndex;
            if (results == null || maxCount <= 0)
                return total;
            int start = Math.Max(0, startIndex);
            int count = Math.Min(Math.Min(maxCount, results.Length), total - start);
            for (int i = 0; i < count; i++)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(start + i);
                Type type = GetAssetTypeById(UnpackTypeId(slot.Key));
                results[i] = new ResourceAssetInfo
                {
                    LoadKeyId = slot.LoadKeyId,
                    Package = GetPackageNameById(UnpackPackageId(slot.Key)),
                    Location = GetLocationNameById(UnpackLocationId(slot.Key)),
                    TypeName = type?.Name ?? string.Empty,
                    Kind = slot.AssetKind,
                    State = slot.State,
                    DirectRefCount = slot.DirectRefCount,
                    LegacyDirectRefCount = slot.LegacyDirectRefCount,
                    BindingRefCount = slot.BindingRefCount,
                    PendingRefCount = slot.PendingRefCount,
                    IdleExpireIn = slot.State == ResourceAssetState.Idle ? Math.Max(0, slot.IdleExpireTime - Time.unscaledTime) : 0,
                    RefCountTotal = ReferenceCount(ref slot),
                    HandleValid = slot.Handle != null && slot.Handle.IsValid,
                    HandleKind = (byte)(slot.Handle is SubAssetsHandle ? ResourceHandleKind.SubAssetsHandle : ResourceHandleKind.AssetHandle)
                };
            }
            return total;
        }

        internal int ReleaseAllUnusedAssetRecords()
        {
            int count = _idleCount;
            while (_idleHead >= 0)
                ReleaseAssetStorage(_idleHead);
            return count;
        }

        internal void ForceReleaseAllAssetRecords()
        {
            for (int i = 0; i < _assetSlotNextIndex; i++)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(i);
                if (slot.State == ResourceAssetState.Released)
                    continue;
                DisposeHandle(slot.Handle);
                ReleaseResourceKey(slot.Key);
                ClearAssetSlot(ref slot);
                slot.NextFree = _assetSlotFreeHead;
                _assetSlotFreeHead = i;
            }
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _legacyLeaseHeadByUnityObjectId.Clear();
            _ownerLeaseByKey.Clear();
            _idleCount = 0;
            _idleHead = _idleTail = _idleCursor = -1;
            for (int i = 0; i < _leaseSlotNextIndex; i++)
                GetLeaseSlotRef(i) = default;
            _leaseSlotNextIndex = 0;
            _leaseSlotFreeHead = -1;
        }

        internal ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            return AcquireDirect(new ResourceKey(location, packageName, typeof(GameObject), ResourceAssetKind.Prefab));
        }

        internal UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken)
        {
            return AcquireDirectAsync(new ResourceKey(location, packageName, typeof(GameObject), ResourceAssetKind.Prefab), cancellationToken);
        }

        private ResourceLeaseHandle AcquireLease(int assetId, ResourceLeaseKind kind)
        {
            ref AssetSlot asset = ref GetAssetSlotRef(assetId);
            int leaseIndex = AllocateLeaseSlot();
            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.AssetId = assetId;
            lease.AssetGeneration = asset.Generation;
            lease.Kind = kind;
            lease.State = ResourceLeaseState.Active;
            if (kind == ResourceLeaseKind.Binding)
                asset.BindingRefCount++;
            else if (kind == ResourceLeaseKind.Legacy)
            {
                asset.LegacyDirectRefCount++;
                ulong objectId = UnityObjectId.Get(asset.Asset);
                if (_legacyLeaseHeadByUnityObjectId.TryGetValue(objectId, out int head))
                {
                    lease.NextLegacy = head;
                    GetLeaseSlotRef(head).PreviousLegacy = leaseIndex;
                }
                _legacyLeaseHeadByUnityObjectId.Set(objectId, leaseIndex);
            }
            else
                asset.DirectRefCount++;
            RemoveIdle(assetId, ref asset);
            asset.State = ResourceAssetState.Active;
            return new ResourceLeaseHandle(leaseIndex, lease.Generation);
        }

        private void UnlinkLegacyLease(int leaseIndex, ref LeaseSlot lease, ref AssetSlot asset)
        {
            ulong objectId = UnityObjectId.Get(asset.Asset);
            if (lease.PreviousLegacy >= 0)
                GetLeaseSlotRef(lease.PreviousLegacy).NextLegacy = lease.NextLegacy;
            else if (lease.NextLegacy >= 0)
                _legacyLeaseHeadByUnityObjectId.Set(objectId, lease.NextLegacy);
            else
                _legacyLeaseHeadByUnityObjectId.Remove(objectId);
            if (lease.NextLegacy >= 0)
                GetLeaseSlotRef(lease.NextLegacy).PreviousLegacy = lease.PreviousLegacy;
        }

        private bool TryReleaseLegacyDirectByAsset(object asset)
        {
            if (asset is not Object unityObject || ReferenceEquals(unityObject, null))
                return false;
            if (!_legacyLeaseHeadByUnityObjectId.TryGetValue(UnityObjectId.Get(unityObject), out int index))
                return false;
            Release(new ResourceLeaseHandle(index, GetLeaseSlotRef(index).Generation));
            return true;
        }

        private int CreateAssetRecord(LoadingOperationState operation)
        {
            int assetId = AllocateAssetSlot();
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            slot.Key = operation.Key;
            slot.LoadKeyId = AllocateLoadKeyId();
            slot.Asset = operation.AssetHandle?.AssetObject;
            slot.Handle = operation.AssetHandle != null ? operation.AssetHandle : operation.SubAssetsHandle;
            slot.AssetKind = operation.AssetKind;
            slot.PendingRefCount = 1;
            slot.State = ResourceAssetState.Active;
            _assetRecordsByKey.Set(slot.Key, assetId);
            RetainResourceKey(slot.Key);
            _assetRecordByLoadKeyId.Set(slot.LoadKeyId, assetId);
            return assetId;
        }

        private void ReleaseAssetStorage(int assetId)
        {
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            RemoveIdle(assetId, ref slot);
            _assetRecordsByKey.Remove(slot.Key);
            _assetRecordByLoadKeyId.Remove(slot.LoadKeyId);
            ReleaseResourceKey(slot.Key);
            HandleBase handle = slot.Handle;
            ClearAssetSlot(ref slot);
            slot.NextFree = _assetSlotFreeHead;
            _assetSlotFreeHead = assetId;
            DisposeHandle(handle);
        }

        private int AllocateLoadKeyId()
        {
            if (_loadKeyNextId == int.MaxValue)
                throw new InvalidOperationException("Resource load key space exhausted.");
            return _loadKeyNextId++;
        }

        private string NormalizePackageName(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName;
        }

        private static Type NormalizeAssetType(Type assetType, ResourceAssetKind assetKind)
        {
            switch (assetKind)
            {
                case ResourceAssetKind.Sprite:
                case ResourceAssetKind.SubAssets:
                    return typeof(Sprite);
                case ResourceAssetKind.Material:
                    return typeof(Material);
                case ResourceAssetKind.Prefab:
                    return typeof(GameObject);
                default:
                    return assetType ?? typeof(Object);
            }
        }

        private static ResourceAssetKind NormalizeAssetKind(Type assetType, ResourceAssetKind assetKind)
        {
            return assetKind == ResourceAssetKind.Unknown || assetKind == ResourceAssetKind.Asset ? InferAssetKind(assetType) : assetKind;
        }

        private static ResourceAssetKind InferAssetKind(Type assetType)
        {
            if (assetType == typeof(Sprite)) return ResourceAssetKind.Sprite;
            if (assetType == typeof(Material)) return ResourceAssetKind.Material;
            if (assetType == typeof(GameObject)) return ResourceAssetKind.Prefab;
            return ResourceAssetKind.Asset;
        }

        private static int ReferenceCount(ref AssetSlot slot)
        {
            return slot.DirectRefCount + slot.LegacyDirectRefCount + slot.BindingRefCount + slot.PendingRefCount;
        }

        private void UpdateAssetStateAndIdleQueue(int assetId, ref AssetSlot slot)
        {
            if (ReferenceCount(ref slot) > 0)
            {
                RemoveIdle(assetId, ref slot);
                slot.State = ResourceAssetState.Active;
                return;
            }
            if (slot.State == ResourceAssetState.Idle)
                return;
            slot.State = ResourceAssetState.Idle;
            slot.IdleExpireTime = Time.unscaledTime + IdleAssetExpireTime;
            slot.IdlePrevious = _idleTail;
            slot.IdleNext = -1;
            if (_idleTail >= 0)
                GetAssetSlotRef(_idleTail).IdleNext = assetId;
            else
                _idleHead = assetId;
            _idleTail = assetId;
            _idleCount++;
            if (_idleCount > IdleAssetCapacity)
                ReleaseAssetStorage(_idleHead);
        }

        private void RemoveIdle(int assetId, ref AssetSlot slot)
        {
            if (slot.State != ResourceAssetState.Idle)
                return;
            if (slot.IdlePrevious >= 0)
                GetAssetSlotRef(slot.IdlePrevious).IdleNext = slot.IdleNext;
            else
                _idleHead = slot.IdleNext;
            if (slot.IdleNext >= 0)
                GetAssetSlotRef(slot.IdleNext).IdlePrevious = slot.IdlePrevious;
            else
                _idleTail = slot.IdlePrevious;
            if (_idleCursor == assetId)
                _idleCursor = slot.IdleNext >= 0 ? slot.IdleNext : _idleHead;
            slot.IdlePrevious = slot.IdleNext = -1;
            _idleCount--;
        }

        private static void ClearAssetSlot(ref AssetSlot slot)
        {
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = generation;
            slot.IdlePrevious = slot.IdleNext = -1;
        }

        private int AllocateAssetSlot()
        {
            int index;
            if (_assetSlotFreeHead >= 0)
            {
                index = _assetSlotFreeHead;
                _assetSlotFreeHead = GetAssetSlotRef(index).NextFree;
            }
            else
            {
                index = _assetSlotNextIndex++;
                ReserveAssetPage(index);
            }
            ref AssetSlot slot = ref GetAssetSlotRef(index);
            uint generation = slot.Generation + 1;
            slot = default;
            slot.Generation = generation == 0 ? 1 : generation;
            slot.IdlePrevious = slot.IdleNext = -1;
            return index;
        }

        private int AllocateLeaseSlot()
        {
            int index;
            if (_leaseSlotFreeHead >= 0)
            {
                index = _leaseSlotFreeHead;
                _leaseSlotFreeHead = GetLeaseSlotRef(index).NextFree;
            }
            else
            {
                index = _leaseSlotNextIndex++;
                ReserveLeasePage(index);
            }
            ref LeaseSlot slot = ref GetLeaseSlotRef(index);
            slot = default;
            if (++_nextLeaseGeneration == 0)
                throw new InvalidOperationException("Resource lease generation space exhausted.");
            slot.Generation = _nextLeaseGeneration;
            slot.NextLegacy = slot.PreviousLegacy = -1;
            return index;
        }

        private void FreeLeaseSlot(int index)
        {
            ref LeaseSlot slot = ref GetLeaseSlotRef(index);
            slot = default;
            slot.NextFree = _leaseSlotFreeHead;
            _leaseSlotFreeHead = index;
        }

        private bool TryGetLeaseSlotIndex(ResourceLeaseHandle handle, out int leaseIndex)
        {
            leaseIndex = handle.Index;
            if (!handle.IsValid || leaseIndex >= _leaseSlotNextIndex)
                return false;
            ref LeaseSlot slot = ref GetLeaseSlotRef(leaseIndex);
            if (slot.Generation != handle.Generation || slot.State != ResourceLeaseState.Active)
                return false;
            ref AssetSlot asset = ref GetAssetSlotRef(slot.AssetId);
            return asset.Generation == slot.AssetGeneration && asset.State != ResourceAssetState.Released;
        }

        private ref AssetSlot GetAssetSlotRef(int index)
        {
            return ref _assetSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private ref LeaseSlot GetLeaseSlotRef(int index)
        {
            return ref _leaseSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private void ReserveAssetPage(int index)
        {
            int page = index >> RecordPageBits;
            if (_assetSlotPages == null)
                _assetSlotPages = new AssetSlot[Math.Max(4, page + 1)][];
            else if (page >= _assetSlotPages.Length)
                Array.Resize(ref _assetSlotPages, Math.Max(page + 1, _assetSlotPages.Length << 1));
            if (_assetSlotPages[page] == null)
                _assetSlotPages[page] = new AssetSlot[RecordPageSize];
        }

        private void ReserveLeasePage(int index)
        {
            int page = index >> RecordPageBits;
            if (_leaseSlotPages == null)
                _leaseSlotPages = new LeaseSlot[Math.Max(4, page + 1)][];
            else if (page >= _leaseSlotPages.Length)
                Array.Resize(ref _leaseSlotPages, Math.Max(page + 1, _leaseSlotPages.Length << 1));
            if (_leaseSlotPages[page] == null)
                _leaseSlotPages[page] = new LeaseSlot[RecordPageSize];
        }
    }
}
