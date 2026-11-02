using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    public readonly struct AssetRef<T> where T : Object
    {
        internal readonly int Id;

        internal AssetRef(int id)
        {
            Id = id;
        }

        public bool IsValid => Id != 0;
    }

    public readonly struct AssetLease<T> : IDisposable where T : Object
    {
        public readonly T Asset;
        private readonly ResourceService _service;
        private readonly int _entry;
        private readonly uint _generation;

        internal AssetLease(ResourceService service, int entry, uint generation, T asset)
        {
            _service = service;
            _entry = entry;
            _generation = generation;
            Asset = asset;
        }

        public bool IsValid => _service != null && _service.IsLeaseAlive(_entry, _generation);

        public void Dispose()
        {
            _service?.ReleaseLease(_entry, _generation);
        }
    }

    public readonly struct ResourceScope : IDisposable
    {
        internal readonly ResourceService Service;
        internal readonly int Index;
        internal readonly uint Generation;

        internal ResourceScope(ResourceService service, int index, uint generation)
        {
            Service = service;
            Index = index;
            Generation = generation;
        }

        public bool IsAlive => Service != null && Service.IsScopeAlive(Index, Generation);

        public T Load<T>(AssetRef<T> key) where T : Object
        {
            return Service.LoadIntoScope<T>(this, key.Id);
        }

        public T Load<T>(string location, string packageName = null) where T : Object
        {
            return Service.LoadIntoScope<T>(this, Service.KeyId<T>(location, packageName));
        }

        public UniTask<T> LoadAsync<T>(AssetRef<T> key, CancellationToken cancellationToken = default) where T : Object
        {
            return Service.LoadIntoScopeAsync<T>(this, key.Id, cancellationToken);
        }

        public UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = null) where T : Object
        {
            return Service.LoadIntoScopeAsync<T>(this, Service.KeyId<T>(location, packageName), cancellationToken);
        }

        public void Dispose()
        {
            Service?.DisposeScope(Index, Generation);
        }
    }

    internal enum ResourceAssetState : byte
    {
        Released = 0,
        Loading = 1,
        Active = 2,
        Idle = 3,
    }

    internal struct ResourceAssetInfo
    {
        public int AssetId;
        public string Package;
        public string Location;
        public string TypeName;
        public bool SubAssets;
        public ResourceAssetState State;
        public int RefCount;
        public float IdleSeconds;
    }

    internal enum ResourceScopeSource : byte
    {
        Manual = 0,
        Instance = 1,
        Binding = 2,
    }

    internal struct ResourceScopeInfo
    {
        public int Index;
        public uint Generation;
        public ResourceScopeSource Source;
        public string Name;
        public int EntryCount;
        public int BindingCount;
        public bool HasOwner;
        public UnityEngine.GameObject OwnerObject;
    }

    internal struct ResourceStats
    {
        public int AssetCount;
        public int LoadingCount;
        public int IdleCount;
        public int ReferenceCount;
        public int ScopeCount;
        public int ScopeEntryCount;
        public int LeaseCount;
        public int BindingCount;
        public int BindingReferenceCount;
        public int WaiterCount;
        public long HitCount;
        public long IdleHitCount;
        public long JoinCount;
        public long MissCount;
    }
}
