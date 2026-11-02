using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        private struct AssetRecord
        {
            public Object Asset;
            public HandleBase Handle;
            public LoadWaiter Waiters;
            public int RefCount;
            public int IdlePrevious;
            public int IdleNext;
            public int LoadingIndex;
            public float IdleSince;
            public ResourceAssetState State;
        }

        private readonly ResourceSlotTable<AssetRecord> _records = new ResourceSlotTable<AssetRecord>();

        internal int KeyId<T>(string location, string packageName) where T : Object
        {
            return AssetId(PackageId(packageName), location, ResourceTypeId<T>.Value, typeof(T), false);
        }

        internal int SubAssetsKeyId(string location, string packageName)
        {
            return AssetId(PackageId(packageName), location, ResourceTypeId<Sprite>.Value, typeof(Sprite), true);
        }

        private int PackageId(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ? _defaultPackageId : _keys.PackageId(packageName);
        }

        private int AssetId(int packageId, string location, int typeId, Type assetType, bool subAssets)
        {
            int assetId = _keys.AssetId(packageId, location, typeId, assetType, subAssets);
            if (assetId == _records.Count)
                _records.Allocate();
            return assetId;
        }

        private ref AssetRecord Record(int assetId) => ref _records[assetId];

        internal Object AssetOf(int assetId) => _records[assetId].Asset;

        internal T AssetOf<T>(int assetId) where T : Object => Unsafe.As<T>(_records[assetId].Asset);

        internal Sprite SubSpriteOf(int assetId, string spriteName)
        {
            return ((SubAssetsHandle)_records[assetId].Handle).GetSubAssetObject<Sprite>(spriteName);
        }

        internal bool AcquireLoaded(int assetId)
        {
            ref AssetRecord record = ref Record(assetId);
            switch (record.State)
            {
                case ResourceAssetState.Active:
                    record.RefCount++;
                    _hitCount++;
                    return true;
                case ResourceAssetState.Idle:
                    _idleHitCount++;
                    RemoveIdle(assetId, ref record);
                    record.State = ResourceAssetState.Active;
                    record.RefCount = 1;
                    return true;
                default:
                    return false;
            }
        }

        internal bool AddReference(int assetId)
        {
            ref AssetRecord record = ref Record(assetId);
            if (record.State != ResourceAssetState.Active)
                return false;
            record.RefCount++;
            return true;
        }

        internal void RemoveReference(int assetId)
        {
            ref AssetRecord record = ref Record(assetId);
            if (--record.RefCount == 0)
                AddIdle(assetId, ref record);
        }

        private static void ReleaseStorage(ref AssetRecord record)
        {
            HandleBase handle = record.Handle;
            record.Asset = null;
            record.Handle = null;
            record.RefCount = 0;
            record.State = ResourceAssetState.Released;
            handle.Release();
        }

        private void ReleaseAllStorage()
        {
            for (int assetId = 1; assetId < _records.Count; assetId++)
            {
                ref AssetRecord record = ref Record(assetId);
                if (record.State == ResourceAssetState.Active || record.State == ResourceAssetState.Idle)
                    ReleaseStorage(ref record);
            }

            _idleHead = -1;
            _idleTail = -1;
            _idleCount = 0;
        }
    }
}
