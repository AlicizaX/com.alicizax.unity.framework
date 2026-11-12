using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private List<UIWidget> _widgets;

        internal void DestroyWidget(UIWidget widget)
        {
            _widgets.Remove(widget);
            widget.DestroyView(true);
        }

        protected UniTask<T> CreateWidgetAsync<T>(Transform parent, bool visible = true,
            CancellationToken cancellationToken = default) where T : UIWidget =>
            CreateWidgetAsync<T>(UIMetaRegistry.Require(typeof(T)), parent, visible, cancellationToken);

        protected UniTask<UIWidget> CreateWidgetAsync(string typeName, Transform parent, bool visible = true,
            CancellationToken cancellationToken = default) =>
            CreateWidgetAsync<UIWidget>(RequireWidget(typeName), parent, visible, cancellationToken);

        protected T CreateWidgetSync<T>(Transform parent, bool visible = true) where T : UIWidget =>
            (T)CreateWidgetSync(UIMetaRegistry.Require(typeof(T)), parent, visible);

        protected UIWidget CreateWidgetSync(string typeName, Transform parent, bool visible = true) =>
            CreateWidgetSync(RequireWidget(typeName), parent, visible);

        /// <summary>用已存在的节点创建 Widget；节点随 Widget 一起销毁。</summary>
        protected T CreateWidgetSync<T>(UIHolderObjectBase holder, bool visible = true) where T : UIWidget
        {
            UIMetaRegistry.UIMetaInfo info = UIMetaRegistry.Require(typeof(T));
            if (info == null) return null;
            if (holder == null || !info.HolderType.IsInstanceOfType(holder))
            {
                Log.Error("[UI] Holder does not match Widget {0}.", typeof(T));
                return null;
            }
            UIWidget widget = AddWidget(info, holder.transform);
            if (widget == null) return null;
            widget.AttachHolder(holder, visible);
            return (T)widget;
        }

        private protected UIWidget AddWidget(UIMetaRegistry.UIMetaInfo info, Transform parent)
        {
            if (info == null) return null;
            if (_state is UIState.Loading or UIState.Destroyed || parent == null ||
                !parent.IsChildOf(_holder.transform) || !parent.gameObject.activeInHierarchy)
            {
                Log.Error("[UI] Widget {0} must be created under an active node of a bound view.", info.Name);
                return null;
            }
            var widget = (UIWidget)Service.CreateView(info);
            widget.Parent = this;
            (_widgets ??= new List<UIWidget>(4)).Add(widget);
            return widget;
        }

        private async UniTask<T> CreateWidgetAsync<T>(UIMetaRegistry.UIMetaInfo info, Transform parent, bool visible,
            CancellationToken cancellationToken) where T : UIWidget
        {
            UIWidget widget = AddWidget(info, parent);
            if (widget == null) return null;
            await widget.LoadHolderAsync(parent, visible, cancellationToken);
            return widget.State == UIState.Destroyed ? null : (T)widget;
        }

        private UIWidget CreateWidgetSync(UIMetaRegistry.UIMetaInfo info, Transform parent, bool visible)
        {
            UIWidget widget = AddWidget(info, parent);
            if (widget == null) return null;
            widget.LoadHolder(parent, visible);
            return widget.State == UIState.Destroyed ? null : widget;
        }

        private protected static UIMetaRegistry.UIMetaInfo RequireWidget(string typeName)
        {
            UIMetaRegistry.UIMetaInfo info = UIMetaRegistry.Require(typeName);
            if (info == null || !info.IsWindow) return info;
            Log.Error("[UI] The UI type must be a Widget: {0}.", typeName);
            return null;
        }

        private void SetWidgetsEnabled(bool enabled)
        {
            if (_widgets == null) return;
            for (int i = 0; i < _widgets.Count; i++)
            {
                UIWidget widget = _widgets[i];
                if (widget.State is UIState.Opening or UIState.Opened) widget.SetEnabled(enabled);
            }
        }

        /// <summary>逐个出列再销毁：销毁钩子里再销毁兄弟 Widget 时，列表里不会留下已销毁的条目。</summary>
        private void DestroyWidgets()
        {
            if (_widgets == null) return;
            while (_widgets.Count > 0)
            {
                int last = _widgets.Count - 1;
                UIWidget widget = _widgets[last];
                _widgets.RemoveAt(last);
                widget.DestroyView(false);
            }
        }
    }
}
