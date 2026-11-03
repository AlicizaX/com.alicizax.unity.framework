using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private UIWidget _firstChild;
        private UIWidget _lastChild;

        internal UIWidget FirstChild => _firstChild;

        private void RefreshChildrenEffective()
        {
            for (UIWidget child = _firstChild; child != null; child = child.NextSibling) child.RefreshEffective();
        }

        private void DestroyChildrenImmediate()
        {
            for (UIWidget child = _firstChild; child != null; child = child.NextSibling) child.DestroyNow();
        }

        private void CancelChildCreations()
        {
            for (UIWidget child = _firstChild; child != null; child = child.NextSibling) child.CancelLoad();
        }

        internal async UniTask<UIWidget> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            UIWidget widget = BeginWidgetCreate(metadata, parent);
            if (widget == null) return null;
            CancellationToken loadToken = widget.LoadToken;
            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
                registration = cancellationToken.Register(UIWidget.CancelLoadCallback, widget);
            try
            {
                bool loaded = await UIHolderFactory.CreateUIResourceAsync(widget, parent, loadToken);
                if (!loaded || widget.DestroyRequested)
                {
                    widget.DestroyNow();
                    return null;
                }
                return FinishWidgetCreate(widget, visible);
            }
            finally
            {
                registration.Dispose();
                widget.EndLoad();
            }
        }

        internal UIWidget CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            UIWidget widget = BeginWidgetCreate(metadata, parent);
            if (widget == null) return null;
            if (UIHolderFactory.CreateUIResourceSync(widget, parent))
                return FinishWidgetCreate(widget, visible);
            widget.DestroyNow();
            return null;
        }

        protected async UniTask<UIWidget> CreateWidgetAsync(string typeName, Transform parent, bool visible = true,
            CancellationToken cancellationToken = default)
        {
            if (!UIMetaRegistry.TryGet(typeName, out var info))
            {
                Log.Error("[UI] Unknown Widget type: {0}.", typeName);
                return null;
            }
            return await CreateWidgetUIAsync(UIMetadata.Create(Type.GetTypeFromHandle(info.RuntimeTypeHandle)), parent, visible, cancellationToken);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(Transform parent, bool visible = true,
            CancellationToken cancellationToken = default) where T : UIWidget =>
            (T)await CreateWidgetUIAsync(UIMetadata.Create(typeof(T)), parent, visible, cancellationToken);

        protected UIWidget CreateWidgetSync(string typeName, Transform parent, bool visible = true)
        {
            if (!UIMetaRegistry.TryGet(typeName, out var info))
            {
                Log.Error("[UI] Unknown Widget type: {0}.", typeName);
                return null;
            }
            return CreateWidgetUISync(UIMetadata.Create(Type.GetTypeFromHandle(info.RuntimeTypeHandle)), parent, visible);
        }

        protected T CreateWidgetSync<T>(Transform parent, bool visible = true) where T : UIWidget =>
            (T)CreateWidgetUISync(UIMetadata.Create(typeof(T)), parent, visible);

        protected T CreateWidgetSync<T>(UIHolderObjectBase holder, bool destroyHolderOnDispose = true, bool visible = true) where T : UIWidget
        {
            if (holder == null)
            {
                Log.Error("[UI] Cannot create a Widget with a missing Holder.");
                return null;
            }
            UIMetadata metadata = UIMetadata.Create(typeof(T));
            if (metadata == null) return null;
            if (!Type.GetTypeFromHandle(metadata.MetaInfo.HolderRuntimeTypeHandle).IsInstanceOfType(holder))
            {
                Log.Error("[UI] Holder type does not match Widget {0}.", typeof(T));
                return null;
            }
            UIWidget widget = BeginWidgetCreate(metadata, holder.transform);
            if (widget == null) return null;
            widget.SetDestroyHolderOnDispose(destroyHolderOnDispose);
            widget.BindUIHolder(holder);
            return (T)FinishWidgetCreate(widget, visible);
        }

        private UIWidget BeginWidgetCreate(UIMetadata metadata, Transform parent)
        {
            if (DestroyRequested)
            {
                Log.Error("[UI] Cannot create a Widget after its parent requested destruction.");
                return null;
            }
            if (parent == null || Holder == null || !parent.IsChildOf(Holder.transform))
            {
                Log.Error("[UI] Widget must belong to its parent's Transform subtree.");
                return null;
            }
            if (metadata == null) return null;
            if (!typeof(UIWidget).IsAssignableFrom(metadata.UILogicType))
            {
                Log.Error("[UI] The UI type must be a Widget: {0}.", metadata.UILogicType);
                return null;
            }
            UIWidget widget = (UIWidget)metadata.CreateUI(Service);
            if (widget == null) return null;
            widget.Parent = this;
            widget.RootWindow = RootWindow;
            widget.PreviousSibling = _lastChild;
            if (_lastChild == null) _firstChild = widget;
            else _lastChild.NextSibling = widget;
            _lastChild = widget;
            return widget;
        }

        private UIWidget FinishWidgetCreate(UIWidget widget, bool open)
        {
            if (DestroyRequested || widget.DestroyRequested) return null;
            widget.InternalInitialize(open);
            return widget.DestroyRequested ? null : widget;
        }

        public UniTask RemoveWidget(UIWidget widget) =>
            widget != null && widget.Parent == this ? widget.Destroy() : UniTask.CompletedTask;

        internal void DetachWidget(UIWidget widget)
        {
            UIWidget previous = widget.PreviousSibling;
            UIWidget next = widget.NextSibling;
            if (previous == null) _firstChild = next;
            else previous.NextSibling = next;
            if (next == null) _lastChild = previous;
            else next.PreviousSibling = previous;
            widget.PreviousSibling = null;
            OnWidgetRemoved(widget);
        }

        internal virtual void OnWidgetRemoved(UIWidget widget) { }
    }
}
