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
    internal sealed class RuntimeGameObjectPool : MemoryObject
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
            public int poolableCount;
            public float spawnTime;
            public float lastReleaseTime;
            public int prevInactive;
            public int nextInactive;
            public uint generation;
            public SlotState state;
        }

        private const int PageBits = 7;
        private const int PageSize = 1 << PageBits;
        private const int InitialPageCapacity = 4;
        private const int WarmupCreateBatch = 8;
        private const float WarmupFrameBudgetSeconds = 0.001f;
        private static readonly Comparison<GameObjectPoolInstanceSnapshot> InstanceComparer = CompareInstanceSnapshot;

        private GameObjectPoolService _service;
        private IPrefabLoader _loader;
        private PoolCompiledRule _rule;
        private int _poolIndex;
        private string _location;
        private Transform _root;
        private GameObject _prefab;
        private UniTaskCompletionSource<GameObject> _prefabLoadCompletionSource;
        private bool _isShuttingDown;
        private int _loadVersion;
        private float _nextMaintenanceAt;
        private int _maintenanceHeapIndex;
        private Slot[][] _pages;
        private int[][] _pageFreeStacks;
        private int[] _pageAliveCounts;
        private int[] _pageFreeTops;
        private int _pageCount;
        private int[] _freePageStack;
        private int _freePageTop;
        private int _inactiveHead;
        private int _inactiveTail;
        private int _activeCount;
        private int _inactiveCount;
        private int _totalCount;
        private int _retainTarget;
        private int _spawnCount;
        private int _despawnCount;
        private int _hitCount;
        private int _missCount;
        private int _expandCount;
        private int _destroyCount;
        private int _peakActive;
        private uint _generationCounter;
        private readonly List<IGameObjectPoolable> _poolableBuffer = new List<IGameObjectPoolable>(8);

        public string Location => _location;
        public string Group => _rule.Group;
        public int TotalCount => _totalCount;
        public int ActiveCount => _activeCount;
        public int InactiveCount => _inactiveCount;
        public bool IsPrefabLoaded => _prefab != null;
        public float NextMaintenanceAt => _nextMaintenanceAt;

        public void Initialize(
            GameObjectPoolService service,
            int poolIndex,
            in PoolCompiledRule rule,
            string location,
            IPrefabLoader loader,
            Transform inactiveRoot)
        {
            _service = service;
            _poolIndex = poolIndex;
            _rule = rule;
            _location = location;
            _loader = loader;
            _root = inactiveRoot;
            _retainTarget = rule.MinIdle;
            _nextMaintenanceAt = float.MaxValue;
            _maintenanceHeapIndex = -1;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _pages = SlotArrayPool<Slot[]>.Rent(InitialPageCapacity);
            _pageFreeStacks = SlotArrayPool<int[]>.Rent(InitialPageCapacity);
            _pageAliveCounts = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _pageFreeTops = SlotArrayPool<int>.Rent(InitialPageCapacity);
            _freePageStack = SlotArrayPool<int>.Rent(InitialPageCapacity);
            Array.Clear(_pages, 0, InitialPageCapacity);
            Array.Clear(_pageFreeStacks, 0, InitialPageCapacity);
            Array.Clear(_pageAliveCounts, 0, InitialPageCapacity);
            Array.Clear(_pageFreeTops, 0, InitialPageCapacity);
            Array.Clear(_freePageStack, 0, InitialPageCapacity);
            ScheduleMaintenance(float.MaxValue);
        }

        public GameObject Spawn(Transform parent)
        {
            if (_prefab == null)
            {
                return null;
            }

            return SpawnPrepared(parent);
        }

        public async UniTask<GameObject> SpawnAsync(Transform parent, CancellationToken cancellationToken)
        {
            if (await LoadPrefabAsync(cancellationToken) == null)
            {
                return null;
            }

            return SpawnPrepared(parent);
        }

        public GameObject LoadPrefab()
        {
            if (_prefab == null && _prefabLoadCompletionSource == null)
            {
                _prefab = _loader.LoadPrefab(_location);
            }

            return _prefab;
        }

        public async UniTask<GameObject> LoadPrefabAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_prefab != null)
            {
                return _prefab;
            }

            int loadVersion = _loadVersion;
            UniTaskCompletionSource<GameObject> completion = _prefabLoadCompletionSource;
            if (completion == null)
            {
                completion = new UniTaskCompletionSource<GameObject>();
                _prefabLoadCompletionSource = completion;
                RunPrefabLoadAsync(loadVersion, _loader, _location, completion).Forget();
            }

            await completion.Task.AttachExternalCancellation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_isShuttingDown || loadVersion != _loadVersion)
            {
                throw new OperationCanceledException();
            }

            return _prefab;
        }

        public async UniTask WarmupAsync(int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int loadVersion = _loadVersion;
            int target = Mathf.Min(Mathf.Max(0, count), _rule.HardCapacity);
            if (target <= 0 || _inactiveCount >= target)
            {
                return;
            }

            if (await LoadPrefabAsync(cancellationToken) == null)
            {
                return;
            }

            int createdThisFrame = 0;
            float frameStart = Time.realtimeSinceStartup;
            try
            {
                while (_inactiveCount < target && _totalCount < _rule.HardCapacity)
                {
                    int slotIndex = CreateTrackedInstance();
                    if (slotIndex < 0)
                    {
                        break;
                    }

                    ParkInactive(slotIndex);
                    createdThisFrame++;
                    if (createdThisFrame >= WarmupCreateBatch || Time.realtimeSinceStartup - frameStart >= WarmupFrameBudgetSeconds)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                        if (_isShuttingDown || loadVersion != _loadVersion)
                        {
                            throw new OperationCanceledException();
                        }

                        createdThisFrame = 0;
                        frameStart = Time.realtimeSinceStartup;
                    }
                }
            }
            finally
            {
                if (!_isShuttingDown && loadVersion == _loadVersion)
                {
                    RefreshMaintenance();
                }
            }
        }

        public bool ReleaseFromHandle(GameObjectPoolHandle handle)
        {
            if (handle == null || !IsValidIndex(handle.SlotIndex))
            {
                return false;
            }

            ref Slot slot = ref GetSlotRef(handle.SlotIndex);
            if (slot.handle != handle || slot.generation != handle.Generation || slot.state != SlotState.Active)
            {
                return false;
            }

            ReleaseTrackedInstance(handle.SlotIndex);
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Log.Warning(ZString.Format(
                "[GameObjectPool] Pooled object destroyed outside pool. Rule:{0}, Location:{1}",
                _rule.EntryName,
                _location));
#endif
            RemoveDestroyedSlot(slotIndex);
        }

        public void SetMaintenanceHeapIndex(int heapIndex)
        {
            _maintenanceHeapIndex = heapIndex;
        }

        public void ExecuteMaintenance(float now, bool lowMemory)
        {
            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in _rule, _totalCount, lowMemory);
            _retainTarget = Mathf.Clamp(plan.RetainTarget, _rule.MinIdle, _rule.HardCapacity);

            int budget = Mathf.Max(1, plan.TrimBudget);
            while (_inactiveHead >= 0 && budget > 0 && ShouldTrimHead(now, in plan))
            {
                DestroyTrackedInstance(_inactiveHead);
                budget--;
            }

            if (_prefab != null && _totalCount == 0 && plan.UnloadPrefab)
            {
                _loader.UnloadPrefab(_prefab);
                _prefab = null;
            }

            RefreshMaintenance();
        }

        public void Flush()
        {
            ExecuteMaintenance(Time.time, true);
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            _loadVersion++;
            _prefabLoadCompletionSource?.TrySetCanceled();
            _prefabLoadCompletionSource = null;
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
                    ref Slot slot = ref pageSlots[offset];
                    if (slot.state == SlotState.Free && slot.instance == null)
                    {
                        continue;
                    }

                    try
                    {
                        InvokeOnPooledDestroy(ref slot);
                    }
                    finally
                    {
                        slot.handle?.Detach();
                        if (slot.instance != null)
                        {
                            slot.instance.SafeDestroySelf();
                        }

                        ClearSlot(ref slot);
                        _destroyCount++;
                    }
                }
            }

            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;
            if (_prefab != null)
            {
                _loader.UnloadPrefab(_prefab);
                _prefab = null;
            }
        }

        public GameObjectPoolSnapshot CreateSnapshot(bool includeInstances)
        {
            float now = Time.time;
            var snapshot = MemoryPool.Acquire<GameObjectPoolSnapshot>();
            snapshot.entryName = _rule.EntryName;
            snapshot.group = _rule.Group;
            snapshot.location = _location;
            snapshot.policy = _rule.Policy;
            snapshot.minIdle = _rule.MinIdle;
            snapshot.retainTarget = _retainTarget;
            snapshot.softCapacity = _rule.SoftCapacity;
            snapshot.hardCapacity = _rule.HardCapacity;
            snapshot.unloadPrefab = _rule.UnloadPrefab;
            snapshot.totalCount = _totalCount;
            snapshot.activeCount = _activeCount;
            snapshot.inactiveCount = _inactiveCount;
            snapshot.prefabLoaded = _prefab != null;
            snapshot.nextMaintenanceIn = _nextMaintenanceAt >= float.MaxValue ? -1f : Mathf.Max(0f, _nextMaintenanceAt - now);
            snapshot.spawnCount = _spawnCount;
            snapshot.despawnCount = _despawnCount;
            snapshot.hitCount = _hitCount;
            snapshot.missCount = _missCount;
            snapshot.expandCount = _expandCount;
            snapshot.destroyCount = _destroyCount;
            snapshot.peakActive = _peakActive;
            if (includeInstances)
            {
                FillInstances(snapshot, now);
            }

            return snapshot;
        }

        public void FillInstances(GameObjectPoolSnapshot snapshot)
        {
            FillInstances(snapshot, Time.time);
        }

        public override void Clear()
        {
            ReturnStorage();
            _prefabLoadCompletionSource?.TrySetCanceled();
            _prefabLoadCompletionSource = null;
            _service = null;
            _loader = null;
            _rule = default;
            _poolIndex = 0;
            _location = null;
            _root = null;
            _prefab = null;
            _isShuttingDown = false;
            _loadVersion++;
            _nextMaintenanceAt = float.MaxValue;
            _maintenanceHeapIndex = -1;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;
            _retainTarget = 0;
            _spawnCount = 0;
            _despawnCount = 0;
            _hitCount = 0;
            _missCount = 0;
            _expandCount = 0;
            _destroyCount = 0;
            _peakActive = 0;
            _generationCounter = 0;
        }

        private GameObject SpawnPrepared(Transform parent)
        {
            _spawnCount++;
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
                slotIndex = CreateTrackedInstance();
                if (slotIndex < 0)
                {
                    RefreshMaintenance();
                    return null;
                }
            }

            ActivateTrackedInstance(slotIndex, parent);
            if (_activeCount > _peakActive)
            {
                _peakActive = _activeCount;
            }

            RefreshMaintenance();
            return GetSlotRef(slotIndex).instance;
        }

        private void ActivateTrackedInstance(int slotIndex, Transform parent)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.state = SlotState.Active;
            _activeCount++;
            slot.transform.SetParent(parent, false);
            if (!slot.instance.activeSelf)
            {
                slot.instance.SetActive(true);
            }

            var context = new PoolSpawnContext(_location, _rule.Group, parent, (uint)Time.frameCount);
            try
            {
                InvokeOnSpawn(ref slot, in context);
            }
            catch
            {
                _activeCount--;
                if (slot.instance != null && slot.instance.activeSelf)
                {
                    slot.instance.SetActive(false);
                }

                ParkInactive(slotIndex);
                RefreshMaintenance();
                throw;
            }
        }

        private void ReleaseTrackedInstance(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.state != SlotState.Active)
            {
                return;
            }

            _despawnCount++;
            _activeCount--;
            try
            {
                InvokeOnDespawn(ref slot);
            }
            finally
            {
                if (slot.instance != null && slot.instance.activeSelf)
                {
                    slot.instance.SetActive(false);
                }

                ParkInactive(slotIndex);
                RefreshMaintenance();
            }
        }

        private void ParkInactive(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.state = SlotState.Inactive;
            slot.lastReleaseTime = Time.time;
            slot.transform.SetParent(_root, false);
            AddToInactiveTail(slotIndex);
        }

        private int CreateTrackedInstance()
        {
            if (_totalCount >= _rule.HardCapacity)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Warning(ZString.Format(
                    "[GameObjectPool] HardCapacity reached. Rule:{0}, Location:{1}, Hard:{2}",
                    _rule.EntryName,
                    _location,
                    _rule.HardCapacity));
#endif
                return -1;
            }

            int slotIndex = AllocSlot();
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.generation = ++_generationCounter;
            slot.state = SlotState.Inactive;
            slot.spawnTime = Time.time;
            slot.lastReleaseTime = Time.time;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            slot.instance = UnityEngine.Object.Instantiate(_prefab);
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
            CachePoolables(ref slot);
            _totalCount++;
            _expandCount++;
            return slotIndex;
        }

        private void DestroyTrackedInstance(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);
            if (slot.state == SlotState.Active)
            {
                _activeCount--;
            }

            try
            {
                InvokeOnPooledDestroy(ref slot);
            }
            finally
            {
                slot.handle?.Detach();
                if (slot.instance != null)
                {
                    slot.instance.SafeDestroySelf();
                }

                ClearSlot(ref slot);
                FreeSlot(slotIndex);
                _totalCount--;
                _destroyCount++;
            }
        }

        private void RemoveDestroyedSlot(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);
            if (slot.state == SlotState.Active)
            {
                _activeCount--;
            }

            try
            {
                InvokeOnPooledDestroy(ref slot);
            }
            finally
            {
                slot.handle?.Detach();
                ClearSlot(ref slot);
                FreeSlot(slotIndex);
                _totalCount--;
                _destroyCount++;
                RefreshMaintenance();
            }
        }

        private bool ShouldTrimHead(float now, in PoolRecyclePlan plan)
        {
            if (_inactiveHead < 0 || _totalCount <= plan.RetainTarget)
            {
                return false;
            }

            if (plan.ForceTrim || _rule.Policy == PoolPolicy.Fixed || _totalCount > _rule.SoftCapacity)
            {
                return true;
            }

            if (_rule.Policy == PoolPolicy.Sticky)
            {
                return false;
            }

            float deadline = GetSlotRef(_inactiveHead).lastReleaseTime + _rule.IdleSeconds;
            return now >= deadline;
        }

        private void RefreshMaintenance()
        {
            float now = Time.time;
            float due = float.MaxValue;
            if (_rule.Policy != PoolPolicy.Sticky)
            {
                int retain = Mathf.Max(_rule.MinIdle, _retainTarget);
                if (_inactiveHead >= 0 && _totalCount > retain)
                {
                    due = _rule.Policy == PoolPolicy.Fixed || _totalCount > _rule.SoftCapacity
                        ? now
                        : GetSlotRef(_inactiveHead).lastReleaseTime + _rule.IdleSeconds;
                }
                else if (_prefab != null && _totalCount == 0 && _rule.UnloadPrefab)
                {
                    due = _rule.Policy == PoolPolicy.Burst ? now + _rule.IdleSeconds : now;
                }
            }

            ScheduleMaintenance(due);
        }

        private void ScheduleMaintenance(float dueTime)
        {
            _nextMaintenanceAt = dueTime;
            _service.ScheduleMaintenance(_poolIndex, dueTime, ref _maintenanceHeapIndex);
        }

        private async UniTaskVoid RunPrefabLoadAsync(int loadVersion, IPrefabLoader loader, string location,
            UniTaskCompletionSource<GameObject> completionSource)
        {
            GameObject loaded = null;
            try
            {
                loaded = await loader.LoadPrefabAsync(location);
            }
            catch
            {
                loaded = null;
            }

            if (_isShuttingDown || loadVersion != _loadVersion)
            {
                if (loaded != null)
                {
                    loader.UnloadPrefab(loaded);
                }

                completionSource.TrySetCanceled();
                return;
            }

            _prefab = loaded;
            _prefabLoadCompletionSource = null;
            completionSource.TrySetResult(_prefab);
        }

        private void CachePoolables(ref Slot slot)
        {
            _poolableBuffer.Clear();
            slot.instance.GetComponentsInChildren(true, _poolableBuffer);
            slot.poolableCount = _poolableBuffer.Count;
            if (slot.poolableCount == 0)
            {
                slot.poolables = null;
                return;
            }

            slot.poolables = SlotArrayPool<IGameObjectPoolable>.Rent(slot.poolableCount);
            for (int i = 0; i < slot.poolableCount; i++)
            {
                slot.poolables[i] = _poolableBuffer[i];
            }
        }

        private static void InvokeOnSpawn(ref Slot slot, in PoolSpawnContext context)
        {
            for (int i = 0; i < slot.poolableCount; i++)
            {
                slot.poolables[i].OnSpawn(in context);
            }
        }

        private static void InvokeOnDespawn(ref Slot slot)
        {
            for (int i = 0; i < slot.poolableCount; i++)
            {
                slot.poolables[i].OnDespawn();
            }
        }

        private static void InvokeOnPooledDestroy(ref Slot slot)
        {
            for (int i = 0; i < slot.poolableCount; i++)
            {
                try
                {
                    slot.poolables[i].OnPooledDestroy();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void AddToInactiveTail(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
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
            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.state != SlotState.Inactive)
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
            _inactiveCount--;
        }

        private void FillInstances(GameObjectPoolSnapshot snapshot, float now)
        {
            snapshot.ClearInstances();
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
                    instanceSnapshot.idleDuration = slot.state == SlotState.Active ? 0f : Mathf.Max(0f, now - slot.lastReleaseTime);
                    instanceSnapshot.lifeDuration = Mathf.Max(0f, now - slot.spawnTime);
                    instanceSnapshot.gameObject = slot.instance;
                    snapshot.instances.Add(instanceSnapshot);
                }
            }

            snapshot.instances.Sort(InstanceComparer);
        }

        private static int CompareInstanceSnapshot(GameObjectPoolInstanceSnapshot left, GameObjectPoolInstanceSnapshot right)
        {
            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int state = right.isActive.CompareTo(left.isActive);
            return state != 0 ? state : string.CompareOrdinal(left.instanceName, right.instanceName);
        }

        private void ClearSlot(ref Slot slot)
        {
            if (slot.poolables != null)
            {
                SlotArrayPool<IGameObjectPoolable>.Return(slot.poolables, true);
            }

            slot = default;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            slot.state = SlotState.Free;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Slot GetSlotRef(int index)
        {
            return ref _pages[index >> PageBits][index & (PageSize - 1)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidIndex(int index)
        {
            return index >= 0 && (index >> PageBits) < _pageCount && _pages[index >> PageBits] != null;
        }

        private int AllocSlot()
        {
            if (_freePageTop <= 0)
            {
                AllocatePage();
            }

            int page = _freePageStack[_freePageTop - 1];
            int offset = _pageFreeStacks[page][--_pageFreeTops[page]];
            if (_pageFreeTops[page] <= 0)
            {
                _freePageTop--;
            }

            _pageAliveCounts[page]++;
            return (page << PageBits) | offset;
        }

        private void FreeSlot(int index)
        {
            int page = index >> PageBits;
            int offset = index & (PageSize - 1);
            if (_pageFreeTops[page] == 0)
            {
                _freePageStack[_freePageTop++] = page;
            }

            _pageFreeStacks[page][_pageFreeTops[page]++] = offset;
            _pageAliveCounts[page]--;
        }

        private void AllocatePage()
        {
            EnsurePageCapacity(_pageCount + 1);
            int page = _pageCount++;
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
            _freePageStack[_freePageTop++] = page;
        }

        private void EnsurePageCapacity(int required)
        {
            if (_pages.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _pages.Length << 1);
            GrowArray(ref _pages, newCapacity);
            GrowArray(ref _pageFreeStacks, newCapacity);
            GrowArray(ref _pageAliveCounts, newCapacity);
            GrowArray(ref _pageFreeTops, newCapacity);
            GrowArray(ref _freePageStack, newCapacity);
        }

        private static void GrowArray<T>(ref T[] array, int newCapacity)
        {
            T[] grown = SlotArrayPool<T>.Rent(newCapacity);
            Array.Clear(grown, 0, newCapacity);
            if (array != null)
            {
                Array.Copy(array, 0, grown, 0, array.Length);
                SlotArrayPool<T>.Return(array, true);
            }

            array = grown;
        }

        private void ReturnStorage()
        {
            for (int page = 0; page < _pageCount; page++)
            {
                if (_pages[page] != null)
                {
                    for (int offset = 0; offset < PageSize; offset++)
                    {
                        if (_pages[page][offset].poolables != null)
                        {
                            SlotArrayPool<IGameObjectPoolable>.Return(_pages[page][offset].poolables, true);
                        }
                    }

                    SlotArrayPool<Slot>.Return(_pages[page], true);
                    SlotArrayPool<int>.Return(_pageFreeStacks[page], true);
                }
            }

            SlotArrayPool<Slot[]>.Return(_pages, true);
            SlotArrayPool<int[]>.Return(_pageFreeStacks, true);
            SlotArrayPool<int>.Return(_pageAliveCounts, true);
            SlotArrayPool<int>.Return(_pageFreeTops, true);
            SlotArrayPool<int>.Return(_freePageStack, true);
            _pages = null;
            _pageFreeStacks = null;
            _pageAliveCounts = null;
            _pageFreeTops = null;
            _freePageStack = null;
            _pageCount = 0;
            _freePageTop = 0;
        }
    }
}
