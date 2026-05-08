using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static class MemoryPool<T> where T : class, IMemory, new()
    {
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly MemoryPoolRegistry.MemoryPoolHandle s_Handle;
        private static T[] s_Stack = Array.Empty<T>();
        private static int s_Count;
        private static int s_SoftCapacity = 2048;
        private static int s_HardCapacity = 8192;
        private static Dictionary<T, byte> s_InPoolSet;
        private static int s_StrictCheckVersion = -1;

        private static int s_CurrentInUse;
        private static int s_PeakInUseShort;
        private static int s_PeakInUseLong;
        private static int s_AcquireThisFrame;
        private static int s_ReleaseThisFrame;
        private static int s_TargetReserve = MIN_KEEP;
        private static int s_IdleFrames;
        private static int s_HotFrames;
        private static int s_LastTickFrame = -1;
        private static int s_ConsecutiveMiss;

        private const int MIN_KEEP = 4;
        private const int SHORT_DECAY_START = 300;
        private const int LONG_DECAY_START = 1800;
        private const int UNSCHEDULE_IDLE_FRAMES = 3600;

        private static int s_AcquireCount;
        private static int s_ReleaseCount;
        private static int s_CreateCount;

        static MemoryPool()
        {
            s_Handle = new MemoryPoolRegistry.MemoryPoolHandle(
                acquire: AcquireAsMemory,
                release: ReleaseAsMemory,
                clear: ClearAll,
                prewarm: Prewarm,
                getInfo: GetInfo,
                tick: Tick,
                shrink: Shrink,
                compact: Compact);
            MemoryPoolRegistry.Register(typeof(T), s_Handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IMemory AcquireAsMemory()
        {
            return Acquire();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReleaseAsMemory(IMemory memory)
        {
            Release((T)memory);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Acquire()
        {
            MemoryPoolRegistry.ScheduleTick(s_Handle);
            s_AcquireCount++;
            s_AcquireThisFrame++;
            s_CurrentInUse++;
            if (s_CurrentInUse > s_PeakInUseShort)
                s_PeakInUseShort = s_CurrentInUse;
            if (s_CurrentInUse > s_PeakInUseLong)
                s_PeakInUseLong = s_CurrentInUse;

            if (s_Count > 0)
            {
                s_ConsecutiveMiss = 0;
                int idx = --s_Count;
                T item = s_Stack[idx];
                s_Stack[idx] = null;
                RemoveFromStrictCheckSet(item);
                return item;
            }

            s_ConsecutiveMiss++;
            return CreateNew();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T CreateNew()
        {
            s_CreateCount++;
            return new T();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release(T item)
        {
            if (item == null) return;

            MemoryPoolRegistry.ScheduleTick(s_Handle);
            EnsureStrictCheckState();

            if (MemoryPool.EnableStrictCheck && s_InPoolSet.ContainsKey(item))
                throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: Double release detected.");

            s_ReleaseCount++;
            s_ReleaseThisFrame++;

            if (s_CurrentInUse > 0)
                s_CurrentInUse--;

            item.Clear();

            if (s_Count >= s_HardCapacity)
                return;

            EnsureStackCapacity(s_Count + 1);
            AddToStrictCheckSet(item);
            s_Stack[s_Count++] = item;
        }

        internal static bool Tick(int frameCount)
        {
            if (frameCount == s_LastTickFrame) return true;
            s_LastTickFrame = frameCount;

            bool active = s_AcquireThisFrame > 0 || s_ReleaseThisFrame > 0 || s_CurrentInUse > 0;
            if (active)
            {
                s_HotFrames++;
                s_IdleFrames = 0;
            }
            else
            {
                s_IdleFrames++;
                s_HotFrames = 0;
            }

            UpdateTargetReserve();
            FillReserveBudgeted();
            ReleaseExcessBudgeted();

            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;

            return s_IdleFrames < UNSCHEDULE_IDLE_FRAMES || s_Count > s_TargetReserve;
        }

        private static void UpdateTargetReserve()
        {
            if (s_IdleFrames >= SHORT_DECAY_START && s_PeakInUseShort > 0)
                s_PeakInUseShort -= Math.Max(1, s_PeakInUseShort >> 4);

            if (s_IdleFrames >= LONG_DECAY_START && s_PeakInUseLong > 0)
                s_PeakInUseLong -= Math.Max(1, s_PeakInUseLong >> 6);

            int shortReserve = s_PeakInUseShort + (s_PeakInUseShort >> 1);
            int longReserve = s_PeakInUseLong + (s_PeakInUseLong >> 2);
            int desired = Math.Max(shortReserve, longReserve);
            if (desired < MIN_KEEP) desired = MIN_KEEP;
            if (desired > s_SoftCapacity) desired = s_SoftCapacity;
            s_TargetReserve = desired;
        }

        private static void FillReserveBudgeted()
        {
            int available = s_Count + s_CurrentInUse;
            if (available >= s_TargetReserve || s_Count >= s_SoftCapacity)
                return;

            int need = s_TargetReserve - available;
            int budget = GetCreateBudget();
            int createCount = Math.Min(need, budget);
            int room = s_SoftCapacity - s_Count;
            if (createCount > room) createCount = room;

            for (int i = 0; i < createCount; i++)
            {
                EnsureStackCapacity(s_Count + 1);
                T item = new T();
                s_CreateCount++;
                s_Stack[s_Count++] = item;
                AddToStrictCheckSet(item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetCreateBudget()
        {
            if (s_ConsecutiveMiss > 0) return 8;
            if (s_HotFrames > 0) return 4;
            return 1;
        }

        private static void ReleaseExcessBudgeted()
        {
            if (s_IdleFrames < SHORT_DECAY_START || s_Count <= s_TargetReserve)
                return;

            int excess = s_Count - s_TargetReserve;
            int budget = s_IdleFrames < LONG_DECAY_START ? 4 : 16;
            int removeCount = Math.Min(excess, budget);
            int newCount = s_Count - removeCount;
            RemoveRangeFromStrictCheckSet(newCount, removeCount);
            Array.Clear(s_Stack, newCount, removeCount);
            s_Count = newCount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureStackCapacity(int required)
        {
            if (s_Stack.Length >= required)
                return;

            int newLen = s_Stack.Length == 0 ? 8 : s_Stack.Length;
            while (newLen < required)
                newLen <<= 1;
            if (newLen > s_HardCapacity)
                newLen = s_HardCapacity;

            var newStack = new T[newLen];
            Array.Copy(s_Stack, 0, newStack, 0, s_Count);
            s_Stack = newStack;
        }

        public static void Prewarm(int count)
        {
            MemoryPoolRegistry.ScheduleTick(s_Handle);
            count = Math.Min(count, s_HardCapacity);
            if (count <= s_Count) return;

            EnsureStackCapacity(count);

            while (s_Count < count)
            {
                T item = new T();
                s_Stack[s_Count++] = item;
                AddToStrictCheckSet(item);
                s_CreateCount++;
            }

            if (count > s_TargetReserve)
                s_TargetReserve = Math.Min(count, s_SoftCapacity);
        }

        public static void Shrink(int keepCount)
        {
            if (keepCount >= s_Count) return;
            keepCount = Math.Max(keepCount, 0);

            RemoveRangeFromStrictCheckSet(keepCount, s_Count - keepCount);
            Array.Clear(s_Stack, keepCount, s_Count - keepCount);
            s_Count = keepCount;
            if (s_TargetReserve > keepCount)
                s_TargetReserve = Math.Max(keepCount, MIN_KEEP);
        }

        public static void Compact()
        {
            int newLen = s_Count <= 0 ? 0 : Math.Max(NextPowerOfTwo(s_Count), MIN_KEEP);
            if (newLen == s_Stack.Length)
                return;

            if (newLen == 0)
            {
                s_Stack = Array.Empty<T>();
                return;
            }

            var newStack = new T[newLen];
            Array.Copy(s_Stack, 0, newStack, 0, s_Count);
            s_Stack = newStack;
        }

        public static void SetMaxCapacity(int max)
        {
            SetCapacity(max, Math.Max(max << 2, MIN_KEEP));
        }

        public static void SetCapacity(int softCapacity, int hardCapacity)
        {
            softCapacity = Math.Max(softCapacity, MIN_KEEP);
            hardCapacity = Math.Max(hardCapacity, softCapacity);
            s_SoftCapacity = softCapacity;
            s_HardCapacity = hardCapacity;
            if (s_TargetReserve > s_SoftCapacity)
                s_TargetReserve = s_SoftCapacity;
        }

        public static void ClearAll()
        {
            MemoryPoolRegistry.UnscheduleTick(s_Handle);
            ResetStrictCheckSet();
            Array.Clear(s_Stack, 0, s_Count);
            s_Count = 0;
            s_CurrentInUse = 0;
            s_PeakInUseShort = 0;
            s_PeakInUseLong = 0;
            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;
            s_TargetReserve = MIN_KEEP;
            s_IdleFrames = 0;
            s_HotFrames = 0;
            s_ConsecutiveMiss = 0;
            s_Stack = Array.Empty<T>();
        }

        public static int UnusedCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_Count;
        }

        internal static void GetInfo(ref MemoryPoolInfo info)
        {
            info.Set(
                typeof(T), s_Count,
                s_CurrentInUse,
                s_AcquireCount, s_ReleaseCount,
                s_CreateCount,
                s_TargetReserve, s_SoftCapacity,
                s_IdleFrames, s_Stack.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureStrictCheckState()
        {
            int version = MemoryPool.StrictCheckVersion;
            if (s_StrictCheckVersion == version)
                return;

            s_StrictCheckVersion = version;
            if (!MemoryPool.EnableStrictCheck)
            {
                s_InPoolSet = null;
                return;
            }

            RebuildStrictCheckSet();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RebuildStrictCheckSet()
        {
            int capacity = s_Count > 0 ? s_Count : 4;
            var set = new Dictionary<T, byte>(capacity, ReferenceComparer.Instance);
            for (int i = 0; i < s_Count; i++)
            {
                T item = s_Stack[i];
                if (item != null)
                    set[item] = 0;
            }

            s_InPoolSet = set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddToStrictCheckSet(T item)
        {
            EnsureStrictCheckState();
            if (!MemoryPool.EnableStrictCheck || item == null)
                return;

            s_InPoolSet[item] = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RemoveFromStrictCheckSet(T item)
        {
            EnsureStrictCheckState();
            if (!MemoryPool.EnableStrictCheck || item == null)
                return;

            s_InPoolSet.Remove(item);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RemoveRangeFromStrictCheckSet(int startIndex, int count)
        {
            EnsureStrictCheckState();
            if (!MemoryPool.EnableStrictCheck || count <= 0)
                return;

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                T item = s_Stack[i];
                if (item != null)
                    s_InPoolSet.Remove(item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ResetStrictCheckSet()
        {
            EnsureStrictCheckState();
            if (!MemoryPool.EnableStrictCheck)
                return;

            s_InPoolSet.Clear();
        }

        private static int NextPowerOfTwo(int value)
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
}
