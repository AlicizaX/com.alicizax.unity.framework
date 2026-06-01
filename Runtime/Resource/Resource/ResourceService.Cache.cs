namespace AlicizaX.Resource.Runtime
{
    internal partial class ResourceService
    {
        private int _assetRecordCapacity = 64;
        private int _assetLeaseCapacity = 128;
        private int _bindingOwnerCapacity = 64;
        private int _bindingSlotCapacity = 128;
        private int _registeredTargetCapacity = 128;
        private float _idleAssetExpireTime = 60f;

        /// <summary>
        /// 获取或设置资源记录的预热容量。
        /// </summary>
        public int AssetRecordCapacity
        {
            get => _assetRecordCapacity;
            set
            {
                _assetRecordCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity, _assetRecordCapacity);
            }
        }

        /// <summary>
        /// 获取或设置资源租约记录的预热容量。
        /// </summary>
        public int AssetLeaseCapacity
        {
            get => _assetLeaseCapacity;
            set
            {
                _assetLeaseCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity, _assetRecordCapacity);
            }
        }

        /// <summary>
        /// 获取或设置绑定所有者记录的预热容量。
        /// </summary>
        public int BindingOwnerCapacity
        {
            get => _bindingOwnerCapacity;
            set
            {
                _bindingOwnerCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <summary>
        /// 获取或设置绑定槽位记录的预热容量。
        /// </summary>
        public int BindingSlotCapacity
        {
            get => _bindingSlotCapacity;
            set
            {
                _bindingSlotCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <summary>
        /// 获取或设置已注册目标组件索引的预热容量。
        /// </summary>
        public int RegisteredTargetCapacity
        {
            get => _registeredTargetCapacity;
            set
            {
                _registeredTargetCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        private void WarmupBindingRecords()
        {
            _bindingService?.Warmup(_bindingOwnerCapacity, _bindingSlotCapacity, _registeredTargetCapacity);
            ResourceOwner.WarmupReleaseBuffer(_bindingOwnerCapacity);
        }

        /// <summary>
        /// 获取或设置无引用资源句柄在空闲状态保留的秒数，超时后释放。
        /// </summary>
        public float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        /// <summary>
        /// 释放一个旧式直接资源引用。
        /// </summary>
        /// <param name="asset">要卸载的资源。</param>
        public void UnloadAsset(object asset)
        {
            TryReleaseLegacyDirectByAsset(asset);
        }
    }
}
