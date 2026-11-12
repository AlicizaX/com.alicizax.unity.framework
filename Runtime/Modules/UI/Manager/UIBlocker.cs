using AlicizaX.Timer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 全屏输入遮挡层：显示时拦截点击，倒计时结束或强制退出时隐藏。
    /// 随 <see cref="UIService"/> 构造而创建、随其释放而停用。
    /// </summary>
    internal sealed class UIBlocker
    {
        private const int SortingOrder = short.MaxValue;

        private readonly ITimerService _timers;
        private readonly GameObject _root;
        private readonly TimerHandlerNoArgs _onExpired;
        private ulong _timer;

        internal UIBlocker(ITimerService timers, Transform canvasRoot)
        {
            _timers = timers;
            _onExpired = OnExpired;
            _root = new GameObject("LayerBlock");
            var rect = _root.AddComponent<RectTransform>();
            rect.SetParent(canvasRoot);
            rect.SetAsLastSibling();
            // 嵌套 Canvas：渲染模式与相机沿用根 Canvas，只需要自己的排序
            Canvas blockCanvas = _root.AddComponent<Canvas>();
            blockCanvas.overrideSorting = true;
            blockCanvas.sortingOrder = SortingOrder;
            _root.AddComponent<GraphicRaycaster>();
            _root.AddComponent<UIBlock>();
            rect.ResetToFullScreen();
            _root.SetActive(false);
        }

        internal bool IsActive => _root.activeSelf;

        internal float Remaining => _timer == 0 ? 0f : _timers.GetLeftTime(_timer);

        internal void Show(float duration)
        {
            RemoveTimer();
            _root.SetActive(true);
            _timer = _timers.AddTimer(_onExpired, duration, isUnscaled: true);
        }

        internal void Hide()
        {
            RemoveTimer();
            _root.SetActive(false);
        }

        /// <summary>节点挂在 UI 根节点下，随根节点一起销毁；这里只撤掉定时器。</summary>
        internal void Dispose() => RemoveTimer();

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
