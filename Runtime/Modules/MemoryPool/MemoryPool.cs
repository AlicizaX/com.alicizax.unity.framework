using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static partial class MemoryPool
    {
        public const int MinimumFreeReserveLimit = 4;

        /// <summary>
        /// 池空闲多少帧后开始缓慢缩容（每 tick 释放 4 个）。默认 1800 帧（@60fps ≈ 30秒）。
        /// </summary>
        public static int ShortDecayStartFrames = 1800;

        /// <summary>
        /// 池空闲多少帧后加速缩容（每 tick 释放 16 个）。默认 7200 帧（@60fps ≈ 2分钟）。
        /// </summary>
        public static int LongDecayStartFrames = 7200;

        /// <summary>
        /// 池空闲多少帧后停止调度 Tick（省 CPU）。默认 18000 帧（@60fps ≈ 5分钟）。
        /// </summary>
        public static int UnscheduleIdleFrames = 18000;

        public static int ZeroFreeReserveStartFrames = 7200;

        public static int AutoTrimNativeMetadataFrames = 18000;

        public static int DefaultSoftFreeReserveLimit = 128;

        public static int DefaultHardFreeReserveLimit = 512;


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
        public static T Acquire<T>() where T : MemoryObject, new()
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryObject Acquire(Type memoryType)
        {
            return MemoryPoolRegistry.Acquire(memoryType);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release<T>(T memory) where T : MemoryObject, new()
        {
            MemoryPool<T>.Release(memory);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release(MemoryObject memory)
        {
            MemoryPoolRegistry.Release(memory);
        }

        public static void Add<T>(int count) where T : MemoryObject, new()
        {
            MemoryPool<T>.Add(count);
        }

        public static void Add(Type memoryType, int count)
        {
            MemoryPoolRegistry.Add(memoryType, count);
        }

        public static void SetCapacity(Type memoryType, int softCapacity, int hardCapacity)
        {
            MemoryPoolRegistry.SetCapacity(memoryType, softCapacity, hardCapacity);
        }

        public static void SetDefaultCapacity(int softCapacity, int hardCapacity)
        {
            softCapacity = Math.Max(softCapacity, MinimumFreeReserveLimit);
            hardCapacity = Math.Max(hardCapacity, softCapacity);
            DefaultSoftFreeReserveLimit = softCapacity;
            DefaultHardFreeReserveLimit = hardCapacity;
            MemoryPoolRegistry.SetCapacityAll(softCapacity, hardCapacity);
        }

        public static void Remove<T>(int count) where T : MemoryObject, new()
        {
            int target = MemoryPool<T>.UnusedCount - count;
            MemoryPool<T>.Shrink(target);
        }

        public static void Remove(Type memoryType, int count)
        {
            MemoryPoolRegistry.RemoveFromType(memoryType, count);
        }

        public static void RemoveAll<T>() where T : MemoryObject, new()
        {
            MemoryPool<T>.ClearAll();
        }

        public static void SetCapacity<T>(int softCapacity, int hardCapacity) where T : MemoryObject, new()
        {
            MemoryPool<T>.SetCapacity(softCapacity, hardCapacity);
        }

        public static void Compact<T>() where T : MemoryObject, new()
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

        public static void TrimNativeMetadata<T>() where T : MemoryObject, new()
        {
            MemoryPool<T>.TrimNativeMetadata();
        }

        public static void TrimNativeMetadata(Type memoryType)
        {
            MemoryPoolRegistry.TrimNativeMetadata(memoryType);
        }

        public static void TrimAllNativeMetadata()
        {
            MemoryPoolRegistry.TrimAllNativeMetadata();
        }

        public static void ResetAllStats()
        {
            MemoryPoolRegistry.ResetAllStats();
        }

        public static void RemoveAll(Type memoryType)
        {
            MemoryPoolRegistry.ClearType(memoryType);
        }
    }
}
