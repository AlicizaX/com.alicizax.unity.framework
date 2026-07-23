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
                return UIFinalizeClosedResult.Fail(int.MinValue, UICloseFailureReason.FinalizeFailed);
            }

            int layerIndex = meta.MetaInfo.UILayer;
            var popResult = Pop(meta);
            if (popResult.removedIndex < 0)
            {
                return UIFinalizeClosedResult.Fail(popResult.previousFullscreenIndex, UICloseFailureReason.FinalizeFailed);
            }

            if (refreshVisual)
            {
                try
                {
                    await SortWindowVisibleAsync(layerIndex, popResult.previousFullscreenIndex);
                    SortWindowDepth(layerIndex, popResult.removedIndex);
                }
                catch
                {
                    MarkLayerVisualDirty(layerIndex);
                    return new UIFinalizeClosedResult(false, popResult.removedIndex, popResult.previousFullscreenIndex, UICloseFailureReason.VisibilityRefreshFailed);
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

            return new UIFinalizeClosedResult(true, popResult.removedIndex, popResult.previousFullscreenIndex, UICloseFailureReason.None);
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

        private async UniTask<UIShowResult> RunPreparedShowVisualAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            UniTaskCompletionSource<UIBase> showCompletionSource,
            int layerIndex)
        {
            UIShowResult showResult = UIShowResult.Failed;
            int rollbackPreviousFullscreenIndex = int.MinValue;
            bool exceptionThrown = false;
            try
            {
                UniTask<bool> openTask = meta.View.InternalOpen();
                rollbackPreviousFullscreenIndex = PromoteOcclusionWindow(meta);
                await SortWindowVisibleAsync(meta.MetaInfo.UILayer, rollbackPreviousFullscreenIndex);
                bool openResult = await openTask;

                showResult = openResult
                    ? IsStaleShowOperation(meta, operationVersion)
                        ? UIShowResult.Cancelled
                        : new UIShowResult(meta.View, UIShowResultState.Opened)
                    : IsShowAcceptedAfterOpenInterruption(meta, operationVersion);

                if (!showResult.IsAccepted && !IsStaleShowOperation(meta, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("Show visual state failed, rollback will run", meta, operationVersion, "The UI did not reach a valid opened or occlusion-interrupted state.");
#endif
                    await RollbackFailedShowAsync(meta, operationVersion, rollbackPreviousFullscreenIndex);
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
                    await RollbackFailedShowAsync(meta, operationVersion, rollbackPreviousFullscreenIndex);
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
            public UIMetadata Meta;
            public bool Visible;

            public OcclusionWorkItem(UIMetadata meta, bool visible)
            {
                Meta = meta;
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

                UIMetadata meta = layer.Items[i];
                UIBase view = meta?.View;
                if (view == null)
                {
                    continue;
                }

                bool visible = showAll || i >= fullscreenIdx;
                if (meta.StackRemovalPending)
                {
                    view.Visible = false;
                    continue;
                }

                if (NeedsAsyncOcclusion(meta, visible, occlusionMode))
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

                    UIMetadata meta = layer.Items[i];
                    bool visible = showAll || i >= fullscreenIdx;
                    if (NeedsAsyncOcclusion(meta, visible, occlusionMode))
                    {
                        await ApplyWindowOcclusionAsync(layer, visibilityVersion, meta, visible, occlusionMode);
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

                    UIMetadata meta = layer.Items[i];
                    bool visible = showAll || i >= fullscreenIdx;
                    if (NeedsAsyncOcclusion(meta, visible, occlusionMode))
                    {
                        workItems[n++] = new OcclusionWorkItem(meta, visible);
                    }
                }

                if (n == 0)
                {
                    return;
                }

                if (n == 1)
                {
                    OcclusionWorkItem workItem = workItems[0];
                    await ApplyWindowOcclusionAsync(layer, visibilityVersion, workItem.Meta, workItem.Visible, occlusionMode);
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
                    tasks[i] = ApplyWindowOcclusionAsync(layer, visibilityVersion, workItem.Meta, workItem.Visible, occlusionMode);
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

        private static bool NeedsAsyncOcclusion(UIMetadata meta, bool visible, UIOcclusionMode occlusionMode)
        {
            UIBase view = meta?.View;
            if (view == null || meta.StackRemovalPending)
            {
                return false;
            }

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

        private static async UniTask ApplyWindowOcclusionAsync(LayerData layer, int visibilityVersion, UIMetadata meta, bool visible, UIOcclusionMode occlusionMode)
        {
            if (!IsVisibilityVersionCurrent(layer, visibilityVersion) || meta == null || meta.StackRemovalPending)
            {
                return;
            }

            UIBase view = meta.View;
            if (view == null)
            {
                return;
            }

            if (visible)
            {
                if (view.State == UIState.Closed || view.State == UIState.Closing)
                {
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
            if (!CanRollbackShow(meta, operationVersion))
            {
                return;
            }

            var popResult = Pop(meta);
            int rollbackPreviousFullscreenIndex = previousFullscreenIndex == int.MinValue
                ? popResult.previousFullscreenIndex
                : previousFullscreenIndex;
            await SortWindowVisibleAsync(meta.MetaInfo.UILayer, rollbackPreviousFullscreenIndex);
            SortWindowDepth(meta.MetaInfo.UILayer, popResult.removedIndex >= 0 ? popResult.removedIndex : 0);

            await meta.DisposeAsync();
        }

        private bool CanRollbackShow(UIMetadata meta, int operationVersion)
        {
            return meta != null
                   && meta.OperationVersion == operationVersion
                   && IsMetaInOpenStack(meta);
        }

        internal async UniTask<bool> RebuildLayerVisualStateAsync(UILayer layer)
        {
            int layerIndex = (int)layer;
            if ((uint)layerIndex >= (uint)_openUI.Length || _layerMutationBusy[layerIndex])
            {
                return false;
            }

            _layerMutationBusy[layerIndex] = true;
            try
            {
                LayerData layerData = _openUI[layerIndex];
                if (layerData == null)
                {
                    _layerVisualDirty[layerIndex] = false;
                    return true;
                }

                layerData.LastFullscreenIndex = FindLastFullscreenIndex(layerData, layerData.Count - 1);
                ClearStackRemovalPendingOnLayer(layerData);
                await SortWindowVisibleAsync(layerIndex, int.MinValue);
                SortWindowDepth(layerIndex, 0);
                _layerVisualDirty[layerIndex] = false;
                return true;
            }
            catch
            {
                _layerVisualDirty[layerIndex] = true;
                return false;
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
