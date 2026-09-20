using System;
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private readonly List<UIWindowRecord> _cached = new(8);
        private Action<UIWindowRecord> _onTimerDisposeWindow;

        private void CacheWindow(UIWindowRecord record)
        {
            UIWindow view = record.View;
            if (view == null || view.State == UIState.Destroying || view.State == UIState.Destroyed) return;
            if (record.ForceClose || view.DestroyRequested || record.MetaInfo.CacheTime == 0)
            {
                view.DestroyNow();
                return;
            }

            if (view.Holder == null || !view.Holder.IsValid())
            {
                Log.Error("[UI] Invalid holder while caching {0}. Destroying.", record.Metadata.UILogicTypeName);
                view.DestroyNow();
                return;
            }

            view.ClearUserData();
            view.SetCanvasEnabled(false);
            view.Holder.transform.SetParent(UICacheLayer, false);
            if (record.View != view || record.CacheIndex >= 0) return;
            record.CacheIndex = _cached.Count;
            _cached.Add(record);
            if (record.MetaInfo.CacheTime < 0) return;
            _onTimerDisposeWindow ??= OnTimerDisposeWindow;
            record.CacheTimer = _timerService.AddTimer(_onTimerDisposeWindow, record,
                record.MetaInfo.CacheTime, isLoop: false, isUnscaled: true);
            if (record.CacheTimer == 0)
            {
                RemoveFromCache(record);
                view.DestroyNow();
            }
        }

        private void OnTimerDisposeWindow(UIWindowRecord record)
        {
            if (record.CacheIndex < 0 || record.View == null || record.View.State != UIState.Closed) return;
            record.CacheTimer = 0;
            RemoveFromCache(record);
            record.View.DestroyNow();
        }

        private void RemoveFromCache(UIWindowRecord record)
        {
            int index = record.CacheIndex;
            if (index < 0) return;
            int lastIndex = _cached.Count - 1;
            UIWindowRecord last = _cached[lastIndex];
            _cached[index] = last;
            last.CacheIndex = index;
            _cached.RemoveAt(lastIndex);
            record.CacheIndex = -1;
            ulong timer = record.CacheTimer;
            record.CacheTimer = 0;
            if (timer != 0) _timerService.RemoveTimer(timer);
        }
    }
}
