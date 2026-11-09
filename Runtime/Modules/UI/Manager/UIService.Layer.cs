using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        internal const int WindowsPerLayerLimit = LAYER_DEEP / WINDOW_DEEP;

        private readonly List<UIWindow>[] _layerWindows = CreateLayerWindows();

        private static List<UIWindow>[] CreateLayerWindows()
        {
            var layers = new List<UIWindow>[(int)UILayer.All];
            for (int i = 0; i < layers.Length; i++) layers[i] = new List<UIWindow>(8);
            return layers;
        }

        private void BringToFront(UIWindow window)
        {
            List<UIWindow> windows = _layerWindows[(int)window.MetaInfo.Layer];
            int index = window.LayerIndex;
            if (index >= 0 && index == windows.Count - 1) return;
            if (index >= 0) windows.RemoveAt(index);
            windows.Add(window);
            Debug.Assert(windows.Count <= WindowsPerLayerLimit,
                "[UI] Too many windows in one layer: canvas sorting order overlaps the next layer.");
            AssignLayerIndices(windows, index >= 0 ? index : windows.Count - 1);
        }

        private void RemoveFromLayer(UIWindow window)
        {
            int index = window.LayerIndex;
            if (index < 0) return;
            List<UIWindow> windows = _layerWindows[(int)window.MetaInfo.Layer];
            windows.RemoveAt(index);
            window.LayerIndex = -1;
            AssignLayerIndices(windows, index);
        }

        private static void AssignLayerIndices(List<UIWindow> windows, int from)
        {
            for (int i = from; i < windows.Count; i++) windows[i].LayerIndex = i;
        }

        /// <summary>从最高层开始，关闭第一个逻辑打开且满足谓词的窗口。</summary>
        public async UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false)
        {
            if (predicate == null) return false;
            for (int layer = _layerWindows.Length - 1; layer >= 0; layer--)
            {
                List<UIWindow> windows = _layerWindows[layer];
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    UIWindow window = windows[i];
                    if (window.State is not (UIState.Opening or UIState.Opened) || !predicate(window.MetaInfo.Handle)) continue;
                    await CloseWindow(window, force, snap: false).AwaitTransition();
                    return true;
                }
            }
            return false;
        }

        /// <summary>从最高层开始，找第一个逻辑打开（不含正在关闭）且满足谓词的窗口 Holder。</summary>
        public bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder)
        {
            for (int layer = _layerWindows.Length - 1; layer >= 0; layer--)
            {
                List<UIWindow> windows = _layerWindows[layer];
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    UIWindow window = windows[i];
                    if (window.State is not (UIState.Opening or UIState.Opened)) continue;
                    UIHolderObjectBase candidate = window.Holder;
                    if (predicate != null && !predicate(candidate)) continue;
                    holder = candidate;
                    return true;
                }
            }
            holder = null;
            return false;
        }
    }
}
