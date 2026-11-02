using System.Collections.Generic;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        private float _idleAssetExpireTime = 60f;
        private int _idleAssetCapacity = 256;
        private int _idleReleaseCountPerTick = 16;
        private int _idleHead = -1;
        private int _idleTail = -1;
        private int _idleCount;

        public float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        public int IdleAssetCapacity
        {
            get => _idleAssetCapacity;
            set
            {
                _idleAssetCapacity = value < 0 ? 0 : value;
                while (_idleCount > _idleAssetCapacity)
                    ReleaseIdleHead();
            }
        }

        public int IdleReleaseCountPerTick
        {
            get => _idleReleaseCountPerTick;
            set => _idleReleaseCountPerTick = value < 0 ? 0 : value;
        }

        private void AddIdle(int assetId, ref AssetRecord record)
        {
            record.State = ResourceAssetState.Idle;
            record.IdleSince = _now;
            record.IdlePrevious = _idleTail;
            record.IdleNext = -1;
            if (_idleTail >= 0)
                Record(_idleTail).IdleNext = assetId;
            else
                _idleHead = assetId;
            _idleTail = assetId;
            if (++_idleCount > _idleAssetCapacity)
                ReleaseIdleHead();
        }

        private void RemoveIdle(int assetId, ref AssetRecord record)
        {
            if (record.IdlePrevious >= 0)
                Record(record.IdlePrevious).IdleNext = record.IdleNext;
            else
                _idleHead = record.IdleNext;
            if (record.IdleNext >= 0)
                Record(record.IdleNext).IdlePrevious = record.IdlePrevious;
            else
                _idleTail = record.IdlePrevious;
            _idleCount--;
        }

        private void ReleaseIdleHead()
        {
            int assetId = _idleHead;
            ref AssetRecord record = ref Record(assetId);
            RemoveIdle(assetId, ref record);
            ReleaseStorage(ref record);
        }

        private void ReleaseExpiredIdleAssets()
        {
            for (int released = 0; released < _idleReleaseCountPerTick && _idleHead >= 0; released++)
            {
                if (_now - Record(_idleHead).IdleSince < _idleAssetExpireTime)
                    return;
                ReleaseIdleHead();
            }
        }

        public void UnloadUnusedAssets(bool releaseIdleAssets)
        {
            if (releaseIdleAssets)
            {
                while (_idleHead >= 0)
                    ReleaseIdleHead();
            }

            RemoveCompletedOperations(_unloadUnusedAssetsOperations);
            if (_unloadUnusedAssetsOperations.Count > 0)
                return;

            foreach (KeyValuePair<string, ResourcePackage> pair in _packages)
            {
                if (pair.Value is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true } package)
                    _unloadUnusedAssetsOperations.Add(package.UnloadUnusedAssetsAsync());
            }
        }

        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        public void SetForceUnloadUnusedAssetsAction(System.Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        public void OnLowMemory()
        {
            Log.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }
    }
}
