using System;
using System.Buffers;

namespace AlicizaX.ObjectPool
{
    /// <summary>
    /// 数组池管理器，避免频繁分配数组
    /// </summary>
    internal static class SlotArrayPool<T>
    {
        public static T[] Rent(int minimumLength)
        {
            return ArrayPool<T>.Shared.Rent(minimumLength);
        }

        public static void Return(T[] array, bool clearArray = false)
        {
            if (array != null)
                ArrayPool<T>.Shared.Return(array, clearArray);
        }
    }
}
