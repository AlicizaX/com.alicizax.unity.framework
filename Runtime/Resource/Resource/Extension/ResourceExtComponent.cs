using System;
using System.Collections;
using System.Collections.Generic;
using AlicizaX.ObjectPool;
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
        private float autoReleaseInterval = 60f;

        [SerializeField]
        private int maxProcessPerFrame = 50;

        [SerializeField]
        private int releaseCheckThreshold = 16;

        private LoadAssetObject[] _loadAssetObjects;
        private int _loadAssetObjectCount;
        private int _currentProcessIndex;
        private Dictionary<Object, int> _trackedAssetIndices;
        private bool _releaseRequested;
        private float _nextReleaseCheckTime = float.MaxValue;

        private IObjectPool<AssetItemObject> _assetItemPool;

        private IEnumerator Start()
        {
            Instance = this;
            enabled = false;
            Application.lowMemory += OnLowMemory;
            yield return new WaitForEndOfFrame();
            IObjectPoolService objectPoolComponent = AppServices.Require<IObjectPoolService>();
            _assetItemPool = objectPoolComponent.CreatePool<AssetItemObject>(
                new ObjectPoolCreateOptions(
                    name: "SetAssetPool",
                    allowMultiSpawn: true,
                    autoReleaseInterval: autoReleaseInterval,
                    capacity: 16,
                    expireTime: 60,
                    priority: 0));
            _loadAssetObjects = new LoadAssetObject[16];
            _trackedAssetIndices = new Dictionary<Object, int>(16);

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
                _releaseRequested = false;
                _nextReleaseCheckTime = float.MaxValue;
                enabled = false;
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
                _releaseRequested = false;
                _nextReleaseCheckTime = float.MaxValue;
                enabled = false;
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
                _resourceService?.UnloadAsset(item.AssetTarget);
                _assetItemPool?.Unspawn(item.AssetTarget);
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
            ScheduleReleaseSweep();
        }
    }
}