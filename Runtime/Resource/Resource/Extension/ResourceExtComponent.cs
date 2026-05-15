using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 资源组件拓展。
    /// </summary>
    [DisallowMultipleComponent]
    public partial class ResourceExtComponent : MonoBehaviour
    {
        public static ResourceExtComponent Instance { private set; get; }

        [SerializeField]
        private float checkCanReleaseInterval = 30f;

        [SerializeField]
        private int maxProcessPerFrame = 50;

        [SerializeField]
        private int releaseCheckThreshold = 16;

        [SerializeField]
        private int spriteKeepAliveCapacity = 64;

        [SerializeField]
        private float spriteKeepAliveExpireTime = 120f;

        private LoadAssetObject[] _loadAssetObjects;
        private int _loadAssetObjectCount;
        private int _currentProcessIndex;
        private Dictionary<Object, int> _trackedAssetIndices;
        private bool _releaseRequested;
        private float _nextReleaseCheckTime = float.MaxValue;
        private Dictionary<string, SpriteKeepAliveItem> _spriteKeepAliveCache;
        private SpriteKeepAliveItem _spriteKeepAliveHead;
        private SpriteKeepAliveItem _spriteKeepAliveTail;
        private bool _isDestroying;

        private sealed class SpriteKeepAliveItem : IMemory
        {
            public string Location;
            public Object Asset;
            public float LastUseTime;
            public SpriteKeepAliveItem Prev;
            public SpriteKeepAliveItem Next;

            public void Clear()
            {
                Location = null;
                Asset = null;
                LastUseTime = 0f;
                Prev = null;
                Next = null;
            }
        }

        private static readonly WaitForEndOfFrame CachedWaitForEndOfFrame = new WaitForEndOfFrame();

        private IEnumerator Start()
        {
            Instance = this;
            enabled = false;
            Application.lowMemory += OnLowMemory;
            yield return CachedWaitForEndOfFrame;
            _loadAssetObjects = new LoadAssetObject[16];
            _trackedAssetIndices = new Dictionary<Object, int>(16);
            EnsureSpriteKeepAliveCache();

            InitializedResources();
        }

        private void Update()
        {
            if (!_releaseRequested)
            {
                enabled = false;
                return;
            }

            if (Time.unscaledTime < _nextReleaseCheckTime)
            {
                return;
            }

            ReleaseUnused();
        }

        public void ReleaseUnused()
        {
            if (_loadAssetObjectCount == 0)
            {
                _currentProcessIndex = 0;
                ReleaseExpiredSpriteKeepAliveAssets();
                CompleteReleaseSweepIfIdle();
                return;
            }

            int processedCount = 0;
            while (_currentProcessIndex < _loadAssetObjectCount && processedCount < maxProcessPerFrame)
            {
                LoadAssetObject item = _loadAssetObjects[_currentProcessIndex];
                if (item.AssetObject.IsCanRelease())
                {
                    RemoveTrackedAt(_currentProcessIndex, releaseAsset: true);
                }
                else
                {
                    _currentProcessIndex++;
                }

                processedCount++;
            }

            if (_currentProcessIndex >= _loadAssetObjectCount)
            {
                _currentProcessIndex = 0;
                ReleaseExpiredSpriteKeepAliveAssets();
                CompleteReleaseSweepIfIdle();
            }
            else
            {
                ScheduleReleaseSweep(checkCanReleaseInterval);
            }
        }

        private void SetAsset(ISetAssetObject setAssetObject, Object assetObject)
        {
            ReplaceTrackedAsset(setAssetObject.TargetObject);
            EnsureTrackedCapacity(_loadAssetObjectCount + 1);

            int index = _loadAssetObjectCount++;
            _loadAssetObjects[index] = LoadAssetObject.Create(setAssetObject, assetObject);
            if (setAssetObject.TargetObject != null)
            {
                _trackedAssetIndices[setAssetObject.TargetObject] = index;
            }

            setAssetObject.SetAsset(assetObject);
            if (_loadAssetObjectCount >= releaseCheckThreshold)
            {
                ScheduleReleaseSweep();
            }
        }

        private void ReplaceTrackedAsset(Object target)
        {
            if (target == null || _trackedAssetIndices == null)
            {
                return;
            }

            if (_trackedAssetIndices.TryGetValue(target, out int existingIndex))
            {
                RemoveTrackedAt(existingIndex, releaseAsset: true);
            }
        }

        private void RemoveTrackedAt(int index, bool releaseAsset)
        {
            if (index < 0 || index >= _loadAssetObjectCount)
            {
                return;
            }

            LoadAssetObject item = _loadAssetObjects[index];
            var trackedObject = item.AssetObject?.TargetObject;
            if (trackedObject != null && _trackedAssetIndices != null)
            {
                _trackedAssetIndices.Remove(trackedObject);
            }

            if (releaseAsset && item.AssetTarget != null)
            {
                if (!TryKeepAliveSpriteAsset(item))
                {
                    _resourceService?.UnloadAsset(item.AssetTarget);
                }
            }

            if (item.AssetObject != null)
            {
                ReleaseSetAssetObject(item.AssetObject);
            }

            MemoryPool.Release(item);

            int lastIndex = _loadAssetObjectCount - 1;
            if (index != lastIndex)
            {
                LoadAssetObject lastItem = _loadAssetObjects[lastIndex];
                _loadAssetObjects[index] = lastItem;
                Object movedTarget = lastItem.AssetObject?.TargetObject;
                if (movedTarget != null && _trackedAssetIndices != null)
                {
                    _trackedAssetIndices[movedTarget] = index;
                }
            }

            _loadAssetObjects[lastIndex] = null;
            _loadAssetObjectCount = lastIndex;
            if (_currentProcessIndex > index)
            {
                _currentProcessIndex--;
            }

            if (_loadAssetObjectCount > 0)
            {
                ScheduleReleaseSweep(checkCanReleaseInterval);
            }
        }


        private static void ReleaseSetAssetObject(ISetAssetObject assetObject)
        {
            if (assetObject is SetSpriteObject setSpriteObject)
            {
                MemoryPool.Release(setSpriteObject);
            }
        }
        private void ReleaseTrackedAssets()
        {
            for (int i = _loadAssetObjectCount - 1; i >= 0; i--)
            {
                RemoveTrackedAt(i, releaseAsset: true);
            }

            _currentProcessIndex = 0;
            _trackedAssetIndices?.Clear();
            _releaseRequested = false;
            _nextReleaseCheckTime = float.MaxValue;
            enabled = false;
        }

        private void EnsureTrackedCapacity(int capacity)
        {
            if (_loadAssetObjects.Length >= capacity)
            {
                return;
            }

            int newCapacity = _loadAssetObjects.Length << 1;
            while (newCapacity < capacity)
            {
                newCapacity <<= 1;
            }

            Array.Resize(ref _loadAssetObjects, newCapacity);
        }

        private void ScheduleReleaseSweep(float delay = 0f)
        {
            _releaseRequested = true;
            float dueTime = Time.unscaledTime + Math.Max(0f, delay);
            _nextReleaseCheckTime = Math.Min(_nextReleaseCheckTime, dueTime);
            enabled = true;
        }

        private void OnLowMemory()
        {
            ReleaseAllSpriteKeepAliveAssets();
            ScheduleReleaseSweep();
        }

        private bool TryUseSpriteKeepAliveAsset(string location, out Object assetObject)
        {
            assetObject = null;
            if (string.IsNullOrEmpty(location) || _spriteKeepAliveCache == null)
            {
                return false;
            }

            if (!_spriteKeepAliveCache.TryGetValue(location, out var item))
            {
                return false;
            }

            RemoveSpriteKeepAliveNode(item);
            _spriteKeepAliveCache.Remove(location);
            assetObject = item.Asset;
            item.Asset = null;
            MemoryPool.Release(item);
            return assetObject != null;
        }

        private bool TryKeepAliveSpriteAsset(LoadAssetObject item)
        {
            if (_isDestroying || spriteKeepAliveCapacity <= 0 || item.AssetTarget is not Sprite)
            {
                return false;
            }

            string location = item.AssetObject?.Location;
            if (string.IsNullOrEmpty(location))
            {
                return false;
            }

            EnsureSpriteKeepAliveCache();
            if (_spriteKeepAliveCache.TryGetValue(location, out var existingItem))
            {
                MoveSpriteKeepAliveNodeToHead(existingItem);
                existingItem.LastUseTime = Time.unscaledTime;
                _resourceService?.UnloadAsset(item.AssetTarget);
                ScheduleReleaseSweep(spriteKeepAliveExpireTime);
                return true;
            }

            var keepAliveItem = MemoryPool.Acquire<SpriteKeepAliveItem>();
            keepAliveItem.Location = location;
            keepAliveItem.Asset = item.AssetTarget;
            keepAliveItem.LastUseTime = Time.unscaledTime;
            _spriteKeepAliveCache[location] = keepAliveItem;
            AddSpriteKeepAliveNodeToHead(keepAliveItem);

            TrimSpriteKeepAliveCapacity();
            ScheduleReleaseSweep(spriteKeepAliveExpireTime);
            return true;
        }

        private void TrimSpriteKeepAliveCapacity()
        {
            while (_spriteKeepAliveCache.Count > spriteKeepAliveCapacity)
            {
                if (_spriteKeepAliveTail == null)
                {
                    return;
                }

                ReleaseSpriteKeepAliveAsset(_spriteKeepAliveTail);
            }
        }

        private void ReleaseExpiredSpriteKeepAliveAssets()
        {
            if (_spriteKeepAliveCache == null || _spriteKeepAliveCache.Count == 0)
            {
                return;
            }

            float expireBefore = Time.unscaledTime - Math.Max(0f, spriteKeepAliveExpireTime);
            while (_spriteKeepAliveTail != null && _spriteKeepAliveTail.LastUseTime <= expireBefore)
            {
                ReleaseSpriteKeepAliveAsset(_spriteKeepAliveTail);
            }
        }

        private void ReleaseAllSpriteKeepAliveAssets()
        {
            if (_spriteKeepAliveCache == null || _spriteKeepAliveCache.Count == 0)
            {
                return;
            }

            SpriteKeepAliveItem current = _spriteKeepAliveHead;
            while (current != null)
            {
                SpriteKeepAliveItem next = current.Next;
                if (current.Asset != null)
                {
                    _resourceService?.UnloadAsset(current.Asset);
                }

                MemoryPool.Release(current);
                current = next;
            }

            _spriteKeepAliveCache.Clear();
            _spriteKeepAliveHead = null;
            _spriteKeepAliveTail = null;
        }

        private void ReleaseSpriteKeepAliveAsset(string location)
        {
            if (!_spriteKeepAliveCache.TryGetValue(location, out var item))
            {
                return;
            }

            ReleaseSpriteKeepAliveAsset(item);
        }

        private void ReleaseSpriteKeepAliveAsset(SpriteKeepAliveItem item)
        {
            if (item == null)
            {
                return;
            }

            _spriteKeepAliveCache.Remove(item.Location);
            RemoveSpriteKeepAliveNode(item);
            if (item.Asset != null)
            {
                _resourceService?.UnloadAsset(item.Asset);
            }

            MemoryPool.Release(item);
        }

        private void EnsureSpriteKeepAliveCache()
        {
            if (_spriteKeepAliveCache == null)
            {
                _spriteKeepAliveCache = new Dictionary<string, SpriteKeepAliveItem>(Math.Max(1, spriteKeepAliveCapacity));
            }
        }

        private void AddSpriteKeepAliveNodeToHead(SpriteKeepAliveItem item)
        {
            item.Prev = null;
            item.Next = _spriteKeepAliveHead;
            if (_spriteKeepAliveHead != null)
            {
                _spriteKeepAliveHead.Prev = item;
            }
            else
            {
                _spriteKeepAliveTail = item;
            }

            _spriteKeepAliveHead = item;
        }

        private void MoveSpriteKeepAliveNodeToHead(SpriteKeepAliveItem item)
        {
            if (ReferenceEquals(_spriteKeepAliveHead, item))
            {
                return;
            }

            RemoveSpriteKeepAliveNode(item);
            AddSpriteKeepAliveNodeToHead(item);
        }

        private void RemoveSpriteKeepAliveNode(SpriteKeepAliveItem item)
        {
            if (item.Prev != null)
            {
                item.Prev.Next = item.Next;
            }
            else if (ReferenceEquals(_spriteKeepAliveHead, item))
            {
                _spriteKeepAliveHead = item.Next;
            }

            if (item.Next != null)
            {
                item.Next.Prev = item.Prev;
            }
            else if (ReferenceEquals(_spriteKeepAliveTail, item))
            {
                _spriteKeepAliveTail = item.Prev;
            }

            item.Prev = null;
            item.Next = null;
        }

        private void CompleteReleaseSweepIfIdle()
        {
            if (_loadAssetObjectCount > 0)
            {
                ScheduleReleaseSweep(checkCanReleaseInterval);
                return;
            }

            if (_spriteKeepAliveCache != null && _spriteKeepAliveCache.Count > 0)
            {
                ScheduleReleaseSweep(Math.Max(1f, Math.Min(checkCanReleaseInterval, spriteKeepAliveExpireTime)));
                return;
            }

            _releaseRequested = false;
            _nextReleaseCheckTime = float.MaxValue;
            enabled = false;
        }
    }
}
