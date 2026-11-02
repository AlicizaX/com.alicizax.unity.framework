using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    internal sealed class RuntimeGameObjectPool
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
            public SlotState state;
            public bool ownsVisibility;
        }

        private const int InitialSlotCapacity = 8;
        private const int WarmupCreateBatch = 8;
        private const float WarmupFrameBudgetSeconds = 0.001f;
        private static readonly Comparison<GameObjectPoolInstanceSnapshot> InstanceComparer = CompareInstanceSnapshot;

        private readonly GameObjectPoolService _service;
        private readonly IPrefabLoader _loader;
        private readonly PoolCompiledRule _rule;
        private readonly int _poolIndex;
        private readonly string _location;
        private readonly Transform _root;
        private readonly List<IGameObjectPoolable> _poolableBuffer = new List<IGameObjectPoolable>(8);
        private GameObject _prefab;
        private UniTaskCompletionSource<GameObject> _prefabLoadCompletionSource;
        private bool _isShuttingDown;
        private int _loadVersion;
        private Slot[] _slots = new Slot[InitialSlotCapacity];
        private int[] _freeSlots = new int[InitialSlotCapacity];
        private int _freeSlotCount;
        private int _slotCount;
        private int _inactiveHead = -1;
        private int _inactiveTail = -1;
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

        internal int MaintenanceHeapIndex = -1;

        public string Location => _location;
        public string Group => _rule.Group;
        public int TotalCount => _totalCount;
        public int ActiveCount => _activeCount;
        public int InactiveCount => _inactiveCount;
        public bool IsPrefabLoaded => _prefab != null;
        public float NextMaintenanceAt => _service.GetMaintenanceDueTime(MaintenanceHeapIndex);

        public RuntimeGameObjectPool(
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
            _retainTarget = PoolPolicyPlanner.Plan(in rule, 0, false).RetainTarget;
        }

        internal int PoolIndex => _poolIndex;

        public GameObject Spawn(Transform parent)
        {
            if (_prefab == null)
            {
                return null;
            }

            _spawnCount++;
            int slotIndex = _inactiveTail;
            if (slotIndex >= 0)
            {
                RemoveFromInactive(slotIndex);
                _hitCount++;
            }
            else
            {
                _missCount++;
                slotIndex = CreateTrackedInstance();
                if (slotIndex < 0)
                {
                    return null;
                }
            }

            ref Slot slot = ref _slots[slotIndex];
            slot.state = SlotState.Active;
            GameObject instance = slot.instance;
            Transform transform = slot.transform;
            IGameObjectPoolable[] poolables = slot.poolables;
            if (++_activeCount > _peakActive)
            {
                _peakActive = _activeCount;
            }

            transform.SetParent(parent, false);
            if (!instance.activeSelf)
            {
                instance.SetActive(true);
            }

            var context = new PoolSpawnContext(_location, _rule.Group, parent, (uint)Time.frameCount);
            try
            {
                for (int i = 0; i < poolables.Length; i++)
                {
                    poolables[i].OnSpawn(in context);
                }
            }
            catch
            {
                if (_slots[slotIndex].state == SlotState.Active && ReferenceEquals(_slots[slotIndex].instance, instance))
                {
                    _activeCount--;
                    float now = Time.unscaledTime;
                    ParkInactive(slotIndex, now);
                    instance.SetActive(false);
                    AdvanceMaintenance(now);
                }

                throw;
            }

            return instance;
        }

        public async UniTask<GameObject> SpawnAsync(Transform parent, CancellationToken cancellationToken)
        {
            if (await LoadPrefabAsync(cancellationToken) == null)
            {
                return null;
            }

            return Spawn(parent);
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
                    ParkInactive(CreateTrackedInstance(), Time.unscaledTime);
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
                    AdvanceMaintenance(Time.unscaledTime);
                }
            }
        }

        internal void Despawn(int slotIndex)
        {
            ref Slot slot = ref _slots[slotIndex];
            if (slot.state != SlotState.Active)
            {
                throw new InvalidOperationException(ZString.Format("[GameObjectPool] Instance is not spawned. Location:{0}", _location));
            }

            GameObject instance = slot.instance;
            IGameObjectPoolable[] poolables = slot.poolables;
            bool ownsVisibility = slot.ownsVisibility;
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i].OnDespawn();
            }

            if (!ownsVisibility)
            {
                instance.SetActive(false);
            }

            _despawnCount++;
            _activeCount--;
            float now = Time.unscaledTime;
            ParkInactive(slotIndex, now);
            AdvanceMaintenance(now);
        }

        internal void RemoveDestroyedSlot(int slotIndex)
        {
            ref Slot slot = ref _slots[slotIndex];
            if (slot.state == SlotState.Active)
            {
                _activeCount--;
            }
            else
            {
                RemoveFromInactive(slotIndex);
            }

            IGameObjectPoolable[] poolables = slot.poolables;
            slot.handle.Detach();
            FreeSlot(slotIndex);
            _totalCount--;
            _destroyCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Log.Warning(ZString.Format(
                "[GameObjectPool] Pooled object destroyed outside pool. Rule:{0}, Location:{1}",
                _rule.EntryName,
                _location));
#endif
            InvokeOnPooledDestroy(poolables);
            RescheduleMaintenance(Time.unscaledTime);
        }

        public void ExecuteMaintenance(float now, bool lowMemory)
        {
            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in _rule, _inactiveCount, lowMemory);
            _retainTarget = plan.RetainTarget;
            for (int budget = plan.TrimBudget; budget > 0 && ShouldTrimHead(now, in plan); budget--)
            {
                DestroyTrackedInstance(_inactiveHead);
            }

            if (_prefab != null && _totalCount == 0 && plan.UnloadPrefab)
            {
                _loader.UnloadPrefab(_prefab);
                _prefab = null;
            }

            RescheduleMaintenance(now);
        }

        public void Flush()
        {
            ExecuteMaintenance(Time.unscaledTime, true);
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            _loadVersion++;
            _prefabLoadCompletionSource?.TrySetCanceled();
            _prefabLoadCompletionSource = null;
            _service.ScheduleMaintenance(this, float.MaxValue);

            for (int i = 0; i < _slotCount; i++)
            {
                Slot released = _slots[i];
                if (released.state == SlotState.Free)
                {
                    continue;
                }

                _slots[i] = default;
                released.handle.Detach();
                InvokeOnPooledDestroy(released.poolables);
                released.instance.SafeDestroySelf();
                _destroyCount++;
            }

            _slotCount = 0;
            _freeSlotCount = 0;
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
            float now = Time.unscaledTime;
            float nextMaintenanceAt = NextMaintenanceAt;
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
            snapshot.nextMaintenanceIn = nextMaintenanceAt >= float.MaxValue ? -1f : Mathf.Max(0f, nextMaintenanceAt - now);
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
            FillInstances(snapshot, Time.unscaledTime);
        }

        private void ParkInactive(int slotIndex, float now)
        {
            ref Slot slot = ref _slots[slotIndex];
            slot.state = SlotState.Inactive;
            slot.lastReleaseTime = now;
            Transform transform = slot.transform;
            AddToInactiveTail(slotIndex);
            transform.SetParent(_root, false);
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

            GameObject instance = UnityEngine.Object.Instantiate(_prefab, _root, false);
#if UNITY_EDITOR
            instance.name = ZString.Format("{0}[Pool]", _prefab.name);
#endif
            instance.SetActive(false);
            GameObjectPoolHandle handle = instance.GetComponent<GameObjectPoolHandle>();
            if (handle == null)
            {
                handle = instance.AddComponent<GameObjectPoolHandle>();
            }

            IGameObjectPoolable[] poolables = CollectPoolables(instance);
            float now = Time.unscaledTime;
            int slotIndex = AllocSlot();
            ref Slot slot = ref _slots[slotIndex];
            slot.instance = instance;
            slot.transform = instance.transform;
            slot.handle = handle;
            slot.poolables = poolables;
            slot.spawnTime = now;
            slot.lastReleaseTime = now;
            slot.prevInactive = -1;
            slot.nextInactive = -1;
            slot.state = SlotState.Inactive;
            slot.ownsVisibility = instance.TryGetComponent(out IGameObjectPoolable _);
            handle.Bind(this, slotIndex);
            _totalCount++;
            _expandCount++;
            return slotIndex;
        }

        private void DestroyTrackedInstance(int slotIndex)
        {
            RemoveFromInactive(slotIndex);
            ref Slot slot = ref _slots[slotIndex];
            GameObject instance = slot.instance;
            IGameObjectPoolable[] poolables = slot.poolables;
            slot.handle.Detach();
            FreeSlot(slotIndex);
            _totalCount--;
            _destroyCount++;
            InvokeOnPooledDestroy(poolables);
            instance.SafeDestroySelf();
        }

        private bool ShouldTrimHead(float now, in PoolRecyclePlan plan)
        {
            if (_inactiveCount <= plan.RetainTarget)
            {
                return false;
            }

            if (plan.ForceTrim || _rule.Policy == PoolPolicy.Fixed || _totalCount > _rule.SoftCapacity)
            {
                return true;
            }

            return now >= IdleDeadline(_inactiveHead);
        }

        private float IdleDeadline(int slotIndex)
        {
            return (float)(_slots[slotIndex].lastReleaseTime + _rule.IdleSeconds);
        }

        private float ComputeMaintenanceDueTime(float now)
        {
            if (_rule.Policy == PoolPolicy.Sticky)
            {
                return float.MaxValue;
            }

            if (_inactiveCount > _retainTarget)
            {
                return _rule.Policy == PoolPolicy.Fixed || _totalCount > _rule.SoftCapacity
                    ? now
                    : IdleDeadline(_inactiveHead);
            }

            if (_prefab != null && _totalCount == 0 && _rule.UnloadPrefab)
            {
                return _rule.Policy == PoolPolicy.Burst ? now + _rule.IdleSeconds : now;
            }

            return float.MaxValue;
        }

        private void AdvanceMaintenance(float now)
        {
            float dueTime = ComputeMaintenanceDueTime(now);
            if (dueTime < NextMaintenanceAt)
            {
                _service.ScheduleMaintenance(this, dueTime);
            }
        }

        private void RescheduleMaintenance(float now)
        {
            _service.ScheduleMaintenance(this, ComputeMaintenanceDueTime(now));
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

        private IGameObjectPoolable[] CollectPoolables(GameObject instance)
        {
            _poolableBuffer.Clear();
            instance.GetComponentsInChildren(true, _poolableBuffer);
            return _poolableBuffer.Count == 0 ? Array.Empty<IGameObjectPoolable>() : _poolableBuffer.ToArray();
        }

        private static void InvokeOnPooledDestroy(IGameObjectPoolable[] poolables)
        {
            for (int i = 0; i < poolables.Length; i++)
            {
                try
                {
                    poolables[i].OnPooledDestroy();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void AddToInactiveTail(int slotIndex)
        {
            ref Slot slot = ref _slots[slotIndex];
            slot.prevInactive = _inactiveTail;
            slot.nextInactive = -1;
            if (_inactiveTail >= 0)
            {
                _slots[_inactiveTail].nextInactive = slotIndex;
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
            ref Slot slot = ref _slots[slotIndex];
            int prev = slot.prevInactive;
            int next = slot.nextInactive;
            if (prev >= 0)
            {
                _slots[prev].nextInactive = next;
            }
            else
            {
                _inactiveHead = next;
            }

            if (next >= 0)
            {
                _slots[next].prevInactive = prev;
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
            for (int i = 0; i < _slotCount; i++)
            {
                ref Slot slot = ref _slots[i];
                if (slot.state == SlotState.Free)
                {
                    continue;
                }

                var instanceSnapshot = MemoryPool.Acquire<GameObjectPoolInstanceSnapshot>();
                instanceSnapshot.instanceName = slot.instance.name;
                instanceSnapshot.isActive = slot.state == SlotState.Active;
                instanceSnapshot.idleDuration = slot.state == SlotState.Active ? 0f : Mathf.Max(0f, now - slot.lastReleaseTime);
                instanceSnapshot.lifeDuration = Mathf.Max(0f, now - slot.spawnTime);
                instanceSnapshot.gameObject = slot.instance;
                snapshot.instances.Add(instanceSnapshot);
            }

            snapshot.instances.Sort(InstanceComparer);
        }

        private static int CompareInstanceSnapshot(GameObjectPoolInstanceSnapshot left, GameObjectPoolInstanceSnapshot right)
        {
            int state = right.isActive.CompareTo(left.isActive);
            return state != 0 ? state : string.CompareOrdinal(left.instanceName, right.instanceName);
        }

        private int AllocSlot()
        {
            if (_freeSlotCount != 0)
            {
                return _freeSlots[--_freeSlotCount];
            }

            if (_slotCount == _slots.Length)
            {
                Array.Resize(ref _slots, _slotCount << 1);
                Array.Resize(ref _freeSlots, _slotCount << 1);
            }

            return _slotCount++;
        }

        private void FreeSlot(int slotIndex)
        {
            _slots[slotIndex] = default;
            _freeSlots[_freeSlotCount++] = slotIndex;
        }
    }
}
