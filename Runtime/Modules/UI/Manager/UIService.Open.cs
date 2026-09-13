using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private sealed class LayerData
        {
            internal readonly List<UIWindowRecord> Items = new(16);
            internal int Count => Items.Count;
        }

        private readonly LayerData[] _openUI = new LayerData[(int)UILayer.All];

        private UIBase ShowUISyncCore(UIWindowRecord record, object[] userDatas)
        {
            if (record == null) return null;
            if (record.Flight != null)
                throw new InvalidOperationException("An asynchronous UIKit load for this panel type is in progress. Await it instead.");

            UIBase view = record.View;
            view?.ValidateOpen();
            if (view == null || view.DestroyRequested)
            {
                record.View = view = record.Metadata.CreateUI(this);
                if (view == null) return null;
                if (!UIHolderFactory.CreateUIResourceSync(view, GetLayerRect(record.MetaInfo.UILayer)))
                {
                    view.DestroyNow();
                    return null;
                }
            }
            BringToFront(record);
            return CommitWindow(record, view, userDatas).View;
        }

        private UniTask<UIOpenResult> RequestShow(UIWindowRecord record, object[] userDatas, CancellationToken token)
        {
            if (token.IsCancellationRequested || _shuttingDown)
                return UniTask.FromResult(UIOpenResult.Cancelled);
            if (record == null) return UniTask.FromResult(UIOpenResult.Failed);
            record.View?.ValidateOpen();
            BringToFront(record);
            UIWindowLoad flight = record.Flight;
            if (flight == null && record.View != null && !record.View.DestroyRequested)
                return UniTask.FromResult(CommitWindow(record, record.View, userDatas));

            bool start = flight == null;
            if (start)
            {
                record.View = record.Metadata.CreateUI(this);
                if (record.View == null)
                {
                    RemoveFromOpenStack(record);
                    return UniTask.FromResult(UIOpenResult.Failed);
                }
                flight = record.Flight = new UIWindowLoad(record.View);
            }

            var request = new UIOpenRequest(userDatas, token);
            flight.Requests.Add(request);
            flight.Waiters++;
            if (token.CanBeCanceled)
            {
                UIWindowLoad captured = flight;
                request.Registration = token.Register(() => CancelRequestOnMainThread(record, captured, request).Forget());
                if (request.Completed) request.Registration.Dispose();
            }
            if (start) LoadWindow(record, flight).Forget();
            return request.Completion.Task;
        }

        private async UniTask CancelRequestOnMainThread(UIWindowRecord record, UIWindowLoad flight, UIOpenRequest request)
        {
            if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
            if (request.Completed) return;
            flight.Waiters--;
            if (flight.Waiters == 0 && record.Flight == flight)
            {
                record.Flight = null;
                RemoveFromOpenStack(record);
                if (record.View == flight.View) record.View = null;
                flight.View.DestroyNow();
            }
            request.Finish(UIOpenResult.Cancelled);
        }

        private async UniTask LoadWindow(UIWindowRecord record, UIWindowLoad flight)
        {
            bool loaded = !flight.Cancellation.IsCancellationRequested &&
                await UIHolderFactory.CreateUIResourceAsync(flight.View, GetLayerRect(record.MetaInfo.UILayer), flight.Cancellation.Token);
            flight.View.EndResourceLoad(flight.Cancellation);
            if (record.Flight != flight) return;
            record.Flight = null;

            if (!loaded)
            {
                RemoveFromOpenStack(record);
                record.View = null;
                flight.View.DestroyNow();
                FinishLoadRequests(flight, UIOpenResult.Failed);
                return;
            }

            bool hasWaiter = false;
            object[] initializeArgs = null;
            foreach (UIOpenRequest request in flight.Requests)
            {
                if (request.Completed || request.Token.IsCancellationRequested) continue;
                hasWaiter = true;
                if (request.Arguments != null && request.Arguments.Length > 0)
                {
                    initializeArgs = request.Arguments;
                    break;
                }
            }
            if (!hasWaiter)
            {
                RemoveFromOpenStack(record);
                record.View = null;
                flight.View.DestroyNow();
                FinishLoadRequests(flight, UIOpenResult.Cancelled);
                return;
            }
            flight.View.RefreshParams(initializeArgs);
            bool initialized = flight.View.InternalInitialize();
            for (int i = 0; i < flight.Requests.Count; i++)
            {
                UIOpenRequest request = flight.Requests[i];
                if (request.Completed) continue;
                UIOpenResult result = request.Token.IsCancellationRequested
                    ? UIOpenResult.Cancelled
                    : initialized ? CommitWindow(record, flight.View, request.Arguments)
                    : flight.View.State == UIState.Opened ? new UIOpenResult(flight.View, UIOpenStatus.Opened)
                    : UIOpenResult.Cancelled;
                if (result.Status == UIOpenStatus.Opened) initialized = true;
                if (request.Completed) continue;
                flight.Waiters--;
                request.Finish(result);
            }
            flight.Requests.Clear();
            if (flight.View.State == UIState.Initialized) flight.View.DestroyNow();
        }

        private static void FinishLoadRequests(UIWindowLoad flight, UIOpenResult result)
        {
            foreach (UIOpenRequest request in flight.Requests)
                if (!request.Completed) request.Finish(result);
            flight.Requests.Clear();
            flight.Waiters = 0;
        }

        private void CancelWindowLoad(UIWindowRecord record)
        {
            UIWindowLoad flight = record.Flight;
            if (flight == null) return;
            record.Flight = null;
            RemoveFromOpenStack(record);
            record.View = null;
            flight.View.DestroyNow();
            FinishLoadRequests(flight, UIOpenResult.Cancelled);
        }

        private UIOpenResult CommitWindow(UIWindowRecord record, UIBase view, object[] userDatas)
        {
            if (view.DestroyRequested || record.View != view) return UIOpenResult.Cancelled;
            view.ValidateOpen();
            if (view.State == UIState.Opening) return new UIOpenResult(view, UIOpenStatus.Opened);
            if (record.LayerIndex < 0) BringToFront(record);
            if (view.State == UIState.Loaded)
            {
                view.RefreshParams(userDatas);
                if (!view.InternalInitialize())
                    return view.State == UIState.Opened
                        ? new UIOpenResult(view, UIOpenStatus.Opened) : UIOpenResult.Cancelled;
            }

            record.ForceClose = false;
            int generation = view.TransitionGeneration;
            view.SetCanvasEnabled(true);
            view.Holder.transform.SetParent(GetLayerRect(record.MetaInfo.UILayer), false);
            if (view.DestroyRequested || record.View != view || generation != view.TransitionGeneration)
                return UIOpenResult.Cancelled;
            view.Depth = record.MetaInfo.UILayer * LAYER_DEEP + record.LayerIndex * WINDOW_DEEP;
            view.RefreshParams(userDatas);
            bool opened = view.InternalOpen();
            return opened ? new UIOpenResult(view, UIOpenStatus.Opened) : UIOpenResult.Cancelled;
        }

        private UniTask<bool> RequestClose(UIWindowRecord record, bool force, bool skipTransition)
        {
            if (record == null || record.View == null) return UniTask.FromResult(false);
            if (record.Flight != null)
            {
                CancelWindowLoad(record);
                return UniTask.FromResult(true);
            }
            UIBase view = record.View;
            record.ForceClose |= force;
            if (view.State == UIState.CreatedUI || view.State == UIState.Loaded)
            {
                view.DestroyNow();
                return UniTask.FromResult(true);
            }
            if (view.State == UIState.Cached)
            {
                if (force) view.DestroyNow();
                return UniTask.FromResult(true);
            }
            RemoveFromOpenStack(record);
            OnWindowUnavailable(view);
            return view.InternalClose(skipTransition);
        }

        internal void OnWindowClosed(UIBase view)
        {
            UIWindowRecord record = TryGetWindowRecord(view.GetType().TypeHandle);
            if (record != null && record.View == view) CacheWindow(record);
        }

        private void BringToFront(UIWindowRecord record)
        {
            LayerData layer = _openUI[record.MetaInfo.UILayer];
            RemoveFromCache(record);
            if (record.LayerIndex == layer.Count - 1 && record.LayerIndex >= 0) return;
            int start = record.LayerIndex < 0 ? layer.Count : record.LayerIndex;
            RemovePosition(record);
            record.LayerIndex = layer.Count;
            layer.Items.Add(record);
            SortWindowDepth(record.MetaInfo.UILayer, start);
        }

        public async UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false)
        {
            if (predicate == null) return false;
            for (int layerIndex = _openUI.Length - 1; layerIndex >= 0; layerIndex--)
            {
                LayerData layer = _openUI[layerIndex];
                if (layer == null) continue;
                for (int i = layer.Count - 1; i >= 0; i--)
                {
                    UIWindowRecord record = layer.Items[i];
                    if (predicate(record.MetaInfo.RuntimeTypeHandle))
                        return await RequestClose(record, force, false);
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
                if (layer == null) continue;
                for (int i = layer.Count - 1; i >= 0; i--)
                {
                    UIBase view = layer.Items[i].View;
                    if (view == null || !view.Visible) continue;
                    UIHolderObjectBase candidate = view.Holder;
                    if (candidate != null && candidate.IsValid() && (predicate == null || predicate(candidate)))
                    {
                        holder = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        private void RemovePosition(UIWindowRecord record)
        {
            int index = record.LayerIndex;
            if (index < 0) return;
            LayerData layer = _openUI[record.MetaInfo.UILayer];
            layer.Items.RemoveAt(index);
            for (int i = index; i < layer.Count; i++) layer.Items[i].LayerIndex = i;
            record.LayerIndex = -1;
        }

        private void RemoveFromOpenStack(UIWindowRecord record)
        {
            int index = record.LayerIndex;
            if (index < 0) return;
            RemovePosition(record);
            SortWindowDepth(record.MetaInfo.UILayer, index);
        }

        private void SortWindowDepth(int layerIndex, int startIndex = 0)
        {
            LayerData layer = _openUI[layerIndex];
            for (int i = startIndex; i < layer.Count; i++)
            {
                UIBase view = layer.Items[i].View;
                if (view != null) view.Depth = layerIndex * LAYER_DEEP + i * WINDOW_DEEP;
            }
        }
    }
}
