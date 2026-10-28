using System;
using System.Collections.Generic;
using AlicizaX.Timer.Runtime;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 窗口缓存：负责关闭后实例的挂载、缓存计时与释放。
    /// 生命周期由 <see cref="UIService.Initialize"/> 创建、<see cref="UIService.DestroyAllManagedUI"/> 释放。
    /// </summary>
    internal sealed class UIWindowCache
    {
        private readonly List<UIWindowRecord> _records = new(8);
        private readonly ITimerService _timers;
        private readonly Transform _layer;
        private Action<UIWindowRecord> _onTimerExpired;

        internal UIWindowCache(ITimerService timers, Transform layer)
        {
            _timers = timers;
            _layer = layer;
        }

        internal int Count => _records.Count;

        internal UIWindowRecord Get(int index) => _records[index];

        internal void Add(UIWindowRecord record)
        {
            UIWindow view = record.View;
            if (view == null || record.CacheIndex >= 0) return;

            view.ClearUserData();
            view.SetCanvasEnabled(false);
            view.Holder.transform.SetParent(_layer, false);

            if (record.View != view)
            {
                // SetParent 期间被重入替换：旧实例已无所有者，销毁后不留半缓存状态。
                view.DestroyNow();
                return;
            }
            // SetParent 回调可能重入完成一次关闭与缓存（CacheIndex 已生效），或重入重新打开窗口。
            if (view.State != UIState.Closed || record.CacheIndex >= 0) return;

            record.CacheIndex = _records.Count;
            _records.Add(record);
            if (record.MetaInfo.CacheTime < 0) return;
            _onTimerExpired ??= OnTimerExpired;
            record.CacheTimer = _timers.AddTimer(_onTimerExpired, record,
                record.MetaInfo.CacheTime, isLoop: false, isUnscaled: true);
            if (record.CacheTimer != 0) return;

            Remove(record);
            view.DestroyNow();
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

        /// <summary>服务销毁：缓存实例由 UIService 统一销毁，这里只解除记录与计时器。</summary>
        internal void Clear()
        {
            for (int i = 0; i < _records.Count; i++)
            {
                UIWindowRecord record = _records[i];
                record.CacheIndex = -1;
                ulong timer = record.CacheTimer;
                record.CacheTimer = 0;
                if (timer != 0) _timers.RemoveTimer(timer);
            }
            _records.Clear();
        }

        private void OnTimerExpired(UIWindowRecord record)
        {
            if (record.CacheIndex < 0) return;
            record.CacheTimer = 0;
            Remove(record);
            record.View?.DestroyNow();
        }
    }
}
