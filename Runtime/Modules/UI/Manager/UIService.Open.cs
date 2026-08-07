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
        private readonly LayerData[] _openUI = new LayerData[(int)UILayer.All];

        // Opened：只刷 View，不 sticky
        private static void RefreshOpenedShowUserData(UIMetadata meta, object[] userDatas)
        {
            meta?.RefreshLiveShowUserDatas(userDatas);
        }

        // 加载中 latest 或 Closing 再开意图（唯一 sticky 入口）
        private static void ApplyStickyShowUserData(UIMetadata meta, object[] userDatas)
        {
            meta?.SetPendingShowUserDatas(userDatas);
        }

        private bool TryRefreshExistingShowSync(UIMetadata meta, object[] userDatas, out UIBase view)
        {
            view = null;
            if (meta == null)
            {
                return false;
            }

            UIState state = meta.State;
            if (state == UIState.Opened)
            {
                RefreshOpenedShowUserData(meta, userDatas);
                view = meta.View;
                return true;
            }

            if (meta.ShowInProgress || state == UIState.Opening)
            {
                ApplyStickyShowUserData(meta, userDatas);
                view = meta.View;
                return true;
            }

            return false;
        }

        private async UniTask<UIShowResult> RefreshExistingShowAsync(UIMetadata meta, object[] userDatas)
        {
            UIState state = meta.State;
            if (state == UIState.Opened)
            {
                RefreshOpenedShowUserData(meta, userDatas);
                return new UIShowResult(meta.View, UIShowResultState.Opened);
            }

            // 加载中 join：更新 latest，等待本次打开结果（被 Close 打断则 Cancelled，需再 Show）
            ApplyStickyShowUserData(meta, userDatas);
            UIBase joinedView = await meta.WaitForShowOperationAsync();
            if (joinedView != null && joinedView.State == UIState.Opened)
            {
                return new UIShowResult(joinedView, UIShowResultState.Opened);
            }

            if (meta.State == UIState.Closing
                || meta.State == UIState.Closed
                || meta.State == UIState.Destroying
                || meta.State == UIState.Destroyed
                || meta.State == UIState.Uninitialized
                || joinedView == null)
            {
                return UIShowResult.Cancelled;
            }

            return CreateShowResultFromView(joinedView);
        }

        private bool ShouldRefreshExistingShow(UIMetadata meta)
        {
            if (meta == null)
            {
                return false;
            }

            return meta.ShowInProgress
                   || meta.State == UIState.Opened
                   || meta.State == UIState.Opening;
        }

        private UIBase ShowUISyncCore(UIMetadata meta, object[] userDatas)
        {
            if (TryRefreshExistingShowSync(meta, userDatas, out UIBase refreshed))
            {
                return refreshed;
            }

            // Sync 无法 await 关闭；不写 sticky（关后再开请用异步 Show）
            if (IsCloseBlockingShow(meta))
            {
                Log.Warning("[UI] ShowUISync rejected while closing: {0}", meta.UILogicTypeName);
                return null;
            }

            return ShowUIImplSync(meta, userDatas);
        }

        private UniTask<UIShowResult> EnqueueShowCommandAsync(UIMetadata meta, object[] userDatas)
        {
            if (meta == null)
            {
                return UniTask.FromResult(UIShowResult.Failed);
            }

            // 同类型打开中/已打开：只刷 latest userData，不重复打开
            if (ShouldRefreshExistingShow(meta))
            {
                return RefreshExistingShowAsync(meta, userDatas);
            }

            // Closing 中：等逻辑关闭完成后再用 latest 打开
            if (IsCloseBlockingShow(meta))
            {
                return ShowAfterCloseAsync(meta, userDatas);
            }

            // per-meta 互斥；同层不同类型可并行，不再走层串行队列
            return ShowUIImplAsync(meta, userDatas);
        }

        private static bool IsCloseBlockingShow(UIMetadata meta)
        {
            return meta != null
                   && (meta.CloseInProgress
                       || meta.State == UIState.Closing);
        }

        private async UniTask<UIShowResult> ShowAfterCloseAsync(UIMetadata meta, object[] userDatas)
        {
            ApplyStickyShowUserData(meta, userDatas);
            await meta.WaitForCloseOperationAsync();

            // 多路 Closing Show 的 latest 已合并进 pending；CompleteShow 时再清
            object[] latest = meta.GetPendingShowUserDatas(userDatas);

            if (IsCloseBlockingShow(meta))
            {
                return UIShowResult.Cancelled;
            }

            if (ShouldRefreshExistingShow(meta))
            {
                return await RefreshExistingShowAsync(meta, latest);
            }

            return await ShowUIImplAsync(meta, latest);
        }

        private UniTask<bool> EnqueueCloseCommandAsync(UIMetadata meta, bool force)
        {
            if (meta == null)
            {
                return UniTask.FromResult(false);
            }

            if (meta.ShowInProgress)
            {
                meta.RequestCancelShowLoad();
            }

            if (meta.State == UIState.Uninitialized
                || meta.State == UIState.Destroying
                || meta.State == UIState.Destroyed)
            {
                return UniTask.FromResult(false);
            }

            return CloseUIImplCore(meta, force);
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
            if (ShouldRefreshExistingShow(metaInfo))
            {
                return await RefreshExistingShowAsync(metaInfo, userDatas);
            }

            if (IsCloseBlockingShow(metaInfo))
            {
                return await ShowAfterCloseAsync(metaInfo, userDatas);
            }

            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts))
            {
                // Begin 失败：可能是 Show 并发，或 Close 抢占
                if (IsCloseBlockingShow(metaInfo))
                {
                    return await ShowAfterCloseAsync(metaInfo, userDatas);
                }

                return await RefreshExistingShowAsync(metaInfo, userDatas);
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
                    bool cancelled = IsShowCancelled(metaInfo, operationVersion, cancellationToken);
                    if (!cancelled)
                    {
#if UNITY_EDITOR
                        WarnUIOperation("Show invalid after resource creation", metaInfo, operationVersion);
#endif
                    }

                    if (CanRollbackShow(metaInfo, operationVersion))
                    {
                        await RollbackFailedShowAsync(metaInfo, operationVersion);
                    }

                    showResult = cancelled ? UIShowResult.Cancelled : UIShowResult.Failed;
                }
                else
                {
                    FinalizeShow(metaInfo, metaInfo.GetPendingShowUserDatas(userDatas));
                    SortWindowDepth(metaInfo.MetaInfo.UILayer);
                    if (metaInfo.State == UIState.Loaded && !await metaInfo.View.InternalInitlized(metaInfo, operationVersion))
                    {
                        bool cancelled = IsShowCancelled(metaInfo, operationVersion, cancellationToken);
                        if (!cancelled)
                        {
#if UNITY_EDITOR
                            WarnUIOperation("Show init failed", metaInfo, operationVersion);
#endif
                        }

                        showResult = cancelled ? UIShowResult.Cancelled : UIShowResult.Failed;
                    }
                    else if (metaInfo.OperationVersion != operationVersion || cancellationToken.IsCancellationRequested)
                    {
                        showResult = UIShowResult.Cancelled;
                    }
                    else
                    {
                        visualStarted = true;
                        return await RunPreparedShowVisualAsync(metaInfo, operationVersion, loadCts);
                    }
                }

                if (!showResult.IsAccepted && CanRollbackShow(metaInfo, operationVersion))
                {
                    await RollbackFailedShowAsync(metaInfo, operationVersion);
                }
            }
            catch (OperationCanceledException)
            {
                if (CanRollbackShow(metaInfo, operationVersion))
                {
                    await RollbackFailedShowAsync(metaInfo, operationVersion);
                }

                showResult = UIShowResult.Cancelled;
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
                    loadCts.Dispose();
                }
            }

            return showResult;
        }

        private UIBase ShowUIImplSync(UIMetadata metaInfo, object[] userDatas)
        {
            if (TryRefreshExistingShowSync(metaInfo, userDatas, out UIBase refreshed))
            {
                return refreshed;
            }

            if (IsCloseBlockingShow(metaInfo))
            {
                Log.Warning("[UI] ShowUISync rejected while closing: {0}", metaInfo.UILogicTypeName);
                return null;
            }

            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts))
            {
                // Close 抢占：无法同步等待；Show 并发：写入 in-flight latest
                if (IsCloseBlockingShow(metaInfo))
                {
                    Log.Warning("[UI] ShowUISync rejected while closing: {0}", metaInfo.UILogicTypeName);
                    return null;
                }

                ApplyStickyShowUserData(metaInfo, userDatas);
                return metaInfo.View;
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
                    CompletePreparedShowFailureBeforeStackImmediate(metaInfo, operationVersion, loadCts);
                    return null;
                }

                FinalizeShow(metaInfo, metaInfo.GetPendingShowUserDatas(userDatas));
                SortWindowDepth(metaInfo.MetaInfo.UILayer);
                if (metaInfo.State == UIState.Loaded && !metaInfo.View.InternalInitlizedSync(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("ShowSync init failed", metaInfo, operationVersion);
#endif
                    FailPreparedShowSync(metaInfo, operationVersion, loadCts);
                    return null;
                }

                if (metaInfo.OperationVersion != operationVersion)
                {
                    FailPreparedShowSync(metaInfo, operationVersion, loadCts);
                    return null;
                }

                // Sync：逻辑 Open（含 OnOpen）必须在返回前完成；转场后台并行
                bool openResult = metaInfo.View != null
                                 && metaInfo.View.InternalOpen(metaInfo, operationVersion);
                if (openResult && metaInfo.IsOperationCurrent(operationVersion))
                {
                    UIBase view = metaInfo.View;
                    metaInfo.CompleteShowOperation(view);
                    metaInfo.EndShowOperation(operationVersion, loadCts);
                    loadCts.Dispose();
                    return view;
                }

                FailPreparedShowSync(metaInfo, operationVersion, loadCts);
                return null;
            }
            catch
            {
                if (CanRollbackShow(metaInfo, operationVersion))
                {
                    FailPreparedShowSync(metaInfo, operationVersion, loadCts);
                }
                else
                {
                    CompletePreparedShowFailureBeforeStackImmediate(metaInfo, operationVersion, loadCts);
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

            // 二次 Close：join 同一关闭流程
            if (meta.CloseInProgress || meta.State == UIState.Closing)
            {
                return await meta.WaitForCloseOperationAsync();
            }

            bool interruptedShow = meta.ShowInProgress;
            if (interruptedShow)
            {
                meta.RequestCancelShowLoad();
            }

            if (!meta.BeginCloseOperation(out int operationVersion))
            {
                // 并发 Begin 失败：join 已在进行中的 Close
                if (meta.CloseInProgress || meta.State == UIState.Closing)
                {
                    return await meta.WaitForCloseOperationAsync();
                }

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
                else if (meta.View != null)
                {
                    // InternalClose await 关场；失败也会尽量落到 Closed。Finalize/Cache 在 Closed 之后
                    await meta.View.InternalClose(meta, operationVersion);
                    if (meta.OperationVersion == operationVersion && meta.State == UIState.Closed)
                    {
                        UIFinalizeClosedResult finalizeResult = await FinalizeClosedWindowAsync(meta, force, finalizeMode, refreshVisual: true);
                        closeCompleted = finalizeResult.Success;
                    }
#if UNITY_EDITOR
                    else if (meta.OperationVersion == operationVersion)
                    {
                        WarnUIOperation("Close interrupted", meta, operationVersion);
                    }
#endif
                }
            }
            finally
            {
                meta.EndCloseOperation(operationVersion);
                meta.CompleteCloseOperation(closeCompleted);
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
                SortWindowDepth(layerIndex, removedIndex);
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

        // 仅 Opened；过渡态不算打开
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
                    if (!predicate(handle))
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

                    if (predicate != null && !predicate(candidate))
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
                // 缓存路径关闭 Canvas；出缓存时恢复渲染
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
                   && meta.IsOperationCurrent(operationVersion)
                   && meta.View != null
                   && meta.State != UIState.Uninitialized
                   && meta.State != UIState.CreatedUI
                   && meta.State != UIState.Destroyed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsShowCancelled(UIMetadata meta, int operationVersion, CancellationToken cancellationToken)
        {
            return meta == null
                   || !meta.IsOperationCurrent(operationVersion)
                   || cancellationToken.IsCancellationRequested;
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
            CancellationTokenSource loadCts)
        {
            UIShowResult showResult = UIShowResult.Failed;
            bool exceptionThrown = false;
            try
            {
                // InternalOpen 仅完成逻辑 Open；转场在 View 后台并行
                bool openResult = meta.View != null && meta.View.InternalOpen(meta, operationVersion);

                showResult = openResult
                    ? meta.IsOperationCurrent(operationVersion)
                        ? new UIShowResult(meta.View, UIShowResultState.Opened)
                        : UIShowResult.Cancelled
                    : IsShowAcceptedAfterOpenInterruption(meta, operationVersion);

                if (!showResult.IsAccepted && meta.IsOperationCurrent(operationVersion))
                {
                    if (showResult.State == UIShowResultState.Failed)
                    {
#if UNITY_EDITOR
                        WarnUIOperation("Show open rejected", meta, operationVersion);
#endif
                    }

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
                loadCts.Dispose();
            }

            return showResult;
        }

        // Sync 失败：同步 Pop + DisposeImmediate，返回前结束 ShowInProgress，避免半残 View 被复用
        private void FailPreparedShowSync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts)
        {
            if (CanRollbackShow(meta, operationVersion))
            {
                int removed = Pop(meta);
                SortWindowDepth(meta.MetaInfo.UILayer, removed >= 0 ? removed : 0);
                // DisposeImmediate 内 CancelAsyncOperations：清 ShowInProgress / CompleteShow / 销毁 View
                meta.DisposeImmediate();
                loadCts?.Dispose();
                return;
            }

            CompletePreparedShowFailureBeforeStackImmediate(meta, operationVersion, loadCts);
        }

        private void CompletePreparedShowFailureBeforeStackImmediate(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts)
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
            SortWindowDepth(layerIndex, removed >= 0 ? removed : 0);
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
            if ((uint)layerIndex >= (uint)_openUI.Length)
            {
                return UniTask.FromResult(false);
            }

            LayerData layerData = _openUI[layerIndex];
            if (layerData != null)
            {
                ClearStackRemovalPendingOnLayer(layerData);
                SortWindowDepth(layerIndex, 0);
            }

            return UniTask.FromResult(true);
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
