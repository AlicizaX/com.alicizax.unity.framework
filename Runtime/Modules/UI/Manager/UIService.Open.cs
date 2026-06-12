using System;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX;
using AlicizaX.ObjectPool;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    sealed class LayerData
    {
        public UIMetadata[] Items;
        public int[] TypeIdToIndex;
        public int Count;
        public int LastFullscreenIndex;
        public int VisibilityVersion;

        public LayerData(int initialCapacity)
        {
            Items = new UIMetadata[initialCapacity];
            TypeIdToIndex = UITypeIndexArray.Create(initialCapacity);

            Count = 0;
            LastFullscreenIndex = -1;
            VisibilityVersion = 0;
        }

        public void EnsureTypeCapacity(int typeId)
        {
            UITypeIndexArray.EnsureCapacity(ref TypeIdToIndex, typeId);
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

        private readonly struct VisualStateUpdateResult
        {
            public readonly UIShowResult ShowResult;
            public readonly int PreviousFullscreenIndex;

            public VisualStateUpdateResult(UIShowResult showResult, int previousFullscreenIndex)
            {
                ShowResult = showResult;
                PreviousFullscreenIndex = previousFullscreenIndex;
            }
        }

        private async UniTask<UIShowResult> ShowUIImplAsync(UIMetadata metaInfo, object[] userDatas)
        {
            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
#if UNITY_EDITOR
                WarnUIOperation("Show joined", metaInfo, metaInfo?.OperationVersion ?? -1, "BeginShowOperation returned false. Another show operation is already running, so this call refreshes user data and waits for the running show.");
#endif
                metaInfo.SetPendingShowUserDatas(userDatas);
                UIBase joinedView = await metaInfo.WaitForShowOperationAsync();
                return CreateShowResultFromView(joinedView);
            }

            // 在 Begin 之后只捕获一次令牌：后续新操作会替换 CTS，再读属性会拿到错误令牌 特别注意
            CancellationToken cancellationToken = loadCts.Token;
            UIShowResult showResult = UIShowResult.Failed;
            int rollbackPreviousFullscreenIndex = int.MinValue;
            bool exceptionThrown = false;
            try
            {
                ReserveOpenSlot(metaInfo);
                await UIHolderFactory.CreateUIResourceAsync(metaInfo, UICacheLayer, cancellationToken);
                if (!IsShowValidAfterResourceCreation(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    if (!IsStaleShowOperation(metaInfo, operationVersion) && UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("Show invalid after async resource creation", metaInfo, operationVersion, GetInvalidShowReason(metaInfo, operationVersion));
#endif
                    if (ShouldRollbackInvalidShow(metaInfo, operationVersion))
                    {
                        await RollbackFailedShowAsync(metaInfo, operationVersion);
                    }

                    showResult = UIShowResult.Failed;
                }
                else
                {
                    FinalizeShow(metaInfo, metaInfo.GetPendingShowUserDatas(userDatas));
                    VisualStateUpdateResult visualStateResult = await UpdateVisualState(metaInfo, operationVersion);
                    showResult = visualStateResult.ShowResult;
                    rollbackPreviousFullscreenIndex = visualStateResult.PreviousFullscreenIndex;
                    if (!showResult.IsAccepted)
                    {
                        if (!IsStaleShowOperation(metaInfo, operationVersion))
                        {
#if UNITY_EDITOR
                            WarnUIOperation("Show visual state failed, rollback will run", metaInfo, operationVersion, "UpdateVisualState returned false. The UI did not reach a valid opened or occlusion-interrupted state.");
#endif
                            await RollbackFailedShowAsync(metaInfo, operationVersion, rollbackPreviousFullscreenIndex);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                exceptionThrown = true;
#if UNITY_EDITOR
                if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("Show exception, rollback will run", metaInfo, operationVersion, $"Exception={exception.GetType().Name}: {exception.Message}");
#endif
                if (ShouldRollbackInvalidShow(metaInfo, operationVersion))
                {
                    await RollbackFailedShowAsync(metaInfo, operationVersion, rollbackPreviousFullscreenIndex);
                }

                metaInfo.FailShowOperation(showCompletionSource, exception);
                throw;
            }
            finally
            {
                UIBase result = showResult.IsAccepted ? metaInfo.View : null;
                metaInfo.EndShowOperation(operationVersion, loadCts);
                if (!exceptionThrown)
                {
                    metaInfo.CompleteShowOperation(showCompletionSource, result);
                }
                loadCts.Dispose();
            }

            return showResult;
        }
        private UIBase ShowUIImplSync(UIMetadata metaInfo, object[] userDatas)
        {
            if (metaInfo.HasAsyncInitialize)
            {
                Log.Error("[UI] {0} uses async initialize and cannot be shown by sync API.", metaInfo.UILogicTypeName);
                return null;
            }

            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperationSync(out int operationVersion))
            {
#if UNITY_EDITOR
                WarnUIOperation("ShowSync skipped", metaInfo, metaInfo?.OperationVersion ?? -1, "BeginShowOperation returned false. Another show operation is already running, so this call only refreshes user data.");
#endif
                return RefreshAndGetOpenedView(metaInfo, userDatas);
            }

            bool showResult = false;
            try
            {
                ReserveOpenSlot(metaInfo);
                UIHolderFactory.CreateUIResourceSync(metaInfo, UICacheLayer);
                if (!IsShowValidAfterResourceCreation(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("ShowSync invalid after resource creation", metaInfo, operationVersion, GetInvalidShowReason(metaInfo, operationVersion));
#endif
                    if (ShouldRollbackInvalidShow(metaInfo, operationVersion))
                    {
                        RollbackFailedShow(metaInfo, operationVersion);
                    }

                    showResult = false;
                }
                else
                {
                    FinalizeShow(metaInfo, userDatas);
                    showResult = UpdateVisualStateSync(metaInfo, operationVersion);
                    if (!showResult)
                    {
#if UNITY_EDITOR
                        WarnUIOperation("ShowSync visual state failed, rollback will run", metaInfo, operationVersion, "UpdateVisualStateSync returned false. The UI did not reach a valid opened state.");
#endif
                        RollbackFailedShow(metaInfo, operationVersion);
                    }
                }
            }
            catch (Exception exception)
            {
#if UNITY_EDITOR
                if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("ShowSync exception, rollback will run", metaInfo, operationVersion, $"Exception={exception.GetType().Name}: {exception.Message}");
#endif
                if (ShouldRollbackInvalidShow(metaInfo, operationVersion))
                {
                    RollbackFailedShow(metaInfo, operationVersion);
                }

                throw;
            }
            finally
            {
                metaInfo.EndShowOperationSync(operationVersion);
            }

            return showResult ? metaInfo.View : null;
        }
        private async UniTask<bool> CloseUIImplCore(UIMetadata meta, bool force)
        {
            if (meta == null || meta.State == UIState.Uninitialized || meta.State == UIState.Destroying)
            {
                return false;
            }

            bool interruptedShow = meta.ShowInProgress;
            if (!meta.BeginCloseOperation(out int operationVersion))
            {
#if UNITY_EDITOR
                WarnUIOperation("Close skipped", meta, meta?.OperationVersion ?? -1, "BeginCloseOperation returned false. Another close operation is already running.");
#endif
                return false;
            }

            bool closeCompleted = false;
            try
            {
                if (meta.State == UIState.CreatedUI)
                {
#if UNITY_EDITOR
                    WarnUIOperation("Close interrupted pending show", meta, operationVersion, "CloseUI was called before the UI resource finished loading. The reserved stack slot will be removed and the UI will be disposed.");
#endif
                    var popResult = Pop(meta);
                    if (popResult.removedIndex < 0)
                    {
                        return false;
                    }

                    await SortWindowVisibleAsync(meta.MetaInfo.UILayer, popResult.previousFullscreenIndex);
                    SortWindowDepth(meta.MetaInfo.UILayer, popResult.removedIndex);
                    await meta.DisposeAsync();
                    closeCompleted = true;
                }
                else if (meta.State == UIState.Loaded || meta.State == UIState.Initialized)
                {
                    var popResult = Pop(meta);
                    if (popResult.removedIndex < 0)
                    {
                        return false;
                    }

                    await SortWindowVisibleAsync(meta.MetaInfo.UILayer, popResult.previousFullscreenIndex);
                    SortWindowDepth(meta.MetaInfo.UILayer, popResult.removedIndex);
                    meta.View.Visible = false;
                    if (interruptedShow)
                    {
                        await meta.DisposeAsync();
                    }
                    else
                    {
                        CacheWindow(meta, force);
                    }

                    closeCompleted = true;
                }
                else
                {
                    bool closeResult = await meta.View.InternalClose();
                    if (closeResult && meta.State == UIState.Closed && meta.OperationVersion == operationVersion)
                    {
                        var closedPopResult = Pop(meta);
                        if (closedPopResult.removedIndex < 0)
                        {
                            return false;
                        }

                        await SortWindowVisibleAsync(meta.MetaInfo.UILayer, closedPopResult.previousFullscreenIndex);
                        SortWindowDepth(meta.MetaInfo.UILayer, closedPopResult.removedIndex);
                        CacheWindow(meta, force);
                        closeCompleted = true;
                    }
#if UNITY_EDITOR
                    else
                    {
                        WarnUIOperation("Close interrupted", meta, operationVersion, "InternalClose returned false or the final state was not Closed. The UI was not popped or cached by this close operation.");
                    }
#endif

                }
            }
            finally
            {
                meta.EndCloseOperation(operationVersion);
            }

            return closeCompleted;
        }
        private UIBase GetUIImpl(UIMetadata meta)
        {
            return meta?.State == UIState.Opened ? meta.View : null;
        }

        private static bool IsOpenImpl(UIMetadata meta)
        {
            return meta != null && meta.State == UIState.Opened;
        }

        public async UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false)
        {
            if (predicate == null)
            {
                return false;
            }

            for (int layerIndex = _openUI.Length - 1; layerIndex >= 0; layerIndex--)
            {
                LayerData layer = _openUI[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                for (int i = layer.Count - 1; i >= 0; i--)
                {
                    UIMetadata metadata = layer.Items[i];
                    if (metadata == null
                        || metadata.State == UIState.Uninitialized
                        || metadata.State == UIState.Destroying
                        || metadata.CloseInProgress)
                    {
                        continue;
                    }

                    RuntimeTypeHandle handle = metadata.MetaInfo.RuntimeTypeHandle;
                    bool matched;
                    try
                    {
                        matched = predicate(handle);
                    }
                    catch (Exception)
                    {
                        matched = false;
                    }

                    if (!matched)
                    {
                        continue;
                    }

                    return await CloseUIAsync(handle, force);
                }
            }

            return false;
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
                // 缓存时关闭了 Canvas 渲染，重新显示前恢复
                meta.View.SetCanvasEnabled(true);
                Push(meta);
            }
            else
            {
                switch (meta.State)
                {
                    case UIState.Loaded:
                        Push(meta);
                        break;
                    case UIState.Closed:
                    case UIState.Opening:
                    case UIState.Closing:
                    case UIState.Opened:
                        MoveToTop(meta);
                        break;
                }
            }

            UpdateLayerParent(meta);
            meta.View.RefreshParams(userDatas);
        }

        private static UIBase RefreshAndGetOpenedView(UIMetadata meta, object[] userDatas)
        {
            UIBase view = meta?.View;
            view?.RefreshParams(userDatas);
            return meta?.State == UIState.Opened ? view : null;
        }

        private void ReserveOpenSlot(UIMetadata meta)
        {
            if (meta?.State == UIState.CreatedUI)
            {
                Push(meta);
            }
        }

        private bool IsShowValidAfterResourceCreation(UIMetadata meta, int operationVersion)
        {
            return meta != null
                   && operationVersion == meta.OperationVersion
                   && meta.View != null
                   && meta.State != UIState.Uninitialized
                   && meta.State != UIState.CreatedUI
                   && meta.State != UIState.Destroyed;
        }

        private bool ShouldRollbackInvalidShow(UIMetadata meta, int operationVersion)
        {
            if (meta == null || operationVersion != meta.OperationVersion)
            {
                return false;
            }

            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            return GetOpenIndex(layer, meta) >= 0;
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
                if (IsOcclusionWindow(meta))
                {
                    layer.LastFullscreenIndex = index;
                }

                AddUpdateableWindow(meta);
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
                RemoveUpdateableWindow(meta);

                UpdateFullscreenIndexAfterRemove(layer, meta, index);
                return (index, previousFullscreenIndex);
            }

            return (-1, previousFullscreenIndex);
        }

        private static int GetOpenIndex(LayerData layer, UIMetadata meta)
        {
            int typeId = meta.MetaInfo.TypeId;
            if ((uint)typeId >= (uint)layer.TypeIdToIndex.Length)
            {
                return -1;
            }

            return layer.TypeIdToIndex[typeId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateLayerParent(UIMetadata meta)
        {
            if (meta.View?.Holder != null && meta.View.Holder.IsValid())
            {
                var layerRect = GetLayerRect(meta.MetaInfo.UILayer);
                meta.View.Holder.transform.SetParent(layerRect, false);
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
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async UniTask<VisualStateUpdateResult> UpdateVisualState(UIMetadata meta, int operationVersion)
        {
            int previousFullscreenIndex = int.MinValue;
            SortWindowDepth(meta.MetaInfo.UILayer);
            if (meta.State == UIState.Loaded)
            {
                if (!await meta.View.InternalInitlized(meta, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("Show initialization interrupted", meta, operationVersion, "InternalInitlized returned false. The show operation will be treated as failed.");
#endif
                    return new VisualStateUpdateResult(UIShowResult.Failed, previousFullscreenIndex);
                }

                if (meta.OperationVersion != operationVersion)
                {
#if UNITY_EDITOR
                    WarnUIOperation("Show initialization version changed", meta, operationVersion, "The operation version changed after initialization. The show operation will be treated as failed.");
#endif
                    return new VisualStateUpdateResult(UIShowResult.Cancelled, previousFullscreenIndex);
                }
            }

            UniTask<bool> openTask = meta.View.InternalOpen();
            previousFullscreenIndex = PromoteOcclusionWindow(meta);
            await SortWindowVisibleAsync(meta.MetaInfo.UILayer, previousFullscreenIndex);
            bool openResult = await openTask;

            if (openResult)
            {
                UIShowResult openedResult = IsStaleShowOperation(meta, operationVersion)
                    ? UIShowResult.Cancelled
                    : new UIShowResult(meta.View, UIShowResultState.Opened);
                return new VisualStateUpdateResult(openedResult, previousFullscreenIndex);
            }

            return new VisualStateUpdateResult(IsShowAcceptedAfterOpenInterruption(meta, operationVersion), previousFullscreenIndex);
        }

        private UIShowResult IsShowAcceptedAfterOpenInterruption(UIMetadata meta, int operationVersion)
        {
            if (IsStaleShowOperation(meta, operationVersion))
            {
                return UIShowResult.Cancelled;
            }

            if (meta == null
                || meta.View == null)
            {
#if UNITY_EDITOR
                if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("Show open interruption rejected", meta, operationVersion, GetOpenInterruptionRejectReason(meta, operationVersion));
#endif
                return UIShowResult.Failed;
            }

            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            if (GetOpenIndex(layer, meta) < 0)
            {
#if UNITY_EDITOR
                WarnUIOperation("Show open interruption rejected", meta, operationVersion, "InternalOpen returned false and the UI is no longer in the open stack. Rollback will run.");
#endif
                return UIShowResult.Failed;
            }

            UIState state = meta.State;
            bool accepted = UIStateMachine.IsDisplayActive(state) || state == UIState.Closed;
#if UNITY_EDITOR
            if (meta.View.WasLifecycleInterruptedByOcclusion(operationVersion))
            {
                WarnOcclusionInterruption(
                    accepted ? "Show open interruption accepted" : "Show open interruption rejected",
                    meta,
                    operationVersion,
                    accepted
                        ? "InternalOpen returned false, but the UI is still in the open stack with a recoverable display state. This is treated as an occlusion/lifecycle interruption and rollback is skipped."
                        : "InternalOpen returned false and the final state is not recoverable. Rollback will run.");
            }
            else
            {
                WarnUIOperation(
                    accepted ? "Show open interruption accepted" : "Show open interruption rejected",
                    meta,
                    operationVersion,
                    accepted
                        ? "InternalOpen returned false, but the UI is still in the open stack with a recoverable display state. Rollback is skipped."
                        : "InternalOpen returned false and the final state is not recoverable. Rollback will run.");
            }
#endif
            return accepted
                ? new UIShowResult(meta.View, state == UIState.Closed ? UIShowResultState.OcclusionAccepted : UIShowResultState.Opened)
                : UIShowResult.Failed;
        }

        private static bool IsStaleShowOperation(UIMetadata meta, int operationVersion)
        {
            return meta != null && meta.OperationVersion != operationVersion;
        }

        private static UIShowResult CreateShowResultFromView(UIBase view)
        {
            if (view == null)
            {
                return UIShowResult.Failed;
            }

            return view.State == UIState.Closed
                ? new UIShowResult(view, UIShowResultState.OcclusionAccepted)
                : new UIShowResult(view, UIShowResultState.Opened);
        }


        private bool UpdateVisualStateSync(UIMetadata meta, int operationVersion)
        {
            SortWindowDepth(meta.MetaInfo.UILayer);
            if (meta.State == UIState.Loaded)
            {
                if (!meta.View.InternalInitlizedSync(meta, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("ShowSync initialization failed", meta, operationVersion, "InternalInitlizedSync returned false. The show operation will rollback.");
#endif
                    return false;
                }

                if (meta.OperationVersion != operationVersion)
                {
#if UNITY_EDITOR
                    WarnUIOperation("ShowSync initialization version changed", meta, operationVersion, "The operation version changed after initialization. The show operation will rollback.");
#endif
                    return false;
                }
            }

            bool openResult = meta.View.InternalOpenSync();
            int previousFullscreenIndex = PromoteOcclusionWindow(meta);
            SortWindowVisibleSync(meta.MetaInfo.UILayer, previousFullscreenIndex);

#if UNITY_EDITOR
            if (!openResult)
            {
                WarnUIOperation("ShowSync open failed", meta, operationVersion, "InternalOpenSync returned false. The show operation will rollback.");
            }
#endif
            return openResult && !IsStaleShowOperation(meta, operationVersion);
        }

        private int PromoteOcclusionWindow(UIMetadata meta)
        {
            if (meta == null || !IsOcclusionWindow(meta))
            {
                return int.MinValue;
            }

            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            if ((uint)typeId >= (uint)layer.TypeIdToIndex.Length)
            {
                return int.MinValue;
            }

            int index = layer.TypeIdToIndex[typeId];
            if (index < 0 || index <= layer.LastFullscreenIndex)
            {
                return int.MinValue;
            }

            int previousFullscreenIndex = layer.LastFullscreenIndex;
            layer.LastFullscreenIndex = index;
            return previousFullscreenIndex;
        }

        private async UniTask SortWindowVisibleAsync(int layer, int previousFullscreenIndex = int.MinValue)
        {
            var layerData = _openUI[layer];
            int visibilityVersion = ++layerData.VisibilityVersion;
            int count = layerData.Count;

            int fullscreenIdx = layerData.LastFullscreenIndex;
            if (fullscreenIdx >= count || (fullscreenIdx >= 0 && !IsActiveOcclusionWindow(layerData.Items[fullscreenIdx])))
            {
                fullscreenIdx = FindLastFullscreenIndex(layerData, count - 1);
                layerData.LastFullscreenIndex = fullscreenIdx;
            }

            int oldFullscreenIndex = previousFullscreenIndex == int.MinValue ? fullscreenIdx : previousFullscreenIndex;
            if (oldFullscreenIndex == fullscreenIdx)
            {
                await ApplyVisibilityRangeAsync(layerData, 0, count, fullscreenIdx, visibilityVersion);
                return;
            }

            if (oldFullscreenIndex == -1 && fullscreenIdx == -1)
            {
                await ApplyVisibilityRangeAsync(layerData, 0, count, -1, visibilityVersion);
                return;
            }

            int start = oldFullscreenIndex < 0 || fullscreenIdx < 0
                ? 0
                : Math.Min(oldFullscreenIndex, fullscreenIdx);
            int endExclusive = oldFullscreenIndex < 0 || fullscreenIdx < 0
                ? count
                : Math.Max(oldFullscreenIndex, fullscreenIdx) + 1;

            await ApplyVisibilityRangeAsync(layerData, start, endExclusive, fullscreenIdx, visibilityVersion);
        }

        private void SortWindowVisibleSync(int layer, int previousFullscreenIndex = int.MinValue)
        {
            var layerData = _openUI[layer];
            layerData.VisibilityVersion++;
            int count = layerData.Count;

            int fullscreenIdx = layerData.LastFullscreenIndex;
            if (fullscreenIdx >= count || (fullscreenIdx >= 0 && !IsActiveOcclusionWindow(layerData.Items[fullscreenIdx])))
            {
                fullscreenIdx = FindLastFullscreenIndex(layerData, count - 1);
                layerData.LastFullscreenIndex = fullscreenIdx;
            }

            int oldFullscreenIndex = previousFullscreenIndex == int.MinValue ? fullscreenIdx : previousFullscreenIndex;
            if (oldFullscreenIndex == fullscreenIdx)
            {
                ApplyVisibilityRangeSync(layerData, 0, count, fullscreenIdx);
                return;
            }

            if (oldFullscreenIndex == -1 && fullscreenIdx == -1)
            {
                ApplyVisibilityRangeSync(layerData, 0, count, -1);
                return;
            }

            int start = oldFullscreenIndex < 0 || fullscreenIdx < 0
                ? 0
                : Math.Min(oldFullscreenIndex, fullscreenIdx);
            int endExclusive = oldFullscreenIndex < 0 || fullscreenIdx < 0
                ? count
                : Math.Max(oldFullscreenIndex, fullscreenIdx) + 1;

            ApplyVisibilityRangeSync(layerData, start, endExclusive, fullscreenIdx);
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

        private static bool IsOcclusionWindow(UIMetadata meta)
        {
            return meta.MetaInfo.OcclusionMode != UIOcclusionMode.None;
        }

        private static bool IsActiveOcclusionWindow(UIMetadata meta)
        {
            return IsOcclusionWindow(meta) && (UIStateMachine.IsDisplayActive(meta.State) || meta.State == UIState.Closed);
        }

        private static int FindLastFullscreenIndex(LayerData layer, int startIndex)
        {
            for (int i = Math.Min(startIndex, layer.Count - 1); i >= 0; i--)
            {
                if (IsActiveOcclusionWindow(layer.Items[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private struct OcclusionWorkItem
        {
            public UIBase View;
            public bool Visible;

            public OcclusionWorkItem(UIBase view, bool visible)
            {
                View = view;
                Visible = visible;
            }
        }

        private static async UniTask ApplyVisibilityRangeAsync(LayerData layer, int startInclusive, int endExclusive, int fullscreenIdx, int visibilityVersion)
        {
            NormalizeVisibilityRange(layer, ref startInclusive, ref endExclusive);
            if (!IsVisibilityVersionCurrent(layer, visibilityVersion))
            {
                return;
            }

            bool showAll = fullscreenIdx < 0;
            UIOcclusionMode occlusionMode = GetOcclusionMode(layer, fullscreenIdx, showAll);

            int asyncCount = 0;
            for (int i = startInclusive; i < endExclusive; i++)
            {
                if (!IsVisibilityVersionCurrent(layer, visibilityVersion))
                {
                    return;
                }

                UIBase view = layer.Items[i]?.View;
                if (view == null)
                {
                    continue;
                }

                bool visible = showAll || i >= fullscreenIdx;
                if (NeedsAsyncOcclusion(view, visible, occlusionMode))
                {
                    asyncCount++;
                }
                else
                {
                    view.Visible = visible;
                }
            }

            if (asyncCount == 0)
            {
                return;
            }

            if (asyncCount == 1)
            {
                for (int i = startInclusive; i < endExclusive; i++)
                {
                    if (!IsVisibilityVersionCurrent(layer, visibilityVersion))
                    {
                        return;
                    }

                    UIBase view = layer.Items[i]?.View;
                    if (view == null)
                    {
                        continue;
                    }

                    bool visible = showAll || i >= fullscreenIdx;
                    if (NeedsAsyncOcclusion(view, visible, occlusionMode))
                    {
                        await ApplyWindowOcclusionAsync(layer, visibilityVersion, view, visible, occlusionMode);
                        return;
                    }
                }

                return;
            }

            OcclusionWorkItem[] workItems = null;
            UniTask[] tasks = null;
            int n = 0;

            try
            {
                workItems = SlotArrayPool<OcclusionWorkItem>.Rent(asyncCount);
                for (int i = startInclusive; i < endExclusive && n < asyncCount; i++)
                {
                    if (!IsVisibilityVersionCurrent(layer, visibilityVersion))
                    {
                        return;
                    }

                    UIBase view = layer.Items[i]?.View;
                    if (view == null)
                    {
                        continue;
                    }

                    bool visible = showAll || i >= fullscreenIdx;
                    if (NeedsAsyncOcclusion(view, visible, occlusionMode))
                    {
                        workItems[n++] = new OcclusionWorkItem(view, visible);
                    }
                }

                if (n == 0)
                {
                    return;
                }

                if (n == 1)
                {
                    OcclusionWorkItem workItem = workItems[0];
                    await ApplyWindowOcclusionAsync(layer, visibilityVersion, workItem.View, workItem.Visible, occlusionMode);
                    return;
                }

                tasks = SlotArrayPool<UniTask>.Rent(n);
                if (tasks.Length > n)
                {
                    Array.Clear(tasks, n, tasks.Length - n);
                }

                for (int i = 0; i < n; i++)
                {
                    OcclusionWorkItem workItem = workItems[i];
                    tasks[i] = ApplyWindowOcclusionAsync(layer, visibilityVersion, workItem.View, workItem.Visible, occlusionMode);
                }

                await UniTask.WhenAll(tasks);
            }
            finally
            {
                if (tasks != null)
                {
                    Array.Clear(tasks, 0, n);
                    SlotArrayPool<UniTask>.Return(tasks);
                }

                SlotArrayPool<OcclusionWorkItem>.Return(workItems, true);
            }
        }

        private static void ApplyVisibilityRangeSync(LayerData layer, int startInclusive, int endExclusive, int fullscreenIdx)
        {
            NormalizeVisibilityRange(layer, ref startInclusive, ref endExclusive);

            bool showAll = fullscreenIdx < 0;
            UIOcclusionMode occlusionMode = GetOcclusionMode(layer, fullscreenIdx, showAll);

            for (int i = startInclusive; i < endExclusive; i++)
            {
                UIBase view = layer.Items[i]?.View;
                if (view == null)
                {
                    continue;
                }

                bool visible = showAll || i >= fullscreenIdx;
                if (NeedsAsyncOcclusion(view, visible, occlusionMode))
                {
                    ApplyWindowOcclusionSync(view, visible, occlusionMode);
                }
                else
                {
                    view.Visible = visible;
                }
            }
        }

        private static bool NeedsAsyncOcclusion(UIBase view, bool visible, UIOcclusionMode occlusionMode)
        {
            if (visible)
            {
                return view.State == UIState.Closed || view.State == UIState.Closing;
            }

            return occlusionMode == UIOcclusionMode.Lifecycle && (view.State == UIState.Opened || view.State == UIState.Opening);
        }

        private static UIOcclusionMode GetOcclusionMode(LayerData layer, int fullscreenIdx, bool showAll)
        {
            if (showAll || fullscreenIdx < 0 || fullscreenIdx >= layer.Count)
            {
                return UIOcclusionMode.None;
            }

            return layer.Items[fullscreenIdx]?.MetaInfo.OcclusionMode ?? UIOcclusionMode.None;
        }

        private static void NormalizeVisibilityRange(LayerData layer, ref int startInclusive, ref int endExclusive)
        {
            if (startInclusive < 0)
            {
                startInclusive = 0;
            }

            if (endExclusive > layer.Count)
            {
                endExclusive = layer.Count;
            }
        }

        private static bool IsVisibilityVersionCurrent(LayerData layer, int visibilityVersion)
        {
            return layer != null && layer.VisibilityVersion == visibilityVersion;
        }

        private static async UniTask ApplyWindowOcclusionAsync(LayerData layer, int visibilityVersion, UIBase view, bool visible, UIOcclusionMode occlusionMode)
        {
            if (!IsVisibilityVersionCurrent(layer, visibilityVersion))
            {
                return;
            }

            if (visible)
            {
                if (view.State == UIState.Closed || view.State == UIState.Closing)
                {
#if UNITY_EDITOR
                    WarnOcclusionOperation("Occlusion reveal will reopen UI", view, visible, occlusionMode, "The window became visible while it was Closed or Closing. InternalOpen will be called; Closing means the close transition is being interrupted.");
#endif
                    bool openResult = await view.InternalOpen(causedByOcclusion: true);
#if UNITY_EDITOR
                    if (!openResult)
                    {
                        WarnOcclusionOperation("Occlusion reveal reopen failed", view, visible, occlusionMode, "InternalOpen returned false while restoring a previously occluded window.");
                    }
#endif
                    return;
                }

                view.Visible = true;
                return;
            }

            if (occlusionMode == UIOcclusionMode.Lifecycle && (view.State == UIState.Opened || view.State == UIState.Opening))
            {
#if UNITY_EDITOR
                WarnOcclusionOperation("Lifecycle occlusion will close UI", view, visible, occlusionMode, "The occluding window requires Lifecycle mode. InternalClose will be called; Opening means the open transition is being interrupted.");
#endif
                // Occluded windows are already hidden, so lifecycle occlusion skips the close transition.
                bool closeResult = await view.InternalClose(causedByOcclusion: true, skipTransition: true);
#if UNITY_EDITOR
                if (!closeResult)
                {
                    WarnOcclusionOperation("Lifecycle occlusion close failed", view, visible, occlusionMode, "InternalClose returned false while hiding an occluded window.");
                }
#endif
                return;
            }

            view.Visible = false;
        }

        private static void ApplyWindowOcclusionSync(UIBase view, bool visible, UIOcclusionMode occlusionMode)
        {
            if (visible)
            {
                if (view.State == UIState.Closed || view.State == UIState.Closing)
                {
#if UNITY_EDITOR
                    WarnOcclusionOperation("Occlusion reveal will reopen UI sync", view, visible, occlusionMode, "The window became visible while it was Closed or Closing. InternalOpenSync will be called; Closing means the close transition is being interrupted.");
#endif
                    bool openResult = view.InternalOpenSync(causedByOcclusion: true);
#if UNITY_EDITOR
                    if (!openResult)
                    {
                        WarnOcclusionOperation("Occlusion reveal reopen sync failed", view, visible, occlusionMode, "InternalOpenSync returned false while restoring a previously occluded window.");
                    }
#endif
                    return;
                }

                view.Visible = true;
                return;
            }

            if (occlusionMode == UIOcclusionMode.Lifecycle && (view.State == UIState.Opened || view.State == UIState.Opening))
            {
#if UNITY_EDITOR
                WarnOcclusionOperation("Lifecycle occlusion will close UI sync", view, visible, occlusionMode, "The occluding window requires Lifecycle mode. InternalCloseSync will be called; Opening means the open transition is being interrupted.");
#endif
                bool closeResult = view.InternalCloseSync(causedByOcclusion: true);
#if UNITY_EDITOR
                if (!closeResult)
                {
                    WarnOcclusionOperation("Lifecycle occlusion close sync failed", view, visible, occlusionMode, "InternalCloseSync returned false while hiding an occluded window.");
                }
#endif
                return;
            }

            view.Visible = false;
        }

        private static void UpdateFullscreenIndexAfterRemove(LayerData layer, UIMetadata removedMeta, int removedIndex)
        {
            if (layer.Count == 0)
            {
                layer.LastFullscreenIndex = -1;
                return;
            }

            if (IsOcclusionWindow(removedMeta) && layer.LastFullscreenIndex == removedIndex)
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

            if (!IsOcclusionWindow(meta))
            {
                return;
            }

            if (fromIndex < layer.LastFullscreenIndex)
            {
                layer.LastFullscreenIndex--;
            }

            layer.LastFullscreenIndex = Math.Max(layer.LastFullscreenIndex, toIndex);
        }

        private async UniTask RollbackFailedShowAsync(UIMetadata meta, int operationVersion, int previousFullscreenIndex = int.MinValue)
        {
            if (meta == null || meta.OperationVersion != operationVersion)
            {
#if UNITY_EDITOR
                WarnUIOperation("Rollback skipped", meta, operationVersion, "RollbackFailedShowAsync was called, but the metadata was null or the operation version no longer matched.");
#endif
                return;
            }

#if UNITY_EDITOR
            WarnUIOperation("Rollback show async", meta, operationVersion, "The show operation failed and the UI will be popped, visibility will be recalculated, and the metadata will be disposed.");
#endif
            var popResult = Pop(meta);
            int rollbackPreviousFullscreenIndex = previousFullscreenIndex == int.MinValue
                ? popResult.previousFullscreenIndex
                : previousFullscreenIndex;
            await SortWindowVisibleAsync(meta.MetaInfo.UILayer, rollbackPreviousFullscreenIndex);
            SortWindowDepth(meta.MetaInfo.UILayer, popResult.removedIndex >= 0 ? popResult.removedIndex : 0);

            await meta.DisposeAsync();
        }

        private void RollbackFailedShow(UIMetadata meta, int operationVersion)
        {
            if (meta == null || meta.OperationVersion != operationVersion)
            {
#if UNITY_EDITOR
                WarnUIOperation("Rollback skipped sync", meta, operationVersion, "RollbackFailedShow was called, but the metadata was null or the operation version no longer matched.");
#endif
                return;
            }

#if UNITY_EDITOR
            WarnUIOperation("Rollback show sync", meta, operationVersion, "The sync show operation failed and the UI will be popped, visibility will be recalculated, and the metadata will be disposed immediately.");
#endif
            var popResult = Pop(meta);
            SortWindowVisibleSync(meta.MetaInfo.UILayer, popResult.previousFullscreenIndex);
            SortWindowDepth(meta.MetaInfo.UILayer, popResult.removedIndex >= 0 ? popResult.removedIndex : 0);

            meta.DisposeImmediate();
        }

        private void AddUpdateableWindow(UIMetadata meta)
        {
            if (meta == null || !meta.MetaInfo.NeedUpdate)
            {
                return;
            }

            for (int i = 0; i < _updateableWindowCount; i++)
            {
                if (_updateableWindows[i] == meta)
                {
                    return;
                }
            }

            if (_updateableWindowCount >= _updateableWindows.Length)
            {
                Array.Resize(ref _updateableWindows, _updateableWindows.Length << 1);
            }

            _updateableWindows[_updateableWindowCount++] = meta;
        }

        private void RemoveUpdateableWindow(UIMetadata meta)
        {
            if (meta == null || !meta.MetaInfo.NeedUpdate)
            {
                return;
            }

            for (int i = 0; i < _updateableWindowCount; i++)
            {
                if (_updateableWindows[i] != meta)
                {
                    continue;
                }

                int lastIndex = _updateableWindowCount - 1;
                _updateableWindows[i] = _updateableWindows[lastIndex];
                _updateableWindows[lastIndex] = null;
                _updateableWindowCount = lastIndex;
                return;
            }
        }

#if UNITY_EDITOR
        private static void WarnUIOperation(string title, UIMetadata meta, int expectedOperationVersion, string reason)
        {
            if (!UIWarningSettings.OtherWarningsEnabled)
            {
                return;
            }

            Log.Warning($"[UI] {title}. Reason: {reason} ExpectedVersion={expectedOperationVersion}. {FormatMetadata(meta)}");
        }

        private static void WarnOcclusionInterruption(string title, UIMetadata meta, int expectedOperationVersion, string reason)
        {
            if (!UIWarningSettings.OcclusionWarningsEnabled)
            {
                return;
            }

            Log.Warning($"[UI] {title}. Reason: {reason} ExpectedVersion={expectedOperationVersion}. {FormatMetadata(meta)}");
        }

        private static void WarnOcclusionOperation(string title, UIBase view, bool targetVisible, UIOcclusionMode occlusionMode, string reason)
        {
            if (!UIWarningSettings.OcclusionWarningsEnabled)
            {
                return;
            }

            Log.Warning($"[UI] {title}. Reason: {reason} TargetVisible={targetVisible}, OcclusionMode={occlusionMode}. {FormatView(view)}");
        }

        private static string GetInvalidShowReason(UIMetadata meta, int expectedOperationVersion)
        {
            if (meta == null)
            {
                return "Metadata is null after resource creation.";
            }

            if (meta.OperationVersion != expectedOperationVersion)
            {
                return $"Operation version changed while loading resources. ActualVersion={meta.OperationVersion}.";
            }

            if (meta.View == null)
            {
                return "View is null after resource creation.";
            }

            if (meta.State == UIState.Uninitialized || meta.State == UIState.Destroyed)
            {
                return $"State became {meta.State} after resource creation.";
            }

            if (meta.State == UIState.CreatedUI)
            {
                return "State is still CreatedUI after resource creation. The UI resource was not bound, so rollback will remove the reserved stack slot.";
            }

            return "Show operation became invalid after resource creation.";
        }

        private static string GetOpenInterruptionRejectReason(UIMetadata meta, int expectedOperationVersion)
        {
            if (meta == null)
            {
                return "InternalOpen returned false and metadata is null. Rollback will run.";
            }

            if (meta.OperationVersion != expectedOperationVersion)
            {
                return $"InternalOpen returned false and the operation version changed. ActualVersion={meta.OperationVersion}. Rollback will run.";
            }

            if (meta.View == null)
            {
                return "InternalOpen returned false and View is null. Rollback will run.";
            }

            return "InternalOpen returned false before a recoverable occlusion state could be confirmed. Rollback will run.";
        }

        private static string FormatMetadata(UIMetadata meta)
        {
            if (meta == null)
            {
                return "Meta=null.";
            }

            string viewState = meta.View == null ? "View=null" : $"ViewState={meta.View.State}, Visible={meta.View.Visible}, Depth={meta.View.Depth}";
            return $"UI={meta.UILogicTypeName}, State={meta.State}, {viewState}, ActualVersion={meta.OperationVersion}, CancelRequested={meta.CancelRequested}, ShowInProgress={meta.ShowInProgress}, CloseInProgress={meta.CloseInProgress}, InCache={meta.InCache}, Layer={(UILayer)meta.MetaInfo.UILayer}, TypeId={meta.MetaInfo.TypeId}, Occlusion={meta.MetaInfo.OcclusionMode}.";
        }

        private static string FormatView(UIBase view)
        {
            if (view == null)
            {
                return "View=null.";
            }

            return $"UI={view.GetType().Name}, State={view.State}, Visible={view.Visible}, Depth={view.Depth}.";
        }
#endif
    }
}
