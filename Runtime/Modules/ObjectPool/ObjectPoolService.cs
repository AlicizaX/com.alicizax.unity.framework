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

        private OpenHashMap<ObjectPoolKey> m_PoolMap;
        private ObjectPoolBase[] m_Pools;
        private int m_PoolCount;
        private ObjectPoolBase[] m_ActivePools;
        private int m_ActiveCount;

        public ObjectPoolService()
        {
            m_PoolMap = new OpenHashMap<ObjectPoolKey>(InitPoolArrayCapacity);
            m_Pools = new ObjectPoolBase[InitPoolArrayCapacity];
            m_ActivePools = new ObjectPoolBase[InitPoolArrayCapacity];
            m_PoolCount = 0;
            m_ActiveCount = 0;
        }

        public int Priority => 1;
        public int Count => m_PoolMap.Count;

        void IServiceTickable.Tick(float deltaTime)
        {
            float unscaled = Time.unscaledDeltaTime;
            int i = m_ActiveCount - 1;
            while (i >= 0)
            {
                var pool = m_ActivePools[i];
                pool.Update(deltaTime, unscaled);
                if (pool.ActiveIndex < 0)
                {
                    if (i >= m_ActiveCount)
                        i = m_ActiveCount - 1;
                    continue;
                }

                i--;
            }
        }

        protected override void OnInitialize() { }

        protected override void OnDestroyService()
        {
            for (int i = m_PoolCount - 1; i >= 0; i--)
                m_Pools[i].Shutdown();
            m_PoolMap.Dispose();
            Array.Clear(m_Pools, 0, m_PoolCount);
            Array.Clear(m_ActivePools, 0, m_ActiveCount);
            m_PoolCount = 0;
            m_ActiveCount = 0;
        }

        public bool HasObjectPool<T>(string name = "") where T : ObjectBase
            => m_PoolMap.ContainsKey(new ObjectPoolKey(typeof(T), name));

        public IObjectPool<T> GetObjectPool<T>(string name = "") where T : ObjectBase
            => (IObjectPool<T>)InternalGet(new ObjectPoolKey(typeof(T), name));

        public IObjectPool<T> GetOrCreatePool<T>(ObjectPoolCreateOptions options = default) where T : ObjectBase
        {
            var key = new ObjectPoolKey(typeof(T), options.Name);
            if (m_PoolMap.TryGetValue(key, out int idx))
                return (IObjectPool<T>)m_Pools[idx];

            var pool = new ObjectPool<T>(
                this,
                options.Name ?? string.Empty,
                options.AllowMultiSpawn,
                options.AutoReleaseInterval ?? DefaultAutoReleaseInterval,
                options.Capacity ?? DefaultCapacity,
                options.ExpireTime ?? DefaultExpireTime,
                options.Priority);

            int storageIndex = m_PoolCount;
            if (storageIndex >= m_Pools.Length)
            {
                var newArr = new ObjectPoolBase[m_Pools.Length * 2];
                Array.Copy(m_Pools, 0, newArr, 0, m_PoolCount);
                m_Pools = newArr;
            }

            m_Pools[storageIndex] = pool;
            m_PoolCount++;
            m_PoolMap.AddOrUpdate(key, storageIndex);
            return pool;
        }

        public bool DestroyObjectPool<T>(string name = "") where T : ObjectBase
            => InternalDestroy(new ObjectPoolKey(typeof(T), name));

        internal int GetAllObjectPools(bool sort, ObjectPoolBase[] results)
        {
            if (results == null)
            {
                UnityEngine.Debug.LogError("Results is invalid.");
                return 0;
            }

            int count = m_PoolCount;
            int copy = results.Length < count ? results.Length : count;
            if (sort)
            {
                for (int i = 0; i < copy; i++)
                    results[i] = m_Pools[i];
                for (int i = 1; i < copy; i++)
                {
                    var key = results[i];
                    int keyPriority = key.Priority;
                    int j = i - 1;
                    while (j >= 0 && results[j].Priority > keyPriority)
                    {
                        results[j + 1] = results[j];
                        j--;
                    }
                    results[j + 1] = key;
                }
            }
            else
            {
                Array.Copy(m_Pools, 0, results, 0, copy);
            }

            return count;
        }

        public void Release()
        {
            for (int i = 0; i < m_PoolCount; i++)
                m_Pools[i].Release();
        }

        public void ReleaseAllUnused()
        {
            for (int i = 0; i < m_PoolCount; i++)
                m_Pools[i].ReleaseAllUnused();
        }

        private ObjectPoolBase InternalGet(ObjectPoolKey key)
        {
            if (m_PoolMap.TryGetValue(key, out int idx))
                return m_Pools[idx];
            return null;
        }

        private bool InternalDestroy(ObjectPoolKey key)
        {
            if (!m_PoolMap.TryGetValue(key, out int idx))
                return false;

            var pool = m_Pools[idx];
            SetPoolActive(pool, false);
            pool.Shutdown();

            int lastIndex = m_PoolCount - 1;
            if (idx < lastIndex)
            {
                var lastPool = m_Pools[lastIndex];
                m_Pools[idx] = lastPool;
                m_PoolMap.AddOrUpdate(new ObjectPoolKey(lastPool.ObjectType, lastPool.Name), idx);
            }

            m_Pools[lastIndex] = null;
            m_PoolCount--;
            m_PoolMap.Remove(key);
            return true;
        }

        internal void SetPoolActive(ObjectPoolBase pool, bool active)
        {
            if (active)
            {
                if (pool.ActiveIndex >= 0)
                    return;

                if (m_ActiveCount >= m_ActivePools.Length)
                {
                    var newArr = new ObjectPoolBase[m_ActivePools.Length * 2];
                    Array.Copy(m_ActivePools, 0, newArr, 0, m_ActiveCount);
                    m_ActivePools = newArr;
                }

                pool.ActiveIndex = m_ActiveCount;
                m_ActivePools[m_ActiveCount++] = pool;
                pool.IsActive = true;
                return;
            }

            int activeIndex = pool.ActiveIndex;
            if (activeIndex < 0)
                return;

            int last = m_ActiveCount - 1;
            if (activeIndex < last)
            {
                var lastPool = m_ActivePools[last];
                m_ActivePools[activeIndex] = lastPool;
                lastPool.ActiveIndex = activeIndex;
            }

            m_ActivePools[last] = null;
            m_ActiveCount = last;
            pool.ActiveIndex = -1;
            pool.IsActive = false;
        }
    }
}
