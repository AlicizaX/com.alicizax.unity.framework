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
            public readonly ClearHandler ForceReleaseNativeMetadata;
            public int ActiveIndex = -1;
            public Type Type;
            public bool Registered;

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
                ClearHandler resetStats,
                ClearHandler forceReleaseNativeMetadata)
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
                ForceReleaseNativeMetadata = forceReleaseNativeMetadata;
            }
        }

        // 以 Type 为键，而不是 RuntimeTypeHandle.Value：句柄是真实引用，因此热重载/卸载后，
        // 某个类型句柄地址不可能被另一个类型复用。
        private static readonly Dictionary<Type, MemoryPoolHandle> s_Handles = new Dictionary<Type, MemoryPoolHandle>(64);
        private static readonly List<MemoryPoolHandle> s_Registered = new List<MemoryPoolHandle>(64);

        private static MemoryPoolHandle[] s_ActivePools = new MemoryPoolHandle[16];
        private static int s_ActiveCount;
        private static int s_NextPoolId;
        private static int s_MainThreadId;
        private static int s_CallbackDepth;

        static MemoryPoolRegistry()
        {
            AppDomain.CurrentDomain.DomainUnload += ReleaseNativeOnDomainUnload;
        }

        public static int Count => s_Registered.Count;

        internal static int CurrentFrame { get; private set; }

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

        private static void ForceReleaseAllNativeMetadata()
        {
            for (int i = 0; i < s_Registered.Count; i++)
                s_Registered[i].ForceReleaseNativeMetadata();
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

        internal static void Register(Type type, MemoryPoolHandle handle)
        {
            handle.Type = type;
            handle.Registered = true;
            s_Handles[type] = handle;
            if (!s_Registered.Contains(handle))
                s_Registered.Add(handle);
        }

        /// <summary>
        /// 释放对象已全部归还的池的存储。类型仍保留其句柄（之后 Acquire 可重新激活），
        /// 但在注销期间不计入维护和统计。热更新程序集/运行时类型卸载前应调用此方法。
        /// </summary>
        public static bool Unregister(Type type)
        {
            AssertMainThread();
            ThrowIfInCallback();
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!s_Handles.TryGetValue(type, out MemoryPoolHandle handle) || !handle.Registered)
                return false;

            MemoryPoolInfo info = default;
            handle.GetInfo(ref info);
            if (info.UsingCount != 0)
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' still has {info.UsingCount} leased objects.");

            try
            {
                handle.Clear();
            }
            finally
            {
                handle.TrimNativeMetadata();
                handle.Registered = false;
                UnscheduleTick(handle);
                s_Registered.Remove(handle);
            }

            return true;
        }

        internal static void ScheduleTick(MemoryPoolHandle handle)
        {
            if (!handle.Registered)
                Register(handle.Type, handle);
            if (handle.ActiveIndex >= 0)
                return;

            if (s_ActiveCount >= s_ActivePools.Length)
            {
                var pools = new MemoryPoolHandle[s_ActivePools.Length == 0 ? 16 : s_ActivePools.Length << 1];
                Array.Copy(s_ActivePools, 0, pools, 0, s_ActiveCount);
                s_ActivePools = pools;
            }

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

            int count = s_Registered.Count;
            if (infos.Length < count)
                throw new ArgumentException("Target buffer is too small.", nameof(infos));

            for (int i = 0; i < count; i++)
                s_Registered[i].GetInfo(ref infos[i]);

            return count;
        }

        public static void ClearAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            for (int i = 0; i < s_Registered.Count; i++)
                CollectException(ref exceptions, s_Registered[i].Clear);
            Rethrow(exceptions);
        }

        public static void CompactAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            for (int i = 0; i < s_Registered.Count; i++)
                CollectException(ref exceptions, s_Registered[i].Compact);
            Rethrow(exceptions);
        }

        public static void TrimAllNativeMetadata()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            for (int i = 0; i < s_Registered.Count; i++)
                CollectException(ref exceptions, s_Registered[i].TrimNativeMetadata);
            Rethrow(exceptions);
        }

        public static void ResetAllStats()
        {
            AssertMainThread();
            ThrowIfInCallback();
            for (int i = 0; i < s_Registered.Count; i++)
                s_Registered[i].ResetStats();
        }

        public static void ClearAllNativeMetadata()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            for (int i = 0; i < s_Registered.Count; i++)
                CollectException(ref exceptions, s_Registered[i].ClearNativeMetadata);
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
            for (int i = 0; i < s_Registered.Count; i++)
                s_Registered[i].SetCapacity(softCapacity, hardCapacity);
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

        private static MemoryPoolHandle GetOrCreateHandle(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (s_Handles.TryGetValue(type, out MemoryPoolHandle handle))
            {
                if (!handle.Registered)
                    Register(type, handle);
                return handle;
            }

            ValidateMemoryObjectType(type);
            RuntimeHelpers.RunClassConstructor(typeof(MemoryPool<>).MakeGenericType(type).TypeHandle);

            if (s_Handles.TryGetValue(type, out handle))
            {
                if (!handle.Registered)
                    Register(type, handle);
                return handle;
            }

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
