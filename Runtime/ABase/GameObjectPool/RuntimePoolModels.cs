using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX.ObjectPool;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    public readonly struct PoolSpawnContext
    {
        public readonly string AssetPath;
        public readonly string Group;
        public readonly Transform Parent;
        public readonly uint SpawnFrame;

        public PoolSpawnContext(
            string assetPath,
            string group,
            Transform parent,
            uint spawnFrame = 0)
        {
            AssetPath = assetPath;
            Group = group;
            Parent = parent;
            SpawnFrame = spawnFrame;
        }

        public PoolSpawnContext WithParent(Transform parent)
        {
            return new PoolSpawnContext(
                AssetPath,
                Group,
                parent,
                SpawnFrame);
        }

        public PoolSpawnContext WithGroup(string group)
        {
            return new PoolSpawnContext(
                AssetPath,
                group,
                Parent,
                SpawnFrame);
        }

        public static PoolSpawnContext Create(string assetPath, Transform parent)
        {
            return new PoolSpawnContext(
                assetPath,
                null,
                parent,
                spawnFrame: (uint)Time.frameCount);
        }
    }

    public interface IGameObjectPoolable
    {
        void OnPoolGet(in PoolSpawnContext context);
        void OnPoolRelease();
        void OnPoolDestroy();
    }

    internal readonly struct PoolRecycleRuntimeContext
    {
        public readonly string Group;
        public readonly string AssetPath;
        public readonly PoolCategory Category;
        public readonly int ActiveCount;
        public readonly int InactiveCount;
        public readonly int TotalCount;
        public readonly int MinRetained;
        public readonly int SoftCapacity;
        public readonly int HardCapacity;
        public readonly int ShortPeakRetainPercent;
        public readonly int LongPeakRetainPercent;
        public readonly int ShortPeakDecayFrames;
        public readonly int LongPeakDecayFrames;
        public readonly int TrimMultiplier;
        public readonly bool RetainAllInactive;
        public readonly int PeakActiveShort;
        public readonly int PeakActiveLong;
        public readonly int IdleFrames;
        public readonly int HotFrames;
        public readonly float OldestIdleSeconds;
        public readonly bool LowMemory;

        public PoolRecycleRuntimeContext(
            string group,
            string assetPath,
            PoolCategory category,
            int activeCount,
            int inactiveCount,
            int totalCount,
            int minRetained,
            int softCapacity,
            int hardCapacity,
            int shortPeakRetainPercent,
            int longPeakRetainPercent,
            int shortPeakDecayFrames,
            int longPeakDecayFrames,
            int trimMultiplier,
            bool retainAllInactive,
            int peakActiveShort,
            int peakActiveLong,
            int idleFrames,
            int hotFrames,
            float oldestIdleSeconds,
            bool lowMemory)
        {
            Group = group;
            AssetPath = assetPath;
            Category = category;
            ActiveCount = activeCount;
            InactiveCount = inactiveCount;
            TotalCount = totalCount;
            MinRetained = minRetained;
            SoftCapacity = softCapacity;
            HardCapacity = hardCapacity;
            ShortPeakRetainPercent = shortPeakRetainPercent;
            LongPeakRetainPercent = longPeakRetainPercent;
            ShortPeakDecayFrames = shortPeakDecayFrames;
            LongPeakDecayFrames = longPeakDecayFrames;
            TrimMultiplier = trimMultiplier;
            RetainAllInactive = retainAllInactive;
            PeakActiveShort = peakActiveShort;
            PeakActiveLong = peakActiveLong;
            IdleFrames = idleFrames;
            HotFrames = hotFrames;
            OldestIdleSeconds = oldestIdleSeconds;
            LowMemory = lowMemory;
        }
    }

    internal readonly struct PoolRecyclePlan
    {
        public readonly int RetainTarget;
        public readonly int TrimBudget;
        public readonly bool ForceTrim;
        public readonly bool ForcePrefabUnload;

        public PoolRecyclePlan(int retainTarget, int trimBudget, bool forceTrim, bool forcePrefabUnload)
        {
            RetainTarget = retainTarget;
            TrimBudget = trimBudget;
            ForceTrim = forceTrim;
            ForcePrefabUnload = forcePrefabUnload;
        }
    }

    internal sealed class PendingAcquireCancelState : IMemory
    {
        public RuntimeGameObjectPool pool;
        public int requestId;
        public CancellationToken cancellationToken;

        public static PendingAcquireCancelState Create(
            RuntimeGameObjectPool pool,
            int requestId,
            CancellationToken cancellationToken)
        {
            PendingAcquireCancelState state = MemoryPool.Acquire<PendingAcquireCancelState>();
            state.pool = pool;
            state.requestId = requestId;
            state.cancellationToken = cancellationToken;
            return state;
        }

        public void Clear()
        {
            pool = null;
            requestId = 0;
            cancellationToken = default;
        }
    }

    internal struct IntOpenHashMap
    {
        private int[] _buckets;
        private int[] _keys;
        private int[] _values;
        private int[] _next;
        private int _count;
        private int _freeList;
        private int _mask;
        private int _allocCount;

        private const int MinCapacity = 8;

        public IntOpenHashMap(int capacity)
        {
            int cap = NextPowerOf2(Math.Max(capacity, MinCapacity));
            _mask = cap - 1;
            _buckets = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(cap);
            _keys = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(cap);
            _values = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(cap);
            _next = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(cap);
            Array.Clear(_buckets, 0, _buckets.Length);
            Array.Clear(_keys, 0, _keys.Length);
            Array.Clear(_values, 0, _values.Length);
            Array.Clear(_next, 0, _next.Length);
            _count = 0;
            _freeList = 0;
            _allocCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out int value)
        {
            if (_buckets == null)
            {
                value = -1;
                return false;
            }

            int i = _buckets[Hash(key) & _mask];
            while (i > 0)
            {
                int idx = i - 1;
                if (_keys[idx] == key)
                {
                    value = _values[idx];
                    return true;
                }

                i = _next[idx];
            }

            value = -1;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(int key, int value)
        {
            if (_buckets == null)
            {
                this = new IntOpenHashMap(MinCapacity);
            }

            if (_count >= ((_mask + 1) * 3 >> 2))
            {
                Grow();
            }

            int hash = Hash(key);
            int bucket = hash & _mask;
            int i = _buckets[bucket];
            while (i > 0)
            {
                int idx = i - 1;
                if (_keys[idx] == key)
                {
                    _values[idx] = value;
                    return;
                }

                i = _next[idx];
            }

            int newIndex;
            if (_freeList > 0)
            {
                newIndex = _freeList - 1;
                _freeList = _next[newIndex];
            }
            else
            {
                if (_allocCount > _mask)
                {
                    Grow();
                    bucket = hash & _mask;
                }

                newIndex = _allocCount++;
            }

            _keys[newIndex] = key;
            _values[newIndex] = value;
            _next[newIndex] = _buckets[bucket];
            _buckets[bucket] = newIndex + 1;
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(int key)
        {
            if (_buckets == null)
            {
                return false;
            }

            int bucket = Hash(key) & _mask;
            int previous = 0;
            int i = _buckets[bucket];
            while (i > 0)
            {
                int idx = i - 1;
                if (_keys[idx] == key)
                {
                    if (previous == 0)
                    {
                        _buckets[bucket] = _next[idx];
                    }
                    else
                    {
                        _next[previous - 1] = _next[idx];
                    }

                    _keys[idx] = 0;
                    _values[idx] = -1;
                    _next[idx] = _freeList;
                    _freeList = idx + 1;
                    _count--;
                    return true;
                }

                previous = i;
                i = _next[idx];
            }

            return false;
        }

        public void Clear()
        {
            if (_buckets == null)
            {
                return;
            }

            int capacity = _mask + 1;
            Array.Clear(_buckets, 0, capacity);
            Array.Clear(_keys, 0, capacity);
            Array.Clear(_values, 0, capacity);
            Array.Clear(_next, 0, capacity);
            _count = 0;
            _freeList = 0;
            _allocCount = 0;
        }

        public void Dispose()
        {
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_buckets, true);
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_keys, true);
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_values, true);
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_next, true);
            _buckets = null;
            _keys = null;
            _values = null;
            _next = null;
            _count = 0;
            _freeList = 0;
            _mask = 0;
            _allocCount = 0;
        }

        private void Grow()
        {
            int newCapacity = (_mask + 1) << 1;
            if (newCapacity < MinCapacity)
            {
                newCapacity = MinCapacity;
            }

            int newMask = newCapacity - 1;
            int[] newBuckets = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(newCapacity);
            int[] newKeys = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(newCapacity);
            int[] newValues = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(newCapacity);
            int[] newNext = AlicizaX.ObjectPool.SlotArrayPool<int>.Rent(newCapacity);
            Array.Clear(newBuckets, 0, newBuckets.Length);
            Array.Clear(newKeys, 0, newKeys.Length);
            Array.Clear(newValues, 0, newValues.Length);
            Array.Clear(newNext, 0, newNext.Length);

            int newAlloc = 0;
            int oldCapacity = _mask + 1;
            for (int bucketIndex = 0; bucketIndex < oldCapacity; bucketIndex++)
            {
                int i = _buckets[bucketIndex];
                while (i > 0)
                {
                    int old = i - 1;
                    int newIndex = newAlloc++;
                    newKeys[newIndex] = _keys[old];
                    newValues[newIndex] = _values[old];
                    int newBucket = Hash(newKeys[newIndex]) & newMask;
                    newNext[newIndex] = newBuckets[newBucket];
                    newBuckets[newBucket] = newIndex + 1;
                    i = _next[old];
                }
            }

            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_buckets, true);
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_keys, true);
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_values, true);
            AlicizaX.ObjectPool.SlotArrayPool<int>.Return(_next, true);

            _buckets = newBuckets;
            _keys = newKeys;
            _values = newValues;
            _next = newNext;
            _mask = newMask;
            _allocCount = newAlloc;
            _freeList = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return (int)(x & 0x7fffffff);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOf2(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }

    public readonly struct GameObjectPoolSummarySnapshot
    {
        public readonly bool IsReady;
        public readonly bool WaitingForBootstrap;
        public readonly int PoolCount;
        public readonly int LoadedPrefabCount;
        public readonly int TotalInstanceCount;
        public readonly int ActiveInstanceCount;
        public readonly int InactiveInstanceCount;
        public readonly int PendingMaintenanceCount;

        public GameObjectPoolSummarySnapshot(
            bool isReady,
            bool waitingForBootstrap,
            int poolCount,
            int loadedPrefabCount,
            int totalInstanceCount,
            int activeInstanceCount,
            int inactiveInstanceCount,
            int pendingMaintenanceCount)
        {
            IsReady = isReady;
            WaitingForBootstrap = waitingForBootstrap;
            PoolCount = poolCount;
            LoadedPrefabCount = loadedPrefabCount;
            TotalInstanceCount = totalInstanceCount;
            ActiveInstanceCount = activeInstanceCount;
            InactiveInstanceCount = inactiveInstanceCount;
            PendingMaintenanceCount = pendingMaintenanceCount;
        }
    }

    [Serializable]
    public sealed class GameObjectPoolInstanceSnapshot : IMemory
    {
        public string instanceName;
        public bool isActive;
        public float idleDuration;
        public float lifeDuration;
        public GameObject gameObject;

        public void Clear()
        {
            instanceName = null;
            isActive = false;
            idleDuration = 0f;
            lifeDuration = 0f;
            gameObject = null;
        }
    }

    [Serializable]
    public sealed class GameObjectPoolSnapshot : IMemory
    {
        public string entryName;
        public string group;
        public string assetPath;
        public PoolCategory category;
        public PoolResourceLoaderType loaderType;
        public int minRetained;
        public int retainTarget;
        public int softCapacity;
        public int hardCapacity;
        public int runtimeHardCapacity;
        public int trimPerTick;
        public int shortPeakRetainPercent;
        public int longPeakRetainPercent;
        public int shortPeakDecayFrames;
        public int longPeakDecayFrames;
        public int trimMultiplier;
        public bool retainAllInactive;
        public int totalCount;
        public int activeCount;
        public int inactiveCount;
        public bool prefabLoaded;
        public float prefabIdleDuration;
        public int prefabWakeCount;
        public float prefabWakeGap;
        public float prefabUnloadDelay;
        public float nextMaintenanceIn;
        public int acquireCount;
        public int releaseCount;
        public int hitCount;
        public int missCount;
        public int expandCount;
        public int destroyCount;
        public int peakActive;
        public int peakActiveShort;
        public int peakActiveLong;
        internal readonly List<GameObjectPoolInstanceSnapshot> instances = new List<GameObjectPoolInstanceSnapshot>();

        public int InstanceCount => instances.Count;

        public GameObjectPoolInstanceSnapshot GetInstance(int index)
        {
            return (uint)index < (uint)instances.Count ? instances[index] : null;
        }

        public void Clear()
        {
            entryName = null;
            group = null;
            assetPath = null;
            category = default;
            loaderType = default;
            minRetained = 0;
            retainTarget = 0;
            softCapacity = 0;
            hardCapacity = 0;
            runtimeHardCapacity = 0;
            trimPerTick = 0;
            shortPeakRetainPercent = 0;
            longPeakRetainPercent = 0;
            shortPeakDecayFrames = 0;
            longPeakDecayFrames = 0;
            trimMultiplier = 0;
            retainAllInactive = false;
            totalCount = 0;
            activeCount = 0;
            inactiveCount = 0;
            prefabLoaded = false;
            prefabIdleDuration = 0f;
            prefabWakeCount = 0;
            prefabWakeGap = 0f;
            prefabUnloadDelay = 0f;
            nextMaintenanceIn = 0f;
            acquireCount = 0;
            releaseCount = 0;
            hitCount = 0;
            missCount = 0;
            expandCount = 0;
            destroyCount = 0;
            peakActive = 0;
            peakActiveShort = 0;
            peakActiveLong = 0;

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

    internal sealed class RuntimeGameObjectPool : IMemory
    {
        private enum SlotState : byte
        {
            Free = 0,
            Inactive = 1,
            Active = 2
        }

        private struct Slot
        {
            public GameObject instance;
            public Transform transform;
            public GameObjectPoolHandle handle;
            public IGameObjectPoolable[] poolables;
            public float spawnTime;
            public float lastReleaseTime;
            public int prevInactive;
            public int nextInactive;
            public uint generation;
            public SlotState state;
        }

        private struct PoolableTransformBinding
        {
            public int transformIndex;
            public int[] componentIndices;
            public int poolableStartIndex;
            public int poolableCount;
        }

        private struct PendingAcquire
        {
            public AutoResetUniTaskCompletionSource<GameObject> completionSource;
            public PoolSpawnContext context;
            public CancellationToken cancellationToken;
            public CancellationTokenRegistration cancellationRegistration;
            public PendingAcquireCancelState cancellationState;
            public int requestId;
            public int next;
            public int prev;
            public bool active;
        }

        private const int PageBits = 7;
        private const int PageSize = 1 << PageBits;
        private const int PageMask = PageSize - 1;
        private const byte PageAllocated = 1;
        private const byte PageInFreeStack = 2;
        private const byte PageInEmptyStack = 4;
        private const int InitialPageCapacity = 4;
        private const int WarmupCreateBatch = 8;
        private const float WarmupFrameBudgetSeconds = 0.001f;
        private const float IdleTrimDelaySeconds = 15f;
        private const float PrefabUnloadDelayColdSeconds = 15f;
        private const float PrefabUnloadDelaySparseSeconds = 30f;
        private const float PrefabUnloadDelayHotSeconds = 90f;
        private const float PrefabSparseWakeGapSeconds = 20f;
        private const int PrefabLowWakeCountThreshold = 2;
        private const int PrefabWarmWakeCountThreshold = 6;
        private const int PeakRetainPercentShort = 120;
        private const int PeakRetainPercentLong = 100;
        private const int PeakDecayFramesShort = 180;
        private const int PeakDecayFramesLong = 900;
        private const int TrimBudgetCap = 16;
        private static readonly Comparison<GameObjectPoolInstanceSnapshot> InstanceComparer = CompareInstanceSnapshot;
        private static readonly Action<object> CancelPendingAcquireCallbackDelegate = CancelPendingAcquireCallback;

        private GameObjectPoolService _service;
        private IResourceLoader _loader;
        private PoolCompiledRule _rule;
        private int _poolIndex;
        private string _assetPath;
        private string _loadPath;
        private Transform _root;
        private GameObject _prefab;
        private UniTaskCompletionSource<GameObject> _prefabLoadCompletionSource;
        private bool _prefabLoading;
        private bool _isShuttingDown;
        private int _loadVersion;
        private float _lastPrefabTouchTime;
        private float _lastWakeTime;
        private float _previousWakeTime;
        private float _nextMaintenanceAt;
        private int _maintenanceHeapIndex;
        private Slot[][] _pages;
        private int[][] _pageFreeStacks;
        private int[] _pageAliveCounts;
        private int[] _pageFreeTops;
        private byte[] _pageFlags;
        private int _pageCount;
        private int[] _freePageStack;
        private int[] _freePageStackIndices;
        private int _freePageTop;
        private int[] _emptyPageStack;
        private int _emptyPageTop;
        private int _inactiveHead;
        private int _inactiveTail;
        private int _activeCount;
        private int _inactiveCount;
        private int _totalCount;
        private int _runtimeHardCapacity;
        private int _retainTarget;
        private int _acquireCount;
        private int _releaseCount;
        private int _hitCount;
        private int _missCount;
        private int _expandCount;
        private int _destroyCount;
        private int _peakActive;
        private int _peakActiveShort;
        private int _peakActiveLong;
        private int _acquireSinceMaintain;
        private int _releaseSinceMaintain;
        private int _wakeCountSincePrefabLoad;
        private int _idleFrames;
        private int _hotFrames;
        private uint _generationCounter;
        private PendingAcquire[] _pendingAcquires;
        private int[] _pendingAcquireFreeStack;
        private int _pendingAcquireHead;
        private int _pendingAcquireTail;
        private int _pendingAcquireCount;
        private int _pendingAcquireFreeTop;
        private int _pendingAcquireSlotCount;
        private int _pendingAcquireRequestId;
        private IntOpenHashMap _pendingAcquireIndexMap;
        private PoolableTransformBinding[] _poolableBindings;
        private Transform[] _transformBuildBuffer;
        private int _poolableBindingCount;
        private int _poolableCount;
        private readonly List<MonoBehaviour> _componentBuffer = new List<MonoBehaviour>(8);
        private readonly List<Transform> _transformBuffer = new List<Transform>(32);
        private readonly List<Transform> _instanceTransformBuffer = new List<Transform>(32);
        private readonly List<PoolableTransformBinding> _bindingBuildBuffer = new List<PoolableTransformBinding>(8);
        private readonly List<int> _bindingIndexBuildBuffer = new List<int>(4);

        public int TotalCount => _totalCount;
        public int ActiveCount => _activeCount;
        public int InactiveCount => _inactiveCount;
        public bool IsPrefabLoaded => _prefab != null;
        public float NextMaintenanceAt => _nextMaintenanceAt;
        public bool HasMaintenance => _maintenanceHeapIndex >= 0;
        public float PrefabIdleDuration => _prefab == null ? 0f : Mathf.Max(0f, Time.time - GetPrefabReferenceTime());

        public void Initialize(
            GameObjectPoolService service,
            int poolIndex,
            in PoolCompiledRule rule,
            string assetPath,
            string loadPath,
            IResourceLoader loader,
            Transform inactiveRoot)
        {
            _service = service;
            _poolIndex = poolIndex;
            _rule = rule;
            _assetPath = assetPath;
            _loadPath = loadPath;
            _loader = loader;
            _root = inactiveRoot;
            _runtimeHardCapacity = rule.hardCapacity;
            _retainTarget = GetMinimumRetained();
            _lastPrefabTouchTime = -1f;
            _lastWakeTime = -1f;
            _previousWakeTime = -1f;
            _nextMaintenanceAt = float.MaxValue;
            _maintenanceHeapIndex = -1;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _pendingAcquireHead = -1;
            _pendingAcquireTail = -1;
            _pages = SlotArrayPool<Slot[]>.Rent(InitialPageCapacity);
            _pageFreeStacks = SlotArrayPool<int[]>.Rent(InitialPageCapacity);
            _pageAliveCounts = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _pageFreeTops = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _pageFlags = SlotArrayPool<byte>.Rent(InitialPageCapacity);
            _freePageStack = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _freePageStackIndices = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _emptyPageStack = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _pendingAcquires = SlotArrayPool<PendingAcquire>.Rent(InitialPageCapacity);
            _pendingAcquireFreeStack = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _pendingAcquireIndexMap = new IntOpenHashMap(InitialPageCapacity);
            Array.Clear(_pages, 0, InitialPageCapacity);
            Array.Clear(_pageFreeStacks, 0, InitialPageCapacity);
            Array.Clear(_pageAliveCounts, 0, InitialPageCapacity);
            Array.Clear(_pageFreeTops, 0, InitialPageCapacity);
            Array.Clear(_pageFlags, 0, InitialPageCapacity);
            Array.Clear(_freePageStack, 0, InitialPageCapacity);
            Array.Clear(_freePageStackIndices, 0, InitialPageCapacity);
            Array.Clear(_emptyPageStack, 0, InitialPageCapacity);
            Array.Clear(_pendingAcquires, 0, InitialPageCapacity);
            Array.Clear(_pendingAcquireFreeStack, 0, InitialPageCapacity);

            ScheduleMaintenance(float.MaxValue);
        }

        public GameObject Acquire(in PoolSpawnContext context)
        {
            if (!EnsurePrefabLoaded())
            {
                return null;
            }

            return AcquirePrepared(context);
        }

        public async UniTask<GameObject> AcquireAsync(PoolSpawnContext context, CancellationToken cancellationToken)
        {
            bool prefabLoaded = await EnsurePrefabLoadedAsync(cancellationToken);
            if (!prefabLoaded)
            {
                return null;
            }

            if (_inactiveTail < 0 && _totalCount >= _rule.hardCapacity)
            {
                return await EnqueueAcquireAsync(context, cancellationToken);
            }

            return AcquirePrepared(context);
        }

        public async UniTask WarmupAsync(int count, CancellationToken cancellationToken)
        {
            int warmCount = Mathf.Max(0, count);
            if (warmCount <= 0)
            {
                return;
            }

            if (_inactiveCount >= warmCount)
            {
                return;
            }

            bool prefabLoaded = await EnsurePrefabLoadedAsync(cancellationToken);
            if (!prefabLoaded)
            {
                return;
            }

            int targetWarmCount = Mathf.Min(warmCount, _rule.hardCapacity);

            float frameBudget = WarmupFrameBudgetSeconds;
            int createdThisFrame = 0;
            float frameStart = Time.realtimeSinceStartup;

            while (_inactiveCount < targetWarmCount && _totalCount < _rule.hardCapacity)
            {
                int slotIndex = CreateTrackedInstance();
                ParkInactive(slotIndex, true);
                createdThisFrame++;

                bool budgetReachedByCount = createdThisFrame >= WarmupCreateBatch;
                bool budgetReachedByTime = frameBudget > 0f && Time.realtimeSinceStartup - frameStart >= frameBudget;
                if (budgetReachedByCount || budgetReachedByTime)
                {
                    createdThisFrame = 0;
                    frameStart = Time.realtimeSinceStartup;
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            RefreshMaintenance(false);
        }

        public bool ReleaseFromHandle(GameObjectPoolHandle handle)
        {
            if (handle == null)
            {
                return false;
            }

            int slotIndex = handle.SlotIndex;
            if (!IsValidIndex(slotIndex))
            {
                return false;
            }

            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.handle != handle || slot.generation != handle.Generation || slot.state != SlotState.Active)
            {
                return false;
            }

            ReleaseTrackedInstance(slotIndex);
            return true;
        }

        public void NotifyHandleDestroyed(int slotIndex, uint generation)
        {
            if (_isShuttingDown || !IsValidIndex(slotIndex))
            {
                return;
            }

            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.generation != generation)
            {
                return;
            }

            RemoveExternallyDestroyedSlot(slotIndex);
        }

        public void SetMaintenanceHeapIndex(int heapIndex)
        {
            _maintenanceHeapIndex = heapIndex;
        }

        public void ExecuteMaintenance(float now, bool lowMemory)
        {
            UpdateRetentionMetrics();

            PoolRecyclePlan plan = BuildRecyclePlan(now, lowMemory);
            _retainTarget = Mathf.Clamp(plan.RetainTarget, GetMinimumRetained(), _runtimeHardCapacity);

            int destroyBudget = Mathf.Max(1, plan.TrimBudget);
            while (_inactiveHead >= 0 && destroyBudget > 0 && ShouldTrimHead(now, plan))
            {
                DestroyTrackedInstance(_inactiveHead, true);
                destroyBudget--;
            }

            RestoreRuntimeHardCapacity();

            if (_prefab != null && ShouldUnloadPrefab(now, plan))
            {
                _loader.UnloadAsset(_prefab);
                _prefab = null;
                _prefabLoading = false;
            }

            RefreshMaintenance(false);
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            _loadVersion++;
            _prefabLoading = false;
            _prefabLoadCompletionSource?.TrySetCanceled(_service.ShutdownToken);
            _prefabLoadCompletionSource = null;
            CancelPendingAcquires(_service.ShutdownToken);
            _service.RemoveMaintenance(ref _maintenanceHeapIndex);

            for (int page = 0; page < _pageCount; page++)
            {
                Slot[] pageSlots = _pages[page];
                if (pageSlots == null)
                {
                    continue;
                }

                for (int offset = 0; offset < PageSize; offset++)
                {
                    int slotIndex = MakeIndex(page, offset);
                    ref Slot slot = ref pageSlots[offset];
                    if (slot.state == SlotState.Free && slot.instance == null)
                    {
                        continue;
                    }

                    InvokeOnPoolDestroy(ref slot);
                    if (slot.handle != null)
                    {
                        slot.handle.Detach();
                    }

                    if (slot.instance != null)
                    {
                        GameObject.Destroy(slot.instance);
                    }

                    ClearSlot(ref slot);
                    slot.state = SlotState.Free;
                    slot.prevInactive = -1;
                    slot.nextInactive = -1;
                    _totalCount--;
                    _destroyCount++;
                }
            }

            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;
            CancelPendingAcquires(_service.ShutdownToken);

            if (_prefab != null)
            {
                _loader.UnloadAsset(_prefab);
                _prefab = null;
            }
        }

        public GameObjectPoolSnapshot CreateSnapshot()
        {
            float now = Time.time;
            var snapshot = MemoryPool.Acquire<GameObjectPoolSnapshot>();
            snapshot.entryName = _rule.entryName;
            snapshot.group = _rule.group;
            snapshot.assetPath = _assetPath;
            snapshot.category = _rule.category;
            snapshot.loaderType = _rule.loaderType;
            snapshot.minRetained = GetMinimumRetained();
            snapshot.retainTarget = _retainTarget;
            snapshot.softCapacity = _rule.softCapacity;
            snapshot.hardCapacity = _rule.hardCapacity;
            snapshot.runtimeHardCapacity = _runtimeHardCapacity;
            snapshot.trimPerTick = GetTrimBudgetBase();
            snapshot.shortPeakRetainPercent = PeakRetainPercentShort;
            snapshot.longPeakRetainPercent = PeakRetainPercentLong;
            snapshot.shortPeakDecayFrames = PeakDecayFramesShort;
            snapshot.longPeakDecayFrames = PeakDecayFramesLong;
            snapshot.trimMultiplier = 1;
            snapshot.retainAllInactive = false;
            snapshot.totalCount = _totalCount;
            snapshot.activeCount = _activeCount;
            snapshot.inactiveCount = _inactiveCount;
            snapshot.prefabLoaded = _prefab != null;
            snapshot.prefabIdleDuration = PrefabIdleDuration;
            snapshot.prefabWakeCount = _prefab == null ? 0 : _wakeCountSincePrefabLoad;
            snapshot.prefabWakeGap = _prefab == null ? -1f : GetLastWakeGapDuration();
            snapshot.prefabUnloadDelay = _prefab == null ? -1f : GetPrefabUnloadDelaySeconds();
            snapshot.nextMaintenanceIn = _nextMaintenanceAt >= float.MaxValue
                ? -1f
                : Mathf.Max(0f, _nextMaintenanceAt - now);
            snapshot.acquireCount = _acquireCount;
            snapshot.releaseCount = _releaseCount;
            snapshot.hitCount = _hitCount;
            snapshot.missCount = _missCount;
            snapshot.expandCount = _expandCount;
            snapshot.destroyCount = _destroyCount;
            snapshot.peakActive = _peakActive;
            snapshot.peakActiveShort = _peakActiveShort;
            snapshot.peakActiveLong = _peakActiveLong;

            for (int page = 0; page < _pageCount; page++)
            {
                Slot[] pageSlots = _pages[page];
                if (pageSlots == null)
                {
                    continue;
                }

                for (int offset = 0; offset < PageSize; offset++)
                {
                    ref Slot slot = ref pageSlots[offset];
                    if (slot.state == SlotState.Free && slot.instance == null)
                    {
                        continue;
                    }

                    var instanceSnapshot = MemoryPool.Acquire<GameObjectPoolInstanceSnapshot>();
                    instanceSnapshot.instanceName = slot.instance == null ? "<destroyed>" : slot.instance.name;
                    instanceSnapshot.isActive = slot.state == SlotState.Active;
                    instanceSnapshot.idleDuration = slot.state == SlotState.Active
                        ? 0f
                        : Mathf.Max(0f, now - slot.lastReleaseTime);
                    instanceSnapshot.lifeDuration = Mathf.Max(0f, now - slot.spawnTime);
                    instanceSnapshot.gameObject = slot.instance;
                    snapshot.instances.Add(instanceSnapshot);
                }
            }

            snapshot.instances.Sort(InstanceComparer);
            return snapshot;
        }

        public void Clear()
        {
            ReturnAllPages();
            ReturnStorageArrays();
            ClearLifecycleBindings();
            _service = null;
            _loader = null;
            _rule = default;
            _poolIndex = 0;
            _assetPath = null;
            _loadPath = null;
            _root = null;
            _prefab = null;
            _prefabLoadCompletionSource?.TrySetCanceled(_service == null ? default : _service.ShutdownToken);
            _prefabLoadCompletionSource = null;
            _prefabLoading = false;
            _isShuttingDown = false;
            _loadVersion++;
            _lastPrefabTouchTime = -1f;
            _lastWakeTime = -1f;
            _previousWakeTime = -1f;
            _nextMaintenanceAt = float.MaxValue;
            _maintenanceHeapIndex = -1;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;
            _runtimeHardCapacity = 0;
            _retainTarget = 0;
            _acquireCount = 0;
            _releaseCount = 0;
            _hitCount = 0;
            _missCount = 0;
            _expandCount = 0;
            _destroyCount = 0;
            _peakActive = 0;
            _peakActiveShort = 0;
            _peakActiveLong = 0;
            _acquireSinceMaintain = 0;
            _releaseSinceMaintain = 0;
            _wakeCountSincePrefabLoad = 0;
            _idleFrames = 0;
            _hotFrames = 0;
            _generationCounter = 0;
            _pendingAcquireHead = -1;
            _pendingAcquireTail = -1;
            _pendingAcquireCount = 0;
            _pendingAcquireFreeTop = 0;
            _pendingAcquireSlotCount = 0;
            _pendingAcquireRequestId = 0;
            _transformBuildBuffer = null;
        }

        private GameObject AcquirePrepared(in PoolSpawnContext context)
        {
            _acquireCount++;
            _acquireSinceMaintain++;

            int slotIndex;
            if (_inactiveTail >= 0)
            {
                slotIndex = _inactiveTail;
                RemoveFromInactive(slotIndex);
                _hitCount++;
            }
            else
            {
                _missCount++;
                slotIndex = AcquireOnMiss();
                if (slotIndex < 0)
                {
                    RefreshMaintenance(false);
                    return null;
                }
            }

            ActivateTrackedInstance(slotIndex, context);
            if (_activeCount > _peakActive)
            {
                _peakActive = _activeCount;
            }

            if (_activeCount > _peakActiveShort)
            {
                _peakActiveShort = _activeCount;
            }

            if (_activeCount > _peakActiveLong)
            {
                _peakActiveLong = _activeCount;
            }

            RefreshMaintenance(false);
            return GetSlotRef(slotIndex).instance;
        }

        private int AcquireOnMiss()
        {
            if (_totalCount >= _rule.hardCapacity)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Warning(ZString.Format(
                    "[GameObjectPool] Acquire rejected. HardCapacity reached. Rule:{0}, Asset:{1}, Hard:{2}, Active:{3}, Inactive:{4}",
                    _rule.entryName,
                    _assetPath,
                    _rule.hardCapacity,
                    _activeCount,
                    _inactiveCount));
#endif
                return -1;
            }

            return CreateTrackedInstance();
        }

        private void ActivateTrackedInstance(int slotIndex, in PoolSpawnContext context)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.state = SlotState.Active;
            _activeCount++;
            RecordWake();

            Transform transform = slot.transform;
            transform.SetParent(context.Parent, false);
            if (!slot.instance.activeSelf)
            {
                slot.instance.SetActive(true);
            }

            InvokeOnPoolGet(ref slot, context);
        }

        private void ReleaseTrackedInstance(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.state != SlotState.Active)
            {
                return;
            }

            _releaseCount++;
            _releaseSinceMaintain++;
            _activeCount = Mathf.Max(0, _activeCount - 1);
            InvokeOnPoolRelease(ref slot);
            if (slot.instance.activeSelf)
            {
                slot.instance.SetActive(false);
            }

            if (TryCompletePendingAcquire(slotIndex))
            {
                RefreshMaintenance(false);
                return;
            }

            ParkInactive(slotIndex, false);
            RefreshMaintenance(false);
        }

        private void ParkInactive(int slotIndex, bool newInstance)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.state = SlotState.Inactive;
            slot.lastReleaseTime = Time.time;
            slot.transform.SetParent(_root, false);

            AddToInactiveTail(slotIndex);
        }

        private UniTask<GameObject> EnqueueAcquireAsync(PoolSpawnContext context, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled<GameObject>(cancellationToken);
            }

            int slotIndex = AllocPendingAcquireSlot();
            AutoResetUniTaskCompletionSource<GameObject> completionSource = AutoResetUniTaskCompletionSource<GameObject>.Create();
            int requestId = ++_pendingAcquireRequestId;
            PendingAcquireCancelState cancellationState = cancellationToken.CanBeCanceled
                ? PendingAcquireCancelState.Create(this, requestId, cancellationToken)
                : null;

            _pendingAcquires[slotIndex].completionSource = completionSource;
            _pendingAcquires[slotIndex].context = context;
            _pendingAcquires[slotIndex].cancellationToken = cancellationToken;
            _pendingAcquires[slotIndex].cancellationState = cancellationState;
            _pendingAcquires[slotIndex].requestId = requestId;
            _pendingAcquires[slotIndex].next = -1;
            _pendingAcquires[slotIndex].prev = _pendingAcquireTail;
            _pendingAcquires[slotIndex].active = true;
            if (_pendingAcquireTail >= 0)
            {
                _pendingAcquires[_pendingAcquireTail].next = slotIndex;
            }
            else
            {
                _pendingAcquireHead = slotIndex;
            }

            _pendingAcquireTail = slotIndex;
            _pendingAcquireCount++;
            _pendingAcquireIndexMap.AddOrUpdate(requestId, slotIndex);

            if (cancellationState != null)
            {
                CancellationTokenRegistration registration =
                    cancellationToken.RegisterWithoutCaptureExecutionContext(CancelPendingAcquireCallbackDelegate, cancellationState);
                if (_pendingAcquires[slotIndex].active && _pendingAcquires[slotIndex].requestId == requestId)
                {
                    _pendingAcquires[slotIndex].cancellationRegistration = registration;
                }
                else
                {
                    registration.Dispose();
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                CancelPendingAcquire(requestId, cancellationToken);
            }

            return completionSource.Task;
        }

        private bool TryCompletePendingAcquire(int slotIndex)
        {
            while (_pendingAcquireHead >= 0)
            {
                int pendingIndex = _pendingAcquireHead;
                PendingAcquire pendingAcquire = RemovePendingAcquireAt(pendingIndex);

                if (pendingAcquire.cancellationToken.IsCancellationRequested)
                {
                    CancelCompletionAndReleaseState(ref pendingAcquire, pendingAcquire.cancellationToken);
                    continue;
                }

                ActivateTrackedInstance(slotIndex, pendingAcquire.context);
                pendingAcquire.completionSource.TrySetResult(GetSlotRef(slotIndex).instance);
                ReleasePendingAcquireState(ref pendingAcquire);
                return true;
            }

            return false;
        }

        private static void CancelPendingAcquireCallback(object state)
        {
            PendingAcquireCancelState data = (PendingAcquireCancelState)state;
            data.pool?.CancelPendingAcquire(data.requestId, data.cancellationToken);
        }

        private void CancelPendingAcquire(int requestId, CancellationToken cancellationToken)
        {
            if (!_pendingAcquireIndexMap.TryGetValue(requestId, out int index))
            {
                return;
            }

            ref PendingAcquire pendingAcquire = ref _pendingAcquires[index];
            if (!pendingAcquire.active || pendingAcquire.requestId != requestId)
            {
                return;
            }

            PendingAcquire removed = RemovePendingAcquireAt(index);
            CancelCompletionAndReleaseState(ref removed, cancellationToken);
        }

        private void EnsurePendingAcquireCapacity(int required)
        {
            if (_pendingAcquires.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _pendingAcquires.Length << 1);
            PendingAcquire[] newPendingAcquires = SlotArrayPool<PendingAcquire>.Rent(newCapacity);
            int[] newFreeStack = SlotArrayPool<int>.Rent(newCapacity);
            Array.Clear(newPendingAcquires, 0, newCapacity);
            Array.Clear(newFreeStack, 0, newCapacity);
            Array.Copy(_pendingAcquires, 0, newPendingAcquires, 0, _pendingAcquireSlotCount);
            Array.Copy(_pendingAcquireFreeStack, 0, newFreeStack, 0, _pendingAcquireFreeTop);

            SlotArrayPool<PendingAcquire>.Return(_pendingAcquires, true);
            SlotArrayPool<int>.Return(_pendingAcquireFreeStack, true);
            _pendingAcquires = newPendingAcquires;
            _pendingAcquireFreeStack = newFreeStack;
        }

        private void CancelPendingAcquires(CancellationToken cancellationToken)
        {
            while (_pendingAcquireHead >= 0)
            {
                PendingAcquire pendingAcquire = RemovePendingAcquireAt(_pendingAcquireHead);
                CancelCompletionAndReleaseState(ref pendingAcquire, cancellationToken);
            }

            _pendingAcquireHead = -1;
            _pendingAcquireTail = -1;
            _pendingAcquireIndexMap.Dispose();
        }

        private int AllocPendingAcquireSlot()
        {
            if (_pendingAcquireFreeTop > 0)
            {
                return _pendingAcquireFreeStack[--_pendingAcquireFreeTop];
            }

            EnsurePendingAcquireCapacity(_pendingAcquireSlotCount + 1);
            return _pendingAcquireSlotCount++;
        }

        private PendingAcquire RemovePendingAcquireAt(int index)
        {
            PendingAcquire pendingAcquire = _pendingAcquires[index];
            if (!pendingAcquire.active)
            {
                return pendingAcquire;
            }

            int prev = pendingAcquire.prev;
            int next = pendingAcquire.next;
            if (prev >= 0)
            {
                _pendingAcquires[prev].next = next;
            }
            else
            {
                _pendingAcquireHead = next;
            }

            if (next >= 0)
            {
                _pendingAcquires[next].prev = prev;
            }
            else
            {
                _pendingAcquireTail = prev;
            }

            _pendingAcquireIndexMap.Remove(pendingAcquire.requestId);

            _pendingAcquires[index] = default;
            _pendingAcquireFreeStack[_pendingAcquireFreeTop++] = index;
            _pendingAcquireCount--;
            return pendingAcquire;
        }

        private static void CancelCompletionAndReleaseState(ref PendingAcquire pendingAcquire, CancellationToken cancellationToken)
        {
            pendingAcquire.completionSource?.TrySetCanceled(cancellationToken);
            ReleasePendingAcquireState(ref pendingAcquire);
        }

        private static void ReleasePendingAcquireState(ref PendingAcquire pendingAcquire)
        {
            pendingAcquire.cancellationRegistration.Dispose();
            if (pendingAcquire.cancellationState != null)
            {
                pendingAcquire.cancellationState.pool = null;
                MemoryPool.Release(pendingAcquire.cancellationState);
                pendingAcquire.cancellationState = null;
            }
        }

        private int CreateTrackedInstance()
        {
            int slotIndex = AllocSlot();
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.generation = ++_generationCounter;
            slot.state = SlotState.Inactive;
            slot.spawnTime = Time.time;
            slot.lastReleaseTime = Time.time;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            slot.instance = GameObject.Instantiate(_prefab);
            slot.transform = slot.instance.transform;
#if UNITY_EDITOR
            slot.instance.name = ZString.Format("{0}[Pool]", _prefab.name);
#endif
            slot.transform.SetParent(_root, false);
            if (slot.instance.activeSelf)
            {
                slot.instance.SetActive(false);
            }

            GameObjectPoolHandle handle = slot.instance.GetComponent<GameObjectPoolHandle>();
            if (handle == null)
            {
                handle = slot.instance.AddComponent<GameObjectPoolHandle>();
            }

            handle.Bind(this, slotIndex, slot.generation);
            slot.handle = handle;
            BindLifecycleCache(slot.transform, ref slot);
            _totalCount++;
            TouchPrefab();
            return slotIndex;
        }

        private void DestroyTrackedInstance(int slotIndex, bool countDestroy)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);

            if (slot.state == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            InvokeOnPoolDestroy(ref slot);
            if (slot.handle != null)
            {
                slot.handle.Detach();
            }

            if (slot.instance != null)
            {
                GameObject.Destroy(slot.instance);
            }

            ClearSlot(ref slot);
            slot.state = SlotState.Free;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            if (countDestroy)
            {
                _destroyCount++;
            }
        }

        private void RemoveSlot(int slotIndex, bool countDestroy)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);

            if (slot.state == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            ClearSlot(ref slot);
            slot.state = SlotState.Free;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            if (countDestroy)
            {
                _destroyCount++;
            }

            RestoreRuntimeHardCapacity();
            RefreshMaintenance(false);
        }

        private void RemoveExternallyDestroyedSlot(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Log.Warning(ZString.Format(
                "[GameObjectPool] Pooled object was destroyed outside pool module. Rule:{0}, Asset:{1}, Slot:{2}, Active:{3}",
                _rule.entryName,
                _assetPath,
                slotIndex,
                slot.state == SlotState.Active));
#endif

            RemoveFromInactive(slotIndex);
            if (slot.state == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            InvokeOnPoolDestroy(ref slot);
            if (slot.handle != null)
            {
                slot.handle.Detach();
            }

            ClearSlot(ref slot);
            slot.state = SlotState.Free;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            _destroyCount++;
            RestoreRuntimeHardCapacity();
            RefreshMaintenance(false);
        }

        private void RefreshMaintenance(bool lowMemory)
        {
            float dueTime = ComputeNextMaintenanceAt(Time.time, lowMemory);
            ScheduleMaintenance(dueTime);
        }

        private void ScheduleMaintenance(float dueTime)
        {
            _nextMaintenanceAt = dueTime;
            _service.ScheduleMaintenance(_poolIndex, dueTime, ref _maintenanceHeapIndex);
        }

        private float ComputeNextMaintenanceAt(float now, bool lowMemory)
        {
            if (lowMemory && _inactiveHead >= 0)
            {
                return now;
            }

            float nextDueTime = float.MaxValue;
            if (_inactiveHead >= 0)
            {
                if (_totalCount > _rule.softCapacity)
                {
                    return now;
                }

                ref Slot headSlot = ref GetSlotRef(_inactiveHead);
                nextDueTime = headSlot.lastReleaseTime + IdleTrimDelaySeconds;
            }

            if (_prefab != null && _totalCount == 0)
            {
                float unloadDue = GetPrefabUnloadDueTime();
                if (unloadDue < nextDueTime)
                {
                    nextDueTime = unloadDue;
                }
            }

            return nextDueTime;
        }

        private PoolRecyclePlan BuildRecyclePlan(float now, bool lowMemory)
        {
            float oldestIdle = _inactiveHead >= 0
                ? Mathf.Max(0f, now - GetSlotRef(_inactiveHead).lastReleaseTime)
                : 0f;

            var context = new PoolRecycleRuntimeContext(
                _rule.group,
                _assetPath,
                _rule.category,
                _activeCount,
                _inactiveCount,
                _totalCount,
                GetMinimumRetained(),
                _rule.softCapacity,
                _runtimeHardCapacity,
                PeakRetainPercentShort,
                PeakRetainPercentLong,
                PeakDecayFramesShort,
                PeakDecayFramesLong,
                1,
                false,
                _peakActiveShort,
                _peakActiveLong,
                _idleFrames,
                _hotFrames,
                oldestIdle,
                lowMemory);

            return BuildDefaultRecyclePlan(in context);
        }

        private PoolRecyclePlan BuildDefaultRecyclePlan(in PoolRecycleRuntimeContext context)
        {
            if (_idleFrames >= context.ShortPeakDecayFrames && _peakActiveShort > 0)
            {
                _peakActiveShort -= Mathf.Max(1, _peakActiveShort >> 4);
            }

            if (_idleFrames >= context.LongPeakDecayFrames && _peakActiveLong > 0)
            {
                _peakActiveLong -= Mathf.Max(1, _peakActiveLong >> 6);
            }

            int shortRetain = (_peakActiveShort * context.ShortPeakRetainPercent + 99) / 100;
            int longRetain = (_peakActiveLong * context.LongPeakRetainPercent + 99) / 100;
            int retainTarget = Mathf.Max(context.MinRetained, Mathf.Max(shortRetain, longRetain));
            retainTarget = Mathf.Min(retainTarget, context.SoftCapacity);
            if (context.RetainAllInactive)
            {
                retainTarget = Mathf.Max(retainTarget, context.InactiveCount);
            }

            if (context.LowMemory)
            {
                retainTarget = context.MinRetained;
            }

            int trimBudget = GetTrimBudgetBase();
            bool forceTrim = context.LowMemory;
            bool forceUnload = context.LowMemory;
            return new PoolRecyclePlan(retainTarget, trimBudget, forceTrim, forceUnload);
        }

        private bool ShouldTrimHead(float now, in PoolRecyclePlan plan)
        {
            if (_inactiveHead < 0)
            {
                return false;
            }

            if (_totalCount <= plan.RetainTarget)
            {
                return false;
            }

            if (plan.ForceTrim)
            {
                return true;
            }

            if (_totalCount > _rule.softCapacity)
            {
                return true;
            }

            ref Slot headSlot = ref GetSlotRef(_inactiveHead);
            return _inactiveCount > plan.RetainTarget && now - headSlot.lastReleaseTime >= IdleTrimDelaySeconds;
        }

        private bool ShouldUnloadPrefab(float now, in PoolRecyclePlan plan)
        {
            if (_prefab == null || _totalCount > 0)
            {
                return false;
            }

            if (plan.ForcePrefabUnload)
            {
                return true;
            }

            return now >= GetPrefabUnloadDueTime();
        }

        private void UpdateRetentionMetrics()
        {
            bool hot = _acquireSinceMaintain > 0 || _releaseSinceMaintain > 0 || _activeCount > 0;
            if (hot)
            {
                _hotFrames++;
                _idleFrames = 0;
            }
            else
            {
                _idleFrames++;
                _hotFrames = 0;
            }

            _acquireSinceMaintain = 0;
            _releaseSinceMaintain = 0;
        }

        private void RestoreRuntimeHardCapacity()
        {
            _runtimeHardCapacity = Mathf.Max(_rule.hardCapacity, _totalCount);
        }

        private int GetMinimumRetained()
        {
            return 0;
        }

        private int GetTrimBudgetBase()
        {
            return Mathf.Clamp(_rule.softCapacity >> 2, 1, TrimBudgetCap);
        }

        private bool EnsurePrefabLoaded()
        {
            if (_prefab != null)
            {
                TouchPrefab();
                return true;
            }

            if (_prefabLoading)
            {
                return false;
            }

            _prefab = _loader.LoadPrefab(_loadPath);
            _prefabLoading = false;
            if (_prefab != null)
            {
                ResetPrefabUsageWindow();
            }

            return _prefab != null;
        }

        private async UniTask<bool> EnsurePrefabLoadedAsync(CancellationToken cancellationToken)
        {
            if (_prefab != null)
            {
                TouchPrefab();
                return true;
            }

            if (_prefabLoading)
            {
                await _prefabLoadCompletionSource.Task.AttachExternalCancellation(cancellationToken);
                TouchPrefab();
                return _prefab != null;
            }

            _prefabLoading = true;
            _prefabLoadCompletionSource = new UniTaskCompletionSource<GameObject>();
            int loadVersion = _loadVersion;
            RunPrefabLoadAsync(loadVersion).Forget(OnPrefabLoadException);
            await _prefabLoadCompletionSource.Task.AttachExternalCancellation(cancellationToken);
            TouchPrefab();
            return _prefab != null;
        }

        private async UniTask RunPrefabLoadAsync(int loadVersion)
        {
            GameObject loadedPrefab = await _loader.LoadPrefabAsync(_loadPath, _service.ShutdownToken);
            if (_isShuttingDown || loadVersion != _loadVersion)
            {
                if (loadedPrefab != null)
                {
                    _loader.UnloadAsset(loadedPrefab);
                }

                return;
            }

            _prefab = loadedPrefab;
            _prefabLoading = false;
            UniTaskCompletionSource<GameObject> completionSource = _prefabLoadCompletionSource;
            _prefabLoadCompletionSource = null;
            completionSource?.TrySetResult(_prefab);
            if (_prefab != null)
            {
                ResetPrefabUsageWindow();
            }
        }

        private void OnPrefabLoadException(Exception exception)
        {
            _prefabLoading = false;
            UniTaskCompletionSource<GameObject> completionSource = _prefabLoadCompletionSource;
            _prefabLoadCompletionSource = null;
            completionSource?.TrySetException(exception);
        }

        private void TouchPrefab()
        {
            _lastPrefabTouchTime = Time.time;
        }

        private void ResetPrefabUsageWindow()
        {
            _lastPrefabTouchTime = Time.time;
            _lastWakeTime = -1f;
            _previousWakeTime = -1f;
            _wakeCountSincePrefabLoad = 0;
        }

        private void RecordWake()
        {
            float now = Time.time;
            _previousWakeTime = _lastWakeTime;
            _lastWakeTime = now;
            _wakeCountSincePrefabLoad++;
            _lastPrefabTouchTime = now;
        }

        private float GetPrefabReferenceTime()
        {
            return _lastWakeTime >= 0f ? _lastWakeTime : _lastPrefabTouchTime;
        }

        private float GetLastWakeGapDuration()
        {
            if (_lastWakeTime < 0f || _previousWakeTime < 0f)
            {
                return -1f;
            }

            return Mathf.Max(0f, _lastWakeTime - _previousWakeTime);
        }

        // Empty pools unload faster after sparse wake patterns, and stay loaded longer after bursty use.
        private float GetPrefabUnloadDelaySeconds()
        {
            if (_wakeCountSincePrefabLoad <= 0)
            {
                return PrefabUnloadDelayColdSeconds;
            }

            float lastWakeGap = GetLastWakeGapDuration();
            bool sparseWakeGap = lastWakeGap < 0f || lastWakeGap >= PrefabSparseWakeGapSeconds;
            if (_wakeCountSincePrefabLoad <= PrefabLowWakeCountThreshold)
            {
                return sparseWakeGap
                    ? PrefabUnloadDelayColdSeconds
                    : PrefabUnloadDelaySparseSeconds;
            }

            if (_wakeCountSincePrefabLoad <= PrefabWarmWakeCountThreshold && sparseWakeGap)
            {
                return PrefabUnloadDelaySparseSeconds;
            }

            return PrefabUnloadDelayHotSeconds;
        }

        private float GetPrefabUnloadDueTime()
        {
            if (_prefab == null || _totalCount > 0)
            {
                return float.MaxValue;
            }

            return GetPrefabReferenceTime() + GetPrefabUnloadDelaySeconds();
        }

        private void EnsureLifecycleCacheBuilt()
        {
            if (_poolableBindings != null || _prefab == null)
            {
                return;
            }

            _componentBuffer.Clear();
            _bindingBuildBuffer.Clear();
            _bindingIndexBuildBuffer.Clear();

            _prefab.GetComponentsInChildren(true, _transformBuffer);
            EnsureTransformBuildBufferCapacity(_transformBuffer.Count);
            int runningPoolableIndex = 0;
            for (int transformIndex = 0; transformIndex < _transformBuffer.Count; transformIndex++)
            {
                Transform transform = _transformBuffer[transformIndex];
                _transformBuildBuffer[transformIndex] = transform;
                if (transform == null)
                {
                    continue;
                }

                _componentBuffer.Clear();
                transform.GetComponents(_componentBuffer);
                int componentCount = _componentBuffer.Count;
                if (componentCount <= 0)
                {
                    continue;
                }

                _bindingIndexBuildBuffer.Clear();
                for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    if (_componentBuffer[componentIndex] is IGameObjectPoolable)
                    {
                        _bindingIndexBuildBuffer.Add(componentIndex);
                    }
                }

                int poolableCount = _bindingIndexBuildBuffer.Count;
                if (poolableCount <= 0)
                {
                    continue;
                }

                int[] componentIndices = SlotArrayPool<int>.Rent(poolableCount);
                for (int i = 0; i < poolableCount; i++)
                {
                    componentIndices[i] = _bindingIndexBuildBuffer[i];
                }

                _bindingBuildBuffer.Add(new PoolableTransformBinding
                {
                    transformIndex = transformIndex,
                    componentIndices = componentIndices,
                    poolableStartIndex = runningPoolableIndex,
                    poolableCount = poolableCount
                });

                runningPoolableIndex += poolableCount;
            }

            _poolableBindingCount = _bindingBuildBuffer.Count;
            _poolableCount = runningPoolableIndex;
            if (_poolableBindingCount <= 0)
            {
                _poolableBindings = Array.Empty<PoolableTransformBinding>();
                return;
            }

            _poolableBindings = new PoolableTransformBinding[_poolableBindingCount];
            for (int i = 0; i < _poolableBindingCount; i++)
            {
                _poolableBindings[i] = _bindingBuildBuffer[i];
            }
        }

        private void BindLifecycleCache(Transform instanceRoot, ref Slot slot)
        {
            EnsureLifecycleCacheBuilt();
            if (_poolableCount <= 0)
            {
                slot.poolables = null;
                return;
            }

            IGameObjectPoolable[] poolables = SlotArrayPool<IGameObjectPoolable>.Rent(_poolableCount);
            Array.Clear(poolables, 0, _poolableCount);
            instanceRoot.GetComponentsInChildren(true, _instanceTransformBuffer);

            for (int bindingIndex = 0; bindingIndex < _poolableBindingCount; bindingIndex++)
            {
                ref PoolableTransformBinding binding = ref _poolableBindings[bindingIndex];
                if ((uint)binding.transformIndex >= (uint)_instanceTransformBuffer.Count)
                {
                    continue;
                }

                Transform target = _instanceTransformBuffer[binding.transformIndex];

                _componentBuffer.Clear();
                target.GetComponents(_componentBuffer);
                for (int i = 0; i < binding.poolableCount; i++)
                {
                    int componentIndex = binding.componentIndices[i];
                    if ((uint)componentIndex < (uint)_componentBuffer.Count)
                    {
                        poolables[binding.poolableStartIndex + i] = _componentBuffer[componentIndex] as IGameObjectPoolable;
                    }
                }
            }

            slot.poolables = poolables;
            _instanceTransformBuffer.Clear();
        }

        private void EnsureTransformBuildBufferCapacity(int required)
        {
            if (_transformBuildBuffer != null && _transformBuildBuffer.Length >= required)
            {
                return;
            }

            if (_transformBuildBuffer != null)
            {
                SlotArrayPool<Transform>.Return(_transformBuildBuffer, true);
            }

            _transformBuildBuffer = SlotArrayPool<Transform>.Rent(Mathf.Max(1, required));
            Array.Clear(_transformBuildBuffer, 0, _transformBuildBuffer.Length);
        }

        private void InvokeOnPoolGet(ref Slot slot, in PoolSpawnContext context)
        {
            if (slot.poolables == null)
            {
                return;
            }

            for (int i = 0; i < slot.poolables.Length; i++)
            {
                slot.poolables[i]?.OnPoolGet(context);
            }
        }

        private void InvokeOnPoolRelease(ref Slot slot)
        {
            if (slot.poolables == null)
            {
                return;
            }

            for (int i = 0; i < slot.poolables.Length; i++)
            {
                slot.poolables[i]?.OnPoolRelease();
            }
        }

        private void InvokeOnPoolDestroy(ref Slot slot)
        {
            if (slot.poolables == null)
            {
                return;
            }

            for (int i = 0; i < slot.poolables.Length; i++)
            {
                slot.poolables[i]?.OnPoolDestroy();
            }
        }

        private static void ClearSlot(ref Slot slot)
        {
            if (slot.poolables != null)
            {
                SlotArrayPool<IGameObjectPoolable>.Return(slot.poolables, true);
            }

            slot.instance = null;
            slot.transform = null;
            slot.handle = null;
            slot.poolables = null;
            slot.spawnTime = 0f;
            slot.lastReleaseTime = 0f;
            slot.generation++;
        }

        private void AddToInactiveTail(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            if (_inactiveTail == slotIndex || slot.prevInactive >= 0 || slot.nextInactive >= 0)
            {
                return;
            }

            slot.prevInactive = _inactiveTail;
            slot.nextInactive = -1;
            if (_inactiveTail >= 0)
            {
                GetSlotRef(_inactiveTail).nextInactive = slotIndex;
            }
            else
            {
                _inactiveHead = slotIndex;
            }

            _inactiveTail = slotIndex;
            _inactiveCount++;
        }

        private void RemoveFromInactive(int slotIndex)
        {
            if (!IsValidIndex(slotIndex))
            {
                return;
            }

            ref Slot slot = ref GetSlotRef(slotIndex);
            if (_inactiveHead != slotIndex && slot.prevInactive < 0 && slot.nextInactive < 0)
            {
                return;
            }

            int prev = slot.prevInactive;
            int next = slot.nextInactive;
            if (prev >= 0)
            {
                GetSlotRef(prev).nextInactive = next;
            }
            else
            {
                _inactiveHead = next;
            }

            if (next >= 0)
            {
                GetSlotRef(next).prevInactive = prev;
            }
            else
            {
                _inactiveTail = prev;
            }

            slot.prevInactive = -1;
            slot.nextInactive = -1;
            _inactiveCount = Mathf.Max(0, _inactiveCount - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MakeIndex(int page, int offset)
        {
            return (page << PageBits) | offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PageOf(int index)
        {
            return index >> PageBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int OffsetOf(int index)
        {
            return index & PageMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Slot GetSlotRef(int index)
        {
            return ref _pages[PageOf(index)][OffsetOf(index)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidIndex(int index)
        {
            if (index < 0)
            {
                return false;
            }

            int page = PageOf(index);
            return page < _pageCount && _pages[page] != null;
        }

        private int AllocSlot()
        {
            while (_freePageTop > 0)
            {
                int candidatePage = _freePageStack[_freePageTop - 1];
                if (candidatePage >= 0 &&
                    candidatePage < _pageCount &&
                    _pages[candidatePage] != null &&
                    _pageFreeStacks[candidatePage] != null &&
                    _pageFreeTops[candidatePage] > 0)
                {
                    break;
                }

                RemoveFreePage(candidatePage);
            }

            if (_freePageTop <= 0)
            {
                AllocatePage();
            }

            int page = _freePageStack[_freePageTop - 1];
            int offset = _pageFreeStacks[page][--_pageFreeTops[page]];
            if (_pageFreeTops[page] <= 0)
            {
                RemoveFreePage(page);
            }

            _pageAliveCounts[page]++;
            return MakeIndex(page, offset);
        }

        private void FreeSlot(int index)
        {
            int page = PageOf(index);
            int offset = OffsetOf(index);
            _pageFreeStacks[page][_pageFreeTops[page]++] = offset;
            _pageAliveCounts[page]--;

            if (_pageFreeTops[page] == 1)
            {
                AddFreePage(page);
            }

            if (_pageAliveCounts[page] == 0)
            {
                AddEmptyPage(page);
                ReleaseEmptyPages(1);
            }
        }

        private void AllocatePage()
        {
            int page = GetReusablePageIndex();
            _pages[page] = SlotArrayPool<Slot>.Rent(PageSize);
            _pageFreeStacks[page] = SlotArrayPool<int>.Rent(PageSize);
            Array.Clear(_pages[page], 0, PageSize);
            for (int i = 0; i < PageSize; i++)
            {
                _pageFreeStacks[page][i] = PageSize - 1 - i;
                _pages[page][i].prevInactive = -1;
                _pages[page][i].nextInactive = -1;
            }

            _pageFreeTops[page] = PageSize;
            _pageAliveCounts[page] = 0;
            _pageFlags[page] = PageAllocated;
            AddFreePage(page);
        }

        private int GetReusablePageIndex()
        {
            if (_emptyPageTop > 0)
            {
                int page = _emptyPageStack[--_emptyPageTop];
                _pageFlags[page] = (byte)(_pageFlags[page] & ~PageInEmptyStack);
                return page;
            }

            if (_pageCount >= _pages.Length)
            {
                ExpandPageStorage(_pages.Length << 1);
            }

            return _pageCount++;
        }

        private void ExpandPageStorage(int required)
        {
            int newCapacity = Mathf.Max(required, _pages.Length << 1);
            Slot[][] newPages = SlotArrayPool<Slot[]>.Rent(newCapacity);
            int[][] newPageFreeStacks = SlotArrayPool<int[]>.Rent(newCapacity);
            int[] newPageAliveCounts = SlotArrayPool<int>.Rent(newCapacity);
            int[] newPageFreeTops = SlotArrayPool<int>.Rent(newCapacity);
            byte[] newPageFlags = SlotArrayPool<byte>.Rent(newCapacity);
            int[] newFreePageStack = SlotArrayPool<int>.Rent(newCapacity);
            int[] newFreePageStackIndices = SlotArrayPool<int>.Rent(newCapacity);
            int[] newEmptyPageStack = SlotArrayPool<int>.Rent(newCapacity);

            Array.Clear(newPages, 0, newCapacity);
            Array.Clear(newPageFreeStacks, 0, newCapacity);
            Array.Clear(newPageAliveCounts, 0, newCapacity);
            Array.Clear(newPageFreeTops, 0, newCapacity);
            Array.Clear(newPageFlags, 0, newCapacity);
            Array.Clear(newFreePageStack, 0, newCapacity);
            Array.Clear(newFreePageStackIndices, 0, newCapacity);
            Array.Clear(newEmptyPageStack, 0, newCapacity);

            Array.Copy(_pages, 0, newPages, 0, _pageCount);
            Array.Copy(_pageFreeStacks, 0, newPageFreeStacks, 0, _pageCount);
            Array.Copy(_pageAliveCounts, 0, newPageAliveCounts, 0, _pageCount);
            Array.Copy(_pageFreeTops, 0, newPageFreeTops, 0, _pageCount);
            Array.Copy(_pageFlags, 0, newPageFlags, 0, _pageCount);
            Array.Copy(_freePageStack, 0, newFreePageStack, 0, _freePageTop);
            Array.Copy(_freePageStackIndices, 0, newFreePageStackIndices, 0, _pageCount);
            Array.Copy(_emptyPageStack, 0, newEmptyPageStack, 0, _emptyPageTop);

            SlotArrayPool<Slot[]>.Return(_pages, true);
            SlotArrayPool<int[]>.Return(_pageFreeStacks, true);
            SlotArrayPool<int>.Return(_pageAliveCounts, true);
            SlotArrayPool<int>.Return(_pageFreeTops, true);
            SlotArrayPool<byte>.Return(_pageFlags, true);
            SlotArrayPool<int>.Return(_freePageStack, true);
            SlotArrayPool<int>.Return(_freePageStackIndices, true);
            SlotArrayPool<int>.Return(_emptyPageStack, true);

            _pages = newPages;
            _pageFreeStacks = newPageFreeStacks;
            _pageAliveCounts = newPageAliveCounts;
            _pageFreeTops = newPageFreeTops;
            _pageFlags = newPageFlags;
            _freePageStack = newFreePageStack;
            _freePageStackIndices = newFreePageStackIndices;
            _emptyPageStack = newEmptyPageStack;
        }

        private void AddFreePage(int page)
        {
            if ((_pageFlags[page] & PageInFreeStack) != 0)
            {
                return;
            }

            _pageFlags[page] = (byte)(_pageFlags[page] | PageInFreeStack);
            _freePageStackIndices[page] = _freePageTop;
            _freePageStack[_freePageTop++] = page;
        }

        private void RemoveFreePage(int page)
        {
            if ((_pageFlags[page] & PageInFreeStack) == 0)
            {
                return;
            }

            _pageFlags[page] = (byte)(_pageFlags[page] & ~PageInFreeStack);
            int index = _freePageStackIndices[page];
            if ((uint)index >= (uint)_freePageTop || _freePageStack[index] != page)
            {
                _freePageStackIndices[page] = 0;
                return;
            }

            _freePageTop--;
            int movedPage = _freePageStack[_freePageTop];
            _freePageStack[index] = movedPage;
            _freePageStack[_freePageTop] = 0;
            _freePageStackIndices[page] = 0;
            if (index < _freePageTop)
            {
                _freePageStackIndices[movedPage] = index;
            }
        }

        private void AddEmptyPage(int page)
        {
            if ((_pageFlags[page] & PageInEmptyStack) != 0)
            {
                return;
            }

            _pageFlags[page] = (byte)(_pageFlags[page] | PageInEmptyStack);
            _emptyPageStack[_emptyPageTop++] = page;
        }

        private void ReleaseEmptyPages(int budget)
        {
            while (budget > 0 && _emptyPageTop > 0)
            {
                int page = _emptyPageStack[--_emptyPageTop];
                _pageFlags[page] = (byte)(_pageFlags[page] & ~PageInEmptyStack);
                if (_pages[page] == null || _pageAliveCounts[page] != 0)
                {
                    continue;
                }

                RemoveFreePage(page);
                SlotArrayPool<Slot>.Return(_pages[page], true);
                SlotArrayPool<int>.Return(_pageFreeStacks[page], true);
                _pages[page] = null;
                _pageFreeStacks[page] = null;
                _pageFreeTops[page] = 0;
                _pageAliveCounts[page] = 0;
                _pageFlags[page] = 0;
                budget--;
            }
        }

        private void ReturnAllPages()
        {
            if (_pages == null)
            {
                return;
            }

            for (int page = 0; page < _pageCount; page++)
            {
                if (_pages[page] != null)
                {
                    SlotArrayPool<Slot>.Return(_pages[page], true);
                    _pages[page] = null;
                }

                if (_pageFreeStacks[page] != null)
                {
                    SlotArrayPool<int>.Return(_pageFreeStacks[page], true);
                    _pageFreeStacks[page] = null;
                }
            }
        }

        private void ReturnStorageArrays()
        {
            if (_pages != null)
            {
                SlotArrayPool<Slot[]>.Return(_pages, true);
                _pages = null;
            }

            if (_pageFreeStacks != null)
            {
                SlotArrayPool<int[]>.Return(_pageFreeStacks, true);
                _pageFreeStacks = null;
            }

            if (_pageAliveCounts != null)
            {
                SlotArrayPool<int>.Return(_pageAliveCounts, true);
                _pageAliveCounts = null;
            }

            if (_pageFreeTops != null)
            {
                SlotArrayPool<int>.Return(_pageFreeTops, true);
                _pageFreeTops = null;
            }

            if (_pageFlags != null)
            {
                SlotArrayPool<byte>.Return(_pageFlags, true);
                _pageFlags = null;
            }

            if (_freePageStack != null)
            {
                SlotArrayPool<int>.Return(_freePageStack, true);
                _freePageStack = null;
            }

            if (_freePageStackIndices != null)
            {
                SlotArrayPool<int>.Return(_freePageStackIndices, true);
                _freePageStackIndices = null;
            }

            if (_emptyPageStack != null)
            {
                SlotArrayPool<int>.Return(_emptyPageStack, true);
                _emptyPageStack = null;
            }

            if (_pendingAcquires != null)
            {
                SlotArrayPool<PendingAcquire>.Return(_pendingAcquires, true);
                _pendingAcquires = null;
            }

            if (_pendingAcquireFreeStack != null)
            {
                SlotArrayPool<int>.Return(_pendingAcquireFreeStack, true);
                _pendingAcquireFreeStack = null;
            }

            _pageCount = 0;
            _freePageTop = 0;
            _emptyPageTop = 0;
            _pendingAcquireHead = -1;
            _pendingAcquireTail = -1;
            _pendingAcquireCount = 0;
            _pendingAcquireFreeTop = 0;
            _pendingAcquireSlotCount = 0;
            _pendingAcquireRequestId = 0;
            _pendingAcquireIndexMap.Dispose();
        }

        private void ClearLifecycleBindings()
        {
            if (_poolableBindings == null)
            {
                return;
            }

            for (int i = 0; i < _poolableBindingCount; i++)
            {
                if (_poolableBindings[i].componentIndices != null)
                {
                    SlotArrayPool<int>.Return(_poolableBindings[i].componentIndices, true);
                    _poolableBindings[i].componentIndices = null;
                }
            }

            _poolableBindings = null;
            if (_transformBuildBuffer != null)
            {
                SlotArrayPool<Transform>.Return(_transformBuildBuffer, true);
                _transformBuildBuffer = null;
            }

            _poolableBindingCount = 0;
            _poolableCount = 0;
            _componentBuffer.Clear();
            _transformBuffer.Clear();
            _instanceTransformBuffer.Clear();
            _bindingBuildBuffer.Clear();
            _bindingIndexBuildBuffer.Clear();
        }

        private static int CompareInstanceSnapshot(GameObjectPoolInstanceSnapshot left, GameObjectPoolInstanceSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            if (left.isActive != right.isActive)
            {
                return left.isActive ? -1 : 1;
            }

            return string.Compare(left.instanceName, right.instanceName, StringComparison.Ordinal);
        }
    }
}
