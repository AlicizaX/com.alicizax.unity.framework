using System;

namespace AlicizaX
{
    public readonly struct MemoryPoolBudget
    {
        public static readonly MemoryPoolBudget Default = new MemoryPoolBudget(0, 128, 512, 16, 30f);

        public readonly int RetainFreeCount;
        public readonly int SoftFreeCount;
        public readonly int HardFreeCount;
        public readonly int TrimPerTick;
        public readonly float PeakWindowSeconds;

        public MemoryPoolBudget(int retainFreeCount, int softFreeCount, int hardFreeCount, int trimPerTick, float peakWindowSeconds)
        {
            if (retainFreeCount < 0)
                throw new ArgumentOutOfRangeException(nameof(retainFreeCount));
            if (softFreeCount < retainFreeCount)
                throw new ArgumentOutOfRangeException(nameof(softFreeCount));
            if (hardFreeCount < softFreeCount)
                throw new ArgumentOutOfRangeException(nameof(hardFreeCount));
            if (trimPerTick <= 0)
                throw new ArgumentOutOfRangeException(nameof(trimPerTick));
            if (peakWindowSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(peakWindowSeconds));

            RetainFreeCount = retainFreeCount;
            SoftFreeCount = softFreeCount;
            HardFreeCount = hardFreeCount;
            TrimPerTick = trimPerTick;
            PeakWindowSeconds = peakWindowSeconds;
        }
    }
}
