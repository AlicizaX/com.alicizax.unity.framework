using AlicizaX.Timer.Runtime;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 全屏输入遮挡层：显示时拦截点击，倒计时结束或强制退出时隐藏。
    /// 生命周期由 <see cref="UIService.Initialize"/> 创建、<see cref="UIService.DestroyAllManagedUI"/> 释放。
    /// </summary>
    internal sealed class UIBlocker
    {
        private const int SortingOrder = short.MaxValue;

        private readonly ITimerService _timers;
        private readonly GameObject _root;
        private readonly TimerHandlerNoArgs _onExpired;
        private ulong _timer;

        internal UIBlocker(ITimerService timers, Canvas canvas, Camera camera, Transform canvasRoot)
        {
            _timers = timers;
            _onExpired = OnExpired;
            _root = new GameObject("LayerBlock");
            var rect = _root.AddComponent<RectTransform>();
            rect.SetParent(canvasRoot);
            rect.SetAsLastSibling();
            Canvas blockCanvas = _root.AddComponent<Canvas>();
            blockCanvas.renderMode = canvas.renderMode;
            blockCanvas.worldCamera = camera;
            blockCanvas.planeDistance = canvas.planeDistance;
            blockCanvas.overrideSorting = true;
            blockCanvas.sortingOrder = SortingOrder;
            _root.AddComponent<GraphicRaycaster>();
            _root.AddComponent<UIBlock>();
            rect.ResetToFullScreen();
            _root.SetActive(false);
        }

        internal bool IsActive => _root != null && _root.activeSelf;

        internal float Remaining => _timer == 0 ? 0f : _timers.GetLeftTime(_timer);

        internal void Show(float duration)
        {
            RemoveTimer();
            _root.SetActive(true);
            _timer = _timers.AddTimer(_onExpired, duration);
            if (_timer == 0) _root.SetActive(false);
        }

        internal void Hide()
        {
            RemoveTimer();
            _root.SetActive(false);
        }

        internal void Dispose()
        {
            RemoveTimer();
            if (_root == null) return;
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
        }

        private void OnExpired()
        {
            _timer = 0;
            _root.SetActive(false);
        }

        private void RemoveTimer()
        {
            if (_timer == 0) return;
            _timers.RemoveTimer(_timer);
            _timer = 0;
        }
    }
}
