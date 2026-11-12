#if UNITY_EDITOR
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        int IUIDebugService.LayerCount => _layerWindows.Length;
        int IUIDebugService.CacheWindowCount => CountCachedWindows();
        int IUIDebugService.HistoryCount => _navigator.History.Count;

        void IUIDebugService.FillServiceDebugInfo(UIServiceDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            int openWindowCount = 0;
            for (int i = 0; i < _layerWindows.Length; i++)
            {
                openWindowCount += _layerWindows[i].Count;
            }
            info.UpdateCount = _updateViews.Count;
            info.Initialized = !_stopped;
            info.Orthographic = UICamera != null && UICamera.orthographic;
            info.OpenWindowCount = openWindowCount;
            info.CacheWindowCount = CountCachedWindows();
            info.BlockActive = _blocker.IsActive;
            info.BlockRemaining = _blocker.Remaining;
            info.Camera = UICamera;
            info.Canvas = UICanvas;
            info.Root = UIRoot;
        }

        bool IUIDebugService.FillLayerDebugInfo(int layerIndex, UILayerDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_layerWindows.Length)
            {
                return false;
            }

            info.Layer = (UILayer)layerIndex;
            info.WindowCount = _layerWindows[layerIndex].Count;
            return true;
        }

        bool IUIDebugService.FillWindowDebugInfo(int layerIndex, int windowIndex, UIWindowDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_layerWindows.Length)
            {
                return false;
            }

            List<UIWindow> windows = _layerWindows[layerIndex];
            if ((uint)windowIndex >= (uint)windows.Count)
            {
                info.Clear();
                return false;
            }

            FillWindowDebugInfo(windows[windowIndex], layerIndex, windowIndex, info);
            return true;
        }

        int IUIDebugService.FillCacheDebugInfo(UIWindowDebugInfo[] infos)
        {
            if (infos == null)
            {
                return 0;
            }

            int index = 0;
            foreach (UIWindow window in _windows.Values)
            {
                if (!IsCached(window))
                {
                    continue;
                }

                if (index >= infos.Length)
                {
                    break;
                }

                UIWindowDebugInfo info = infos[index];
                if (info != null)
                {
                    FillWindowDebugInfo(window, (int)window.MetaInfo.Layer, index, info);
                }

                index++;
            }

            return index;
        }

        private int CountCachedWindows()
        {
            int count = 0;
            foreach (UIWindow window in _windows.Values)
            {
                if (IsCached(window)) count++;
            }

            return count;
        }

        private static bool IsCached(UIWindow window) => window.State == UIState.Closed && window.LayerIndex < 0;

        bool IUIDebugService.FillHistoryInfo(int index, UIRouteDebugInfo info)
        {
            List<UIRouteEntry> history = _navigator.History;
            if (info == null || (uint)index >= (uint)history.Count) return false;
            UIRouteEntry entry = history[index];
            info.Clear();
            info.Index = index;
            info.UITypeName = entry.UIType.Name;
            info.IsRoot = index == 0;
            info.ArgsPreview = entry.UserData?.ToString() ?? "null";
            return true;
        }

        private void FillWindowDebugInfo(UIWindow window, int layerIndex, int orderIndex, UIWindowDebugInfo info)
        {
            UIHolderObjectBase holder = window.Holder;
            info.LayerIndex = layerIndex;
            info.OrderIndex = orderIndex;
            info.LogicTypeName = window.MetaInfo.Name;
            info.HolderTypeName = window.MetaInfo.HolderType.Name;
            info.State = window.State;
            info.Visible = window.Visible;
            info.Processing = window.State is UIState.Loading or UIState.Opening or UIState.Closing;
            info.Updating = window.MetaInfo.HasUpdate && window.IsEnabled;
            info.Depth = window.Depth;
            info.CacheTime = window.MetaInfo.CacheTime;
            info.CacheRemaining = window.CacheTimer == 0UL ? 0f : _timerService.GetLeftTime(window.CacheTimer);
            info.HolderTransform = holder != null ? holder.transform : null;
        }
    }
}
#endif
