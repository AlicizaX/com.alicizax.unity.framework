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
        private int _highWaterMark;
        private int _maxCapacity;
        private int _idleFrames;
        private int _poolArrayLength;

        public MemoryPoolInfo(Type type, int unusedCount, int usingCount,
            int acquireCount, int releaseCount, int createCount,
            int highWaterMark, int maxCapacity,
            int idleFrames, int poolArrayLength)
        {
            _type = type;
            _unusedCount = unusedCount;
            _usingCount = usingCount;
            _acquireCount = acquireCount;
            _releaseCount = releaseCount;
            _createCount = createCount;
            _highWaterMark = highWaterMark;
            _maxCapacity = maxCapacity;
            _idleFrames = idleFrames;
            _poolArrayLength = poolArrayLength;
        }

        public Type Type => _type;

        public int UnusedCount => _unusedCount;

        public int UsingCount => _usingCount;

        public int AcquireCount => _acquireCount;

        public int ReleaseCount => _releaseCount;

        public int CreateCount => _createCount;

        public int HighWaterMark => _highWaterMark;

        public int MaxCapacity => _maxCapacity;

        public int IdleFrames => _idleFrames;

        public int PoolArrayLength => _poolArrayLength;

        internal void Set(Type type, int unusedCount, int usingCount,
            int acquireCount, int releaseCount, int createCount,
            int highWaterMark, int maxCapacity,
            int idleFrames, int poolArrayLength)
        {
            _type = type;
            _unusedCount = unusedCount;
            _usingCount = usingCount;
            _acquireCount = acquireCount;
            _releaseCount = releaseCount;
            _createCount = createCount;
            _highWaterMark = highWaterMark;
            _maxCapacity = maxCapacity;
            _idleFrames = idleFrames;
            _poolArrayLength = poolArrayLength;
        }
    }
}
