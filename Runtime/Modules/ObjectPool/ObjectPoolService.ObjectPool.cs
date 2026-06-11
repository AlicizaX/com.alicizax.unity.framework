using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
                public int PrevByName;
                public int NextByName;
                public int PrevAvailableByName;
                public int NextAvailableByName;
                public int PrevUnused;
                public int NextUnused;
                public byte Flags;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public bool IsAlive() => (Flags & 1) != 0;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void SetAlive(bool alive)
                {
                    if (alive) Flags |= 1;
                    else Flags = 0;
                }
            }

            private ObjectSlot[][] m_Pages;
            private int[][] m_PageFreeStacks;
            private int[] m_PageAliveCounts;
            private int[] m_PageFreeTops;
            private byte[] m_PageFlags;
            private int m_PageCount;
            private int[] m_FreePageStack;
            private int m_FreePageTop;
            private int[] m_EmptyPageStack;
            private int m_EmptyPageTop;
            private int[] m_ReleasedPageStack;
            private int m_ReleasedPageTop;

            private ReferenceOpenHashMap m_TargetMap;
            private StringOpenHashMap m_AllNameHeadMap;
            private StringOpenHashMap m_AvailableNameHeadMap;

            private readonly bool m_AllowMultiSpawn;
            private readonly MemoryPoolHandle m_ObjectMemoryPoolHandle;
            private float m_AutoReleaseInterval;
            private int m_Capacity;
            private float m_ExpireTime;
            private int m_Priority;
            private float m_AutoReleaseTime;

            private int m_PendingReleaseCount;
            private int m_ReleasePerFrameBudget;
            private int m_UnusedHead;
            private int m_UnusedTail;
            private int m_LastBudgetScanStart;
            private bool m_IsShuttingDown;
            private int m_ShrinkCounter;
            private const int ShrinkCheckInterval = 60;

            private const int DefaultReleasePerFrame = 8;
            private const int InitSlotCapacity = 16;
            private const int InitPageCapacity = 4;
            private const int PageBits = 8;
            private const int PageSize = 1 << PageBits;
            private const int PageMask = PageSize - 1;
            private const byte PageAllocated = 1;
            private const byte PageInFreeStack = 2;
            private const byte PageInEmptyStack = 4;
            private const int EmptyPageReleaseBudget = 1;

            public ObjectPool(string name, bool allowMultiSpawn,
                float autoReleaseInterval, int capacity, float expireTime, int priority)
                : base(name)
            {
                int initCap = Math.Min(Math.Max(capacity, 1), InitSlotCapacity);
                m_Pages = SlotArrayPool<ObjectSlot[]>.Rent(InitPageCapacity);
                m_PageFreeStacks = SlotArrayPool<int[]>.Rent(InitPageCapacity);
                m_PageAliveCounts = SlotArrayPool<int>.Rent(InitPageCapacity);
                m_PageFreeTops = SlotArrayPool<int>.Rent(InitPageCapacity);
                m_PageFlags = SlotArrayPool<byte>.Rent(InitPageCapacity);
                m_FreePageStack = SlotArrayPool<int>.Rent(InitPageCapacity);
                m_EmptyPageStack = SlotArrayPool<int>.Rent(InitPageCapacity);
                m_ReleasedPageStack = SlotArrayPool<int>.Rent(InitPageCapacity);
                m_TargetMap = new ReferenceOpenHashMap(initCap);
                m_AllNameHeadMap = new StringOpenHashMap(initCap);
                m_AvailableNameHeadMap = new StringOpenHashMap(initCap);
                m_PageCount = 0;
                m_FreePageTop = 0;
                m_EmptyPageTop = 0;
                m_ReleasedPageTop = 0;
                m_AllowMultiSpawn = allowMultiSpawn;
                m_ObjectMemoryPoolHandle = MemoryPool.GetHandle(typeof(T));
                m_AutoReleaseInterval = autoReleaseInterval;
                m_Capacity = capacity;
                m_ExpireTime = expireTime;
                m_Priority = priority;
                m_AutoReleaseTime = 0f;
                m_PendingReleaseCount = 0;
                m_ReleasePerFrameBudget = DefaultReleasePerFrame;
                m_UnusedHead = -1;
                m_UnusedTail = -1;
                m_LastBudgetScanStart = -1;
                m_IsShuttingDown = false;
                m_ShrinkCounter = 0;
            }

            public override Type ObjectType => typeof(T);
            public override int Count => m_TargetMap.Count;
            public override bool AllowMultiSpawn => m_AllowMultiSpawn;

            public override float AutoReleaseInterval
            {
                get => m_AutoReleaseInterval;
                set
                {
                    if (value < 0f)
                    {
#if UNITY_EDITOR
                        UnityEngine.Debug.LogError("AutoReleaseInterval is invalid.");
#endif
                        return;
                    }
                    m_AutoReleaseInterval = value;
                }
            }

            public override int Capacity
            {
                get => m_Capacity;
                set
                {
                    if (value < 0)
                    {
#if UNITY_EDITOR
                        UnityEngine.Debug.LogError("Capacity is invalid.");
#endif
                        return;
                    }
                    m_Capacity = value;
                    if (Count > m_Capacity) MarkRelease(Count - m_Capacity);
                }
            }

            public override float ExpireTime
            {
                get => m_ExpireTime;
                set
                {
                    if (value < 0f)
                    {
#if UNITY_EDITOR
                        UnityEngine.Debug.LogError("ExpireTime is invalid.");
#endif
                        return;
                    }
                    m_ExpireTime = value;
                }
            }

            public override int Priority
            {
                get => m_Priority;
                set => m_Priority = value;
            }

            public override int ReleasePerFrameBudget
            {
                get => m_ReleasePerFrameBudget;
                set => m_ReleasePerFrameBudget = Math.Max(1, value);
            }

            public void Register(T obj, bool spawned)
            {
                if (obj == null) return;
                if (obj.Target == null) return;

                if (m_TargetMap.TryGetValue(obj.Target, out int existingIdx) && GetSlotRef(existingIdx).IsAlive())
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError($"Target '{obj.Target.GetType().FullName}' is already registered in pool '{FullName}'.");
#endif
                    return;
                }

                if (!EnsureRegisterCapacity())
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError($"Object pool '{FullName}' capacity is full.");
#endif
                    return;
                }

                int idx = AllocSlot();
                if (idx < 0)
                    return;

                ref var slot = ref GetSlotRef(idx);
                slot.Obj = obj;
                slot.SpawnCount = spawned ? 1 : 0;
                slot.LastUseTime = Time.realtimeSinceStartup;
                slot.PrevByName = -1;
                slot.NextByName = -1;
                slot.PrevAvailableByName = -1;
                slot.NextAvailableByName = -1;
                slot.PrevUnused = -1;
                slot.NextUnused = -1;
                slot.SetAlive(true);

                m_TargetMap.AddOrUpdate(obj.Target, idx);

                string objectName = obj.Name ?? string.Empty;
                if (m_AllNameHeadMap.TryGetValue(objectName, out int existingHead))
                {
                    GetSlotRef(existingHead).PrevByName = idx;
                    slot.NextByName = existingHead;
                }
                m_AllNameHeadMap.AddOrUpdate(objectName, idx);

                obj.LastUseTime = slot.LastUseTime;
                if (spawned)
                    obj.OnSpawn();
                else
                    MarkSlotAvailable(idx);

                UpdateActiveState();
                ValidateState();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Spawn() => Spawn(string.Empty);

            public T Spawn(string name)
            {
                if (name == null) name = string.Empty;
                if (m_AllowMultiSpawn)
                    return SpawnAny(name);

                int head = FindAvailableByName(name);
                if (head < 0) return null;

                ref var slot = ref GetSlotRef(head);
                if (!slot.IsAlive() || slot.SpawnCount != 0 || !string.Equals(slot.Obj.Name, name, StringComparison.Ordinal))
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError($"Object pool '{FullName}' all-name chain is inconsistent.");
#endif
                    return null;
                }

                float now = Time.realtimeSinceStartup;
                SpawnSlot(head, now);
                ValidateState();
                return slot.Obj;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool CanSpawn() => CanSpawn(string.Empty);

            public bool CanSpawn(string name)
            {
                if (name == null) name = string.Empty;
                if (m_AllowMultiSpawn)
                    return m_AllNameHeadMap.ContainsKey(name);

                return FindAvailableByName(name) >= 0;
            }

            private int FindAvailableByName(string name)
            {
                if (!m_AvailableNameHeadMap.TryGetValue(name, out int head))
                    return -1;

                ref var slot = ref GetSlotRef(head);
                if (!slot.IsAlive()
                    || slot.SpawnCount != 0
                    || !string.Equals(slot.Obj.Name, name, StringComparison.Ordinal))
                    return -1;

                return head;
            }

            public void Unspawn(T obj)
            {
                if (obj == null) return;
                if (obj.Target == null) return;
                if (!m_TargetMap.TryGetValue(obj.Target, out int idx))
                {
                    if (m_IsShuttingDown) return;
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError($"Cannot find target in pool '{Name}', type='{obj.Target.GetType().FullName}'");
#endif
                    return;
                }

                UnspawnSlot(idx);
                ValidateState();
            }

            public void UnspawnTarget(object target)
            {
                if (target == null) return;
                if (!m_TargetMap.TryGetValue(target, out int idx))
                {
                    if (m_IsShuttingDown) return;
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError($"Cannot find target in pool '{Name}', type='{target.GetType().FullName}'");
#endif
                    return;
                }

                UnspawnSlot(idx);
                ValidateState();
            }

            public override void Release()
            {
                MarkRelease(Count - m_Capacity);
            }

            public override void Release(int toReleaseCount)
            {
                MarkRelease(toReleaseCount);
            }

            public override void ReleaseAllUnused()
            {
                int released = 0;
                int current = m_UnusedHead;
                while (current >= 0)
                {
                    int next = GetSlotRef(current).NextUnused;
                    ref var slot = ref GetSlotRef(current);
                    if (CanReleaseSlot(ref slot))
                    {
                        ReleaseSlot(current, false);
                        released++;
                    }
                    current = next;
                }

                int releasedPages = ReleaseEmptyPages(int.MaxValue);
                if (released > 0 || releasedPages > 0)
                {
                    m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - released);
                    UpdateActiveState();
                    ValidateState();
                }
            }

            internal override void Update(float elapseSeconds, float realElapseSeconds)
            {
                m_AutoReleaseTime += realElapseSeconds;
                if (m_AutoReleaseTime >= m_AutoReleaseInterval)
                {
                    m_AutoReleaseTime = 0f;
                    MarkRelease(Count - m_Capacity);
                }

                bool checkExpire = m_ExpireTime < float.MaxValue;
                if (m_PendingReleaseCount <= 0 && !checkExpire)
                {
                    TryProgressiveShrink();
                    UpdateActiveState();
                    return;
                }

                float now = Time.realtimeSinceStartup;
                float expireThreshold = checkExpire ? now - m_ExpireTime : float.MinValue;
                if (m_PendingReleaseCount > 0)
                {
                    int releaseBudget = Math.Min(m_ReleasePerFrameBudget, m_PendingReleaseCount);
                    int releasedByBudget = ReleaseUnused(releaseBudget, false, float.MinValue);
                    m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - releasedByBudget);
                }
                else if (checkExpire)
                {
                    ReleaseExpired(m_ReleasePerFrameBudget, expireThreshold);
                }

                TryProgressiveShrink();
                UpdateActiveState();
            }

            private void TryProgressiveShrink()
            {
                m_ShrinkCounter++;
                if (m_ShrinkCounter < ShrinkCheckInterval)
                    return;

                m_ShrinkCounter = 0;
                ReleaseEmptyPages(EmptyPageReleaseBudget);
            }

            internal override void Shutdown()
            {
                m_IsShuttingDown = true;
                for (int page = 0; page < m_PageCount; page++)
                {
                    ObjectSlot[] slots = m_Pages[page];
                    if (slots == null) continue;

                    for (int offset = 0; offset < PageSize; offset++)
                    {
                        ref var slot = ref slots[offset];
                        if (!slot.IsAlive()) continue;
                        slot.Obj.Release(true);
                        m_ObjectMemoryPoolHandle.Release(slot.Obj);
                        slot.Obj = null;
                        slot.SetAlive(false);
                    }
                }

                m_TargetMap.Dispose();
                m_AllNameHeadMap.Dispose();
                m_AvailableNameHeadMap.Dispose();

                ReleaseAllPages();
                ReturnPageStorage();

                m_PageCount = 0;
                m_FreePageTop = 0;
                m_EmptyPageTop = 0;
                m_ReleasedPageTop = 0;
                m_PendingReleaseCount = 0;
                m_UnusedHead = -1;
                m_UnusedTail = -1;
                m_LastBudgetScanStart = -1;
                m_IsShuttingDown = false;
                ValidateState();
            }

            public override int GetAllObjectInfos(ObjectInfo[] results)
            {
                if (results == null)
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError("Results is invalid.");
#endif
                    return 0;
                }

                int write = 0;
                int capacity = results.Length;
                for (int page = 0; page < m_PageCount; page++)
                {
                    ObjectSlot[] slots = m_Pages[page];
                    if (slots == null) continue;

                    for (int offset = 0; offset < PageSize; offset++)
                    {
                        ref var slot = ref slots[offset];
                        if (!slot.IsAlive()) continue;

                        if (write < capacity)
                        {
                            results[write] = new ObjectInfo(slot.Obj.Name, slot.Obj.Locked,
                                slot.Obj.CustomCanReleaseFlag,
                                slot.Obj.LastUseTime, slot.SpawnCount);
                        }

                        write++;
                    }
                }

                return write;
            }

            internal override void OnLowMemory()
            {
                ReleaseAllUnused();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void SpawnSlot(int idx, float now)
            {
                ref var slot = ref GetSlotRef(idx);
                if (slot.SpawnCount == 0)
                    MarkSlotUnavailable(idx);

                slot.SpawnCount++;
                slot.LastUseTime = now;
                slot.Obj.LastUseTime = now;
                slot.Obj.OnSpawn();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void UnspawnSlot(int idx)
            {
                ref var slot = ref GetSlotRef(idx);
                float now = Time.realtimeSinceStartup;
                slot.LastUseTime = now;
                slot.Obj.LastUseTime = now;
                slot.Obj.OnUnspawn();
                slot.SpawnCount--;
                if (slot.SpawnCount < 0)
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.LogError($"Object '{slot.Obj.Name}' spawn count < 0.");
#endif
                    slot.SpawnCount = 0;
                }
                if (slot.SpawnCount == 0)
                    MarkSlotAvailable(idx);
                if (Count > m_Capacity && slot.SpawnCount == 0)
                    MarkRelease(Count - m_Capacity);
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
                int page = GetWritablePage();
                if (page < 0)
                    return -1;

                int freeTop = m_PageFreeTops[page] - 1;
                int offset = m_PageFreeStacks[page][freeTop];
                m_PageFreeTops[page] = freeTop;
                m_PageAliveCounts[page]++;

                if (freeTop == 0)
                    RemoveFreePage(page);

                return MakeIndex(page, offset);
            }

            private int GetWritablePage()
            {
                while (m_FreePageTop > 0)
                {
                    int page = m_FreePageStack[m_FreePageTop - 1];
                    if (IsAllocatedPage(page) && m_PageFreeTops[page] > 0)
                        return page;

                    m_FreePageTop--;
                    if (page >= 0 && page < m_PageFlags.Length)
                        m_PageFlags[page] = (byte)(m_PageFlags[page] & ~PageInFreeStack);
                }

                return AllocatePage();
            }

            private int AllocatePage()
            {
                int page;
                if (m_ReleasedPageTop > 0)
                {
                    page = m_ReleasedPageStack[--m_ReleasedPageTop];
                }
                else
                {
                    EnsurePageStorageCapacity(m_PageCount + 1);
                    page = m_PageCount++;
                }

                m_Pages[page] = SlotArrayPool<ObjectSlot>.Rent(PageSize);
                m_PageFreeStacks[page] = SlotArrayPool<int>.Rent(PageSize);
                for (int offset = 0; offset < PageSize; offset++)
                    m_PageFreeStacks[page][offset] = offset;

                m_PageAliveCounts[page] = 0;
                m_PageFreeTops[page] = PageSize;
                m_PageFlags[page] = PageAllocated;
                AddFreePage(page);
                return page;
            }

            private void ReleaseSlot(int idx, bool compactStorage = true)
            {
                ref var slot = ref GetSlotRef(idx);
                if (!slot.IsAlive()) return;
                if (slot.SpawnCount > 0) return;

                T obj = slot.Obj;
                MarkSlotUnavailable(idx);

                RemoveFromAllNameChain(idx);
                m_TargetMap.Remove(obj.Target);

                obj.Release(false);
                m_ObjectMemoryPoolHandle.Release(obj);

                slot.Obj = null;
                slot.SetAlive(false);
                slot.SpawnCount = 0;
                slot.PrevByName = -1;
                slot.NextByName = -1;
                slot.PrevAvailableByName = -1;
                slot.NextAvailableByName = -1;
                slot.PrevUnused = -1;
                slot.NextUnused = -1;

                int page = PageOf(idx);
                int offset = OffsetOf(idx);
                m_PageFreeStacks[page][m_PageFreeTops[page]++] = offset;
                m_PageAliveCounts[page]--;

                if (m_PageFreeTops[page] == 1)
                    AddFreePage(page);

                if (m_PageAliveCounts[page] == 0)
                    AddEmptyPageCandidate(page);

                if (compactStorage)
                    ReleaseEmptyPages(EmptyPageReleaseBudget);
            }

            private bool EnsureRegisterCapacity()
            {
                if (m_Capacity == int.MaxValue || Count < m_Capacity)
                    return true;

                int released = ReleaseUnused(1, false, float.MinValue);
                if (released > 0)
                {
                    m_PendingReleaseCount = Math.Max(0, m_PendingReleaseCount - released);
                    return Count < m_Capacity;
                }

                return false;
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
            private ref ObjectSlot GetSlotRef(int index)
            {
                return ref m_Pages[index >> PageBits][index & PageMask];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsAllocatedPage(int page)
            {
                return page >= 0
                       && page < m_PageCount
                       && (m_PageFlags[page] & PageAllocated) != 0
                       && m_Pages[page] != null;
            }

            private void EnsurePageStorageCapacity(int required)
            {
                if (required <= m_Pages.Length)
                    return;

                int newCap = Math.Max(required, m_Pages.Length * 2);
                var newPages = SlotArrayPool<ObjectSlot[]>.Rent(newCap);
                var newPageFreeStacks = SlotArrayPool<int[]>.Rent(newCap);
                var newPageAliveCounts = SlotArrayPool<int>.Rent(newCap);
                var newPageFreeTops = SlotArrayPool<int>.Rent(newCap);
                var newPageFlags = SlotArrayPool<byte>.Rent(newCap);
                var newFreePageStack = SlotArrayPool<int>.Rent(newCap);
                var newEmptyPageStack = SlotArrayPool<int>.Rent(newCap);
                var newReleasedPageStack = SlotArrayPool<int>.Rent(newCap);

                Array.Copy(m_Pages, 0, newPages, 0, m_PageCount);
                Array.Copy(m_PageFreeStacks, 0, newPageFreeStacks, 0, m_PageCount);
                Array.Copy(m_PageAliveCounts, 0, newPageAliveCounts, 0, m_PageCount);
                Array.Copy(m_PageFreeTops, 0, newPageFreeTops, 0, m_PageCount);
                Array.Copy(m_PageFlags, 0, newPageFlags, 0, m_PageCount);
                Array.Copy(m_FreePageStack, 0, newFreePageStack, 0, m_FreePageTop);
                Array.Copy(m_EmptyPageStack, 0, newEmptyPageStack, 0, m_EmptyPageTop);
                Array.Copy(m_ReleasedPageStack, 0, newReleasedPageStack, 0, m_ReleasedPageTop);

                SlotArrayPool<ObjectSlot[]>.Return(m_Pages, true);
                SlotArrayPool<int[]>.Return(m_PageFreeStacks, true);
                SlotArrayPool<int>.Return(m_PageAliveCounts, true);
                SlotArrayPool<int>.Return(m_PageFreeTops, true);
                SlotArrayPool<byte>.Return(m_PageFlags, true);
                SlotArrayPool<int>.Return(m_FreePageStack, true);
                SlotArrayPool<int>.Return(m_EmptyPageStack, true);
                SlotArrayPool<int>.Return(m_ReleasedPageStack, true);

                m_Pages = newPages;
                m_PageFreeStacks = newPageFreeStacks;
                m_PageAliveCounts = newPageAliveCounts;
                m_PageFreeTops = newPageFreeTops;
                m_PageFlags = newPageFlags;
                m_FreePageStack = newFreePageStack;
                m_EmptyPageStack = newEmptyPageStack;
                m_ReleasedPageStack = newReleasedPageStack;
            }


            private static void EnsurePageIndexStackCapacity(ref int[] stack, int required)
            {
                if (required <= stack.Length)
                    return;

                int newCap = Math.Max(required, stack.Length * 2);
                var newStack = SlotArrayPool<int>.Rent(newCap);
                Array.Copy(stack, 0, newStack, 0, stack.Length);
                SlotArrayPool<int>.Return(stack, true);
                stack = newStack;
            }

            private void AddFreePage(int page)
            {
                if ((m_PageFlags[page] & PageInFreeStack) != 0)
                    return;

                m_PageFlags[page] = (byte)(m_PageFlags[page] | PageInFreeStack);
                EnsurePageIndexStackCapacity(ref m_FreePageStack, m_FreePageTop + 1);
                m_FreePageStack[m_FreePageTop++] = page;
            }

            private void RemoveFreePage(int page)
            {
                if ((m_PageFlags[page] & PageInFreeStack) == 0)
                    return;

                m_PageFlags[page] = (byte)(m_PageFlags[page] & ~PageInFreeStack);
            }

            private void AddEmptyPageCandidate(int page)
            {
                if ((m_PageFlags[page] & PageInEmptyStack) != 0)
                    return;

                m_PageFlags[page] = (byte)(m_PageFlags[page] | PageInEmptyStack);
                EnsurePageIndexStackCapacity(ref m_EmptyPageStack, m_EmptyPageTop + 1);
                m_EmptyPageStack[m_EmptyPageTop++] = page;
            }

            private int ReleaseEmptyPages(int budget)
            {
                int released = 0;
                while (budget > 0 && m_EmptyPageTop > 0)
                {
                    int page = m_EmptyPageStack[--m_EmptyPageTop];
                    m_PageFlags[page] = (byte)(m_PageFlags[page] & ~PageInEmptyStack);
                    if (!IsAllocatedPage(page) || m_PageAliveCounts[page] != 0)
                        continue;

                    ReleasePage(page);
                    AddReleasedPage(page);
                    released++;
                    budget--;
                }
                return released;
            }

            private void AddReleasedPage(int page)
            {
                EnsurePageIndexStackCapacity(ref m_ReleasedPageStack, m_ReleasedPageTop + 1);
                m_ReleasedPageStack[m_ReleasedPageTop++] = page;
            }

            private void ReleasePage(int page)
            {
                SlotArrayPool<ObjectSlot>.Return(m_Pages[page], true);
                SlotArrayPool<int>.Return(m_PageFreeStacks[page], true);
                m_Pages[page] = null;
                m_PageFreeStacks[page] = null;
                m_PageAliveCounts[page] = 0;
                m_PageFreeTops[page] = 0;
                m_PageFlags[page] = 0;
            }

            private void ReleaseAllPages()
            {
                for (int page = 0; page < m_PageCount; page++)
                {
                    if (m_Pages[page] != null)
                        SlotArrayPool<ObjectSlot>.Return(m_Pages[page], true);
                    if (m_PageFreeStacks[page] != null)
                        SlotArrayPool<int>.Return(m_PageFreeStacks[page], true);
                    m_Pages[page] = null;
                    m_PageFreeStacks[page] = null;
                    m_PageAliveCounts[page] = 0;
                    m_PageFreeTops[page] = 0;
                    m_PageFlags[page] = 0;
                }
            }

            private void ReturnPageStorage()
            {
                SlotArrayPool<ObjectSlot[]>.Return(m_Pages, true);
                SlotArrayPool<int[]>.Return(m_PageFreeStacks, true);
                SlotArrayPool<int>.Return(m_PageAliveCounts, true);
                SlotArrayPool<int>.Return(m_PageFreeTops, true);
                SlotArrayPool<byte>.Return(m_PageFlags, true);
                SlotArrayPool<int>.Return(m_FreePageStack, true);
                SlotArrayPool<int>.Return(m_EmptyPageStack, true);
                SlotArrayPool<int>.Return(m_ReleasedPageStack, true);
                m_Pages = null;
                m_PageFreeStacks = null;
                m_PageAliveCounts = null;
                m_PageFreeTops = null;
                m_PageFlags = null;
                m_FreePageStack = null;
                m_EmptyPageStack = null;
                m_ReleasedPageStack = null;
            }

            private void RemoveFromAllNameChain(int idx)
            {
                ref var slot = ref GetSlotRef(idx);
                string objectName = slot.Obj.Name ?? string.Empty;
                if (!m_AllNameHeadMap.TryGetValue(objectName, out int head))
                    return;

                int prev = slot.PrevByName;
                int next = slot.NextByName;
                if (prev >= 0)
                {
                    GetSlotRef(prev).NextByName = next;
                }
                else
                {
                    if (head != idx)
                    {
#if UNITY_EDITOR
                        UnityEngine.Debug.LogError($"Object pool '{FullName}' all-name chain is inconsistent.");
#endif
                        return;
                    }

                    if (next >= 0)
                        m_AllNameHeadMap.AddOrUpdate(objectName, next);
                    else
                        m_AllNameHeadMap.Remove(objectName);
                }

                if (next >= 0)
                    GetSlotRef(next).PrevByName = prev;

                slot.PrevByName = -1;
                slot.NextByName = -1;
            }

            private int ReleaseUnused(int maxReleaseCount, bool requireExpired, float expireThreshold)
            {
                int released = 0;
                int scanned = 0;
                int current = requireExpired ? m_UnusedHead : GetBudgetScanStart();

                while (current >= 0 && scanned < maxReleaseCount && released < maxReleaseCount)
                {
                    scanned++;
                    ref var slot = ref GetSlotRef(current);
                    int next = slot.NextUnused;

                    if (requireExpired && slot.LastUseTime > expireThreshold)
                    {
                        current = next;
                        continue;
                    }

                    if (CanReleaseSlot(ref slot))
                    {
                        ReleaseSlot(current, false);
                        released++;
                    }

                    current = next;
                }

                if (!requireExpired)
                {
                    m_LastBudgetScanStart = current >= 0 ? current : m_UnusedHead;
                }

                if (released > 0)
                    ReleaseEmptyPages(EmptyPageReleaseBudget);

                return released;
            }

            private void ReleaseExpired(int maxReleaseCount, float expireThreshold)
            {
                ReleaseUnused(maxReleaseCount, true, expireThreshold);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int GetBudgetScanStart()
            {
                if (m_LastBudgetScanStart >= 0)
                {
                    ref var slot = ref GetSlotRef(m_LastBudgetScanStart);
                    if (slot.IsAlive() && slot.SpawnCount == 0)
                    {
                        return m_LastBudgetScanStart;
                    }
                }

                return m_UnusedHead;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool CanReleaseSlot(ref ObjectSlot slot)
            {
                return slot.IsAlive()
                       && slot.SpawnCount == 0
                       && !slot.Obj.Locked
                       && slot.Obj.CustomCanReleaseFlag;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsValidIndex(int index)
            {
                int page = index >> PageBits;
                int offset = index & PageMask;
                return offset < PageSize && IsAllocatedPage(page);
            }

            [Conditional("UNITY_EDITOR")]
            private void ValidateState()
            {
#if UNITY_EDITOR && ENABLE_OBJECTPOOL_VALIDATION
                int aliveCount = 0;
                int unusedCount = 0;
                for (int page = 0; page < m_PageCount; page++)
                {
                    ObjectSlot[] slots = m_Pages[page];
                    if (slots == null) continue;

                    for (int offset = 0; offset < PageSize; offset++)
                    {
                        int idx = MakeIndex(page, offset);
                        ref var slot = ref slots[offset];
                        if (!slot.IsAlive())
                            continue;

                        aliveCount++;

                        object target = slot.Obj.Target;
                        if (!m_TargetMap.TryGetValue(target, out int mappedIdx) || mappedIdx != idx)
                        {
                            UnityEngine.Debug.LogError($"Object pool '{FullName}' target index map is inconsistent.");
                            continue;
                        }

                        string objectName = slot.Obj.Name ?? string.Empty;
                        if (!m_AllNameHeadMap.TryGetValue(objectName, out int head))
                        {
                            UnityEngine.Debug.LogError($"Object pool '{FullName}' all-name head is missing.");
                            continue;
                        }

                        if (slot.PrevByName < 0 && head != idx)
                        {
                            UnityEngine.Debug.LogError($"Object pool '{FullName}' all-name chain head is inconsistent.");
                        }

                        if (slot.NextByName >= 0 && GetSlotRef(slot.NextByName).PrevByName != idx)
                        {
                            UnityEngine.Debug.LogError($"Object pool '{FullName}' all-name chain link is inconsistent.");
                        }

                        if (slot.SpawnCount == 0)
                        {
                            if (!m_AvailableNameHeadMap.TryGetValue(objectName, out int availableHead))
                            {
                                UnityEngine.Debug.LogError($"Object pool '{FullName}' available-name head is missing.");
                            }

                            if (slot.PrevAvailableByName < 0 && availableHead != idx)
                            {
                                UnityEngine.Debug.LogError($"Object pool '{FullName}' available-name chain head is inconsistent.");
                            }

                            if (slot.NextAvailableByName >= 0 && GetSlotRef(slot.NextAvailableByName).PrevAvailableByName != idx)
                            {
                                UnityEngine.Debug.LogError($"Object pool '{FullName}' available-name chain link is inconsistent.");
                            }
                        }

                        bool inUnusedList = m_UnusedHead == idx || slot.PrevUnused >= 0 || slot.NextUnused >= 0;

                        if (slot.SpawnCount == 0)
                        {
                            unusedCount++;
                            if (!inUnusedList)
                            {
                                UnityEngine.Debug.LogError($"Object pool '{FullName}' unused list is inconsistent.");
                            }
                        }
                        else
                        {
                            if (inUnusedList)
                            {
                                UnityEngine.Debug.LogError($"Object pool '{FullName}' spawned object exists in unused list.");
                            }
                        }
                    }
                }

                if (aliveCount != m_TargetMap.Count)
                {
                    UnityEngine.Debug.LogError($"Object pool '{FullName}' alive count is inconsistent.");
                }

                int walkUnusedCount = 0;
                int current = m_UnusedHead;
                int prevUnused = -1;
                while (current >= 0)
                {
                    ref var slot = ref GetSlotRef(current);
                    if (!slot.IsAlive() || slot.SpawnCount != 0)
                    {
                        UnityEngine.Debug.LogError($"Object pool '{FullName}' unused chain contains invalid slot.");
                    }
                    if (slot.PrevUnused != prevUnused)
                    {
                        UnityEngine.Debug.LogError($"Object pool '{FullName}' unused chain linkage is inconsistent.");
                    }

                    walkUnusedCount++;
                    prevUnused = current;
                    current = slot.NextUnused;
                }

                if (walkUnusedCount != unusedCount)
                {
                    UnityEngine.Debug.LogError($"Object pool '{FullName}' unused chain count is inconsistent.");
                }
#endif
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
                ref var slot = ref GetSlotRef(idx);
                if (!slot.IsAlive() || slot.SpawnCount != 0)
                    return;
                if (slot.PrevAvailableByName >= 0 || slot.NextAvailableByName >= 0)
                    return;

                string objectName = slot.Obj.Name ?? string.Empty;
                if (m_AvailableNameHeadMap.TryGetValue(objectName, out int head))
                {
                    if (head == idx)
                        return;

                    GetSlotRef(head).PrevAvailableByName = idx;
                    slot.NextAvailableByName = head;
                }
                else
                {
                    slot.NextAvailableByName = -1;
                }

                slot.PrevAvailableByName = -1;
                m_AvailableNameHeadMap.AddOrUpdate(objectName, idx);
            }

            private void RemoveFromAvailableNameChain(int idx)
            {
                ref var slot = ref GetSlotRef(idx);
                if (!slot.IsAlive())
                    return;

                string objectName = slot.Obj.Name ?? string.Empty;
                bool hasHead = m_AvailableNameHeadMap.TryGetValue(objectName, out int head);
                if (!hasHead && slot.PrevAvailableByName < 0 && slot.NextAvailableByName < 0)
                    return;

                int prev = slot.PrevAvailableByName;
                int next = slot.NextAvailableByName;

                if (prev >= 0)
                    GetSlotRef(prev).NextAvailableByName = next;
                else if (hasHead && head == idx)
                {
                    if (next >= 0) m_AvailableNameHeadMap.AddOrUpdate(objectName, next);
                    else m_AvailableNameHeadMap.Remove(objectName);
                }

                if (next >= 0)
                    GetSlotRef(next).PrevAvailableByName = prev;

                slot.PrevAvailableByName = -1;
                slot.NextAvailableByName = -1;
            }

            private void AddToUnusedListTail(int idx)
            {
                ref var slot = ref GetSlotRef(idx);
                if (m_UnusedHead == idx || slot.PrevUnused >= 0 || slot.NextUnused >= 0)
                    return;

                slot.PrevUnused = m_UnusedTail;
                slot.NextUnused = -1;

                if (m_UnusedTail >= 0)
                    GetSlotRef(m_UnusedTail).NextUnused = idx;
                else
                    m_UnusedHead = idx;

                m_UnusedTail = idx;
            }

            private void RemoveFromUnusedList(int idx)
            {
                ref var slot = ref GetSlotRef(idx);
                if (m_UnusedHead != idx && slot.PrevUnused < 0 && slot.NextUnused < 0)
                    return;

                int prev = slot.PrevUnused;
                int next = slot.NextUnused;

                if (prev >= 0)
                    GetSlotRef(prev).NextUnused = next;
                else
                    m_UnusedHead = next;

                if (next >= 0)
                    GetSlotRef(next).PrevUnused = prev;
                else
                    m_UnusedTail = prev;

                slot.PrevUnused = -1;
                slot.NextUnused = -1;

                if (m_LastBudgetScanStart == idx)
                    m_LastBudgetScanStart = next >= 0 ? next : m_UnusedHead;
            }

            private T SpawnAny(string name)
            {
                if (!m_AllNameHeadMap.TryGetValue(name, out int head))
                    return null;

                float now = Time.realtimeSinceStartup;
                ref var slot = ref GetSlotRef(head);
                if (!slot.IsAlive() || !string.Equals(slot.Obj.Name, name, StringComparison.Ordinal))
                    return null;

                SpawnSlot(head, now);
                ValidateState();
                return slot.Obj;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void UpdateActiveState()
            {
                IsActive = m_TargetMap.Count > 0 || m_PendingReleaseCount > 0;
            }
        }
    }
}
