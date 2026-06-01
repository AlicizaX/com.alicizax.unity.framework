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
        /// Gets or sets the preheated capacity for Resource asset records.
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

        public int AssetLeaseCapacity
        {
            get => _assetLeaseCapacity;
            set
            {
                _assetLeaseCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity, _assetRecordCapacity);
            }
        }

        public int BindingOwnerCapacity
        {
            get => _bindingOwnerCapacity;
            set
            {
                _bindingOwnerCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        public int BindingSlotCapacity
        {
            get => _bindingSlotCapacity;
            set
            {
                _bindingSlotCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

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
        /// Gets or sets the seconds an unreferenced asset handle stays in Idle before release.
        /// </summary>
        public float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        /// <summary>
        /// Releases one legacy direct asset reference.
        /// </summary>
        /// <param name="asset">The asset to unload.</param>
        public void UnloadAsset(object asset)
        {
            TryReleaseLegacyDirectByAsset(asset);
        }
    }
}
