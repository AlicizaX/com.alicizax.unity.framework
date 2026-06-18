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

        private enum WidgetProcessResult
        {
            Success,
            FailedCleaned,
            Stale,
        }

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
                if (view != null && view.Visible)
                {
                    // 父窗口正在销毁：取消子级未完成的异步操作，关闭时跳过过渡动画
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

        internal async UniTask<UIBase> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible)
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

            if (!metadata.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
                await metadata.DisposeAsync();
                UIMetadataFactory.ReturnToPool(metadata);
                return null;
            }

            CancellationToken cancellationToken = loadCts.Token;
            UIBase result = null;
            bool shouldReturnToPool = false;
            bool visualStarted = false;
            try
            {
                await UIHolderFactory.CreateUIResourceAsync(metadata, parent, cancellationToken, this);
                if (!IsWidgetCreateStillValid(metadata, operationVersion))
                {
                    shouldReturnToPool = await RemoveFailedWidgetAsync(metadata, operationVersion);
                }
                else
                {
                    WidgetProcessResult processResult = await PrepareWidget(metadata, visible, operationVersion);
                    if (TryCompleteWidgetProcess(processResult, out shouldReturnToPool))
                    {
                        result = (UIBase)metadata.View;
                        if (visible)
                        {
                            visualStarted = true;
                            return await RunWidgetOpenVisualAsync(metadata, operationVersion, loadCts, showCompletionSource);
                        }
                    }
                }
            }
            catch
            {
                shouldReturnToPool = await RemoveFailedWidgetAsync(metadata, operationVersion);
                throw;
            }
            finally
            {
                if (!visualStarted)
                {
                    metadata.EndShowOperation(operationVersion, loadCts);
                    metadata.CompleteShowOperation(showCompletionSource, result);
                    loadCts.Dispose();
                    if (shouldReturnToPool)
                    {
                        UIMetadataFactory.ReturnToPool(metadata);
                    }
                }
            }

            return result;
        }

        internal UIBase CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            if (metadata == null)
            {
                return null;
            }

            if (!ValidateSyncWidgetMetadata(metadata))
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

            if (!metadata.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
                metadata.DisposeImmediate();
                UIMetadataFactory.ReturnToPool(metadata);
                return null;
            }

            UIBase result = null;
            bool shouldReturnToPool = false;
            bool visualStarted = false;
            try
            {
                UIHolderFactory.CreateUIResourceSync(metadata, parent, this);
                if (!IsWidgetCreateStillValid(metadata, operationVersion))
                {
                    shouldReturnToPool = RemoveFailedWidgetImmediate(metadata, operationVersion);
                }
                else
                {
                    WidgetProcessResult processResult = PrepareWidgetSync(metadata, visible, operationVersion);
                    if (TryCompleteWidgetProcess(processResult, out shouldReturnToPool))
                    {
                        result = (UIBase)metadata.View;
                        if (visible)
                        {
                            visualStarted = true;
                            RunWidgetOpenVisualAsync(metadata, operationVersion, loadCts, showCompletionSource).Forget();
                        }
                    }
                }
            }
            catch
            {
                shouldReturnToPool = RemoveFailedWidgetImmediate(metadata, operationVersion);
                throw;
            }
            finally
            {
                if (!visualStarted)
                {
                    metadata.EndShowOperation(operationVersion, loadCts);
                    metadata.CompleteShowOperation(showCompletionSource, result);
                    loadCts.Dispose();
                    if (shouldReturnToPool)
                    {
                        UIMetadataFactory.ReturnToPool(metadata);
                    }
                }
            }

            return result;
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

            if (!metadata.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
                await metadata.DisposeAsync();
                UIMetadataFactory.ReturnToPool(metadata);
                return null;
            }

            UIBase widget = null;
            T result = null;
            bool shouldReturnToPool = false;
            bool visualStarted = false;
            try
            {
                widget = (UIBase)metadata.View;
                widget.BindUIHolder(holder, this);
                widget.SetDestroyHolderOnDispose(destroyHolderOnDispose);
                WidgetProcessResult processResult = await PrepareWidget(metadata, true, operationVersion);
                if (TryCompleteWidgetProcess(processResult, out shouldReturnToPool))
                {
                    result = (T)widget;
                    visualStarted = true;
                    UIBase opened = await RunWidgetOpenVisualAsync(metadata, operationVersion, loadCts, showCompletionSource);
                    return (T)opened;
                }
            }
            catch
            {
                shouldReturnToPool = await RemoveFailedWidgetAsync(metadata, operationVersion);
                throw;
            }
            finally
            {
                if (!visualStarted)
                {
                    metadata.EndShowOperation(operationVersion, loadCts);
                    metadata.CompleteShowOperation(showCompletionSource, result);
                    loadCts.Dispose();
                    if (shouldReturnToPool)
                    {
                        UIMetadataFactory.ReturnToPool(metadata);
                    }
                }
            }

            return result;
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
            if (metadata == null)
            {
                return null;
            }

            if (!ValidateSyncWidgetMetadata(metadata))
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

            if (!metadata.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
                metadata.DisposeImmediate();
                UIMetadataFactory.ReturnToPool(metadata);
                return null;
            }

            UIBase widget = (UIBase)metadata.View;
            T result = null;
            bool shouldReturnToPool = false;
            bool visualStarted = false;
            try
            {
                widget.BindUIHolder(holder, this);
                widget.SetDestroyHolderOnDispose(destroyHolderOnDispose);
                WidgetProcessResult processResult = PrepareWidgetSync(metadata, true, operationVersion);
                if (TryCompleteWidgetProcess(processResult, out shouldReturnToPool))
                {
                    result = (T)widget;
                    visualStarted = true;
                    RunWidgetOpenVisualAsync(metadata, operationVersion, loadCts, showCompletionSource).Forget();
                }
            }
            catch
            {
                shouldReturnToPool = RemoveFailedWidgetImmediate(metadata, operationVersion);
                throw;
            }
            finally
            {
                if (!visualStarted)
                {
                    metadata.EndShowOperation(operationVersion, loadCts);
                    metadata.CompleteShowOperation(showCompletionSource, result);
                    loadCts.Dispose();
                    if (shouldReturnToPool)
                    {
                        UIMetadataFactory.ReturnToPool(metadata);
                    }
                }
            }

            return result;
        }

        #endregion

        #endregion

        private static bool ValidateSyncWidgetMetadata(UIMetadata metadata)
        {
            return metadata != null;
        }

        private static bool TryCompleteWidgetProcess(WidgetProcessResult processResult, out bool shouldReturnToPool)
        {
            shouldReturnToPool = processResult == WidgetProcessResult.FailedCleaned;
            return processResult == WidgetProcessResult.Success;
        }


        private async UniTask<WidgetProcessResult> PrepareWidget(UIMetadata meta, bool visible, int operationVersion)
        {
            AddWidget(meta);

            if (!await meta.View.InternalInitlized(meta, operationVersion))
            {
                return await RemoveFailedWidgetAsync(meta, operationVersion)
                    ? WidgetProcessResult.FailedCleaned
                    : WidgetProcessResult.Stale;
            }

            meta.View.Visible = visible;
            return WidgetProcessResult.Success;
        }

        private WidgetProcessResult PrepareWidgetSync(UIMetadata meta, bool visible, int operationVersion)
        {
            AddWidget(meta);

            if (!meta.View.InternalInitlizedSync(meta, operationVersion))
            {
                return RemoveFailedWidgetImmediate(meta, operationVersion)
                    ? WidgetProcessResult.FailedCleaned
                    : WidgetProcessResult.Stale;
            }

            meta.View.Visible = visible;
            return WidgetProcessResult.Success;
        }

        private async UniTask<UIBase> RunWidgetOpenVisualAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            UniTaskCompletionSource<UIBase> showCompletionSource)
        {
            UIBase result = null;
            bool shouldReturnToPool = false;
            bool exceptionThrown = false;
            try
            {
                if (meta.View != null && await meta.View.InternalOpen() && meta.OperationVersion == operationVersion)
                {
                    result = meta.View;
                }
                else
                {
                    shouldReturnToPool = await RemoveFailedWidgetAsync(meta, operationVersion);
                }
            }
            catch (Exception exception)
            {
                exceptionThrown = true;
                shouldReturnToPool = await RemoveFailedWidgetAsync(meta, operationVersion);
                meta.FailShowOperation(showCompletionSource, exception);
                throw;
            }
            finally
            {
                meta.EndShowOperation(operationVersion, loadCts);
                if (!exceptionThrown)
                {
                    meta.CompleteShowOperation(showCompletionSource, result);
                }
                loadCts.Dispose();
                if (shouldReturnToPool)
                {
                    UIMetadataFactory.ReturnToPool(meta);
                }
            }

            return result;
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

        private bool IsWidgetCreateStillValid(UIMetadata meta, int operationVersion)
        {
            return meta != null
                   && meta.OperationVersion == operationVersion
                   && State != UIState.Destroying
                   && State != UIState.Destroyed
                   && meta.View != null
                   && meta.State == UIState.Loaded;
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
                    await widget.InternalClose();
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

        private async UniTask<bool> RemoveFailedWidgetAsync(UIMetadata meta, int operationVersion)
        {
            if (meta == null)
            {
                return false;
            }

            if (!meta.IsOperationCurrent(operationVersion))
            {
                return false;
            }

            bool removed = RemoveChildMetadata(meta);
            if (!removed && meta.State == UIState.Uninitialized)
            {
                return true;
            }

            await meta.DisposeAsync();
            return true;
        }

        private bool RemoveFailedWidgetImmediate(UIMetadata meta, int operationVersion)
        {
            if (meta == null)
            {
                return false;
            }

            if (!meta.IsOperationCurrent(operationVersion))
            {
                return false;
            }

            bool removed = RemoveChildMetadata(meta);
            if (!removed && meta.State == UIState.Uninitialized)
            {
                return true;
            }

            meta.DisposeImmediate();
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
