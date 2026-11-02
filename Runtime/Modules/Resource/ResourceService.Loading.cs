using System;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        private int[] _loadingAssetIds = new int[16];
        private int[] _finishedAssetIds = new int[16];
        private int _loadingCount;
        private int _cancelableWaiterCount;
        private long _hitCount;
        private long _idleHitCount;
        private long _joinCount;
        private long _missCount;

        public AssetLease<T> LoadLease<T>(AssetRef<T> key) where T : Object
        {
            return LoadLease<T>(key.Id);
        }

        public AssetLease<T> LoadLease<T>(string location, string packageName = null) where T : Object
        {
            return LoadLease<T>(KeyId<T>(location, packageName));
        }

        public UniTask<AssetLease<T>> LoadLeaseAsync<T>(AssetRef<T> key, CancellationToken cancellationToken = default) where T : Object
        {
            return LoadLeaseAsync<T>(key.Id, cancellationToken);
        }

        public UniTask<AssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = null) where T : Object
        {
            return LoadLeaseAsync<T>(KeyId<T>(location, packageName), cancellationToken);
        }

        private AssetLease<T> LoadLease<T>(int assetId) where T : Object
        {
            return _state == ServiceState.Running && AcquireSync(assetId) ? CreateLease<T>(assetId) : default;
        }

        private UniTask<AssetLease<T>> LoadLeaseAsync<T>(int assetId, CancellationToken cancellationToken) where T : Object
        {
            if (cancellationToken.IsCancellationRequested || _state != ServiceState.Running)
                return default;
            if (AcquireLoaded(assetId))
                return new UniTask<AssetLease<T>>(CreateLease<T>(assetId));
            if (!StartLoading(assetId))
                return default;
            LeaseLoadPromise<T> promise = LeaseLoadPromise<T>.Create(this, cancellationToken);
            AddWaiter(assetId, promise);
            return new UniTask<AssetLease<T>>(promise, promise.Version);
        }

        internal T LoadIntoScope<T>(in ResourceScope scope, int assetId) where T : Object
        {
            if (_state != ServiceState.Running || !IsScopeAlive(scope.Index, scope.Generation))
                return null;
            if (_scopeEntryIds.TryGetValue(ScopeEntryKey(scope.Index, assetId), out _))
            {
                _hitCount++;
                return AssetOf<T>(assetId);
            }
            return AcquireSync(assetId) && AttachToScope(scope.Index, scope.Generation, assetId) ? AssetOf<T>(assetId) : null;
        }

        internal UniTask<T> LoadIntoScopeAsync<T>(in ResourceScope scope, int assetId, CancellationToken cancellationToken) where T : Object
        {
            if (cancellationToken.IsCancellationRequested || _state != ServiceState.Running || !IsScopeAlive(scope.Index, scope.Generation))
                return default;
            if (_scopeEntryIds.TryGetValue(ScopeEntryKey(scope.Index, assetId), out _))
            {
                _hitCount++;
                return new UniTask<T>(AssetOf<T>(assetId));
            }
            if (AcquireLoaded(assetId))
            {
                AddScopeEntry(scope.Index, assetId);
                return new UniTask<T>(AssetOf<T>(assetId));
            }

            if (!StartLoading(assetId))
                return default;
            ScopeLoadPromise<T> promise = ScopeLoadPromise<T>.Create(this, scope, cancellationToken);
            AddWaiter(assetId, promise);
            return new UniTask<T>(promise, promise.Version);
        }

        internal bool AcquireSync(int assetId)
        {
            if (AcquireLoaded(assetId))
                return true;
            if (Record(assetId).State == ResourceAssetState.Released)
            {
                if (!Loader.IsAvailable)
                    return false;
                BeginLoad(assetId, true);
            }
            else
            {
                _joinCount++;
            }

            Loader.WaitForCompletion(Record(assetId).Handle);
            int resetEpoch = _resetEpoch;
            return CompleteLoad(assetId, true) && resetEpoch == _resetEpoch;
        }

        internal bool StartLoading(int assetId)
        {
            if (Record(assetId).State == ResourceAssetState.Loading)
            {
                _joinCount++;
                return true;
            }

            if (!Loader.IsAvailable)
                return false;
            BeginLoad(assetId, false);
            return true;
        }

        internal void AddWaiter(int assetId, LoadWaiter waiter)
        {
            ref AssetRecord record = ref Record(assetId);
            waiter.Next = record.Waiters;
            record.Waiters = waiter;
            if (waiter.CancellationToken.CanBeCanceled)
                _cancelableWaiterCount++;
        }

        private void BeginLoad(int assetId, bool synchronous)
        {
            _missCount++;
            ref ResourceAssetKey key = ref _keys[assetId];
            string packageName = _keys.PackageName(key.PackageId);
            string location = _keys.LocationName(key.LocationId);
            HandleBase handle = key.SubAssets
                ? Loader.LoadSubAssets(packageName, location, synchronous)
                : Loader.LoadAsset(packageName, location, key.AssetType, synchronous);

            ref AssetRecord record = ref Record(assetId);
            record.Handle = handle;
            record.State = ResourceAssetState.Loading;
            record.LoadingIndex = _loadingCount;
            if (_loadingCount == _loadingAssetIds.Length)
                Array.Resize(ref _loadingAssetIds, _loadingCount << 1);
            _loadingAssetIds[_loadingCount++] = assetId;
        }

        private bool CompleteLoad(int assetId, bool acquire)
        {
            ref AssetRecord record = ref Record(assetId);
            RemoveLoading(ref record);

            HandleBase handle = record.Handle;
            LoadWaiter waiters = record.Waiters;
            record.Waiters = null;
            int waiterCount = DetachWaiters(waiters);
            ref ResourceAssetKey key = ref _keys[assetId];
            Object asset = key.SubAssets ? null : ((AssetHandle)handle).AssetObject;
            if (handle.Status != EOperationStatus.Succeeded || !(key.SubAssets || key.AssetType.IsInstanceOfType(asset)))
            {
                Log.Warning(ZString.Format("Resource load failed. Location: {0}, Type: {1}, Error: {2}",
                    _keys.LocationName(key.LocationId), key.AssetType.Name, handle.Error));
                record.Handle = null;
                record.State = ResourceAssetState.Released;
                handle.Release();
                AbortWaiters(waiters);
                return false;
            }

            record.Asset = asset;
            int references = acquire ? waiterCount + 1 : waiterCount;
            if (references == 0)
            {
                AddIdle(assetId, ref record);
                return true;
            }

            record.State = ResourceAssetState.Active;
            record.RefCount = references;
            int resetEpoch = _resetEpoch;
            while (waiters != null)
            {
                LoadWaiter next = waiters.Next;
                waiters.Next = null;
                if (resetEpoch == _resetEpoch)
                    waiters.Complete(assetId);
                else
                    waiters.Abort();
                waiters = next;
            }

            return true;
        }

        private void RemoveLoading(ref AssetRecord record)
        {
            int lastAssetId = _loadingAssetIds[--_loadingCount];
            _loadingAssetIds[record.LoadingIndex] = lastAssetId;
            Record(lastAssetId).LoadingIndex = record.LoadingIndex;
        }

        private int DetachWaiters(LoadWaiter waiters)
        {
            int count = 0;
            for (LoadWaiter waiter = waiters; waiter != null; waiter = waiter.Next)
            {
                count++;
                if (waiter.CancellationToken.CanBeCanceled)
                    _cancelableWaiterCount--;
            }

            return count;
        }

        private static void AbortWaiters(LoadWaiter waiters)
        {
            while (waiters != null)
            {
                LoadWaiter next = waiters.Next;
                waiters.Next = null;
                waiters.Abort();
                waiters = next;
            }
        }

        private void CompleteFinishedLoads()
        {
            if (_finishedAssetIds.Length < _loadingCount)
                Array.Resize(ref _finishedAssetIds, _loadingAssetIds.Length);

            int finishedCount = 0;
            for (int i = 0; i < _loadingCount; i++)
            {
                int assetId = _loadingAssetIds[i];
                if (Record(assetId).Handle.IsDone)
                    _finishedAssetIds[finishedCount++] = assetId;
            }

            for (int i = 0; i < finishedCount; i++)
            {
                int assetId = _finishedAssetIds[i];
                ref AssetRecord record = ref Record(assetId);
                if (record.State == ResourceAssetState.Loading && record.Handle.IsDone)
                    CompleteLoad(assetId, false);
            }
        }

        private void AbortCanceledWaiters()
        {
            LoadWaiter canceled = null;
            for (int i = _loadingCount - 1; i >= 0; i--)
            {
                ref AssetRecord record = ref Record(_loadingAssetIds[i]);
                LoadWaiter previous = null;
                LoadWaiter waiter = record.Waiters;
                while (waiter != null)
                {
                    LoadWaiter next = waiter.Next;
                    if (waiter.CancellationToken.IsCancellationRequested)
                    {
                        if (previous == null)
                            record.Waiters = next;
                        else
                            previous.Next = next;
                        waiter.Next = canceled;
                        canceled = waiter;
                        _cancelableWaiterCount--;
                    }
                    else
                    {
                        previous = waiter;
                    }

                    waiter = next;
                }

                if (record.Waiters == null)
                {
                    HandleBase handle = record.Handle;
                    RemoveLoading(ref record);
                    record.Handle = null;
                    record.State = ResourceAssetState.Released;
                    handle.Release();
                }
            }

            AbortWaiters(canceled);
        }

        private LoadWaiter ReleaseAllLoading()
        {
            LoadWaiter waiters = null;
            for (int i = 0; i < _loadingCount; i++)
            {
                ref AssetRecord record = ref Record(_loadingAssetIds[i]);
                LoadWaiter chain = record.Waiters;
                while (chain != null)
                {
                    LoadWaiter next = chain.Next;
                    chain.Next = waiters;
                    waiters = chain;
                    chain = next;
                }

                HandleBase handle = record.Handle;
                record.Waiters = null;
                record.Handle = null;
                record.State = ResourceAssetState.Released;
                handle.Release();
            }

            _loadingCount = 0;
            _cancelableWaiterCount = 0;
            return waiters;
        }
    }
}
