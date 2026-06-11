using System;
using System.Threading;
using Cysharp.Text;
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
        private const int KeepAliveBucketCount = 256;
        private const int KeepAliveSeconds = 5;
        private const int IdleBucketCount = 256;

        private AssetSlot[][] _assetSlotPages;
        private LeaseSlot[][] _leaseSlotPages;
        private int[] _keepAliveBuckets;
        private int[] _idleBuckets;
        private int _assetSlotNextIndex;
        private int _leaseSlotNextIndex;
        private int _loadKeyNextId;
        private int _assetSlotFreeHead;
        private int _leaseSlotFreeHead;
        private int[] _unusedAssetCandidates;
        private int _unusedAssetCandidateCount;
        private int _lastKeepAliveProcessTick;
        private int _lastIdleProcessTick;
        private readonly ResourceUlongIntMap _assetRecordsByKey = new ResourceUlongIntMap();
        private readonly ResourceIndexMap<int, int> _assetRecordByLoadKeyId = new ResourceIndexMap<int, int>();
        private readonly ResourceIndexMap<int, int> _assetRecordHeadByUnityObjectId = new ResourceIndexMap<int, int>();

        private struct AssetSlot
        {
            public ulong Key;
            public string PackageName;
            public string Location;
            public Type AssetType;
            public int LoadKeyId;
            public int AssetInstanceId;
            public Object Asset;
            public AssetHandle AssetHandle;
            public SubAssetsHandle SubAssetsHandle;
            public uint Generation;
            public int DirectRefCount;
            public int LegacyDirectRefCount;
            public int BindingRefCount;
            public int KeepAliveRefCount;
            public int IdleReleaseRequested;
            public int NextByUnityObject;
            public int KeepAliveExpireTick;
            public int IdleExpireTick;
            public int ExpireQueuePrev;
            public int ExpireQueueNext;
            public int UnusedCandidateIndex;
            public int NextFree;
            public ResourceAssetKind AssetKind;
            public ResourceHandleKind HandleKind;
            public ResourceAssetState State;
            public byte ExpireQueueKind;
        }

        private struct LeaseSlot
        {
            public int AssetId;
            public uint Generation;
            public ResourceLeaseKind Kind;
            public ResourceLeaseState State;
            public byte Flags;
            public int NextFree;
        }

        private void InitializeAssetRecords()
        {
            _assetSlotPages = null;
            _leaseSlotPages = null;
            _keepAliveBuckets = new int[KeepAliveBucketCount];
            for (int i = 0; i < _keepAliveBuckets.Length; i++)
            {
                _keepAliveBuckets[i] = -1;
            }

            _idleBuckets = new int[IdleBucketCount];
            for (int i = 0; i < _idleBuckets.Length; i++)
            {
                _idleBuckets[i] = -1;
            }

            _assetSlotNextIndex = 0;
            _leaseSlotNextIndex = 0;
            _loadKeyNextId = 1;
            _assetSlotFreeHead = -1;
            _leaseSlotFreeHead = -1;
            _unusedAssetCandidates = null;
            _unusedAssetCandidateCount = 0;
            _lastKeepAliveProcessTick = -1;
            _lastIdleProcessTick = -1;
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _assetRecordHeadByUnityObjectId.Clear();
        }

        public void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity)
        {
            if (assetCapacity > 0)
            {
                EnsureAssetSlotPage(assetCapacity - 1);
                _assetRecordsByKey.EnsureCapacity(assetCapacity);
                _assetRecordByLoadKeyId.EnsureCapacity(assetCapacity);
                _assetLoadingOperationByKey.EnsureCapacity(assetCapacity);
                _assetInfoByKey.EnsureCapacity(assetCapacity);
            }

            if (leaseCapacity > 0)
            {
                EnsureLeaseSlotPage(leaseCapacity - 1);
            }

            if (unityObjectIndexCapacity > 0)
            {
                _assetRecordHeadByUnityObjectId.EnsureCapacity(unityObjectIndexCapacity);
            }
        }

        private void ShutdownAssetRecords()
        {
            if (_assetSlotPages != null)
            {
                for (int page = 0; page < _assetSlotPages.Length; page++)
                {
                    AssetSlot[] slots = _assetSlotPages[page];
                    if (slots == null)
                    {
                        continue;
                    }

                    for (int offset = 0; offset < RecordPageSize; offset++)
                    {
                        ref AssetSlot slot = ref slots[offset];
                        if (slot.State == ResourceAssetState.Released && slot.Asset == null && slot.AssetHandle == null && !IsSubAssetsHandleValid(slot.SubAssetsHandle))
                        {
                            continue;
                        }

                        int assetId = (page << RecordPageBits) + offset;
                        RemoveUnusedAssetCandidate(assetId, ref slot);
                        DisposeAssetSlotHandle(ref slot);
                        ClearAssetSlot(ref slot, preserveGeneration: false);
                    }
                }
            }

            ReleaseAllResourceKeysFromMap(_assetRecordsByKey);
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _assetRecordHeadByUnityObjectId.Clear();
            TrimResourceKeyRegistryIfUnused();
            _assetSlotPages = null;
            _leaseSlotPages = null;
            _unusedAssetCandidates = null;
            _unusedAssetCandidateCount = 0;
            _keepAliveBuckets = null;
            _idleBuckets = null;
            _assetSlotNextIndex = 0;
            _leaseSlotNextIndex = 0;
            _loadKeyNextId = 1;
            _assetSlotFreeHead = -1;
            _leaseSlotFreeHead = -1;
            _lastKeepAliveProcessTick = -1;
            _lastIdleProcessTick = -1;
        }

        public ResourceLeaseHandle AcquireDirect(ResourceKey key)
        {
            return TryAcquireDirect(key, out ResourceLeaseHandle handle) ? handle : ResourceLeaseHandle.Invalid;
        }

        internal ResourceLeaseHandle AcquireBinding(ResourceKey key)
        {
            return TryAcquire(key, ResourceLeaseKind.Binding, out ResourceLeaseHandle handle) ? handle : ResourceLeaseHandle.Invalid;
        }

        public async UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            return await AcquireLeaseAsync(key, ResourceLeaseKind.Direct, cancellationToken);
        }

        internal async UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            return await AcquireLeaseAsync(key, ResourceLeaseKind.Binding, cancellationToken);
        }

        internal async UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, ResourceLeaseOptions leaseOptions, CancellationToken cancellationToken = default)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            if (string.IsNullOrEmpty(location) || !CheckLocationValid(location, normalizedPackageName))
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong recordKey = GetAssetRecordKey(normalizedPackageName, location, typeof(Sprite), ResourceAssetKind.SubAssets, ResourceHandleKind.SubAssetsHandle);
            if (TryGetCachedSubAssetsRecord(recordKey, out int existingAssetId))
            {
                return AcquireLease(existingAssetId, ResourceLeaseKind.Binding, leaseOptions);
            }

            ulong loadingKey = GetSubAssetsLoadingOperationKey(location, normalizedPackageName);
            while (!TryBeginLoading(loadingKey))
            {
                if (!await WaitForLoadingAsync(loadingKey, cancellationToken))
                {
                    return ResourceLeaseHandle.Invalid;
                }

                if (TryGetCachedSubAssetsRecord(recordKey, out existingAssetId))
                {
                    return AcquireLease(existingAssetId, ResourceLeaseKind.Binding, leaseOptions);
                }
            }

            ResourcePackage package = YooAssets.GetPackage(normalizedPackageName);
            if (package == null || _isDestroying)
            {
                FailLoading(loadingKey, null);
                return ResourceLeaseHandle.Invalid;
            }

            int loadGeneration = _assetUnloadGeneration;
            if (!IsLoadingStateCurrent(loadGeneration))
            {
                FailLoading(loadingKey, null);
                return ResourceLeaseHandle.Invalid;
            }

            SubAssetsHandle handle = package.LoadSubAssetsAsync<Sprite>(location);

            bool callerCancellationRequested = false;
            while (handle is { IsValid: true, IsDone: false })
            {
                if (!IsLoadingStateCurrent(loadGeneration))
                {
                    DisposeSubAssetsHandle(handle);
                    FailLoading(loadingKey, null);
                    return ResourceLeaseHandle.Invalid;
                }

                if (ShouldAbortLoadingAfterCallerCancellation(loadingKey, cancellationToken, ref callerCancellationRequested))
                {
                    DisposeSubAssetsHandle(handle);
                    FailLoading(loadingKey, null);
                    return ResourceLeaseHandle.Invalid;
                }

                await UniTask.Yield();
            }

            if (!IsLoadingStateCurrent(loadGeneration))
            {
                DisposeSubAssetsHandle(handle);
                FailLoading(loadingKey, null);
                return ResourceLeaseHandle.Invalid;
            }

            if (!handle.IsValid || handle.Status == EOperationStatus.Failed)
            {
                DisposeSubAssetsHandle(handle);
                FailLoading(loadingKey, null);
                return ResourceLeaseHandle.Invalid;
            }

            int assetId = GetOrCreateSubAssetsRecord(normalizedPackageName, location, handle);
            CompleteLoading(loadingKey);
            return callerCancellationRequested ? ResourceLeaseHandle.Invalid : AcquireLease(assetId, ResourceLeaseKind.Binding, leaseOptions);
        }

        private bool TryGetCachedSubAssetsRecord(ulong recordKey, out int assetId)
        {
            assetId = -1;
            if (!_assetRecordsByKey.TryGetValue(recordKey, out assetId) || !IsValidAssetId(assetId))
            {
                assetId = -1;
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.State == ResourceAssetState.Released ||
                slot.HandleKind != ResourceHandleKind.SubAssetsHandle ||
                !IsSubAssetsHandleValid(slot.SubAssetsHandle))
            {
                assetId = -1;
                return false;
            }

            return true;
        }

        internal bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(spriteName) || !TryGetLeaseAssetId(handle, out int assetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.HandleKind != ResourceHandleKind.SubAssetsHandle || !IsSubAssetsHandleValid(slot.SubAssetsHandle))
            {
                return false;
            }

            sprite = slot.SubAssetsHandle.GetSubAssetObject<Sprite>(spriteName);
            return sprite != null;
        }

        private async UniTask<ResourceLeaseHandle> AcquireLeaseAsync(ResourceKey key, ResourceLeaseKind leaseKind, CancellationToken cancellationToken)
        {
            if (key.HasResolvedIds)
            {
                return TryAcquireResolved(key, leaseKind, out ResourceLeaseHandle resolvedHandle) ? resolvedHandle : ResourceLeaseHandle.Invalid;
            }

            if (string.IsNullOrEmpty(key.Location))
            {
                return ResourceLeaseHandle.Invalid;
            }

            string packageName = NormalizePackageName(key.PackageName);
            Type assetType = NormalizeAssetType(key.AssetType, key.AssetKind);
            if (!CheckLocationValid(key.Location, packageName))
            {
                return ResourceLeaseHandle.Invalid;
            }

            ResourceAssetKind assetKind = key.AssetKind == ResourceAssetKind.Unknown ? InferAssetKind(assetType) : key.AssetKind;
            ulong assetLoadingKey = GetLoadingOperationKey(key.Location, packageName, assetType, assetKind);
            UnityEngine.Object asset = await GetOrLoadAssetAsync(key.Location, assetType, assetKind, packageName, assetLoadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong recordKey = GetAssetRecordKey(packageName, key.Location, assetType, assetKind, ResourceHandleKind.AssetHandle);
            if (!_assetRecordsByKey.TryGetValue(recordKey, out int assetId) || !IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return AcquireLease(assetId, leaseKind, ResourceLeaseOptions.None);
        }

        public bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            return TryAcquire(key, ResourceLeaseKind.Direct, out handle);
        }

        private bool TryAcquireResolved(ResourceKey key, ResourceLeaseKind leaseKind, out ResourceLeaseHandle handle)
        {
            handle = ResourceLeaseHandle.Invalid;
            if (key.LoadKeyId <= 0)
            {
                return false;
            }

            if (!_assetRecordByLoadKeyId.TryGetValue(key.LoadKeyId, out int assetId) || !IsValidAssetId(assetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.LoadKeyId != key.LoadKeyId || slot.State == ResourceAssetState.Released || !IsSlotHandleValid(ref slot))
            {
                return false;
            }

            handle = AcquireLease(assetId, leaseKind, ResourceLeaseOptions.None);
            return handle.IsValid;
        }

        private bool TryAcquire(ResourceKey key, ResourceLeaseKind leaseKind, out ResourceLeaseHandle handle)
        {
            handle = ResourceLeaseHandle.Invalid;

            if (key.HasResolvedIds)
            {
                return TryAcquireResolved(key, leaseKind, out handle);
            }

            if (string.IsNullOrEmpty(key.Location))
            {
                return false;
            }

            string packageName = NormalizePackageName(key.PackageName);
            Type assetType = NormalizeAssetType(key.AssetType, key.AssetKind);
            if (!CheckLocationValid(key.Location, packageName))
            {
                return false;
            }

            ResourceAssetKind assetKind = key.AssetKind == ResourceAssetKind.Unknown ? InferAssetKind(assetType) : key.AssetKind;
            if (TryGetCachedAssetRecord(packageName, key.Location, assetType, assetKind, ResourceHandleKind.AssetHandle, out int cachedAssetId, out _))
            {
                handle = AcquireLease(cachedAssetId, leaseKind, ResourceLeaseOptions.None);
                return handle.IsValid;
            }

            AssetHandle assetHandle = GetHandleSync(key.Location, assetType, packageName);
            if (assetHandle == null || assetHandle.AssetObject == null || assetHandle.Status == EOperationStatus.Failed)
            {
                DisposeHandle(assetHandle);
                return false;
            }

            int assetId = GetOrCreateAssetRecord(packageName, key.Location, assetType, assetKind, ResourceHandleKind.AssetHandle, assetHandle.AssetObject, assetHandle);

            handle = AcquireLease(assetId, leaseKind, ResourceLeaseOptions.None);
            return handle.IsValid;
        }

        public void Release(ResourceLeaseHandle handle)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            int assetId = lease.AssetId;
            ResourceLeaseKind leaseKind = lease.Kind;
            byte leaseFlags = lease.Flags;
            if (!IsValidActiveAssetId(assetId))
            {
                FreeLeaseSlot(leaseIndex);
                return;
            }

            ref AssetSlot asset = ref GetAssetSlotRef(assetId);
            if (leaseKind == ResourceLeaseKind.Direct)
            {
                if (asset.DirectRefCount > 0)
                {
                    asset.DirectRefCount--;
                }
            }
            else if (leaseKind == ResourceLeaseKind.Binding)
            {
                if (asset.BindingRefCount > 0)
                {
                    asset.BindingRefCount--;
                }
            }

            if (leaseKind == ResourceLeaseKind.Binding &&
                (leaseFlags & (byte)ResourceLeaseOptions.KeepAliveOnRelease) != 0 &&
                CanEnterKeepAlive(ref asset))
            {
                EnterKeepAlive(assetId, ref asset);
            }

            UpdateAssetStateAndIdleQueue(assetId, ref asset);
            FreeLeaseSlot(leaseIndex);
        }

        internal void ProcessKeepAlive(float unscaledTime, int maxCount = 16)
        {
            if ((_keepAliveBuckets == null && _idleBuckets == null) || maxCount <= 0)
            {
                return;
            }

            int currentTick = ToKeepAliveTick(unscaledTime);
            int processed = ProcessDueKeepAliveBuckets(currentTick, maxCount);
            if (processed < maxCount)
            {
                ProcessDueIdleBuckets(currentTick, maxCount - processed);
            }
        }

        private int ProcessDueKeepAliveBuckets(int currentTick, int maxCount)
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            if (_lastKeepAliveProcessTick < 0 || currentTick - _lastKeepAliveProcessTick > KeepAliveBucketCount)
            {
                _lastKeepAliveProcessTick = currentTick - KeepAliveBucketCount;
            }

            int processed = 0;
            while (_lastKeepAliveProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = _lastKeepAliveProcessTick + 1;
                int bucketProcessed = ProcessKeepAliveBucket(bucketTick, currentTick, maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                _lastKeepAliveProcessTick = bucketTick;
            }

            return processed;
        }

        private int ProcessDueIdleBuckets(int currentTick, int maxCount)
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            if (_lastIdleProcessTick < 0 || currentTick - _lastIdleProcessTick > IdleBucketCount)
            {
                _lastIdleProcessTick = currentTick - IdleBucketCount;
            }

            int processed = 0;
            while (_lastIdleProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = _lastIdleProcessTick + 1;
                int bucketProcessed = ProcessIdleBucket(bucketTick, currentTick, maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                _lastIdleProcessTick = bucketTick;
            }

            return processed;
        }

        private int ProcessKeepAliveBucket(int bucketTick, int currentTick, int maxCount, out bool completed)
        {
            completed = true;
            if (_keepAliveBuckets == null || maxCount <= 0)
            {
                return 0;
            }

            int bucket = bucketTick & (KeepAliveBucketCount - 1);
            int processed = 0;
            int current = _keepAliveBuckets[bucket];
            while (current >= 0)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                if (slot.ExpireQueueKind == 1 && slot.KeepAliveExpireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromKeepAliveBucket(current, ref slot);
                    if (slot.KeepAliveRefCount > 0)
                    {
                        slot.KeepAliveRefCount = 0;
                    }

                    UpdateAssetStateAndIdleQueue(current, ref slot);
                    processed++;
                }

                current = next;
            }

            return processed;
        }

        private int ProcessIdleBucket(int bucketTick, int currentTick, int maxCount, out bool completed)
        {
            completed = true;
            if (_idleBuckets == null || maxCount <= 0)
            {
                return 0;
            }

            int bucket = bucketTick & (IdleBucketCount - 1);
            int processed = 0;
            int current = _idleBuckets[bucket];
            while (current >= 0)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromIdleBucket(current, ref slot);
                    if (HasNoResourceRefs(ref slot))
                    {
                        slot.IdleReleaseRequested = 1;
                        ReleaseAssetStorage(current, slot.Generation);
                    }
                    else
                    {
                        UpdateAssetStateAndIdleQueue(current, ref slot);
                    }

                    processed++;
                }

                current = next;
            }

            return processed;
        }

        internal bool TryGetLeaseAssetObject(ResourceLeaseHandle handle, out Object asset)
        {
            asset = null;
            if (!TryGetLeaseAssetId(handle, out int assetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.State == ResourceAssetState.Released || slot.Asset == null)
            {
                return false;
            }

            asset = slot.Asset;
            return true;
        }

        public bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)
        {
            return TryGetLeaseAssetObject(handle, out asset);
        }

        internal bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            assetId = -1;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            if (!IsValidActiveAssetId(lease.AssetId))
            {
                return false;
            }

            assetId = lease.AssetId;
            return true;
        }

        internal bool SetLeaseOptions(ResourceLeaseHandle handle, ResourceLeaseOptions options)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.Flags = (byte)options;
            return true;
        }

        public int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
        {
            int total = _assetSlotNextIndex;
            if (results == null || maxCount <= 0 || startIndex >= total)
            {
                return total;
            }

            int writeCount = Math.Min(Math.Min(maxCount, results.Length), total - Math.Max(0, startIndex));
            int written = 0;
            int index = Math.Max(0, startIndex);
            while (index < total && written < writeCount)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(index);
                ref ResourceAssetInfo info = ref results[written];
                info.LoadKeyId = slot.LoadKeyId;
                info.Package = slot.PackageName;
                info.Location = slot.Location;
                info.TypeName = slot.AssetType != null ? slot.AssetType.Name : string.Empty;
                info.Kind = slot.AssetKind;
                info.State = slot.State;
                info.DirectRefCount = slot.DirectRefCount;
                info.LegacyDirectRefCount = slot.LegacyDirectRefCount;
                info.BindingRefCount = slot.BindingRefCount;
                info.KeepAliveRefCount = slot.KeepAliveRefCount;
                int currentTick = ToKeepAliveTick(Time.unscaledTime);
                info.KeepAliveExpireIn = slot.KeepAliveRefCount > 0 ? Math.Max(0, slot.KeepAliveExpireTick - currentTick) : 0;
                info.IdleExpireIn = slot.State == ResourceAssetState.Idle && slot.ExpireQueueKind == 2 ? Math.Max(0, slot.IdleExpireTick - currentTick) : 0;
                info.RefCountTotal = slot.DirectRefCount + slot.LegacyDirectRefCount + slot.BindingRefCount + slot.KeepAliveRefCount;
                info.IdleReleaseRequested = slot.IdleReleaseRequested != 0;
                info.HandleValid = IsSlotHandleValid(ref slot);
                info.HandleKind = (byte)slot.HandleKind;
                written++;
                index++;
            }

            return total;
        }

        internal int ReleaseAllUnusedAssetRecords()
        {
            int releasedCount = 0;
            int index = 0;
            while (index < _unusedAssetCandidateCount)
            {
                int assetId = _unusedAssetCandidates[index];
                if (!IsValidAssetId(assetId))
                {
                    RemoveUnusedAssetCandidateAt(index);
                    continue;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                if (slot.Generation == 0 || slot.State == ResourceAssetState.Released || !IsSlotHandleValid(ref slot))
                {
                    RemoveUnusedAssetCandidateAt(index);
                    continue;
                }

                if (!HasNoResourceRefs(ref slot))
                {
                    RemoveUnusedAssetCandidate(assetId, ref slot);
                    continue;
                }

                slot.IdleReleaseRequested = 1;
                uint generation = slot.Generation;
                int previousCandidateCount = _unusedAssetCandidateCount;
                ReleaseAssetStorage(assetId, generation);
                if (_unusedAssetCandidateCount == previousCandidateCount && index < _unusedAssetCandidateCount && _unusedAssetCandidates[index] == assetId)
                {
                    RemoveUnusedAssetCandidateAt(index);
                }

                releasedCount++;
            }

            return releasedCount;
        }

        internal void ForceReleaseAllAssetRecords()
        {
            int total = _assetSlotNextIndex;
            for (int i = 0; i < total; i++)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(i);
                if (slot.Generation == 0 || slot.State == ResourceAssetState.Released)
                {
                    continue;
                }

                RemoveFromExpiryQueue(i, ref slot);
                RemoveUnusedAssetCandidate(i, ref slot);
                DisposeAssetSlotHandle(ref slot);
                UnlinkAssetByUnityObject(i, ref slot);
                ClearAssetSlot(ref slot, preserveGeneration: true);
                FreeAssetSlot(i);
            }

            ReleaseAllResourceKeysFromMap(_assetRecordsByKey);
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _assetRecordHeadByUnityObjectId.Clear();
            _unusedAssetCandidateCount = 0;

            _leaseSlotNextIndex = 0;
            _leaseSlotFreeHead = -1;
            TrimResourceKeyRegistryIfUnused();
        }

        internal ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            ResourceKey key = new ResourceKey(location, packageName, typeof(GameObject), ResourceAssetKind.Prefab);
            return AcquireDirect(key);
        }

        internal async UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return ResourceLeaseHandle.Invalid;
            }

            string normalizedPackageName = NormalizePackageName(packageName);
            if (!CheckLocationValid(location, normalizedPackageName))
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong assetLoadingKey = GetLoadingOperationKey(location, normalizedPackageName, typeof(GameObject), ResourceAssetKind.Prefab);
            UnityEngine.Object asset = await GetOrLoadAssetAsync(location, typeof(GameObject), ResourceAssetKind.Prefab, normalizedPackageName, assetLoadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong key = GetAssetRecordKey(normalizedPackageName, location, typeof(GameObject), ResourceAssetKind.Prefab, ResourceHandleKind.AssetHandle);
            if (!_assetRecordsByKey.TryGetValue(key, out int assetId) || !IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return AcquireLease(assetId, ResourceLeaseKind.Direct, ResourceLeaseOptions.None);
        }

        private ResourceLeaseHandle AcquireLease(int assetId, ResourceLeaseKind leaseKind, ResourceLeaseOptions options)
        {
            if (!IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            ref AssetSlot asset = ref GetAssetSlotRef(assetId);
            if (leaseKind == ResourceLeaseKind.Binding)
            {
                asset.BindingRefCount++;
            }
            else
            {
                asset.DirectRefCount++;
                leaseKind = ResourceLeaseKind.Direct;
            }

            asset.IdleReleaseRequested = 0;
            RemoveUnusedAssetCandidate(assetId, ref asset);
            RemoveFromExpiryQueue(assetId, ref asset);
            if (asset.KeepAliveRefCount > 0)
            {
                asset.KeepAliveRefCount = 0;
            }

            UpdateAssetState(ref asset);

            int leaseIndex = AllocateLeaseSlot();
            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.AssetId = assetId;
            lease.Kind = leaseKind;
            lease.State = ResourceLeaseState.Active;
            lease.Flags = (byte)options;
            return new ResourceLeaseHandle(leaseIndex, lease.Generation);
        }

        private int GetOrCreateAssetRecord(string packageName, string location, Type assetType, ResourceAssetKind assetKind,
            ResourceHandleKind handleKind, Object asset, AssetHandle assetHandle)
        {
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            string normalizedPackageName = NormalizePackageName(packageName);
            ulong key = GetAssetRecordKey(normalizedPackageName, location, assetType, assetKind, handleKind);
            if (_assetRecordsByKey.TryGetValue(key, out int existingId) && IsValidAssetId(existingId))
            {
                ref AssetSlot existing = ref GetAssetSlotRef(existingId);
                if (existing.Asset == null && asset != null)
                {
                    existing.Asset = asset;
                    existing.AssetInstanceId = ResourceUnityObjectId.Get(asset);
                    LinkAssetByUnityObject(existingId, ref existing);
                }

                if (existing.AssetHandle == null && assetHandle != null)
                {
                    existing.AssetHandle = assetHandle;
                    existing.HandleKind = handleKind;
                }

                UpdateAssetStateAndIdleQueue(existingId, ref existing);
                return existingId;
            }

            int assetId = AllocateAssetSlot();
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            slot.Key = key;
            slot.PackageName = normalizedPackageName;
            slot.Location = location;
            slot.AssetType = assetType;
            slot.LoadKeyId = AllocateLoadKeyId();
            slot.Asset = asset;
            slot.AssetInstanceId = ResourceUnityObjectId.Get(asset);
            slot.AssetHandle = assetHandle;
            slot.AssetKind = assetKind;
            slot.HandleKind = handleKind;
            slot.NextByUnityObject = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.NextFree = -1;
            slot.State = ResourceAssetState.Idle;
            _assetRecordsByKey.Set(key, assetId);
            RetainResourceKey(key);
            _assetRecordByLoadKeyId.Set(slot.LoadKeyId, assetId);
            LinkAssetByUnityObject(assetId, ref slot);
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return assetId;
        }

        private int GetOrCreateSubAssetsRecord(string packageName, string location, SubAssetsHandle subAssetsHandle)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            ulong key = GetAssetRecordKey(normalizedPackageName, location, typeof(Sprite), ResourceAssetKind.SubAssets, ResourceHandleKind.SubAssetsHandle);
            if (_assetRecordsByKey.TryGetValue(key, out int existingId) && IsValidAssetId(existingId))
            {
                ref AssetSlot existing = ref GetAssetSlotRef(existingId);
                if (!IsSubAssetsHandleValid(existing.SubAssetsHandle) && IsSubAssetsHandleValid(subAssetsHandle))
                {
                    existing.SubAssetsHandle = subAssetsHandle;
                    existing.HandleKind = ResourceHandleKind.SubAssetsHandle;
                }
                else if (IsSubAssetsHandleValid(subAssetsHandle) && !ReferenceEquals(existing.SubAssetsHandle, subAssetsHandle))
                {
                    DisposeSubAssetsHandle(subAssetsHandle);
                }

                UpdateAssetStateAndIdleQueue(existingId, ref existing);
                return existingId;
            }

            int assetId = AllocateAssetSlot();
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            slot.Key = key;
            slot.PackageName = normalizedPackageName;
            slot.Location = location;
            slot.AssetType = typeof(Sprite);
            slot.LoadKeyId = AllocateLoadKeyId();
            slot.Asset = null;
            slot.AssetInstanceId = 0;
            slot.SubAssetsHandle = subAssetsHandle;
            slot.AssetKind = ResourceAssetKind.SubAssets;
            slot.HandleKind = ResourceHandleKind.SubAssetsHandle;
            slot.NextByUnityObject = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.NextFree = -1;
            slot.State = ResourceAssetState.Idle;
            _assetRecordsByKey.Set(key, assetId);
            RetainResourceKey(key);
            _assetRecordByLoadKeyId.Set(slot.LoadKeyId, assetId);
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return assetId;
        }

        private bool TryGetCachedAssetRecord(string packageName, string location, Type assetType, ResourceAssetKind assetKind, ResourceHandleKind handleKind, out int assetId, out Object asset)
        {
            assetId = -1;
            asset = null;
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            ulong key = GetAssetRecordKey(packageName, location, assetType, assetKind, handleKind);
            if (!_assetRecordsByKey.TryGetValue(key, out assetId) || !IsValidAssetId(assetId))
            {
                assetId = -1;
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.State == ResourceAssetState.Released || slot.Asset == null || !IsSlotHandleValid(ref slot))
            {
                assetId = -1;
                return false;
            }

            asset = slot.Asset;
            return true;
        }

        private bool TryAddLegacyDirectRef(int assetId, uint generation)
        {
            if (!IsValidAssetId(assetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.Generation != generation || slot.State == ResourceAssetState.Released)
            {
                return false;
            }

            slot.LegacyDirectRefCount++;
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return true;
        }

        private bool TryAddLegacyDirectRefByKey(string packageName, string location, Type assetType, Object asset)
        {
            ResourceAssetKind assetKind = InferAssetKind(assetType);
            assetType = NormalizeAssetType(assetType, assetKind);
            ulong key = GetAssetRecordKey(packageName, location, assetType, assetKind, ResourceHandleKind.AssetHandle);
            if (_assetRecordsByKey.TryGetValue(key, out int assetId) && IsValidAssetId(assetId))
            {
                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                return TryAddLegacyDirectRef(assetId, slot.Generation);
            }

            return TryAddLegacyDirectRefByAsset(asset);
        }

        private bool TryAddLegacyDirectRefByAsset(Object asset)
        {
            if (asset == null)
            {
                return false;
            }

            int instanceId = ResourceUnityObjectId.Get(asset);
            if (!_assetRecordHeadByUnityObjectId.TryGetValue(instanceId, out int current))
            {
                return false;
            }

            int matchCount = 0;
            int matchedAssetId = -1;
            while (current >= 0)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.NextByUnityObject;
                if (slot.AssetInstanceId == instanceId && slot.State != ResourceAssetState.Released)
                {
                    matchCount++;
                    if (matchedAssetId < 0)
                    {
                        matchedAssetId = current;
                    }
                }

                current = next;
            }

            if (matchedAssetId < 0)
            {
                return false;
            }

            if (matchCount > 1)
            {
                Log.Warning(ZString.Format("Legacy asset acquire is ambiguous. Asset instance id {0} maps to {1} Resource records. Use ResourceLeaseHandle for explicit ownership.", instanceId, matchCount));
                return false;
            }

            ref AssetSlot matched = ref GetAssetSlotRef(matchedAssetId);
            return TryAddLegacyDirectRef(matchedAssetId, matched.Generation);
        }

        private bool TryReleaseLegacyDirectByAsset(object asset)
        {
            if (asset is not Object unityObject)
            {
                return false;
            }

            int instanceId = ResourceUnityObjectId.Get(unityObject);
            if (!_assetRecordHeadByUnityObjectId.TryGetValue(instanceId, out int current))
            {
                return false;
            }

            int matchCount = 0;
            int matchedAssetId = -1;
            while (current >= 0)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.NextByUnityObject;
                if (slot.AssetInstanceId == instanceId && slot.LegacyDirectRefCount > 0 && slot.State != ResourceAssetState.Released)
                {
                    matchCount++;
                    if (matchedAssetId < 0)
                    {
                        matchedAssetId = current;
                    }
                }

                current = next;
            }

            if (matchedAssetId < 0)
            {
                return false;
            }

            if (matchCount > 1)
            {
                Log.Warning(ZString.Format("UnloadAsset(object) is ambiguous. Asset instance id {0} maps to {1} legacy direct records. No Resource record was released; use ResourceLeaseHandle for explicit ownership.", instanceId, matchCount));
                return false;
            }

            ref AssetSlot matched = ref GetAssetSlotRef(matchedAssetId);
            matched.LegacyDirectRefCount--;
            UpdateAssetStateAndIdleQueue(matchedAssetId, ref matched);
            return true;
        }

        private static bool HasNoResourceRefs(ref AssetSlot slot)
        {
            return slot.DirectRefCount == 0 &&
                   slot.LegacyDirectRefCount == 0 &&
                   slot.BindingRefCount == 0 &&
                   slot.KeepAliveRefCount == 0;
        }

        private void ReleaseAssetStorage(int assetId, uint generation)
        {
            if (!IsValidAssetId(assetId))
            {
                return;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.Generation != generation || slot.State == ResourceAssetState.Released)
            {
                return;
            }

            if (!HasNoResourceRefs(ref slot) || slot.IdleReleaseRequested == 0)
            {
                UpdateAssetStateAndIdleQueue(assetId, ref slot);
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            RemoveUnusedAssetCandidate(assetId, ref slot);
            DisposeAssetSlotHandle(ref slot);
            UnlinkAssetByUnityObject(assetId, ref slot);
            ulong key = slot.Key;
            _assetRecordsByKey.Remove(key);
            ReleaseResourceKey(key);
            if (slot.LoadKeyId > 0)
            {
                _assetRecordByLoadKeyId.Remove(slot.LoadKeyId);
            }

            ClearAssetSlot(ref slot, preserveGeneration: true);
            FreeAssetSlot(assetId);
        }

        private int AllocateLoadKeyId()
        {
            int id = _loadKeyNextId++;
            if (_loadKeyNextId <= 0)
            {
                _loadKeyNextId = 1;
            }

            return id;
        }

        private string NormalizePackageName(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName;
        }

        private static Type NormalizeAssetType(Type assetType, ResourceAssetKind assetKind)
        {
            if (assetKind == ResourceAssetKind.Sprite)
            {
                return typeof(Sprite);
            }

            if (assetKind == ResourceAssetKind.Material)
            {
                return typeof(Material);
            }

            if (assetKind == ResourceAssetKind.Prefab)
            {
                return typeof(GameObject);
            }

            if (assetKind == ResourceAssetKind.SubAssets)
            {
                return typeof(Sprite);
            }

            return assetType ?? typeof(Object);
        }

        private static ResourceAssetKind NormalizeAssetKind(Type assetType, ResourceAssetKind assetKind)
        {
            return assetKind == ResourceAssetKind.Unknown ? InferAssetKind(assetType) : assetKind;
        }

        private static ResourceAssetKind InferAssetKind(Type assetType)
        {
            if (assetType == typeof(Sprite))
            {
                return ResourceAssetKind.Sprite;
            }

            if (assetType == typeof(Material))
            {
                return ResourceAssetKind.Material;
            }

            if (assetType == typeof(GameObject))
            {
                return ResourceAssetKind.Prefab;
            }

            return ResourceAssetKind.Asset;
        }

        private void UpdateAssetState(ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                slot.State = ResourceAssetState.Released;
                return;
            }

            if (slot.DirectRefCount + slot.LegacyDirectRefCount + slot.BindingRefCount > 0)
            {
                slot.State = ResourceAssetState.Active;
                return;
            }

            slot.State = slot.KeepAliveRefCount > 0 ? ResourceAssetState.KeepAlive : ResourceAssetState.Idle;
        }

        private void UpdateAssetStateAndIdleQueue(int assetId, ref AssetSlot slot)
        {
            UpdateAssetState(ref slot);
            if (slot.State == ResourceAssetState.Idle)
            {
                slot.IdleReleaseRequested = 0;
                AddUnusedAssetCandidate(assetId, ref slot);
                EnterIdle(assetId, ref slot);
            }
            else if (slot.ExpireQueueKind == 2)
            {
                RemoveFromIdleBucket(assetId, ref slot);
                slot.IdleReleaseRequested = 0;
                RemoveUnusedAssetCandidate(assetId, ref slot);
            }
            else
            {
                RemoveUnusedAssetCandidate(assetId, ref slot);
            }
        }

        private static bool IsSlotHandleValid(ref AssetSlot slot)
        {
            return slot.HandleKind == ResourceHandleKind.SubAssetsHandle
                ? IsSubAssetsHandleValid(slot.SubAssetsHandle)
                : slot.AssetHandle is { IsValid: true };
        }

        private static bool CanEnterKeepAlive(ref AssetSlot slot)
        {
            return slot.DirectRefCount == 0 &&
                   slot.LegacyDirectRefCount == 0 &&
                   slot.BindingRefCount == 0 &&
                   IsSlotHandleValid(ref slot);
        }

        private void EnterKeepAlive(int assetId, ref AssetSlot slot)
        {
            RemoveFromExpiryQueue(assetId, ref slot);
            int expireTick = ToKeepAliveTick(Time.unscaledTime) + KeepAliveSeconds;
            if (slot.KeepAliveRefCount == 0)
            {
                slot.KeepAliveRefCount = 1;
            }

            slot.KeepAliveExpireTick = expireTick;
            if (slot.ExpireQueueKind == 1)
            {
                RemoveFromKeepAliveBucket(assetId, ref slot);
            }

            int bucket = expireTick & (KeepAliveBucketCount - 1);
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = _keepAliveBuckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            _keepAliveBuckets[bucket] = assetId;
            slot.ExpireQueueKind = 1;
        }

        private void EnterIdle(int assetId, ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                return;
            }

            int expireTick = ToKeepAliveTick(Time.unscaledTime) + Mathf.Max(0, Mathf.CeilToInt(IdleAssetExpireTime));
            if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick == expireTick)
            {
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            slot.IdleExpireTick = expireTick;
            int bucket = expireTick & (IdleBucketCount - 1);
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = _idleBuckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            _idleBuckets[bucket] = assetId;
            slot.ExpireQueueKind = 2;
        }

        private void RemoveFromExpiryQueue(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind == 1)
            {
                RemoveFromKeepAliveBucket(assetId, ref slot);
            }
            else if (slot.ExpireQueueKind == 2)
            {
                RemoveFromIdleBucket(assetId, ref slot);
            }
        }

        private void RemoveFromKeepAliveBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 1 || _keepAliveBuckets == null)
            {
                return;
            }

            int bucket = slot.KeepAliveExpireTick & (KeepAliveBucketCount - 1);
            int prev = slot.ExpireQueuePrev;
            int next = slot.ExpireQueueNext;
            if (prev >= 0)
            {
                ref AssetSlot prevSlot = ref GetAssetSlotRef(prev);
                prevSlot.ExpireQueueNext = next;
            }
            else if (_keepAliveBuckets[bucket] == assetId)
            {
                _keepAliveBuckets[bucket] = next;
            }

            if (next >= 0)
            {
                ref AssetSlot nextSlot = ref GetAssetSlotRef(next);
                nextSlot.ExpireQueuePrev = prev;
            }

            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueKind = 0;
        }

        private void RemoveFromIdleBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 2 || _idleBuckets == null)
            {
                return;
            }

            int bucket = slot.IdleExpireTick & (IdleBucketCount - 1);
            int prev = slot.ExpireQueuePrev;
            int next = slot.ExpireQueueNext;
            if (prev >= 0)
            {
                ref AssetSlot prevSlot = ref GetAssetSlotRef(prev);
                prevSlot.ExpireQueueNext = next;
            }
            else if (_idleBuckets[bucket] == assetId)
            {
                _idleBuckets[bucket] = next;
            }

            if (next >= 0)
            {
                ref AssetSlot nextSlot = ref GetAssetSlotRef(next);
                nextSlot.ExpireQueuePrev = prev;
            }

            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueKind = 0;
        }

        private void AddUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            if (slot.UnusedCandidateIndex >= 0)
            {
                return;
            }

            if (_unusedAssetCandidates == null)
            {
                _unusedAssetCandidates = new int[Math.Max(16, _assetRecordCapacity)];
            }
            else if (_unusedAssetCandidateCount >= _unusedAssetCandidates.Length)
            {
                Array.Resize(ref _unusedAssetCandidates, _unusedAssetCandidates.Length << 1);
            }

            slot.UnusedCandidateIndex = _unusedAssetCandidateCount;
            _unusedAssetCandidates[_unusedAssetCandidateCount++] = assetId;
        }

        private void RemoveUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            int index = slot.UnusedCandidateIndex;
            if (index < 0 || index >= _unusedAssetCandidateCount)
            {
                slot.UnusedCandidateIndex = -1;
                return;
            }

            if (_unusedAssetCandidates[index] != assetId)
            {
                for (int i = 0; i < _unusedAssetCandidateCount; i++)
                {
                    if (_unusedAssetCandidates[i] == assetId)
                    {
                        RemoveUnusedAssetCandidateAt(i);
                        return;
                    }
                }

                slot.UnusedCandidateIndex = -1;
                return;
            }

            RemoveUnusedAssetCandidateAt(index);
        }

        private void RemoveUnusedAssetCandidateAt(int index)
        {
            if (index < 0 || index >= _unusedAssetCandidateCount)
            {
                return;
            }

            int removedAssetId = _unusedAssetCandidates[index];
            int lastIndex = --_unusedAssetCandidateCount;
            int movedAssetId = _unusedAssetCandidates[lastIndex];
            _unusedAssetCandidates[lastIndex] = 0;
            if (index != lastIndex)
            {
                _unusedAssetCandidates[index] = movedAssetId;
                if (IsValidAssetId(movedAssetId))
                {
                    ref AssetSlot movedSlot = ref GetAssetSlotRef(movedAssetId);
                    movedSlot.UnusedCandidateIndex = index;
                }
            }

            if (IsValidAssetId(removedAssetId))
            {
                ref AssetSlot removedSlot = ref GetAssetSlotRef(removedAssetId);
                removedSlot.UnusedCandidateIndex = -1;
            }
        }

        private static int ToKeepAliveTick(float unscaledTime)
        {
            return Mathf.Max(0, Mathf.FloorToInt(unscaledTime));
        }

        private void DisposeAssetSlotHandle(ref AssetSlot slot)
        {
            AssetHandle handle = slot.AssetHandle;
            if (handle is { IsValid: true })
            {
                handle.Dispose();
            }

            DisposeSubAssetsHandle(slot.SubAssetsHandle);
            slot.AssetHandle = null;
            slot.State = ResourceAssetState.Released;
        }

        private static void DisposeSubAssetsHandle(SubAssetsHandle handle)
        {
            if (IsSubAssetsHandleValid(handle))
            {
                handle.Dispose();
            }
        }

        private static bool IsSubAssetsHandleValid(SubAssetsHandle handle)
        {
            return handle != null && handle.IsValid;
        }

        private void ClearAssetSlot(ref AssetSlot slot, bool preserveGeneration)
        {
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = preserveGeneration ? generation : 0;
            slot.NextByUnityObject = -1;
            slot.NextFree = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = ResourceAssetState.Released;
        }

        private void LinkAssetByUnityObject(int assetId, ref AssetSlot slot)
        {
            if (slot.AssetInstanceId == 0)
            {
                return;
            }

            if (_assetRecordHeadByUnityObjectId.TryGetValue(slot.AssetInstanceId, out int head))
            {
                slot.NextByUnityObject = head;
            }
            else
            {
                slot.NextByUnityObject = -1;
            }

            _assetRecordHeadByUnityObjectId.Set(slot.AssetInstanceId, assetId);
        }

        private void UnlinkAssetByUnityObject(int assetId, ref AssetSlot slot)
        {
            int instanceId = slot.AssetInstanceId;
            if (instanceId == 0 || !_assetRecordHeadByUnityObjectId.TryGetValue(instanceId, out int current))
            {
                return;
            }

            int previous = -1;
            while (current >= 0)
            {
                ref AssetSlot currentSlot = ref GetAssetSlotRef(current);
                int next = currentSlot.NextByUnityObject;
                if (current == assetId)
                {
                    if (previous >= 0)
                    {
                        ref AssetSlot previousSlot = ref GetAssetSlotRef(previous);
                        previousSlot.NextByUnityObject = next;
                    }
                    else if (next >= 0)
                    {
                        _assetRecordHeadByUnityObjectId.Set(instanceId, next);
                    }
                    else
                    {
                        _assetRecordHeadByUnityObjectId.Remove(instanceId);
                    }

                    currentSlot.NextByUnityObject = -1;
                    return;
                }

                previous = current;
                current = next;
            }
        }

        private int AllocateAssetSlot()
        {
            int index;
            if (_assetSlotFreeHead >= 0)
            {
                index = _assetSlotFreeHead;
                ref AssetSlot freeSlot = ref GetAssetSlotRef(index);
                _assetSlotFreeHead = freeSlot.NextFree;
            }
            else
            {
                index = _assetSlotNextIndex++;
                EnsureAssetSlotPage(index);
            }

            ref AssetSlot slot = ref GetAssetSlotRef(index);
            uint generation = slot.Generation + 1;
            if (generation == 0)
            {
                generation = 1;
            }

            slot = default;
            slot.Generation = generation;
            slot.NextByUnityObject = -1;
            slot.NextFree = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = ResourceAssetState.Released;
            return index;
        }

        private void FreeAssetSlot(int index)
        {
            ref AssetSlot slot = ref GetAssetSlotRef(index);
            slot.NextFree = _assetSlotFreeHead;
            _assetSlotFreeHead = index;
        }

        private int AllocateLeaseSlot()
        {
            int index;
            if (_leaseSlotFreeHead >= 0)
            {
                index = _leaseSlotFreeHead;
                ref LeaseSlot freeSlot = ref GetLeaseSlotRef(index);
                _leaseSlotFreeHead = freeSlot.NextFree;
            }
            else
            {
                index = _leaseSlotNextIndex++;
                EnsureLeaseSlotPage(index);
            }

            ref LeaseSlot slot = ref GetLeaseSlotRef(index);
            uint generation = slot.Generation + 1;
            if (generation == 0)
            {
                generation = 1;
            }

            slot = default;
            slot.Generation = generation;
            slot.NextFree = -1;
            slot.State = ResourceLeaseState.Free;
            return index;
        }

        private void FreeLeaseSlot(int index)
        {
            ref LeaseSlot slot = ref GetLeaseSlotRef(index);
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = generation;
            slot.State = ResourceLeaseState.Released;
            slot.NextFree = _leaseSlotFreeHead;
            _leaseSlotFreeHead = index;
        }

        private bool TryGetLeaseSlotIndex(ResourceLeaseHandle handle, out int leaseIndex)
        {
            leaseIndex = handle.Index;
            if (!handle.IsValid || !IsValidLeaseId(handle.Index))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(handle.Index);
            return lease.Generation == handle.Generation && lease.State == ResourceLeaseState.Active;
        }

        private bool IsValidActiveAssetId(int assetId)
        {
            if (!IsValidAssetId(assetId))
            {
                return false;
            }

            return GetAssetSlotRef(assetId).Generation != 0;
        }

        private bool IsValidAssetId(int assetId)
        {
            return assetId >= 0 && assetId < _assetSlotNextIndex && _assetSlotPages != null;
        }

        private bool IsValidLeaseId(int leaseId)
        {
            return leaseId >= 0 && leaseId < _leaseSlotNextIndex && _leaseSlotPages != null;
        }

        private ref AssetSlot GetAssetSlotRef(int index)
        {
            return ref _assetSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private ref LeaseSlot GetLeaseSlotRef(int index)
        {
            return ref _leaseSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private void EnsureAssetSlotPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
            if (_assetSlotPages == null)
            {
                _assetSlotPages = new AssetSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _assetSlotPages.Length)
            {
                Array.Resize(ref _assetSlotPages, Math.Max(pageIndex + 1, _assetSlotPages.Length << 1));
            }

            if (_assetSlotPages[pageIndex] == null)
            {
                _assetSlotPages[pageIndex] = new AssetSlot[RecordPageSize];
            }
        }

        private void EnsureLeaseSlotPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
            if (_leaseSlotPages == null)
            {
                _leaseSlotPages = new LeaseSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _leaseSlotPages.Length)
            {
                Array.Resize(ref _leaseSlotPages, Math.Max(pageIndex + 1, _leaseSlotPages.Length << 1));
            }

            if (_leaseSlotPages[pageIndex] == null)
            {
                _leaseSlotPages[pageIndex] = new LeaseSlot[RecordPageSize];
            }
        }
    }
}
