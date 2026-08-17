using System;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
                    metadata.CancelAsyncOperations();
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
                UIBase view = _children[i].View;
                if (view.State == UIState.Opened)
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

        internal UniTask<UIBase> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible)
        {
            return CreateWidgetCoreAsync(
                metadata,
                visible,
                async (meta, cts) =>
                {
                    await UIHolderFactory.CreateUIResourceAsync(meta, parent, cts.Token, this);
                    return true;
                });
        }

        internal UIBase CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            return CreateWidgetCoreSync(
                metadata,
                visible,
                meta =>
                {
                    UIHolderFactory.CreateUIResourceSync(meta, parent, this);
                    return true;
                });
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

        protected async UniTask<T> CreateWidgetAsync<T>(UIHolderObjectBase holder, bool destroyHolderOnDispose = false) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            UIBase view = await CreateWidgetCoreAsync(
                metadata,
                visible: true,
                (meta, _) =>
                {
                    UIBase widget = meta.View;
                    widget.BindUIHolder(holder, this);
                    widget.SetDestroyHolderOnDispose(destroyHolderOnDispose);
                    return UniTask.FromResult(true);
                });
            return (T)view;
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

        protected T CreateWidgetSync<T>(UIHolderObjectBase holder, bool destroyHolderOnDispose = false) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)CreateWidgetCoreSync(
                metadata,
                visible: true,
                meta =>
                {
                    UIBase widget = meta.View;
                    widget.BindUIHolder(holder, this);
                    widget.SetDestroyHolderOnDispose(destroyHolderOnDispose);
                    return true;
                });
        }

        #endregion

        #endregion

        // Widget 轻量路径：create → load → init → open；仅用 generation + CTS，不进入 Window Showing 态
        private async UniTask<UIBase> CreateWidgetCoreAsync(
            UIMetadata metadata,
            bool visible,
            Func<UIMetadata, CancellationTokenSource, UniTask<bool>> resourceStep)
        {
            if (metadata == null)
            {
                return null;
            }

            metadata.CreateUI();
            if (metadata.View == null)
            {
                await metadata.DisposeAsync();
                UIMetadataFactory.ReturnToPool(metadata);
                return null;
            }

            int createVersion = metadata.BeginWidgetCreate(out CancellationTokenSource loadCts);
            try
            {
                if (!await resourceStep(metadata, loadCts)
                    || !IsWidgetCreateStillValid(metadata, createVersion))
                {
                    await FailWidgetCreateAsync(metadata, createVersion);
                    return null;
                }

                AddWidget(metadata);
                if (!await metadata.View.InternalInitlized(metadata, createVersion)
                    || !metadata.IsOperationCurrent(createVersion)
                    || State == UIState.Destroying
                    || State == UIState.Destroyed)
                {
                    await FailWidgetCreateAsync(metadata, createVersion);
                    return null;
                }

                metadata.View.Visible = visible;
                if (!visible)
                {
                    return metadata.View;
                }

                if (metadata.View.InternalOpen(metadata, createVersion)
                    && metadata.IsOperationCurrent(createVersion))
                {
                    return metadata.View;
                }

                await FailWidgetCreateAsync(metadata, createVersion);
                return null;
            }
            catch
            {
                await FailWidgetCreateAsync(metadata, createVersion);
                throw;
            }
            finally
            {
                metadata.EndWidgetCreate(createVersion, loadCts);
                loadCts.Dispose();
            }
        }

        private UIBase CreateWidgetCoreSync(
            UIMetadata metadata,
            bool visible,
            Func<UIMetadata, bool> resourceStep)
        {
            if (metadata == null)
            {
                return null;
            }

            metadata.CreateUI();
            if (metadata.View == null)
            {
                metadata.DisposeImmediate();
                UIMetadataFactory.ReturnToPool(metadata);
                return null;
            }

            int createVersion = metadata.BeginWidgetCreate(out CancellationTokenSource loadCts);
            try
            {
                if (!resourceStep(metadata) || !IsWidgetCreateStillValid(metadata, createVersion))
                {
                    FailWidgetCreateImmediate(metadata, createVersion);
                    return null;
                }

                AddWidget(metadata);
                if (!metadata.View.InternalInitlizedSync(metadata, createVersion)
                    || !metadata.IsOperationCurrent(createVersion)
                    || State == UIState.Destroying
                    || State == UIState.Destroyed)
                {
                    FailWidgetCreateImmediate(metadata, createVersion);
                    return null;
                }

                metadata.View.Visible = visible;
                if (!visible)
                {
                    return metadata.View;
                }

                if (metadata.View.InternalOpen(metadata, createVersion)
                    && metadata.IsOperationCurrent(createVersion))
                {
                    return metadata.View;
                }

                FailWidgetCreateImmediate(metadata, createVersion);
                return null;
            }
            catch
            {
                FailWidgetCreateImmediate(metadata, createVersion);
                throw;
            }
            finally
            {
                metadata.EndWidgetCreate(createVersion, loadCts);
                loadCts.Dispose();
            }
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

        private bool IsWidgetCreateStillValid(UIMetadata meta, int createVersion)
        {
            return meta != null
                   && meta.IsOperationCurrent(createVersion)
                   && State != UIState.Destroying
                   && State != UIState.Destroyed
                   && meta.View != null
                   && meta.State == UIState.Loaded;
        }

        private async UniTask FailWidgetCreateAsync(UIMetadata meta, int createVersion)
        {
            if (meta == null || !meta.IsOperationCurrent(createVersion))
            {
                return;
            }

            RemoveChildMetadata(meta);
            await meta.DisposeAsync();
            UIMetadataFactory.ReturnToPool(meta);
        }

        private void FailWidgetCreateImmediate(UIMetadata meta, int createVersion)
        {
            if (meta == null || !meta.IsOperationCurrent(createVersion))
            {
                return;
            }

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
                meta.CancelAsyncOperations();
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
