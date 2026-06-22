using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace AlicizaX
{
    public static unsafe class MemoryPool<T> where T : MemoryObject, new()
    {
        private const int PageShift = 5;
        private const int PageSize = 1 << PageShift;
        private const int PageMask = PageSize - 1;
        private const int InvalidIndex = -1;
        private const int MinKeep = MemoryPool.MinimumFreeReserveLimit;
        private const int LookaheadFrames = 8;
        private const int MissBoost = 2;
        private const int BurstMargin = 2;
        private const float RateEwmaAlpha = 0.25f;
        private const float PeakEwmaAlpha = 0.08f;

        private const byte ObjectStateNone = 0;
        private const byte ObjectStateFree = 1;
        private const byte ObjectStateLeased = 2;
        private const byte ObjectStateReleasing = 3;
        private const byte ObjectStateEvicting = 4;
        private const byte ObjectStateEvicted = 5;

        private const byte SlotStateEmpty = 0;
        private const byte SlotStateFree = 1;
        private const byte SlotStateLeased = 2;
        private const byte SlotStateReleasing = 3;
        private const byte SlotStateEvicting = 4;
        private const byte SlotStateEvicted = 5;

        private const int PageFlagInFreeQueue = 1 << 0;
        private const int PageFlagInEmptyQueue = 1 << 1;
        private const int PageFlagInEvictQueue = 1 << 2;
        private const int PageFlagTombstone = 1 << 3;
        private const int PageFlagFreeQueueDebt = 1 << 4;
        private const int PageFlagEmptyQueueDebt = 1 << 5;

        private struct PageHeader
        {
            public int ConstructedCount;
            public int FreeCount;
            public int LeasedCount;
            public int EmptyCount;
            public int FreeHead;
            public int EmptyHead;
            public int NextUninitializedSlot;
            public int PageGeneration;
            public int QueueGeneration;
            public int LastUsedFrame;
            public int Flags;
        }

        private struct SlotMeta
        {
            public int PageGeneration;
            public int SlotGeneration;
            public int Next;
            public byte State;
        }

        private readonly struct PageHandle
        {
            public readonly int PageIndex;
            public readonly int QueueGeneration;

            public PageHandle(int pageIndex, int queueGeneration)
            {
                PageIndex = pageIndex;
                QueueGeneration = queueGeneration;
            }
        }

        private static readonly MemoryPoolRegistry.MemoryPoolHandle s_Handle;
        private static readonly MemoryPoolHandle s_PublicHandle;
        private static readonly int s_PoolId;

        private static PageHeader* s_PageHeaders;
        private static SlotMeta* s_SlotMetas;
        private static T[][] s_ObjectPages = Array.Empty<T[]>();
        private static int s_PageCount;
        private static int s_PageCapacity;
        private static int s_SlotCapacity;

        private static PageHandle* s_FreePageQueue;
        private static int s_FreeQueueCapacity;
        private static int s_FreeQueueHead;
        private static int s_FreeQueueTail;
        private static int s_FreeQueueCount;

        private static PageHandle* s_EmptyPageQueue;
        private static int s_EmptyQueueCapacity;
        private static int s_EmptyQueueHead;
        private static int s_EmptyQueueTail;
        private static int s_EmptyQueueCount;

        private static PageHandle* s_EvictPageQueue;
        private static int s_EvictQueueCapacity;
        private static int s_EvictQueueHead;
        private static int s_EvictQueueTail;
        private static int s_EvictQueueCount;

        private static int* s_ReleasedPageStack;
        private static int s_ReleasedPageCapacity;
        private static int s_ReleasedPageCount;
        private static bool s_HasFreeScanDebt;
        private static bool s_HasEmptyScanDebt;
        private static int s_FreeDebtScanCursor;
        private static int s_EmptyDebtScanCursor;

        private static int s_InUse;
        private static int s_FreeCount;
        private static int s_ConstructedCount;
        private static int s_CreatedCount;
        private static int s_EmergencyCreateCount;
        private static int s_MissCount;
        private static int s_MissDebt;
        private static int s_AcquireCount;
        private static int s_ReleaseCount;
        private static int s_AcquireThisFrame;
        private static int s_ReleaseThisFrame;
        private static int s_LastAcquireFrame;
        private static int s_LastReleaseFrame;
        private static int s_LastMissFrame;
        private static float s_AcquireRateEwma;
        private static float s_ReleaseRateEwma;
        private static float s_BurstEwma;
        private static int s_FastPeakInUse;
        private static int s_SlowPeakInUse;
        private static int s_TargetLiveForecast = MinKeep;
        private static int s_TargetFreeReserve = MinKeep;
        private static int s_SoftFreeReserveLimit = MemoryPool.DefaultSoftFreeReserveLimit;
        private static int s_HardFreeReserveLimit = MemoryPool.DefaultHardFreeReserveLimit;
        private static int s_IdleFrames;
        private static int s_LastTickFrame = InvalidIndex;
        private static bool s_InPoolCallback;
        private static bool s_PendingClearNativeMetadata;

        static MemoryPool()
        {
            MemoryPoolRegistry.AssertMainThread();
            s_Handle = new MemoryPoolRegistry.MemoryPoolHandle(
                typeof(T),
                acquire: AcquireAsMemory,
                release: ReleaseAsMemory,
                clear: ClearAll,
                clearNativeMetadata: ClearAllNativeMetadata,
                add: Add,
                setCapacity: SetCapacity,
                getInfo: GetInfo,
                tick: Tick,
                shrink: Shrink,
                compact: Compact,
                trimNativeMetadata: TrimNativeMetadata,
                resetStats: ResetStats);
            s_PublicHandle = new MemoryPoolHandle(s_Handle);
            s_PoolId = s_Handle.PoolId;
            MemoryPoolRegistry.Register(typeof(T), s_Handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MemoryObject AcquireAsMemory()
        {
            return Acquire();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReleaseAsMemory(MemoryObject memory)
        {
            Release((T)memory);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Acquire()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Acquire");
            MemoryPoolRegistry.ScheduleTick(s_Handle);
            s_AcquireCount++;
            s_AcquireThisFrame++;
            s_LastAcquireFrame = MemoryPoolRegistry.CurrentFrame;
            s_InUse++;
            if (s_InUse > s_FastPeakInUse)
                s_FastPeakInUse = s_InUse;
            if (s_InUse > s_SlowPeakInUse)
                s_SlowPeakInUse = s_InUse;

            if (TryAcquireFree(out T item))
                return item;

            s_MissCount++;
            s_MissDebt++;
            s_LastMissFrame = s_LastAcquireFrame;
            UpdateWatermarkOnMiss();
            NormalizeMissDebt();
            return EmergencyCreateOne();
        }

        public static void Release(T item)
        {
            MemoryPoolRegistry.AssertMainThread();
            if (item == null)
                return;
            ThrowIfInPoolCallback("Release");

            ValidateForRelease(item, out int pageIndex, out int slotIndex);

            if ((s_PageHeaders[pageIndex].Flags & PageFlagTombstone) != 0)
                ReleaseTombstoneLeased(item, pageIndex, slotIndex);
            else if (s_FreeCount < s_HardFreeReserveLimit)
                ReleaseToFree(item, pageIndex, slotIndex);
            else
                ReleaseOverHard(item, pageIndex, slotIndex);
        }

        public static void Add(int count)
        {
            MemoryPoolRegistry.AssertMainThread();
            if (count <= 0)
                return;

            MemoryPoolRegistry.ScheduleTick(s_Handle);
            int target = s_TargetFreeReserve + count;
            s_TargetFreeReserve = Clamp(target, MinKeep, s_HardFreeReserveLimit);
            s_TargetLiveForecast = Math.Max(s_TargetLiveForecast, s_InUse + s_TargetFreeReserve);
            s_MissDebt += count;
            NormalizeMissDebt();

            int budget = MemoryPoolRegistry.GetGrowthBudget();
            ProcessGrowth(Math.Min(count, budget));
        }

        public static void Shrink(int keepCount)
        {
            MemoryPoolRegistry.AssertMainThread();
            keepCount = Math.Max(keepCount, 0);
            s_TargetFreeReserve = Math.Min(s_TargetFreeReserve, keepCount);
            int budget = Math.Max(0, s_FreeCount - keepCount);
            ProcessEvict(budget);
        }

        public static void Compact()
        {
            MemoryPoolRegistry.AssertMainThread();
            ProcessEvict(Math.Max(0, s_FreeCount - s_TargetFreeReserve));
        }

        public static void TrimNativeMetadata()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("TrimNativeMetadata");
            if (s_InUse != 0)
                return;

            Exception callbackException = ClearAllCore();
            ReleaseNativeMetadataNow();
            MemoryPoolRegistry.UnscheduleTick(s_Handle);
            Rethrow(callbackException);
        }

        public static void SetMaxCapacity(int max)
        {
            SetCapacity(max, Math.Max(max << 2, MinKeep));
        }

        public static void SetCapacity(int softCapacity, int hardCapacity)
        {
            MemoryPoolRegistry.AssertMainThread();
            softCapacity = Math.Max(softCapacity, MinKeep);
            hardCapacity = Math.Max(hardCapacity, softCapacity);
            s_SoftFreeReserveLimit = softCapacity;
            s_HardFreeReserveLimit = hardCapacity;
            s_TargetFreeReserve = Math.Min(s_TargetFreeReserve, s_SoftFreeReserveLimit);
            s_TargetLiveForecast = Math.Min(s_TargetLiveForecast, s_SoftFreeReserveLimit);
            NormalizeMissDebt();
            MemoryPoolRegistry.ScheduleTick(s_Handle);
        }

        public static void ClearAll()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("ClearAll");
            Exception callbackException = ClearAllCore();
            Rethrow(callbackException);
        }

        private static Exception ClearAllCore()
        {
            Exception callbackException = null;
            for (int pageIndex = 0; pageIndex < s_PageCount; pageIndex++)
                CaptureFirstException(ref callbackException, TombstonePage(pageIndex));

            ClearQueues();
            s_FreeCount = 0;
            s_InUse = 0;
            for (int pageIndex = 0; pageIndex < s_PageCount; pageIndex++)
                s_InUse += s_PageHeaders[pageIndex].LeasedCount;

            s_ConstructedCount = s_InUse;
            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;
            s_MissDebt = 0;
            s_AcquireRateEwma = 0f;
            s_ReleaseRateEwma = 0f;
            s_BurstEwma = 0f;
            s_FastPeakInUse = 0;
            s_SlowPeakInUse = 0;
            s_TargetFreeReserve = 0;
            s_TargetLiveForecast = 0;
            s_IdleFrames = 0;
            s_LastAcquireFrame = InvalidIndex;
            s_LastReleaseFrame = InvalidIndex;
            s_LastMissFrame = InvalidIndex;
            s_LastTickFrame = InvalidIndex;
            s_HasFreeScanDebt = false;
            s_HasEmptyScanDebt = false;
            s_FreeDebtScanCursor = 0;
            s_EmptyDebtScanCursor = 0;

            if (s_InUse == 0)
                ReleaseAllPages();

            MemoryPoolRegistry.UnscheduleTick(s_Handle);
            return callbackException;
        }

        public static void ClearAllNativeMetadata()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("ClearAllNativeMetadata");
            Exception callbackException = ClearAllCore();
            if (s_InUse > 0)
            {
                s_PendingClearNativeMetadata = true;
                Rethrow(callbackException);
                return;
            }

            ReleaseNativeMetadataNow();
            Rethrow(callbackException);
        }

        private static void ReleaseNativeMetadataNow()
        {
            s_PendingClearNativeMetadata = false;
            FreeNativeMetadata();
            s_ObjectPages = Array.Empty<T[]>();
            s_PageCount = 0;
            s_ReleasedPageCount = 0;
            s_InUse = 0;
            s_FreeCount = 0;
            s_ConstructedCount = 0;
            s_PageCapacity = 0;
            s_SlotCapacity = 0;
            s_FreeQueueCapacity = 0;
            s_EmptyQueueCapacity = 0;
            s_EvictQueueCapacity = 0;
            s_ReleasedPageCapacity = 0;
        }

        public static void ResetStats()
        {
            MemoryPoolRegistry.AssertMainThread();
            s_AcquireCount = 0;
            s_ReleaseCount = 0;
            s_CreatedCount = 0;
            s_EmergencyCreateCount = 0;
            s_MissCount = 0;
            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;
            s_LastAcquireFrame = InvalidIndex;
            s_LastReleaseFrame = InvalidIndex;
            s_LastMissFrame = InvalidIndex;
            s_AcquireRateEwma = 0f;
            s_ReleaseRateEwma = 0f;
            s_BurstEwma = 0f;
            s_FastPeakInUse = s_InUse;
            s_SlowPeakInUse = s_InUse;
        }

        public static int UnusedCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_FreeCount;
        }

        internal static void GetInfo(ref MemoryPoolInfo info)
        {
            info.Set(
                typeof(T), s_FreeCount,
                s_InUse,
                s_AcquireCount, s_ReleaseCount,
                s_CreatedCount,
                s_TargetFreeReserve, s_HardFreeReserveLimit,
                s_IdleFrames, Math.Max(0, s_PageCount - s_ReleasedPageCount) * PageSize);
        }

        internal static bool Tick(int frameCount)
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Tick");
            if (frameCount == s_LastTickFrame)
                return true;

            s_LastTickFrame = frameCount;
            bool active = s_AcquireThisFrame > 0 || s_ReleaseThisFrame > 0 || s_InUse > 0 || s_MissDebt > 0;
            s_IdleFrames = active ? 0 : s_IdleFrames + 1;

            NormalizeMissDebt();
            UpdateWatermarks();
            NormalizeMissDebt();
            ProcessDirtyQueues(8);
            ProcessGrowth(MemoryPoolRegistry.GetGrowthBudget());
            NormalizeMissDebt();
            ProcessEvict(MemoryPoolRegistry.GetEvictBudget());

            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;

            if (TryAutoTrimNativeMetadata())
                return false;

            return s_IdleFrames < MemoryPool.UnscheduleIdleFrames || ShouldKeepTickingForAutoTrim() || s_FreeCount > s_TargetFreeReserve || s_MissDebt > 0 || s_InUse > 0;
        }

        private static bool ShouldKeepTickingForAutoTrim()
        {
            return MemoryPool.AutoTrimNativeMetadataFrames >= 0
                   && s_IdleFrames < MemoryPool.AutoTrimNativeMetadataFrames
                   && s_InUse == 0
                   && s_PageCapacity > 0;
        }

        private static bool TryAutoTrimNativeMetadata()
        {
            if (MemoryPool.AutoTrimNativeMetadataFrames < 0)
                return false;
            if (s_IdleFrames < MemoryPool.AutoTrimNativeMetadataFrames)
                return false;
            if (s_InUse != 0 || s_FreeCount != 0 || s_ConstructedCount != 0)
                return false;
            if (s_PageCapacity <= 0)
                return false;

            ReleaseNativeMetadataNow();
            return true;
        }

        private static bool TryAcquireFree(out T item)
        {
            while (TryDequeueValidPage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, PageFlagInFreeQueue, true, out int pageIndex))
            {
                ref PageHeader page = ref s_PageHeaders[pageIndex];
                int slotIndex = page.FreeHead;
                if (slotIndex < 0)
                    continue;

                int slotMetaIndex = GetSlotMetaIndex(pageIndex, slotIndex);
                ref SlotMeta slot = ref s_SlotMetas[slotMetaIndex];
                item = s_ObjectPages[pageIndex][slotIndex];
                if (item == null || slot.State != SlotStateFree || slot.PageGeneration != page.PageGeneration)
                    ThrowInvalidState("Corrupted free slot.");

                page.FreeHead = slot.Next;
                page.FreeCount--;
                page.LeasedCount++;
                page.LastUsedFrame = MemoryPoolRegistry.CurrentFrame;
                slot.Next = InvalidIndex;
                slot.State = SlotStateLeased;
                item.State = ObjectStateLeased;
                s_ObjectPages[pageIndex][slotIndex] = null;
                s_FreeCount--;

                if (page.FreeCount > 0)
                    EnqueuePage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, pageIndex, PageFlagInFreeQueue);

                return true;
            }

            item = null;
            return false;
        }

        private static T EmergencyCreateOne()
        {
            EnsureEmergencySlot(out int pageIndex, out int slotIndex);
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            int slotMetaIndex = GetSlotMetaIndex(pageIndex, slotIndex);
            ref SlotMeta slot = ref s_SlotMetas[slotMetaIndex];

            T item = new T();
            s_CreatedCount++;
            s_EmergencyCreateCount++;
            s_ConstructedCount++;
            page.ConstructedCount++;
            page.EmptyCount--;
            page.LeasedCount++;
            page.LastUsedFrame = MemoryPoolRegistry.CurrentFrame;

            InitializeMemoryObject(item, pageIndex, slotIndex, page.PageGeneration, slot.SlotGeneration, ObjectStateLeased);
            slot.PageGeneration = page.PageGeneration;
            slot.State = SlotStateLeased;
            slot.Next = InvalidIndex;
            return item;
        }

        private static void ReleaseToFree(T item, int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            item.State = ObjectStateReleasing;
            slot.State = SlotStateReleasing;
            try
            {
                InvokeClear(item);
            }
            catch
            {
                item.State = ObjectStateLeased;
                slot.State = SlotStateLeased;
                throw;
            }

            item.State = ObjectStateFree;
            slot.State = SlotStateFree;
            slot.Next = page.FreeHead;
            page.FreeHead = slotIndex;
            page.LeasedCount--;
            page.FreeCount++;
            page.LastUsedFrame = MemoryPoolRegistry.CurrentFrame;
            s_InUse--;
            s_FreeCount++;
            s_ReleaseCount++;
            s_ReleaseThisFrame++;
            s_LastReleaseFrame = MemoryPoolRegistry.CurrentFrame;
            s_ObjectPages[pageIndex][slotIndex] = item;
            EnqueuePage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, pageIndex, PageFlagInFreeQueue);
            TryCompletePendingNativeMetadataClear();
        }

        private static void ReleaseOverHard(T item, int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            item.State = ObjectStateReleasing;
            slot.State = SlotStateReleasing;
            try
            {
                InvokeClear(item);
            }
            catch
            {
                item.State = ObjectStateLeased;
                slot.State = SlotStateLeased;
                throw;
            }

            Exception evictException = EvictLeasedObject(item, ref page, ref slot, pageIndex, slotIndex, true);
            s_InUse--;
            s_ReleaseCount++;
            s_ReleaseThisFrame++;
            s_LastReleaseFrame = MemoryPoolRegistry.CurrentFrame;
            TryReleaseEmptyPage(pageIndex);
            TryCompletePendingNativeMetadataClear();
            Rethrow(evictException);
        }

        private static void ReleaseTombstoneLeased(T item, int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            item.State = ObjectStateReleasing;
            slot.State = SlotStateReleasing;
            try
            {
                InvokeClear(item);
            }
            catch
            {
                item.State = ObjectStateLeased;
                slot.State = SlotStateLeased;
                throw;
            }

            Exception evictException = EvictLeasedObject(item, ref page, ref slot, pageIndex, slotIndex, false);
            s_InUse = Math.Max(0, s_InUse - 1);
            s_ReleaseCount++;
            s_ReleaseThisFrame++;
            s_LastReleaseFrame = MemoryPoolRegistry.CurrentFrame;
            if ((page.Flags & PageFlagTombstone) != 0 && page.LeasedCount == 0)
                ReleasePageStorage(pageIndex);
            TryCompletePendingNativeMetadataClear();
            Rethrow(evictException);
        }

        private static Exception EvictLeasedObject(T item, ref PageHeader page, ref SlotMeta slot, int pageIndex, int slotIndex, bool enqueueEmpty)
        {
            item.State = ObjectStateEvicting;
            slot.State = SlotStateEvicting;
            Exception callbackException = CaptureCallbackException(item);

            item.State = ObjectStateEvicted;
            ResetMemoryObject(item);
            slot.SlotGeneration++;
            slot.State = SlotStateEmpty;
            if (enqueueEmpty)
            {
                slot.Next = page.EmptyHead;
                page.EmptyHead = slotIndex;
            }
            else
            {
                slot.Next = InvalidIndex;
            }
            page.LeasedCount--;
            page.ConstructedCount--;
            page.EmptyCount++;
            s_ConstructedCount--;
            if (enqueueEmpty)
                EnqueuePage(s_EmptyPageQueue, s_EmptyQueueCapacity, ref s_EmptyQueueHead, ref s_EmptyQueueTail, ref s_EmptyQueueCount, pageIndex, PageFlagInEmptyQueue);
            return callbackException;
        }

        private static Exception EvictFree(int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            T item = s_ObjectPages[pageIndex][slotIndex];
            if (item == null || slot.State != SlotStateFree)
                ThrowInvalidState("Corrupted evict slot.");

            item.State = ObjectStateEvicting;
            slot.State = SlotStateEvicting;
            Exception callbackException = CaptureCallbackException(item);

            item.State = ObjectStateEvicted;
            s_ObjectPages[pageIndex][slotIndex] = null;
            ResetMemoryObject(item);
            slot.SlotGeneration++;
            slot.State = SlotStateEmpty;
            slot.Next = page.EmptyHead;
            page.EmptyHead = slotIndex;
            page.ConstructedCount--;
            page.FreeCount--;
            page.EmptyCount++;
            s_ConstructedCount--;
            s_FreeCount--;
            EnqueuePage(s_EmptyPageQueue, s_EmptyQueueCapacity, ref s_EmptyQueueHead, ref s_EmptyQueueTail, ref s_EmptyQueueCount, pageIndex, PageFlagInEmptyQueue);
            return callbackException;
        }

        private static void ProcessGrowth(int budget)
        {
            while (budget > 0 && s_FreeCount < s_TargetFreeReserve && s_FreeCount < s_HardFreeReserveLimit)
            {
                EnsureEmergencySlot(out int pageIndex, out int slotIndex);
                ref PageHeader page = ref s_PageHeaders[pageIndex];
                ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
                T item = new T();
                s_CreatedCount++;
                s_ConstructedCount++;
                page.ConstructedCount++;
                page.EmptyCount--;
                page.FreeCount++;
                InitializeMemoryObject(item, pageIndex, slotIndex, page.PageGeneration, slot.SlotGeneration, ObjectStateFree);
                slot.PageGeneration = page.PageGeneration;
                slot.State = SlotStateFree;
                slot.Next = page.FreeHead;
                page.FreeHead = slotIndex;
                s_ObjectPages[pageIndex][slotIndex] = item;
                s_FreeCount++;
                if (s_MissDebt > 0)
                    s_MissDebt--;
                EnqueuePage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, pageIndex, PageFlagInFreeQueue);
                budget--;
            }
            NormalizeMissDebt();
        }

        private static void ProcessEvict(int budget)
        {
            NormalizeMissDebt();
            if (budget <= 0 || s_MissDebt > 0 || s_FreeCount <= s_TargetFreeReserve)
                return;

            while (budget > 0 && s_FreeCount > s_TargetFreeReserve)
            {
            if (!TryDequeueValidPage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, PageFlagInFreeQueue, true, out int pageIndex))
                    return;

                ref PageHeader page = ref s_PageHeaders[pageIndex];
                int slotIndex = page.FreeHead;
                if (slotIndex < 0)
                    continue;

                page.FreeHead = s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)].Next;
                Exception evictException = EvictFree(pageIndex, slotIndex);
                if (page.FreeCount > 0)
                    EnqueuePage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, pageIndex, PageFlagInFreeQueue);
                TryReleaseEmptyPage(pageIndex);
                budget--;
                Rethrow(evictException);
            }
        }

        private static void EnsureEmergencySlot(out int pageIndex, out int slotIndex)
        {
            if (!TryDequeueValidPage(s_EmptyPageQueue, s_EmptyQueueCapacity, ref s_EmptyQueueHead, ref s_EmptyQueueTail, ref s_EmptyQueueCount, PageFlagInEmptyQueue, false, out pageIndex))
                pageIndex = CreatePage();

            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if (page.EmptyHead >= 0)
            {
                slotIndex = page.EmptyHead;
                page.EmptyHead = s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)].Next;
            }
            else
            {
                slotIndex = page.NextUninitializedSlot;
                page.NextUninitializedSlot++;
            }

            if (page.EmptyCount > 1)
                EnqueuePage(s_EmptyPageQueue, s_EmptyQueueCapacity, ref s_EmptyQueueHead, ref s_EmptyQueueTail, ref s_EmptyQueueCount, pageIndex, PageFlagInEmptyQueue);
        }

        private static int CreatePage()
        {
            int pageIndex;
            if (s_ReleasedPageCount > 0)
            {
                pageIndex = s_ReleasedPageStack[--s_ReleasedPageCount];
                s_ReleasedPageStack[s_ReleasedPageCount] = 0;
            }
            else
            {
                EnsurePageCapacity(s_PageCount + 1);
                pageIndex = s_PageCount++;
            }

            int pageGeneration = s_PageHeaders[pageIndex].PageGeneration;
            int queueGeneration = s_PageHeaders[pageIndex].QueueGeneration;
            if (pageGeneration == 0)
                pageGeneration = 1;
            if (queueGeneration == 0)
                queueGeneration = 1;

            s_PageHeaders[pageIndex] = new PageHeader
            {
                EmptyCount = PageSize,
                FreeHead = InvalidIndex,
                EmptyHead = InvalidIndex,
                PageGeneration = pageGeneration,
                QueueGeneration = queueGeneration
            };
            s_ObjectPages[pageIndex] = new T[PageSize];
            int start = pageIndex << PageShift;
            for (int i = 0; i < PageSize; i++)
            {
                s_SlotMetas[start + i].PageGeneration = pageGeneration;
                if (s_SlotMetas[start + i].SlotGeneration == 0)
                    s_SlotMetas[start + i].SlotGeneration = 1;
                s_SlotMetas[start + i].Next = InvalidIndex;
                s_SlotMetas[start + i].State = SlotStateEmpty;
            }

            return pageIndex;
        }

        private static void EnsurePageCapacity(int requiredPages)
        {
            if (s_PageCapacity >= requiredPages)
                return;

            int newPageCapacity = s_PageCapacity == 0 ? 4 : s_PageCapacity;
            while (newPageCapacity < requiredPages)
                newPageCapacity <<= 1;

            Array.Resize(ref s_ObjectPages, newPageCapacity);
            ResizeUnmanaged(ref s_PageHeaders, s_PageCapacity, newPageCapacity);
            ResizeUnmanaged(ref s_SlotMetas, s_SlotCapacity, newPageCapacity * PageSize);
            ResizeUnmanaged(ref s_ReleasedPageStack, s_ReleasedPageCapacity, newPageCapacity);
            s_PageCapacity = newPageCapacity;
            s_SlotCapacity = newPageCapacity * PageSize;
            s_ReleasedPageCapacity = newPageCapacity;
            EnsureQueueCapacity(ref s_FreePageQueue, ref s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, s_FreeQueueCount, newPageCapacity);
            EnsureQueueCapacity(ref s_EmptyPageQueue, ref s_EmptyQueueCapacity, ref s_EmptyQueueHead, ref s_EmptyQueueTail, s_EmptyQueueCount, newPageCapacity);
            EnsureQueueCapacity(ref s_EvictPageQueue, ref s_EvictQueueCapacity, ref s_EvictQueueHead, ref s_EvictQueueTail, s_EvictQueueCount, newPageCapacity);
        }

        private static void ValidateForRelease(T item, out int pageIndex, out int slotIndex)
        {
            if (!item.OwnerHandle.IsValid)
                ThrowInvalidState("Memory object has no owner pool.");
            if (item.PoolId != s_PoolId)
                ThrowInvalidState("Memory object belongs to another pool.");

            DecodeSlotId(item.SlotId, out pageIndex, out slotIndex);
            if ((uint)pageIndex >= (uint)s_PageCount || (uint)slotIndex >= PageSize)
                ThrowInvalidState("Memory object slot is out of range.");

            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            if (item.PageGeneration != page.PageGeneration || slot.PageGeneration != page.PageGeneration)
                ThrowInvalidState("Memory object page generation mismatch.");
            if (item.SlotGeneration != slot.SlotGeneration)
                ThrowInvalidState("Memory object slot generation mismatch.");
            if (item.State != ObjectStateLeased || slot.State != SlotStateLeased)
                ThrowInvalidState("Memory object is not leased.");
        }

        private static bool TryDequeueValidPage(PageHandle* queue, int capacity, ref int head, ref int tail, ref int count, int flag, bool requireFree, out int pageIndex)
        {
            while (count > 0)
            {
                PageHandle handle = queue[head];
                queue[head] = default;
                head = (head + 1) % capacity;
                count--;

                if ((uint)handle.PageIndex >= (uint)s_PageCount)
                    continue;

                ref PageHeader page = ref s_PageHeaders[handle.PageIndex];
                page.Flags &= ~flag;
                if (page.QueueGeneration != handle.QueueGeneration || (page.Flags & PageFlagTombstone) != 0)
                    continue;
                if (requireFree ? page.FreeCount <= 0 : page.EmptyCount <= 0)
                    continue;

                pageIndex = handle.PageIndex;
                return true;
            }

            head = 0;
            tail = 0;
            pageIndex = InvalidIndex;
            return false;
        }

        private static void EnqueuePage(PageHandle* queue, int capacity, ref int head, ref int tail, ref int count, int pageIndex, int flag)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if ((page.Flags & (flag | PageFlagTombstone)) != 0)
                return;
            if (count == capacity)
            {
                if (flag == PageFlagInFreeQueue)
                {
                    page.Flags |= PageFlagFreeQueueDebt;
                    s_HasFreeScanDebt = true;
                }
                else if (flag == PageFlagInEmptyQueue)
                {
                    page.Flags |= PageFlagEmptyQueueDebt;
                    s_HasEmptyScanDebt = true;
                }

                return;
            }

            queue[tail] = new PageHandle(pageIndex, page.QueueGeneration);
            tail = (tail + 1) % capacity;
            count++;
            page.Flags |= flag;
            if (flag == PageFlagInFreeQueue)
                page.Flags &= ~PageFlagFreeQueueDebt;
            else if (flag == PageFlagInEmptyQueue)
                page.Flags &= ~PageFlagEmptyQueueDebt;
        }

        private static void ProcessDirtyQueues(int budget)
        {
            while (budget > 0 && s_HasFreeScanDebt)
            {
                if (!TryRepairQueueDebt(PageFlagFreeQueueDebt, PageFlagInFreeQueue, ref s_FreeDebtScanCursor, true))
                    s_HasFreeScanDebt = false;
                budget--;
            }

            while (budget > 0 && s_HasEmptyScanDebt)
            {
                if (!TryRepairQueueDebt(PageFlagEmptyQueueDebt, PageFlagInEmptyQueue, ref s_EmptyDebtScanCursor, false))
                    s_HasEmptyScanDebt = false;
                budget--;
            }
        }

        private static bool TryRepairQueueDebt(int debtFlag, int queueFlag, ref int cursor, bool freeQueue)
        {
            if (s_PageCount <= 0)
                return false;

            int scanned = 0;
            bool hasMoreDebt = false;
            while (scanned < s_PageCount)
            {
                int pageIndex = cursor;
                cursor++;
                if (cursor >= s_PageCount)
                    cursor = 0;
                scanned++;

                ref PageHeader page = ref s_PageHeaders[pageIndex];
                if ((page.Flags & debtFlag) == 0)
                    continue;

                hasMoreDebt = true;
                if ((page.Flags & PageFlagTombstone) != 0)
                {
                    page.Flags &= ~debtFlag;
                    continue;
                }

                if (freeQueue)
                {
                    if (page.FreeCount > 0 && (page.Flags & PageFlagInFreeQueue) == 0)
                        EnqueuePage(s_FreePageQueue, s_FreeQueueCapacity, ref s_FreeQueueHead, ref s_FreeQueueTail, ref s_FreeQueueCount, pageIndex, queueFlag);
                }
                else
                {
                    if (page.EmptyCount > 0 && (page.Flags & PageFlagInEmptyQueue) == 0)
                        EnqueuePage(s_EmptyPageQueue, s_EmptyQueueCapacity, ref s_EmptyQueueHead, ref s_EmptyQueueTail, ref s_EmptyQueueCount, pageIndex, queueFlag);
                }

                return true;
            }

            return hasMoreDebt;
        }

        private static void EnsureQueueCapacity(ref PageHandle* queue, ref int queueCapacity, ref int head, ref int tail, int count, int capacity)
        {
            if (queueCapacity >= capacity)
                return;

            PageHandle* oldQueue = queue;
            int oldCapacity = queueCapacity;
            PageHandle* newQueue = AllocUnmanaged<PageHandle>(capacity);
            if (oldQueue != null && oldCapacity > 0 && count > 0)
            {
                for (int i = 0; i < count; i++)
                    newQueue[i] = oldQueue[(head + i) % oldCapacity];
            }

            FreeUnmanaged(oldQueue);
            queue = newQueue;
            queueCapacity = capacity;
            head = 0;
            tail = count;
        }

        private static void UpdateWatermarkOnMiss()
        {
            int boostedMissDebt = s_MissDebt * MissBoost;
            s_TargetFreeReserve = Clamp(Math.Max(s_TargetFreeReserve, boostedMissDebt), MinKeep, Math.Min(s_SoftFreeReserveLimit, s_HardFreeReserveLimit));
            s_TargetLiveForecast = Math.Min(Math.Max(s_TargetLiveForecast, s_InUse + boostedMissDebt), s_SoftFreeReserveLimit);
        }

        private static void NormalizeMissDebt()
        {
            if (s_MissDebt <= 0)
                return;

            if (MemoryPoolRegistry.GetGrowthBudget() <= 0)
            {
                s_MissDebt = 0;
                return;
            }

            int reserveLimit = Math.Min(s_TargetFreeReserve, s_HardFreeReserveLimit);
            int maxDebt = Math.Max(0, reserveLimit - s_FreeCount);
            if (s_MissDebt > maxDebt)
                s_MissDebt = maxDebt;
        }

        private static void UpdateWatermarks()
        {
            int minFreeReserve = s_IdleFrames >= MemoryPool.ZeroFreeReserveStartFrames ? 0 : MinKeep;
            s_AcquireRateEwma = Lerp(s_AcquireRateEwma, s_AcquireThisFrame, RateEwmaAlpha);
            s_ReleaseRateEwma = Lerp(s_ReleaseRateEwma, s_ReleaseThisFrame, RateEwmaAlpha);
            int frameBurst = Math.Max(0, s_AcquireThisFrame - s_ReleaseThisFrame);
            s_BurstEwma = Lerp(s_BurstEwma, frameBurst, RateEwmaAlpha);
            s_FastPeakInUse = Math.Max(s_InUse, (int)Lerp(s_FastPeakInUse, s_InUse, PeakEwmaAlpha));
            s_SlowPeakInUse = Math.Max(s_InUse, (int)Lerp(s_SlowPeakInUse, s_InUse, PeakEwmaAlpha * 0.25f));

            if (s_IdleFrames >= MemoryPool.ShortDecayStartFrames && s_FastPeakInUse > 0)
                s_FastPeakInUse -= Math.Max(1, s_FastPeakInUse >> 4);
            if (s_IdleFrames >= MemoryPool.LongDecayStartFrames && s_SlowPeakInUse > 0)
                s_SlowPeakInUse -= Math.Max(1, s_SlowPeakInUse >> 6);

            int liveForecast = Max(
                s_FastPeakInUse + BurstMargin,
                s_SlowPeakInUse,
                CeilToInt(s_AcquireRateEwma * LookaheadFrames),
                s_MissDebt * MissBoost,
                minFreeReserve);
            int desiredFree = Max(
                CeilToInt(s_BurstEwma),
                CeilToInt(s_AcquireRateEwma * LookaheadFrames),
                s_MissDebt * MissBoost,
                minFreeReserve);
            if (s_MissDebt > 0)
            {
                liveForecast = Math.Max(liveForecast, s_TargetLiveForecast);
                desiredFree = Math.Max(desiredFree, s_TargetFreeReserve);
            }
            else if (s_IdleFrames < MemoryPool.ShortDecayStartFrames)
            {
                liveForecast = Math.Max(liveForecast, s_TargetLiveForecast);
                desiredFree = Math.Max(desiredFree, s_TargetFreeReserve);
            }

            s_TargetFreeReserve = Clamp(desiredFree, minFreeReserve, Math.Min(s_SoftFreeReserveLimit, s_HardFreeReserveLimit));
            s_TargetLiveForecast = Math.Min(liveForecast, s_SoftFreeReserveLimit);
        }

        private static Exception TombstonePage(int pageIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if ((page.Flags & PageFlagTombstone) != 0)
                return null;

            page.Flags |= PageFlagTombstone;
            page.QueueGeneration++;

            Exception callbackException = null;
            int slotIndex = page.FreeHead;
            while (slotIndex >= 0)
            {
                int next = s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)].Next;
                CaptureFirstException(ref callbackException, TombstoneEvictFree(pageIndex, slotIndex));
                slotIndex = next;
            }

            page.FreeHead = InvalidIndex;
            page.EmptyHead = InvalidIndex;
            page.NextUninitializedSlot = PageSize;
            page.FreeCount = 0;
            page.EmptyCount = PageSize - page.LeasedCount;
            page.ConstructedCount = page.LeasedCount;
            page.Flags &= ~(PageFlagInFreeQueue | PageFlagInEmptyQueue | PageFlagInEvictQueue);
            return callbackException;
        }

        private static Exception TombstoneEvictFree(int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            T item = s_ObjectPages[pageIndex][slotIndex];
            Exception callbackException = null;
            if (item != null)
            {
                item.State = ObjectStateEvicting;
                slot.State = SlotStateEvicting;
                callbackException = CaptureCallbackException(item);
                item.State = ObjectStateEvicted;
                ResetMemoryObject(item);
            }

            s_ObjectPages[pageIndex][slotIndex] = null;
            slot.SlotGeneration++;
            slot.State = SlotStateEmpty;
            slot.Next = InvalidIndex;
            page.ConstructedCount--;
            page.FreeCount--;
            page.EmptyCount++;
            s_ConstructedCount--;
            s_FreeCount--;
            return callbackException;
        }

        private static void TryReleaseEmptyPage(int pageIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if (page.LeasedCount != 0 || page.FreeCount != 0 || page.EmptyCount != PageSize)
                return;

            ReleasePageStorage(pageIndex);
        }

        private static void ReleasePageStorage(int pageIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            page.PageGeneration++;
            page.QueueGeneration++;
            page.FreeHead = InvalidIndex;
            page.EmptyHead = InvalidIndex;
            page.NextUninitializedSlot = 0;
            page.ConstructedCount = 0;
            page.FreeCount = 0;
            page.LeasedCount = 0;
            page.EmptyCount = PageSize;
            page.Flags = 0;
            s_ObjectPages[pageIndex] = null;
            if (s_ReleasedPageCount < s_ReleasedPageCapacity)
                s_ReleasedPageStack[s_ReleasedPageCount++] = pageIndex;
        }

        private static void ReleaseAllPages()
        {
            for (int i = 0; i < s_PageCount; i++)
                s_ObjectPages[i] = null;

            s_PageCount = 0;
            s_ReleasedPageCount = 0;
            s_ConstructedCount = 0;
            s_FreeCount = 0;
        }

        private static void ClearQueues()
        {
            s_FreeQueueHead = s_FreeQueueTail = s_FreeQueueCount = 0;
            s_EmptyQueueHead = s_EmptyQueueTail = s_EmptyQueueCount = 0;
            s_EvictQueueHead = s_EvictQueueTail = s_EvictQueueCount = 0;
        }

        private static void InitializeMemoryObject(T item, int pageIndex, int slotIndex, int pageGeneration, int slotGeneration, byte state)
        {
            item.OwnerHandle = s_PublicHandle;
            item.PoolId = s_PoolId;
            item.SlotId = EncodeSlotId(pageIndex, slotIndex);
            item.PageGeneration = pageGeneration;
            item.SlotGeneration = slotGeneration;
            item.State = state;
        }

        private static void ResetMemoryObject(T item)
        {
            item.OwnerHandle = default;
            item.PoolId = 0;
            item.SlotId = InvalidIndex;
            item.PageGeneration = 0;
            item.SlotGeneration = 0;
            item.State = ObjectStateNone;
        }

        private static void InvokeClear(T item)
        {
            if (s_InPoolCallback)
                ThrowInvalidState("Memory pool callback reentry detected.");

            s_InPoolCallback = true;
            try
            {
                item.Clear();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: Clear() failed.", exception);
            }
            finally
            {
                s_InPoolCallback = false;
            }
        }

        private static void InvokeOnEvict(T item)
        {
            if (!(item is IPoolEvictable evictable))
                return;
            if (s_InPoolCallback)
                ThrowInvalidState("Memory pool callback reentry detected.");

            s_InPoolCallback = true;
            try
            {
                evictable.OnEvict();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: OnEvict() failed.", exception);
            }
            finally
            {
                s_InPoolCallback = false;
            }
        }

        private static void ThrowIfInPoolCallback(string operation)
        {
            if (s_InPoolCallback)
                ThrowInvalidState($"{operation} is not allowed during Clear() or OnEvict().");
        }

        private static U* AllocUnmanaged<U>(int count) where U : unmanaged
        {
            if (count <= 0)
                return null;

            int size = sizeof(U) * count;
            IntPtr memory = Marshal.AllocHGlobal(size);
            Span<byte> bytes = new Span<byte>((void*)memory, size);
            bytes.Clear();
            return (U*)memory;
        }

        private static void ResizeUnmanaged<U>(ref U* buffer, int oldCount, int newCount) where U : unmanaged
        {
            U* newBuffer = AllocUnmanaged<U>(newCount);
            if (buffer != null && oldCount > 0 && newBuffer != null)
            {
                int copyCount = Math.Min(oldCount, newCount);
                Buffer.MemoryCopy(buffer, newBuffer, sizeof(U) * newCount, sizeof(U) * copyCount);
            }

            FreeUnmanaged(buffer);
            buffer = newBuffer;
        }

        private static void FreeUnmanaged<U>(U* buffer) where U : unmanaged
        {
            if (buffer != null)
                Marshal.FreeHGlobal((IntPtr)buffer);
        }

        private static void FreeNativeMetadata()
        {
            FreeUnmanaged(s_PageHeaders);
            FreeUnmanaged(s_SlotMetas);
            FreeUnmanaged(s_FreePageQueue);
            FreeUnmanaged(s_EmptyPageQueue);
            FreeUnmanaged(s_EvictPageQueue);
            FreeUnmanaged(s_ReleasedPageStack);
            s_PageHeaders = null;
            s_SlotMetas = null;
            s_FreePageQueue = null;
            s_EmptyPageQueue = null;
            s_EvictPageQueue = null;
            s_ReleasedPageStack = null;
        }

        private static void TryCompletePendingNativeMetadataClear()
        {
            if (!s_PendingClearNativeMetadata || s_InUse > 0)
                return;

            Exception callbackException = ClearAllCore();
            ReleaseNativeMetadataNow();
            Rethrow(callbackException);
        }

        private static Exception CaptureCallbackException(T item)
        {
            try
            {
                InvokeOnEvict(item);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void CaptureFirstException(ref Exception first, Exception next)
        {
            if (first == null && next != null)
                first = next;
        }

        private static void Rethrow(Exception exception)
        {
            if (exception != null)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EncodeSlotId(int pageIndex, int slotIndex)
        {
            return (pageIndex << PageShift) | slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecodeSlotId(int slotId, out int pageIndex, out int slotIndex)
        {
            pageIndex = slotId >> PageShift;
            slotIndex = slotId & PageMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSlotMetaIndex(int pageIndex, int slotIndex)
        {
            return (pageIndex << PageShift) + slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int Max(int a, int b, int c, int d, int e)
        {
            return Math.Max(Math.Max(Math.Max(a, b), Math.Max(c, d)), e);
        }

        private static int Max(int a, int b, int c, int d)
        {
            return Math.Max(Math.Max(a, b), Math.Max(c, d));
        }

        private static int CeilToInt(float value)
        {
            return (int)Math.Ceiling(value);
        }

        private static float Lerp(float from, float to, float alpha)
        {
            return from + (to - from) * alpha;
        }

        private static void ThrowInvalidState(string message)
        {
            throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: {message}");
        }
    }
}
