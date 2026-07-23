#if UNITY_EDITOR
namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        int IUIDebugService.LayerCount => _openUI.Length;
        int IUIDebugService.CacheWindowCount => m_CacheWindowCount;

        void IUIDebugService.FillServiceDebugInfo(UIServiceDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            int openWindowCount = 0;
            int updateWindowCount = 0;
            for (int i = 0; i < _openUI.Length; i++)
            {
                LayerData layer = _openUI[i];
                if (layer == null)
                {
                    continue;
                }

                int count = layer.Count;
                openWindowCount += count;
                for (int j = 0; j < count; j++)
                {
                    UIMetadata metadata = layer.Items[j];
                    if (metadata != null && metadata.MetaInfo.NeedUpdate)
                    {
                        updateWindowCount++;
                    }
                }
            }

            info.Initialized = UIRoot != null && UICanvas != null;
            info.Orthographic = _isOrthographic;
            info.LayerCount = _openUI.Length;
            info.OpenWindowCount = openWindowCount;
            info.CacheWindowCount = m_CacheWindowCount;
            info.UpdateWindowCount = updateWindowCount;
            info.BlockTimerHandle = m_LastCountDownHandle;
            info.BlockActive = m_LayerBlock != null && m_LayerBlock.activeSelf;
            info.Camera = UICamera;
            info.Canvas = UICanvas;
            info.Root = UIRoot;
            info.CanvasRoot = UICanvasRoot;
        }

        bool IUIDebugService.FillLayerDebugInfo(int layerIndex, UILayerDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_openUI.Length)
            {
                return false;
            }

            LayerData layer = _openUI[layerIndex];
            if (layer == null)
            {
                info.Clear();
                info.LayerIndex = layerIndex;
                info.Layer = (UILayer)layerIndex;
                return false;
            }

            info.LayerIndex = layerIndex;
            info.Layer = (UILayer)layerIndex;
            info.WindowCount = layer.Count;
            info.RectTransform = m_AllWindowLayer[layerIndex];
            return true;
        }

        bool IUIDebugService.FillWindowDebugInfo(int layerIndex, int windowIndex, UIWindowDebugInfo info)
        {
            if (info == null || (uint)layerIndex >= (uint)_openUI.Length)
            {
                return false;
            }

            LayerData layer = _openUI[layerIndex];
            if (layer == null || (uint)windowIndex >= (uint)layer.Count)
            {
                info?.Clear();
                return false;
            }

            FillWindowDebugInfo(layer.Items[windowIndex], layerIndex, windowIndex, 0UL, info);
            return true;
        }

        int IUIDebugService.FillCacheDebugInfo(UIWindowDebugInfo[] infos, int capacity)
        {
            if (infos == null || capacity <= 0)
            {
                return 0;
            }

            int index = 0;
            for (int i = 0; i < m_CacheWindowCount; i++)
            {
                if (index >= capacity || index >= infos.Length)
                {
                    break;
                }

                UIWindowDebugInfo info = infos[index];
                if (info != null)
                {
                    CacheEntry entry = m_CacheWindow[i];
                    FillWindowDebugInfo(entry.Metadata, entry.Metadata.MetaInfo.UILayer, index, entry.TimerHandle, info);
                }

                index++;
            }

            return index;
        }

        private static void FillWindowDebugInfo(UIMetadata metadata, int layerIndex, int orderIndex, ulong timerHandle, UIWindowDebugInfo info)
        {
            if (metadata == null)
            {
                info.Clear();
                return;
            }

            UIBase view = metadata.View;
            UIHolderObjectBase holder = view?.Holder;
            info.LayerIndex = layerIndex;
            info.OrderIndex = orderIndex;
            info.RuntimeTypeHandle = metadata.MetaInfo.RuntimeTypeHandle;
            info.LogicTypeName = metadata.UILogicTypeName;
            info.HolderTypeName = metadata.UIHolderTypeName;
            info.State = metadata.State;
            info.Visible = view != null && view.Visible;
            info.InCache = metadata.InCache;
            info.NeedUpdate = metadata.MetaInfo.NeedUpdate;
            info.ShowInProgress = metadata.ShowInProgress;
            info.CloseInProgress = metadata.CloseInProgress;
            info.Depth = view != null ? view.Depth : 0;
            info.CacheTime = metadata.MetaInfo.CacheTime;
            info.CacheTimerHandle = timerHandle;
            info.HolderTransform = holder != null ? holder.transform : null;
            info.StateDuration = view != null ? view.StateDuration : 0f;
        }
    }
}
#endif
