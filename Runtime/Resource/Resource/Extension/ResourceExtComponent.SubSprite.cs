using System.Collections.Generic;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 资源组件拓展。
    /// </summary>
    public partial class ResourceExtComponent
    {
        private readonly Dictionary<string, SubAssetsHandle> _subAssetsHandles = new Dictionary<string, SubAssetsHandle>(8);
        private readonly Dictionary<string, int> _subSpriteReferences = new Dictionary<string, int>(8);
        private readonly Dictionary<string, SubAssetLoadingOperationState> _subAssetLoadingOperations = new Dictionary<string, SubAssetLoadingOperationState>(4);
        private readonly Dictionary<SubSpriteKey, Sprite> _subSpriteCache = new Dictionary<SubSpriteKey, Sprite>(32, SubSpriteKeyComparer.Instance);

        private readonly struct SubSpriteKey
        {
            public readonly string Location;
            public readonly string SpriteName;

            public SubSpriteKey(string location, string spriteName)
            {
                Location = location ?? string.Empty;
                SpriteName = spriteName ?? string.Empty;
            }
        }

        private sealed class SubSpriteKeyComparer : IEqualityComparer<SubSpriteKey>
        {
            public static readonly SubSpriteKeyComparer Instance = new SubSpriteKeyComparer();

            private SubSpriteKeyComparer()
            {
            }

            public bool Equals(SubSpriteKey x, SubSpriteKey y)
            {
                return string.Equals(x.Location, y.Location, System.StringComparison.Ordinal) &&
                       string.Equals(x.SpriteName, y.SpriteName, System.StringComparison.Ordinal);
            }

            public int GetHashCode(SubSpriteKey obj)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(obj.Location ?? string.Empty);
                    hash = hash * 31 + System.StringComparer.Ordinal.GetHashCode(obj.SpriteName ?? string.Empty);
                    return hash;
                }
            }
        }

        private sealed class SubAssetLoadingOperationState : IMemory
        {
            public bool IsDone { get; private set; }
            public SubAssetsHandle Handle { get; private set; }
            public int WaiterCount { get; private set; }
            public bool ReleaseRequested { get; private set; }

            public void AddWaiter()
            {
                WaiterCount++;
            }

            public void RemoveWaiter()
            {
                if (WaiterCount > 0)
                {
                    WaiterCount--;
                }
            }

            public void Complete(SubAssetsHandle handle)
            {
                IsDone = true;
                Handle = handle;
            }

            public void RequestRelease()
            {
                ReleaseRequested = true;
            }

            public void Clear()
            {
                IsDone = false;
                Handle = default;
                WaiterCount = 0;
                ReleaseRequested = false;
            }
        }

        public async UniTask SetSubSprite(Image image, string location, string spriteName, bool setNativeSize = false, CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                Log.Warning("SetSubAssets Image is null");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var subSprite = await GetSubSpriteImp(location, spriteName, cancellationToken);

            if (image == null || cancellationToken.IsCancellationRequested || subSprite == null)
            {
                ReleaseSubAssetsIfUnused(location);
                return;
            }

            image.sprite = subSprite;
            if (setNativeSize)
            {
                image.SetNativeSize();
            }

            AddReference(image.gameObject, location);
        }

        public async UniTask SetSubSprite(SpriteRenderer spriteRenderer, string location, string spriteName, CancellationToken cancellationToken = default)
        {
            if (spriteRenderer == null)
            {
                Log.Warning("SetSubAssets Image is null");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var subSprite = await GetSubSpriteImp(location, spriteName, cancellationToken);

            if (spriteRenderer == null || cancellationToken.IsCancellationRequested || subSprite == null)
            {
                ReleaseSubAssetsIfUnused(location);
                return;
            }

            spriteRenderer.sprite = subSprite;
            AddReference(spriteRenderer.gameObject, location);
        }

        private async UniTask<Sprite> GetSubSpriteImp(string location, string spriteName, CancellationToken cancellationToken = default)
        {
            var assetInfo = YooAssets.GetAssetInfo(location);
            if (assetInfo.IsInvalid)
            {
                Log.Error(ZString.Format("Invalid location: {0}", location));
                return null;
            }

            SubSpriteKey key = new SubSpriteKey(location, spriteName);
            if (_subSpriteCache.TryGetValue(key, out Sprite cachedSprite))
            {
                return cachedSprite;
            }

            var subAssetsHandle = await GetOrLoadSubAssetsHandleAsync(location, cancellationToken);
            if (!subAssetsHandle.IsValid)
            {
                return null;
            }

            var subSprite = subAssetsHandle.GetSubAssetObject<Sprite>(spriteName);
            if (subSprite == null)
            {
                Log.Error(ZString.Format("Invalid sprite name: {0} in {1}", spriteName, location));
                return null;
            }

            _subSpriteCache[key] = subSprite;
            return subSprite;
        }

        private void AddReference(GameObject target, string location)
        {
            var subSpriteReference = target.GetComponent<SubSpriteReference>();
            if (subSpriteReference == null)
            {
                subSpriteReference = target.AddComponent<SubSpriteReference>();
            }

            if (subSpriteReference.Reference(location))
            {
                _subSpriteReferences[location] = _subSpriteReferences.TryGetValue(location, out var count) ? count + 1 : 1;
            }
        }

        internal void DeleteReference(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return;
            }

            int nextCount = _subSpriteReferences.TryGetValue(location, out var count) ? count - 1 : 0;
            if (nextCount > 0)
            {
                _subSpriteReferences[location] = nextCount;
                return;
            }

            _subSpriteReferences.Remove(location);
            if (_subAssetsHandles.TryGetValue(location, out var subAssetsHandle))
            {
                subAssetsHandle.Dispose();
                _subAssetsHandles.Remove(location);
                ClearSubSpriteCache(location);
            }
        }

        private async UniTask<SubAssetsHandle> GetOrLoadSubAssetsHandleAsync(string location, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_subAssetsHandles.TryGetValue(location, out var cachedHandle))
                {
                    return cachedHandle;
                }

                if (_subAssetLoadingOperations.TryGetValue(location, out var loadingOperation))
                {
                    loadingOperation.AddWaiter();
                    try
                    {
                        while (!loadingOperation.IsDone)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                break;
                            }

                            await UniTask.Yield();
                        }
                    }
                    finally
                    {
                        loadingOperation.RemoveWaiter();
                        ReleaseSubAssetLoadingOperationIfReady(loadingOperation);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return default;
                    }

                    continue;
                }

                var loadingState = MemoryPool.Acquire<SubAssetLoadingOperationState>();
                _subAssetLoadingOperations.Add(location, loadingState);

                SubAssetsHandle subAssetsHandle = YooAssets.LoadSubAssetsAsync<Sprite>(location);
                while (subAssetsHandle is { IsValid: true, IsDone: false })
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        DisposeSubAssetsHandle(subAssetsHandle);
                        CompleteSubAssetLoading(location, loadingState, default);
                        return default;
                    }

                    await UniTask.Yield();
                }

                if (!subAssetsHandle.IsValid || subAssetsHandle.Status == EOperationStatus.Failed)
                {
                    DisposeSubAssetsHandle(subAssetsHandle);
                    CompleteSubAssetLoading(location, loadingState, default);
                    return default;
                }

                _subAssetsHandles[location] = subAssetsHandle;
                CompleteSubAssetLoading(location, loadingState, subAssetsHandle);
                return subAssetsHandle;
            }
        }

        private void CompleteSubAssetLoading(string location, SubAssetLoadingOperationState loadingState, SubAssetsHandle subAssetsHandle)
        {
            _subAssetLoadingOperations.Remove(location);
            loadingState.Complete(subAssetsHandle);
            loadingState.RequestRelease();
            ReleaseSubAssetLoadingOperationIfReady(loadingState);
        }

        private static void ReleaseSubAssetLoadingOperationIfReady(SubAssetLoadingOperationState loadingState)
        {
            if (loadingState is { ReleaseRequested: true, WaiterCount: 0 })
            {
                MemoryPool.Release(loadingState);
            }
        }

        private static void DisposeSubAssetsHandle(SubAssetsHandle subAssetsHandle)
        {
            if (subAssetsHandle.IsValid)
            {
                subAssetsHandle.Dispose();
            }
        }

        private void ClearSubSpriteCache(string location)
        {
            if (_subSpriteCache.Count == 0)
            {
                return;
            }

            bool found = true;
            while (found)
            {
                found = false;
                var enumerator = _subSpriteCache.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (string.Equals(enumerator.Current.Key.Location, location, System.StringComparison.Ordinal))
                    {
                        _subSpriteCache.Remove(enumerator.Current.Key);
                        found = true;
                        break;
                    }
                }
            }
        }

        private void ReleaseSubAssetsIfUnused(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return;
            }

            if (_subSpriteReferences.TryGetValue(location, out var count) && count > 0)
            {
                return;
            }

            if (_subAssetsHandles.TryGetValue(location, out var subAssetsHandle))
            {
                subAssetsHandle.Dispose();
                _subAssetsHandles.Remove(location);
                ClearSubSpriteCache(location);
            }
        }
    }

    [DisallowMultipleComponent]
    public class SubSpriteReference : MonoBehaviour
    {
        private string _location;

        public bool Reference(string location)
        {
            if (_location == location)
            {
                return false;
            }

            if (_location != null && _location != location)
            {
                ResourceExtComponent.Instance?.DeleteReference(_location);
            }

            _location = location;
            return true;
        }

        private void OnDestroy()
        {
            if (_location != null)
            {
                ResourceExtComponent.Instance?.DeleteReference(_location);
            }
        }
    }
}
