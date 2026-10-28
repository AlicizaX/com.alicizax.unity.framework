namespace AlicizaX.Resource.Runtime
{
    internal partial class ResourceService
    {
        private float _idleAssetExpireTime = 60f;
        private int _idleAssetCapacity = 256;

        public int IdleAssetCapacity
        {
            get => _idleAssetCapacity;
            set
            {
                _idleAssetCapacity = value < 0 ? 0 : value;
                while (_idleCount > _idleAssetCapacity)
                    ReleaseAssetStorage(_idleHead);
            }
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
