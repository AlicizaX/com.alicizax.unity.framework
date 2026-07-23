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
        private readonly int[] _layerPendingCloseHeads = new int[(int)UILayer.All];

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
            if ((uint)layer >= (uint)_openUI.Length)
            {
                return true;
            }

            // dirty 时先 best-effort 恢复，避免永久拒事务。
            if (_layerVisualDirty[layer] && !TryEnsureLayerNotVisuallyDirty(layer))
            {
                return true;
            }

            return _layerMutationBusy[layer] || _layerVisualDirty[layer];
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

            // 事务结束后再尝试恢复 dirty，避免半截深度状态永久锁层。
            if (_layerVisualDirty[layer])
            {
                TryRecoverLayerVisualState(layer);
            }

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
            int head = _layerPendingCloseHeads[layer];
            int count = _layerPendingCloseCounts[layer];

            for (int i = 0; i < count; i++)
            {
                int index = head + i;
                if (buffer[index].TypeId != typeId)
                {
                    continue;
                }

                if (force && !buffer[index].Force)
                {
                    buffer[index].Force = true;
                }

                if (meta.ShowInProgress)
                {
                    meta.RequestCancelShowLoad();
                }

                return true;
            }

            CompactLayerCloseQueueIfNeeded(layer);
            head = _layerPendingCloseHeads[layer];
            count = _layerPendingCloseCounts[layer];
            buffer = _layerPendingCloses[layer];

            int writeIndex = head + count;
            if (writeIndex >= buffer.Length)
            {
                int newSize = Math.Max(buffer.Length << 1, writeIndex + 1);
                Array.Resize(ref buffer, newSize);
                _layerPendingCloses[layer] = buffer;
            }

            buffer[writeIndex] = new PendingLayerClose
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

        private void CompactLayerCloseQueueIfNeeded(int layer)
        {
            int head = _layerPendingCloseHeads[layer];
            if (head == 0)
            {
                return;
            }

            PendingLayerClose[] buffer = _layerPendingCloses[layer];
            int count = _layerPendingCloseCounts[layer];
            if (count > 0)
            {
                Array.Copy(buffer, head, buffer, 0, count);
            }

            Array.Clear(buffer, count, head);
            _layerPendingCloseHeads[layer] = 0;
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
                int head = _layerPendingCloseHeads[layer];
                PendingLayerClose entry = buffer[head];
                buffer[head] = default;
                _layerPendingCloseHeads[layer] = head + 1;
                _layerPendingCloseCounts[layer]--;

                if (_layerPendingCloseCounts[layer] == 0)
                {
                    _layerPendingCloseHeads[layer] = 0;
                }

                // UniTask 会同步执行到第一个 await；成功占锁后 busy 已为 true。
                CloseUIAsyncCore(entry.Handle, entry.Force).Forget();
            }
        }

        private void ClearAllLayerCloseQueues()
        {
            for (int layer = 0; layer < _layerPendingCloseCounts.Length; layer++)
            {
                PendingLayerClose[] buffer = _layerPendingCloses[layer];
                int head = _layerPendingCloseHeads[layer];
                int count = _layerPendingCloseCounts[layer];
                if (count > 0)
                {
                    Array.Clear(buffer, head, count);
                }

                _layerPendingCloseCounts[layer] = 0;
                _layerPendingCloseHeads[layer] = 0;
            }
        }

        private void MarkLayerVisualDirty(int layer)
        {
            if ((uint)layer >= (uint)_layerVisualDirty.Length)
            {
                return;
            }

            _layerVisualDirty[layer] = true;

            // 非 busy 时立刻 best-effort 恢复；busy 时留给 EndLayerMutation / 下次入口。
            if (!_layerMutationBusy[layer])
            {
                TryRecoverLayerVisualState(layer);
            }
        }

        /// <summary>
        /// 尝试清除层 visual dirty：重算深度并清 StackRemovalPending。
        /// 层 busy 时不抢锁，返回 false。
        /// </summary>
        private bool TryRecoverLayerVisualState(int layer)
        {
            if ((uint)layer >= (uint)_layerVisualDirty.Length)
            {
                return false;
            }

            if (!_layerVisualDirty[layer])
            {
                return true;
            }

            if (_layerMutationBusy[layer])
            {
                return false;
            }

            try
            {
                LayerData layerData = _openUI[layer];
                if (layerData != null)
                {
                    ClearStackRemovalPendingOnLayer(layerData);
                    SortWindowDepth(layer, 0);
                }

                _layerVisualDirty[layer] = false;
                return true;
            }
            catch
            {
                _layerVisualDirty[layer] = true;
                return false;
            }
        }

        private bool TryEnsureLayerNotVisuallyDirty(int layer)
        {
            if ((uint)layer >= (uint)_layerVisualDirty.Length)
            {
                return false;
            }

            if (!_layerVisualDirty[layer])
            {
                return true;
            }

            return TryRecoverLayerVisualState(layer);
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

            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts))
            {
                metaInfo.SetPendingShowUserDatas(userDatas);
                UIBase joinedView = await metaInfo.WaitForShowOperationAsync();
                return CreateShowResultFromView(joinedView);
            }

            // Begin 后只拿一次 token：后续新操作会替换 CTS，再读属性会拿到错误令牌。
            if (!TryBeginLayerMutation(layerIndex))
            {
                metaInfo.CompleteShowOperation(null);
                metaInfo.EndShowOperation(operationVersion, loadCts);
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
                    if (metaInfo.IsOperationCurrent(operationVersion))
                    {
                        WarnUIOperation("Show invalid after resource creation", metaInfo, operationVersion);
                    }
#endif
                    if (CanRollbackShow(metaInfo, operationVersion))
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
                        WarnUIOperation("Show init failed", metaInfo, operationVersion);
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
                        return await RunPreparedShowVisualAsync(metaInfo, operationVersion, loadCts, layerIndex);
                    }
                }

                if (!showResult.IsAccepted && CanRollbackShow(metaInfo, operationVersion))
                {
                    await RollbackFailedShowAsync(metaInfo, operationVersion);
                }
            }
            catch (Exception exception)
            {
                exceptionThrown = true;
                if (CanRollbackShow(metaInfo, operationVersion))
                {
                    await RollbackFailedShowAsync(metaInfo, operationVersion);
                }

                metaInfo.FailShowOperation(exception);
                throw;
            }
            finally
            {
                if (!visualStarted)
                {
                    UIBase result = showResult.IsAccepted ? metaInfo.View : null;
                    if (!exceptionThrown)
                    {
                        metaInfo.CompleteShowOperation(result);
                    }

                    metaInfo.EndShowOperation(operationVersion, loadCts);
                    EndLayerMutation(layerIndex);
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

            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts))
            {
                metaInfo.SetPendingShowUserDatas(userDatas);
                return metaInfo.View;
            }

            if (!TryBeginLayerMutation(layerIndex))
            {
                metaInfo.CompleteShowOperation(null);
                metaInfo.EndShowOperation(operationVersion, loadCts);
                loadCts.Dispose();
                return null;
            }

            try
            {
                UIHolderFactory.CreateUIResourceSync(metaInfo, UICacheLayer);
                if (!IsShowValidAfterResourceCreation(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    if (metaInfo.IsOperationCurrent(operationVersion))
                    {
                        WarnUIOperation("ShowSync invalid after resource creation", metaInfo, operationVersion);
                    }
#endif
                    CompletePreparedShowFailureBeforeStackImmediate(metaInfo, operationVersion, loadCts, layerIndex);
                    return null;
                }

                FinalizeShow(metaInfo, userDatas);
                SortWindowDepth(metaInfo.MetaInfo.UILayer);
                if (metaInfo.State == UIState.Loaded && !metaInfo.View.InternalInitlizedSync(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("ShowSync init failed", metaInfo, operationVersion);
#endif
                    CompletePreparedShowFailureAsync(metaInfo, operationVersion, loadCts, layerIndex).Forget();
                    return null;
                }

                if (metaInfo.OperationVersion != operationVersion)
                {
                    CompletePreparedShowFailureAsync(metaInfo, operationVersion, loadCts, layerIndex).Forget();
                    return null;
                }

                UIBase view = metaInfo.View;
                RunPreparedShowVisualAsync(metaInfo, operationVersion, loadCts, layerIndex).Forget();
                return view;
            }
            catch
            {
                if (CanRollbackShow(metaInfo, operationVersion))
                {
                    CompletePreparedShowFailureAsync(metaInfo, operationVersion, loadCts, layerIndex).Forget();
                }
                else
                {
                    CompletePreparedShowFailureBeforeStackImmediate(metaInfo, operationVersion, loadCts, layerIndex);
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
                        WarnUIOperation("Close interrupted", meta, operationVersion);
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

            // 深度刷新是 best-effort：失败只标 dirty，绝不阻止实例回收。
            if (refreshVisual)
            {
                try
                {
                    SortWindowDepth(layerIndex, removedIndex);
                }
                catch
                {
                    MarkLayerVisualDirty(layerIndex);
                }
            }

            // Pop 成功后必须回收，避免出栈半成品悬空。
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
            // 资源完成后：事务仍当前、View 已绑定资源（非 CreatedUI/空/已毁）。
            return meta != null
                   && meta.IsOperationCurrent(operationVersion)
                   && meta.View != null
                   && meta.State != UIState.Uninitialized
                   && meta.State != UIState.CreatedUI
                   && meta.State != UIState.Destroyed;
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

        /// <summary>
        /// InternalOpen 返回 false 时：stale→Cancelled；仍在栈且可显示→Accepted；否则 Failed。
        /// 诊断日志由调用方在 Failed 时统一打一次。
        /// </summary>
        private UIShowResult IsShowAcceptedAfterOpenInterruption(UIMetadata meta, int operationVersion)
        {
            if (meta == null || !meta.IsOperationCurrent(operationVersion))
            {
                return UIShowResult.Cancelled;
            }

            if (meta.View == null || !IsMetaInOpenStack(meta))
            {
                return UIShowResult.Failed;
            }

            return UIStateMachine.IsDisplayActive(meta.State)
                ? new UIShowResult(meta.View, UIShowResultState.Opened)
                : UIShowResult.Failed;
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
            int layerIndex)
        {
            UIShowResult showResult = UIShowResult.Failed;
            bool exceptionThrown = false;
            try
            {
                bool openResult = await meta.View.InternalOpen();

                showResult = openResult
                    ? meta.IsOperationCurrent(operationVersion)
                        ? new UIShowResult(meta.View, UIShowResultState.Opened)
                        : UIShowResult.Cancelled
                    : IsShowAcceptedAfterOpenInterruption(meta, operationVersion);

                if (!showResult.IsAccepted && meta.IsOperationCurrent(operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("Show open rejected", meta, operationVersion);
#endif
                    await RollbackFailedShowAsync(meta, operationVersion);
                }
            }
            catch (Exception exception)
            {
                exceptionThrown = true;
                if (CanRollbackShow(meta, operationVersion))
                {
                    await RollbackFailedShowAsync(meta, operationVersion);
                }

                meta.FailShowOperation(exception);
                throw;
            }
            finally
            {
                UIBase result = showResult.IsAccepted ? meta.View : null;
                if (!exceptionThrown)
                {
                    meta.CompleteShowOperation(result);
                }

                meta.EndShowOperation(operationVersion, loadCts);
                EndLayerMutation(layerIndex);
                loadCts.Dispose();
            }

            return showResult;
        }

        private async UniTaskVoid CompletePreparedShowFailureAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            int layerIndex)
        {
            try
            {
                if (CanRollbackShow(meta, operationVersion))
                {
                    await RollbackFailedShowAsync(meta, operationVersion);
                }
            }
            finally
            {
                meta?.CompleteShowOperation(null);
                meta?.EndShowOperation(operationVersion, loadCts);
                EndLayerMutation(layerIndex);
                loadCts?.Dispose();
            }
        }

        private void CompletePreparedShowFailureBeforeStackImmediate(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            int layerIndex)
        {
            try
            {
                if (meta != null && meta.IsOperationCurrent(operationVersion) && !CanRollbackShow(meta, operationVersion))
                {
                    meta.DisposeImmediate();
                }
            }
            finally
            {
                meta?.CompleteShowOperation(null);
                meta?.EndShowOperation(operationVersion, loadCts);
                EndLayerMutation(layerIndex);
                loadCts?.Dispose();
            }
        }

        private void SortWindowDepth(int layer, int startIndex = 0)
        {
            if ((uint)layer >= (uint)_openUI.Length)
            {
                return;
            }

            LayerData layerData = _openUI[layer];
            if (layerData == null)
            {
                return;
            }

            if (startIndex < 0)
            {
                startIndex = 0;
            }

            int baseDepth = layer * LAYER_DEEP;
            for (int i = startIndex; i < layerData.Count; i++)
            {
                UIBase view = layerData.Items[i]?.View;
                if (view == null)
                {
                    continue;
                }

                int newDepth = baseDepth + i * WINDOW_DEEP;
                if (view.Depth != newDepth)
                {
                    view.Depth = newDepth;
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
            int layerIndex = meta.MetaInfo.UILayer;
            try
            {
                SortWindowDepth(layerIndex, removed >= 0 ? removed : 0);
            }
            catch
            {
                MarkLayerVisualDirty(layerIndex);
            }

            // 与 Finalize 一致：出栈后必须回收实例。
            await meta.DisposeAsync();
        }

        private bool CanRollbackShow(UIMetadata meta, int operationVersion)
        {
            return meta != null
                   && meta.IsOperationCurrent(operationVersion)
                   && IsMetaInOpenStack(meta);
        }

        public UniTask<bool> RebuildLayerVisualStateAsync(UILayer layer)
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
                if (layerData != null)
                {
                    ClearStackRemovalPendingOnLayer(layerData);
                    SortWindowDepth(layerIndex, 0);
                }

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
                // 直接清 busy，避免 EndLayerMutation 再嵌套 recover/drain 造成重入语义复杂化。
                _layerMutationBusy[layerIndex] = false;
                if (_layerVisualDirty[layerIndex])
                {
                    TryRecoverLayerVisualState(layerIndex);
                }

                DrainLayerCloseQueue(layerIndex);
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
        private static void WarnUIOperation(string title, UIMetadata meta, int expectedOperationVersion)
        {
            if (!UIWarningSettings.OtherWarningsEnabled)
            {
                return;
            }

            Log.Warning($"[UI] {title}. ExpectedVersion={expectedOperationVersion}. {FormatMetadata(meta)}");
        }

        private static string FormatMetadata(UIMetadata meta)
        {
            if (meta == null)
            {
                return "Meta=null.";
            }

            string viewState = meta.View == null ? "View=null" : $"ViewState={meta.View.State}, Visible={meta.View.Visible}, Depth={meta.View.Depth}";
            return $"UI={meta.UILogicTypeName}, State={meta.State}, {viewState}, ActualVersion={meta.OperationVersion}, ShowInProgress={meta.ShowInProgress}, CloseInProgress={meta.CloseInProgress}, InCache={meta.InCache}, Layer={(UILayer)meta.MetaInfo.UILayer}, TypeId={meta.MetaInfo.TypeId}.";
        }
#endif
    }
}
