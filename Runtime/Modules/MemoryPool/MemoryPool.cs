using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static class MemoryPool
    {
        public static int Count => MemoryPoolRegistry.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Acquire<T>() where T : MemoryObject, new()
        {
            return MemoryPool<T>.Instance.Acquire();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release(MemoryObject memory)
        {
            memory.Pool.Return(memory);
        }

        public static void SetBudget(in MemoryPoolBudget budget)
        {
            MemoryPoolRegistry.SetBudget(budget);
        }

        public static void TrimAll()
        {
            MemoryPoolRegistry.TrimAll(0);
        }

        public static int GetInfos(Span<MemoryPoolInfo> destination)
        {
            return MemoryPoolRegistry.GetInfos(destination);
        }
    }
}
