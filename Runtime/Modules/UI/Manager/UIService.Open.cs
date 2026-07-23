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

        public LayerData(int initialCapacity)
        {
            Items = new UIMetadata[initialCapacity];
            TypeIdToIndex = UITypeIndexArray.Create(initialCapacity);

            Count = 0;
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
        private struct PendingLayerClose
        {
            public RuntimeTypeHandle Handle;
            public int TypeId;
            public bool Force;
        }

        private readonly LayerData[] _openUI = new LayerData[(int)UILayer.All];
        private readonly bool[] _layerMutationBusy = new bool[(int)UILayer.All];
        private readonly bool[] _layerVisualDirty = new bool[(int)UILayer.All];
        private readonly PendingLayerClose[][] _layerPendingCloses = CreateLayerPendingCloseBuffers();
        private readonly int[] _layerPendingCloseCounts = new int[(int)UILayer.All];

        private static PendingLayerClose[][] CreateLayerPendingCloseBuffers()
        {
            var buffers = new PendingLayerClose[(int)UILayer.All][];
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = new PendingLayerClose[4];
            }

            return buffers;
        }

        private bool IsLayerBlockedForMutation(int layer)
        {
            return (uint)layer >= (uint)_openUI.Length
                   || _layerMutationBusy[layer]
                   || _layerVisualDirty[layer];
        }

        private UICloseFailureReason GetLayerBlockedReason(int layer)
        {
            return (uint)layer < (uint)_layerVisualDirty.Length && _layerVisualDirty[layer]
                ? UICloseFailureReason.LayerVisualDirty
                : UICloseFailureReason.LayerTransactionBusy;
        }

        private bool TryBeginLayerMutation(int layer)
        {
            if (IsLayerBlockedForMutation(layer))
            {
                return false;
            }

            _layerMutationBusy[layer] = true;
            return true;
        }

        private void EndLayerMutation(int layer)
        {
            if ((uint)layer >= (uint)_layerMutationBusy.Length)
            {
                return;
            }

            _layerMutationBusy[layer] = false;
            DrainLayerCloseQueue(layer);
        }

        private bool TryEnqueueLayerClose(UIMetadata meta, bool force)
        {
            if (meta == null)
            {
                return false;
            }

            int layer = meta.MetaInfo.UILayer;
            if ((uint)layer >= (uint)_layerPendingCloses.Length)
            {
                return false;
            }

            int typeId = meta.MetaInfo.TypeId;
            PendingLayerClose[] buffer = _layerPendingCloses[layer];
            int count = _layerPendingCloseCounts[layer];

            for (int i = 0; i < count; i++)
            {
                if (buffer[i].TypeId != typeId)
                {
                    continue;
                }

                if (force && !buffer[i].Force)
                {
                    buffer[i].Force = true;
                }

                if (meta.ShowInProgress)
                {
                    meta.RequestCancelShowLoad();
                }

                return true;
            }

            if (count >= buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length << 1);
                _layerPendingCloses[layer] = buffer;
            }

            buffer[count] = new PendingLayerClose
            {
                Handle = meta.MetaInfo.RuntimeTypeHandle,
                TypeId = typeId,
                Force = force,
            };
            _layerPendingCloseCounts[layer] = count + 1;

            if (meta.ShowInProgress)
            {
                meta.RequestCancelShowLoad();
            }

            return true;
        }

        private void DrainLayerCloseQueue(int layer)
        {
            if ((uint)layer >= (uint)_layerPendingCloseCounts.Length)
            {
                return;
            }

            // 同步失败的 Close 不会重新占锁，需要继续取下一项；
            // 一旦某项成功 BeginMutation（busy=true），交给该事务的 EndLayerMutation 再继续 drain。
            while (_layerPendingCloseCounts[layer] > 0)
            {
                if (_layerMutationBusy[layer] || _layerVisualDirty[layer])
                {
                    return;
                }

                PendingLayerClose[] buffer = _layerPendingCloses[layer];
                PendingLayerClose entry = buffer[0];
                int count = _layerPendingCloseCounts[layer];
                for (int i = 1; i < count; i++)
                {
                    buffer[i - 1] = buffer[i];
                }

                buffer[count - 1] = default;
                _layerPendingCloseCounts[layer] = count - 1;

                // UniTask 会同步执行到第一个 await；成功占锁后 busy 已为 true。
                CloseUIAsyncCore(entry.Handle, entry.Force).Forget();
            }
        }

        private void ClearAllLayerCloseQueues()
        {
            for (int layer = 0; layer < _layerPendingCloseCounts.Length; layer++)
            {
                PendingLayerClose[] buffer = _layerPendingCloses[layer];
                int count = _layerPendingCloseCounts[layer];
                if (count > 0)
                {
                    Array.Clear(buffer, 0, count);
                }

                _layerPendingCloseCounts[layer] = 0;
            }
        }

        private void MarkLayerVisualDirty(int layer)
        {
            if ((uint)layer < (uint)_layerVisualDirty.Length)
            {
                _layerVisualDirty[layer] = true;
            }
        }

        private bool IsMetaInOpenStack(UIMetadata meta)
        {
            if (meta == null)
            {
                return false;
            }

            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            return layer != null && GetOpenIndex(layer, meta) >= 0;
        }

        private async UniTask<UIShowResult> ShowUIImplAsync(UIMetadata metaInfo, object[] userDatas)
        {
            CreateMetaUI(metaInfo);
            int layerIndex = metaInfo.MetaInfo.UILayer;
            if (IsLayerBlockedForMutation(layerIndex) && !metaInfo.ShowInProgress)
            {
                return UIShowResult.Failed;
            }

            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
                metaInfo.SetPendingShowUserDatas(userDatas);
                UIBase joinedView = await metaInfo.WaitForShowOperationAsync();
                return CreateShowResultFromView(joinedView);
            }

            // 鍦?Begin 涔嬪悗鍙崟鑾蜂竴娆′护鐗岋細鍚庣画鏂版搷浣滀細鏇挎崲 CTS锛屽啀璇诲睘鎬т細鎷垮埌閿欒浠ょ墝 鐗瑰埆娉ㄦ剰
            if (!TryBeginLayerMutation(layerIndex))
            {
                metaInfo.EndShowOperation(operationVersion, loadCts);
                metaInfo.CompleteShowOperation(showCompletionSource, null);
                loadCts.Dispose();
                return UIShowResult.Failed;
            }

            CancellationToken cancellationToken = loadCts.Token;
            UIShowResult showResult = UIShowResult.Failed;
            bool exceptionThrown = false;
            bool visualStarted = false;
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
                    SortWindowDepth(metaInfo.MetaInfo.UILayer);
                    if (metaInfo.State == UIState.Loaded && !await metaInfo.View.InternalInitlized(metaInfo, operationVersion))
                    {
#if UNITY_EDITOR
                        WarnUIOperation("Show initialization interrupted", metaInfo, operationVersion, "InternalInitlized returned false. The show operation will be treated as failed.");
#endif
                        showResult = UIShowResult.Failed;
                    }
                    else if (metaInfo.OperationVersion != operationVersion)
                    {
                        showResult = UIShowResult.Cancelled;
                    }
                    else
                    {
                        visualStarted = true;
                        return await RunPreparedShowVisualAsync(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex);
                    }
                }

                if (!showResult.IsAccepted && !IsStaleShowOperation(metaInfo, operationVersion) && ShouldRollbackInvalidShow(metaInfo, operationVersion))
                {
                    await RollbackFailedShowAsync(metaInfo, operationVersion);
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
                    await RollbackFailedShowAsync(metaInfo, operationVersion);
                }

                metaInfo.FailShowOperation(showCompletionSource, exception);
                throw;
            }
            finally
            {
                if (!visualStarted)
                {
                    UIBase result = showResult.IsAccepted ? metaInfo.View : null;
                    metaInfo.EndShowOperation(operationVersion, loadCts);
                    EndLayerMutation(layerIndex);
                    if (!exceptionThrown)
                    {
                        metaInfo.CompleteShowOperation(showCompletionSource, result);
                    }
                    loadCts.Dispose();
                }
            }

            return showResult;
        }
        private UIBase ShowUIImplSync(UIMetadata metaInfo, object[] userDatas)
        {
            CreateMetaUI(metaInfo);
            int layerIndex = metaInfo.MetaInfo.UILayer;
            if (IsLayerBlockedForMutation(layerIndex) && !metaInfo.ShowInProgress)
            {
                return null;
            }

            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource))
            {
                metaInfo.SetPendingShowUserDatas(userDatas);
                return metaInfo.View;
            }

            if (!TryBeginLayerMutation(layerIndex))
            {
                metaInfo.EndShowOperation(operationVersion, loadCts);
                metaInfo.CompleteShowOperation(showCompletionSource, null);
                loadCts.Dispose();
                return null;
            }

            try
            {
                UIHolderFactory.CreateUIResourceSync(metaInfo, UICacheLayer);
                if (!IsShowValidAfterResourceCreation(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("ShowSync invalid after resource creation", metaInfo, operationVersion, GetInvalidShowReason(metaInfo, operationVersion));
#endif
                    CompletePreparedShowFailureBeforeStackImmediate(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex);
                    return null;
                }

                FinalizeShow(metaInfo, userDatas);
                SortWindowDepth(metaInfo.MetaInfo.UILayer);
                if (metaInfo.State == UIState.Loaded && !metaInfo.View.InternalInitlizedSync(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("ShowSync initialization failed", metaInfo, operationVersion, "InternalInitlizedSync returned false. The show operation will rollback.");
#endif
                    CompletePreparedShowFailureAsync(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex).Forget();
                    return null;
                }

                if (metaInfo.OperationVersion != operationVersion)
                {
                    CompletePreparedShowFailureAsync(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex).Forget();
                    return null;
                }

                UIBase view = metaInfo.View;
                RunPreparedShowVisualAsync(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex).Forget();
                return view;
            }
            catch (Exception exception)
            {
#if UNITY_EDITOR
                if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("ShowSync exception, rollback will run", metaInfo, operationVersion, $"Exception={exception.GetType().Name}: {exception.Message}");
#endif
                if (ShouldRollbackInvalidShow(metaInfo, operationVersion))
                {
                    CompletePreparedShowFailureAsync(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex).Forget();
                }
                else
                {
                    CompletePreparedShowFailureBeforeStackImmediate(metaInfo, operationVersion, loadCts, showCompletionSource, layerIndex);
                }

                throw;
            }
        }
        private async UniTask<bool> CloseUIImplCore(UIMetadata meta, bool force)
        {
            if (meta == null || meta.State == UIState.Uninitialized || meta.State == UIState.Destroying || meta.State == UIState.Destroyed)
            {
                return false;
            }

            int layerIndex = meta.MetaInfo.UILayer;
            if (IsLayerBlockedForMutation(layerIndex))
            {

                if ((uint)layerIndex < (uint)_layerMutationBusy.Length
                    && _layerMutationBusy[layerIndex]
                    && !_layerVisualDirty[layerIndex])
                {
                    return TryEnqueueLayerClose(meta, force);
                }

                return false;
            }

            if (!TryBeginLayerMutation(layerIndex))
            {
                if ((uint)layerIndex < (uint)_layerMutationBusy.Length
                    && _layerMutationBusy[layerIndex]
                    && !_layerVisualDirty[layerIndex])
                {
                    return TryEnqueueLayerClose(meta, force);
                }

                return false;
            }

            bool interruptedShow = meta.ShowInProgress;
            if (!meta.BeginCloseOperation(out int operationVersion))
            {
                EndLayerMutation(layerIndex);
                return false;
            }

            bool closeCompleted = false;
            try
            {
                UIState state = meta.State;
                UIFinalizeClosedMode finalizeMode = GetFinalizeModeForExplicitClose(meta, interruptedShow);
                if (state == UIState.CreatedUI)
                {
                    UIFinalizeClosedResult finalizeResult = await FinalizeClosedWindowAsync(meta, force, finalizeMode, refreshVisual: true);
                    closeCompleted = finalizeResult.Success;
                }
                else if (state == UIState.Loaded || state == UIState.Initialized || state == UIState.Closed)
                {
                    if (meta.View != null)
                    {
                        meta.View.Visible = false;
                    }

                    UIFinalizeClosedResult finalizeResult = await FinalizeClosedWindowAsync(meta, force, finalizeMode, refreshVisual: true);
                    closeCompleted = finalizeResult.Success;
                }
                else
                {
                    bool closeResult = await meta.View.InternalClose();
                    if (closeResult && meta.State == UIState.Closed && meta.OperationVersion == operationVersion)
                    {
                        UIFinalizeClosedResult finalizeResult = await FinalizeClosedWindowAsync(meta, force, finalizeMode, refreshVisual: true);
                        closeCompleted = finalizeResult.Success;
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
                EndLayerMutation(layerIndex);
            }

            return closeCompleted;
        }

        private async UniTask<UIFinalizeClosedResult> FinalizeClosedWindowAsync(
            UIMetadata meta,
            bool force,
            UIFinalizeClosedMode mode,
            bool refreshVisual)
        {
            if (meta == null)
            {
                return UIFinalizeClosedResult.Fail(UICloseFailureReason.FinalizeFailed);
            }

            int layerIndex = meta.MetaInfo.UILayer;
            int removedIndex = Pop(meta);
            if (removedIndex < 0)
            {
                return UIFinalizeClosedResult.Fail(UICloseFailureReason.FinalizeFailed);
            }

            if (refreshVisual)
            {
                try
                {
                    SortWindowDepth(layerIndex, removedIndex);
                }
                catch
                {
                    MarkLayerVisualDirty(layerIndex);
                    return new UIFinalizeClosedResult(false, removedIndex, UICloseFailureReason.VisibilityRefreshFailed);
                }
            }

            if (mode == UIFinalizeClosedMode.Dispose)
            {
                await meta.DisposeAsync();
            }
            else
            {
                CacheWindow(meta, force);
            }

            return new UIFinalizeClosedResult(true, removedIndex, UICloseFailureReason.None);
        }

        private static UIFinalizeClosedMode GetFinalizeModeForExplicitClose(UIMetadata meta, bool interruptedShow)
        {
            if (meta == null)
            {
                return UIFinalizeClosedMode.Dispose;
            }

            UIState state = meta.State;
            if (state == UIState.CreatedUI || interruptedShow)
            {
                return UIFinalizeClosedMode.Dispose;
            }

            return UIFinalizeClosedMode.Cache;
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
                if (IsLayerBlockedForMutation(layerIndex))
                {
                    return false;
                }

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

        public bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder)
        {
            holder = null;

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
                    if (!IsTopVisibleHolderCandidate(metadata))
                    {
                        continue;
                    }

                    UIHolderObjectBase candidate = metadata.View.Holder;
                    if (candidate == null || !candidate.IsValid())
                    {
                        continue;
                    }

                    bool accepted;
                    try
                    {
                        accepted = predicate == null || predicate(candidate);
                    }
                    catch (Exception)
                    {
                        accepted = false;
                    }

                    if (!accepted)
                    {
                        continue;
                    }

                    holder = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsTopVisibleHolderCandidate(UIMetadata metadata)
        {
            if (metadata == null
                || metadata.View == null
                || metadata.StackRemovalPending
                || !UIStateMachine.IsDisplayActive(metadata.State))
            {
                return false;
            }

            return metadata.View.Visible;
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
                // 缂撳瓨鏃跺叧闂簡 Canvas 娓叉煋锛岄噸鏂版樉绀哄墠鎭㈠
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

                AddUpdateableWindow(meta);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Pop(UIMetadata meta)
        {
            var layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            if ((uint)typeId < (uint)layer.TypeIdToIndex.Length)
            {
                int index = layer.TypeIdToIndex[typeId];
                if (index < 0)
                {
                    return -1;
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

                return index;
            }

            return -1;
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
            }
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
            bool accepted = UIStateMachine.IsDisplayActive(state);
#if UNITY_EDITOR
            WarnUIOperation(
                accepted ? "Show open interruption accepted" : "Show open interruption rejected",
                meta,
                operationVersion,
                accepted
                    ? "InternalOpen returned false, but the UI is still in the open stack with a recoverable display state. Rollback is skipped."
                    : "InternalOpen returned false and the final state is not recoverable. Rollback will run.");
#endif
            return accepted
                ? new UIShowResult(meta.View, UIShowResultState.Opened)
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

            return view.State == UIState.Opened
                ? new UIShowResult(view, UIShowResultState.Opened)
                : UIShowResult.Failed;
        }

        private async UniTask<UIShowResult> RunPreparedShowVisualAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            UniTaskCompletionSource<UIBase> showCompletionSource,
            int layerIndex)
        {
            UIShowResult showResult = UIShowResult.Failed;
            bool exceptionThrown = false;
            try
            {
                bool openResult = await meta.View.InternalOpen();

                showResult = openResult
                    ? IsStaleShowOperation(meta, operationVersion)
                        ? UIShowResult.Cancelled
                        : new UIShowResult(meta.View, UIShowResultState.Opened)
                    : IsShowAcceptedAfterOpenInterruption(meta, operationVersion);

                if (!showResult.IsAccepted && !IsStaleShowOperation(meta, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("Show visual state failed, rollback will run", meta, operationVersion, "The UI did not reach a valid opened state.");
#endif
                    await RollbackFailedShowAsync(meta, operationVersion);
                }
            }
            catch (Exception exception)
            {
                exceptionThrown = true;
#if UNITY_EDITOR
                if (UIWarningSettings.OtherWarningsEnabled) WarnUIOperation("Show visual exception, rollback will run", meta, operationVersion, $"Exception={exception.GetType().Name}: {exception.Message}");
#endif
                if (ShouldRollbackInvalidShow(meta, operationVersion))
                {
                    await RollbackFailedShowAsync(meta, operationVersion);
                }

                meta.FailShowOperation(showCompletionSource, exception);
                throw;
            }
            finally
            {
                UIBase result = showResult.IsAccepted ? meta.View : null;
                meta.EndShowOperation(operationVersion, loadCts);
                EndLayerMutation(layerIndex);
                if (!exceptionThrown)
                {
                    meta.CompleteShowOperation(showCompletionSource, result);
                }
                loadCts.Dispose();
            }

            return showResult;
        }

        private async UniTaskVoid CompletePreparedShowFailureAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            UniTaskCompletionSource<UIBase> showCompletionSource,
            int layerIndex)
        {
            try
            {
                if (ShouldRollbackInvalidShow(meta, operationVersion))
                {
                    await RollbackFailedShowAsync(meta, operationVersion);
                }
            }
            finally
            {
                meta?.EndShowOperation(operationVersion, loadCts);
                EndLayerMutation(layerIndex);
                meta?.CompleteShowOperation(showCompletionSource, null);
                loadCts?.Dispose();
            }
        }

        private void CompletePreparedShowFailureBeforeStackImmediate(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            UniTaskCompletionSource<UIBase> showCompletionSource,
            int layerIndex)
        {
            try
            {
                if (meta != null && meta.OperationVersion == operationVersion && !ShouldRollbackInvalidShow(meta, operationVersion))
                {
                    meta.DisposeImmediate();
                }
            }
            finally
            {
                meta?.EndShowOperation(operationVersion, loadCts);
                EndLayerMutation(layerIndex);
                meta?.CompleteShowOperation(showCompletionSource, null);
                loadCts?.Dispose();
            }
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

        private async UniTask RollbackFailedShowAsync(UIMetadata meta, int operationVersion)
        {
            if (!CanRollbackShow(meta, operationVersion))
            {
                return;
            }

            int removed = Pop(meta);
            SortWindowDepth(meta.MetaInfo.UILayer, removed >= 0 ? removed : 0);

            await meta.DisposeAsync();
        }

        private bool CanRollbackShow(UIMetadata meta, int operationVersion)
        {
            return meta != null
                   && meta.OperationVersion == operationVersion
                   && IsMetaInOpenStack(meta);
        }

        internal UniTask<bool> RebuildLayerVisualStateAsync(UILayer layer)
        {
            int layerIndex = (int)layer;
            if ((uint)layerIndex >= (uint)_openUI.Length || _layerMutationBusy[layerIndex])
            {
                return UniTask.FromResult(false);
            }

            _layerMutationBusy[layerIndex] = true;
            try
            {
                LayerData layerData = _openUI[layerIndex];
                if (layerData == null)
                {
                    _layerVisualDirty[layerIndex] = false;
                    return UniTask.FromResult(true);
                }

                ClearStackRemovalPendingOnLayer(layerData);
                SortWindowDepth(layerIndex, 0);
                _layerVisualDirty[layerIndex] = false;
                return UniTask.FromResult(true);
            }
            catch
            {
                _layerVisualDirty[layerIndex] = true;
                return UniTask.FromResult(false);
            }
            finally
            {
                EndLayerMutation(layerIndex);
            }
        }

        private static void ClearStackRemovalPendingOnLayer(LayerData layerData)
        {
            if (layerData == null)
            {
                return;
            }

            for (int i = 0; i < layerData.Count; i++)
            {
                UIMetadata meta = layerData.Items[i];
                if (meta != null)
                {
                    meta.StackRemovalPending = false;
                }
            }
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

            return "InternalOpen returned false before a recoverable display state could be confirmed. Rollback will run.";
        }

        private static string FormatMetadata(UIMetadata meta)
        {
            if (meta == null)
            {
                return "Meta=null.";
            }

            string viewState = meta.View == null ? "View=null" : $"ViewState={meta.View.State}, Visible={meta.View.Visible}, Depth={meta.View.Depth}";
            return $"UI={meta.UILogicTypeName}, State={meta.State}, {viewState}, ActualVersion={meta.OperationVersion}, CancelRequested={meta.CancelRequested}, ShowInProgress={meta.ShowInProgress}, CloseInProgress={meta.CloseInProgress}, InCache={meta.InCache}, Layer={(UILayer)meta.MetaInfo.UILayer}, TypeId={meta.MetaInfo.TypeId}.";
        }
#endif
    }
}
