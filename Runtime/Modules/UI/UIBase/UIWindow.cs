using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWindow : UIBase
    {
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private int _layerIndex = -1;
        internal ulong CacheTimer;

        protected UIViewTransition CloseSelf(bool force = false, bool snap = false) =>
            Service.CloseWindow(this, force, snap);

        internal int Depth => (int)MetaInfo.Layer * UIService.LAYER_DEEP + _layerIndex * UIService.WINDOW_DEEP;

        /// <summary>层级内的位置，-1 表示不在层级中。节点尚未绑定时只记录，绑定时一次应用。</summary>
        internal int LayerIndex
        {
            get => _layerIndex;
            set
            {
                if (_layerIndex == value) return;
                _layerIndex = value;
                if (value >= 0 && _canvas != null) _canvas.sortingOrder = Depth;
            }
        }

        internal sealed override void DestroySelf() => Service.DestroyLostWindow(this);

        private protected sealed override void ReleaseClosed(bool destroy) => Service.ReleaseClosedWindow(this, destroy);

        private protected sealed override void BindVisuals(UIHolderObjectBase holder)
        {
            holder.RectTransform.ResetToFullScreen();
            // 根节点统一到 UI 层：UI 相机按层裁剪，显隐只靠 Canvas 开关
            holder.gameObject.layer = UIComponent.UIShowLayer;
            if (!holder.TryGetComponent(out _canvas)) _canvas = holder.gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = Depth;
            if (!holder.TryGetComponent(out _raycaster)) _raycaster = holder.gameObject.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = false;
        }

        private protected sealed override void ApplyVisible(bool visible) => _canvas.enabled = visible;

        private protected sealed override void SetInteractable(bool interactable) => _raycaster.enabled = interactable;
    }

    public abstract class UIWindow<T> : UIWindow where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
    }
}
