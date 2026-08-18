namespace AlicizaX.ObjectPool
{
    public readonly struct ObjectPoolCreateOptions
    {
        public readonly string Name;
        public readonly bool AllowMultiSpawn;
        public readonly float? AutoReleaseInterval;
        public readonly int? Capacity;
        public readonly float? ExpireTime;
        public readonly int Priority;

        public ObjectPoolCreateOptions(
            string name = "",
            bool allowMultiSpawn = false,
            float? autoReleaseInterval = null,
            int? capacity = null,
            float? expireTime = null,
            int priority = 0)
        {
            Name = name ?? string.Empty;
            AllowMultiSpawn = allowMultiSpawn;
            AutoReleaseInterval = autoReleaseInterval;
            Capacity = capacity;
            ExpireTime = expireTime;
            Priority = priority;
        }

    }

    public interface IObjectPoolService : IService
    {
        int Count { get; }

        bool HasObjectPool<T>(string name = "") where T : ObjectBase;
        IObjectPool<T> GetObjectPool<T>(string name = "") where T : ObjectBase;
        IObjectPool<T> GetOrCreatePool<T>(ObjectPoolCreateOptions options = default) where T : ObjectBase;
        bool DestroyObjectPool<T>(string name = "") where T : ObjectBase;

        void Release();
        void ReleaseAllUnused();
    }
}
