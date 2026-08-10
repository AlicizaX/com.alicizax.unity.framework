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

        private static void RefreshOpenedShowUserData(UIMetadata meta, object[] userDatas)
        {
            meta?.RefreshLiveShowUserDatas(userDatas);
        }

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
            if (meta.State == UIState.Opened)
            {
                RefreshOpenedShowUserData(meta, userDatas);
                return new UIShowResult(meta.View, UIShowResultState.Opened);
            }

            ApplyStickyShowUserData(meta, userDatas);
            UIBase joinedView = await meta.WaitForShowOperationAsync();
            if (joinedView != null && joinedView.State == UIState.Opened)
            {
                return new UIShowResult(joinedView, UIShowResultState.Opened);
            }

            return UIShowResult.Cancelled;
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

            return ResolveShowEntryAsync(meta, userDatas, allowShowAfterClose: true);
        }

        private static bool IsCloseBlockingShow(UIMetadata meta)
        {
            return meta.CloseInProgress || meta.State == UIState.Closing;
        }

        private UniTask<UIShowResult> ResolveShowEntryAsync(UIMetadata meta, object[] userDatas, bool allowShowAfterClose)
        {
            if (ShouldRefreshExistingShow(meta))
            {
                return RefreshExistingShowAsync(meta, userDatas);
            }

            if (IsCloseBlockingShow(meta))
            {
                return allowShowAfterClose
                    ? ShowAfterCloseAsync(meta, userDatas)
                    : UniTask.FromResult(UIShowResult.Cancelled);
            }

            return ShowUIImplAsync(meta, userDatas);
        }

        private async UniTask<UIShowResult> ShowAfterCloseAsync(UIMetadata meta, object[] userDatas)
        {
            ApplyStickyShowUserData(meta, userDatas);
            await meta.WaitForCloseOperationAsync();

            object[] latest = meta.GetPendingShowUserDatas(userDatas);
            return await ResolveShowEntryAsync(meta, latest, allowShowAfterClose: false);
        }

        private UniTask<bool> EnqueueCloseCommandAsync(UIMetadata meta, bool force, bool skipTransition = false)
        {
            if (meta.ShowInProgress)
            {
                meta.RequestCancelShowLoad();
            }

            UIState state = meta.State;
            if (state == UIState.Uninitialized
                || state == UIState.Destroying
                || state == UIState.Destroyed)
            {
                return UniTask.FromResult(false);
            }

            return CloseUIImplCore(meta, force, skipTransition);
        }

        private bool IsMetaInOpenStack(UIMetadata meta)
        {
            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            return layer != null && GetOpenIndex(layer, meta) >= 0;
        }

        private async UniTask<UIShowResult> ShowUIImplAsync(UIMetadata metaInfo, object[] userDatas)
        {
            if (ShouldRefreshExistingShow(metaInfo) || IsCloseBlockingShow(metaInfo))
            {
                return await ResolveShowEntryAsync(metaInfo, userDatas, allowShowAfterClose: true);
            }

            CreateMetaUI(metaInfo);
            if (!metaInfo.BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts))
            {
                return await ResolveShowEntryAsync(metaInfo, userDatas, allowShowAfterClose: true);
            }

            CancellationToken cancellationToken = loadCts.Token;
            try
            {
                ReserveOpenSlot(metaInfo);
                await UIHolderFactory.CreateUIResourceAsync(metaInfo, UICacheLayer, cancellationToken);

                if (!IsShowValidAfterResourceCreation(metaInfo, operationVersion))
                {
                    bool cancelled = IsShowCancelled(metaInfo, operationVersion, cancellationToken);
#if UNITY_EDITOR
                    if (!cancelled)
                    {
                        WarnUIOperation("Show invalid after resource creation", metaInfo, operationVersion);
                    }
#endif
                    return await FailShowAsync(metaInfo, operationVersion, loadCts, cancelled);
                }

                FinalizeShow(metaInfo, metaInfo.GetPendingShowUserDatas(userDatas));
                SortWindowDepth(metaInfo.MetaInfo.UILayer);

                if (metaInfo.State == UIState.Loaded && !await metaInfo.View.InternalInitlized(metaInfo, operationVersion))
                {
                    bool cancelled = IsShowCancelled(metaInfo, operationVersion, cancellationToken);
#if UNITY_EDITOR
                    if (!cancelled)
                    {
                        WarnUIOperation("Show init failed", metaInfo, operationVersion);
                    }
#endif
                    return await FailShowAsync(metaInfo, operationVersion, loadCts, cancelled);
                }

                if (metaInfo.OperationVersion != operationVersion || cancellationToken.IsCancellationRequested)
                {
                    return await FailShowAsync(metaInfo, operationVersion, loadCts, cancelled: true);
                }

                return await RunPreparedShowVisualAsync(metaInfo, operationVersion, loadCts);
            }
            catch (OperationCanceledException)
            {
                return await FailShowAsync(metaInfo, operationVersion, loadCts, cancelled: true);
            }
            catch (Exception exception)
            {
                await FailShowAsync(metaInfo, operationVersion, loadCts, cancelled: false, exception);
                throw;
            }
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
                    FailShowSync(metaInfo, operationVersion, loadCts);
                    return null;
                }

                FinalizeShow(metaInfo, metaInfo.GetPendingShowUserDatas(userDatas));
                SortWindowDepth(metaInfo.MetaInfo.UILayer);
                if (metaInfo.State == UIState.Loaded && !metaInfo.View.InternalInitlizedSync(metaInfo, operationVersion))
                {
#if UNITY_EDITOR
                    WarnUIOperation("ShowSync init failed", metaInfo, operationVersion);
#endif
                    FailShowSync(metaInfo, operationVersion, loadCts);
                    return null;
                }

                if (metaInfo.OperationVersion != operationVersion)
                {
                    FailShowSync(metaInfo, operationVersion, loadCts);
                    return null;
                }

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

                FailShowSync(metaInfo, operationVersion, loadCts);
                return null;
            }
            catch
            {
                FailShowSync(metaInfo, operationVersion, loadCts);
                throw;
            }
        }

        private async UniTask<bool> CloseUIImplCore(UIMetadata meta, bool force, bool skipTransition = false)
        {
            UIState state = meta.State;
            if (state == UIState.Uninitialized || state == UIState.Destroying || state == UIState.Destroyed)
            {
                return false;
            }

            if (meta.CloseInProgress || state == UIState.Closing)
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
                return meta.CloseInProgress || meta.State == UIState.Closing
                    ? await meta.WaitForCloseOperationAsync()
                    : false;
            }

            bool closeCompleted = false;
            try
            {
                state = meta.State;
                UIFinalizeClosedMode finalizeMode = GetFinalizeModeForExplicitClose(state, interruptedShow);
                if (state == UIState.CreatedUI
                    || state == UIState.Loaded
                    || state == UIState.Initialized
                    || state == UIState.Closed)
                {
                    if (meta.View != null && state != UIState.CreatedUI)
                    {
                        meta.View.Visible = false;
                    }

                    closeCompleted = await FinalizeClosedWindowAsync(meta, force, finalizeMode);
                }
                else if (meta.View != null)
                {
                    await meta.View.InternalClose(meta, operationVersion, skipTransition);
                    if (meta.OperationVersion == operationVersion && meta.State == UIState.Closed)
                    {
                        closeCompleted = await FinalizeClosedWindowAsync(meta, force, finalizeMode);
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

        private async UniTask<bool> FinalizeClosedWindowAsync(
            UIMetadata meta,
            bool force,
            UIFinalizeClosedMode mode)
        {
            int layerIndex = meta.MetaInfo.UILayer;
            int removedIndex = Pop(meta);
            if (removedIndex < 0)
            {
                return false;
            }

            SortWindowDepth(layerIndex, removedIndex);

            if (mode == UIFinalizeClosedMode.Dispose)
            {
                await meta.DisposeAsync();
            }
            else
            {
                CacheWindow(meta, force);
            }

            return true;
        }

        private static UIFinalizeClosedMode GetFinalizeModeForExplicitClose(UIState state, bool interruptedShow)
        {
            return state == UIState.CreatedUI || interruptedShow
                ? UIFinalizeClosedMode.Dispose
                : UIFinalizeClosedMode.Cache;
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
                meta.View.ExitCacheVisual();
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
            return meta.IsOperationCurrent(operationVersion)
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

        private async UniTask<UIShowResult> RunPreparedShowVisualAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts)
        {
            try
            {
                bool openResult = meta.View != null && meta.View.InternalOpen(meta, operationVersion);
                UIShowResult showResult = openResult
                    ? meta.IsOperationCurrent(operationVersion)
                        ? new UIShowResult(meta.View, UIShowResultState.Opened)
                        : UIShowResult.Cancelled
                    : IsShowAcceptedAfterOpenInterruption(meta, operationVersion);

                if (showResult.IsAccepted)
                {
                    meta.CompleteShowOperation(meta.View);
                    meta.EndShowOperation(operationVersion, loadCts);
                    loadCts.Dispose();
                    return showResult;
                }

                if (showResult.State == UIShowResultState.Failed)
                {
#if UNITY_EDITOR
                    WarnUIOperation("Show open rejected", meta, operationVersion);
#endif
                }

                return await FailShowAsync(
                    meta,
                    operationVersion,
                    loadCts,
                    cancelled: showResult.State == UIShowResultState.Cancelled);
            }
            catch (Exception exception)
            {
                await FailShowAsync(meta, operationVersion, loadCts, cancelled: false, exception);
                throw;
            }
        }

        private async UniTask<UIShowResult> FailShowAsync(
            UIMetadata meta,
            int operationVersion,
            CancellationTokenSource loadCts,
            bool cancelled,
            Exception exception = null)
        {
            try
            {
                if (CanRollbackShow(meta, operationVersion))
                {
                    int removed = Pop(meta);
                    SortWindowDepth(meta.MetaInfo.UILayer, removed >= 0 ? removed : 0);
                    await meta.DisposeAsync();
                }
            }
            finally
            {
                if (exception != null)
                {
                    meta.FailShowOperation(exception);
                }
                else
                {
                    meta.CompleteShowOperation(null);
                }

                meta.EndShowOperation(operationVersion, loadCts);
                loadCts.Dispose();
            }

            return cancelled ? UIShowResult.Cancelled : UIShowResult.Failed;
        }

        private void FailShowSync(UIMetadata meta, int operationVersion, CancellationTokenSource loadCts)
        {
            try
            {
                if (!meta.IsOperationCurrent(operationVersion))
                {
                    return;
                }

                if (IsMetaInOpenStack(meta))
                {
                    int removed = Pop(meta);
                    SortWindowDepth(meta.MetaInfo.UILayer, removed >= 0 ? removed : 0);
                }

                meta.DisposeImmediate();
            }
            finally
            {
                meta.CompleteShowOperation(null);
                meta.EndShowOperation(operationVersion, loadCts);
                loadCts.Dispose();
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

        private bool CanRollbackShow(UIMetadata meta, int operationVersion)
        {
            return meta.IsOperationCurrent(operationVersion) && IsMetaInOpenStack(meta);
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
            if (!UIWarningSettings.Enabled)
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
