using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal sealed class ResourceBindingService : IResourceBindingService
    {
        private const int PageBits = 8;
        private const int PageSize = 1 << PageBits;
        private const int PageMask = PageSize - 1;

        private readonly ResourceService _resourceService;
        private OwnerSlot[][] _ownerPages;
        private BindingSlot[][] _bindingPages;
        private RegisteredTargetSlot[][] _registeredTargetPages;
        private int _ownerNextIndex;
        private int _bindingNextIndex;
        private int _registeredTargetNextIndex;
        private int _ownerFreeHead = -1;
        private int _bindingFreeHead = -1;
        private int _registeredTargetFreeHead = -1;
        private readonly ResourceIndexMap<int, int> _ownerIndexByGameObjectId = new ResourceIndexMap<int, int>();
        private readonly ResourceIndexMap<OwnerSlotKey, int> _bindingIndexByOwnerSlot = new ResourceIndexMap<OwnerSlotKey, int>();
        private readonly ResourceIndexMap<int, TargetOwnerEntry> _ownerByTargetComponentId = new ResourceIndexMap<int, TargetOwnerEntry>();
        private bool _isShutdown;

        private struct OwnerSlot
        {
            public int OwnerId;
            public int GameObjectId;
            public uint Generation;
            public int BindingHead;
            public int RegisteredTargetHead;
            public int BindingCount;
            public int RegisteredTargetCount;
            public ResourceOwner Owner;
            public byte State;
            public int NextFree;
        }

        private struct BindingSlot
        {
            public long SlotKey;
            public int OwnerId;
            public int TargetGameObjectId;
            public int TargetComponentId;
            public uint OwnerGeneration;
            public Object Target;
            public Object AppliedAsset;
            public Object RuntimeObject;
            public int AssetId;
            public int ViewKeyId;
            public ResourceLeaseHandle Lease;
            public uint Version;
            public int NextByOwner;
            public ushort SubIndex;
            public ResourceBindingSlotType SlotType;
            public byte Flags;
            public int NextFree;
        }

        private struct RegisteredTargetSlot
        {
            public int TargetComponentId;
            public int OwnerId;
            public uint OwnerGeneration;
            public int NextByOwner;
            public byte State;
            public int NextFree;
        }

        private readonly struct OwnerSlotKey : IEquatable<OwnerSlotKey>
        {
            public readonly int OwnerId;
            public readonly long SlotKey;

            public OwnerSlotKey(int ownerId, long slotKey)
            {
                OwnerId = ownerId;
                SlotKey = slotKey;
            }

            public bool Equals(OwnerSlotKey other)
            {
                return OwnerId == other.OwnerId && SlotKey == other.SlotKey;
            }

            public override bool Equals(object obj)
            {
                return obj is OwnerSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (OwnerId * 397) ^ SlotKey.GetHashCode();
                }
            }
        }

        private readonly struct TargetOwnerEntry
        {
            public readonly int OwnerId;
            public readonly uint OwnerGeneration;

            public TargetOwnerEntry(int ownerId, uint ownerGeneration)
            {
                OwnerId = ownerId;
                OwnerGeneration = ownerGeneration;
            }
        }

        public ResourceBindingService(ResourceService resourceService)
        {
            _resourceService = resourceService;
        }

        public void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity)
        {
            if (ownerCapacity > 0)
            {
                EnsureOwnerPage(ownerCapacity - 1);
                _ownerIndexByGameObjectId.EnsureCapacity(ownerCapacity);
            }

            if (bindingCapacity > 0)
            {
                EnsureBindingPage(bindingCapacity - 1);
                _bindingIndexByOwnerSlot.EnsureCapacity(bindingCapacity);
            }

            if (registeredTargetCapacity > 0)
            {
                EnsureRegisteredTargetPage(registeredTargetCapacity - 1);
                _ownerByTargetComponentId.EnsureCapacity(registeredTargetCapacity);
            }
        }

        public void Shutdown()
        {
            _isShutdown = true;
            for (int index = 0; index < _ownerNextIndex; index++)
            {
                if (!IsValidOwnerIndex(index))
                {
                    continue;
                }

                OwnerSlot owner = GetOwnerSlotRef(index);
                if (owner.State != 1)
                {
                    continue;
                }

                ReleaseOwner(owner.OwnerId, owner.Generation);
            }

            _ownerPages = null;
            _bindingPages = null;
            _registeredTargetPages = null;
            _ownerNextIndex = 0;
            _bindingNextIndex = 0;
            _registeredTargetNextIndex = 0;
            _ownerFreeHead = -1;
            _bindingFreeHead = -1;
            _registeredTargetFreeHead = -1;
            _ownerIndexByGameObjectId.Clear();
            _bindingIndexByOwnerSlot.Clear();
            _ownerByTargetComponentId.Clear();
            _isShutdown = false;
        }

        public ResourceBindStatus RegisterOwner(ResourceOwner owner)
        {
            if (_isShutdown)
            {
                return ResourceBindStatus.ServiceShutdown;
            }

            if (owner == null || owner.gameObject == null)
            {
                return ResourceBindStatus.MissingOwner;
            }

            int gameObjectId = ResourceUnityObjectId.Get(owner.gameObject);
            if (_ownerIndexByGameObjectId.TryGetValue(gameObjectId, out int existingIndex))
            {
                ref OwnerSlot existing = ref GetOwnerSlotRef(existingIndex);
                if (existing.State == 1)
                {
                    owner.SetRegistered(existing.OwnerId, existing.GameObjectId, existing.Generation);
                    return ResourceBindStatus.Success;
                }
            }

            int ownerIndex = AllocateOwnerSlot();
            ref OwnerSlot slot = ref GetOwnerSlotRef(ownerIndex);
            slot.OwnerId = ownerIndex + 1;
            slot.GameObjectId = gameObjectId;
            slot.BindingHead = -1;
            slot.RegisteredTargetHead = -1;
            slot.Owner = owner;
            slot.State = 1;
            _ownerIndexByGameObjectId.Set(gameObjectId, ownerIndex);
            owner.SetRegistered(slot.OwnerId, slot.GameObjectId, slot.Generation);
            return ResourceBindStatus.Success;
        }

        public ResourceBindStatus ReleaseOwner(ResourceOwner owner)
        {
            if (owner == null || !owner.IsRegistered)
            {
                return ResourceBindStatus.MissingOwner;
            }

            return ReleaseOwner(owner.OwnerId, owner.Generation);
        }

        public ResourceBindStatus ReleaseOwner(int ownerId, uint generation)
        {
            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return ResourceBindStatus.MissingOwner;
            }

            ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
            if (owner.State != 1 || owner.Generation != generation)
            {
                return ResourceBindStatus.StaleOwner;
            }

            int bindingIndex = owner.BindingHead;
            while (bindingIndex >= 0)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
                int next = binding.NextByOwner;
                ClearAndReleaseBinding(ref binding);
                FreeBindingSlot(bindingIndex);
                bindingIndex = next;
            }
            owner.BindingHead = -1;
            owner.BindingCount = 0;

            int targetIndex = owner.RegisteredTargetHead;
            while (targetIndex >= 0)
            {
                ref RegisteredTargetSlot targetSlot = ref GetRegisteredTargetSlotRef(targetIndex);
                int next = targetSlot.NextByOwner;
                if (_ownerByTargetComponentId.TryGetValue(targetSlot.TargetComponentId, out TargetOwnerEntry entry) &&
                    entry.OwnerId == owner.OwnerId &&
                    entry.OwnerGeneration == owner.Generation)
                {
                    _ownerByTargetComponentId.Remove(targetSlot.TargetComponentId);
                }

                FreeRegisteredTargetSlot(targetIndex);
                targetIndex = next;
            }
            owner.RegisteredTargetHead = -1;
            owner.RegisteredTargetCount = 0;

            _ownerIndexByGameObjectId.Remove(owner.GameObjectId);
            if (owner.Owner != null && owner.Owner.IsRegistered && owner.Owner.Generation == generation)
            {
                owner.Owner.ClearRegistered();
            }

            FreeOwnerSlot(ownerIndex);
            return ResourceBindStatus.Success;
        }

        public ResourceBindStatus RegisterTarget(ResourceOwner owner, Component target)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            int targetComponentId = ResourceUnityObjectId.Get(target);
            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            if (_ownerByTargetComponentId.TryGetValue(targetComponentId, out TargetOwnerEntry existingEntry))
            {
                if (existingEntry.OwnerId == ownerSlot.OwnerId &&
                    existingEntry.OwnerGeneration == ownerSlot.Generation)
                {
                    return ResourceBindStatus.Success;
                }

                RemoveRegisteredTargetSlot(existingEntry.OwnerId, existingEntry.OwnerGeneration, targetComponentId);
            }

            _ownerByTargetComponentId.Set(targetComponentId, new TargetOwnerEntry(ownerSlot.OwnerId, ownerSlot.Generation));

            int registeredIndex = AllocateRegisteredTargetSlot();
            ref RegisteredTargetSlot registeredTarget = ref GetRegisteredTargetSlotRef(registeredIndex);
            registeredTarget.TargetComponentId = targetComponentId;
            registeredTarget.OwnerId = ownerSlot.OwnerId;
            registeredTarget.OwnerGeneration = ownerSlot.Generation;
            registeredTarget.NextByOwner = ownerSlot.RegisteredTargetHead;
            registeredTarget.State = 1;
            ownerSlot.RegisteredTargetHead = registeredIndex;
            ownerSlot.RegisteredTargetCount++;
            return ResourceBindStatus.Success;
        }

        public ResourceBindStatus UnregisterTarget(ResourceOwner owner, Component target)
        {
            if (owner == null || !owner.IsRegistered)
            {
                return ResourceBindStatus.MissingOwner;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            int targetComponentId = ResourceUnityObjectId.Get(target);
            if (_ownerByTargetComponentId.TryGetValue(targetComponentId, out TargetOwnerEntry entry) &&
                entry.OwnerId == owner.OwnerId &&
                entry.OwnerGeneration == owner.Generation)
            {
                _ownerByTargetComponentId.Remove(targetComponentId);
                RemoveRegisteredTargetSlot(entry.OwnerId, entry.OwnerGeneration, targetComponentId);
            }

            return ResourceBindStatus.Success;
        }

        public ResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            if (image == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ResourceBindStatus status = BindSpriteSource(owner, image, key, ResourceBindingSlotType.ImageSprite, options);
            if (status == ResourceBindStatus.Success && (options & ResourceBindingOptions.SetNativeSize) != 0)
            {
                image.SetNativeSize();
            }

            return status;
        }

        public ResourceBindStatus BindSprite(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            if (spriteRenderer == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return BindSpriteSource(owner, spriteRenderer, key, ResourceBindingSlotType.SpriteRendererSprite, options);
        }

        public async UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, Image image, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ResourceBindStatus status = await BindSubSpriteSourceAsync(owner, image, atlasKey, spriteName, ResourceBindingSlotType.SubSprite, options, cancellationToken);
            if (status == ResourceBindStatus.Success && (options & ResourceBindingOptions.SetNativeSize) != 0)
            {
                image.SetNativeSize();
            }

            return status;
        }

        public async UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
        {
            if (spriteRenderer == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return await BindSubSpriteSourceAsync(owner, spriteRenderer, atlasKey, spriteName, ResourceBindingSlotType.SpriteRendererSprite, options, cancellationToken);
        }

        public ResourceBindStatus BindImageMaterial(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            if (image == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return BindMaterialSource(owner, image, key, ResourceBindingSlotType.ImageMaterial, createRuntimeInstance: false, options);
        }

        public async UniTask<ResourceBindStatus> BindImageMaterialAsync(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return await BindMaterialSourceAsync(owner, image, key, ResourceBindingSlotType.ImageMaterial, false, options, cancellationToken);
        }

        public ResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            if (renderer == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return BindMaterialSource(owner, renderer, key, ResourceBindingSlotType.RendererSharedMaterial, createRuntimeInstance: false, options);
        }

        public async UniTask<ResourceBindStatus> BindSharedMaterialAsync(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
        {
            if (renderer == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return await BindMaterialSourceAsync(owner, renderer, key, ResourceBindingSlotType.RendererSharedMaterial, false, options, cancellationToken);
        }

        public ResourceBindStatus BindMaterialInstance(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
        {
            if (renderer == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return BindMaterialSource(owner, renderer, key, ResourceBindingSlotType.RendererMaterialInstance, createRuntimeInstance: true, options);
        }

        public async UniTask<ResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
        {
            if (renderer == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            return await BindMaterialSourceAsync(owner, renderer, key, ResourceBindingSlotType.RendererMaterialInstance, true, options, cancellationToken);
        }

        public int GetOwnerInfos(ResourceOwnerInfo[] results, int startIndex, int maxCount)
        {
            int total = _ownerNextIndex;
            if (results == null || maxCount <= 0 || startIndex >= total)
            {
                return total;
            }

            int writeLimit = Math.Min(Math.Min(maxCount, results.Length), total - Math.Max(0, startIndex));
            int written = 0;
            int index = Math.Max(0, startIndex);
            while (index < total && written < writeLimit)
            {
                ref OwnerSlot owner = ref GetOwnerSlotRef(index);
                ref ResourceOwnerInfo info = ref results[written];
                info.Active = owner.State == 1;
                info.OwnerIndex = index;
                info.OwnerId = owner.OwnerId;
                info.GameObjectId = owner.GameObjectId;
                info.Generation = owner.Generation;
                info.BindingCount = owner.BindingCount;
                info.RegisteredTargetCount = owner.RegisteredTargetCount;
                info.HasOwnerObject = owner.Owner != null;
#if UNITY_EDITOR
                info.OwnerObject = owner.Owner != null ? owner.Owner.gameObject : null;
#endif
                written++;
                index++;
            }

            return total;
        }

        public int GetBindingInfos(ResourceBindingInfo[] results, int startIndex, int maxCount)
        {
            int total = _bindingNextIndex;
            if (results == null || maxCount <= 0 || startIndex >= total)
            {
                return total;
            }

            int writeLimit = Math.Min(Math.Min(maxCount, results.Length), total - Math.Max(0, startIndex));
            int written = 0;
            int index = Math.Max(0, startIndex);
            while (index < total && written < writeLimit)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(index);
                ref ResourceBindingInfo info = ref results[written];
                info.Active = binding.OwnerId > 0 && binding.SlotType != ResourceBindingSlotType.None;
                info.BindingIndex = index;
                info.OwnerId = binding.OwnerId;
                info.OwnerGeneration = binding.OwnerGeneration;
                info.TargetGameObjectId = binding.TargetGameObjectId;
                info.TargetComponentId = binding.TargetComponentId;
                info.SlotKey = binding.SlotKey;
                info.AssetId = binding.AssetId;
                info.ViewKeyId = binding.ViewKeyId;
                info.Lease = binding.Lease;
                info.Version = binding.Version;
                info.SubIndex = binding.SubIndex;
                info.SlotType = binding.SlotType;
                info.HasAppliedAsset = binding.AppliedAsset != null;
                info.HasRuntimeObject = binding.RuntimeObject != null;
#if UNITY_EDITOR
                info.TargetObject = binding.Target;
#endif
                written++;
                index++;
            }

            return total;
        }

        internal ResourceBindStatus RegisterPrefabSource(ResourceOwner owner, ResourceLeaseHandle lease, GameObject prefabSource)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (!lease.IsValid || prefabSource == null)
            {
                return ResourceBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            long slotKey = BuildSlotKey(ownerSlot.GameObjectId, ResourceBindingSlotType.PrefabSource, 0);
            OwnerSlotKey key = new OwnerSlotKey(ownerSlot.OwnerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(key, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(key, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            ResourceLeaseHandle oldLease = binding.Lease;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = ownerSlot.GameObjectId;
            binding.TargetComponentId = ownerSlot.GameObjectId;
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = owner;
            binding.AppliedAsset = prefabSource;
            binding.RuntimeObject = null;
            binding.AssetId = _resourceService.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.ViewKeyId = 0;
            binding.Lease = lease;
            binding.SlotType = ResourceBindingSlotType.PrefabSource;
            binding.Flags = (byte)ResourceBindingOptions.KeepAliveOnRelease;
            _resourceService.SetLeaseOptions(lease, ResourceLeaseOptions.KeepAliveOnRelease);
            binding.Version++;
            if (oldLease.IsValid)
            {
                _resourceService.Release(oldLease);
            }

            return ResourceBindStatus.Success;
        }

        private ResourceBindStatus BindMaterialSource(ResourceOwner owner, Component target, ResourceKey key, ResourceBindingSlotType slotType, bool createRuntimeInstance, ResourceBindingOptions options)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ResourceKey materialKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Material), ResourceAssetKind.Material)
                : key;
            ResourceLeaseHandle newLease = _resourceService.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return ResourceBindStatus.LoadFailed;
            }

            if (!_resourceService.TryGetLeaseAsset(newLease, out Object asset) || asset is not Material sourceMaterial)
            {
                _resourceService.Release(newLease);
                return ResourceBindStatus.LoadFailed;
            }

            Material appliedMaterial = sourceMaterial;
            Material runtimeMaterial = null;
            if (createRuntimeInstance)
            {
                runtimeMaterial = Object.Instantiate(sourceMaterial);
                appliedMaterial = runtimeMaterial;
            }

            if (!ApplyMaterial(target, appliedMaterial, slotType))
            {
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _resourceService.Release(newLease);
                return ResourceBindStatus.ApplyFailed;
            }

            ResourceBindStatus registerStatus = RegisterMaterialSource(owner, target, newLease, appliedMaterial, runtimeMaterial, slotType, options, 0);
            if (registerStatus != ResourceBindStatus.Success)
            {
                ClearMaterialSlot(target, appliedMaterial, runtimeMaterial, slotType);
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _resourceService.Release(newLease);
            }

            return registerStatus;
        }

        private async UniTask<ResourceBindStatus> BindMaterialSourceAsync(ResourceOwner owner, Component target, ResourceKey key, ResourceBindingSlotType slotType, bool createRuntimeInstance, ResourceBindingOptions options, CancellationToken cancellationToken)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ResourceBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, target, slotType, out int ownerId, out uint ownerGeneration,
                out int targetComponentId, out int targetGameObjectId, out long slotKey, out uint requestVersion);
            if (reserveStatus != ResourceBindStatus.Success)
            {
                return reserveStatus;
            }

            ResourceKey materialKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Material), ResourceAssetKind.Material)
                : key;
            ResourceLeaseHandle newLease = await _resourceService.AcquireBindingAsync(materialKey, cancellationToken);
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId, slotKey, requestVersion, target))
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.StaleOwner;
            }

            if (!_resourceService.TryGetLeaseAsset(newLease, out Object asset) || asset is not Material sourceMaterial)
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId, slotKey, requestVersion, target))
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested ? ResourceBindStatus.LoadFailed : ResourceBindStatus.StaleOwner;
            }

            Material appliedMaterial = sourceMaterial;
            Material runtimeMaterial = null;
            if (createRuntimeInstance)
            {
                runtimeMaterial = Object.Instantiate(sourceMaterial);
                appliedMaterial = runtimeMaterial;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId, slotKey, requestVersion, target))
            {
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(target, appliedMaterial, slotType))
            {
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.ApplyFailed;
            }

            ResourceBindStatus registerStatus = RegisterMaterialSource(owner, target, newLease, appliedMaterial, runtimeMaterial, slotType, options, requestVersion);
            if (registerStatus != ResourceBindStatus.Success)
            {
                ClearMaterialSlot(target, appliedMaterial, runtimeMaterial, slotType);
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _resourceService.Release(newLease);
            }

            return registerStatus;
        }

        internal ResourceBindStatus RegisterMaterialSource(ResourceOwner owner, Component target, ResourceLeaseHandle lease, Material appliedMaterial, Material runtimeMaterial, ResourceBindingSlotType slotType)
        {
            return RegisterMaterialSource(owner, target, lease, appliedMaterial, runtimeMaterial, slotType, ResourceBindingOptions.None, 0);
        }

        private ResourceBindStatus RegisterMaterialSource(ResourceOwner owner, Component target, ResourceLeaseHandle lease, Material appliedMaterial, Material runtimeMaterial, ResourceBindingSlotType slotType, ResourceBindingOptions options, uint reservedVersion)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            if (!lease.IsValid || appliedMaterial == null)
            {
                return ResourceBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            long slotKey = BuildSlotKey(ResourceUnityObjectId.Get(target), slotType, 0);
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerSlot.OwnerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(ownerSlotKey, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            ResourceLeaseHandle oldLease = binding.Lease;
            Object oldRuntimeObject = binding.RuntimeObject;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = ResourceUnityObjectId.Get(target.gameObject);
            binding.TargetComponentId = ResourceUnityObjectId.Get(target);
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = target;
            binding.AppliedAsset = appliedMaterial;
            binding.RuntimeObject = runtimeMaterial;
            binding.AssetId = _resourceService.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.ViewKeyId = 0;
            binding.Lease = lease;
            binding.SlotType = slotType;
            binding.Flags = (byte)options;
            _resourceService.SetLeaseOptions(lease, ToLeaseOptions(options));
            if (reservedVersion != 0 && binding.Version == reservedVersion)
            {
                binding.Version = reservedVersion;
            }
            else
            {
                binding.Version++;
            }

            if (oldRuntimeObject != null)
            {
                Object.Destroy(oldRuntimeObject);
            }

            if (oldLease.IsValid)
            {
                _resourceService.Release(oldLease);
            }

            return ResourceBindStatus.Success;
        }

        private ResourceBindStatus BindSpriteSource(ResourceOwner owner, Component target, ResourceKey key, ResourceBindingSlotType slotType, ResourceBindingOptions options)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ResourceKey spriteKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Sprite), ResourceAssetKind.Sprite)
                : key;
            ResourceLeaseHandle newLease = _resourceService.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return ResourceBindStatus.LoadFailed;
            }

            if (!_resourceService.TryGetLeaseAsset(newLease, out Object asset) || asset is not Sprite sprite)
            {
                _resourceService.Release(newLease);
                return ResourceBindStatus.LoadFailed;
            }

            if (!ApplySprite(target, sprite, slotType))
            {
                _resourceService.Release(newLease);
                return ResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, target, newLease, sprite, slotType, options, 0);
        }

        internal ResourceBindStatus RegisterSpriteSource(ResourceOwner owner, Component target, ResourceLeaseHandle lease, Sprite sprite, ResourceBindingSlotType slotType)
        {
            return RegisterSpriteSource(owner, target, lease, sprite, slotType, ResourceBindingOptions.None, 0);
        }

        private ResourceBindStatus RegisterSpriteSource(ResourceOwner owner, Component target, ResourceLeaseHandle lease, Sprite sprite, ResourceBindingSlotType slotType, ResourceBindingOptions options, uint reservedVersion)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            if (!lease.IsValid || sprite == null)
            {
                return ResourceBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            long slotKey = BuildSlotKey(ResourceUnityObjectId.Get(target), slotType, 0);
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerSlot.OwnerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(ownerSlotKey, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            ResourceLeaseHandle oldLease = binding.Lease;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = ResourceUnityObjectId.Get(target.gameObject);
            binding.TargetComponentId = ResourceUnityObjectId.Get(target);
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = target;
            binding.AppliedAsset = sprite;
            binding.RuntimeObject = null;
            binding.AssetId = _resourceService.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.ViewKeyId = 0;
            binding.Lease = lease;
            binding.SlotType = slotType;
            binding.Flags = (byte)options;
            _resourceService.SetLeaseOptions(lease, ToLeaseOptions(options));
            if (reservedVersion != 0 && binding.Version == reservedVersion)
            {
                binding.Version = reservedVersion;
            }
            else
            {
                binding.Version++;
            }

            if (oldLease.IsValid)
            {
                _resourceService.Release(oldLease);
            }

            return ResourceBindStatus.Success;
        }

        private static bool ApplySprite(Component target, Sprite sprite, ResourceBindingSlotType slotType)
        {
            if (sprite == null)
            {
                return false;
            }

            switch (slotType)
            {
                case ResourceBindingSlotType.ImageSprite:
                case ResourceBindingSlotType.SubSprite:
                    if (target is Image image)
                    {
                        image.sprite = sprite;
                        return true;
                    }
                    break;
                case ResourceBindingSlotType.SpriteRendererSprite:
                    if (target is SpriteRenderer spriteRenderer)
                    {
                        spriteRenderer.sprite = sprite;
                        return true;
                    }
                    break;
            }

            return false;
        }

        private async UniTask<ResourceBindStatus> BindSubSpriteSourceAsync(ResourceOwner owner, Component target, ResourceKey atlasKey, string spriteName, ResourceBindingSlotType slotType, ResourceBindingOptions options, CancellationToken cancellationToken)
        {
            ResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != ResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ResourceBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, target, slotType, out int ownerId, out uint ownerGeneration,
                out int targetComponentId, out int targetGameObjectId, out long slotKey, out uint requestVersion);
            if (reserveStatus != ResourceBindStatus.Success)
            {
                return reserveStatus;
            }

            ResourceLeaseHandle newLease = await _resourceService.AcquireSubAssetsBindingAsync(atlasKey.Location, atlasKey.PackageName, ToLeaseOptions(options), cancellationToken);
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId, slotKey, requestVersion, target))
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.StaleOwner;
            }

            if (!_resourceService.TryGetSubSpriteAsset(newLease, spriteName, out Sprite sprite))
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId, slotKey, requestVersion, target))
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested ? ResourceBindStatus.LoadFailed : ResourceBindStatus.StaleOwner;
            }

            if (!ApplySprite(target, sprite, slotType))
            {
                _resourceService.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return ResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, target, newLease, sprite, slotType, options, requestVersion);
        }

        private ResourceBindStatus ReserveBindingRequest(int ownerIndex, Component target, ResourceBindingSlotType slotType,
            out int ownerId, out uint ownerGeneration, out int targetComponentId, out int targetGameObjectId, out long slotKey, out uint requestVersion)
        {
            ownerId = 0;
            ownerGeneration = 0;
            targetComponentId = 0;
            targetGameObjectId = 0;
            slotKey = 0;
            requestVersion = 0;

            if (target == null || target.gameObject == null)
            {
                return ResourceBindStatus.MissingTarget;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            ownerId = ownerSlot.OwnerId;
            ownerGeneration = ownerSlot.Generation;
            targetComponentId = ResourceUnityObjectId.Get(target);
            targetGameObjectId = ResourceUnityObjectId.Get(target.gameObject);
            slotKey = BuildSlotKey(targetComponentId, slotType, 0);
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(ownerSlotKey, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerId;
            binding.TargetGameObjectId = targetGameObjectId;
            binding.TargetComponentId = targetComponentId;
            binding.OwnerGeneration = ownerGeneration;
            binding.Target = target;
            binding.SlotType = slotType;
            binding.Version++;
            requestVersion = binding.Version;
            return ResourceBindStatus.Success;
        }

        private void CancelReservedBindingRequest(int ownerId, uint ownerGeneration, long slotKey, uint requestVersion)
        {
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                return;
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            if (binding.OwnerGeneration != ownerGeneration ||
                binding.Version != requestVersion ||
                binding.Lease.IsValid ||
                binding.AppliedAsset != null ||
                binding.RuntimeObject != null)
            {
                return;
            }

            int ownerIndex = ownerId - 1;
            if (IsValidOwnerIndex(ownerIndex))
            {
                ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
                if (owner.State == 1 && owner.Generation == ownerGeneration)
                {
                    UnlinkBindingFromOwner(ref owner, bindingIndex);
                }
            }

            _bindingIndexByOwnerSlot.Remove(ownerSlotKey);
            FreeBindingSlot(bindingIndex);
        }

        private void RemoveRegisteredTargetSlot(int ownerId, uint ownerGeneration, int targetComponentId)
        {
            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return;
            }

            ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
            if (owner.State != 1 || owner.Generation != ownerGeneration)
            {
                return;
            }

            int previous = -1;
            int current = owner.RegisteredTargetHead;
            while (current >= 0)
            {
                ref RegisteredTargetSlot target = ref GetRegisteredTargetSlotRef(current);
                int next = target.NextByOwner;
                if (target.TargetComponentId == targetComponentId &&
                    target.OwnerId == ownerId &&
                    target.OwnerGeneration == ownerGeneration)
                {
                    if (previous >= 0)
                    {
                        ref RegisteredTargetSlot previousTarget = ref GetRegisteredTargetSlotRef(previous);
                        previousTarget.NextByOwner = next;
                    }
                    else
                    {
                        owner.RegisteredTargetHead = next;
                    }

                    if (owner.RegisteredTargetCount > 0)
                    {
                        owner.RegisteredTargetCount--;
                    }

                    FreeRegisteredTargetSlot(current);
                    return;
                }

                previous = current;
                current = next;
            }
        }

        private void UnlinkBindingFromOwner(ref OwnerSlot owner, int bindingIndex)
        {
            int previous = -1;
            int current = owner.BindingHead;
            while (current >= 0)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(current);
                int next = binding.NextByOwner;
                if (current == bindingIndex)
                {
                    if (previous >= 0)
                    {
                        ref BindingSlot previousBinding = ref GetBindingSlotRef(previous);
                        previousBinding.NextByOwner = next;
                    }
                    else
                    {
                        owner.BindingHead = next;
                    }

                    if (owner.BindingCount > 0)
                    {
                        owner.BindingCount--;
                    }

                    return;
                }

                previous = current;
                current = next;
            }
        }

        private bool IsBindingRequestCurrent(int ownerId, uint ownerGeneration, int targetComponentId, int targetGameObjectId, long slotKey, uint requestVersion, Component target)
        {
            if (_isShutdown || target == null || ResourceUnityObjectId.Get(target) != targetComponentId)
            {
                return false;
            }

            if (target.gameObject == null || ResourceUnityObjectId.Get(target.gameObject) != targetGameObjectId)
            {
                return false;
            }

            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return false;
            }

            ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
            if (owner.State != 1 || owner.Generation != ownerGeneration)
            {
                return false;
            }

            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, slotKey);
            if (_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
                return binding.OwnerGeneration == ownerGeneration &&
                       binding.TargetComponentId == targetComponentId &&
                       binding.Version == requestVersion;
            }

            return false;
        }

        private static bool ApplyMaterial(Component target, Material material, ResourceBindingSlotType slotType)
        {
            if (material == null)
            {
                return false;
            }

            switch (slotType)
            {
                case ResourceBindingSlotType.ImageMaterial:
                    if (target is Image image)
                    {
                        image.material = material;
                        return true;
                    }
                    break;
                case ResourceBindingSlotType.RendererSharedMaterial:
                case ResourceBindingSlotType.RendererMaterialInstance:
                    if (target is Renderer renderer)
                    {
                        renderer.sharedMaterial = material;
                        return true;
                    }
                    break;
            }

            return false;
        }

        internal static void ClearMaterialSlot(Component target, Material appliedMaterial, Material runtimeMaterial, ResourceBindingSlotType slotType)
        {
            switch (slotType)
            {
                case ResourceBindingSlotType.ImageMaterial:
                    if (target is Image image && image.material == appliedMaterial)
                    {
                        image.material = null;
                    }
                    break;
                case ResourceBindingSlotType.RendererSharedMaterial:
                    if (target is Renderer renderer && renderer.sharedMaterial == appliedMaterial)
                    {
                        renderer.sharedMaterial = null;
                    }
                    break;
                case ResourceBindingSlotType.RendererMaterialInstance:
                    if (target is Renderer runtimeRenderer && runtimeRenderer.sharedMaterial == runtimeMaterial)
                    {
                        runtimeRenderer.sharedMaterial = null;
                    }
                    break;
            }
        }

        private ResourceBindStatus EnsureOwner(ResourceOwner owner, out int ownerIndex)
        {
            ownerIndex = -1;
            if (_isShutdown)
            {
                return ResourceBindStatus.ServiceShutdown;
            }

            if (owner == null)
            {
                return ResourceBindStatus.MissingOwner;
            }

            if (!owner.IsRegistered)
            {
                ResourceBindStatus status = RegisterOwner(owner);
                if (status != ResourceBindStatus.Success)
                {
                    return status;
                }
            }

            ownerIndex = owner.OwnerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return ResourceBindStatus.MissingOwner;
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(ownerIndex);
            if (slot.State != 1 || slot.Generation != owner.Generation)
            {
                return ResourceBindStatus.StaleOwner;
            }

            return ResourceBindStatus.Success;
        }

        private void ClearAndReleaseBinding(ref BindingSlot binding)
        {
            ClearKnownComponentSlot(ref binding);
            if (binding.RuntimeObject != null)
            {
                Object.Destroy(binding.RuntimeObject);
            }

            if (binding.Lease.IsValid)
            {
                _resourceService.Release(binding.Lease);
            }

            _bindingIndexByOwnerSlot.Remove(new OwnerSlotKey(binding.OwnerId, binding.SlotKey));
            binding.Target = null;
            binding.AppliedAsset = null;
            binding.RuntimeObject = null;
            binding.Lease = ResourceLeaseHandle.Invalid;
            binding.AssetId = 0;
            binding.ViewKeyId = 0;
            binding.Flags = 0;
        }

        private static void ClearKnownComponentSlot(ref BindingSlot binding)
        {
            switch (binding.SlotType)
            {
                case ResourceBindingSlotType.ImageSprite:
                case ResourceBindingSlotType.SubSprite:
                    if (binding.Target is Image image && image.sprite == binding.AppliedAsset)
                    {
                        image.sprite = null;
                    }
                    break;
                case ResourceBindingSlotType.SpriteRendererSprite:
                    if (binding.Target is SpriteRenderer spriteRenderer && spriteRenderer.sprite == binding.AppliedAsset)
                    {
                        spriteRenderer.sprite = null;
                    }
                    break;
                case ResourceBindingSlotType.RendererSharedMaterial:
                    if (binding.Target is Renderer renderer && renderer.sharedMaterial == binding.AppliedAsset)
                    {
                        renderer.sharedMaterial = null;
                    }
                    break;
                case ResourceBindingSlotType.RendererMaterialInstance:
                    if (binding.Target is Renderer runtimeRenderer && runtimeRenderer.sharedMaterial == binding.RuntimeObject)
                    {
                        runtimeRenderer.sharedMaterial = null;
                    }
                    break;
            }
        }

        private static long BuildSlotKey(int targetComponentId, ResourceBindingSlotType slotType, ushort subIndex)
        {
            return ((long)(uint)targetComponentId << 32) | ((long)slotType << 16) | subIndex;
        }

        private static ResourceLeaseOptions ToLeaseOptions(ResourceBindingOptions options)
        {
            return (options & ResourceBindingOptions.KeepAliveOnRelease) != 0
                ? ResourceLeaseOptions.KeepAliveOnRelease
                : ResourceLeaseOptions.None;
        }

        private int AllocateOwnerSlot()
        {
            int index;
            if (_ownerFreeHead >= 0)
            {
                index = _ownerFreeHead;
                ref OwnerSlot free = ref GetOwnerSlotRef(index);
                _ownerFreeHead = free.NextFree;
            }
            else
            {
                index = _ownerNextIndex++;
                EnsureOwnerPage(index);
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(index);
            uint generation = slot.Generation + 1;
            if (generation == 0)
            {
                generation = 1;
            }

            slot = default;
            slot.Generation = generation;
            slot.BindingHead = -1;
            slot.RegisteredTargetHead = -1;
            slot.NextFree = -1;
            return index;
        }

        private void FreeOwnerSlot(int index)
        {
            ref OwnerSlot slot = ref GetOwnerSlotRef(index);
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = generation;
            slot.State = 0;
            slot.NextFree = _ownerFreeHead;
            _ownerFreeHead = index;
        }

        private int AllocateBindingSlot()
        {
            int index;
            if (_bindingFreeHead >= 0)
            {
                index = _bindingFreeHead;
                ref BindingSlot free = ref GetBindingSlotRef(index);
                _bindingFreeHead = free.NextFree;
            }
            else
            {
                index = _bindingNextIndex++;
                EnsureBindingPage(index);
            }

            ref BindingSlot slot = ref GetBindingSlotRef(index);
            slot = default;
            slot.NextByOwner = -1;
            slot.NextFree = -1;
            slot.Lease = ResourceLeaseHandle.Invalid;
            return index;
        }

        private void FreeBindingSlot(int index)
        {
            ref BindingSlot slot = ref GetBindingSlotRef(index);
            slot = default;
            slot.Lease = ResourceLeaseHandle.Invalid;
            slot.NextFree = _bindingFreeHead;
            _bindingFreeHead = index;
        }

        private int AllocateRegisteredTargetSlot()
        {
            int index;
            if (_registeredTargetFreeHead >= 0)
            {
                index = _registeredTargetFreeHead;
                ref RegisteredTargetSlot free = ref GetRegisteredTargetSlotRef(index);
                _registeredTargetFreeHead = free.NextFree;
            }
            else
            {
                index = _registeredTargetNextIndex++;
                EnsureRegisteredTargetPage(index);
            }

            ref RegisteredTargetSlot slot = ref GetRegisteredTargetSlotRef(index);
            slot = default;
            slot.NextByOwner = -1;
            slot.NextFree = -1;
            return index;
        }

        private void FreeRegisteredTargetSlot(int index)
        {
            ref RegisteredTargetSlot slot = ref GetRegisteredTargetSlotRef(index);
            slot = default;
            slot.NextFree = _registeredTargetFreeHead;
            _registeredTargetFreeHead = index;
        }

        private bool IsValidOwnerIndex(int index)
        {
            return index >= 0 && index < _ownerNextIndex && _ownerPages != null && _ownerPages[index >> PageBits] != null;
        }

        private ref OwnerSlot GetOwnerSlotRef(int index)
        {
            return ref _ownerPages[index >> PageBits][index & PageMask];
        }

        private ref BindingSlot GetBindingSlotRef(int index)
        {
            return ref _bindingPages[index >> PageBits][index & PageMask];
        }

        private ref RegisteredTargetSlot GetRegisteredTargetSlotRef(int index)
        {
            return ref _registeredTargetPages[index >> PageBits][index & PageMask];
        }

        private void EnsureOwnerPage(int index)
        {
            int page = index >> PageBits;
            if (_ownerPages == null)
            {
                _ownerPages = new OwnerSlot[Math.Max(4, page + 1)][];
            }
            else if (page >= _ownerPages.Length)
            {
                Array.Resize(ref _ownerPages, Math.Max(page + 1, _ownerPages.Length << 1));
            }

            if (_ownerPages[page] == null)
            {
                _ownerPages[page] = new OwnerSlot[PageSize];
            }
        }

        private void EnsureBindingPage(int index)
        {
            int page = index >> PageBits;
            if (_bindingPages == null)
            {
                _bindingPages = new BindingSlot[Math.Max(4, page + 1)][];
            }
            else if (page >= _bindingPages.Length)
            {
                Array.Resize(ref _bindingPages, Math.Max(page + 1, _bindingPages.Length << 1));
            }

            if (_bindingPages[page] == null)
            {
                _bindingPages[page] = new BindingSlot[PageSize];
            }
        }

        private void EnsureRegisteredTargetPage(int index)
        {
            int page = index >> PageBits;
            if (_registeredTargetPages == null)
            {
                _registeredTargetPages = new RegisteredTargetSlot[Math.Max(4, page + 1)][];
            }
            else if (page >= _registeredTargetPages.Length)
            {
                Array.Resize(ref _registeredTargetPages, Math.Max(page + 1, _registeredTargetPages.Length << 1));
            }

            if (_registeredTargetPages[page] == null)
            {
                _registeredTargetPages[page] = new RegisteredTargetSlot[PageSize];
            }
        }
    }
}
