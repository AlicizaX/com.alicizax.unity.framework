using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace AlicizaX.Timer.Runtime
{
    public delegate void TimerHandlerNoArgs();

    internal delegate void TimerGenericInvoker(object handler, object arg);

    internal static class TimerGenericInvokerCache<T> where T : class
    {
        public static readonly TimerGenericInvoker Invoke = InvokeGeneric;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InvokeGeneric(object handler, object arg)
        {
            ((Action<T>)handler).Invoke((T)arg);
        }
    }

    internal static class TimerStructInvokerCache<TCallback, TArg>
        where TCallback : struct, ITimerCallback<TArg>
        where TArg : class
    {
        public static readonly TimerGenericInvoker Invoke = InvokeCallback;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InvokeCallback(object handler, object arg)
        {
            default(TCallback).Invoke((TArg)arg);
        }
    }

    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class TimerService : ServiceBase, ITimerService, ITimerDebugService, IServiceTickable
#if UNITY_EDITOR
        , ITimerEditorDebugService
#endif
    {
        private const int PAGE_SHIFT = 8;
        private const int PAGE_SIZE = 1 << PAGE_SHIFT;
        private const int PAGE_MASK = PAGE_SIZE - 1;
        private const int MAX_PAGE_COUNT = 4096;
        private const int INVALID_INDEX = -1;
        private const int HANDLE_INDEX_BITS = 21;
        private const ulong HANDLE_INDEX_MASK = (1UL << HANDLE_INDEX_BITS) - 1UL;
        private const int WHEEL_SHIFT = 8;
        private const int WHEEL_SIZE = 1 << WHEEL_SHIFT;
        private const int WHEEL_MASK = WHEEL_SIZE - 1;
        private const int WHEEL_LEVEL_COUNT = 4;
        private const int WHEEL_MAX_LEVEL = WHEEL_LEVEL_COUNT - 1;
        private const int WHEEL_BUCKET_COUNT = WHEEL_SIZE * WHEEL_LEVEL_COUNT;
        private const int MAX_TIMERS_PER_FRAME = 256;
        private const int MAX_CATCHUP_TICKS = 4096;
        private const int INITIAL_PAGE_DIRECTORY_CAPACITY = 4;
        private const double TICKS_PER_SECOND = 1000d;
        private const double MINIMUM_DELAY_SECONDS = 0.000001d;
#if UNITY_EDITOR
        private const double STALE_ONE_SHOT_SECONDS = 300d;
#endif

        private const byte STATE_ACTIVE = 1 << 0;
        private const byte STATE_RUNNING = 1 << 1;
        private const byte STATE_LOOP = 1 << 2;
        private const byte STATE_UNSCALED = 1 << 3;
        private const byte STATE_RELEASE_PENDING = 1 << 4;

        private sealed class TimerPage
        {
            public readonly ulong[] Handles = new ulong[PAGE_SIZE];
            public readonly byte[] States = new byte[PAGE_SIZE];
            public readonly double[] TriggerTimes = new double[PAGE_SIZE];
            public readonly double[] Durations = new double[PAGE_SIZE];
            public readonly double[] RemainingTimes = new double[PAGE_SIZE];
#if UNITY_EDITOR
            public readonly double[] CreationTimes = new double[PAGE_SIZE];
#endif
            public readonly long[] DueTicks = new long[PAGE_SIZE];
            public readonly int[] QueueIndices = new int[PAGE_SIZE];
            public readonly int[] NextIndices = new int[PAGE_SIZE];
            public readonly int[] QueuePrevIndices = new int[PAGE_SIZE];
            public readonly TimerGenericInvoker[] Invokers = new TimerGenericInvoker[PAGE_SIZE];
            public readonly object[] Handlers = new object[PAGE_SIZE];
            public readonly object[] Args = new object[PAGE_SIZE];

            public TimerPage()
            {
                for (int i = 0; i < PAGE_SIZE; i++)
                {
                    QueueIndices[i] = INVALID_INDEX;
                }
            }
        }

        private sealed class TimerWheel
        {
            public readonly int[] Heads = new int[WHEEL_BUCKET_COUNT];
            public readonly int[] Tails = new int[WHEEL_BUCKET_COUNT];
            public int Count;
            public long CursorTick;

            public TimerWheel(long cursorTick)
            {
                for (int i = 0; i < WHEEL_BUCKET_COUNT; i++)
                {
                    Heads[i] = INVALID_INDEX;
                    Tails[i] = INVALID_INDEX;
                }

                CursorTick = cursorTick;
            }
        }

        private static ulong s_handleGeneration;

        private readonly TimerWheel _scaledWheel;
        private readonly TimerWheel _unscaledWheel;
        private TimerPage[] _pages;
        private int[] _pageActiveCounts;
        private int _pageCount;
        private int _slotCapacity;
        private int _freeHead;
        private int _activeCount;
        private int _peakActiveCount;
        private int _executingSlotIndex;
        private bool _executingSlotRescheduled;
        private bool _isDestroyed;

        public TimerService()
        {
            _pages = new TimerPage[INITIAL_PAGE_DIRECTORY_CAPACITY];
            _pageActiveCounts = new int[INITIAL_PAGE_DIRECTORY_CAPACITY];
            _scaledWheel = new TimerWheel(TimeToTickFloor(Time.timeAsDouble));
            _unscaledWheel = new TimerWheel(TimeToTickFloor(Time.unscaledTimeAsDouble));
            _freeHead = INVALID_INDEX;
            _executingSlotIndex = INVALID_INDEX;
            AddPage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AddTimer(TimerHandlerNoArgs callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            if (callback == null)
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("callback is null.");
#endif
                return 0UL;
            }

            return ScheduleTimer(null, callback, null, time, isLoop, isUnscaled);
        }

        /// <summary>
        /// 热路径调用方必须使用缓存委托或静态方法组，禁止传入捕获 lambda 或闭包，
        /// 因为闭包对象会产生分配，破坏 0GC 约束。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class
        {
            if (callback == null)
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("callback is null.");
#endif
                return 0UL;
            }

            return ScheduleTimer(TimerGenericInvokerCache<T>.Invoke, callback, arg, time, isLoop, isUnscaled);
        }

        /// <summary>
        /// 零分配回调路径。委托按 (TCallback, TArg) 缓存一次；调用方传入结构体类型而非
        /// 方法组，因此每次调用都不会分配委托。
        /// </summary>
        public ulong AddTimer<TCallback, TArg>(TArg arg, float time, bool isLoop = false, bool isUnscaled = false)
            where TCallback : struct, ITimerCallback<TArg>
            where TArg : class
        {
            return ScheduleTimer(TimerStructInvokerCache<TCallback, TArg>.Invoke, null, arg, time, isLoop, isUnscaled);
        }

#if UNITY_EDITOR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WarnAddTimerFailed(string reason)
        {
            Log.Warning("[Timer] AddTimer failed: {0}", reason);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Stop(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0)
            {
                return;
            }

            byte state = GetState(slotIndex);
            if ((state & STATE_RUNNING) == 0)
            {
                return;
            }

            bool isUnscaled = (state & STATE_UNSCALED) != 0;
            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, GetWheel(isUnscaled));
                double leftTime = GetTriggerTime(slotIndex) - GetCurrentTime(isUnscaled);
                SetRemainingTime(slotIndex, leftTime > MINIMUM_DELAY_SECONDS ? leftTime : MINIMUM_DELAY_SECONDS);
            }
            else
            {
                SetRemainingTime(slotIndex, (state & STATE_LOOP) != 0 ? GetDuration(slotIndex) : MINIMUM_DELAY_SECONDS);
            }

            SetState(slotIndex, (byte)(state & ~STATE_RUNNING));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resume(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0)
            {
                return;
            }

            byte state = GetState(slotIndex);
            if ((state & STATE_RUNNING) != 0)
            {
                return;
            }

            bool isUnscaled = (state & STATE_UNSCALED) != 0;
            SetTriggerTime(slotIndex, GetCurrentTime(isUnscaled) + GetRemainingTime(slotIndex));
            SetState(slotIndex, (byte)(state | STATE_RUNNING));
            AddToQueue(slotIndex, GetWheel(isUnscaled));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsRunning(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            return slotIndex >= 0 && (GetState(slotIndex) & STATE_RUNNING) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetLeftTime(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0)
            {
                return 0f;
            }

            byte state = GetState(slotIndex);
            double leftTime = (state & STATE_RUNNING) == 0
                ? GetRemainingTime(slotIndex)
                : GetTriggerTime(slotIndex) - GetCurrentTime((state & STATE_UNSCALED) != 0);
            return leftTime > 0d ? (float)leftTime : 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Restart(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0)
            {
                return;
            }

            byte state = GetState(slotIndex);
            bool isUnscaled = (state & STATE_UNSCALED) != 0;
            TimerWheel wheel = GetWheel(isUnscaled);
            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, wheel);
            }

            SetTriggerTime(slotIndex, GetCurrentTime(isUnscaled) + GetDuration(slotIndex));
            SetState(slotIndex, (byte)(state | STATE_RUNNING));
            AddToQueue(slotIndex, wheel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveTimer(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex >= 0)
            {
                ReleaseSlot(slotIndex);
            }
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            if (_executingSlotIndex >= 0)
            {
                return;
            }

            AdvanceQueue(_scaledWheel, Time.timeAsDouble);
            AdvanceQueue(_unscaledWheel, Time.unscaledTimeAsDouble);
        }

        protected override void OnDispose()
        {
            _isDestroyed = true;
            ClearAll();
        }

        void ITimerDebugService.GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            activeCount = _activeCount;
            poolCapacity = _slotCapacity;
            peakActiveCount = _peakActiveCount;
            bool executingSlotPending = _executingSlotIndex >= 0 && (GetState(_executingSlotIndex) & STATE_RELEASE_PENDING) != 0;
            freeCount = _slotCapacity - _activeCount - (executingSlotPending ? 1 : 0);
        }

        int ITimerDebugService.GetAllTimers(TimerDebugInfo[] results)
        {
            if (results == null)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = Time.timeAsDouble;
            double unscaledTime = Time.unscaledTimeAsDouble;
#if UNITY_EDITOR
            double realtime = Time.realtimeSinceStartupAsDouble;
#else
            const double realtime = 0d;
#endif
            for (int slotIndex = 0; slotIndex < _slotCapacity && count < results.Length; slotIndex++)
            {
                if ((GetState(slotIndex) & STATE_ACTIVE) != 0)
                {
                    FillDebugInfo(slotIndex, ref results[count++], scaledTime, unscaledTime, realtime);
                }
            }

            return count;
        }

#if UNITY_EDITOR
        int ITimerEditorDebugService.GetStaleOneShotTimers(TimerDebugInfo[] results)
        {
            if (results == null)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = Time.timeAsDouble;
            double unscaledTime = Time.unscaledTimeAsDouble;
            double realtime = Time.realtimeSinceStartupAsDouble;
            for (int slotIndex = 0; slotIndex < _slotCapacity && count < results.Length; slotIndex++)
            {
                if ((GetState(slotIndex) & (STATE_ACTIVE | STATE_LOOP)) == STATE_ACTIVE
                    && realtime - GetCreationTime(slotIndex) > STALE_ONE_SHOT_SECONDS)
                {
                    FillDebugInfo(slotIndex, ref results[count++], scaledTime, unscaledTime, realtime);
                }
            }

            return count;
        }
#endif

        private ulong ScheduleTimer(TimerGenericInvoker invoker, object handler, object arg, float time, bool isLoop, bool isUnscaled)
        {
            int slotIndex = AcquireSlot(time);
            if (slotIndex < 0)
            {
                return 0UL;
            }

            double duration = NormalizeDelay(time);
            ulong handle = (++s_handleGeneration << HANDLE_INDEX_BITS) | (uint)(slotIndex + 1);
            TimerPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.Handles[offset] = handle;
            page.States[offset] = ComposeState(isLoop, isUnscaled);
            page.TriggerTimes[offset] = GetCurrentTime(isUnscaled) + duration;
            page.Durations[offset] = duration;
#if UNITY_EDITOR
            page.CreationTimes[offset] = Time.realtimeSinceStartupAsDouble;
#endif
            page.Invokers[offset] = invoker;
            page.Handlers[offset] = handler;
            page.Args[offset] = arg;

            _pageActiveCounts[slotIndex >> PAGE_SHIFT]++;
            if (++_activeCount > _peakActiveCount)
            {
                _peakActiveCount = _activeCount;
            }

            AddToQueue(slotIndex, GetWheel(isUnscaled));
            return handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AcquireSlot(float delay)
        {
            if (_isDestroyed || !float.IsFinite(delay))
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("service is destroyed or delay is not finite.");
#endif
                return INVALID_INDEX;
            }

            if (_freeHead < 0 && !AddPage())
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("no available timer slot.");
#endif
                return INVALID_INDEX;
            }

            int slotIndex = _freeHead;
            _freeHead = GetNextIndex(slotIndex);
            return slotIndex;
        }

        private bool AddPage()
        {
            if (_pageCount >= MAX_PAGE_COUNT)
            {
                return false;
            }

            if (_pageCount == _pages.Length)
            {
                int capacity = Math.Min(MAX_PAGE_COUNT, _pages.Length << 1);
                Array.Resize(ref _pages, capacity);
                Array.Resize(ref _pageActiveCounts, capacity);
            }

            _pages[_pageCount] = new TimerPage();
            int baseSlotIndex = _pageCount << PAGE_SHIFT;
            _pageCount++;
            _slotCapacity += PAGE_SIZE;
            for (int slotIndex = baseSlotIndex + PAGE_MASK; slotIndex >= baseSlotIndex; slotIndex--)
            {
                PushFreeSlot(slotIndex);
            }

            return true;
        }

        /// <summary>
        /// 释放尾部不含已租用槽位的页。需要重建空闲槽位栈，因为被释放页中的槽位必须从栈中消失。
        /// 应在大量计时器被移除后调用。
        /// </summary>
        public void TrimExcess()
        {
            int retainedPageCount = _executingSlotIndex >= 0 ? (_executingSlotIndex >> PAGE_SHIFT) + 1 : 0;
            int pageCount = _pageCount;
            while (pageCount > retainedPageCount && _pageActiveCounts[pageCount - 1] == 0)
            {
                pageCount--;
            }

            if (pageCount == _pageCount)
            {
                return;
            }

            Array.Clear(_pages, pageCount, _pageCount - pageCount);
            _pageCount = pageCount;
            _slotCapacity = pageCount << PAGE_SHIFT;
            _freeHead = INVALID_INDEX;
            for (int slotIndex = _slotCapacity - 1; slotIndex >= 0; slotIndex--)
            {
                if (GetState(slotIndex) == 0)
                {
                    PushFreeSlot(slotIndex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComposeState(bool isLoop, bool isUnscaled)
        {
            byte state = (byte)(STATE_ACTIVE | STATE_RUNNING);
            if (isLoop)
            {
                state |= STATE_LOOP;
            }

            if (isUnscaled)
            {
                state |= STATE_UNSCALED;
            }

            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSlotIndex(ulong timerHandle)
        {
            int slotIndex = (int)(timerHandle & HANDLE_INDEX_MASK) - 1;
            if ((uint)slotIndex >= (uint)_slotCapacity || GetHandle(slotIndex) != timerHandle)
            {
                return INVALID_INDEX;
            }

            return slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReleaseSlot(int slotIndex)
        {
            TimerPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            if (page.QueueIndices[offset] >= 0)
            {
                RemoveFromQueue(slotIndex, GetWheel((page.States[offset] & STATE_UNSCALED) != 0));
            }

            _activeCount--;
            _pageActiveCounts[slotIndex >> PAGE_SHIFT]--;
            page.Handles[offset] = 0UL;
            page.Invokers[offset] = null;
            page.Handlers[offset] = null;
            page.Args[offset] = null;

            if (slotIndex == _executingSlotIndex)
            {
                page.States[offset] = STATE_RELEASE_PENDING;
                return;
            }

            page.States[offset] = 0;
            PushFreeSlot(slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PushFreeSlot(int slotIndex)
        {
            SetNextIndex(slotIndex, _freeHead);
            _freeHead = slotIndex;
        }

        private void ClearAll()
        {
            for (int slotIndex = 0; slotIndex < _slotCapacity; slotIndex++)
            {
                if ((GetState(slotIndex) & STATE_ACTIVE) != 0)
                {
                    ReleaseSlot(slotIndex);
                }
            }

            TrimExcess();
        }

        private void ProcessDueTimer(int slotIndex, TimerWheel wheel, double currentTime)
        {
            _executingSlotIndex = slotIndex;
            _executingSlotRescheduled = false;

            try
            {
                TimerPage page = GetPage(slotIndex);
                int offset = GetOffset(slotIndex);
                TimerGenericInvoker invoker = page.Invokers[offset];
                if (invoker == null)
                {
                    ((TimerHandlerNoArgs)page.Handlers[offset]).Invoke();
                }
                else
                {
                    invoker.Invoke(page.Handlers[offset], page.Args[offset]);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _executingSlotIndex = INVALID_INDEX;
            }

            byte state = GetState(slotIndex);
            if ((state & STATE_RELEASE_PENDING) != 0)
            {
                SetState(slotIndex, 0);
                PushFreeSlot(slotIndex);
                return;
            }

            if (_executingSlotRescheduled)
            {
                return;
            }

            if ((state & STATE_LOOP) == 0)
            {
                ReleaseSlot(slotIndex);
                return;
            }

            if ((state & STATE_RUNNING) != 0)
            {
                RescheduleLoop(slotIndex, wheel, currentTime);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RescheduleLoop(int slotIndex, TimerWheel wheel, double currentTime)
        {
            double duration = GetDuration(slotIndex);
            double triggerTime = GetTriggerTime(slotIndex) + duration;
            if (triggerTime <= currentTime)
            {
                triggerTime = currentTime + duration;
            }

            SetTriggerTime(slotIndex, triggerTime);
            AddToQueue(slotIndex, wheel);
        }

        private void AdvanceQueue(TimerWheel wheel, double currentTime)
        {
            long currentTick = TimeToTickFloor(currentTime);
            if (wheel.Count == 0)
            {
                wheel.CursorTick = currentTick + 1L;
                return;
            }

            long cursorTick = wheel.CursorTick;
            if (cursorTick > currentTick)
                return;

            if (currentTick - cursorTick > MAX_CATCHUP_TICKS)
            {
                RebaseWheel(wheel, currentTick);
                cursorTick = currentTick;
            }

            int callbackBudget = MAX_TIMERS_PER_FRAME;
            while (cursorTick <= currentTick && wheel.Count > 0)
            {
                wheel.CursorTick = cursorTick;
                AdvanceWheelTick(wheel, cursorTick, currentTime, ref callbackBudget);
                if (callbackBudget <= 0)
                {
                    // 游标停留在处理到一半的 tick 上：剩余计时器仍留在该桶中，
                    // 下一帧用新的预算继续执行。
                    return;
                }

                cursorTick++;
            }

            wheel.CursorTick = currentTick + 1L;
        }

        /// <summary>
        /// 长时间停滞后，将所有排队计时器重新以当前 tick 为基准。已过期计时器合并到当前
        /// level-0 桶（突发量仍受回调预算约束），未来计时器回到其常规轮层级。
        /// 复杂度为 O(桶数 + 计时器数)，避免挂起或长卡顿后遍历数百万个空 tick。
        /// </summary>
        private void RebaseWheel(TimerWheel wheel, long currentTick)
        {
            for (int bucket = 0; bucket < WHEEL_BUCKET_COUNT; bucket++)
            {
                int slotIndex = DetachBucket(wheel, bucket);
                while (slotIndex >= 0)
                {
                    int nextSlotIndex = GetNextIndex(slotIndex);
                    long dueTick = GetDueTick(slotIndex);
                    long targetTick = dueTick > currentTick ? dueTick : currentTick;
                    AddToBucket(slotIndex, GetWheelBucketIndex(targetTick, currentTick), wheel);
                    slotIndex = nextSlotIndex;
                }
            }

            wheel.CursorTick = currentTick;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToQueue(int slotIndex, TimerWheel wheel)
        {
            long dueTick = TimeToTickCeiling(GetTriggerTime(slotIndex));
            long cursorTick = wheel.CursorTick;
            if (dueTick < cursorTick)
            {
                dueTick = cursorTick;
            }

            SetDueTick(slotIndex, dueTick);
            AddToBucket(slotIndex, GetWheelBucketIndex(dueTick, cursorTick), wheel);
            wheel.Count++;
            if (slotIndex == _executingSlotIndex)
            {
                _executingSlotRescheduled = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveFromQueue(int slotIndex, TimerWheel wheel)
        {
            int bucketIndex = GetQueueIndex(slotIndex);
            int prevSlotIndex = GetQueuePrevIndex(slotIndex);
            int nextSlotIndex = GetNextIndex(slotIndex);
            if (prevSlotIndex >= 0)
            {
                SetNextIndex(prevSlotIndex, nextSlotIndex);
            }
            else
            {
                wheel.Heads[bucketIndex] = nextSlotIndex;
            }

            if (nextSlotIndex >= 0)
            {
                SetQueuePrevIndex(nextSlotIndex, prevSlotIndex);
            }
            else
            {
                wheel.Tails[bucketIndex] = prevSlotIndex;
            }

            SetQueueIndex(slotIndex, INVALID_INDEX);
            wheel.Count--;
        }

        private void AdvanceWheelTick(TimerWheel wheel, long tick, double currentTime, ref int callbackBudget)
        {
            if ((tick & WHEEL_MASK) == 0L)
            {
                for (int level = WHEEL_MAX_LEVEL; level > 0; level--)
                {
                    CascadeWheelLevel(wheel, tick, level);
                }
            }

            ProcessBucket(wheel, (int)(tick & WHEEL_MASK), currentTime, ref callbackBudget);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CascadeWheelLevel(TimerWheel wheel, long tick, int level)
        {
            long mask = (1L << (level * WHEEL_SHIFT)) - 1L;
            if ((tick & mask) != 0L)
            {
                return;
            }

            int slotIndex = DetachBucket(wheel, (level << WHEEL_SHIFT) + (int)((tick >> (level * WHEEL_SHIFT)) & WHEEL_MASK));
            while (slotIndex >= 0)
            {
                int nextSlotIndex = GetNextIndex(slotIndex);
                AddToBucket(slotIndex, GetWheelBucketIndex(GetDueTick(slotIndex), tick), wheel);
                slotIndex = nextSlotIndex;
            }
        }

        private void ProcessBucket(TimerWheel wheel, int bucketIndex, double currentTime, ref int callbackBudget)
        {
            while (callbackBudget > 0)
            {
                int slotIndex = wheel.Heads[bucketIndex];
                if (slotIndex < 0)
                    return;

                RemoveFromQueue(slotIndex, wheel);
                callbackBudget--;
                ProcessDueTimer(slotIndex, wheel, currentTime);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToBucket(int slotIndex, int bucketIndex, TimerWheel wheel)
        {
            int tailSlotIndex = wheel.Tails[bucketIndex];
            SetQueueIndex(slotIndex, bucketIndex);
            SetQueuePrevIndex(slotIndex, tailSlotIndex);
            SetNextIndex(slotIndex, INVALID_INDEX);
            if (tailSlotIndex >= 0)
            {
                SetNextIndex(tailSlotIndex, slotIndex);
            }
            else
            {
                wheel.Heads[bucketIndex] = slotIndex;
            }

            wheel.Tails[bucketIndex] = slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DetachBucket(TimerWheel wheel, int bucketIndex)
        {
            int headSlotIndex = wheel.Heads[bucketIndex];
            wheel.Heads[bucketIndex] = INVALID_INDEX;
            wheel.Tails[bucketIndex] = INVALID_INDEX;
            return headSlotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetWheelBucketIndex(long dueTick, long currentTick)
        {
            long delta = dueTick - currentTick;
            if (delta < WHEEL_SIZE)
            {
                return (int)(dueTick & WHEEL_MASK);
            }

            if (delta < (1L << (WHEEL_SHIFT * 2)))
            {
                return WHEEL_SIZE + (int)((dueTick >> WHEEL_SHIFT) & WHEEL_MASK);
            }

            if (delta < (1L << (WHEEL_SHIFT * 3)))
            {
                return (WHEEL_SIZE << 1) + (int)((dueTick >> (WHEEL_SHIFT * 2)) & WHEEL_MASK);
            }

            return (WHEEL_MAX_LEVEL << WHEEL_SHIFT) + (int)((dueTick >> (WHEEL_SHIFT * WHEEL_MAX_LEVEL)) & WHEEL_MASK);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimerWheel GetWheel(bool isUnscaled)
        {
            return isUnscaled ? _unscaledWheel : _scaledWheel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GetCurrentTime(bool isUnscaled)
        {
            return isUnscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long TimeToTickFloor(double time)
        {
            return (long)(time * TICKS_PER_SECOND);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long TimeToTickCeiling(double time)
        {
            double tick = Math.Ceiling(time * TICKS_PER_SECOND);
            return tick >= long.MaxValue ? long.MaxValue : (long)tick;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NormalizeDelay(float delay)
        {
            return delay > MINIMUM_DELAY_SECONDS ? delay : MINIMUM_DELAY_SECONDS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimerPage GetPage(int slotIndex)
        {
            return _pages[slotIndex >> PAGE_SHIFT];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetOffset(int slotIndex)
        {
            return slotIndex & PAGE_MASK;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong GetHandle(int slotIndex)
        {
            return GetPage(slotIndex).Handles[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetState(int slotIndex)
        {
            return GetPage(slotIndex).States[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetState(int slotIndex, byte value)
        {
            GetPage(slotIndex).States[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetTriggerTime(int slotIndex)
        {
            return GetPage(slotIndex).TriggerTimes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTriggerTime(int slotIndex, double value)
        {
            GetPage(slotIndex).TriggerTimes[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetDuration(int slotIndex)
        {
            return GetPage(slotIndex).Durations[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetRemainingTime(int slotIndex)
        {
            return GetPage(slotIndex).RemainingTimes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetRemainingTime(int slotIndex, double value)
        {
            GetPage(slotIndex).RemainingTimes[GetOffset(slotIndex)] = value;
        }

#if UNITY_EDITOR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetCreationTime(int slotIndex)
        {
            return GetPage(slotIndex).CreationTimes[GetOffset(slotIndex)];
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetDueTick(int slotIndex)
        {
            return GetPage(slotIndex).DueTicks[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDueTick(int slotIndex, long value)
        {
            GetPage(slotIndex).DueTicks[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQueueIndex(int slotIndex)
        {
            return GetPage(slotIndex).QueueIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetQueueIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).QueueIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetNextIndex(int slotIndex)
        {
            return GetPage(slotIndex).NextIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetNextIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).NextIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQueuePrevIndex(int slotIndex)
        {
            return GetPage(slotIndex).QueuePrevIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetQueuePrevIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).QueuePrevIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillDebugInfo(int slotIndex, ref TimerDebugInfo info, double scaledTime, double unscaledTime, double realtime)
        {
            byte state = GetState(slotIndex);
            bool running = (state & STATE_RUNNING) != 0;
            bool unscaled = (state & STATE_UNSCALED) != 0;
            double leftTime = running ? GetTriggerTime(slotIndex) - (unscaled ? unscaledTime : scaledTime) : GetRemainingTime(slotIndex);
            if (leftTime < 0d)
            {
                leftTime = 0d;
            }

            byte flags = 0;
            if (running)
            {
                flags |= TimerDebugFlags.Running;
            }

            if ((state & STATE_LOOP) != 0)
            {
                flags |= TimerDebugFlags.Loop;
            }

            if (unscaled)
            {
                flags |= TimerDebugFlags.Unscaled;
            }

            info.TimerHandle = GetHandle(slotIndex);
            info.LeftTime = (float)leftTime;
            info.Duration = (float)GetDuration(slotIndex);
#if UNITY_EDITOR
            info.Age = (float)(realtime - GetCreationTime(slotIndex));
#else
            info.Age = 0f;
#endif
            info.Flags = flags;
        }
    }
}
