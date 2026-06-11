using System;
using UnityEngine;

namespace AlicizaX.ObjectPool
{

    [UnityEngine.Scripting.Preserve]
    internal sealed partial class ObjectPoolService : ServiceBase, IObjectPoolService, IServiceTickable
    {
        private const float DefaultAutoReleaseInterval = float.MaxValue;
        private const int DefaultCapacity = int.MaxValue;
        private const float DefaultExpireTime = float.MaxValue;
        private const int InitPoolArrayCapacity = 8;

        private TypeNamePairOpenHashMap m_PoolMap;
        private ReferenceOpenHashMap m_PoolRefMap;
        private ObjectPoolBase[] m_Pools;
        private int m_PoolCount;
        private ObjectPoolBase[] m_CachedSortedPools;
        private int m_CachedSortedCount;

        public ObjectPoolService()
        {
            m_PoolMap = new TypeNamePairOpenHashMap(InitPoolArrayCapacity);
            m_PoolRefMap = new ReferenceOpenHashMap(InitPoolArrayCapacity);
            m_Pools = new ObjectPoolBase[InitPoolArrayCapacity];
            m_PoolCount = 0;
            m_CachedSortedPools = Array.Empty<ObjectPoolBase>();
            m_CachedSortedCount = 0;
        }

        public int Priority => 1;
        public int Count => m_PoolMap.Count;

        void IServiceTickable.Tick(float deltaTime)
        {
            float unscaled = Time.unscaledDeltaTime;
            for (int i = 0; i < m_PoolCount; i++)
            {
                var pool = m_Pools[i];
                if (pool.IsActive)
                    pool.Update(deltaTime, unscaled);
            }
        }

        protected override void OnInitialize() { }

        protected override void OnDestroyService()
        {
            for (int i = m_PoolCount - 1; i >= 0; i--)
                m_Pools[i].Shutdown();
            m_PoolMap.Dispose();
            m_PoolRefMap.Dispose();
            Array.Clear(m_Pools, 0, m_PoolCount);
            Array.Clear(m_CachedSortedPools, 0, m_CachedSortedCount);
            m_PoolCount = 0;
            m_CachedSortedCount = 0;
        }

        // ========== Has ==========

        public bool HasObjectPool<T>() where T : ObjectBase
            => m_PoolMap.ContainsKey(new TypeNamePair(typeof(T)));

        public bool HasObjectPool<T>(string name) where T : ObjectBase
            => m_PoolMap.ContainsKey(new TypeNamePair(typeof(T), name));

        // ========== Get ==========

        public IObjectPool<T> GetObjectPool<T>() where T : ObjectBase
            => (IObjectPool<T>)InternalGet(new TypeNamePair(typeof(T)));

        public IObjectPool<T> GetObjectPool<T>(string name) where T : ObjectBase
            => (IObjectPool<T>)InternalGet(new TypeNamePair(typeof(T), name));

        // ========== GetAll ==========

        internal int GetAllObjectPools(bool sort, ObjectPoolBase[] results)
        {
            if (results == null)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogError("Results is invalid.");
#endif
                return 0;
            }

            if (sort)
            {
                CacheSortedObjectPools();
                int copyCount = results.Length < m_CachedSortedCount ? results.Length : m_CachedSortedCount;
                Array.Copy(m_CachedSortedPools, 0, results, 0, copyCount);
                return m_CachedSortedCount;
            }

            int count = m_PoolCount;
            int copy = results.Length < count ? results.Length : count;
            Array.Copy(m_Pools, 0, results, 0, copy);
            return count;
        }

        // ========== Create (single entry point) ==========

        public IObjectPool<T> CreatePool<T>(ObjectPoolCreateOptions options = default) where T : ObjectBase
        {
            var key = new TypeNamePair(typeof(T), options.Name);
            if (m_PoolMap.ContainsKey(key))
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogError($"Already exist object pool '{key}'.");
#endif
                return null;
            }

            var pool = new ObjectPool<T>(
                options.Name ?? string.Empty,
                options.AllowMultiSpawn,
                options.AutoReleaseInterval ?? DefaultAutoReleaseInterval,
                options.Capacity ?? DefaultCapacity,
                options.ExpireTime ?? DefaultExpireTime,
                options.Priority);

            int idx = m_PoolCount;
            if (idx >= m_Pools.Length)
            {
                var newArr = new ObjectPoolBase[m_Pools.Length * 2];
                Array.Copy(m_Pools, 0, newArr, 0, m_PoolCount);
                m_Pools = newArr;
            }
            m_Pools[idx] = pool;
            m_PoolCount++;
            m_PoolMap.AddOrUpdate(key, idx);
            m_PoolRefMap.AddOrUpdate(pool, idx);
            return pool;
        }

        // ========== Destroy ==========

        public bool DestroyObjectPool<T>() where T : ObjectBase
            => InternalDestroy(new TypeNamePair(typeof(T)));

        public bool DestroyObjectPool<T>(string name) where T : ObjectBase
            => InternalDestroy(new TypeNamePair(typeof(T), name));

        public bool DestroyObjectPool<T>(IObjectPool<T> objectPool) where T : ObjectBase
        {
            if (objectPool == null)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogError("Object pool is invalid.");
#endif
                return false;
            }
            if (!m_PoolRefMap.TryGetValue(objectPool, out int idx)
                || idx < 0
                || idx >= m_PoolCount
                || !ReferenceEquals(m_Pools[idx], objectPool))
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogError("Object pool is not registered in this service.");
#endif
                return false;
            }
            return InternalDestroy(new TypeNamePair(typeof(T), objectPool.Name));
        }

        // ========== Release ==========

        public void Release()
        {
            CacheSortedObjectPools();
            for (int i = 0; i < m_CachedSortedCount; i++)
                m_CachedSortedPools[i].Release();
        }

        public void ReleaseAllUnused()
        {
            CacheSortedObjectPools();
            for (int i = 0; i < m_CachedSortedCount; i++)
                m_CachedSortedPools[i].ReleaseAllUnused();
        }

        // ========== Low memory ==========

        public void OnLowMemory()
        {
            for (int i = 0; i < m_PoolCount; i++)
                m_Pools[i].OnLowMemory();
        }

        // ========== Internal ==========

        private ObjectPoolBase InternalGet(TypeNamePair key)
        {
            if (m_PoolMap.TryGetValue(key, out int idx))
                return m_Pools[idx];
            return null;
        }

        private bool InternalDestroy(TypeNamePair key)
        {
            if (!m_PoolMap.TryGetValue(key, out int idx))
                return false;

            var pool = m_Pools[idx];
            pool.Shutdown();

            int lastIndex = m_PoolCount - 1;
            if (idx < lastIndex)
            {
                var lastPool = m_Pools[lastIndex];
                m_Pools[idx] = lastPool;
                m_PoolRefMap.AddOrUpdate(lastPool, idx);
                var lastKey = new TypeNamePair(lastPool.ObjectType, lastPool.Name);
                m_PoolMap.AddOrUpdate(lastKey, idx);
            }
            m_Pools[lastIndex] = null;
            m_PoolCount--;

            m_PoolMap.Remove(key);
            m_PoolRefMap.Remove(pool);
            if (m_CachedSortedCount > 0)
                Array.Clear(m_CachedSortedPools, 0, m_CachedSortedCount);
            m_CachedSortedCount = 0;
            return true;
        }

        private void CacheSortedObjectPools()
        {
            int count = m_PoolCount;
            if (m_CachedSortedPools.Length < count)
                m_CachedSortedPools = new ObjectPoolBase[Math.Max(count, 8)];

            Array.Copy(m_Pools, 0, m_CachedSortedPools, 0, count);
            if (m_CachedSortedCount > count)
                Array.Clear(m_CachedSortedPools, count, m_CachedSortedCount - count);
            m_CachedSortedCount = count;

            for (int i = 1; i < count; i++)
            {
                var key = m_CachedSortedPools[i];
                int keyPriority = key.Priority;
                int j = i - 1;
                while (j >= 0 && m_CachedSortedPools[j].Priority > keyPriority)
                {
                    m_CachedSortedPools[j + 1] = m_CachedSortedPools[j];
                    j--;
                }
                m_CachedSortedPools[j + 1] = key;
            }
        }

    }
}
