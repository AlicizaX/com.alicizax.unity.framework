using System;
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
        }

        public void EnsureTypeCapacity(int typeId) => UITypeIndexArray.EnsureCapacity(ref TypeIdToIndex, typeId);

        public void EnsureItemCapacity()
        {
            if (Count == Items.Length)
                Array.Resize(ref Items, Items.Length << 1);
        }
    }

    internal sealed partial class UIService
    {
        private readonly LayerData[] _openUI = new LayerData[(int)UILayer.All];
        private int _showOrder;

        private UniTask<UIShowResult> EnqueueShowCommandAsync(UIMetadata meta, object[] userDatas)
        {
            if (meta == null)
                return UniTask.FromResult(UIShowResult.Failed);

            BringToFront(meta);
            if (meta.State == UIState.Opened && !meta.IsProcessing)
            {
                meta.RefreshLiveShowUserDatas(userDatas);
                return UniTask.FromResult(new UIShowResult(meta.View, UIShowResultState.Opened));
            }

            UniTask<UIShowResult> task = meta.EnqueueShow(userDatas);
            StartRequestProcessor(meta);
            return task;
        }

        private UniTask<bool> EnqueueCloseCommandAsync(UIMetadata meta, bool force, bool skipTransition)
        {
            if (meta == null)
                return UniTask.FromResult(false);

            UniTask<bool> task = meta.EnqueueClose(force, skipTransition);
            StartRequestProcessor(meta);
            return task;
        }

        private void StartRequestProcessor(UIMetadata meta)
        {
            if (meta.TryStartProcessor())
                ProcessRequestsAsync(meta).Forget();
        }

        private async UniTaskVoid ProcessRequestsAsync(UIMetadata meta)
        {
            UIRequest request = null;
            try
            {
                while (true)
                {
                    if (meta.TryDequeueClose(out request))
                    {
                        bool closed = await ExecuteCloseAsync(meta, request.Force, request.SkipTransition);
                        meta.CompleteRequest(request, closed);
                        request = null;
                        continue;
                    }

                    if (meta.TryDequeueShow(out request))
                    {
                        UIShowResult result = await ExecuteShowAsync(meta);
                        meta.CompleteRequest(request, result);
                        request = null;
                        continue;
                    }

                    break;
                }
            }
            catch (OperationCanceledException)
            {
                if (request != null)
                {
                    if (request.Kind == UIRequestKind.Show)
                        meta.CompleteRequest(request, await AbortShowAsync(meta, cancelled: true));
                    else
                        meta.CompleteRequest(request, false);
                    request = null;
                }
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Request processor failed for {0}.", meta.UILogicTypeName);
                Log.Exception(exception);
                if (request != null)
                {
                    if (request.Kind == UIRequestKind.Show)
                        await AbortShowAsync(meta);
                    else
                    {
                        RemoveFromOpenStack(meta);
                        await meta.DisposeAsync();
                    }

                    if (request.Kind == UIRequestKind.Show)
                        meta.CompleteRequest(request, UIShowResult.Failed);
                    else
                        meta.CompleteRequest(request, false);
                    request = null;
                }
            }
            finally
            {
                meta.StopProcessor();
                if (meta.HasPendingWork)
                    StartRequestProcessor(meta);
            }
        }

        private async UniTask<UIShowResult> ExecuteShowAsync(UIMetadata meta)
        {
            try
            {
                if (meta.IsShowLoadCancelled)
                    return await AbortShowAsync(meta, cancelled: true);

                if (meta.State == UIState.Cached)
                    RestoreFromCache(meta);
                else if (meta.State == UIState.Uninitialized)
                {
                    meta.CreateUI();
                    if (meta.View == null)
                        return await AbortShowAsync(meta);
                }

                if (meta.State == UIState.CreatedUI)
                {
                    PlaceByShowOrder(meta);
                    CancellationTokenSource loadCts = meta.BeginResourceLoad();
                    try
                    {
                        await UIHolderFactory.CreateUIResourceAsync(meta, GetLayerRect(meta.MetaInfo.UILayer), loadCts.Token);
                    }
                    finally
                    {
                        meta.EndResourceLoad(loadCts);
                    }

                    if (meta.IsShowLoadCancelled || loadCts.IsCancellationRequested)
                        return await AbortShowAsync(meta, cancelled: true);

                    ApplyWindowDepth(meta);
                }

                if (meta.View == null)
                    return await AbortShowAsync(meta);

                if (meta.State == UIState.Loaded && !await meta.View.InternalInitlized())
                    return await AbortShowAsync(meta);

                if (meta.IsShowLoadCancelled)
                    return await AbortShowAsync(meta, cancelled: true);

                meta.View.RefreshParams(meta.LatestShowUserDatas);
                if (!meta.View.InternalOpen())
                    return await AbortShowAsync(meta);

                UpdateLayerParent(meta);
                ApplyWindowDepth(meta);
                return new UIShowResult(meta.View, UIShowResultState.Opened);
            }
            catch (OperationCanceledException)
            {
                return await AbortShowAsync(meta, cancelled: true);
            }
        }

        private static bool IsLiveShow(UIMetadata meta)
        {
            UIState state = meta.State;
            return state == UIState.Opened || state == UIState.Opening;
        }

        private async UniTask<UIShowResult> AbortShowAsync(UIMetadata meta, bool cancelled = false)
        {
            if (cancelled && (IsLiveShow(meta) || meta.ShouldRetainView))
                return UIShowResult.Cancelled;

            RemoveFromOpenStack(meta);
            RemoveFromCache(meta.MetaInfo.TypeId);
            await meta.DisposeAsync();
            return cancelled ? UIShowResult.Cancelled : UIShowResult.Failed;
        }

        private async UniTask<bool> ExecuteCloseAsync(UIMetadata meta, bool force, bool skipTransition)
        {
            if (meta.View == null)
            {
                RemoveFromOpenStack(meta);
                RemoveFromCache(meta.MetaInfo.TypeId);
                return true;
            }

            if (meta.State == UIState.Cached)
            {
                RemoveFromOpenStack(meta);
                RemoveFromCache(meta.MetaInfo.TypeId);
                meta.DisposeImmediate();
                return true;
            }

            UIState state = meta.State;
            if (state == UIState.CreatedUI || state == UIState.Loaded || state == UIState.Initialized)
            {
                RemoveFromOpenStack(meta);
                await meta.DisposeAsync();
                return true;
            }

            if (state == UIState.Opening || state == UIState.Opened)
            {
                if (!await meta.View.InternalClose(skipTransition) && meta.State != UIState.Closed)
                    await meta.View.InternalClose(skipTransition: true);
            }

            if (meta.State != UIState.Closed)
            {
                RemoveFromOpenStack(meta);
                await meta.DisposeAsync();
                return false;
            }

            RemoveFromOpenStack(meta);
            CacheWindow(meta, force);
            return true;
        }

        private UIBase ShowUISyncCore(UIMetadata meta, object[] userDatas)
        {
            if (meta == null)
                return null;

            if (meta.State == UIState.Opened)
            {
                meta.DropPendingClose();
                BringToFront(meta);
                meta.RefreshLiveShowUserDatas(userDatas);
                return meta.View;
            }

            UIState state = meta.State;
            if (meta.IsClosing || state == UIState.Closing || state == UIState.Destroying)
            {
                EnqueueShowCommandAsync(meta, userDatas);
                return null;
            }

            meta.RetainView();
            meta.DropPendingClose();
            meta.CancelActiveShowLoad();
            BringToFront(meta);
            try
            {
                if (meta.State == UIState.Cached)
                    RestoreFromCache(meta);
                else if (meta.State == UIState.Uninitialized)
                    meta.CreateUI();

                if (meta.View == null)
                {
                    AbortShowSync(meta);
                    return null;
                }

                if (meta.State == UIState.CreatedUI)
                {
                    PlaceByShowOrder(meta);
                    UIHolderFactory.CreateUIResourceSync(meta, GetLayerRect(meta.MetaInfo.UILayer));
                    ApplyWindowDepth(meta);
                }

                if (meta.State == UIState.Loaded && !meta.View.InternalInitlizedSync())
                {
                    AbortShowSync(meta);
                    return null;
                }

                meta.View.RefreshParams(userDatas);
                if (!meta.View.InternalOpen())
                {
                    AbortShowSync(meta);
                    return null;
                }

                UpdateLayerParent(meta);
                ApplyWindowDepth(meta);
                return meta.View;
            }
            catch
            {
                AbortShowSync(meta);
                throw;
            }
            finally
            {
                meta.ReleaseRetainView();
            }
        }

        private void AbortShowSync(UIMetadata meta)
        {
            RemoveFromOpenStack(meta);
            RemoveFromCache(meta.MetaInfo.TypeId);
            meta.DisposeImmediate();
        }

        private int RestoreFromCache(UIMetadata meta)
        {
            RemoveFromCache(meta.MetaInfo.TypeId);
            meta.View.ExitCacheVisual();
            return PlaceByShowOrder(meta);
        }

        private void BringToFront(UIMetadata meta)
        {
            if (meta == null)
                return;

            UIState state = meta.State;
            if (state == UIState.Destroying || state == UIState.Destroyed)
                return;

            StampShowOrder(meta);
            if (IsInCache(meta))
                RemoveFromCache(meta.MetaInfo.TypeId);

            int startIndex = PlaceByShowOrder(meta);
            SortWindowDepth(meta.MetaInfo.UILayer, startIndex);
        }

        private void StampShowOrder(UIMetadata meta)
        {
            meta.LastShowOrder = ++_showOrder;
        }

        private bool IsInCache(UIMetadata meta)
        {
            int typeId = meta.MetaInfo.TypeId;
            return (uint)typeId < (uint)m_CacheTypeIdToIndex.Length && m_CacheTypeIdToIndex[typeId] >= 0;
        }

        private int PlaceByShowOrder(UIMetadata meta)
        {
            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            layer.EnsureTypeCapacity(typeId);

            int current = -1;
            if ((uint)typeId < (uint)layer.TypeIdToIndex.Length)
                current = layer.TypeIdToIndex[typeId];

            if (current >= 0)
            {
                int expected = FindShowOrderInsertIndexExcluding(layer, meta.LastShowOrder, current);
                if (expected == current)
                    return current;

                int last = layer.Count - 1;
                for (int i = current; i < last; i++)
                {
                    layer.Items[i] = layer.Items[i + 1];
                    layer.TypeIdToIndex[layer.Items[i].MetaInfo.TypeId] = i;
                }

                layer.Items[last] = null;
                layer.Count = last;
                layer.TypeIdToIndex[typeId] = -1;
            }

            layer.EnsureItemCapacity();
            int insert = FindShowOrderInsertIndex(layer, meta.LastShowOrder);
            for (int i = layer.Count; i > insert; i--)
            {
                layer.Items[i] = layer.Items[i - 1];
                layer.TypeIdToIndex[layer.Items[i].MetaInfo.TypeId] = i;
            }

            layer.Items[insert] = meta;
            layer.TypeIdToIndex[typeId] = insert;
            layer.Count++;
            if (current < 0)
                AddUpdateableWindow(meta);

            return current < 0 ? insert : (current < insert ? current : insert);
        }

        private static int FindShowOrderInsertIndex(LayerData layer, int order)
        {
            return FindShowOrderInsertIndexExcluding(layer, order, -1);
        }

        private static int FindShowOrderInsertIndexExcluding(LayerData layer, int order, int excludeIndex)
        {
            int count = 0;
            for (int i = 0; i < layer.Count; i++)
            {
                if (i == excludeIndex)
                    continue;
                if (layer.Items[i].LastShowOrder > order)
                    return count;
                count++;
            }

            return count;
        }

        private void ApplyWindowDepth(UIMetadata meta)
        {
            if (meta?.View == null)
                return;

            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            if ((uint)typeId >= (uint)layer.TypeIdToIndex.Length)
                return;

            int index = layer.TypeIdToIndex[typeId];
            if (index < 0)
                return;

            meta.View.Depth = meta.MetaInfo.UILayer * LAYER_DEEP + index * WINDOW_DEEP;
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
                return false;

            for (int layerIndex = _openUI.Length - 1; layerIndex >= 0; layerIndex--)
            {
                LayerData layer = _openUI[layerIndex];
                if (layer == null)
                    continue;

                for (int i = layer.Count - 1; i >= 0; i--)
                {
                    UIMetadata metadata = layer.Items[i];
                    if (metadata == null || metadata.State == UIState.Uninitialized || metadata.State == UIState.Destroying)
                        continue;

                    if (predicate(metadata.MetaInfo.RuntimeTypeHandle))
                        return await CloseUIAsync(metadata.MetaInfo.RuntimeTypeHandle, force);
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
                    continue;

                for (int i = layer.Count - 1; i >= 0; i--)
                {
                    UIMetadata metadata = layer.Items[i];
                    if (!IsTopVisibleHolderCandidate(metadata))
                        continue;

                    UIHolderObjectBase candidate = metadata.View.Holder;
                    if (candidate != null && candidate.IsValid() && (predicate == null || predicate(candidate)))
                    {
                        holder = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsTopVisibleHolderCandidate(UIMetadata metadata)
        {
            return metadata != null && metadata.View != null && UIStateMachine.IsDisplayActive(metadata.State) && metadata.View.Visible;
        }

        private int Pop(UIMetadata meta)
        {
            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            if ((uint)typeId >= (uint)layer.TypeIdToIndex.Length)
                return -1;

            int index = layer.TypeIdToIndex[typeId];
            if (index < 0)
                return -1;

            int last = layer.Count - 1;
            for (int i = index; i < last; i++)
            {
                layer.Items[i] = layer.Items[i + 1];
                layer.TypeIdToIndex[layer.Items[i].MetaInfo.TypeId] = i;
            }

            layer.Items[last] = null;
            layer.Count = last;
            layer.TypeIdToIndex[typeId] = -1;
            RemoveUpdateableWindow(meta);
            return index;
        }

        private void RemoveFromOpenStack(UIMetadata meta)
        {
            if (!IsMetaInOpenStack(meta))
                return;

            int index = Pop(meta);
            SortWindowDepth(meta.MetaInfo.UILayer, index);
        }

        private bool IsMetaInOpenStack(UIMetadata meta)
        {
            if (meta == null || (uint)meta.MetaInfo.UILayer >= (uint)_openUI.Length)
                return false;
            LayerData layer = _openUI[meta.MetaInfo.UILayer];
            int typeId = meta.MetaInfo.TypeId;
            return (uint)typeId < (uint)layer.TypeIdToIndex.Length && layer.TypeIdToIndex[typeId] >= 0;
        }

        private void UpdateLayerParent(UIMetadata meta)
        {
            if (meta.View?.Holder == null || !meta.View.Holder.IsValid())
                return;
            meta.View.Holder.transform.SetParent(GetLayerRect(meta.MetaInfo.UILayer), false);
        }

        private void SortWindowDepth(int layerIndex, int startIndex = 0)
        {
            if ((uint)layerIndex >= (uint)_openUI.Length)
                return;
            LayerData layer = _openUI[layerIndex];
            if (startIndex < 0)
                startIndex = 0;
            int baseDepth = layerIndex * LAYER_DEEP;
            for (int i = startIndex; i < layer.Count; i++)
            {
                UIBase view = layer.Items[i]?.View;
                if (view != null)
                    view.Depth = baseDepth + i * WINDOW_DEEP;
            }
        }

        private void AddUpdateableWindow(UIMetadata meta)
        {
            if (meta == null || !meta.MetaInfo.NeedUpdate)
                return;
            for (int i = 0; i < _updateableWindowCount; i++)
                if (_updateableWindows[i] == meta)
                    return;

            if (_updateableWindowCount == _updateableWindows.Length)
                Array.Resize(ref _updateableWindows, _updateableWindows.Length << 1);
            _updateableWindows[_updateableWindowCount++] = meta;
        }

        private void RemoveUpdateableWindow(UIMetadata meta)
        {
            if (meta == null || !meta.MetaInfo.NeedUpdate)
                return;
            for (int i = 0; i < _updateableWindowCount; i++)
            {
                if (_updateableWindows[i] != meta)
                    continue;
                int last = _updateableWindowCount - 1;
                _updateableWindows[i] = _updateableWindows[last];
                _updateableWindows[last] = null;
                _updateableWindowCount = last;
                return;
            }
        }
    }
}
