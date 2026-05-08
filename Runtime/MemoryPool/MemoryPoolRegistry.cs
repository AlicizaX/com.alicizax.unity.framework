using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static class MemoryPoolRegistry
    {
        internal sealed class MemoryPoolHandle
        {
            public delegate IMemory AcquireHandler();
            public delegate void ReleaseHandler(IMemory memory);
            public delegate void ClearHandler();
            public delegate void IntHandler(int value);
            public delegate bool TickHandler(int value);
            public delegate void GetInfoHandler(ref MemoryPoolInfo info);

            public readonly AcquireHandler Acquire;
            public readonly ReleaseHandler Release;
            public readonly ClearHandler Clear;
            public readonly IntHandler Prewarm;
            public readonly GetInfoHandler GetInfo;
            public readonly TickHandler Tick;
            public readonly IntHandler Shrink;
            public readonly ClearHandler Compact;
            public int ActiveIndex = -1;

            public MemoryPoolHandle(
                AcquireHandler acquire,
                ReleaseHandler release,
                ClearHandler clear,
                IntHandler prewarm,
                GetInfoHandler getInfo,
                TickHandler tick,
                IntHandler shrink,
                ClearHandler compact)
            {
                Acquire = acquire;
                Release = release;
                Clear = clear;
                Prewarm = prewarm;
                GetInfo = getInfo;
                Tick = tick;
                Shrink = shrink;
                Compact = compact;
            }
        }

        private static readonly Dictionary<Type, MemoryPoolHandle> s_Handles
            = new Dictionary<Type, MemoryPoolHandle>(64);

        private static MemoryPoolHandle[] s_ActivePools = Array.Empty<MemoryPoolHandle>();
        private static int s_ActiveCount;

        public static int Count => s_Handles.Count;

        internal static void Register(Type type, MemoryPoolHandle handle)
        {
            s_Handles[type] = handle;
        }

        internal static void ScheduleTick(MemoryPoolHandle handle)
        {
            if (handle == null || handle.ActiveIndex >= 0)
                return;

            if (s_ActiveCount == s_ActivePools.Length)
            {
                int newLength = s_ActivePools.Length == 0 ? 16 : s_ActivePools.Length << 1;
                var activePools = new MemoryPoolHandle[newLength];
                Array.Copy(s_ActivePools, 0, activePools, 0, s_ActiveCount);
                s_ActivePools = activePools;
            }

            handle.ActiveIndex = s_ActiveCount;
            s_ActivePools[s_ActiveCount++] = handle;
        }


        internal static void UnscheduleTick(MemoryPoolHandle handle)
        {
            if (handle == null)
                return;

            int index = handle.ActiveIndex;
            if (index < 0 || index >= s_ActiveCount)
            {
                handle.ActiveIndex = -1;
                return;
            }

            int lastIndex = --s_ActiveCount;
            MemoryPoolHandle last = s_ActivePools[lastIndex];
            s_ActivePools[lastIndex] = null;
            handle.ActiveIndex = -1;

            if (index != lastIndex)
            {
                s_ActivePools[index] = last;
                last.ActiveIndex = index;
            }
        }


        public static AlicizaX.MemoryPoolHandle GetHandle(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (s_Handles.TryGetValue(type, out var handle))
                return new AlicizaX.MemoryPoolHandle(handle);

            EnsureRegistered(type);

            if (s_Handles.TryGetValue(type, out handle))
                return new AlicizaX.MemoryPoolHandle(handle);

            throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
        }

        public static IMemory Acquire(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (s_Handles.TryGetValue(type, out var handle))
                return handle.Acquire();

            EnsureRegistered(type);

            if (s_Handles.TryGetValue(type, out handle))
                return handle.Acquire();

            throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
        }

        public static void Release(IMemory memory)
        {
            if (memory == null)
                throw new ArgumentNullException(nameof(memory));

            Type type = memory.GetType();
            if (s_Handles.TryGetValue(type, out var handle))
            {
                handle.Release(memory);
                return;
            }

            EnsureRegistered(type);

            if (s_Handles.TryGetValue(type, out handle))
            {
                handle.Release(memory);
                return;
            }

            throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
        }

        public static int GetAllInfos(MemoryPoolInfo[] infos)
        {
            if (infos == null)
                throw new ArgumentNullException(nameof(infos));

            int count = s_Handles.Count;
            if (infos.Length < count)
                throw new ArgumentException("Target buffer is too small.", nameof(infos));

            int i = 0;
            foreach (var kv in s_Handles)
            {
                kv.Value.GetInfo(ref infos[i]);
                i++;
            }

            return count;
        }

        public static MemoryPoolInfo[] GetAllInfos()
        {
            var infos = new MemoryPoolInfo[s_Handles.Count];
            GetAllInfos(infos);
            return infos;
        }

        public static void ClearAll()
        {
            foreach (var kv in s_Handles)
                kv.Value.Clear();

            for (int i = 0; i < s_ActiveCount; i++)
                s_ActivePools[i].ActiveIndex = -1;

            Array.Clear(s_ActivePools, 0, s_ActiveCount);
            s_ActiveCount = 0;
        }

        public static void CompactAll()
        {
            foreach (var kv in s_Handles)
                kv.Value.Compact();
        }

        public static void Prewarm(Type type, int count)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (s_Handles.TryGetValue(type, out var handle))
            {
                handle.Prewarm(count);
                return;
            }

            EnsureRegistered(type);

            if (s_Handles.TryGetValue(type, out handle))
            {
                handle.Prewarm(count);
                return;
            }

            throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
        }

        public static void ClearType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!s_Handles.TryGetValue(type, out var handle))
            {
                EnsureRegistered(type);
                if (!s_Handles.TryGetValue(type, out handle))
                    throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
            }

            handle.Clear();
        }

        public static void CompactType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!s_Handles.TryGetValue(type, out var handle))
            {
                EnsureRegistered(type);
                if (!s_Handles.TryGetValue(type, out handle))
                    throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
            }

            handle.Compact();
        }

        public static void RemoveFromType(Type type, int count)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (s_Handles.TryGetValue(type, out var handle))
            {
                MemoryPoolInfo info = default;
                handle.GetInfo(ref info);
                int unused = info.UnusedCount;
                handle.Shrink(unused - count);
                return;
            }

            EnsureRegistered(type);
            if (s_Handles.TryGetValue(type, out handle))
            {
                MemoryPoolInfo info = default;
                handle.GetInfo(ref info);
                int unused = info.UnusedCount;
                handle.Shrink(unused - count);
                return;
            }

            throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");
        }

        public static void TickAll(int frameCount)
        {
            int i = 0;
            while (i < s_ActiveCount)
            {
                MemoryPoolHandle handle = s_ActivePools[i];
                if (handle.Tick(frameCount))
                {
                    i++;
                    continue;
                }

                int lastIndex = --s_ActiveCount;
                MemoryPoolHandle last = s_ActivePools[lastIndex];
                s_ActivePools[lastIndex] = null;
                handle.ActiveIndex = -1;

                if (i != lastIndex)
                {
                    s_ActivePools[i] = last;
                    last.ActiveIndex = i;
                }
            }
        }

        private static void EnsureRegistered(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(IMemory).IsAssignableFrom(type))
                throw new Exception($"MemoryPool: Type '{type.FullName}' is not a valid IMemory type.");

            RuntimeHelpers.RunClassConstructor(
                typeof(MemoryPool<>).MakeGenericType(type).TypeHandle);
        }
    }
}
