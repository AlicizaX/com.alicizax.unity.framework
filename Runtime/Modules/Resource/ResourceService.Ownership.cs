using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        private const int LeaseScope = -1;
        private const int FreeEntry = -2;

        private struct ScopeSlot
        {
            public ResourceOwner Owner;
            public string Name;
            public int EntryHead;
            public int EntryCount;
            public uint Generation;
            public bool Live;
            public ResourceScopeSource Source;
        }

        private struct EntrySlot
        {
            public int AssetId;
            public int Scope;
            public int Next;
            public uint Generation;
        }

        private readonly ResourceSlotTable<ScopeSlot> _scopes = new ResourceSlotTable<ScopeSlot>();
        private readonly ResourceSlotTable<EntrySlot> _entries = new ResourceSlotTable<EntrySlot>();
        private readonly ResourceUlongIntMap _scopeEntryIds = new ResourceUlongIntMap();
        private int _leaseCount;
        private int _ownerSweepCursor;

        public ResourceScope CreateScope(string debugName = null)
        {
            int index = _scopes.Allocate();
            ref ScopeSlot scope = ref _scopes[index];
            if (scope.Generation == 0)
                scope.Generation = 1;
            scope.Live = true;
            scope.Source = ResourceScopeSource.Manual;
            scope.Name = debugName;
            scope.EntryHead = -1;
            return new ResourceScope(this, index, scope.Generation);
        }

        internal bool IsScopeAlive(int index, uint generation) => _scopes[index].Generation == generation;

        internal void DisposeScope(int index, uint generation)
        {
            ref ScopeSlot scope = ref _scopes[index];
            if (scope.Generation != generation)
                return;

            _bindings.ReleaseScope(index);
            if (scope.Generation != generation)
                return;

            int entry = scope.EntryHead;
            scope.Generation = NextGeneration(scope.Generation);
            scope.Live = false;
            scope.Owner = null;
            scope.Name = null;
            scope.EntryHead = -1;
            scope.EntryCount = 0;
            _scopes.Free(index);

            while (entry >= 0)
            {
                ref EntrySlot slot = ref _entries[entry];
                int next = slot.Next;
                int assetId = slot.AssetId;
                _scopeEntryIds.Remove(ScopeEntryKey(index, assetId));
                FreeEntrySlot(entry, ref slot);
                RemoveReference(assetId);
                entry = next;
            }
        }

        internal bool AttachToScope(int scopeIndex, uint generation, int assetId)
        {
            if (_scopes[scopeIndex].Generation != generation)
            {
                RemoveReference(assetId);
                return false;
            }

            if (_scopeEntryIds.TryGetValue(ScopeEntryKey(scopeIndex, assetId), out _))
                RemoveReference(assetId);
            else
                AddScopeEntry(scopeIndex, assetId);
            return true;
        }

        private void AddScopeEntry(int scopeIndex, int assetId)
        {
            int entry = AllocateEntry(assetId, scopeIndex);
            ref ScopeSlot scope = ref _scopes[scopeIndex];
            _entries[entry].Next = scope.EntryHead;
            scope.EntryHead = entry;
            scope.EntryCount++;
            _scopeEntryIds.Set(ScopeEntryKey(scopeIndex, assetId), entry);
        }

        internal AssetLease<T> CreateLease<T>(int assetId) where T : Object
        {
            int entry = AllocateEntry(assetId, LeaseScope);
            _leaseCount++;
            return new AssetLease<T>(this, entry, _entries[entry].Generation, AssetOf<T>(assetId));
        }

        internal bool IsLeaseAlive(int entry, uint generation) => _entries[entry].Generation == generation;

        internal void ReleaseLease(int entry, uint generation)
        {
            ref EntrySlot slot = ref _entries[entry];
            if (slot.Generation != generation)
                return;
            int assetId = slot.AssetId;
            FreeEntrySlot(entry, ref slot);
            _leaseCount--;
            RemoveReference(assetId);
        }

        private int AllocateEntry(int assetId, int scope)
        {
            int entry = _entries.Allocate();
            ref EntrySlot slot = ref _entries[entry];
            if (slot.Generation == 0)
                slot.Generation = 1;
            slot.AssetId = assetId;
            slot.Scope = scope;
            slot.Next = -1;
            return entry;
        }

        private void FreeEntrySlot(int entry, ref EntrySlot slot)
        {
            slot.Generation = NextGeneration(slot.Generation);
            slot.Scope = FreeEntry;
            slot.AssetId = 0;
            _entries.Free(entry);
        }

        private void ClearAllScopeEntries()
        {
            for (int entry = 0; entry < _entries.Count; entry++)
            {
                ref EntrySlot slot = ref _entries[entry];
                if (slot.Scope != FreeEntry)
                    FreeEntrySlot(entry, ref slot);
            }

            for (int index = 0; index < _scopes.Count; index++)
            {
                ref ScopeSlot scope = ref _scopes[index];
                scope.EntryHead = -1;
                scope.EntryCount = 0;
            }

            _scopeEntryIds.Clear();
            _leaseCount = 0;
        }

        private void DisposeAllScopes()
        {
            for (int index = 0; index < _scopes.Count; index++)
            {
                ref ScopeSlot scope = ref _scopes[index];
                if (!scope.Live)
                    continue;
                scope.Generation = NextGeneration(scope.Generation);
                scope.Live = false;
                scope.Owner = null;
                scope.Name = null;
                _scopes.Free(index);
            }
        }

        internal ResourceScope OwnerScope(GameObject gameObject, ResourceScopeSource source, string name)
        {
            ResourceOwner owner = gameObject.GetComponent<ResourceOwner>();
            if (owner == null)
                owner = gameObject.AddComponent<ResourceOwner>();
            ResourceScope scope = owner.Scope;
            if (ReferenceEquals(scope.Service, this) && IsScopeAlive(scope.Index, scope.Generation))
                return scope;

            scope = CreateScope(name);
            _scopes[scope.Index].Owner = owner;
            _scopes[scope.Index].Source = source;
            owner.Scope = scope;
            return scope;
        }

        private void ReleaseDestroyedOwnerScopes()
        {
            int checks = Math.Min(MaintenanceSweepBudget, _scopes.Count);
            for (int i = 0; i < checks; i++)
            {
                if (_ownerSweepCursor >= _scopes.Count)
                    _ownerSweepCursor = 0;
                int index = _ownerSweepCursor++;
                ref ScopeSlot scope = ref _scopes[index];
                if (scope.Live && !ReferenceEquals(scope.Owner, null) && scope.Owner == null)
                    DisposeScope(index, scope.Generation);
            }
        }

        public GameObject Instantiate(string location, Transform parent = null, string packageName = null)
        {
            int assetId = KeyId<GameObject>(location, packageName);
            return _state == ServiceState.Running && AcquireSync(assetId) ? InstantiateOwned(assetId, parent) : null;
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = null)
        {
            int assetId = KeyId<GameObject>(location, packageName);
            using AssetLease<GameObject> lease = await LoadLeaseAsync<GameObject>(assetId, cancellationToken);
            if (lease.Asset == null || cancellationToken.IsCancellationRequested || (!ReferenceEquals(parent, null) && parent == null))
                return null;
            return AddReference(assetId) ? InstantiateOwned(assetId, parent) : null;
        }

        private GameObject InstantiateOwned(int assetId, Transform parent)
        {
            int resetEpoch = _resetEpoch;
            GameObject instance = Object.Instantiate(AssetOf<GameObject>(assetId), parent);
            if (resetEpoch != _resetEpoch)
            {
                DestroyRuntimeObject(instance);
                return null;
            }

            ResourceScope scope = OwnerScope(instance, ResourceScopeSource.Instance, _keys.LocationName(_keys[assetId].LocationId));
            AttachToScope(scope.Index, scope.Generation, assetId);
            return instance;
        }

        internal static void DestroyRuntimeObject(Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }

        private static ulong ScopeEntryKey(int scopeIndex, int assetId) => ((ulong)(uint)scopeIndex << 32) | (uint)assetId;

        internal static uint NextGeneration(uint generation) => generation == uint.MaxValue ? 1u : generation + 1u;
    }
}
