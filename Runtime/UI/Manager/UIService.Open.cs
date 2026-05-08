using System;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    sealed class LayerData
    {
        public UIMetadata[] Items;
        public int[] TypeIdToIndex;
        public int Count;
        public int LastFullscreenIndex;

        public LayerData(int initialCapacity)
        {
            Items = new UIMetadata[initialCapacity];
            TypeIdToIndex = new int[initialCapacity];
            for (int i = 0; i < TypeIdToIndex.Length; i++)
            {
                TypeIdToIndex[i] = -1;
            }

            Count = 0;
            LastFullscreenIndex = -1;
        }

        public void EnsureTypeCapacity(int typeId)
        {
            if ((uint)typeId < (uint)TypeIdToIndex.Length)
            {
                return;
            }

            int oldLength = TypeIdToIndex.Length;
            int newLength = oldLength;
            while (newLength <= typeId)
            {
                newLength <<= 1;
            }

            Array.Resize(ref TypeIdToIndex, newLength);
            for (int i = oldLength; i < newLength; i++)
            {
                TypeIdToIndex[i] = -1;
            }
        }

        public void EnsureItemCapacity()
        {
            if (Count < Items.Length)
            {
                return;
            }

            Array.Resize(ref Items, Items.Length << 1);
        }
    }

    internal sealed partial class UIService
    {
        private readonly LayerData[] _openUI = new LayerData[(int)UILayer.All];

        private async UniTask<UIBase> ShowUIImplAsync(UIMetadata metaInfo, params object[] userDatas)
        {
            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperation())
            {
                metaInfo.View?.RefreshParams(userDatas);
                return metaInfo.View;
            }

            int operationVersion = metaInfo.OperationVersion;
            await UIHolderFactory.CreateUIResourceAsync(metaInfo, UICacheLayer);
            if (operationVersion != metaInfo.OperationVersion || metaInfo.View == null || metaInfo.State == UIState.Uninitialized || metaInfo.State == UIState.Destroyed)
            {
                metaInfo.EndShowOperation(operationVersion);
                return null;
            }

            FinalizeShow(metaInfo, userDatas);
            bool showResult = await UpdateVisualState(metaInfo, metaInfo.CancellationToken);
            metaInfo.EndShowOperation(operationVersion);
            return showResult ? metaInfo.View : null;
        }

        private UIBase ShowUIImplSync(UIMetadata metaInfo, params object[] userDatas)
        {
            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperation())
            {
                metaInfo.View?.RefreshParams(userDatas);
                return metaInfo.View;
            }

            int operationVersion = metaInfo.OperationVersion;
            UIHolderFactory.CreateUIResourceSync(metaInfo, UICacheLayer);
            if (operationVersion != metaInfo.OperationVersion || metaInfo.View == null || metaInfo.State == UIState.Uninitialized || metaInfo.State == UIState.Destroyed)
            {
                metaInfo.EndShowOperation(operationVersion);
                return null;
            }

            FinalizeShow(metaInfo, userDatas);
            bool showResult = UpdateVisualStateSync(metaInfo);
            metaInfo.EndShowOperation(operationVersion);
            if (!showResult)
            {
                return null;
            }

            return metaInfo.View;
        }

        private async UniTask CloseUIImpl(UIMetadata meta, bool force)
        {
            if (meta.State == UIState.Uninitialized)
            {
                return;
            }

            if (!meta.BeginCloseOperation())
            {
                return;
            }

            int operationVersion = meta.OperationVersion;

            if (meta.State == UIState.CreatedUI)
            {
                await meta.DisposeAsync();
                meta.EndCloseOperation(operationVersion);
                return;
            }

            if (meta.State == UIState.Loaded || meta.State == UIState.Initialized)
            {
                var popResult = Pop(meta);
                SortWindowVisible(meta.MetaInfo.UILayer, popResult.previousFullscreenIndex);
                SortWindowDepth(meta.MetaInfo.UILayer, popResult.removedIndex >= 0 ? popResult.removedIndex : 0);
                meta.View.Visible = false;
                CacheWindow(meta, force);
                meta.EndCloseOperation(operationVersion);
                return;
            }

            bool closeResult = await meta.View.InternalClose(meta.CancellationToken);
            if (!closeResult || meta.State != UIState.Closed)
            {
                meta.EndCloseOperation(operationVersion);
                return;
            }

            var closedPopResult = Pop(meta);
            SortWindowVisible(meta.MetaInfo.UILayer, closedPopResult.previousFullscreenIndex);
            SortWindowDepth(meta.MetaInfo.UILayer, closedPopResult.removedIndex >= 0 ? closedPopResult.removedIndex : 0);
            CacheWindow(meta, force);
            meta.EndCloseOperation(operationVersion);
        }


        private UIBase GetUIImpl(UIMetadata meta)
        {
            return meta.View;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateMetaUI(UIMetadata meta)
        {
            if (meta.State == UIState.Uninitialized) meta.CreateUI();
        }

        private void FinalizeShow(UIMetadata meta, object[] userDatas)
        {
            if (meta.InCache)
            {
                RemoveFromCache(meta.MetaInfo.RuntimeTypeHandle);
                Push(meta);
            }
            else
            {
                switch (meta.State)
                {
                    case UIState.Loaded:
                        Push(meta);
                        break;
                    case UIState.Opening:
                    case UIState.Closing:
                    case UIState.Opened:
                        MoveToTop(meta);
                        break;
                }
            }

            meta.View.RefreshParams(userDatas);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Push(UIMetadata meta)
        {
            var layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            layer.EnsureTypeCapacity(typeId);
            if (layer.TypeIdToIndex[typeId] < 0)
            {
                layer.EnsureItemCapacity();
                int index = layer.Count++;
                layer.Items[index] = meta;
                layer.TypeIdToIndex[typeId] = index;
                if (meta.MetaInfo.FullScreen)
                {
                    layer.LastFullscreenIndex = index;
                }

                UpdateLayerParent(meta);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (int removedIndex, int previousFullscreenIndex) Pop(UIMetadata meta)
        {
            var layer = _openUI[meta.MetaInfo.UILayer];
            int previousFullscreenIndex = layer.LastFullscreenIndex;
            int typeId = meta.MetaInfo.TypeId;
            if ((uint)typeId < (uint)layer.TypeIdToIndex.Length)
            {
                int index = layer.TypeIdToIndex[typeId];
                if (index < 0)
                {
                    return (-1, previousFullscreenIndex);
                }

                int lastIndex = layer.Count - 1;
                for (int i = index; i < lastIndex; i++)
                {
                    UIMetadata item = layer.Items[i + 1];
                    layer.Items[i] = item;
                    layer.TypeIdToIndex[item.MetaInfo.TypeId] = i;
                }

                layer.Items[lastIndex] = null;
                layer.Count = lastIndex;
                layer.TypeIdToIndex[typeId] = -1;

                UpdateFullscreenIndexAfterRemove(layer, meta, index);
                return (index, previousFullscreenIndex);
            }

            return (-1, previousFullscreenIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateLayerParent(UIMetadata meta)
        {
            if (meta.View?.Holder != null && meta.View.Holder.IsValid())
            {
                var layerRect = GetLayerRect(meta.MetaInfo.UILayer);
                meta.View.Holder.transform.SetParent(layerRect);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MoveToTop(UIMetadata meta)
        {
            var layer = _openUI[meta.MetaInfo.UILayer];
            int lastIdx = layer.Count - 1;
            int typeId = meta.MetaInfo.TypeId;

            if ((uint)typeId >= (uint)layer.TypeIdToIndex.Length)
                return;

            int currentIdx = layer.TypeIdToIndex[typeId];

            if (currentIdx != lastIdx && currentIdx >= 0)
            {
                for (int i = currentIdx; i < lastIdx; i++)
                {
                    UIMetadata item = layer.Items[i + 1];
                    layer.Items[i] = item;
                    layer.TypeIdToIndex[item.MetaInfo.TypeId] = i;
                }

                layer.Items[lastIdx] = meta;
                layer.TypeIdToIndex[typeId] = lastIdx;
                UpdateFullscreenIndexAfterMove(layer, meta, currentIdx, lastIdx);

                SortWindowDepth(meta.MetaInfo.UILayer, currentIdx);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async UniTask<bool> UpdateVisualState(UIMetadata meta, CancellationToken cancellationToken = default)
        {
            SortWindowVisible(meta.MetaInfo.UILayer);
            SortWindowDepth(meta.MetaInfo.UILayer);
            if (meta.State == UIState.Loaded)
            {
                if (!await meta.View.InternalInitlized(cancellationToken))
                {
                    return false;
                }
            }

            return await meta.View.InternalOpen(cancellationToken);
        }

        private bool UpdateVisualStateSync(UIMetadata meta)
        {
            SortWindowVisible(meta.MetaInfo.UILayer);
            SortWindowDepth(meta.MetaInfo.UILayer);
            if (meta.State == UIState.Loaded)
            {
                if (!meta.View.InternalInitlizedSync()) return false;
            }

            return meta.View.InternalOpenSync();
        }

        private void SortWindowVisible(int layer, int previousFullscreenIndex = int.MinValue)
        {
            var layerData = _openUI[layer];
            int count = layerData.Count;

            int fullscreenIdx = layerData.LastFullscreenIndex;
            if (fullscreenIdx >= count || (fullscreenIdx >= 0 && !IsDisplayFullscreen(layerData.Items[fullscreenIdx])))
            {
                fullscreenIdx = FindLastFullscreenIndex(layerData, count - 1);
                layerData.LastFullscreenIndex = fullscreenIdx;
            }

            int oldFullscreenIndex = previousFullscreenIndex == int.MinValue ? fullscreenIdx : previousFullscreenIndex;
            if (oldFullscreenIndex == fullscreenIdx)
            {
                ApplyVisibilityRange(layerData, fullscreenIdx >= 0 ? fullscreenIdx : 0, count, fullscreenIdx);
                return;
            }

            if (oldFullscreenIndex == -1 && fullscreenIdx == -1)
            {
                ApplyVisibilityRange(layerData, 0, count, -1);
                return;
            }

            int start = oldFullscreenIndex < 0 || fullscreenIdx < 0
                ? 0
                : Math.Min(oldFullscreenIndex, fullscreenIdx);
            int endExclusive = oldFullscreenIndex < 0 || fullscreenIdx < 0
                ? count
                : Math.Max(oldFullscreenIndex, fullscreenIdx) + 1;

            ApplyVisibilityRange(layerData, start, endExclusive, fullscreenIdx);
        }

        private void SortWindowDepth(int layer, int startIndex = 0)
        {
            var layerData = _openUI[layer];
            int baseDepth = layer * LAYER_DEEP;

            for (int i = startIndex; i < layerData.Count; i++)
            {
                int newDepth = baseDepth + i * WINDOW_DEEP;

                if (layerData.Items[i].View.Depth != newDepth)
                {
                    layerData.Items[i].View.Depth = newDepth;
                }
            }
        }

        private static bool IsDisplayFullscreen(UIMetadata meta)
        {
            return meta.MetaInfo.FullScreen && UIStateMachine.IsDisplayActive(meta.State);
        }

        private static int FindLastFullscreenIndex(LayerData layer, int startIndex)
        {
            for (int i = Math.Min(startIndex, layer.Count - 1); i >= 0; i--)
            {
                if (IsDisplayFullscreen(layer.Items[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ApplyVisibilityRange(LayerData layer, int startInclusive, int endExclusive, int fullscreenIdx)
        {
            if (startInclusive < 0)
            {
                startInclusive = 0;
            }

            if (endExclusive > layer.Count)
            {
                endExclusive = layer.Count;
            }

            bool showAll = fullscreenIdx < 0;
            for (int i = startInclusive; i < endExclusive; i++)
            {
                layer.Items[i].View.Visible = showAll || i >= fullscreenIdx;
            }
        }

        private static void UpdateFullscreenIndexAfterRemove(LayerData layer, UIMetadata removedMeta, int removedIndex)
        {
            if (layer.Count == 0)
            {
                layer.LastFullscreenIndex = -1;
                return;
            }

            if (removedMeta.MetaInfo.FullScreen && layer.LastFullscreenIndex == removedIndex)
            {
                layer.LastFullscreenIndex = FindLastFullscreenIndex(layer, removedIndex - 1);
                return;
            }

            if (removedIndex < layer.LastFullscreenIndex)
            {
                layer.LastFullscreenIndex--;
            }
        }

        private static void UpdateFullscreenIndexAfterMove(LayerData layer, UIMetadata meta, int fromIndex, int toIndex)
        {
            if (layer.LastFullscreenIndex == fromIndex)
            {
                layer.LastFullscreenIndex = toIndex;
                return;
            }

            if (!meta.MetaInfo.FullScreen)
            {
                return;
            }

            if (fromIndex < layer.LastFullscreenIndex)
            {
                layer.LastFullscreenIndex--;
            }

            layer.LastFullscreenIndex = Math.Max(layer.LastFullscreenIndex, toIndex);
        }
    }
}
