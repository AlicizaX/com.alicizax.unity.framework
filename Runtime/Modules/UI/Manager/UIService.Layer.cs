using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private readonly List<UIWindow>[] _layerWindows = CreateLayerWindows();

        private static List<UIWindow>[] CreateLayerWindows()
        {
            var layers = new List<UIWindow>[LayerCount];
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

        public async UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false)
        {
            UIWindow window = FindTopOpen(predicate, static window => window.MetaInfo.Handle);
            if (window == null) return false;
            await CloseWindow(window, force, snap: false).AwaitTransition();
            return true;
        }

        public bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder)
        {
            UIWindow window = FindTopOpen(predicate, static window => window.Holder);
            holder = window?.Holder;
            return window != null;
        }

        /// <summary>
        /// 从最高层开始，找第一个逻辑打开（不含正在关闭）且满足谓词的窗口；predicate 为 null 表示不过滤。
        /// key 取出谓词要判断的值，用静态委托传入，不分配。
        /// </summary>
        private UIWindow FindTopOpen<TKey>(Predicate<TKey> predicate, Func<UIWindow, TKey> key)
        {
            for (int layer = _layerWindows.Length - 1; layer >= 0; layer--)
            {
                List<UIWindow> windows = _layerWindows[layer];
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    UIWindow window = windows[i];
                    if (window.State is not (UIState.Opening or UIState.Opened)) continue;
                    if (predicate == null || predicate(key(window))) return window;
                }
            }
            return null;
        }
    }
}
