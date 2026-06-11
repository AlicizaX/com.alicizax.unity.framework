using System;
using System.Runtime.InteropServices;

namespace AlicizaX
{
    /// <summary>
    /// Memory pool snapshot info.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct MemoryPoolInfo
    {
        private Type _type;
        private int _unusedCount;
        private int _usingCount;
        private int _acquireCount;
        private int _releaseCount;
        private int _createCount;
        private int _targetFreeReserve;
        private int _maxCapacity;
        private int _idleFrames;
        private int _pageCapacity;

        public MemoryPoolInfo(Type type, int unusedCount, int usingCount,
            int acquireCount, int releaseCount, int createCount,
            int targetFreeReserve, int maxCapacity,
            int idleFrames, int pageCapacity)
        {
            _type = type;
            _unusedCount = unusedCount;
            _usingCount = usingCount;
            _acquireCount = acquireCount;
            _releaseCount = releaseCount;
            _createCount = createCount;
            _targetFreeReserve = targetFreeReserve;
            _maxCapacity = maxCapacity;
            _idleFrames = idleFrames;
            _pageCapacity = pageCapacity;
        }

        public Type Type => _type;

        public int UnusedCount => _unusedCount;

        public int UsingCount => _usingCount;

        public int AcquireCount => _acquireCount;

        public int ReleaseCount => _releaseCount;

        public int CreateCount => _createCount;

        public int TargetFreeReserve => _targetFreeReserve;

        public int MaxCapacity => _maxCapacity;

        public int IdleFrames => _idleFrames;

        public int PageCapacity => _pageCapacity;

        internal void Set(Type type, int unusedCount, int usingCount,
            int acquireCount, int releaseCount, int createCount,
            int targetFreeReserve, int maxCapacity,
            int idleFrames, int pageCapacity)
        {
            _type = type;
            _unusedCount = unusedCount;
            _usingCount = usingCount;
            _acquireCount = acquireCount;
            _releaseCount = releaseCount;
            _createCount = createCount;
            _targetFreeReserve = targetFreeReserve;
            _maxCapacity = maxCapacity;
            _idleFrames = idleFrames;
            _pageCapacity = pageCapacity;
        }
    }
}
