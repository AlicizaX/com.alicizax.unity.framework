using System;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private UIMetadata[] _children = new UIMetadata[8];
        private int[] _childTypeIdToIndex = CreateIndexArray(8);
        private int _childCount;
        private UIMetadata[] _updateableChildren = new UIMetadata[4];
        private int _updateableChildCount;

        private void UpdateChildren()
        {
            for (int i = 0; i < _updateableChildCount; i++)
            {
                var meta = _updateableChildren[i];
                if (meta.View.State == UIState.Opened)
                {
                    meta.View.InternalUpdate();
                }
            }
        }

        private async UniTask DestroyAllChildren()
        {
            while (_childCount > 0)
            {
                UIMetadata metadata = _children[--_childCount];
                _children[_childCount] = null;
                ClearChildIndex(metadata);
                if (metadata.View.Visible)
                {
                    metadata.CancelAsyncOperations();
                    metadata.EnsureCancellationToken();
                    await metadata.View.InternalClose(metadata.CancellationToken);
                }

                await metadata.DisposeAsync();
                UIMetadataFactory.ReturnToPool(metadata);
            }

            _updateableChildCount = 0;
        }

        private void DestroyAllChildrenImmediate()
        {
            while (_childCount > 0)
            {
                UIMetadata metadata = _children[--_childCount];
                _children[_childCount] = null;
                ClearChildIndex(metadata);
                metadata.DisposeImmediate();
                UIMetadataFactory.ReturnToPool(metadata);
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

        internal async UniTask<UIBase> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible)
        {
            metadata.CreateUI();
            await UIHolderFactory.CreateUIResourceAsync(metadata, parent, this);
            await ProcessWidget(metadata, visible, metadata.CancellationToken);
            return (UIBase)metadata.View;
        }

        internal UIBase CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            metadata.CreateUI();
            UIHolderFactory.CreateUIResourceSync(metadata, parent, this);
            ProcessWidgetSync(metadata, visible);
            return (UIBase)metadata.View;
        }

        #region CreateWidget

        #region Async

        protected async UniTask<UIBase> CreateWidgetAsync(string typeName, Transform parent, bool visible = true)
        {
            UIMetaRegistry.TryGet(typeName, out var metaRegistry);
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(metaRegistry.RuntimeTypeHandle);
            return await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(UIHolderObjectBase holder) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            metadata.CreateUI();
            UIBase widget = (UIBase)metadata.View;
            widget.BindUIHolder(holder, this);
            await ProcessWidget(metadata, true, metadata.CancellationToken);
            return (T)widget;
        }

        #endregion


        #region Sync

        protected UIBase CreateWidgetSync(string typeName, Transform parent, bool visible = true)
        {
            UIMetaRegistry.TryGet(typeName, out var metaRegistry);
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(metaRegistry.RuntimeTypeHandle);
            return CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(UIHolderObjectBase holder) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            metadata.CreateUI();
            UIBase widget = (UIBase)metadata.View;
            widget.BindUIHolder(holder, this);
            ProcessWidgetSync(metadata, true);
            return (T)widget;
        }

        #endregion

        #endregion


        private async UniTask ProcessWidget(UIMetadata meta, bool visible, CancellationToken cancellationToken = default)
        {
            if (!AddWidget(meta))
            {
                await meta.DisposeAsync();
                UIMetadataFactory.ReturnToPool(meta);
                return;
            }

            if (!await meta.View.InternalInitlized(cancellationToken))
            {
                return;
            }

            meta.View.Visible = visible;
            if (meta.View.Visible)
            {
                await meta.View.InternalOpen(cancellationToken);
            }
        }

        private bool AddWidget(UIMetadata meta)
        {
            int typeId = meta.MetaInfo.TypeId;
            EnsureChildIndexCapacity(typeId);
            if (_childTypeIdToIndex[typeId] >= 0)
            {
                Log.Warning("Already has widget:{0}", meta.View);
                return false;
            }

            EnsureChildCapacity();
            int index = _childCount++;
            _children[index] = meta;
            _childTypeIdToIndex[typeId] = index;

            if (meta.MetaInfo.NeedUpdate)
            {
                EnsureUpdateableChildCapacity();
                _updateableChildren[_updateableChildCount++] = meta;
            }

            return true;
        }

        public async UniTask RemoveWidget(UIBase widget)
        {
            if (!TryRemoveChild(widget, out var meta))
            {
                return;
            }

            if (meta != null)
            {
                meta.CancelAsyncOperations();
                meta.EnsureCancellationToken();
                await widget.InternalClose(meta.CancellationToken);

                if (meta.MetaInfo.NeedUpdate)
                {
                    RemoveUpdateableChild(meta);
                }

                await meta.DisposeAsync();
                UIMetadataFactory.ReturnToPool(meta);
            }
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

        private void ProcessWidgetSync(UIMetadata meta, bool visible)
        {
            if (!AddWidget(meta))
            {
                meta.DisposeImmediate();
                UIMetadataFactory.ReturnToPool(meta);
                return;
            }

            if (!meta.View.InternalInitlizedSync())
            {
                return;
            }

            meta.View.Visible = visible;
            if (meta.View.Visible)
            {
                meta.View.InternalOpenSync();
            }
        }

        private bool TryRemoveChild(UIBase widget, out UIMetadata meta)
        {
            meta = null;
            if (widget == null)
            {
                return false;
            }

            int typeId = widget.UITypeId;
            if ((uint)typeId >= (uint)_childTypeIdToIndex.Length)
            {
                return false;
            }

            int index = _childTypeIdToIndex[typeId];
            if (index < 0 || index >= _childCount)
            {
                return false;
            }

            meta = _children[index];
            int lastIndex = _childCount - 1;
            UIMetadata last = _children[lastIndex];
            _children[index] = last;
            _children[lastIndex] = null;
            _childCount = lastIndex;
            _childTypeIdToIndex[typeId] = -1;
            if (index != lastIndex && last != null)
            {
                _childTypeIdToIndex[last.MetaInfo.TypeId] = index;
            }

            return true;
        }

        private void ClearChildIndex(UIMetadata metadata)
        {
            if (metadata == null)
            {
                return;
            }

            int typeId = metadata.MetaInfo.TypeId;
            if ((uint)typeId < (uint)_childTypeIdToIndex.Length)
            {
                _childTypeIdToIndex[typeId] = -1;
            }
        }

        private void EnsureChildCapacity()
        {
            if (_childCount < _children.Length)
            {
                return;
            }

            Array.Resize(ref _children, _children.Length << 1);
        }

        private void EnsureChildIndexCapacity(int typeId)
        {
            if ((uint)typeId < (uint)_childTypeIdToIndex.Length)
            {
                return;
            }

            int oldLength = _childTypeIdToIndex.Length;
            int newLength = oldLength;
            while (newLength <= typeId)
            {
                newLength <<= 1;
            }

            Array.Resize(ref _childTypeIdToIndex, newLength);
            for (int i = oldLength; i < newLength; i++)
            {
                _childTypeIdToIndex[i] = -1;
            }
        }

        private void EnsureUpdateableChildCapacity()
        {
            if (_updateableChildCount < _updateableChildren.Length)
            {
                return;
            }

            Array.Resize(ref _updateableChildren, _updateableChildren.Length << 1);
        }

        private static int[] CreateIndexArray(int capacity)
        {
            int[] values = new int[capacity];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = -1;
            }

            return values;
        }
    }
}
