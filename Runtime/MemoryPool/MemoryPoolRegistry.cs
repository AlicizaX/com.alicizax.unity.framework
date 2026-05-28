using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

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
            public delegate void CapacityHandler(int softCapacity, int hardCapacity);
            public delegate bool TickHandler(int value);
            public delegate void GetInfoHandler(ref MemoryPoolInfo info);

            public readonly int PoolId;
            public readonly Type MemoryType;
            public readonly AcquireHandler Acquire;
            public readonly ReleaseHandler Release;
            public readonly ClearHandler Clear;
            public readonly ClearHandler ClearNativeMetadata;
            public readonly IntHandler Add;
            public readonly CapacityHandler SetCapacity;
            public readonly GetInfoHandler GetInfo;
            public readonly TickHandler Tick;
            public readonly IntHandler Shrink;
            public readonly ClearHandler Compact;
            public readonly ClearHandler TrimNativeMetadata;
            public readonly ClearHandler ResetStats;
            public int ActiveIndex = -1;
            public bool ActiveQueueDebt;

            public MemoryPoolHandle(
                Type memoryType,
                AcquireHandler acquire,
                ReleaseHandler release,
                ClearHandler clear,
                ClearHandler clearNativeMetadata,
                IntHandler add,
                CapacityHandler setCapacity,
                GetInfoHandler getInfo,
                TickHandler tick,
                IntHandler shrink,
                ClearHandler compact,
                ClearHandler trimNativeMetadata,
                ClearHandler resetStats)
            {
                PoolId = ++s_NextPoolId;
                MemoryType = memoryType;
                Acquire = acquire;
                Release = release;
                Clear = clear;
                ClearNativeMetadata = clearNativeMetadata;
                Add = add;
                SetCapacity = setCapacity;
                GetInfo = getInfo;
                Tick = tick;
                Shrink = shrink;
                Compact = compact;
                TrimNativeMetadata = trimNativeMetadata;
                ResetStats = resetStats;
            }
        }

        private static IntPtr[] s_HandleKeys = new IntPtr[64];
        private static MemoryPoolHandle[] s_HandleValues = new MemoryPoolHandle[64];
        private static int s_HandleCount;

        private static MemoryPoolHandle[] s_ActivePools = Array.Empty<MemoryPoolHandle>();
        private static int s_ActiveCount;
        private static int s_NextPoolId;
        private static MemoryPoolPhase s_Phase = MemoryPoolPhase.Gameplay;
        private static bool s_HasActiveQueueDebt;
        private static int s_MainThreadId;

        public static int Count => s_HandleCount;

        internal static int CurrentFrame { get; private set; }

        public static MemoryPoolPhase Phase
        {
            get => s_Phase;
            set
            {
                AssertMainThread();
                s_Phase = value;
            }
        }

        internal static void InitializeMainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeMainThreadOnLoad()
        {
            InitializeMainThread();
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (s_MainThreadId == 0)
                s_MainThreadId = currentThreadId;
            if (s_MainThreadId != currentThreadId)
                throw new InvalidOperationException("MemoryPool must be used from the Unity main thread.");
        }

        internal static int GetGrowthBudget()
        {
            switch (s_Phase)
            {
                case MemoryPoolPhase.Boot:
                case MemoryPoolPhase.Loading:
                    return 32;
                case MemoryPoolPhase.Background:
                    return 8;
                case MemoryPoolPhase.LowMemory:
                    return 0;
                default:
                    return 2;
            }
        }

        internal static int GetEvictBudget()
        {
            switch (s_Phase)
            {
                case MemoryPoolPhase.LowMemory:
                    return 32;
                case MemoryPoolPhase.Background:
                    return 16;
                case MemoryPoolPhase.Boot:
                case MemoryPoolPhase.Loading:
                    return 4;
                default:
                    return 2;
            }
        }

        internal static void Register(Type type, MemoryPoolHandle handle)
        {
            AddOrUpdateHandle(type.TypeHandle.Value, handle);
        }

        internal static void ScheduleTick(MemoryPoolHandle handle)
        {
            if (handle == null)
                return;
            if (handle.ActiveIndex >= 0)
            {
                handle.ActiveQueueDebt = false;
                return;
            }

            if (s_ActiveCount == s_ActivePools.Length)
            {
                handle.ActiveQueueDebt = true;
                s_HasActiveQueueDebt = true;
                return;
            }

            handle.ActiveIndex = s_ActiveCount;
            handle.ActiveQueueDebt = false;
            s_ActivePools[s_ActiveCount++] = handle;
        }

        internal static void UnscheduleTick(MemoryPoolHandle handle)
        {
            if (handle == null)
                return;

            handle.ActiveQueueDebt = false;
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
            AssertMainThread();
            return new AlicizaX.MemoryPoolHandle(GetOrCreateHandle(type));
        }

        public static IMemory Acquire(Type type)
        {
            AssertMainThread();
            return GetOrCreateHandle(type).Acquire();
        }

        public static void Release(IMemory memory)
        {
            AssertMainThread();
            if (memory == null)
                return;

            if (!(memory is MemoryObject memoryObject))
                throw new InvalidOperationException("MemoryPool.Release(IMemory) only accepts MemoryObject instances.");

            MemoryPoolHandle handle = GetOwnerHandle(memoryObject);
            if (handle.PoolId == memoryObject.PoolId)
            {
                handle.Release(memory);
                return;
            }

            throw new InvalidOperationException("MemoryPool.Release(IMemory) rejected an object without a valid owner pool.");
        }

        public static int GetAllInfos(MemoryPoolInfo[] infos)
        {
            AssertMainThread();
            if (infos == null)
                throw new ArgumentNullException(nameof(infos));

            int count = s_HandleCount;
            if (infos.Length < count)
                throw new ArgumentException("Target buffer is too small.", nameof(infos));

            int i = 0;
            for (int slot = 0; slot < s_HandleValues.Length; slot++)
            {
                MemoryPoolHandle handle = s_HandleValues[slot];
                if (handle == null)
                    continue;

                handle.GetInfo(ref infos[i]);
                i++;
            }

            return count;
        }

        public static MemoryPoolInfo[] GetAllInfos()
        {
            var infos = new MemoryPoolInfo[s_HandleCount];
            GetAllInfos(infos);
            return infos;
        }

        public static void ClearAll()
        {
            AssertMainThread();
            Exception exception = null;
            for (int i = 0; i < s_HandleValues.Length; i++)
                CaptureFirstException(ref exception, s_HandleValues[i]?.Clear);

            ClearActiveScheduleState();
            Rethrow(exception);
        }

        public static void CompactAll()
        {
            AssertMainThread();
            Exception exception = null;
            for (int i = 0; i < s_HandleValues.Length; i++)
                CaptureFirstException(ref exception, s_HandleValues[i]?.Compact);
            Rethrow(exception);
        }

        public static void TrimAllNativeMetadata()
        {
            AssertMainThread();
            Exception exception = null;
            for (int i = 0; i < s_HandleValues.Length; i++)
                CaptureFirstException(ref exception, s_HandleValues[i]?.TrimNativeMetadata);
            Rethrow(exception);
        }

        public static void ResetAllStats()
        {
            AssertMainThread();
            for (int i = 0; i < s_HandleValues.Length; i++)
                s_HandleValues[i]?.ResetStats();
        }

        public static void ClearAllNativeMetadata()
        {
            AssertMainThread();
            Exception exception = null;
            for (int i = 0; i < s_HandleValues.Length; i++)
                CaptureFirstException(ref exception, s_HandleValues[i]?.ClearNativeMetadata);

            ClearActiveScheduleState();
            Rethrow(exception);
        }

        private static void ClearActiveScheduleState()
        {
            for (int i = 0; i < s_HandleValues.Length; i++)
            {
                MemoryPoolHandle handle = s_HandleValues[i];
                if (handle == null)
                    continue;

                handle.ActiveIndex = -1;
                handle.ActiveQueueDebt = false;
            }

            for (int i = 0; i < s_ActiveCount; i++)
            {
                MemoryPoolHandle handle = s_ActivePools[i];
                if (handle != null)
                    handle.ActiveIndex = -1;
            }

            Array.Clear(s_ActivePools, 0, s_ActiveCount);
            s_ActiveCount = 0;
            s_HasActiveQueueDebt = false;
        }

        public static void Add(Type type, int count)
        {
            AssertMainThread();
            GetOrCreateHandle(type).Add(count);
        }

        public static void SetCapacity(Type type, int softCapacity, int hardCapacity)
        {
            AssertMainThread();
            GetOrCreateHandle(type).SetCapacity(softCapacity, hardCapacity);
        }

        public static void SetCapacityAll(int softCapacity, int hardCapacity)
        {
            AssertMainThread();
            for (int i = 0; i < s_HandleValues.Length; i++)
                s_HandleValues[i]?.SetCapacity(softCapacity, hardCapacity);
        }

        public static void ClearType(Type type)
        {
            AssertMainThread();
            GetOrCreateHandle(type).Clear();
        }

        public static void CompactType(Type type)
        {
            AssertMainThread();
            GetOrCreateHandle(type).Compact();
        }

        public static void TrimNativeMetadata(Type type)
        {
            AssertMainThread();
            GetOrCreateHandle(type).TrimNativeMetadata();
        }

        public static void RemoveFromType(Type type, int count)
        {
            AssertMainThread();
            MemoryPoolHandle handle = GetOrCreateHandle(type);
            MemoryPoolInfo info = default;
            handle.GetInfo(ref info);
            handle.Shrink(info.UnusedCount - count);
        }

        public static void TickAll(int frameCount)
        {
            AssertMainThread();
            CurrentFrame = frameCount;
            ProcessActiveQueueDebt();
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
            ProcessActiveQueueDebt();
        }

        private static void ProcessActiveQueueDebt()
        {
            if (!s_HasActiveQueueDebt)
                return;

            EnsureActiveCapacity(s_ActiveCount + CountDebtPools());
            s_HasActiveQueueDebt = false;
            for (int i = 0; i < s_HandleValues.Length; i++)
            {
                MemoryPoolHandle handle = s_HandleValues[i];
                if (handle == null)
                    continue;
                if (!handle.ActiveQueueDebt || handle.ActiveIndex >= 0)
                    continue;

                handle.ActiveQueueDebt = false;
                ScheduleTick(handle);
                if (handle.ActiveQueueDebt)
                    s_HasActiveQueueDebt = true;
            }
        }

        private static int CountDebtPools()
        {
            int count = 0;
            for (int i = 0; i < s_HandleValues.Length; i++)
            {
                MemoryPoolHandle handle = s_HandleValues[i];
                if (handle != null && handle.ActiveQueueDebt && handle.ActiveIndex < 0)
                    count++;
            }

            return count;
        }

        private static void EnsureActiveCapacity(int required)
        {
            if (s_ActivePools.Length >= required)
                return;

            int newLength = s_ActivePools.Length == 0 ? 16 : s_ActivePools.Length;
            while (newLength < required)
                newLength <<= 1;

            var activePools = new MemoryPoolHandle[newLength];
            Array.Copy(s_ActivePools, 0, activePools, 0, s_ActiveCount);
            s_ActivePools = activePools;
        }

        private static bool TryGetHandle(IntPtr key, out MemoryPoolHandle handle)
        {
            int index = FindHandleSlot(key, out bool found);
            if (found)
            {
                handle = s_HandleValues[index];
                return true;
            }

            handle = null;
            return false;
        }

        private static void AddOrUpdateHandle(IntPtr key, MemoryPoolHandle handle)
        {
            if ((s_HandleCount + 1) * 4 >= s_HandleKeys.Length * 3)
                GrowHandleCache();

            int index = FindHandleSlot(key, out bool found);
            if (!found)
                s_HandleCount++;

            s_HandleKeys[index] = key;
            s_HandleValues[index] = handle;
        }

        private static int FindHandleSlot(IntPtr key, out bool found)
        {
            int mask = s_HandleKeys.Length - 1;
            int index = Mix((ulong)key.ToInt64()) & mask;
            while (true)
            {
                IntPtr existing = s_HandleKeys[index];
                if (existing == IntPtr.Zero)
                {
                    found = false;
                    return index;
                }

                if (existing == key)
                {
                    found = true;
                    return index;
                }

                index = (index + 1) & mask;
            }
        }

        private static void GrowHandleCache()
        {
            IntPtr[] oldKeys = s_HandleKeys;
            MemoryPoolHandle[] oldValues = s_HandleValues;
            s_HandleKeys = new IntPtr[oldKeys.Length << 1];
            s_HandleValues = new MemoryPoolHandle[oldValues.Length << 1];
            int oldCount = s_HandleCount;
            s_HandleCount = 0;
            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != IntPtr.Zero)
                    AddOrUpdateHandle(oldKeys[i], oldValues[i]);
            }

            s_HandleCount = oldCount;
        }

        private static int Mix(ulong value)
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            value *= 0xc4ceb9fe1a85ec53UL;
            value ^= value >> 33;
            return (int)value;
        }

        private static MemoryPoolHandle GetOrCreateHandle(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            RuntimeTypeHandle typeHandle = type.TypeHandle;
            if (TryGetHandle(typeHandle.Value, out MemoryPoolHandle handle))
                return handle;

            ValidateMemoryObjectType(type);
            RuntimeHelpers.RunClassConstructor(
                typeof(MemoryPool<>).MakeGenericType(type).TypeHandle);

            if (TryGetHandle(typeHandle.Value, out handle))
                return handle;

            throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' could not be materialized.");
        }

        private static MemoryPoolHandle GetOwnerHandle(MemoryObject memory)
        {
            MemoryPoolHandle handle = memory.OwnerHandle.IsValid ? memory.OwnerHandle.Inner : null;
            if (handle == null)
                throw new InvalidOperationException("Memory object has no owner pool.");
            if (handle.PoolId != memory.PoolId)
                throw new InvalidOperationException("Memory object owner pool mismatch.");
            return handle;
        }

        private static void ValidateMemoryObjectType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (!type.IsClass)
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must be a class.");
            if (type.IsAbstract)
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must not be abstract.");
            if (type.ContainsGenericParameters)
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must not be an open generic type.");
            if (!typeof(MemoryObject).IsAssignableFrom(type))
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must inherit MemoryObject.");
            if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) == null)
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must have a public parameterless constructor.");
        }

        private static void CaptureFirstException(ref Exception first, MemoryPoolHandle.ClearHandler action)
        {
            if (action == null)
                return;

            try
            {
                action();
            }
            catch (Exception exception)
            {
                if (first == null)
                    first = exception;
            }
        }

        private static void Rethrow(Exception exception)
        {
            if (exception != null)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
