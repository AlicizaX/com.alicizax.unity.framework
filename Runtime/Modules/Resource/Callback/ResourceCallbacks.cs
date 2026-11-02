using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal abstract class LoadWaiter : MemoryObject
    {
        internal LoadWaiter Next;
        internal CancellationToken CancellationToken;

        internal abstract void Complete(int assetId);

        internal abstract void Abort();
    }

    internal sealed class ScopeLoadPromise<T> : LoadWaiter, IUniTaskSource<T> where T : Object
    {
        private UniTaskCompletionSourceCore<T> _core;
        private ResourceService _service;
        private int _scopeIndex;
        private uint _scopeGeneration;

        internal short Version => _core.Version;

        internal static ScopeLoadPromise<T> Create(ResourceService service, in ResourceScope scope, CancellationToken cancellationToken)
        {
            ScopeLoadPromise<T> promise = MemoryPool.Acquire<ScopeLoadPromise<T>>();
            promise._service = service;
            promise._scopeIndex = scope.Index;
            promise._scopeGeneration = scope.Generation;
            promise.CancellationToken = cancellationToken;
            return promise;
        }

        internal override void Complete(int assetId)
        {
            _core.TrySetResult(_service.AttachToScope(_scopeIndex, _scopeGeneration, assetId) ? _service.AssetOf<T>(assetId) : null);
        }

        internal override void Abort()
        {
            _core.TrySetResult(null);
        }

        public T GetResult(short token)
        {
            T result = _core.GetResult(token);
            MemoryPool.Release(this);
            return result;
        }

        void IUniTaskSource.GetResult(short token)
        {
            GetResult(token);
        }

        public UniTaskStatus GetStatus(short token) => _core.GetStatus(token);

        public UniTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

        public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

        public override void Clear()
        {
            _core.Reset();
            _service = null;
            Next = null;
            CancellationToken = default;
        }
    }

    internal sealed class LeaseLoadPromise<T> : LoadWaiter, IUniTaskSource<AssetLease<T>> where T : Object
    {
        private UniTaskCompletionSourceCore<AssetLease<T>> _core;
        private ResourceService _service;

        internal short Version => _core.Version;

        internal static LeaseLoadPromise<T> Create(ResourceService service, CancellationToken cancellationToken)
        {
            LeaseLoadPromise<T> promise = MemoryPool.Acquire<LeaseLoadPromise<T>>();
            promise._service = service;
            promise.CancellationToken = cancellationToken;
            return promise;
        }

        internal override void Complete(int assetId)
        {
            _core.TrySetResult(_service.CreateLease<T>(assetId));
        }

        internal override void Abort()
        {
            _core.TrySetResult(default);
        }

        public AssetLease<T> GetResult(short token)
        {
            AssetLease<T> result = _core.GetResult(token);
            MemoryPool.Release(this);
            return result;
        }

        void IUniTaskSource.GetResult(short token)
        {
            GetResult(token);
        }

        public UniTaskStatus GetStatus(short token) => _core.GetStatus(token);

        public UniTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

        public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

        public override void Clear()
        {
            _core.Reset();
            _service = null;
            Next = null;
            CancellationToken = default;
        }
    }

    internal sealed class BindingLoadWaiter : LoadWaiter
    {
        private ResourceBindingService _bindings;
        private string _spriteName;
        private int _bindingIndex;
        private uint _bindingGeneration;
        private uint _version;
        private ResourceBindingOptions _options;

        internal static BindingLoadWaiter Create(ResourceBindingService bindings, int bindingIndex, uint bindingGeneration, uint version, string spriteName, ResourceBindingOptions options)
        {
            BindingLoadWaiter waiter = MemoryPool.Acquire<BindingLoadWaiter>();
            waiter._bindings = bindings;
            waiter._bindingIndex = bindingIndex;
            waiter._bindingGeneration = bindingGeneration;
            waiter._version = version;
            waiter._spriteName = spriteName;
            waiter._options = options;
            return waiter;
        }

        internal override void Complete(int assetId)
        {
            ResourceBindingService bindings = _bindings;
            int bindingIndex = _bindingIndex;
            uint bindingGeneration = _bindingGeneration;
            uint version = _version;
            string spriteName = _spriteName;
            ResourceBindingOptions options = _options;
            MemoryPool.Release(this);
            bindings.ApplyLoaded(bindingIndex, bindingGeneration, version, assetId, spriteName, options);
        }

        internal override void Abort()
        {
            MemoryPool.Release(this);
        }

        public override void Clear()
        {
            _bindings = null;
            _spriteName = null;
            Next = null;
        }
    }
}
