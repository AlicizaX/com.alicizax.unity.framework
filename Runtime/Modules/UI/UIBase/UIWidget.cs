using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase Parent;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private CanvasGroup _canvasGroup;
        private float _shownAlpha = 1f;

        private protected override bool ParentEnabled => Parent.IsEnabled;

        public void Open(object userData = null) => OpenView(userData);

        public void Close() => CloseView(false, false);

        /// <summary>播放关闭转场后销毁。</summary>
        public UIViewTransition Destroy()
        {
            CloseView(false, true);
            if (State == UIState.Closed) Parent.DestroyWidget(this);
            return new UIViewTransition(this);
        }

        internal sealed override void DestroySelf() => Parent.DestroyWidget(this);

        private protected sealed override void ReleaseClosed(bool destroy)
        {
            if (destroy) Parent.DestroyWidget(this);
        }

        private protected sealed override void BindVisuals(UIHolderObjectBase holder)
        {
            if (holder.TryGetComponent(out _canvas))
            {
                holder.TryGetComponent(out _raycaster);
            }
            else
            {
                if (!holder.TryGetComponent(out _canvasGroup)) _canvasGroup = holder.gameObject.AddComponent<CanvasGroup>();
                if (_canvasGroup.alpha > 0f) _shownAlpha = _canvasGroup.alpha;
            }
            SetInteractable(false);
        }

        private protected sealed override void ApplyVisible(bool visible)
        {
            if (_canvas != null) _canvas.enabled = visible;
            else _canvasGroup.alpha = visible ? _shownAlpha : 0f;
        }

        private protected sealed override void SetInteractable(bool interactable)
        {
            if (_canvas != null)
            {
                if (_raycaster != null) _raycaster.enabled = interactable;
                return;
            }
            _canvasGroup.blocksRaycasts = interactable;
            _canvasGroup.interactable = interactable;
        }
    }

    public abstract class UIWidget<T> : UIWidget where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
    }
}
