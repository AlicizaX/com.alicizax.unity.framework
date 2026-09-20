using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase Parent;
        private CanvasGroup _canvasGroup;
        private float _shownAlpha = 1f;
        internal override bool ParentEffective => Parent.Effective;
        public override bool IsVisible => Visible && Parent.IsVisible;

        public void Open(params object[] userDatas) => InternalOpen(userDatas);

        public void Close()
        {
            if (DestroyRequested || State == UIState.Initialized) return;
            InternalClose().Forget();
        }

        public UniTask Destroy() => InternalDestroy();
        internal override void OnDestroyed() => Parent.DetachWidget(this);

        private protected sealed override void ApplyVisible(bool value)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = value ? _shownAlpha : 0f;
        }

        private protected override void SetInteractable(bool value)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.blocksRaycasts = value;
            _canvasGroup.interactable = value;
        }

        private protected sealed override void ReleaseVisuals() => _canvasGroup = null;

        internal void BindWidgetHolder(UIHolderObjectBase holder)
        {
            BindHolderCommon(holder);
            _canvasGroup = holder.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = holder.gameObject.AddComponent<CanvasGroup>();
            if (_canvasGroup.alpha > 0f) _shownAlpha = _canvasGroup.alpha;
            if (Holder.HasTransition) Holder.ApplyTransitionState(false);
        }
    }

    public abstract class UIWidget<T> : UIWidget where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed override Type UIHolderType => typeof(T);
        internal sealed override void BindUIHolder(UIHolderObjectBase holder) => BindWidgetHolder(holder);
    }
}
