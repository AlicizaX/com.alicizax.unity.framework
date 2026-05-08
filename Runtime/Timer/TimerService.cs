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

    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class TimerService : ServiceBase, ITimerService, ITimerCapacityService, ITimerDebugService, IServiceTickable
#if UNITY_EDITOR
        , ITimerEditorDebugService
#endif
    {
        private const int PAGE_SHIFT = 8;
        private const int PAGE_SIZE = 1 << PAGE_SHIFT;
        private const int PAGE_MASK = PAGE_SIZE - 1;
        private const int DEFAULT_INITIAL_CAPACITY = 1024;
        private const int MAX_PAGE_COUNT = 4096;
        private const int INVALID_INDEX = -1;
        private const double MINIMUM_DELAY_SECONDS = 0.000001d;
        private const double STALE_ONE_SHOT_SECONDS = 300d;

        private const byte HANDLER_NONE = 0;
        private const byte HANDLER_NO_ARGS = 1;
        private const byte HANDLER_GENERIC = 2;

        private const byte STATE_ACTIVE = 1 << 0;
        private const byte STATE_RUNNING = 1 << 1;
        private const byte STATE_LOOP = 1 << 2;
        private const byte STATE_UNSCALED = 1 << 3;
        private const byte STATE_RELEASE_PENDING = 1 << 4;

        private sealed class TimerPage
        {
            public readonly ulong[] Handles = new ulong[PAGE_SIZE];
            public readonly uint[] Versions = new uint[PAGE_SIZE];
            public readonly byte[] States = new byte[PAGE_SIZE];
            public readonly byte[] HandlerTypes = new byte[PAGE_SIZE];
            public readonly double[] TriggerTimes = new double[PAGE_SIZE];
            public readonly double[] Durations = new double[PAGE_SIZE];
            public readonly double[] RemainingTimes = new double[PAGE_SIZE];
            public readonly double[] CreationTimes = new double[PAGE_SIZE];
            public readonly int[] QueueIndices = new int[PAGE_SIZE];
            public readonly int[] ActiveIndices = new int[PAGE_SIZE];
            public readonly TimerHandlerNoArgs[] NoArgsHandlers = new TimerHandlerNoArgs[PAGE_SIZE];
            public readonly TimerGenericInvoker[] GenericInvokers = new TimerGenericInvoker[PAGE_SIZE];
            public readonly object[] GenericHandlers = new object[PAGE_SIZE];
            public readonly object[] GenericArgs = new object[PAGE_SIZE];

            public TimerPage()
            {
                for (int i = 0; i < PAGE_SIZE; i++)
                {
                    QueueIndices[i] = INVALID_INDEX;
                    ActiveIndices[i] = INVALID_INDEX;
                }
            }
        }

        private sealed class IntPage
        {
            public readonly int[] Values = new int[PAGE_SIZE];
        }

        private TimerPage[] _pages;
        private IntPage[] _freeSlotPages;
        private IntPage[] _activeSlotPages;
        private IntPage[] _scaledHeapPages;
        private IntPage[] _unscaledHeapPages;
        private int _pageCount;
        private int _slotCapacity;
        private int _freeCount;
        private int _activeCount;
        private int _peakActiveCount;
        private int _scaledHeapCount;
        private int _unscaledHeapCount;
        private int _executingSlotIndex;
        private double _executingCurrentTime;

        public TimerService() : this(DEFAULT_INITIAL_CAPACITY)
        {
        }

        public TimerService(int initialCapacity)
        {
            int normalizedCapacity = NormalizeCapacity(initialCapacity);
            _pages = new TimerPage[MAX_PAGE_COUNT];
            _freeSlotPages = new IntPage[MAX_PAGE_COUNT];
            _activeSlotPages = new IntPage[MAX_PAGE_COUNT];
            _scaledHeapPages = new IntPage[MAX_PAGE_COUNT];
            _unscaledHeapPages = new IntPage[MAX_PAGE_COUNT];
            _executingSlotIndex = INVALID_INDEX;
            Prewarm(normalizedCapacity);
        }

        public int Order
        {
            get { return 0; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prewarm(int capacity)
        {
            int targetCapacity = NormalizeCapacity(capacity);
            if (targetCapacity > MAX_PAGE_COUNT * PAGE_SIZE)
            {
                targetCapacity = MAX_PAGE_COUNT * PAGE_SIZE;
            }

            while (_slotCapacity < targetCapacity)
            {
                AddPage();
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AddTimer(TimerHandlerNoArgs callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            if (callback == null)
            {
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(time), isLoop, isUnscaled);
            SetHandlerType(slotIndex, HANDLER_NO_ARGS);
            SetNoArgsHandler(slotIndex, callback);
            AddActive(slotIndex);
            AddToQueue(slotIndex, isUnscaled);
            return GetHandle(slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class
        {
            if (callback == null)
            {
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(time), isLoop, isUnscaled);
            SetHandlerType(slotIndex, HANDLER_GENERIC);
            SetGenericInvoker(slotIndex, TimerGenericInvokerCache<T>.Invoke);
            SetGenericHandler(slotIndex, callback);
            SetGenericArg(slotIndex, arg);
            AddActive(slotIndex);
            AddToQueue(slotIndex, isUnscaled);
            return GetHandle(slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Stop(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0 || (GetState(slotIndex) & STATE_RUNNING) == 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, isUnscaled);
                double leftTime = GetTriggerTime(slotIndex) - GetCurrentTime(isUnscaled);
                SetRemainingTime(slotIndex, leftTime > MINIMUM_DELAY_SECONDS ? leftTime : MINIMUM_DELAY_SECONDS);
            }
            else
            {
                SetRemainingTime(slotIndex, IsLoop(slotIndex) ? GetDuration(slotIndex) : MINIMUM_DELAY_SECONDS);
            }

            ClearState(slotIndex, STATE_RUNNING);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resume(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0 || (GetState(slotIndex) & STATE_RUNNING) != 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            double delay = GetRemainingTime(slotIndex);
            if (delay <= MINIMUM_DELAY_SECONDS)
            {
                delay = MINIMUM_DELAY_SECONDS;
            }

            SetTriggerTime(slotIndex, GetCurrentTime(isUnscaled) + delay);
            SetRemainingTime(slotIndex, 0d);
            SetState(slotIndex, STATE_RUNNING);
            AddToQueue(slotIndex, isUnscaled);
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

            double leftTime = (GetState(slotIndex) & STATE_RUNNING) == 0
                ? GetRemainingTime(slotIndex)
                : GetTriggerTime(slotIndex) - GetCurrentTime(IsUnscaled(slotIndex));
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

            bool isUnscaled = IsUnscaled(slotIndex);
            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, isUnscaled);
            }

            SetTriggerTime(slotIndex, GetCurrentTime(isUnscaled) + GetDuration(slotIndex));
            SetRemainingTime(slotIndex, 0d);
            SetState(slotIndex, STATE_RUNNING);
            AddToQueue(slotIndex, isUnscaled);
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
            RecoverInterruptedExecution();
            AdvanceQueue(false, Time.timeAsDouble);
            AdvanceQueue(true, Time.unscaledTimeAsDouble);
        }

        protected override void OnInitialize()
        {
        }

        protected override void OnDestroyService()
        {
            ClearAll();
        }

        void ITimerDebugService.GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            activeCount = _activeCount;
            poolCapacity = _slotCapacity;
            peakActiveCount = _peakActiveCount;
            freeCount = _freeCount;
        }

        int ITimerDebugService.GetAllTimers(TimerDebugInfo[] results)
        {
            if (results == null || results.Length == 0)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = Time.timeAsDouble;
            double unscaledTime = Time.unscaledTimeAsDouble;
            double realtime = Time.realtimeSinceStartupAsDouble;
            int limit = results.Length;
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                FillDebugInfo(GetActiveSlot(i), ref results[count], scaledTime, unscaledTime, realtime);
                count++;
            }

            return count;
        }

#if UNITY_EDITOR
        int ITimerEditorDebugService.GetStaleOneShotTimers(TimerDebugInfo[] results)
        {
            if (results == null || results.Length == 0)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = Time.timeAsDouble;
            double unscaledTime = Time.unscaledTimeAsDouble;
            double realtime = Time.realtimeSinceStartupAsDouble;
            int limit = results.Length;
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                int slotIndex = GetActiveSlot(i);
                if (IsLoop(slotIndex) || realtime - GetCreationTime(slotIndex) <= STALE_ONE_SHOT_SECONDS)
                {
                    continue;
                }

                FillDebugInfo(slotIndex, ref results[count], scaledTime, unscaledTime, realtime);
                count++;
            }

            return count;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AcquireSlot()
        {
            if (_freeCount <= 0)
            {
                AddPage();
                if (_freeCount <= 0)
                {
                    return INVALID_INDEX;
                }
            }

            return GetFreeSlot(--_freeCount);
        }

        private void AddPage()
        {
            if (_pageCount >= MAX_PAGE_COUNT)
            {
                return;
            }

            EnsureIndexPage(_freeSlotPages, _pageCount);
            EnsureIndexPage(_activeSlotPages, _pageCount);
            EnsureIndexPage(_scaledHeapPages, _pageCount);
            EnsureIndexPage(_unscaledHeapPages, _pageCount);

            TimerPage page = new TimerPage();
            _pages[_pageCount] = page;

            int baseSlotIndex = _pageCount << PAGE_SHIFT;
            for (int i = 0; i < PAGE_SIZE; i++)
            {
                SetFreeSlot(_freeCount++, baseSlotIndex + i);
            }

            _pageCount++;
            _slotCapacity += PAGE_SIZE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureIndexPage(IntPage[] pages, int pageIndex)
        {
            if (pages[pageIndex] == null)
            {
                pages[pageIndex] = new IntPage();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFreeSlot(int index)
        {
            return GetPagedInt(_freeSlotPages, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFreeSlot(int index, int value)
        {
            SetPagedInt(_freeSlotPages, index, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetActiveSlot(int index)
        {
            return GetPagedInt(_activeSlotPages, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetActiveSlot(int index, int value)
        {
            SetPagedInt(_activeSlotPages, index, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPagedInt(IntPage[] pages, int index)
        {
            return pages[index >> PAGE_SHIFT].Values[index & PAGE_MASK];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetPagedInt(IntPage[] pages, int index, int value)
        {
            pages[index >> PAGE_SHIFT].Values[index & PAGE_MASK] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeSlot(int slotIndex, double duration, bool isLoop, bool isUnscaled)
        {
            int pageIndex = slotIndex >> PAGE_SHIFT;
            int offset = slotIndex & PAGE_MASK;
            TimerPage page = _pages[pageIndex];
            uint version = page.Versions[offset] + 1U;
            page.Versions[offset] = version == 0U ? 1U : version;
            page.Handles[offset] = ComposeHandle(slotIndex, page.Versions[offset]);
            page.States[offset] = ComposeState(isLoop, isUnscaled);
            page.TriggerTimes[offset] = GetCurrentTime(isUnscaled) + duration;
            page.Durations[offset] = duration;
            page.RemainingTimes[offset] = 0d;
            page.CreationTimes[offset] = Time.realtimeSinceStartupAsDouble;
            page.QueueIndices[offset] = INVALID_INDEX;
            page.ActiveIndices[offset] = INVALID_INDEX;
            page.HandlerTypes[offset] = HANDLER_NONE;
            page.NoArgsHandlers[offset] = null;
            page.GenericInvokers[offset] = null;
            page.GenericHandlers[offset] = null;
            page.GenericArgs[offset] = null;
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
        private static ulong ComposeHandle(int slotIndex, uint version)
        {
            return ((ulong)version << 32) | (uint)(slotIndex + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSlotIndex(ulong timerHandle)
        {
            int slotIndex = (int)((timerHandle & 0xffffffffUL) - 1UL);
            if ((uint)slotIndex >= (uint)_slotCapacity || GetHandle(slotIndex) != timerHandle || (GetState(slotIndex) & STATE_ACTIVE) == 0)
            {
                return INVALID_INDEX;
            }

            return slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddActive(int slotIndex)
        {
            int activeIndex = _activeCount++;
            SetActiveSlot(activeIndex, slotIndex);
            SetActiveIndex(slotIndex, activeIndex);
            if (_activeCount > _peakActiveCount)
            {
                _peakActiveCount = _activeCount;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveActive(int slotIndex)
        {
            int activeIndex = GetActiveIndex(slotIndex);
            if ((uint)activeIndex >= (uint)_activeCount)
            {
                SetActiveIndex(slotIndex, INVALID_INDEX);
                return;
            }

            int lastIndex = --_activeCount;
            int lastSlotIndex = GetActiveSlot(lastIndex);
            SetActiveSlot(activeIndex, lastSlotIndex);
            SetActiveIndex(lastSlotIndex, activeIndex);
            SetActiveSlot(lastIndex, 0);
            SetActiveIndex(slotIndex, INVALID_INDEX);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReleaseSlot(int slotIndex)
        {
            byte state = GetState(slotIndex);
            if ((state & STATE_ACTIVE) == 0)
            {
                return;
            }

            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, (state & STATE_UNSCALED) != 0);
            }

            RemoveActive(slotIndex);
            ClearHandler(slotIndex);
            SetRemainingTime(slotIndex, 0d);
            SetTriggerTime(slotIndex, 0d);
            SetDuration(slotIndex, 0d);
            SetCreationTime(slotIndex, 0d);
            SetHandle(slotIndex, 0UL);

            if (slotIndex == _executingSlotIndex)
            {
                SetStateRaw(slotIndex, STATE_RELEASE_PENDING);
                return;
            }

            SetStateRaw(slotIndex, 0);
            SetFreeSlot(_freeCount++, slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearHandler(int slotIndex)
        {
            SetHandlerType(slotIndex, HANDLER_NONE);
            SetNoArgsHandler(slotIndex, null);
            SetGenericInvoker(slotIndex, null);
            SetGenericHandler(slotIndex, null);
            SetGenericArg(slotIndex, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FreeReleasedExecutingSlot(int slotIndex)
        {
            SetStateRaw(slotIndex, 0);
            SetFreeSlot(_freeCount++, slotIndex);
        }

        private void ClearAll()
        {
            _scaledHeapCount = 0;
            _unscaledHeapCount = 0;
            while (_activeCount > 0)
            {
                _executingSlotIndex = INVALID_INDEX;
                ReleaseSlot(GetActiveSlot(_activeCount - 1));
            }

            _executingSlotIndex = INVALID_INDEX;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecoverInterruptedExecution()
        {
            int slotIndex = _executingSlotIndex;
            if (slotIndex < 0)
            {
                return;
            }

            _executingSlotIndex = INVALID_INDEX;
            if ((uint)slotIndex >= (uint)_slotCapacity)
            {
                return;
            }

            byte state = GetState(slotIndex);
            if ((state & STATE_RELEASE_PENDING) != 0)
            {
                FreeReleasedExecutingSlot(slotIndex);
                return;
            }

            if ((state & STATE_ACTIVE) == 0 || GetQueueIndex(slotIndex) >= 0)
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
                RescheduleLoop(slotIndex, _executingCurrentTime);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessDueTimer(int slotIndex, double currentTime)
        {
            byte state = GetState(slotIndex);
            if ((state & (STATE_ACTIVE | STATE_RUNNING)) != (STATE_ACTIVE | STATE_RUNNING))
            {
                return;
            }

            _executingSlotIndex = slotIndex;
            _executingCurrentTime = currentTime;

            byte handlerType = GetHandlerType(slotIndex);
            if (handlerType == HANDLER_NO_ARGS)
            {
                GetNoArgsHandler(slotIndex).Invoke();
            }
            else if (handlerType == HANDLER_GENERIC)
            {
                GetGenericInvoker(slotIndex).Invoke(GetGenericHandler(slotIndex), GetGenericArg(slotIndex));
            }

            _executingSlotIndex = INVALID_INDEX;

            state = GetState(slotIndex);
            if ((state & STATE_RELEASE_PENDING) != 0)
            {
                FreeReleasedExecutingSlot(slotIndex);
                return;
            }

            if ((state & STATE_ACTIVE) == 0 || GetQueueIndex(slotIndex) >= 0)
            {
                return;
            }

            if ((state & STATE_LOOP) != 0)
            {
                if ((state & STATE_RUNNING) != 0)
                {
                    RescheduleLoop(slotIndex, currentTime);
                }

                return;
            }

            ReleaseSlot(slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RescheduleLoop(int slotIndex, double currentTime)
        {
            double duration = GetDuration(slotIndex);
            double triggerTime = GetTriggerTime(slotIndex) + duration;
            if (triggerTime <= currentTime)
            {
                triggerTime = currentTime + duration;
            }

            SetTriggerTime(slotIndex, triggerTime);
            AddToQueue(slotIndex, IsUnscaled(slotIndex));
        }

        private void AdvanceQueue(bool isUnscaled, double currentTime)
        {
            while (GetHeapCount(isUnscaled) > 0)
            {
                int slotIndex = GetHeapRoot(isUnscaled);
                byte state = GetState(slotIndex);
                if ((state & (STATE_ACTIVE | STATE_RUNNING)) != (STATE_ACTIVE | STATE_RUNNING))
                {
                    RemoveRoot(isUnscaled);
                    continue;
                }

                if (GetTriggerTime(slotIndex) > currentTime)
                {
                    return;
                }

                RemoveRoot(isUnscaled);
                ProcessDueTimer(slotIndex, currentTime);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToQueue(int slotIndex, bool isUnscaled)
        {
            int heapIndex = GetHeapCount(isUnscaled);
            SetHeapCount(isUnscaled, heapIndex + 1);
            SetHeapValue(isUnscaled, heapIndex, slotIndex);
            SetQueueIndex(slotIndex, heapIndex);
            BubbleUp(isUnscaled, heapIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveFromQueue(int slotIndex, bool isUnscaled)
        {
            int heapIndex = GetQueueIndex(slotIndex);
            int count = GetHeapCount(isUnscaled);
            if ((uint)heapIndex >= (uint)count)
            {
                SetQueueIndex(slotIndex, INVALID_INDEX);
                return;
            }

            int lastIndex = count - 1;
            int lastSlotIndex = GetHeapValue(isUnscaled, lastIndex);
            SetHeapCount(isUnscaled, lastIndex);
            SetQueueIndex(slotIndex, INVALID_INDEX);
            if (heapIndex == lastIndex)
            {
                return;
            }

            SetHeapValue(isUnscaled, heapIndex, lastSlotIndex);
            SetQueueIndex(lastSlotIndex, heapIndex);
            if (!BubbleUp(isUnscaled, heapIndex))
            {
                BubbleDown(isUnscaled, heapIndex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveRoot(bool isUnscaled)
        {
            int rootSlotIndex = GetHeapRoot(isUnscaled);
            int lastIndex = GetHeapCount(isUnscaled) - 1;
            int lastSlotIndex = GetHeapValue(isUnscaled, lastIndex);
            SetHeapCount(isUnscaled, lastIndex);
            SetQueueIndex(rootSlotIndex, INVALID_INDEX);
            if (lastIndex <= 0)
            {
                return;
            }

            SetHeapValue(isUnscaled, 0, lastSlotIndex);
            SetQueueIndex(lastSlotIndex, 0);
            BubbleDown(isUnscaled, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool BubbleUp(bool isUnscaled, int index)
        {
            bool moved = false;
            while (index > 0)
            {
                int parentIndex = (index - 1) >> 1;
                if (!Less(GetHeapValue(isUnscaled, index), GetHeapValue(isUnscaled, parentIndex)))
                {
                    break;
                }

                SwapHeap(isUnscaled, index, parentIndex);
                index = parentIndex;
                moved = true;
            }

            return moved;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BubbleDown(bool isUnscaled, int index)
        {
            int count = GetHeapCount(isUnscaled);
            while (true)
            {
                int leftIndex = (index << 1) + 1;
                if (leftIndex >= count)
                {
                    return;
                }

                int rightIndex = leftIndex + 1;
                int smallestIndex = leftIndex;
                if (rightIndex < count && Less(GetHeapValue(isUnscaled, rightIndex), GetHeapValue(isUnscaled, leftIndex)))
                {
                    smallestIndex = rightIndex;
                }

                if (!Less(GetHeapValue(isUnscaled, smallestIndex), GetHeapValue(isUnscaled, index)))
                {
                    return;
                }

                SwapHeap(isUnscaled, index, smallestIndex);
                index = smallestIndex;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Less(int leftSlotIndex, int rightSlotIndex)
        {
            double leftTime = GetTriggerTime(leftSlotIndex);
            double rightTime = GetTriggerTime(rightSlotIndex);
            if (leftTime < rightTime)
            {
                return true;
            }

            if (leftTime > rightTime)
            {
                return false;
            }

            return GetHandle(leftSlotIndex) < GetHandle(rightSlotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SwapHeap(bool isUnscaled, int leftIndex, int rightIndex)
        {
            int leftSlotIndex = GetHeapValue(isUnscaled, leftIndex);
            int rightSlotIndex = GetHeapValue(isUnscaled, rightIndex);
            SetHeapValue(isUnscaled, leftIndex, rightSlotIndex);
            SetHeapValue(isUnscaled, rightIndex, leftSlotIndex);
            SetQueueIndex(leftSlotIndex, rightIndex);
            SetQueueIndex(rightSlotIndex, leftIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetHeapCount(bool isUnscaled)
        {
            return isUnscaled ? _unscaledHeapCount : _scaledHeapCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetHeapCount(bool isUnscaled, int value)
        {
            if (isUnscaled)
            {
                _unscaledHeapCount = value;
            }
            else
            {
                _scaledHeapCount = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetHeapRoot(bool isUnscaled)
        {
            return GetHeapValue(isUnscaled, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetHeapValue(bool isUnscaled, int index)
        {
            return isUnscaled ? GetPagedInt(_unscaledHeapPages, index) : GetPagedInt(_scaledHeapPages, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetHeapValue(bool isUnscaled, int index, int value)
        {
            if (isUnscaled)
            {
                SetPagedInt(_unscaledHeapPages, index, value);
            }
            else
            {
                SetPagedInt(_scaledHeapPages, index, value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsUnscaled(int slotIndex)
        {
            return (GetState(slotIndex) & STATE_UNSCALED) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLoop(int slotIndex)
        {
            return (GetState(slotIndex) & STATE_LOOP) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GetCurrentTime(bool isUnscaled)
        {
            return isUnscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NormalizeDelay(float delay)
        {
            return delay > MINIMUM_DELAY_SECONDS ? delay : MINIMUM_DELAY_SECONDS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NormalizeCapacity(int capacity)
        {
            int normalizedCapacity = capacity > PAGE_SIZE ? capacity : PAGE_SIZE;
            int remainder = normalizedCapacity & PAGE_MASK;
            if (remainder != 0)
            {
                normalizedCapacity += PAGE_SIZE - remainder;
            }

            return normalizedCapacity;
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
        private void SetHandle(int slotIndex, ulong value)
        {
            GetPage(slotIndex).Handles[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetState(int slotIndex)
        {
            return GetPage(slotIndex).States[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetState(int slotIndex, byte mask)
        {
            TimerPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] | mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearState(int slotIndex, byte mask)
        {
            TimerPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] & ~mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetStateRaw(int slotIndex, byte value)
        {
            GetPage(slotIndex).States[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetHandlerType(int slotIndex)
        {
            return GetPage(slotIndex).HandlerTypes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetHandlerType(int slotIndex, byte value)
        {
            GetPage(slotIndex).HandlerTypes[GetOffset(slotIndex)] = value;
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
        private void SetDuration(int slotIndex, double value)
        {
            GetPage(slotIndex).Durations[GetOffset(slotIndex)] = value;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetCreationTime(int slotIndex)
        {
            return GetPage(slotIndex).CreationTimes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCreationTime(int slotIndex, double value)
        {
            GetPage(slotIndex).CreationTimes[GetOffset(slotIndex)] = value;
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
        private int GetActiveIndex(int slotIndex)
        {
            return GetPage(slotIndex).ActiveIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetActiveIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).ActiveIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimerHandlerNoArgs GetNoArgsHandler(int slotIndex)
        {
            return GetPage(slotIndex).NoArgsHandlers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetNoArgsHandler(int slotIndex, TimerHandlerNoArgs value)
        {
            GetPage(slotIndex).NoArgsHandlers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimerGenericInvoker GetGenericInvoker(int slotIndex)
        {
            return GetPage(slotIndex).GenericInvokers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGenericInvoker(int slotIndex, TimerGenericInvoker value)
        {
            GetPage(slotIndex).GenericInvokers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGenericHandler(int slotIndex)
        {
            return GetPage(slotIndex).GenericHandlers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGenericHandler(int slotIndex, object value)
        {
            GetPage(slotIndex).GenericHandlers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGenericArg(int slotIndex)
        {
            return GetPage(slotIndex).GenericArgs[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGenericArg(int slotIndex, object value)
        {
            GetPage(slotIndex).GenericArgs[GetOffset(slotIndex)] = value;
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
            info.Age = (float)(realtime - GetCreationTime(slotIndex));
            info.Flags = flags;
        }
    }
}
