using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private readonly List<UIWindowRecord>[] _openUI = new List<UIWindowRecord>[(int)UILayer.All];

        private UIWindow ShowUISyncCore(UIWindowRecord record, object[] userDatas)
        {
            if (record == null) return null;
            if (record.Flight != null)
            {
                Log.Error("[UI] An asynchronous load for this window is in progress. Await it instead.");
                return null;
            }

            UIWindow view = record.View;
            if (view != null && (view.State == UIState.Destroying || view.State == UIState.Destroyed))
            {
                if (record.View == view) record.View = null;
                view = null;
            }
            if (view != null && view.State == UIState.Closing)
            {
                Log.Warning("[UI] Ignoring Show for {0} while it is closing.",
                    record.Metadata.UILogicTypeName);
                return null;
            }

            if (view == null)
            {
                record.View = view = record.Metadata.CreateWindow(this);
                if (view == null) return null;
                if (!UIHolderFactory.CreateUIResourceSync(view, GetLayerRect(record.MetaInfo.UILayer)))
                {
                    view.DestroyNow();
                    return null;
                }
            }

            BringToFront(record);
            UIOpenResult result = CommitWindow(record, view, userDatas);
            return result.View;
        }

        private async UniTask<UIOpenResult> RequestShow(
            UIWindowRecord record, object[] userDatas, CancellationToken token, bool waitForClose = false)
        {
            if (token.IsCancellationRequested || _shuttingDown) return UIOpenResult.Cancelled;
            if (record == null) return UIOpenResult.Failed;

            UIWindow view = record.View;
            if (view != null && (view.State == UIState.Destroying || view.State == UIState.Destroyed))
            {
                if (record.View == view) record.View = null;
                view = null;
            }
            if (view != null && view.State == UIState.Closing)
            {
                if (!waitForClose)
                {
                    Log.Warning("[UI] Ignoring Show for {0} while it is closing.",
                        record.Metadata.UILogicTypeName);
                    return new UIOpenResult(null, UIOpenStatus.Ignored);
                }

                UniTask settled = record.Settled?.Task ?? view.AwaitClosed();
                if (token.CanBeCanceled)
                {
                    try { await settled.AttachExternalCancellation(token); }
                    catch (OperationCanceledException) { return UIOpenResult.Cancelled; }
                }
                else await settled;
                if (token.IsCancellationRequested || _shuttingDown) return UIOpenResult.Cancelled;
                view = record.View;
                if (view != null && (view.State == UIState.Destroying || view.State == UIState.Destroyed))
                {
                    if (record.View == view) record.View = null;
                    view = null;
                }
            }

            BringToFront(record);
            UIWindowLoad flight = record.Flight;
            if (flight == null && record.View != null &&
                record.View.State != UIState.Destroying && record.View.State != UIState.Destroyed)
            {
                UIOpenResult result = CommitWindow(record, record.View, userDatas);
                if (result.View == null || result.View.IsOpen) return result;
                UniTask<bool> opened = result.View.AwaitLogicalOpen();
                bool success;
                try { success = token.CanBeCanceled ? await opened.AttachExternalCancellation(token) : await opened; }
                catch (OperationCanceledException) { return UIOpenResult.Cancelled; }
                return success && result.View.IsOpen && record.View == result.View ? result : UIOpenResult.Cancelled;
            }

            bool start = flight == null;
            if (start)
            {
                record.View = record.Metadata.CreateWindow(this);
                if (record.View == null)
                {
                    RemoveFromOpenStack(record);
                    return UIOpenResult.Failed;
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
            return await request.Completion.Task;
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
                flight.Cancel();
                flight.View.DestroyNow();
            }
            request.Finish(UIOpenResult.Cancelled);
        }

        private async UniTask LoadWindow(UIWindowRecord record, UIWindowLoad flight)
        {
            bool loaded;
            try
            {
                loaded = !flight.Cancellation.IsCancellationRequested &&
                    await UIHolderFactory.CreateUIResourceAsync(flight.View, GetLayerRect(record.MetaInfo.UILayer), flight.Cancellation.Token);
            }
            finally { flight.Cancellation.Dispose(); }
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
                    initializeArgs = request.Arguments;
            }
            if (!hasWaiter)
            {
                RemoveFromOpenStack(record);
                record.View = null;
                flight.View.DestroyNow();
                FinishLoadRequests(flight, UIOpenResult.Cancelled);
                return;
            }

            UIOpenResult shared = CommitWindow(record, flight.View, initializeArgs);
            if (flight.Waiters == 0)
            {
                flight.View.DestroyNow();
                shared = UIOpenResult.Cancelled;
            }
            for (int i = 0; i < flight.Requests.Count; i++)
            {
                UIOpenRequest request = flight.Requests[i];
                if (request.Completed) continue;
                if (request.Token.IsCancellationRequested)
                {
                    request.Finish(UIOpenResult.Cancelled);
                    continue;
                }
                if (record.View != flight.View || !flight.View.IsOpen) shared = UIOpenResult.Cancelled;
                if (request.Completed) continue;
                flight.Waiters--;
                request.Finish(shared);
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
            flight.Cancel();
            flight.View.DestroyNow();
            FinishLoadRequests(flight, UIOpenResult.Cancelled);
        }

        private UIOpenResult CommitWindow(UIWindowRecord record, UIWindow view, object[] userDatas)
        {
            if (record.View != view || view.State == UIState.Destroying || view.State == UIState.Destroyed)
                return UIOpenResult.Cancelled;
            if (view.State == UIState.Closing)
            {
                Log.Warning("[UI] Ignoring Show for {0} while it is closing.",
                    record.Metadata.UILogicTypeName);
                return new UIOpenResult(view.IsOpen ? view : null, UIOpenStatus.Ignored);
            }
            if (view.DestroyRequested)
                return UIOpenResult.Cancelled;
            if (record.LayerIndex < 0) BringToFront(record);
            if (view.State is UIState.Loaded or UIState.Initialized or UIState.Closed)
            {
                record.ForceClose = false;
                view.SetCanvasEnabled(true);
                view.Holder.transform.SetParent(GetLayerRect(record.MetaInfo.UILayer), false);
                if (view.DestroyRequested || record.View != view)
                    return UIOpenResult.Cancelled;
                view.Depth = record.MetaInfo.UILayer * LAYER_DEEP + record.LayerIndex * WINDOW_DEEP;
            }
            if (!view.InternalOpen(userDatas))
                return UIOpenResult.Cancelled;
            return new UIOpenResult(view, UIOpenStatus.Opened);
        }

        private async UniTask RequestClose(UIWindowRecord record, bool force, bool skipTransition, bool affectNavigation)
        {
            if (record == null)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return;
            }
            if (record.Flight != null)
            {
                CancelWindowLoad(record);
                return;
            }

            UIWindow view = record.View;
            if (view == null)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return;
            }
            if (record.IsCached)
            {
                if (force) view.DestroyNow();
                return;
            }
            if (view.State == UIState.CreatedUI || view.State == UIState.Loaded)
            {
                view.DestroyNow();
                return;
            }
            if (view.State == UIState.Destroying || view.State == UIState.Destroyed)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return;
            }

            record.ForceClose |= force;
            if (view.State == UIState.Closed)
            {
                FinalizeAfterClosed(record, view);
                return;
            }
            if (affectNavigation && _currentView == view) ClearHistory();
            record.Settled ??= new UniTaskCompletionSource();
            UniTaskCompletionSource settled = record.Settled;
            try
            {
                await view.InternalClose(skipTransition, destroy: record.ForceClose);
            }
            finally
            {
                if (record.Settled == settled) record.Settled = null;
                settled.TrySetResult();
            }
        }

        private void FinalizeAfterClosed(UIWindowRecord record, UIWindow view)
        {
            if (record.View != view) return;
            if (view.State == UIState.Destroying || view.State == UIState.Destroyed) return;
            if (record.IsCached) return;
            if (view.State != UIState.Closed) return;
            RemoveFromOpenStack(record);
            if (record.ForceClose || view.DestroyRequested || record.MetaInfo.CacheTime == 0)
                view.DestroyNow();
            else CacheWindow(record);
        }

        private void BringToFront(UIWindowRecord record)
        {
            List<UIWindowRecord> windows = _openUI[record.MetaInfo.UILayer];
            RemoveFromCache(record);
            if (record.LayerIndex == windows.Count - 1 && record.LayerIndex >= 0) return;
            int start = record.LayerIndex < 0 ? windows.Count : record.LayerIndex;
            RemovePosition(record);
            record.LayerIndex = windows.Count;
            windows.Add(record);
            SortWindowDepth(record.MetaInfo.UILayer, start);
        }

        public async UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false)
        {
            if (predicate == null) return false;
            for (int layerIndex = _openUI.Length - 1; layerIndex >= 0; layerIndex--)
            {
                List<UIWindowRecord> windows = _openUI[layerIndex];
                if (windows == null) continue;
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    UIWindowRecord record = windows[i];
                    if (!predicate(record.MetaInfo.RuntimeTypeHandle)) continue;
                    await RequestClose(record, force, false, affectNavigation: true);
                    return true;
                }
            }
            return false;
        }

        public bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder)
        {
            holder = null;
            for (int layerIndex = _openUI.Length - 1; layerIndex >= 0; layerIndex--)
            {
                List<UIWindowRecord> windows = _openUI[layerIndex];
                if (windows == null) continue;
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    UIWindow view = windows[i].View;
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
            List<UIWindowRecord> windows = _openUI[record.MetaInfo.UILayer];
            windows.RemoveAt(index);
            for (int i = index; i < windows.Count; i++) windows[i].LayerIndex = i;
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
            List<UIWindowRecord> windows = _openUI[layerIndex];
            for (int i = startIndex; i < windows.Count; i++)
            {
                UIWindow view = windows[i].View;
                if (view != null) view.Depth = layerIndex * LAYER_DEEP + i * WINDOW_DEEP;
            }
        }
    }
}
