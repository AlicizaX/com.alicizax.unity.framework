using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX
{
    public enum PoolPolicy : byte
    {
        Fixed = 0,
        Burst = 1,
        Sticky = 2
    }

    public readonly struct PoolSpawnContext
    {
        public readonly string Location;
        public readonly string Group;
        public readonly Transform Parent;
        public readonly uint SpawnFrame;

        public PoolSpawnContext(string location, string group, Transform parent, uint spawnFrame)
        {
            Location = location;
            Group = group;
            Parent = parent;
            SpawnFrame = spawnFrame;
        }
    }

    public interface IGameObjectPoolable
    {
        void OnSpawn(in PoolSpawnContext context);
        void OnDespawn();
        void OnPooledDestroy();
    }

    public readonly struct GameObjectPoolSummarySnapshot
    {
        public readonly bool IsReady;
        public readonly int PoolCount;
        public readonly int LoadedPrefabCount;
        public readonly int TotalInstanceCount;
        public readonly int ActiveInstanceCount;
        public readonly int InactiveInstanceCount;
        public readonly int PendingMaintenanceCount;

        public GameObjectPoolSummarySnapshot(
            bool isReady,
            int poolCount,
            int loadedPrefabCount,
            int totalInstanceCount,
            int activeInstanceCount,
            int inactiveInstanceCount,
            int pendingMaintenanceCount)
        {
            IsReady = isReady;
            PoolCount = poolCount;
            LoadedPrefabCount = loadedPrefabCount;
            TotalInstanceCount = totalInstanceCount;
            ActiveInstanceCount = activeInstanceCount;
            InactiveInstanceCount = inactiveInstanceCount;
            PendingMaintenanceCount = pendingMaintenanceCount;
        }
    }

    public sealed class GameObjectPoolInstanceSnapshot : MemoryObject
    {
        public string instanceName;
        public bool isActive;
        public float idleDuration;
        public float lifeDuration;
        public GameObject gameObject;

        public override void Clear()
        {
            instanceName = null;
            isActive = false;
            idleDuration = 0f;
            lifeDuration = 0f;
            gameObject = null;
        }
    }

    public sealed class GameObjectPoolSnapshot : MemoryObject
    {
        public string entryName;
        public string group;
        public string location;
        public PoolPolicy policy;
        public int minIdle;
        public int retainTarget;
        public int softCapacity;
        public int hardCapacity;
        public bool unloadPrefab;
        public int totalCount;
        public int activeCount;
        public int inactiveCount;
        public bool prefabLoaded;
        public float nextMaintenanceIn;
        public int spawnCount;
        public int despawnCount;
        public int hitCount;
        public int missCount;
        public int expandCount;
        public int destroyCount;
        public int peakActive;
        internal readonly List<GameObjectPoolInstanceSnapshot> instances = new List<GameObjectPoolInstanceSnapshot>();

        public int InstanceCount => instances.Count;

        public GameObjectPoolInstanceSnapshot GetInstance(int index)
        {
            return (uint)index < (uint)instances.Count ? instances[index] : null;
        }

        public override void Clear()
        {
            entryName = null;
            group = null;
            location = null;
            policy = default;
            minIdle = 0;
            retainTarget = 0;
            softCapacity = 0;
            hardCapacity = 0;
            unloadPrefab = false;
            totalCount = 0;
            activeCount = 0;
            inactiveCount = 0;
            prefabLoaded = false;
            nextMaintenanceIn = 0f;
            spawnCount = 0;
            despawnCount = 0;
            hitCount = 0;
            missCount = 0;
            expandCount = 0;
            destroyCount = 0;
            peakActive = 0;
            ClearInstances();
        }

        internal void ClearInstances()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                MemoryPool.Release(instances[i]);
            }

            instances.Clear();
        }
    }

    [DisallowMultipleComponent]
    public sealed class GameObjectPoolHandle : MonoBehaviour
    {
        private RuntimeGameObjectPool _owner;
        private int _slotIndex = -1;
        private uint _generation;

        internal int SlotIndex => _slotIndex;
        internal uint Generation => _generation;

        internal void Bind(RuntimeGameObjectPool owner, int slotIndex, uint generation)
        {
            _owner = owner;
            _slotIndex = slotIndex;
            _generation = generation;
        }

        internal void Detach()
        {
            _owner = null;
            _slotIndex = -1;
            _generation = 0;
        }

        internal bool TryRelease()
        {
            return _owner != null && _owner.ReleaseFromHandle(this);
        }

        private void OnDestroy()
        {
            if (_owner != null)
            {
                _owner.NotifyHandleDestroyed(_slotIndex, _generation);
            }
        }
    }
}
