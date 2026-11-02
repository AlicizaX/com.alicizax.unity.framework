using System;
using System.Collections.Generic;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal sealed class ResourceBindingService
    {
        private const int SweepBudget = 32;

        private struct BindingSlot
        {
            public Component Target;
            public Object Applied;
            public Object RuntimeInstance;
            public ulong TargetId;
            public int AssetId;
            public int Scope;
            public int Previous;
            public int Next;
            public uint Version;
            public uint Generation;
            public ResourceBindingSlot Slot;
        }

        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            private readonly ulong _targetId;
            private readonly ResourceBindingSlot _slot;

            public BindingKey(ulong targetId, ResourceBindingSlot slot)
            {
                _targetId = targetId;
                _slot = slot == ResourceBindingSlot.RendererMaterialInstance ? ResourceBindingSlot.RendererSharedMaterial : slot;
            }

            public bool Equals(BindingKey other) => _targetId == other._targetId && _slot == other._slot;

            public override bool Equals(object obj) => obj is BindingKey other && Equals(other);

            public override int GetHashCode() => (_targetId.GetHashCode() * 397) ^ (int)_slot;
        }

        private readonly ResourceService _service;
        private readonly ResourceSlotTable<BindingSlot> _slots = new ResourceSlotTable<BindingSlot>();
        private readonly Dictionary<BindingKey, int> _slotIds = new Dictionary<BindingKey, int>();
        private int[] _scopeHeads = Array.Empty<int>();
        private int _sweepCursor;

        internal ResourceBindingService(ResourceService service)
        {
            _service = service;
        }

        internal int Count => _slots.LiveCount;

        internal int SlotCount => _slots.Count;

        internal void Bind(in ResourceScope scope, Component target, ResourceBindingSlot slot, int assetId, string spriteName, ResourceBindingOptions options)
        {
            if (!_service.IsRunning || !scope.IsAlive)
                return;

            ulong targetId = UnityObjectId.Get(target);
            BindingKey key = new BindingKey(targetId, slot);
            if (!_slotIds.TryGetValue(key, out int index))
            {
                if (assetId == 0)
                    return;
                index = CreateSlot(scope.Index, target, targetId, key);
            }
            else if (_slots[index].Scope != scope.Index)
            {
                Unlink(index);
                Link(index, scope.Index);
            }

            ref BindingSlot binding = ref _slots[index];
            binding.Slot = slot;
            uint version = ++binding.Version;
            uint generation = binding.Generation;
            if (assetId == 0)
            {
                Release(index, true);
                return;
            }

            if (_service.AcquireLoaded(assetId))
            {
                Apply(index, assetId, spriteName, options);
                return;
            }

            if ((options & ResourceBindingOptions.Synchronous) != 0)
            {
                if (_service.AcquireSync(assetId))
                    ApplyLoaded(index, generation, version, assetId, spriteName, options);
                return;
            }

            if (_service.StartLoading(assetId))
                _service.AddWaiter(assetId, BindingLoadWaiter.Create(this, index, generation, version, spriteName, options));
        }

        internal void ApplyLoaded(int index, uint generation, uint version, int assetId, string spriteName, ResourceBindingOptions options)
        {
            ref BindingSlot binding = ref _slots[index];
            if (binding.Generation != generation || binding.Version != version || binding.Target == null)
            {
                _service.RemoveReference(assetId);
                return;
            }

            Apply(index, assetId, spriteName, options);
        }

        private void Apply(int index, int assetId, string spriteName, ResourceBindingOptions options)
        {
            Object asset = spriteName == null ? _service.AssetOf(assetId) : _service.SubSpriteOf(assetId, spriteName);
            if (asset == null)
            {
                Log.Warning(ZString.Format("Resource binding sprite '{0}' not found.", spriteName));
                _service.RemoveReference(assetId);
                return;
            }

            ref BindingSlot binding = ref _slots[index];
            int previousAssetId = binding.AssetId;
            Object previousInstance = binding.RuntimeInstance;
            bool instance = binding.Slot == ResourceBindingSlot.RendererMaterialInstance;
            Object applied = instance ? Object.Instantiate(asset) : asset;
            Component target = binding.Target;
            ResourceBindingSlot slot = binding.Slot;
            binding.AssetId = assetId;
            binding.Applied = applied;
            binding.RuntimeInstance = instance ? applied : null;
            if (previousAssetId != 0)
                _service.RemoveReference(previousAssetId);

            SetProperty(target, slot, applied, options);
            ResourceService.DestroyRuntimeObject(previousInstance);
        }

        internal void ReleaseScope(int scope)
        {
            if (scope >= _scopeHeads.Length)
                return;
            while (_scopeHeads[scope] >= 0)
                Release(_scopeHeads[scope], true);
        }

        internal void DropReferences()
        {
            for (int index = 0; index < _slots.Count; index++)
                _slots[index].AssetId = 0;
        }

        internal void ReleaseAll()
        {
            for (int index = 0; index < _slots.Count; index++)
            {
                if (!ReferenceEquals(_slots[index].Target, null))
                    Release(index, true);
            }
        }

        internal void ReleaseDestroyedTargets()
        {
            int checks = Math.Min(SweepBudget, _slots.Count);
            for (int i = 0; i < checks; i++)
            {
                if (_sweepCursor >= _slots.Count)
                    _sweepCursor = 0;
                int index = _sweepCursor++;
                Component target = _slots[index].Target;
                if (!ReferenceEquals(target, null) && target == null)
                    Release(index, false);
            }
        }

        private void Release(int index, bool clearTarget)
        {
            ref BindingSlot binding = ref _slots[index];
            Component target = binding.Target;
            Object applied = binding.Applied;
            Object instance = binding.RuntimeInstance;
            int assetId = binding.AssetId;
            ResourceBindingSlot slot = binding.Slot;
            Unlink(index);
            _slotIds.Remove(new BindingKey(binding.TargetId, slot));
            binding.Target = null;
            binding.Applied = null;
            binding.RuntimeInstance = null;
            binding.AssetId = 0;
            binding.Generation = ResourceService.NextGeneration(binding.Generation);
            _slots.Free(index);
            if (assetId != 0)
                _service.RemoveReference(assetId);

            if (clearTarget && target != null)
                ClearProperty(target, slot, applied);
            ResourceService.DestroyRuntimeObject(instance);
        }

        private int CreateSlot(int scope, Component target, ulong targetId, BindingKey key)
        {
            int index = _slots.Allocate();
            ref BindingSlot binding = ref _slots[index];
            if (binding.Generation == 0)
                binding.Generation = 1;
            binding.Target = target;
            binding.TargetId = targetId;
            _slotIds.Add(key, index);
            Link(index, scope);
            return index;
        }

        private void Link(int index, int scope)
        {
            if (scope >= _scopeHeads.Length)
            {
                int oldLength = _scopeHeads.Length;
                Array.Resize(ref _scopeHeads, Math.Max(Math.Max(16, oldLength << 1), scope + 1));
                for (int i = oldLength; i < _scopeHeads.Length; i++)
                    _scopeHeads[i] = -1;
            }

            ref BindingSlot binding = ref _slots[index];
            int head = _scopeHeads[scope];
            binding.Scope = scope;
            binding.Previous = -1;
            binding.Next = head;
            if (head >= 0)
                _slots[head].Previous = index;
            _scopeHeads[scope] = index;
        }

        private void Unlink(int index)
        {
            ref BindingSlot binding = ref _slots[index];
            if (binding.Previous >= 0)
                _slots[binding.Previous].Next = binding.Next;
            else
                _scopeHeads[binding.Scope] = binding.Next;
            if (binding.Next >= 0)
                _slots[binding.Next].Previous = binding.Previous;
        }

        private static void SetProperty(Component target, ResourceBindingSlot slot, Object applied, ResourceBindingOptions options)
        {
            switch (slot)
            {
                case ResourceBindingSlot.ImageSprite:
                    Image image = (Image)target;
                    image.sprite = (Sprite)applied;
                    if ((options & ResourceBindingOptions.SetNativeSize) != 0)
                        image.SetNativeSize();
                    break;
                case ResourceBindingSlot.SpriteRendererSprite:
                    ((SpriteRenderer)target).sprite = (Sprite)applied;
                    break;
                case ResourceBindingSlot.ImageMaterial:
                    ((Image)target).material = (Material)applied;
                    break;
                default:
                    ((Renderer)target).sharedMaterial = (Material)applied;
                    break;
            }
        }

        private static void ClearProperty(Component target, ResourceBindingSlot slot, Object applied)
        {
            switch (slot)
            {
                case ResourceBindingSlot.ImageSprite:
                    Image image = (Image)target;
                    if (image.sprite == applied)
                        image.sprite = null;
                    break;
                case ResourceBindingSlot.SpriteRendererSprite:
                    SpriteRenderer spriteRenderer = (SpriteRenderer)target;
                    if (spriteRenderer.sprite == applied)
                        spriteRenderer.sprite = null;
                    break;
                case ResourceBindingSlot.ImageMaterial:
                    Image materialImage = (Image)target;
                    if (materialImage.material == applied)
                        materialImage.material = null;
                    break;
                default:
                    Renderer renderer = (Renderer)target;
                    if (renderer.sharedMaterial == applied)
                        renderer.sharedMaterial = null;
                    break;
            }
        }

        internal int GetInfos(ResourceBindingInfo[] results, int startIndex)
        {
            int count = 0;
            for (int index = startIndex; index < _slots.Count && count < results.Length; index++)
            {
                ref BindingSlot binding = ref _slots[index];
                if (ReferenceEquals(binding.Target, null))
                    continue;
                results[count++] = new ResourceBindingInfo
                {
                    Index = index,
                    Scope = binding.Scope,
                    Target = binding.Target,
                    TargetId = binding.TargetId,
                    Slot = binding.Slot,
                    AssetId = binding.AssetId,
                    Version = binding.Version,
                    HasRuntimeInstance = binding.RuntimeInstance != null,
                };
            }

            return count;
        }

        internal int CountScopeBindings(int scope)
        {
            int count = 0;
            if (scope >= _scopeHeads.Length)
                return 0;
            for (int index = _scopeHeads[scope]; index >= 0; index = _slots[index].Next)
                count++;
            return count;
        }

        internal int CountReferences()
        {
            int count = 0;
            for (int index = 0; index < _slots.Count; index++)
            {
                if (_slots[index].AssetId != 0)
                    count++;
            }

            return count;
        }
    }
}
