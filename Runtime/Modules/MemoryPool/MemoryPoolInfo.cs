using System;

namespace AlicizaX
{
    public readonly struct MemoryPoolInfo
    {
        public Type Type { get; }
        public int FreeCount { get; }
        public int LeasedCount { get; }
        public int CreatedCount { get; }
        public int TargetFreeCount { get; }
        public int PeakLeasedCount { get; }

        internal MemoryPoolInfo(Type type, int freeCount, int leasedCount, int createdCount, int targetFreeCount, int peakLeasedCount)
        {
            Type = type;
            FreeCount = freeCount;
            LeasedCount = leasedCount;
            CreatedCount = createdCount;
            TargetFreeCount = targetFreeCount;
            PeakLeasedCount = peakLeasedCount;
        }
    }
}
