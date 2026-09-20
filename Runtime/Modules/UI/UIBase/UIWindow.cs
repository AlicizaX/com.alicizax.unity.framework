using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWindow : UIBase
    {
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;

        protected UniTask CloseSelf(bool force = false, bool skipTransition = false) =>
            Service.CloseWindow(this, force, skipTransition);

        internal override void OnClosed() => Service.NotifyWindowClosed(this);
        internal override void OnDestroyStarted() => Service.NotifyWindowDestroying(this);
        internal override void OnActivityChanged() => Service.SetUpdating(this, Effective && State == UIState.Opened);

        internal Canvas Canvas => _canvas;
        internal int Depth
        {
            get => _canvas != null ? _canvas.sortingOrder : 0;
            set { if (_canvas != null) _canvas.sortingOrder = value; }
        }

        internal void SetCanvasEnabled(bool value) => _canvas.enabled = value;
        private protected override void ApplyVisible(bool value) =>
            Holder.gameObject.layer = value ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
        private protected override void SetInteractable(bool value)
        {
            if (_raycaster != null) _raycaster.enabled = value;
        }

        private protected override void ReleaseVisuals()
        {
            _canvas = null;
            _raycaster = null;
        }

        internal void BindWindowHolder(UIHolderObjectBase holder)
        {
            BindHolderCommon(holder);
            RectTransform rect = holder.RectTransform;
            rect.localPosition = Vector3.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            _canvas = holder.GetComponent<Canvas>();
            if (_canvas == null) _canvas = holder.gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _raycaster = holder.GetComponent<GraphicRaycaster>();
            if (_raycaster == null) _raycaster = holder.gameObject.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = false;
        }
    }

    public abstract class UIWindow<T> : UIWindow where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed override Type UIHolderType => typeof(T);
        internal sealed override void BindUIHolder(UIHolderObjectBase holder) => BindWindowHolder(holder);
    }
}
