#if UNITY_EDITOR
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        int IUIDebugService.LayerCount => _layerStacks.Length;
        int IUIDebugService.CacheWindowCount => _cache.Count;

        void IUIDebugService.FillServiceDebugInfo(UIServiceDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            int openWindowCount = 0;
            for (int i = 0; i < _layerStacks.Length; i++)
            {
                openWindowCount += _layerStacks[i].Count;
            }
            info.UpdateCount = _updateViews.Count;
            info.Initialized = _lifetime == UILifetime.Running;
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
            if (info == null || (uint)layerIndex >= (uint)_layerStacks.Length)
            {
                return false;
            }

            info.Layer = (UILayer)layerIndex;
            info.WindowCount = _layerStacks[layerIndex].Count;
            return true;
        }

        bool IUIDebugService.FillWindowDebugInfo(int layerIndex, int windowIndex, UIWindowDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_layerStacks.Length)
            {
                return false;
            }

            UIWindowRecord record = _layerStacks[layerIndex].Bottom;
            for (int i = 0; record != null && i < windowIndex; i++) record = record.StackAbove;
            if (windowIndex < 0 || record == null)
            {
                info.Clear();
                return false;
            }

            FillWindowDebugInfo(record, layerIndex, windowIndex, info);
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
            info.Updating = view != null && view.IsUpdating;
            info.Depth = view != null ? view.Depth : 0;
            info.CacheTime = record.MetaInfo.CacheTime;
            info.CacheRemaining = GetTimerRemaining(record.CacheTimer);
            info.HolderTransform = holder != null ? holder.transform : null;
        }

        internal UIWindowRecord FindRecord(System.RuntimeTypeHandle handle) => TryGetWindowRecord(handle);
        internal UITickList UpdateViews => _updateViews;
        internal UITickList TransitionViews => _transitionViews;
        internal UILayerStack LayerStack(int layer) => _layerStacks[layer];
        internal uint CurrentTick => _tick;
        internal Dictionary<System.RuntimeTypeHandle, UIWindowRecord>.ValueCollection Records => _windows.Values;

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
