#if UNITY_EDITOR
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        int IUIDebugService.LayerCount => _openUI.Length;
        int IUIDebugService.CacheWindowCount => _cache.Count;

        void IUIDebugService.FillServiceDebugInfo(UIServiceDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            int openWindowCount = 0;
            for (int i = 0; i < _openUI.Length; i++)
            {
                List<UIWindowRecord> windows = _openUI[i];
                if (windows == null)
                {
                    continue;
                }

                openWindowCount += windows.Count;
            }
            info.UpdateCount = _updating.Count;
            info.Initialized = _initialized;
            info.Orthographic = UICamera != null && UICamera.orthographic;
            info.OpenWindowCount = openWindowCount;
            info.CacheWindowCount = _cache.Count;
            info.BlockActive = _blocker != null && _blocker.IsActive;
            info.BlockRemaining = _blocker != null ? _blocker.Remaining : 0f;
            info.Camera = UICamera;
            info.Canvas = UICanvas;
            info.Root = UIRoot;
        }

        bool IUIDebugService.FillLayerDebugInfo(int layerIndex, UILayerDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_openUI.Length)
            {
                return false;
            }

            List<UIWindowRecord> windows = _openUI[layerIndex];
            if (windows == null)
            {
                info.Clear();
                info.Layer = (UILayer)layerIndex;
                return false;
            }

            info.Layer = (UILayer)layerIndex;
            info.WindowCount = windows.Count;
            return true;
        }

        bool IUIDebugService.FillWindowDebugInfo(int layerIndex, int windowIndex, UIWindowDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_openUI.Length)
            {
                return false;
            }

            List<UIWindowRecord> windows = _openUI[layerIndex];
            if (windows == null || (uint)windowIndex >= (uint)windows.Count)
            {
                info.Clear();
                return false;
            }

            FillWindowDebugInfo(windows[windowIndex], layerIndex, windowIndex, info);
            return true;
        }

        int IUIDebugService.FillCacheDebugInfo(UIWindowDebugInfo[] infos, int capacity)
        {
            if (infos == null || capacity <= 0)
            {
                return 0;
            }

            int index = 0;
            for (int i = 0; i < _cache.Count; i++)
            {
                if (index >= capacity || index >= infos.Length)
                {
                    break;
                }

                UIWindowDebugInfo info = infos[index];
                if (info != null)
                {
                    UIWindowRecord record = _cache.Get(i);
                    FillWindowDebugInfo(record, record.MetaInfo.UILayer, index, info);
                }

                index++;
            }

            return index;
        }

        private void FillWindowDebugInfo(UIWindowRecord record, int layerIndex, int orderIndex, UIWindowDebugInfo info)
        {
            if (record == null)
            {
                info.Clear();
                return;
            }

            UIWindow view = record.View;
            UIHolderObjectBase holder = view?.Holder;
            info.LayerIndex = layerIndex;
            info.OrderIndex = orderIndex;
            info.LogicTypeName = record.Metadata.UILogicTypeName;
            info.HolderTypeName = record.Metadata.UIHolderTypeName;
            info.State = record.State;
            info.Visible = view != null && view.Visible;
            info.Processing = record.Flight != null || record.State == UIState.Opening || record.State == UIState.Closing;
            info.Updating = view != null && _updating.Contains(view);
            info.Depth = view != null ? view.Depth : 0;
            info.CacheTime = record.MetaInfo.CacheTime;
            info.CacheRemaining = GetTimerRemaining(record.CacheTimer);
            info.HolderTransform = holder != null ? holder.transform : null;
        }

        private float GetTimerRemaining(ulong timerHandle)
        {
            if (timerHandle == 0UL || _timerService == null)
            {
                return 0f;
            }

            return _timerService.GetLeftTime(timerHandle);
        }
    }
}
#endif
