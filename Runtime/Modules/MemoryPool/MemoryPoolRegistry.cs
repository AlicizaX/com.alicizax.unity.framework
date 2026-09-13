using System;
using System.Collections.Generic;
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
            public delegate MemoryObject AcquireHandler();
            public delegate void ReleaseHandler(MemoryObject memory);
            public delegate void ClearHandler();
            public delegate void IntHandler(int value);
            public delegate void CapacityHandler(int softCapacity, int hardCapacity);
            public delegate bool TickHandler(int value);
            public delegate void GetInfoHandler(ref MemoryPoolInfo info);

            public readonly int PoolId;
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

            public MemoryPoolHandle(
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

        private static MemoryPoolHandle[] s_ActivePools = new MemoryPoolHandle[16];
        private static Action[] s_NativeReleasers = Array.Empty<Action>();
        private static int s_NativeReleaserCount;
        private static int s_ActiveCount;
        private static int s_NextPoolId;
        private static MemoryPoolPhase s_Phase = MemoryPoolPhase.Gameplay;
        private static int s_MainThreadId;
        private static int s_CallbackDepth;

        static MemoryPoolRegistry()
        {
            AppDomain.CurrentDomain.DomainUnload += ReleaseNativeOnDomainUnload;
        }

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

        private static void ReleaseNativeOnDomainUnload(object sender, EventArgs e)
        {
            ForceReleaseAllNativeMetadata();
        }

        internal static void RegisterNativeReleaser(Action releaser)
        {
            if (s_NativeReleaserCount == s_NativeReleasers.Length)
            {
                int newLength = s_NativeReleasers.Length == 0 ? 16 : s_NativeReleasers.Length << 1;
                var releasers = new Action[newLength];
                Array.Copy(s_NativeReleasers, 0, releasers, 0, s_NativeReleaserCount);
                s_NativeReleasers = releasers;
            }

            s_NativeReleasers[s_NativeReleaserCount++] = releaser;
        }

        private static void ForceReleaseAllNativeMetadata()
        {
            for (int i = 0; i < s_NativeReleaserCount; i++)
                s_NativeReleasers[i]();
            ClearActiveScheduleState();
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
            ReserveActiveCapacity(s_HandleCount);
        }

        internal static void ScheduleTick(MemoryPoolHandle handle)
        {
            if (handle.ActiveIndex >= 0)
                return;
            handle.ActiveIndex = s_ActiveCount;
            s_ActivePools[s_ActiveCount++] = handle;
        }

        internal static void UnscheduleTick(MemoryPoolHandle handle)
        {
            int index = handle.ActiveIndex;
            if (index < 0)
                return;
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

        public static MemoryObject Acquire(Type type)
        {
            AssertMainThread();
            return GetOrCreateHandle(type).Acquire();
        }

        public static void Release(MemoryObject memory)
        {
            AssertMainThread();
            if (memory == null)
                return;
            MemoryPoolHandle handle = memory.OwnerHandle.Inner;
            if (handle == null)
                throw new InvalidOperationException("Memory object has no owner pool.");
            handle.Release(memory);
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

        public static void ClearAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
                CollectException(ref exceptions, handles[i]?.Clear);
            Rethrow(exceptions);
        }

        public static void CompactAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
                CollectException(ref exceptions, handles[i]?.Compact);
            Rethrow(exceptions);
        }

        public static void TrimAllNativeMetadata()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
                CollectException(ref exceptions, handles[i]?.TrimNativeMetadata);
            Rethrow(exceptions);
        }

        public static void ResetAllStats()
        {
            AssertMainThread();
            ThrowIfInCallback();
            for (int i = 0; i < s_HandleValues.Length; i++)
                s_HandleValues[i]?.ResetStats();
        }

        public static void ClearAllNativeMetadata()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
                CollectException(ref exceptions, handles[i]?.ClearNativeMetadata);
            Rethrow(exceptions);
        }

        private static void ClearActiveScheduleState()
        {
            for (int i = 0; i < s_ActiveCount; i++)
                s_ActivePools[i].ActiveIndex = -1;
            Array.Clear(s_ActivePools, 0, s_ActiveCount);
            s_ActiveCount = 0;
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
            ThrowIfInCallback();
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
            if (count <= 0)
                return;
            MemoryPoolHandle handle = GetOrCreateHandle(type);
            MemoryPoolInfo info = default;
            handle.GetInfo(ref info);
            handle.Shrink(info.UnusedCount - count);
        }

        public static void TickAll(int frameCount)
        {
            AssertMainThread();
            ThrowIfInCallback();
            CurrentFrame = frameCount;
            List<Exception> exceptions = null;
            int i = 0;
            while (i < s_ActiveCount)
            {
                MemoryPoolHandle handle = s_ActivePools[i];
                try
                {
                    if (!handle.Tick(frameCount))
                        UnscheduleTick(handle);
                }
                catch (Exception exception)
                {
                    (exceptions ??= new List<Exception>()).Add(exception);
                }
                if (i < s_ActiveCount && ReferenceEquals(s_ActivePools[i], handle))
                    i++;
            }
            Rethrow(exceptions);
        }

        private static void ReserveActiveCapacity(int required)
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

        private static void ValidateMemoryObjectType(Type type)
        {
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

        internal static void BeginCallback()
        {
            s_CallbackDepth++;
        }

        internal static void EndCallback()
        {
            s_CallbackDepth--;
        }

        private static void ThrowIfInCallback()
        {
            if (s_CallbackDepth != 0)
                throw new InvalidOperationException("Global memory pool maintenance is not allowed during a pool callback.");
        }

        private static void CollectException(ref List<Exception> exceptions, MemoryPoolHandle.ClearHandler action)
        {
            if (action == null)
                return;

            try
            {
                action();
            }
            catch (Exception exception)
            {
                (exceptions ??= new List<Exception>()).Add(exception);
            }
        }

        private static void Rethrow(List<Exception> exceptions)
        {
            if (exceptions != null)
                ExceptionDispatchInfo.Capture(exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions)).Throw();
        }
    }
}
