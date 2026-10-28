using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace AlicizaX.ObjectPool
{
    internal sealed partial class ObjectPoolService
    {
        private sealed class ObjectPool<T> : ObjectPoolBase, IObjectPool<T> where T : ObjectBase
        {
            private struct ObjectSlot
            {
                public T Obj;
                public int SpawnCount;
                public float LastUseTime;
                public int PrevAvailable;
                public int NextAvailable;
                public int PrevUnused;
                public int NextUnused;
                public int PrevAll;
                public int NextAll;
                public byte Flags;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public bool IsAlive() => (Flags & 1) != 0;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void SetAlive(bool alive)
                {
                    Flags = alive ? (byte)1 : (byte)0;
                }
            }

            private readonly ObjectPoolService m_Owner;
            private readonly bool m_AllowMultiSpawn;

            private ObjectSlot[] m_Slots;
            private int[] m_FreeStack;
            private int m_FreeTop;
            private int m_SlotCount;

            private OpenHashMap<RefKey> m_TargetMap;
            private OpenHashMap<string> m_AvailableNameHeadMap;
            private OpenHashMap<string> m_AllNameHeadMap;
            private bool m_HasNameMap;

            private int m_UnnamedAvailableHead;
            private int m_UnnamedAllHead;
            private int m_UnusedHead;
            private int m_UnusedTail;
            private int m_UnusedCount;

            private float m_AutoReleaseInterval;
            private int m_Capacity;
            private float m_ExpireTime;
            private int m_Priority;
            private float m_AutoReleaseTime;
            private int m_PendingReleaseCount;
            private bool m_IsShuttingDown;
            private int m_CallbackCount;

            private const int DefaultReleasePerFrame = 8;
            private const int InitSlotCapacity = 16;

            public ObjectPool(ObjectPoolService owner, string name, bool allowMultiSpawn,
                float autoReleaseInterval, int capacity, float expireTime, int priority)
                : base(name)
            {
                int initCap = Math.Min(Math.Max(capacity, 1), InitSlotCapacity);
                m_Owner = owner;
                m_Slots = new ObjectSlot[initCap];
                m_FreeStack = new int[initCap];
                for (int i = 0; i < initCap; i++)
                    m_FreeStack[i] = i;
                m_FreeTop = initCap;
                m_SlotCount = initCap;
                m_TargetMap = new OpenHashMap<RefKey>(initCap);
                m_AllowMultiSpawn = allowMultiSpawn;
                m_AutoReleaseInterval = autoReleaseInterval;
                m_Capacity = capacity;
                m_ExpireTime = expireTime;
                m_Priority = priority;
                m_AutoReleaseTime = 0f;
                m_PendingReleaseCount = 0;
                m_UnnamedAvailableHead = -1;
                m_UnnamedAllHead = -1;
                m_UnusedHead = -1;
                m_UnusedTail = -1;
                m_UnusedCount = 0;
                m_IsShuttingDown = false;
            }

            public override Type ObjectType => typeof(T);
            public override int Count => m_TargetMap.Count;
            public override bool AllowMultiSpawn => m_AllowMultiSpawn;
            internal override bool IsInCallback => m_CallbackCount > 0;

            public override float AutoReleaseInterval
            {
                get => m_AutoReleaseInterval;
                set
                {
                    ThrowIfShuttingDown();
                    EnsureNotInCallback();
                    if (value < 0f)
                    {
                        Log.Error("AutoReleaseInterval is invalid.");
                        return;
                    }

                    m_AutoReleaseInterval = value;
                    UpdateActiveState();
                }
            }

            public override int Capacity
            {
                get => m_Capacity;
                set
                {
                    ThrowIfShuttingDown();
                    EnsureNotInCallback();
                    if (value < 0)
                    {
                        Log.Error("Capacity is invalid.");
                        return;
                    }

                    m_Capacity = value;
                    m_PendingReleaseCount = Count > m_Capacity ? Count - m_Capacity : 0;
                    UpdateActiveState();
                }
            }

            public override float ExpireTime
            {
                get => m_ExpireTime;
                set
                {
                    ThrowIfShuttingDown();
                    EnsureNotInCallback();
                    if (value < 0f)
                    {
                        Log.Error("ExpireTime is invalid.");
                        return;
                    }

                    m_ExpireTime = value;
                    if (TrackLastUseTime)
                        StampUntimedUnusedSlots();
                    UpdateActiveState();
                }
            }

            public override int Priority
            {
                get => m_Priority;
                set => m_Priority = value;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static RefKey Key(object target) => new RefKey(target);

            public bool Register(T obj, bool spawned)
            {
                ThrowIfShuttingDown();
                EnsureNotInCallback();
                if (obj == null)
                {
                    Log.Error($"Object or target is invalid in pool '{FullName}'.");
                    return false;
                }

                if (obj.Pool != null)
                {
                    Log.Error($"Object is already registered in pool '{obj.Pool.FullName}'.");
                    return false;
                }

                if (obj.Target == null)
                {
                    Log.Error($"Object or target is invalid in pool '{FullName}'.");
                    RecycleObject(obj);
                    return false;
                }

                if (m_TargetMap.TryGetValue(Key(obj.Target), out int existingIdx) && m_Slots[existingIdx].IsAlive())
                {
                    Log.Error($"Target '{obj.Target.GetType().FullName}' is already registered in pool '{FullName}'.");
                    RecycleObject(obj);
                    return false;
                }

                obj.Pool = this;
                bool hasCapacity;
                try
                {
                    hasCapacity = ReserveRegisterSlot();
                }
                catch
                {
                    obj.Pool = null;
                    RecycleObject(obj);
                    throw;
                }

                if (m_IsShuttingDown)
                {
                    // 预留容量期间回调销毁了该池。Shutdown 会拒绝变更，但本次请求仍需干净地拒绝。
                    obj.Pool = null;
                    RecycleObject(obj);
                    return false;
                }

                if (!hasCapacity || (m_Capacity != int.MaxValue && Count >= m_Capacity))
                {
                    Log.Error($"Object pool '{FullName}' capacity is full.");
                    obj.Pool = null;
                    RecycleObject(obj);
                    return false;
                }

                int idx = AllocSlot();
                ref var slot = ref m_Slots[idx];
                slot.Obj = obj;
                slot.SpawnCount = 0;
                slot.LastUseTime = 0f;
                slot.PrevAvailable = -1;
                slot.NextAvailable = -1;
                slot.PrevUnused = -1;
                slot.NextUnused = -1;
                slot.PrevAll = -1;
                slot.NextAll = -1;
                slot.SetAlive(true);

                m_TargetMap.AddOrUpdate(Key(obj.Target), idx);
                if (m_AllowMultiSpawn)
                    AddToNameChain(idx, false);

                if (TrackLastUseTime)
                {
                    float now = Time.realtimeSinceStartup;
                    slot.LastUseTime = now;
                    obj.LastUseTime = now;
                }

                MarkSlotAvailable(idx);
                if (spawned)
                    SpawnSlot(idx);

                UpdateActiveState();
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Spawn() => Spawn(string.Empty);

            public T Spawn(string name)
            {
                ThrowIfShuttingDown();
                EnsureNotInCallback();
                if (name == null)
                    name = string.Empty;

                if (m_AllowMultiSpawn)
                    return SpawnAny(name);

                int head = FindAvailableByName(name);
                if (head < 0)
                    return null;

                T obj = m_Slots[head].Obj;
                SpawnSlot(head);
                return m_IsShuttingDown ? null : obj;
            }

            public void Unspawn(T obj)
            {
                if (m_IsShuttingDown)
                    return;
                EnsureNotInCallback();
                if (obj == null || obj.Target == null)
                    return;
                if (obj.Pool != this
                    || !m_TargetMap.TryGetValue(Key(obj.Target), out int idx)
                    || !m_Slots[idx].IsAlive()
                    || !ReferenceEquals(m_Slots[idx].Obj, obj))
                {
                    Log.Error($"Cannot find target in pool '{Name}', type='{obj.Target.GetType().FullName}', object is not registered.");
                    return;
                }

                UnspawnSlot(idx);
            }

            public void UnspawnTarget(object target)
            {
                if (m_IsShuttingDown)
                    return;
                EnsureNotInCallback();
                if (target == null)
                    return;
                if (!m_TargetMap.TryGetValue(Key(target), out int idx))
                {
                    Log.Error($"Cannot find target in pool '{Name}', type='{target.GetType().FullName}'");
                    return;
                }

                UnspawnSlot(idx);
            }

            public override void Release()
            {
                ReleaseAllUnused();
            }

            public override void Release(int toReleaseCount)
            {
                EnsureNotInCallback();
                if (toReleaseCount <= 0)
                    return;

                int released = ReleaseUnused(toReleaseCount, false, float.MinValue);
                m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - released);
                UpdateActiveState();
            }

            public override void ReleaseAllUnused()
            {
                EnsureNotInCallback();
                int released = ReleaseUnused(int.MaxValue, false, float.MinValue);
                m_PendingReleaseCount = 0;
                UpdateActiveState();
            }

            internal override void Update(float elapseSeconds, float realElapseSeconds)
            {
                if (m_IsShuttingDown)
                    return;
                bool overCapacity = Count > m_Capacity;
                if (m_AutoReleaseInterval < float.MaxValue && overCapacity)
                {
                    m_AutoReleaseTime += realElapseSeconds;
                    if (m_AutoReleaseTime >= m_AutoReleaseInterval)
                    {
                        m_AutoReleaseTime = 0f;
                        MarkRelease(Count - m_Capacity);
                    }
                }

                bool checkExpire = m_ExpireTime < float.MaxValue && m_UnusedCount > 0;
                if (m_PendingReleaseCount <= 0 && !checkExpire)
                {
                    UpdateActiveState();
                    return;
                }

                if (m_PendingReleaseCount > 0)
                {
                    int releaseBudget = Math.Min(DefaultReleasePerFrame, m_PendingReleaseCount);
                    int releasedByBudget = ReleaseUnused(releaseBudget, false, float.MinValue);
                    m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - releasedByBudget);
                }
                else
                {
                    ReleaseUnused(DefaultReleasePerFrame, true, Time.realtimeSinceStartup - m_ExpireTime);
                }

                UpdateActiveState();
            }

            internal override void Shutdown()
            {
                if (m_Slots == null || m_IsShuttingDown)
                    return;
                m_IsShuttingDown = true;
                EnsureNotInCallback();
                m_Owner.SetPoolActive(this, false);
                List<Exception> errors = null;
                for (int i = 0; i < m_SlotCount; i++)
                {
                    ref var slot = ref m_Slots[i];
                    if (!slot.IsAlive())
                        continue;

                    T obj = slot.Obj;
                    m_TargetMap.Remove(Key(obj.Target));
                    m_Slots[i] = default;
                    try { obj.Release(true); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                    try { RecycleObject(obj); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
                m_TargetMap.Dispose();
                if (m_HasNameMap)
                {
                    m_AvailableNameHeadMap.Dispose();
                    if (m_AllowMultiSpawn)
                        m_AllNameHeadMap.Dispose();
                    m_HasNameMap = false;
                }

                m_Slots = null;
                m_FreeStack = null;
                m_FreeTop = 0;
                m_SlotCount = 0;
                m_PendingReleaseCount = 0;
                m_UnnamedAvailableHead = -1;
                m_UnnamedAllHead = -1;
                m_UnusedHead = -1;
                m_UnusedTail = -1;
                m_UnusedCount = 0;
                if (errors != null)
                    throw new AggregateException(errors);
            }

            internal override int GetAllObjectInfos(ObjectInfo[] results)
            {
                if (results == null)
                {
                    Log.Error("Results is invalid.");
                    return 0;
                }

                int write = 0;
                int capacity = results.Length;
                for (int i = 0; i < m_SlotCount; i++)
                {
                    ref var slot = ref m_Slots[i];
                    if (!slot.IsAlive())
                        continue;

                    if (write < capacity)
                    {
                        results[write] = new ObjectInfo(slot.Obj.Name, slot.Obj.Locked,
                            slot.Obj.CustomCanReleaseFlag,
                            slot.Obj.LastUseTime, slot.SpawnCount);
                    }

                    write++;
                }

                return write;
            }

            private bool TrackLastUseTime => m_ExpireTime < float.MaxValue;

            private void StampUntimedUnusedSlots()
            {
                float now = Time.realtimeSinceStartup;
                int current = m_UnusedHead;
                while (current >= 0)
                {
                    ref var slot = ref m_Slots[current];
                    if (slot.LastUseTime == 0f)
                    {
                        slot.LastUseTime = now;
                        slot.Obj.LastUseTime = now;
                    }
                    current = slot.NextUnused;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void SpawnSlot(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if (slot.SpawnCount == 0)
                    MarkSlotUnavailable(idx);

                slot.SpawnCount++;
                if (TrackLastUseTime)
                {
                    float now = Time.realtimeSinceStartup;
                    slot.LastUseTime = now;
                    slot.Obj.LastUseTime = now;
                }

                T obj = slot.Obj;
                m_CallbackCount++;
                try
                {
                    obj.OnSpawn();
                }
                catch
                {
                    ref var returned = ref m_Slots[idx];
                    returned.SpawnCount--;
                    if (returned.SpawnCount == 0)
                        MarkSlotAvailable(idx);
                    throw;
                }
                finally
                {
                    m_CallbackCount--;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void UnspawnSlot(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if (slot.SpawnCount == 0)
                {
                    Log.Error($"Object '{slot.Obj.Name}' is not spawned.");
                    return;
                }
                T obj = slot.Obj;
                m_CallbackCount++;
                try
                {
                    obj.OnUnspawn();
                }
                finally
                {
                    m_CallbackCount--;
                    ref var returned = ref m_Slots[idx];
                    returned.SpawnCount--;
                    if (TrackLastUseTime)
                    {
                        float now = Time.realtimeSinceStartup;
                        returned.LastUseTime = now;
                        obj.LastUseTime = now;
                    }
                    if (returned.SpawnCount == 0)
                        MarkSlotAvailable(idx);
                    if (Count > m_Capacity && returned.SpawnCount == 0)
                        MarkRelease(Count - m_Capacity);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void MarkRelease(int count)
            {
                if (count > 0)
                    m_PendingReleaseCount = Math.Max(m_PendingReleaseCount, count);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int AllocSlot()
            {
                if (m_FreeTop == 0)
                    GrowSlots();

                return m_FreeStack[--m_FreeTop];
            }

            private void GrowSlots()
            {
                int oldCap = m_SlotCount;
                int newCap = oldCap * 2;
                Array.Resize(ref m_Slots, newCap);
                Array.Resize(ref m_FreeStack, newCap);
                for (int i = oldCap; i < newCap; i++)
                    m_FreeStack[m_FreeTop++] = i;
                m_SlotCount = newCap;
            }

            private void ReleaseSlot(int idx)
            {
                ref var slot = ref m_Slots[idx];
                T obj = slot.Obj;
                object target = obj.Target;
                MarkSlotUnavailable(idx);
                if (m_AllowMultiSpawn)
                    RemoveFromNameChain(idx, false);
                m_TargetMap.Remove(Key(target));

                // 调用用户代码前先摘除槽位。回调不能变更当前池，因此即使 Release() 抛异常，
                // 池仍保持一致；包装对象被回收，调用方看到原始异常。
                m_Slots[idx] = default;
                m_FreeStack[m_FreeTop++] = idx;

                Exception error = null;
                m_CallbackCount++;
                try
                {
                    obj.Release(false);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    m_CallbackCount--;
                }

                try
                {
                    RecycleObject(obj);
                }
                catch (Exception exception)
                {
                    error = error == null ? exception : new AggregateException(error, exception);
                }

                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
            }

            private bool ReserveRegisterSlot()
            {
                if (m_Capacity == int.MaxValue || Count < m_Capacity)
                    return true;

                int released = ReleaseUnused(1, false, float.MinValue);
                if (released > 0)
                    m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - released);

                return Count < m_Capacity;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void RecycleObject(T obj)
            {
                if (obj.OwnerHandle.IsValid)
                    obj.OwnerHandle.Release(obj);
                else
                    obj.Clear();
            }

            private int FindAvailableByName(string name)
            {
                if (name.Length == 0)
                    return m_UnnamedAvailableHead;

                if (!m_HasNameMap)
                    return -1;
                if (!m_AvailableNameHeadMap.TryGetValue(name, out int head))
                    return -1;
                return head;
            }

            private T SpawnAny(string name)
            {
                int head = FindAllByName(name);
                if (head < 0)
                    return null;

                T obj = m_Slots[head].Obj;
                SpawnSlot(head);
                return m_IsShuttingDown ? null : obj;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int FindAllByName(string name)
            {
                if (name.Length == 0)
                    return m_UnnamedAllHead;
                if (!m_HasNameMap)
                    return -1;
                if (!m_AllNameHeadMap.TryGetValue(name, out int head))
                    return -1;
                return head;
            }

            private int ReleaseUnused(int maxReleaseCount, bool requireExpired, float expireThreshold)
            {
                if (m_IsShuttingDown)
                    return 0;

                int released = 0;
                int visited = 0;
                int limit = m_UnusedCount;
                int current = m_UnusedHead;
                Exception callbackError = null;
                while (current >= 0 && released < maxReleaseCount && visited < limit)
                {
                    visited++;
                    ref var slot = ref m_Slots[current];
                    int next = slot.NextUnused;

                    if (requireExpired && (slot.LastUseTime == 0f || slot.LastUseTime > expireThreshold))
                    {
                        current = next;
                        continue;
                    }

                    if (!CanReleaseSlot(ref slot))
                    {
                        current = next;
                        continue;
                    }

                    try
                    {
                        ReleaseSlot(current);
                        released++;
                    }
                    catch (Exception error)
                    {
                        callbackError = callbackError == null ? error : new AggregateException(callbackError, error);
                    }

                    // Release() 回调不能变更当前池，因此已保存的 next 槽位在释放槽位
                    // 自行摘除后，仍是正确的续遍历位置。
                    current = next;
                }

                if (callbackError != null)
                    ExceptionDispatchInfo.Capture(callbackError).Throw();
                return released;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool CanReleaseSlot(ref ObjectSlot slot)
            {
                return slot.IsAlive()
                       && slot.SpawnCount == 0
                       && !slot.Obj.Locked
                       && slot.Obj.CustomCanReleaseFlag;
            }

            private void MarkSlotAvailable(int idx)
            {
                AddToUnusedListTail(idx);
                AddToNameChain(idx, true);
            }

            private void MarkSlotUnavailable(int idx)
            {
                RemoveFromNameChain(idx, true);
                RemoveFromUnusedList(idx);
            }

            /// <summary>
            /// 将槽位加入可用链（available: true）或全量名称链（available: false）。
            /// 两条链的区别仅在于使用的链接字段和头节点。
            /// </summary>
            private void AddToNameChain(int idx, bool available)
            {
                ref var slot = ref m_Slots[idx];
                if (available)
                {
                    if (!slot.IsAlive() || slot.SpawnCount != 0)
                        return;
                    if (slot.PrevAvailable >= 0 || slot.NextAvailable >= 0)
                        return;
                }

                ref int prev = ref (available ? ref slot.PrevAvailable : ref slot.PrevAll);
                ref int next = ref (available ? ref slot.NextAvailable : ref slot.NextAll);
                string objectName = slot.Obj.Name ?? string.Empty;
                if (objectName.Length == 0)
                {
                    ref int head = ref (available ? ref m_UnnamedAvailableHead : ref m_UnnamedAllHead);
                    if (head == idx)
                        return;
                    next = head;
                    prev = -1;
                    if (head >= 0)
                        SetPrevLink(head, idx, available);
                    head = idx;
                    return;
                }

                EnsureNameMap();
                ref OpenHashMap<string> map = ref (available ? ref m_AvailableNameHeadMap : ref m_AllNameHeadMap);
                if (map.TryGetValue(objectName, out int namedHead))
                {
                    if (namedHead == idx)
                        return;
                    SetPrevLink(namedHead, idx, available);
                    next = namedHead;
                }
                else
                {
                    next = -1;
                }

                prev = -1;
                map.AddOrUpdate(objectName, idx);
            }

            private void RemoveFromNameChain(int idx, bool available)
            {
                ref var slot = ref m_Slots[idx];
                if (available && !slot.IsAlive())
                    return;

                string objectName = slot.Obj.Name ?? string.Empty;
                bool isUnnamed = objectName.Length == 0;
                ref int prev = ref (available ? ref slot.PrevAvailable : ref slot.PrevAll);
                ref int next = ref (available ? ref slot.NextAvailable : ref slot.NextAll);
                int previousIndex = prev;
                int nextIndex = next;

                bool isHead;
                if (isUnnamed)
                {
                    ref int head = ref (available ? ref m_UnnamedAvailableHead : ref m_UnnamedAllHead);
                    isHead = head == idx;
                }
                else
                {
                    ref OpenHashMap<string> map = ref (available ? ref m_AvailableNameHeadMap : ref m_AllNameHeadMap);
                    isHead = m_HasNameMap && map.TryGetValue(objectName, out int namedHead) && namedHead == idx;
                }

                if (!isHead && previousIndex < 0 && nextIndex < 0)
                    return;

                if (previousIndex >= 0)
                {
                    SetNextLink(previousIndex, nextIndex, available);
                }
                else if (isHead)
                {
                    if (isUnnamed)
                    {
                        ref int head = ref (available ? ref m_UnnamedAvailableHead : ref m_UnnamedAllHead);
                        head = nextIndex;
                    }
                    else
                    {
                        ref OpenHashMap<string> map = ref (available ? ref m_AvailableNameHeadMap : ref m_AllNameHeadMap);
                        if (nextIndex >= 0)
                            map.AddOrUpdate(objectName, nextIndex);
                        else
                            map.Remove(objectName);
                    }
                }

                if (nextIndex >= 0)
                    SetPrevLink(nextIndex, previousIndex, available);

                prev = -1;
                next = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void SetPrevLink(int idx, int value, bool available)
            {
                ref var slot = ref m_Slots[idx];
                if (available) slot.PrevAvailable = value;
                else slot.PrevAll = value;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void SetNextLink(int idx, int value, bool available)
            {
                ref var slot = ref m_Slots[idx];
                if (available) slot.NextAvailable = value;
                else slot.NextAll = value;
            }

            private void AddToUnusedListTail(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if (m_UnusedHead == idx || slot.PrevUnused >= 0 || slot.NextUnused >= 0)
                    return;

                slot.PrevUnused = m_UnusedTail;
                slot.NextUnused = -1;
                if (m_UnusedTail >= 0)
                    m_Slots[m_UnusedTail].NextUnused = idx;
                else
                    m_UnusedHead = idx;
                m_UnusedTail = idx;
                m_UnusedCount++;
            }

            private void RemoveFromUnusedList(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if (m_UnusedHead != idx && slot.PrevUnused < 0 && slot.NextUnused < 0)
                    return;

                int prev = slot.PrevUnused;
                int next = slot.NextUnused;
                if (prev >= 0)
                    m_Slots[prev].NextUnused = next;
                else
                    m_UnusedHead = next;

                if (next >= 0)
                    m_Slots[next].PrevUnused = prev;
                else
                    m_UnusedTail = prev;

                slot.PrevUnused = -1;
                slot.NextUnused = -1;
                m_UnusedCount--;
            }

            private void EnsureNameMap()
            {
                if (m_HasNameMap)
                    return;

                int cap = Math.Max(8, m_SlotCount);
                m_AvailableNameHeadMap = new OpenHashMap<string>(cap);
                if (m_AllowMultiSpawn)
                    m_AllNameHeadMap = new OpenHashMap<string>(cap);
                m_HasNameMap = true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void UpdateActiveState()
            {
                bool active = !m_IsShuttingDown && (m_PendingReleaseCount > 0
                              || (m_ExpireTime < float.MaxValue && m_UnusedCount > 0)
                              || (m_AutoReleaseInterval < float.MaxValue && Count > m_Capacity));
                m_Owner.SetPoolActive(this, active);
            }

            private void ThrowIfShuttingDown()
            {
                if (m_IsShuttingDown)
                    throw new ObjectDisposedException(FullName);
            }

            /// <summary>
            /// 池回调（OnSpawn/OnUnspawn/Release）可以使用其他池和服务，但不得变更
            /// 当前正在调用它的池。拒绝重入变更，使槽位/名称/空闲链表无需回滚记账也能保持一致。
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void EnsureNotInCallback()
            {
                if (m_CallbackCount > 0)
                    throw new InvalidOperationException($"Object pool '{FullName}' cannot be mutated from a pool callback.");
            }

        }
    }
}

