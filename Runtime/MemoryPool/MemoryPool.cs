using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static partial class MemoryPool
    {
        private static bool _enableStrictCheck;
        private static int _strictCheckVersion;


        public static bool EnableStrictCheck
        {
            get => _enableStrictCheck;
            set
            {
                if (_enableStrictCheck == value)
                    return;

                _enableStrictCheck = value;
                _strictCheckVersion++;
            }
        }

        internal static int StrictCheckVersion => _strictCheckVersion;


        public static int Count => MemoryPoolRegistry.Count;
        

        public static int GetAllMemoryPoolInfos(MemoryPoolInfo[] infos)
        {
            return MemoryPoolRegistry.GetAllInfos(infos);
        }


        public static void ClearAll()
        {
            MemoryPoolRegistry.ClearAll();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Acquire<T>() where T : class, IMemory, new()
        {
            return MemoryPool<T>.Acquire();
        }

        /// <summary>
        /// 获取动态内存类型的缓存句柄。运行时热路径应提前缓存该句柄，避免反复使用 Type 查找。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryPoolHandle GetHandle(Type memoryType)
        {
            return MemoryPoolRegistry.GetHandle(memoryType);
        }

        /// <summary>
        /// 慢速动态路径。禁止在运行时热路径调用；请提前通过 GetHandle(Type) 缓存 MemoryPoolHandle 后再获取对象。
        /// </summary>
        [Obsolete("慢速动态路径，禁止在运行时热路径使用。请缓存 MemoryPoolHandle，或改用 MemoryPool<T>.Acquire / MemoryPool.Acquire<T>。", false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IMemory Acquire(Type memoryType)
        {
            return MemoryPoolRegistry.Acquire(memoryType);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release<T>(T memory) where T : class, IMemory, new()
        {
            MemoryPool<T>.Release(memory);
        }

        /// <summary>
        /// 慢速动态路径。禁止在运行时热路径调用；请通过缓存的 MemoryPoolHandle 或 Release&lt;T&gt; 回收对象。
        /// </summary>
        [Obsolete("慢速动态路径，禁止在运行时热路径使用。请通过缓存的 MemoryPoolHandle，或改用 MemoryPool<T>.Release / MemoryPool.Release<T>。", false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release(IMemory memory)
        {
            MemoryPoolRegistry.Release(memory);
        }

        public static void Add<T>(int count) where T : class, IMemory, new()
        {
            MemoryPool<T>.Prewarm(count);
        }

        public static void Add(Type memoryType, int count)
        {
            MemoryPoolRegistry.Prewarm(memoryType, count);
        }

        public static void Remove<T>(int count) where T : class, IMemory, new()
        {
            int target = MemoryPool<T>.UnusedCount - count;
            MemoryPool<T>.Shrink(target);
        }

        public static void Remove(Type memoryType, int count)
        {
            MemoryPoolRegistry.RemoveFromType(memoryType, count);
        }

        public static void RemoveAll<T>() where T : class, IMemory, new()
        {
            MemoryPool<T>.ClearAll();
        }

        public static void SetCapacity<T>(int softCapacity, int hardCapacity) where T : class, IMemory, new()
        {
            MemoryPool<T>.SetCapacity(softCapacity, hardCapacity);
        }

        public static void Compact<T>() where T : class, IMemory, new()
        {
            MemoryPool<T>.Compact();
        }

        public static void Compact(Type memoryType)
        {
            MemoryPoolRegistry.CompactType(memoryType);
        }

        public static void CompactAll()
        {
            MemoryPoolRegistry.CompactAll();
        }

        public static void RemoveAll(Type memoryType)
        {
            MemoryPoolRegistry.ClearType(memoryType);
        }
    }
}
