using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace AlicizaX
{
    internal static class MemoryPoolRegistry
    {
        private const int InitialCapacity = 64;

        internal static MemoryPoolBudget Budget = MemoryPoolBudget.Default;

        private static MemoryPoolBase[] s_Pools = new MemoryPoolBase[InitialCapacity];
        private static int s_PoolCount;
        private static MemoryPoolBase[] s_ScheduledPools = new MemoryPoolBase[InitialCapacity];
        private static int s_ScheduledCount;
        private static int s_MainThreadId;

        internal static int Count => s_PoolCount;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void InitializeMainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != s_MainThreadId)
                throw new InvalidOperationException("MemoryPool must be used from the Unity main thread.");
        }

        internal static void Register(MemoryPoolBase pool)
        {
            if (s_PoolCount == s_Pools.Length)
                Array.Resize(ref s_Pools, s_PoolCount << 1);
            s_Pools[s_PoolCount++] = pool;
        }

        internal static void Schedule(MemoryPoolBase pool)
        {
            if (s_ScheduledCount == s_ScheduledPools.Length)
                Array.Resize(ref s_ScheduledPools, s_ScheduledCount << 1);
            s_ScheduledPools[s_ScheduledCount++] = pool;
        }

        internal static void TickAll(float now)
        {
            AssertMainThread();
            int index = 0;
            while (index < s_ScheduledCount)
            {
                if (s_ScheduledPools[index].Tick(now))
                {
                    index++;
                    continue;
                }

                s_ScheduledPools[index] = s_ScheduledPools[--s_ScheduledCount];
                s_ScheduledPools[s_ScheduledCount] = null;
            }
        }

        internal static void TrimAll(int keepFreeCount)
        {
            AssertMainThread();
            for (int i = 0; i < s_PoolCount; i++)
                s_Pools[i].Trim(keepFreeCount);
        }

        internal static void SetBudget(in MemoryPoolBudget budget)
        {
            AssertMainThread();
            Budget = budget;
            for (int i = 0; i < s_PoolCount; i++)
                s_Pools[i].ApplyBudget();
        }

        internal static int GetInfos(Span<MemoryPoolInfo> destination)
        {
            int count = destination.Length < s_PoolCount ? destination.Length : s_PoolCount;
            for (int i = 0; i < count; i++)
                destination[i] = s_Pools[i].GetInfo();
            return count;
        }
    }
}
