using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWindow : UIBase
    {
        internal UIWindowRecord Record;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private int _depth;

        protected UIWindow() => RootWindow = this;

        protected UniTask CloseSelf(bool force = false, bool skipTransition = false) =>
            Service.CloseWindow(this, force, skipTransition);

        internal override void OnClosed() => Service.FinalizeAfterClosed(Record);
        internal override void OnDestroyStarted() => Service.ReleaseRecord(Record);

        internal Canvas Canvas => _canvas;

        /// <summary>层级深度。资源尚未绑定 Holder 时只记录，待 Canvas 创建后一次应用。</summary>
        internal int Depth
        {
            get => _depth;
            set
            {
                if (_depth == value) return;
                _depth = value;
                if (_canvas != null) _canvas.sortingOrder = value;
            }
        }

        private protected override void ApplyVisible(bool value)
        {
            Holder.gameObject.layer = value ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
            if (State != UIState.Initializing) _canvas.enabled = value;
        }

        private protected override void SetInteractable(bool value)
        {
            _raycaster.enabled = value;
        }

        private protected override void ReleaseVisuals()
        {
            _canvas = null;
            _raycaster = null;
        }

        internal void BindWindowHolder(UIHolderObjectBase holder)
        {
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
            _canvas.sortingOrder = _depth;
            _raycaster = holder.GetComponent<GraphicRaycaster>();
            if (_raycaster == null) _raycaster = holder.gameObject.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = false;
            if (!DestroyRequested) BindHolderCommon(holder);
        }
    }

    public abstract class UIWindow<T> : UIWindow where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed override Type UIHolderType => typeof(T);
        internal sealed override void BindUIHolder(UIHolderObjectBase holder) => BindWindowHolder(holder);
    }
}
