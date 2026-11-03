using System;
using System.Collections.Generic;
using AlicizaX.Timer.Runtime;

namespace AlicizaX.UI.Runtime
{
    internal sealed class UIWindowCache
    {
        private readonly List<UIWindowRecord> _records = new(8);
        private readonly ITimerService _timers;
        private readonly Action<UIWindowRecord> _onTimerExpired;

        internal UIWindowCache(ITimerService timers)
        {
            _timers = timers;
            _onTimerExpired = OnTimerExpired;
        }

        internal int Count => _records.Count;

        internal UIWindowRecord Get(int index) => _records[index];

        internal void Add(UIWindowRecord record)
        {
            record.View.ClearUserData();
            record.CacheIndex = _records.Count;
            _records.Add(record);
            int cacheTime = record.MetaInfo.CacheTime;
            if (cacheTime <= 0) return;
            record.CacheTimer = _timers.AddTimer(_onTimerExpired, record, cacheTime, isLoop: false, isUnscaled: true);
            if (record.CacheTimer == 0) record.View.DestroyNow();
        }

        internal void Remove(UIWindowRecord record)
        {
            int index = record.CacheIndex;
            if (index < 0) return;
            int lastIndex = _records.Count - 1;
            UIWindowRecord last = _records[lastIndex];
            _records[index] = last;
            last.CacheIndex = index;
            _records.RemoveAt(lastIndex);
            record.CacheIndex = -1;
            ulong timer = record.CacheTimer;
            record.CacheTimer = 0;
            if (timer != 0) _timers.RemoveTimer(timer);
        }

        private void OnTimerExpired(UIWindowRecord record)
        {
            record.CacheTimer = 0;
            record.View.DestroyNow();
        }
    }
}
