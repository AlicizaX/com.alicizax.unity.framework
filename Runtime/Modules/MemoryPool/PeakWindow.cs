namespace AlicizaX
{
    internal unsafe struct PeakWindow
    {
        private const int BucketCount = 8;
        private const int BucketMask = BucketCount - 1;

        private fixed int _bucketPeaks[BucketCount];
        private int _cursor;
        private float _bucketEndTime;

        public int Current;
        public int Max;

        public int Peak => Current > Max ? Current : Max;

        public void Advance(float now, int liveCount, float windowSeconds)
        {
            if (now < _bucketEndTime)
                return;

            float bucketSeconds = windowSeconds / BucketCount;
            float elapsedBuckets = (now - _bucketEndTime) / bucketSeconds;
            int closedBuckets = elapsedBuckets >= BucketCount - 1 ? BucketCount : 1 + (int)elapsedBuckets;
            for (int i = 0; i < closedBuckets; i++)
            {
                _bucketPeaks[_cursor] = Current;
                _cursor = (_cursor + 1) & BucketMask;
            }

            Current = liveCount;
            int max = 0;
            for (int i = 0; i < BucketCount; i++)
            {
                if (_bucketPeaks[i] > max)
                    max = _bucketPeaks[i];
            }

            Max = max;
            _bucketEndTime = now + bucketSeconds;
        }
    }
}
