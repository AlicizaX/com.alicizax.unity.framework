using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private List<UIWidget> _children;
        private UIWidget[] _childBuffer;
        private int _childCount;

        private int SnapshotChildren()
        {
            int count = _children?.Count ?? 0;
            if (count == 0)
            {
                // 子节点被移除后清掉快照尾部引用，避免缓存窗口长期持有已销毁的 Widget。
                if (_childBuffer != null && _childCount > 0) Array.Clear(_childBuffer, 0, _childCount);
                _childCount = 0;
                return 0;
            }
            if (_childBuffer == null || _childBuffer.Length < count)
                _childBuffer = new UIWidget[Mathf.NextPowerOfTwo(count)];
            _children.CopyTo(_childBuffer);
            if (_childCount > count) Array.Clear(_childBuffer, count, _childCount - count);
            _childCount = count;
            return count;
        }

        private void RefreshChildrenEffective()
        {
            int count = SnapshotChildren();
            for (int i = 0; i < count; i++)
            {
                UIWidget child = _childBuffer[i];
                if (!child.DestroyRequested) child.RefreshEffective();
            }
        }

        private void DestroyChildrenImmediate()
        {
            int count = SnapshotChildren();
            for (int i = 0; i < count; i++) _childBuffer[i].DestroyNow();
            if (_childBuffer != null) Array.Clear(_childBuffer, 0, _childCount);
            _childCount = 0;
        }

        private void UpdateChildren(UIBase root, int generation, float deltaTime)
        {
            int count = SnapshotChildren();
            for (int i = 0; i < count && Effective && root.IsCurrent(generation); i++)
                _childBuffer[i].InternalUpdate(root, generation, deltaTime);
        }

        private void CancelChildCreations()
        {
            int count = SnapshotChildren();
            for (int i = 0; i < count; i++) _childBuffer[i].CancelLoad();
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
            OnWidgetRemoved(widget);
        }

        internal virtual void OnWidgetRemoved(UIWidget widget) { }
    }
}
