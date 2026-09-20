using System;
using System.Runtime.ExceptionServices;
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
        private OwnerSlot[][] _owners;
        private BindingSlot[][] _bindings;
        private TargetSlot[][] _targets;
        private int _ownerNext;
        private int _bindingNext;
        private int _targetNext;
        private int _ownerFree = -1;
        private int _bindingFree = -1;
        private int _targetFree = -1;
        private int _ownerCursor;
        private int _targetCursor;
        private uint _nextOwnerGeneration;
        private uint _nextRequestVersion;
        private uint _world;
        private bool _isShutdown;
        private readonly ResourceIndexMap<ulong, int> _ownerByObject = new ResourceIndexMap<ulong, int>();
        private readonly ResourceIndexMap<SlotKey, int> _bindingBySlot = new ResourceIndexMap<SlotKey, int>();
        private readonly ResourceIndexMap<ulong, int> _targetByObject = new ResourceIndexMap<ulong, int>();

        private struct OwnerSlot
        {
            public ResourceOwner Owner;
            public ulong ObjectId;
            public uint Generation;
            public int BindingHead;
            public int TargetHead;
            public int AssetLeaseHead;
            public int BindingCount;
            public int TargetCount;
            public int NextFree;
            public byte State;
        }

        private struct BindingSlot
        {
            public SlotKey Key;
            public Object Target;
            public Object AppliedAsset;
            public Object RuntimeObject;
            public ResourceLeaseHandle Lease;
            public int OwnerId;
            public uint Version;
            public int Previous;
            public int Next;
            public int NextFree;
        }

        private struct TargetSlot
        {
            public Component Target;
            public ulong ObjectId;
            public int OwnerId;
            public int Previous;
            public int Next;
            public int NextFree;
        }

        private readonly struct SlotKey : IEquatable<SlotKey>
        {
            public readonly ulong TargetId;
            public readonly ResourceBindingSlotType Type;

            public SlotKey(ulong targetId, ResourceBindingSlotType type)
            {
                TargetId = targetId;
                Type = type == ResourceBindingSlotType.SubSprite ? ResourceBindingSlotType.ImageSprite :
                    type == ResourceBindingSlotType.RendererMaterialInstance ? ResourceBindingSlotType.RendererSharedMaterial : type;
            }

            public bool Equals(SlotKey other) => TargetId == other.TargetId && Type == other.Type;
            public override bool Equals(object obj) => obj is SlotKey other && Equals(other);
            public override int GetHashCode() => (TargetId.GetHashCode() * 397) ^ (int)Type;
        }

        public ResourceBindingService(ResourceService resourceService)
        {
            _resourceService = resourceService;
        }

        public void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity)
        {
            for (int i = 0; i < ownerCapacity; i += PageSize) ReservePage(ref _owners, i);
            for (int i = 0; i < bindingCapacity; i += PageSize) ReservePage(ref _bindings, i);
            for (int i = 0; i < registeredTargetCapacity; i += PageSize) ReservePage(ref _targets, i);
            _ownerByObject.ReserveCapacity(ownerCapacity);
            _bindingBySlot.ReserveCapacity(bindingCapacity);
            _targetByObject.ReserveCapacity(registeredTargetCapacity);
        }

        public void Shutdown()
        {
            _isShutdown = true;
            unchecked { _world++; }
            Exception error = null;
            for (int i = 0; i < _ownerNext; i++)
            {
                if (Owner(i).State != 1) continue;
                try { ReleaseOwner(i + 1, Owner(i).Generation); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            for (int i = 0; i < _ownerNext; i++)
            {
                if (Owner(i).State != 2) continue;
                try { DrainOwnerBindings(i); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        public void Reset()
        {
            try
            {
                Shutdown();
            }
            finally
            {
                _isShutdown = false;
            }
        }

        public ResourceBindStatus RegisterOwner(ResourceOwner owner)
        {
            if (_isShutdown) return ResourceBindStatus.ServiceShutdown;
            if (owner == null) return ResourceBindStatus.MissingOwner;
            if (owner.IsRegistered)
                return ReferenceEquals(owner.BindingService, this) && IsOwnerCurrent(owner)
                    ? ResourceBindStatus.Success : ResourceBindStatus.StaleOwner;
            ulong objectId = UnityObjectId.Get(owner.gameObject);
            if (_ownerByObject.TryGetValue(objectId, out int oldIndex))
            {
                ReleaseOwner(oldIndex + 1, Owner(oldIndex).Generation);
                return RegisterOwner(owner);
            }
            int index = AllocateOwner();
            ref OwnerSlot slot = ref Owner(index);
            slot.Owner = owner;
            slot.ObjectId = objectId;
            slot.State = 1;
            _ownerByObject.Set(objectId, index);
            owner.SetRegistered(this, index + 1, objectId, slot.Generation);
            return ResourceBindStatus.Success;
        }

        public ResourceBindStatus ReleaseOwner(ResourceOwner owner)
        {
            if (owner == null || !owner.IsRegistered) return ResourceBindStatus.MissingOwner;
            if (!ReferenceEquals(owner.BindingService, this)) return ResourceBindStatus.StaleOwner;
            return ReleaseOwner(owner.OwnerId, owner.Generation);
        }

        public ResourceBindStatus ReleaseOwner(int ownerId, uint generation)
            => ReleaseOwner(ownerId, generation, false);

        internal ResourceBindStatus ReleaseOwner(int ownerId, uint generation, bool keepPrefabSource)
        {
            int index = ownerId - 1;
            if (index < 0 || index >= _ownerNext) return ResourceBindStatus.MissingOwner;
            ref OwnerSlot slot = ref Owner(index);
            if (slot.State != 1 || slot.Generation != generation) return ResourceBindStatus.StaleOwner;
            uint world = _world;
            slot.State = 2;
            _ownerByObject.Remove(slot.ObjectId);
            _resourceService.ReleaseOwnerLeases(ownerId, ref slot.AssetLeaseHead);
            Exception error = null;
            while (slot.BindingHead >= 0)
            {
                int binding = slot.BindingHead;
                if (keepPrefabSource && world == _world && !_isShutdown && slot.Owner != null &&
                    Binding(binding).Key.Type == ResourceBindingSlotType.PrefabSource)
                    binding = Binding(binding).Next;
                if (binding < 0) break;
                try { ReleaseBinding(binding); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            while (slot.TargetHead >= 0) RemoveTarget(slot.TargetHead);
            if (keepPrefabSource && world == _world && !_isShutdown && slot.BindingCount > 0)
            {
                slot.Generation = AllocateOwnerGeneration();
                slot.State = 1;
                _ownerByObject.Set(slot.ObjectId, index);
                slot.Owner.SetRegistered(this, ownerId, slot.ObjectId, slot.Generation);
                if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
                return ResourceBindStatus.Success;
            }
            while (slot.BindingHead >= 0)
            {
                try { ReleaseBinding(slot.BindingHead); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            while (slot.TargetHead >= 0) RemoveTarget(slot.TargetHead);
            if (!ReferenceEquals(slot.Owner, null))
                slot.Owner.ClearRegistered();
            slot = default;
            slot.NextFree = _ownerFree;
            _ownerFree = index;
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return ResourceBindStatus.Success;
        }

        private void DrainOwnerBindings(int index)
        {
            ref OwnerSlot slot = ref Owner(index);
            Exception error = null;
            while (slot.BindingHead >= 0)
            {
                try { ReleaseBinding(slot.BindingHead); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            while (slot.TargetHead >= 0) RemoveTarget(slot.TargetHead);
            if (!ReferenceEquals(slot.Owner, null))
                slot.Owner.ClearRegistered();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        public ResourceBindStatus RegisterTarget(ResourceOwner owner, Component target)
        {
            ResourceBindStatus status = RegisterOwner(owner);
            if (status != ResourceBindStatus.Success) return status;
            if (target == null) return ResourceBindStatus.MissingTarget;
            ulong objectId = UnityObjectId.Get(target);
            bool transferred = false;
            if (_targetByObject.TryGetValue(objectId, out int existing))
            {
                if (Target(existing).OwnerId == owner.OwnerId) return ResourceBindStatus.Success;
                RemoveTarget(existing);
                transferred = true;
            }
            int index = AllocateTarget();
            ref TargetSlot slot = ref Target(index);
            ref OwnerSlot ownerSlot = ref Owner(owner.OwnerId - 1);
            slot.Target = target;
            slot.ObjectId = objectId;
            slot.OwnerId = owner.OwnerId;
            slot.Next = ownerSlot.TargetHead;
            if (slot.Next >= 0) Target(slot.Next).Previous = index;
            ownerSlot.TargetHead = index;
            ownerSlot.TargetCount++;
            _targetByObject.Set(objectId, index);
            if (transferred) ReleaseTargetBindings(objectId);
            return IsOwnerCurrent(owner) && _targetByObject.TryGetValue(objectId, out int current) && Target(current).OwnerId == owner.OwnerId
                ? ResourceBindStatus.Success : ResourceBindStatus.StaleOwner;
        }

        public ResourceBindStatus UnregisterTarget(ResourceOwner owner, Component target)
        {
            if (!IsOwnerCurrent(owner)) return ResourceBindStatus.StaleOwner;
            if (target == null) return ResourceBindStatus.MissingTarget;
            if (!_targetByObject.TryGetValue(UnityObjectId.Get(target), out int index)) return ResourceBindStatus.Success;
            if (Target(index).OwnerId != owner.OwnerId) return ResourceBindStatus.StaleOwner;
            ulong objectId = Target(index).ObjectId;
            RemoveTarget(index);
            ReleaseTargetBindings(objectId);
            return ResourceBindStatus.Success;
        }

        public ResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
            => Bind(owner, image, Typed(key, typeof(Sprite)), ResourceBindingSlotType.ImageSprite, options, false);

        public ResourceBindStatus BindSprite(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
            => Bind(owner, spriteRenderer, Typed(key, typeof(Sprite)), ResourceBindingSlotType.SpriteRendererSprite, options, false);

        public UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, Image image, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
            => BindAsync(owner, image, SubAssets(atlasKey), ResourceBindingSlotType.ImageSprite, options, false, spriteName, cancellationToken);

        public UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
            => BindAsync(owner, spriteRenderer, SubAssets(atlasKey), ResourceBindingSlotType.SpriteRendererSprite, options, false, spriteName, cancellationToken);

        public ResourceBindStatus BindImageMaterial(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
            => Bind(owner, image, Typed(key, typeof(Material)), ResourceBindingSlotType.ImageMaterial, options, false);

        public UniTask<ResourceBindStatus> BindImageMaterialAsync(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
            => BindAsync(owner, image, Typed(key, typeof(Material)), ResourceBindingSlotType.ImageMaterial, options, false, null, cancellationToken);

        public ResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
            => Bind(owner, renderer, Typed(key, typeof(Material)), ResourceBindingSlotType.RendererSharedMaterial, options, false);

        public UniTask<ResourceBindStatus> BindSharedMaterialAsync(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
            => BindAsync(owner, renderer, Typed(key, typeof(Material)), ResourceBindingSlotType.RendererSharedMaterial, options, false, null, cancellationToken);

        public ResourceBindStatus BindMaterialInstance(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None)
            => Bind(owner, renderer, Typed(key, typeof(Material)), ResourceBindingSlotType.RendererSharedMaterial, options, true);

        public UniTask<ResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options = ResourceBindingOptions.None, CancellationToken cancellationToken = default)
            => BindAsync(owner, renderer, Typed(key, typeof(Material)), ResourceBindingSlotType.RendererSharedMaterial, options, true, null, cancellationToken);

        private ResourceBindStatus Bind(ResourceOwner owner, Component target, in ResourceKey key, ResourceBindingSlotType type, ResourceBindingOptions options, bool instance)
        {
            ResourceBindStatus status = Reserve(owner, target, type, out ResourceLoadLifetime request);
            if (status != ResourceBindStatus.Success) return status;
            if (!key.HasResolvedIds && string.IsNullOrEmpty(key.Location))
            {
                ReleaseBinding(request.BindingIndex);
                return ResourceBindStatus.Success;
            }
            try
            {
                ResourceLeaseHandle lease = _resourceService.AcquireBinding(key);
                return Commit(request, lease, options, instance, null);
            }
            finally
            {
                CancelReservation(request);
            }
        }

        private async UniTask<ResourceBindStatus> BindAsync(ResourceOwner owner, Component target, ResourceKey key, ResourceBindingSlotType type,
            ResourceBindingOptions options, bool instance, string spriteName, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return ResourceBindStatus.Canceled;
            ResourceBindStatus status = Reserve(owner, target, type, out ResourceLoadLifetime request);
            if (status != ResourceBindStatus.Success) return status;
            if (!key.HasResolvedIds && string.IsNullOrEmpty(key.Location))
            {
                ReleaseBinding(request.BindingIndex);
                return ResourceBindStatus.Success;
            }
            try
            {
                ResourceLeaseHandle lease = await _resourceService.AcquireBindingAsync(key, cancellationToken, request);
                if (cancellationToken.IsCancellationRequested)
                {
                    _resourceService.Release(lease);
                    return ResourceBindStatus.Canceled;
                }
                return Commit(request, lease, options, instance, spriteName);
            }
            finally
            {
                CancelReservation(request);
            }
        }

        private ResourceBindStatus Reserve(ResourceOwner owner, Component target, ResourceBindingSlotType type, out ResourceLoadLifetime request)
        {
            request = default;
            if (_nextRequestVersion == uint.MaxValue)
                throw new InvalidOperationException("Resource binding version space exhausted.");
            uint version = ++_nextRequestVersion;
            ResourceBindStatus status = RegisterTarget(owner, target);
            if (status != ResourceBindStatus.Success) return status;
            SlotKey key = new SlotKey(UnityObjectId.Get(target), type);
            if (!_bindingBySlot.TryGetValue(key, out int index))
                index = CreateBinding(owner, target, key);
            else if (Binding(index).Version > version)
                return ResourceBindStatus.StaleOwner;
            Binding(index).Version = version;
            request = new ResourceLoadLifetime(this, owner, index, version);
            return ResourceBindStatus.Success;
        }

        private int CreateBinding(ResourceOwner owner, Object target, SlotKey key)
        {
            int index = AllocateBinding();
            ref BindingSlot binding = ref Binding(index);
            ref OwnerSlot slot = ref Owner(owner.OwnerId - 1);
            binding.OwnerId = owner.OwnerId;
            binding.Target = target;
            binding.Key = key;
            binding.Next = slot.BindingHead;
            if (binding.Next >= 0) Binding(binding.Next).Previous = index;
            slot.BindingHead = index;
            slot.BindingCount++;
            _bindingBySlot.Set(key, index);
            return index;
        }

        private ResourceBindStatus Commit(in ResourceLoadLifetime request, ResourceLeaseHandle lease, ResourceBindingOptions options, bool instance, string spriteName)
        {
            Object runtime = null;
            bool transferred = false;
            try
            {
                if (!request.IsCurrent) return ResourceBindStatus.StaleOwner;
                if (!lease.IsValid) return ResourceBindStatus.LoadFailed;
                Object asset;
                if (spriteName != null)
                {
                    if (!_resourceService.TryGetSubSpriteAsset(lease, spriteName, out Sprite sprite))
                        return ResourceBindStatus.LoadFailed;
                    asset = sprite;
                }
                else if (!_resourceService.TryGetLeaseAsset(lease, out asset))
                    return ResourceBindStatus.LoadFailed;
                ref BindingSlot binding = ref Binding(request.BindingIndex);
                bool materialSlot = binding.Key.Type == ResourceBindingSlotType.ImageMaterial || binding.Key.Type == ResourceBindingSlotType.RendererSharedMaterial;
                if (materialSlot ? asset is not Material : asset is not Sprite)
                    return ResourceBindStatus.LoadFailed;
                if (instance)
                    asset = runtime = Object.Instantiate(asset);
                ResourceLeaseHandle previousLease = binding.Lease;
                Object previousRuntimeObject = binding.RuntimeObject;
                binding.AppliedAsset = asset;
                binding.RuntimeObject = runtime;
                binding.Lease = lease;
                transferred = true;
                try
                {
                    Apply(ref binding, options, request);
                    return request.IsCurrent ? ResourceBindStatus.Success : ResourceBindStatus.StaleOwner;
                }
                catch (Exception exception)
                {
                    try
                    {
                        if (Matches(request)) ReleaseBinding(request.BindingIndex);
                    }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException(exception, cleanup);
                    }
                    throw;
                }
                finally
                {
                    ReleaseContents(previousLease, previousRuntimeObject);
                }
            }
            finally
            {
                if (!transferred)
                {
                    _resourceService.Release(lease);
                    ResourceService.DestroyRuntimeObject(runtime);
                }
            }
        }

        private static void Apply(ref BindingSlot binding, ResourceBindingOptions options, in ResourceLoadLifetime request)
        {
            switch (binding.Key.Type)
            {
                case ResourceBindingSlotType.ImageSprite:
                    var image = (Image)binding.Target;
                    image.sprite = (Sprite)binding.AppliedAsset;
                    if ((options & ResourceBindingOptions.SetNativeSize) != 0 && request.IsCurrent) image.SetNativeSize();
                    break;
                case ResourceBindingSlotType.SpriteRendererSprite:
                    ((SpriteRenderer)binding.Target).sprite = (Sprite)binding.AppliedAsset;
                    break;
                case ResourceBindingSlotType.ImageMaterial:
                    ((Image)binding.Target).material = (Material)binding.AppliedAsset;
                    break;
                case ResourceBindingSlotType.RendererSharedMaterial:
                    ((Renderer)binding.Target).sharedMaterial = (Material)binding.AppliedAsset;
                    break;
            }
        }

        internal ResourceBindStatus RegisterPrefabSource(ResourceOwner owner, ResourceLeaseHandle lease, GameObject prefabSource)
        {
            ResourceBindStatus status = RegisterOwner(owner);
            if (status != ResourceBindStatus.Success) return status;
            if (!lease.IsValid || prefabSource == null) return ResourceBindStatus.InvalidKey;
            SlotKey key = new SlotKey(owner.GameObjectId, ResourceBindingSlotType.PrefabSource);
            if (!_bindingBySlot.TryGetValue(key, out int index)) index = CreateBinding(owner, owner, key);
            ref BindingSlot binding = ref Binding(index);
            ResourceLeaseHandle previous = binding.Lease;
            binding.Lease = lease;
            binding.AppliedAsset = prefabSource;
            _resourceService.Release(previous);
            return ResourceBindStatus.Success;
        }

        internal void RetainAsset(int ownerId, ResourceLeaseHandle lease)
        {
            _resourceService.RetainOwnerLease(ownerId, ref Owner(ownerId - 1).AssetLeaseHead, lease);
        }

        internal bool IsOwnerCurrent(ResourceOwner owner)
        {
            if (_isShutdown || owner == null || !ReferenceEquals(owner.BindingService, this))
                return false;
            int index = owner.OwnerId - 1;
            return index >= 0 && index < _ownerNext && Owner(index).State == 1 &&
                   Owner(index).Generation == owner.Generation && ReferenceEquals(Owner(index).Owner, owner);
        }

        internal bool IsLoadCurrent(in ResourceLoadLifetime request)
        {
            ResourceOwner owner = request.Owner;
            if (_isShutdown || owner == null || !ReferenceEquals(owner.BindingService, this) ||
                owner.OwnerId != request.OwnerId || owner.Generation != request.OwnerGeneration || Owner(request.OwnerId - 1).State != 1)
                return false;
            return request.BindingIndex < 0 || Matches(request) && Binding(request.BindingIndex).Target != null;
        }

        private bool Matches(in ResourceLoadLifetime request)
        {
            ref BindingSlot binding = ref Binding(request.BindingIndex);
            return binding.OwnerId == request.OwnerId && binding.Version == request.Version;
        }

        private void CancelReservation(in ResourceLoadLifetime request)
        {
            if (Matches(request) && !Binding(request.BindingIndex).Lease.IsValid)
                ReleaseBinding(request.BindingIndex);
        }

        private void ReleaseBinding(int index)
        {
            BindingSlot binding = DetachBinding(index);
            ReleaseDetachedBinding(in binding);
        }

        private BindingSlot DetachBinding(int index)
        {
            BindingSlot binding = Binding(index);
            ref OwnerSlot owner = ref Owner(binding.OwnerId - 1);
            if (binding.Previous >= 0) Binding(binding.Previous).Next = binding.Next;
            else owner.BindingHead = binding.Next;
            if (binding.Next >= 0) Binding(binding.Next).Previous = binding.Previous;
            owner.BindingCount--;
            _bindingBySlot.Remove(binding.Key);
            Binding(index) = default;
            Binding(index).NextFree = _bindingFree;
            _bindingFree = index;
            return binding;
        }

        private void ReleaseDetachedBinding(in BindingSlot binding)
        {
            try
            {
                if (!_bindingBySlot.TryGetValue(binding.Key, out _)) ClearApplied(in binding);
            }
            finally
            {
                ReleaseContents(binding.Lease, binding.RuntimeObject);
            }
        }

        private static void ClearApplied(in BindingSlot binding)
        {
            if (binding.Target == null || binding.AppliedAsset == null) return;
            switch (binding.Key.Type)
            {
                case ResourceBindingSlotType.ImageSprite:
                    var image = (Image)binding.Target;
                    if (image.sprite == binding.AppliedAsset) image.sprite = null;
                    break;
                case ResourceBindingSlotType.SpriteRendererSprite:
                    var spriteRenderer = (SpriteRenderer)binding.Target;
                    if (spriteRenderer.sprite == binding.AppliedAsset) spriteRenderer.sprite = null;
                    break;
                case ResourceBindingSlotType.ImageMaterial:
                    var materialImage = (Image)binding.Target;
                    if (materialImage.material == binding.AppliedAsset) materialImage.material = null;
                    break;
                case ResourceBindingSlotType.RendererSharedMaterial:
                    var renderer = (Renderer)binding.Target;
                    if (renderer.sharedMaterial == binding.AppliedAsset) renderer.sharedMaterial = null;
                    break;
            }
        }

        private void ReleaseContents(ResourceLeaseHandle lease, Object runtimeObject)
        {
            _resourceService.Release(lease);
            ResourceService.DestroyRuntimeObject(runtimeObject);
        }

        private void ReleaseTargetBindings(ulong id, ResourceBindingSlotType type = ResourceBindingSlotType.ImageSprite)
        {
            if (type > ResourceBindingSlotType.RendererSharedMaterial) return;
            bool found = _bindingBySlot.TryGetValue(new SlotKey(id, type), out int index);
            BindingSlot binding = found ? DetachBinding(index) : default;
            Exception error = null;
            try { ReleaseTargetBindings(id, type + 1); }
            catch (Exception exception) { error = exception; }
            if (found)
            {
                try { ReleaseDetachedBinding(in binding); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private void RemoveTarget(int index)
        {
            TargetSlot target = Target(index);
            ref OwnerSlot owner = ref Owner(target.OwnerId - 1);
            if (target.Previous >= 0) Target(target.Previous).Next = target.Next;
            else owner.TargetHead = target.Next;
            if (target.Next >= 0) Target(target.Next).Previous = target.Previous;
            owner.TargetCount--;
            _targetByObject.Remove(target.ObjectId);
            Target(index) = default;
            Target(index).NextFree = _targetFree;
            _targetFree = index;
        }

        internal void ProcessDestroyedObjects(int budget)
        {
            Exception error = null;
            int ownerChecks = _ownerByObject.Count > 0 ? Math.Min(budget, _ownerNext) : 0;
            for (int i = 0; i < ownerChecks; i++)
            {
                if (_ownerCursor >= _ownerNext) _ownerCursor = 0;
                int index = _ownerCursor++;
                if (Owner(index).State != 1 || Owner(index).Owner != null) continue;
                try { ReleaseOwner(index + 1, Owner(index).Generation); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            int targetChecks = _targetByObject.Count > 0 ? Math.Min(budget, _targetNext) : 0;
            for (int i = 0; i < targetChecks; i++)
            {
                if (_targetCursor >= _targetNext) _targetCursor = 0;
                int index = _targetCursor++;
                if (Target(index).OwnerId == 0 || Target(index).Target != null) continue;
                ulong objectId = Target(index).ObjectId;
                RemoveTarget(index);
                try { ReleaseTargetBindings(objectId); }
                catch (Exception exception) { error = Combine(error, exception); }
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        public int GetOwnerInfos(ResourceOwnerInfo[] results, int startIndex, int maxCount)
        {
            int start = Math.Max(0, startIndex);
            int count = results == null ? 0 : Math.Min(Math.Min(maxCount, results.Length), _ownerNext - start);
            for (int i = 0; i < count; i++)
            {
                ref OwnerSlot slot = ref Owner(start + i);
                results[i] = new ResourceOwnerInfo
                {
                    Active = slot.State == 1,
                    OwnerIndex = start + i,
                    OwnerId = slot.State == 1 ? start + i + 1 : 0,
                    GameObjectId = slot.ObjectId,
                    Generation = slot.Generation,
                    BindingCount = slot.BindingCount,
                    RegisteredTargetCount = slot.TargetCount,
                    HasOwnerObject = slot.Owner != null,
#if UNITY_EDITOR
                    OwnerObject = slot.Owner != null ? slot.Owner.gameObject : null
#endif
                };
            }
            return _ownerNext;
        }

        public int GetBindingInfos(ResourceBindingInfo[] results, int startIndex, int maxCount)
        {
            int start = Math.Max(0, startIndex);
            int count = results == null ? 0 : Math.Min(Math.Min(maxCount, results.Length), _bindingNext - start);
            for (int i = 0; i < count; i++)
            {
                ref BindingSlot slot = ref Binding(start + i);
                Component target = slot.Target as Component;
                results[i] = new ResourceBindingInfo
                {
                    Active = slot.OwnerId > 0,
                    BindingIndex = start + i,
                    OwnerId = slot.OwnerId,
                    OwnerGeneration = slot.OwnerId > 0 ? Owner(slot.OwnerId - 1).Generation : 0,
                    TargetComponentId = slot.Key.TargetId,
                    TargetGameObjectId = target != null ? UnityObjectId.Get(target.gameObject) : 0,
                    SlotKey = slot.Key.TargetId,
                    AssetId = _resourceService.TryGetLeaseAssetId(slot.Lease, out int assetId) ? assetId : -1,
                    Lease = slot.Lease,
                    Version = slot.Version,
                    SlotType = slot.RuntimeObject != null ? ResourceBindingSlotType.RendererMaterialInstance : slot.Key.Type,
                    HasAppliedAsset = slot.AppliedAsset != null,
                    HasRuntimeObject = slot.RuntimeObject != null,
#if UNITY_EDITOR
                    TargetObject = slot.Target
#endif
                };
            }
            return _bindingNext;
        }

        private static ResourceKey Typed(in ResourceKey key, Type type)
            => key.AssetType == null && !key.HasResolvedIds ? new ResourceKey(key.Location, key.PackageName, type) : key;

        private static ResourceKey SubAssets(in ResourceKey key)
            => key.HasResolvedIds ? key : new ResourceKey(key.Location, key.PackageName, typeof(Sprite), ResourceAssetKind.SubAssets);

        private static Exception Combine(Exception current, Exception next)
            => current == null ? next : new AggregateException(current, next);

        private int AllocateOwner()
        {
            int index;
            if (_ownerFree >= 0) { index = _ownerFree; _ownerFree = Owner(index).NextFree; }
            else { index = _ownerNext++; ReservePage(ref _owners, index); }
            Owner(index) = new OwnerSlot { Generation = AllocateOwnerGeneration(), BindingHead = -1, TargetHead = -1, AssetLeaseHead = -1 };
            return index;
        }

        private uint AllocateOwnerGeneration()
        {
            if (_nextOwnerGeneration == uint.MaxValue) throw new InvalidOperationException("Resource owner generation space exhausted.");
            return ++_nextOwnerGeneration;
        }

        private int AllocateBinding()
        {
            int index;
            if (_bindingFree >= 0) { index = _bindingFree; _bindingFree = Binding(index).NextFree; }
            else { index = _bindingNext++; ReservePage(ref _bindings, index); }
            Binding(index) = new BindingSlot { Previous = -1, Next = -1 };
            return index;
        }

        private int AllocateTarget()
        {
            int index;
            if (_targetFree >= 0) { index = _targetFree; _targetFree = Target(index).NextFree; }
            else { index = _targetNext++; ReservePage(ref _targets, index); }
            Target(index) = new TargetSlot { Previous = -1, Next = -1 };
            return index;
        }

        private ref OwnerSlot Owner(int index) => ref _owners[index >> PageBits][index & PageMask];
        private ref BindingSlot Binding(int index) => ref _bindings[index >> PageBits][index & PageMask];
        private ref TargetSlot Target(int index) => ref _targets[index >> PageBits][index & PageMask];

        private static void ReservePage<T>(ref T[][] pages, int index)
        {
            int page = index >> PageBits;
            if (pages == null) pages = new T[Math.Max(4, page + 1)][];
            else if (page >= pages.Length) Array.Resize(ref pages, Math.Max(page + 1, pages.Length << 1));
            if (pages[page] == null) pages[page] = new T[PageSize];
        }
    }
}
