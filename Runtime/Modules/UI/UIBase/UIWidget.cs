using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase Parent;
        private CanvasGroup _canvasGroup;
        private float _shownAlpha = 1f;
        private CancellationTokenSource _loadCts;

        internal override bool ParentEffective => Parent.Effective;
        public override bool IsVisible => Visible && Parent.IsVisible;

        /// <summary>本 Widget 资源加载的取消源：由 Widget 自身拥有，创建期间有效，加载结束或销毁时释放。</summary>
        internal CancellationToken LoadToken => (_loadCts ??= new CancellationTokenSource()).Token;

        internal static readonly Action<object> CancelLoadCallback = static state => ((UIWidget)state).CancelLoad();

        /// <summary>请求取消加载；源由 EndLoad 释放，避免加载方仍在观察 token 时被提前 Dispose。</summary>
        internal void CancelLoad()
        {
            _loadCts?.Cancel();
        }

        internal void EndLoad()
        {
            CancellationTokenSource cancellation = _loadCts;
            if (cancellation == null) return;
            _loadCts = null;
            cancellation.Dispose();
        }

        public void Open(params object[] userDatas) => InternalOpen(userDatas);

        public void Close()
        {
            if (DestroyRequested || State == UIState.Initialized) return;
            InternalClose().Forget();
        }

        public UniTask Destroy() => InternalDestroy();
        internal override void OnDestroyed() => Parent.DetachWidget(this);
        internal override void OnDestroyStarted() => CancelLoad();

        private protected sealed override void ApplyVisible(bool value) =>
            _canvasGroup.alpha = value ? _shownAlpha : 0f;

        private protected override void SetInteractable(bool value)
        {
            _canvasGroup.blocksRaycasts = value;
            _canvasGroup.interactable = value;
        }

        private protected sealed override void ReleaseVisuals()
        {
            CancelLoad();
            _canvasGroup = null;
        }

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
