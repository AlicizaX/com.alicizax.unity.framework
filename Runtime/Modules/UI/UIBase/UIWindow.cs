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

        internal sealed override void DestroySelf() => Service.DestroyWindow(this);

        private protected sealed override void ReleaseClosed(bool destroy) => Service.ReleaseClosedWindow(this, destroy);

        private protected sealed override void BindVisuals(UIHolderObjectBase holder)
        {
            RectTransform rect = holder.RectTransform;
            rect.localPosition = Vector3.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            if (!holder.TryGetComponent(out _canvas)) _canvas = holder.gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = Depth;
            if (!holder.TryGetComponent(out _raycaster)) _raycaster = holder.gameObject.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = false;
            // 初始化阶段只用层隐藏、保持 Canvas 启用，OnInitialize 里仍可读取布局
            holder.gameObject.layer = UIComponent.UIHideLayer;
        }

        private protected sealed override void ApplyVisible(bool visible)
        {
            Holder.gameObject.layer = visible ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
            _canvas.enabled = visible;
        }

        private protected sealed override void SetInteractable(bool interactable) => _raycaster.enabled = interactable;
    }

    public abstract class UIWindow<T> : UIWindow where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
    }
}
