using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            private ReferenceOpenHashMap m_TargetMap;
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
            private bool m_IsReleasing;
            private int m_ReleaseCursor = -1;
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
                m_TargetMap = new ReferenceOpenHashMap(initCap);
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

            public override float AutoReleaseInterval
            {
                get => m_AutoReleaseInterval;
                set
                {
                    ThrowIfShuttingDown();
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

            public bool Register(T obj, bool spawned)
            {
                ThrowIfShuttingDown();
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

                if (m_TargetMap.TryGetValue(obj.Target, out int existingIdx) && m_Slots[existingIdx].IsAlive())
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

                if (!hasCapacity || m_IsShuttingDown || m_Slots == null || (m_Capacity != int.MaxValue && Count >= m_Capacity))
                {
                    if (!m_IsShuttingDown && m_Slots != null)
                        Log.Error($"Object pool '{FullName}' capacity is full.");
                    obj.Pool = null;
                    RecycleObject(obj);
                    return false;
                }

                if (m_TargetMap.TryGetValue(obj.Target, out existingIdx) && m_Slots[existingIdx].IsAlive())
                {
                    Log.Error($"Target '{obj.Target.GetType().FullName}' is already registered in pool '{FullName}'.");
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

                m_TargetMap.AddOrUpdate(obj.Target, idx);
                if (m_AllowMultiSpawn)
                    AddToAllNameChain(idx);

                if (TrackLastUseTime)
                {
                    float now = Time.realtimeSinceStartup;
                    slot.LastUseTime = now;
                    obj.LastUseTime = now;
                }

                MarkSlotAvailable(idx);
                if (spawned)
                    SpawnSlot(idx);

                if (m_IsShuttingDown || m_Slots == null)
                    return false;

                UpdateActiveState();
                ValidateState();
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Spawn() => Spawn(string.Empty);

            public T Spawn(string name)
            {
                ThrowIfShuttingDown();
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
                if (obj == null || obj.Target == null)
                    return;
                if (obj.Pool != this
                    || !m_TargetMap.TryGetValue(obj.Target, out int idx)
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
                if (target == null)
                    return;
                if (!m_TargetMap.TryGetValue(target, out int idx))
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
                if (toReleaseCount <= 0)
                    return;

                int released = ReleaseUnused(toReleaseCount, false, float.MinValue);
                m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - released);
                UpdateActiveState();
                if (released > 0)
                    ValidateState();
            }

            public override void ReleaseAllUnused()
            {
                int released = ReleaseUnused(int.MaxValue, false, float.MinValue);
                m_PendingReleaseCount = 0;
                UpdateActiveState();
                if (released > 0)
                    ValidateState();
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
                if (m_Slots == null)
                    return;
                m_IsShuttingDown = true;
                m_Owner.SetPoolActive(this, false);
                if (m_CallbackCount > 0 || m_IsReleasing)
                    return;
                List<Exception> errors = null;
                m_CallbackCount++;
                for (int i = 0; i < m_SlotCount; i++)
                {
                    ref var slot = ref m_Slots[i];
                    if (!slot.IsAlive())
                        continue;

                    T obj = slot.Obj;
                    m_TargetMap.Remove(obj.Target);
                    m_Slots[i] = default;
                    try { obj.Release(true); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                    try { RecycleObject(obj); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
                m_CallbackCount--;
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
                if ((slot.Flags & 2) != 0)
                    throw new InvalidOperationException("Object is executing a pool callback.");
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
                slot.Flags |= 2;
                m_CallbackCount++;
                Exception callbackError = null;
                try
                {
                    obj.OnSpawn();
                }
                catch (Exception error)
                {
                    m_Slots[idx].SpawnCount--;
                    if (m_Slots[idx].SpawnCount == 0 && !m_IsShuttingDown)
                        MarkSlotAvailable(idx);
                    callbackError = error;
                }
                finally
                {
                    m_Slots[idx].Flags &= unchecked((byte)~2);
                    m_CallbackCount--;
                }
                CompleteCallback(callbackError);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void UnspawnSlot(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if ((slot.Flags & 2) != 0)
                    throw new InvalidOperationException("Object is executing a pool callback.");
                if (slot.SpawnCount == 0)
                {
                    Log.Error($"Object '{slot.Obj.Name}' is not spawned.");
                    return;
                }
                T obj = slot.Obj;
                slot.Flags |= 2;
                m_CallbackCount++;
                Exception callbackError = null;
                try
                {
                    obj.OnUnspawn();
                }
                catch (Exception error)
                {
                    callbackError = error;
                }
                finally
                {
                    ref var returned = ref m_Slots[idx];
                    returned.Flags &= unchecked((byte)~2);
                    returned.SpawnCount--;
                    if (TrackLastUseTime)
                    {
                        float now = Time.realtimeSinceStartup;
                        returned.LastUseTime = now;
                        obj.LastUseTime = now;
                    }
                    if (returned.SpawnCount == 0 && !m_IsShuttingDown)
                        MarkSlotAvailable(idx);
                    if (Count > m_Capacity && returned.SpawnCount == 0)
                        MarkRelease(Count - m_Capacity);
                    m_CallbackCount--;
                }
                CompleteCallback(callbackError);
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
                if (m_AllowMultiSpawn && (slot.Flags & 4) == 0)
                    RemoveFromAllNameChain(idx);
                slot.Flags |= 6;
                m_TargetMap.Remove(target);
                m_CallbackCount++;
                try
                {
                    obj.Release(false);
                }
                catch
                {
                    if (m_Slots != null && !m_IsShuttingDown)
                    {
                        if (!m_TargetMap.TryGetValue(target, out int mapped) || mapped == idx)
                            m_TargetMap.AddOrUpdate(target, idx);
                        AddToUnusedListTail(idx);
                    }
                    throw;
                }
                finally
                {
                    if (m_Slots != null)
                        m_Slots[idx].Flags &= unchecked((byte)~2);
                    m_CallbackCount--;
                }

                if (m_Slots == null)
                    return;

                m_Slots[idx] = default;
                m_FreeStack[m_FreeTop++] = idx;
                RecycleObject(obj);
            }

            private bool ReserveRegisterSlot()
            {
                if (m_Capacity == int.MaxValue || Count < m_Capacity)
                    return true;
                if (m_IsReleasing)
                    return false;

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
                if (m_IsReleasing)
                    throw new InvalidOperationException("Object pool is already releasing objects.");
                int released = 0;
                int visited = 0;
                int limit = m_UnusedCount;
                m_ReleaseCursor = m_UnusedHead;
                m_IsReleasing = true;
                Exception callbackError = null;
                try
                {
                    while (m_ReleaseCursor >= 0 && released < maxReleaseCount && visited < limit && !m_IsShuttingDown)
                    {
                        int current = m_ReleaseCursor;
                        visited++;
                        ref var slot = ref m_Slots[current];
                        m_ReleaseCursor = slot.NextUnused;

                        if (requireExpired && (slot.LastUseTime == 0f || slot.LastUseTime > expireThreshold))
                            continue;

                        if (CanReleaseSlot(ref slot))
                        {
                            ReleaseSlot(current);
                            released++;
                        }
                    }
                }
                catch (Exception error)
                {
                    callbackError = error;
                }
                finally
                {
                    m_IsReleasing = false;
                    m_ReleaseCursor = -1;
                }
                CompleteCallback(callbackError);

                return released;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool CanReleaseSlot(ref ObjectSlot slot)
            {
                return slot.IsAlive()
                       && slot.SpawnCount == 0
                       && (slot.Flags & 2) == 0
                       && !slot.Obj.Locked
                       && slot.Obj.CustomCanReleaseFlag;
            }

            private void MarkSlotAvailable(int idx)
            {
                AddToUnusedListTail(idx);
                AddToAvailableNameChain(idx);
            }

            private void MarkSlotUnavailable(int idx)
            {
                RemoveFromAvailableNameChain(idx);
                RemoveFromUnusedList(idx);
            }

            private void AddToAvailableNameChain(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if (!slot.IsAlive() || slot.SpawnCount != 0)
                    return;
                if (slot.PrevAvailable >= 0 || slot.NextAvailable >= 0)
                    return;

                string objectName = slot.Obj.Name ?? string.Empty;
                if (objectName.Length == 0)
                {
                    if (m_UnnamedAvailableHead == idx)
                        return;
                    slot.NextAvailable = m_UnnamedAvailableHead;
                    slot.PrevAvailable = -1;
                    if (m_UnnamedAvailableHead >= 0)
                        m_Slots[m_UnnamedAvailableHead].PrevAvailable = idx;
                    m_UnnamedAvailableHead = idx;
                    return;
                }

                EnsureNameMap();
                if (m_AvailableNameHeadMap.TryGetValue(objectName, out int head))
                {
                    if (head == idx)
                        return;
                    m_Slots[head].PrevAvailable = idx;
                    slot.NextAvailable = head;
                }
                else
                {
                    slot.NextAvailable = -1;
                }

                slot.PrevAvailable = -1;
                m_AvailableNameHeadMap.AddOrUpdate(objectName, idx);
            }

            private void RemoveFromAvailableNameChain(int idx)
            {
                ref var slot = ref m_Slots[idx];
                if (!slot.IsAlive())
                    return;

                string objectName = slot.Obj.Name ?? string.Empty;
                int prev = slot.PrevAvailable;
                int next = slot.NextAvailable;
                bool isUnnamed = objectName.Length == 0;
                bool isHead = isUnnamed ? m_UnnamedAvailableHead == idx : false;
                if (!isUnnamed && m_HasNameMap && m_AvailableNameHeadMap.TryGetValue(objectName, out int namedHead))
                    isHead = namedHead == idx;

                if (!isHead && prev < 0 && next < 0)
                    return;

                if (prev >= 0)
                    m_Slots[prev].NextAvailable = next;
                else if (isHead)
                {
                    if (isUnnamed)
                        m_UnnamedAvailableHead = next;
                    else if (next >= 0)
                        m_AvailableNameHeadMap.AddOrUpdate(objectName, next);
                    else
                        m_AvailableNameHeadMap.Remove(objectName);
                }

                if (next >= 0)
                    m_Slots[next].PrevAvailable = prev;

                slot.PrevAvailable = -1;
                slot.NextAvailable = -1;
            }

            private void AddToAllNameChain(int idx)
            {
                ref var slot = ref m_Slots[idx];
                string objectName = slot.Obj.Name ?? string.Empty;
                if (objectName.Length == 0)
                {
                    slot.NextAll = m_UnnamedAllHead;
                    slot.PrevAll = -1;
                    if (m_UnnamedAllHead >= 0)
                        m_Slots[m_UnnamedAllHead].PrevAll = idx;
                    m_UnnamedAllHead = idx;
                    return;
                }

                EnsureNameMap();
                if (m_AllNameHeadMap.TryGetValue(objectName, out int head))
                {
                    m_Slots[head].PrevAll = idx;
                    slot.NextAll = head;
                }
                else
                {
                    slot.NextAll = -1;
                }

                slot.PrevAll = -1;
                m_AllNameHeadMap.AddOrUpdate(objectName, idx);
            }

            private void RemoveFromAllNameChain(int idx)
            {
                ref var slot = ref m_Slots[idx];
                string objectName = slot.Obj.Name ?? string.Empty;
                int prev = slot.PrevAll;
                int next = slot.NextAll;
                bool isUnnamed = objectName.Length == 0;
                bool isHead = isUnnamed ? m_UnnamedAllHead == idx : false;
                if (!isUnnamed && m_HasNameMap && m_AllNameHeadMap.TryGetValue(objectName, out int namedHead))
                    isHead = namedHead == idx;

                if (!isHead && prev < 0 && next < 0)
                    return;

                if (prev >= 0)
                    m_Slots[prev].NextAll = next;
                else if (isHead)
                {
                    if (isUnnamed)
                        m_UnnamedAllHead = next;
                    else if (next >= 0)
                        m_AllNameHeadMap.AddOrUpdate(objectName, next);
                    else
                        m_AllNameHeadMap.Remove(objectName);
                }

                if (next >= 0)
                    m_Slots[next].PrevAll = prev;

                slot.PrevAll = -1;
                slot.NextAll = -1;
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
                if (m_ReleaseCursor == idx)
                    m_ReleaseCursor = next;
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

            private void CompleteCallback(Exception error)
            {
                UpdateActiveState();
                if (m_IsShuttingDown && m_CallbackCount == 0 && !m_IsReleasing)
                {
                    try { Shutdown(); }
                    catch (Exception shutdownError)
                    {
                        if (error != null)
                            throw new AggregateException(error, shutdownError);
                        throw;
                    }
                }
                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
            }

            [Conditional("UNITY_EDITOR")]
            private void ValidateState()
            {
#if UNITY_EDITOR && ENABLE_OBJECTPOOL_VALIDATION
                int aliveCount = 0;
                int unusedCount = 0;
                for (int idx = 0; idx < m_SlotCount; idx++)
                {
                    ref var slot = ref m_Slots[idx];
                    if (!slot.IsAlive())
                        continue;

                    aliveCount++;
                    object target = slot.Obj.Target;
                    if (!m_TargetMap.TryGetValue(target, out int mappedIdx) || mappedIdx != idx)
                    {
                        Log.Error($"Object pool '{FullName}' target index map is inconsistent.");
                        continue;
                    }

                    if (slot.SpawnCount == 0)
                        unusedCount++;
                }

                if (aliveCount != m_TargetMap.Count)
                    Log.Error($"Object pool '{FullName}' alive count is inconsistent.");

                int walkUnusedCount = 0;
                int current = m_UnusedHead;
                int prevUnused = -1;
                while (current >= 0)
                {
                    ref var slot = ref m_Slots[current];
                    if (!slot.IsAlive() || slot.SpawnCount != 0)
                        Log.Error($"Object pool '{FullName}' unused chain contains invalid slot.");
                    if (slot.PrevUnused != prevUnused)
                        Log.Error($"Object pool '{FullName}' unused chain linkage is inconsistent.");
                    walkUnusedCount++;
                    prevUnused = current;
                    current = slot.NextUnused;
                }

                if (walkUnusedCount != unusedCount || walkUnusedCount != m_UnusedCount)
                    Log.Error($"Object pool '{FullName}' unused chain count is inconsistent.");
#endif
            }
        }
    }
}
