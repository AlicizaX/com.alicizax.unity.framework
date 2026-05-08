using System.Collections.Generic;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;

namespace AlicizaX.Resource.Runtime
{
    public partial class ResourceExtComponent
    {
        private static IResourceService _resourceService;

        public static IResourceService ResourceService => _resourceService;

        private class LoadingState : IMemory
        {
            public CancellationTokenSource Cts { get; set; }
            public CancellationTokenRegistration Registration { get; set; }
            public string Location { get; set; }

            public void Clear()
            {
                Registration.Dispose();
                Registration = default;

                if (Cts != null)
                {
                    Cts.Cancel();
                    Cts.Dispose();
                    Cts = null;
                }

                Location = string.Empty;
            }
        }

        private readonly Dictionary<UnityEngine.Object, LoadingState> _loadingStates = new Dictionary<UnityEngine.Object, LoadingState>();

        private void InitializedResources()
        {
            _resourceService = AppServices.Require<IResourceService>();
        }

        private void OnLoadAssetSuccess(string assetName, object asset, float duration, object userdata)
        {
            ISetAssetObject setAssetObject = (ISetAssetObject)userdata;
            if (setAssetObject == null)
            {
                return;
            }

            UnityEngine.Object assetObject = asset as UnityEngine.Object;
                if (assetObject == null)
                {
                Log.Error(ZString.Format("Load failure asset type is {0}.", asset?.GetType()));
                return;
            }

            if (IsCurrentLocation(setAssetObject.TargetObject, setAssetObject.Location))
            {
                ClearLoadingState(setAssetObject.TargetObject);
                SetAsset(setAssetObject, TrackLoadedAsset(setAssetObject.Location, assetObject));
            }
            else
            {
                _resourceService.UnloadAsset(assetObject);
            }
        }

        public async UniTask SetAssetByResources<T>(ISetAssetObject setAssetObject, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            var target = setAssetObject.TargetObject;
            var location = setAssetObject.Location;

            if (target == null)
            {
                return;
            }

            CancelAndCleanupOldRequest(target);

            var loadingState = MemoryPool.Acquire<LoadingState>();
            CancellationToken loadToken = cancellationToken;
            if (cancellationToken.CanBeCanceled)
            {
                var linkedTokenSource = new CancellationTokenSource();
                loadingState.Cts = linkedTokenSource;
                loadingState.Registration = cancellationToken.Register(static state =>
                {
                    ((CancellationTokenSource)state).Cancel();
                }, linkedTokenSource);
                loadToken = linkedTokenSource.Token;
            }

            loadingState.Location = location;
            _loadingStates[target] = loadingState;

            if (!IsCurrentLocation(target, location))
            {
                return;
            }

            if (_assetItemPool.CanSpawn(location))
            {
                ClearLoadingState(target);

                var assetObject = (T)_assetItemPool.Spawn(location).Target;
                SetAsset(setAssetObject, assetObject);
                return;
            }

            if (!IsCurrentLocation(target, location))
            {
                return;
            }

            var loadResult = await _resourceService.LoadAssetAsync<T>(location, loadToken).SuppressCancellationThrow();
            if (loadResult.IsCanceled || loadResult.Result == null)
            {
                ClearLoadingState(target);
                return;
            }

            OnLoadAssetSuccess(location, loadResult.Result, 0f, setAssetObject);
        }
        private void CancelAndCleanupOldRequest(UnityEngine.Object target)
        {
            if (_loadingStates.TryGetValue(target, out var oldState))
            {
                MemoryPool.Release(oldState);
                _loadingStates.Remove(target);
            }
        }

        private void ClearLoadingState(UnityEngine.Object target)
        {
            if (_loadingStates.TryGetValue(target, out var state))
            {
                MemoryPool.Release(state);
                _loadingStates.Remove(target);
            }
        }

        private bool IsCurrentLocation(UnityEngine.Object target, string location)
        {
            if (target == null)
            {
                return false;
            }

            return _loadingStates.TryGetValue(target, out var state) && state.Location == location;
        }

        private UnityEngine.Object TrackLoadedAsset(string location, UnityEngine.Object assetObject)
        {
            if (_assetItemPool.CanSpawn(location))
            {
                var cachedAsset = _assetItemPool.Spawn(location).Target as UnityEngine.Object;
                _resourceService.UnloadAsset(assetObject);
                return cachedAsset;
            }

            _assetItemPool.Register(AssetItemObject.Create(location, assetObject), true);
            return assetObject;
        }

        private void OnDestroy()
        {
            UnityEngine.Application.lowMemory -= OnLowMemory;
            ReleaseTrackedAssets();

            var enumerator = _loadingStates.GetEnumerator();
            while (enumerator.MoveNext())
            {
                MemoryPool.Release(enumerator.Current.Value);
            }

            _loadingStates.Clear();
        }
    }
}
