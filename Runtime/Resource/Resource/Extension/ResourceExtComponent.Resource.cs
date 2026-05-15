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
            public string Location { get; set; }
            public int Version { get; set; }

            public void Clear()
            {
                Location = string.Empty;
                Version = 0;
            }
        }

        private int _loadingVersion;
        private readonly Dictionary<int, LoadingState> _loadingStates = new Dictionary<int, LoadingState>(16);

        private void InitializedResources()
        {
            _resourceService = AppServices.App.Require<IResourceService>();
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
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            var target = setAssetObject.TargetObject;
            if (target == null)
            {
                _resourceService.UnloadAsset(assetObject);
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            int targetId = target.GetInstanceID();
            if (IsCurrentLocation(targetId, setAssetObject.Location))
            {
                ClearLoadingState(targetId);
                SetAsset(setAssetObject, assetObject);
            }
            else
            {
                _resourceService.UnloadAsset(assetObject);
                ReleaseSetAssetObject(setAssetObject);
            }
        }

        public async UniTask SetAssetByResources<T>(ISetAssetObject setAssetObject, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            var target = setAssetObject.TargetObject;
            var location = setAssetObject.Location;

            if (target == null)
            {
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            int targetId = target.GetInstanceID();

            if (HasPendingRequest(targetId, location))
            {
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            CancelAndCleanupOldRequest(targetId);

            if (HasCurrentTrackedAsset(target, location))
            {
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            var loadingState = MemoryPool.Acquire<LoadingState>();
            loadingState.Location = location;
            loadingState.Version = ++_loadingVersion;
            _loadingStates[targetId] = loadingState;

            if (!IsCurrentLoadingState(targetId, loadingState))
            {
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            if (typeof(T) == typeof(UnityEngine.Sprite) && TryUseSpriteKeepAliveAsset(location, out var keepAliveAsset))
            {
                ClearLoadingState(targetId, loadingState);
                SetAsset(setAssetObject, keepAliveAsset);
                return;
            }

            var loadResult = await _resourceService.LoadAssetAsync<T>(location, cancellationToken).SuppressCancellationThrow();
            if (loadResult.IsCanceled || loadResult.Result == null)
            {
                ClearLoadingState(targetId, loadingState);
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            if (!IsCurrentLoadingState(targetId, loadingState))
            {
                _resourceService.UnloadAsset(loadResult.Result);
                ReleaseSetAssetObject(setAssetObject);
                return;
            }

            ClearLoadingState(targetId, loadingState);
            SetAsset(setAssetObject, loadResult.Result);
        }

        private void CancelAndCleanupOldRequest(int targetId)
        {
            if (_loadingStates.TryGetValue(targetId, out var oldState))
            {
                _loadingStates.Remove(targetId);
                MemoryPool.Release(oldState);
            }
        }

        private void ClearLoadingState(int targetId)
        {
            if (_loadingStates.TryGetValue(targetId, out var state))
            {
                _loadingStates.Remove(targetId);
                MemoryPool.Release(state);
            }
        }

        private void ClearLoadingState(int targetId, LoadingState expectedState)
        {
            if (_loadingStates.TryGetValue(targetId, out var state) && ReferenceEquals(state, expectedState))
            {
                _loadingStates.Remove(targetId);
                MemoryPool.Release(state);
            }
        }

        private bool IsCurrentLocation(int targetId, string location)
        {
            return _loadingStates.TryGetValue(targetId, out var state) && state.Location == location;
        }

        private bool IsCurrentLoadingState(int targetId, LoadingState loadingState)
        {
            return loadingState != null &&
                   _loadingStates.TryGetValue(targetId, out var state) &&
                   ReferenceEquals(state, loadingState);
        }

        private bool HasPendingRequest(int targetId, string location)
        {
            return _loadingStates.TryGetValue(targetId, out var state) &&
                   state.Location == location;
        }

        private bool HasCurrentTrackedAsset(UnityEngine.Object target, string location)
        {
            if (target == null || _trackedAssetIndices == null || _loadAssetObjects == null)
            {
                return false;
            }

            if (!_trackedAssetIndices.TryGetValue(target, out int index) ||
                index < 0 ||
                index >= _loadAssetObjectCount)
            {
                return false;
            }

            LoadAssetObject item = _loadAssetObjects[index];
            return item?.AssetObject != null &&
                   item.AssetObject.Location == location &&
                   !item.AssetObject.IsCanRelease();
        }

        private void OnDestroy()
        {
            _isDestroying = true;
            UnityEngine.Application.lowMemory -= OnLowMemory;
            ReleaseTrackedAssets();
            ReleaseAllSpriteKeepAliveAssets();

            var enumerator = _loadingStates.GetEnumerator();
            while (enumerator.MoveNext())
            {
                MemoryPool.Release(enumerator.Current.Value);
            }

            _loadingStates.Clear();
        }
    }
}
