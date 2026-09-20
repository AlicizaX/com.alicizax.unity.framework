using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private List<UIWidget> _children;
        private Dictionary<UIWidget, CancellationTokenSource> _childCreations;

        private UIWidget[] SnapshotChildren(out int count)
        {
            count = _children?.Count ?? 0;
            if (count == 0) return null;
            UIWidget[] snapshot = ArrayPool<UIWidget>.Shared.Rent(count);
            _children.CopyTo(snapshot);
            return snapshot;
        }

        private static void ReturnChildren(UIWidget[] snapshot)
        {
            if (snapshot != null) ArrayPool<UIWidget>.Shared.Return(snapshot, true);
        }

        private void RefreshChildrenEffective()
        {
            UIWidget[] snapshot = SnapshotChildren(out int count);
            try
            {
                for (int i = 0; i < count; i++)
                    if (!snapshot[i].DestroyRequested) snapshot[i].RefreshEffective();
            }
            finally { ReturnChildren(snapshot); }
        }

        private void DestroyChildrenImmediate()
        {
            UIWidget[] snapshot = SnapshotChildren(out int count);
            try
            {
                for (int i = 0; i < count; i++)
                    snapshot[i].DestroyNow();
            }
            finally { ReturnChildren(snapshot); }
        }

        private void UpdateChildren(UIBase root, int generation)
        {
            UIWidget[] snapshot = SnapshotChildren(out int count);
            try
            {
                for (int i = 0; i < count && Effective && root.IsCurrent(generation); i++)
                    snapshot[i].InternalUpdate(root, generation);
            }
            finally { ReturnChildren(snapshot); }
        }

        private CancellationTokenSource BeginChildCreation(UIWidget widget)
        {
            var cancellation = new CancellationTokenSource();
            (_childCreations ??= new Dictionary<UIWidget, CancellationTokenSource>()).Add(widget, cancellation);
            return cancellation;
        }

        private void EndChildCreation(UIWidget widget, CancellationTokenSource cancellation)
        {
            _childCreations?.Remove(widget);
            cancellation.Dispose();
        }

        private void CancelChildCreation(UIWidget widget)
        {
            if (_childCreations == null || !_childCreations.TryGetValue(widget, out CancellationTokenSource cancellation))
                return;
            UIHolderObjectBase.Cancel(cancellation);
        }

        private void CancelChildCreations()
        {
            Dictionary<UIWidget, CancellationTokenSource> creations = _childCreations;
            _childCreations = null;
            if (creations == null) return;
            foreach (CancellationTokenSource cancellation in creations.Values)
                UIHolderObjectBase.Cancel(cancellation);
        }

        internal async UniTask<UIWidget> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            UIWidget widget = BeginWidgetCreate(metadata, parent);
            if (widget == null) return null;
            CancellationTokenSource cancellation = BeginChildCreation(widget);
            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
                registration = cancellationToken.Register(() => UIHolderObjectBase.Cancel(cancellation));
            try
            {
                bool loaded = await UIHolderFactory.CreateUIResourceAsync(widget, parent, cancellation.Token);
                if (loaded && !cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    return FinishWidgetCreate(widget, visible);
                widget.DestroyNow();
                return null;
            }
            finally
            {
                registration.Dispose();
                EndChildCreation(widget, cancellation);
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
            (_children ??= new List<UIWidget>(4)).Add(widget);
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
            _children.Remove(widget);
            CancelChildCreation(widget);
            OnWidgetRemoved(widget);
        }

        internal virtual void OnWidgetRemoved(UIWidget widget) { }
    }
}
