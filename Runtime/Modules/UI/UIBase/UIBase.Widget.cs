using System;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private UIMetadata[] _children;
        private int _childCount;
        private UIMetadata[] _updateableChildren;
        private int _updateableChildCount;

        private void UpdateChildren()
        {
            for (int i = 0; i < _updateableChildCount; i++)
            {
                var meta = _updateableChildren[i];
                UIBase view = meta?.View;
                if (view != null && view.State == UIState.Opened)
                {
                    view.InternalUpdate();
                }
            }
        }

        private async UniTask DestroyAllChildren()
        {
            while (_childCount > 0)
            {
                UIMetadata metadata = _children[--_childCount];
                _children[_childCount] = null;
                UIBase view = metadata?.View;
                if (view != null && UIStateMachine.IsDisplayActive(view.State))
                {
                    metadata.CancelResourceLoad();
                    await view.InternalClose(skipTransition: true);
                }

                if (metadata != null)
                {
                    await metadata.DisposeAsync();
                    UIMetadataFactory.ReturnToPool(metadata);
                }
            }

            _updateableChildCount = 0;
        }

        private void DestroyAllChildrenImmediate()
        {
            while (_childCount > 0)
            {
                UIMetadata metadata = _children[--_childCount];
                _children[_childCount] = null;
                if (metadata != null)
                {
                    metadata.DisposeImmediate();
                    UIMetadataFactory.ReturnToPool(metadata);
                }
            }

            _updateableChildCount = 0;
        }

        private void ChildVisible(bool value)
        {
            for (int i = 0; i < _childCount; i++)
            {
                UIBase view = _children[i]?.View;
                if (view != null && view.State == UIState.Opened)
                {
                    view.Visible = value;
                }
            }
        }

        private void SyncChildDepth()
        {
            if (_childCount <= 0 || _children == null)
            {
                return;
            }

            int childDepth = Depth + 5;
            for (int i = 0; i < _childCount; i++)
            {
                UIBase view = _children[i]?.View;
                if (view?._canvas != null)
                {
                    view.Depth = childDepth;
                }
            }
        }

        internal async UniTask<UIBase> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible)
        {
            if (!TryBeginWidgetCreate(metadata))
                return null;

            CancellationTokenSource loadCts = metadata.BeginResourceLoad();
            UIBase widget = null;
            try
            {
                await UIHolderFactory.CreateUIResourceAsync(metadata, parent, loadCts.Token, this);
                widget = await FinishWidgetCreateAsync(metadata, visible);
                return widget;
            }
            finally
            {
                metadata.EndResourceLoad(loadCts);
                if (widget == null)
                    await FailWidgetCreateAsync(metadata);
            }
        }

        internal UIBase CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            if (!TryBeginWidgetCreate(metadata))
                return null;

            UIBase widget = null;
            try
            {
                UIHolderFactory.CreateUIResourceSync(metadata, parent, this);
                widget = FinishWidgetCreateSync(metadata, visible);
                return widget;
            }
            finally
            {
                if (widget == null)
                    FailWidgetCreateImmediate(metadata);
            }
        }

        internal async UniTask<UIBase> CreateWidgetUIAsync(UIMetadata metadata, VisualElement parent, bool visible)
        {
            if (!TryBeginWidgetCreate(metadata))
                return null;

            CancellationTokenSource loadCts = metadata.BeginResourceLoad();
            UIBase widget = null;
            try
            {
                await UIHolderFactory.CreateUIResourceAsync(metadata, Holder.transform, loadCts.Token, this);
                if (metadata.View?.Holder is not UIToolkitHolderBase toolkitHolder)
                {
                    Log.Error("UI Toolkit widget holder is required: {0}", metadata.View?.GetType().Name);
                    return null;
                }

                toolkitHolder.AttachTo(parent);
                widget = await FinishWidgetCreateAsync(metadata, visible);
                return widget;
            }
            finally
            {
                metadata.EndResourceLoad(loadCts);
                if (widget == null)
                    await FailWidgetCreateAsync(metadata);
            }
        }

        internal UIBase CreateWidgetUISync(UIMetadata metadata, VisualElement parent, bool visible)
        {
            if (!TryBeginWidgetCreate(metadata))
                return null;

            UIBase widget = null;
            try
            {
                UIHolderFactory.CreateUIResourceSync(metadata, Holder.transform, this);
                if (metadata.View?.Holder is not UIToolkitHolderBase toolkitHolder)
                {
                    Log.Error("UI Toolkit widget holder is required: {0}", metadata.View?.GetType().Name);
                    return null;
                }

                toolkitHolder.AttachTo(parent);
                widget = FinishWidgetCreateSync(metadata, visible);
                return widget;
            }
            finally
            {
                if (widget == null)
                    FailWidgetCreateImmediate(metadata);
            }
        }

        #region CreateWidget

        #region Async

        protected async UniTask<UIBase> CreateWidgetAsync(string typeName, Transform parent, bool visible = true)
        {
            if (!UIMetaRegistry.TryGet(typeName, out var metaRegistry))
            {
                return null;
            }

            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(metaRegistry.RuntimeTypeHandle);
            return await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(VisualElement parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(UIHolderObjectBase holder, bool destroyHolderOnDispose = false) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            if (!TryBeginWidgetCreate(metadata))
                return null;

            UIBase widget = null;
            try
            {
                metadata.View.BindUIHolder(holder, this);
                metadata.View.SetDestroyHolderOnDispose(destroyHolderOnDispose);
                widget = await FinishWidgetCreateAsync(metadata, visible: true);
                return (T)widget;
            }
            finally
            {
                if (widget == null)
                    await FailWidgetCreateAsync(metadata);
            }
        }

        #endregion


        #region Sync

        protected UIBase CreateWidgetSync(string typeName, Transform parent, bool visible = true)
        {
            if (!UIMetaRegistry.TryGet(typeName, out var metaRegistry))
            {
                return null;
            }

            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(metaRegistry.RuntimeTypeHandle);
            return CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(VisualElement parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(UIHolderObjectBase holder, bool destroyHolderOnDispose = false) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            if (!TryBeginWidgetCreate(metadata))
                return null;

            UIBase widget = null;
            try
            {
                metadata.View.BindUIHolder(holder, this);
                metadata.View.SetDestroyHolderOnDispose(destroyHolderOnDispose);
                widget = FinishWidgetCreateSync(metadata, visible: true);
                return (T)widget;
            }
            finally
            {
                if (widget == null)
                    FailWidgetCreateImmediate(metadata);
            }
        }

        #endregion

        #endregion

        private bool TryBeginWidgetCreate(UIMetadata metadata)
        {
            if (metadata == null)
                return false;

            metadata.CreateUI();
            if (metadata.View != null)
                return true;

            metadata.DisposeImmediate();
            UIMetadataFactory.ReturnToPool(metadata);
            return false;
        }

        private async UniTask<UIBase> FinishWidgetCreateAsync(UIMetadata metadata, bool visible)
        {
            UIBase view = metadata.View;
            if (!CanContinueWidgetCreate(metadata, view, UIState.Loaded))
                return null;

            AddWidget(metadata);
            if (!await view.InternalInitlized() || !CanContinueWidgetCreate(metadata, view, UIState.Initialized))
                return null;

            view.Visible = visible;
            if (!visible)
                return view;

            return view.InternalOpen() ? view : null;
        }

        private UIBase FinishWidgetCreateSync(UIMetadata metadata, bool visible)
        {
            UIBase view = metadata.View;
            if (!CanContinueWidgetCreate(metadata, view, UIState.Loaded))
                return null;

            AddWidget(metadata);
            if (!view.InternalInitlizedSync() || !CanContinueWidgetCreate(metadata, view, UIState.Initialized))
                return null;

            view.Visible = visible;
            if (!visible)
                return view;

            return view.InternalOpen() ? view : null;
        }

        private void AddWidget(UIMetadata meta)
        {
            EnsureChildCapacity();
            int index = _childCount++;
            _children[index] = meta;

            if (meta.MetaInfo.NeedUpdate)
            {
                EnsureUpdateableChildCapacity();
                _updateableChildren[_updateableChildCount++] = meta;
            }
        }

        private bool CanContinueWidgetCreate(UIMetadata meta, UIBase widget, UIState expectedState)
        {
            return State != UIState.Destroying
                   && State != UIState.Destroyed
                   && ReferenceEquals(meta?.View, widget)
                   && widget != null
                   && widget.State == expectedState;
        }

        private async UniTask FailWidgetCreateAsync(UIMetadata meta)
        {
            if (meta == null)
                return;

            RemoveChildMetadata(meta);
            await meta.DisposeAsync();
            UIMetadataFactory.ReturnToPool(meta);
        }

        private void FailWidgetCreateImmediate(UIMetadata meta)
        {
            if (meta == null)
                return;

            RemoveChildMetadata(meta);
            meta.DisposeImmediate();
            UIMetadataFactory.ReturnToPool(meta);
        }

        public async UniTask RemoveWidget(UIBase widget)
        {
            if (!TryRemoveChild(widget, out var meta))
            {
                return;
            }

            OnWidgetRemoved(widget);

            if (meta != null)
            {
                meta.CancelResourceLoad();
                if (UIStateMachine.IsDisplayActive(widget.State))
                {
                    await widget.InternalClose(skipTransition: true);
                }

                if (meta.MetaInfo.NeedUpdate)
                {
                    RemoveUpdateableChild(meta);
                }

                await meta.DisposeAsync();
                UIMetadataFactory.ReturnToPool(meta);
            }
        }

        protected virtual void OnWidgetRemoved(UIBase widget)
        {
        }

        private void RemoveUpdateableChild(UIMetadata meta)
        {
            for (int i = 0; i < _updateableChildCount; i++)
            {
                if (_updateableChildren[i] != meta)
                {
                    continue;
                }

                int lastIndex = _updateableChildCount - 1;
                _updateableChildren[i] = _updateableChildren[lastIndex];
                _updateableChildren[lastIndex] = null;
                _updateableChildCount = lastIndex;
                return;
            }
        }

        private bool TryRemoveChild(UIBase widget, out UIMetadata meta)
        {
            meta = null;
            if (widget == null)
            {
                return false;
            }

            int index = FindChildIndex(widget);
            if (index < 0)
            {
                return false;
            }

            meta = RemoveChildAt(index);
            return true;
        }

        private bool RemoveChildMetadata(UIMetadata meta)
        {
            int index = FindChildIndex(meta);
            if (index < 0)
            {
                return false;
            }

            RemoveChildAt(index);

            if (meta.MetaInfo.NeedUpdate)
            {
                RemoveUpdateableChild(meta);
            }

            return true;
        }

        private int FindChildIndex(UIBase widget)
        {
            for (int i = 0; i < _childCount; i++)
            {
                if (_children[i]?.View == widget)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindChildIndex(UIMetadata metadata)
        {
            for (int i = 0; i < _childCount; i++)
            {
                if (_children[i] == metadata)
                {
                    return i;
                }
            }

            return -1;
        }

        private UIMetadata RemoveChildAt(int index)
        {
            UIMetadata removed = _children[index];
            int lastIndex = _childCount - 1;
            _children[index] = _children[lastIndex];
            _children[lastIndex] = null;
            _childCount = lastIndex;
            return removed;
        }

        private void EnsureChildCapacity()
        {
            if (_children == null)
            {
                _children = new UIMetadata[8];
                return;
            }

            if (_childCount < _children.Length)
            {
                return;
            }

            Array.Resize(ref _children, _children.Length << 1);
        }

        private void EnsureUpdateableChildCapacity()
        {
            if (_updateableChildren == null)
            {
                _updateableChildren = new UIMetadata[4];
                return;
            }

            if (_updateableChildCount < _updateableChildren.Length)
            {
                return;
            }

            Array.Resize(ref _updateableChildren, _updateableChildren.Length << 1);
        }

    }
}
