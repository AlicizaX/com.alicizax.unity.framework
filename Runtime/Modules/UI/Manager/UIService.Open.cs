using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private const int WindowsPerLayerLimit = LAYER_DEEP / WINDOW_DEEP;

        private readonly List<UIWindowRecord>[] _openUI = new List<UIWindowRecord>[(int)UILayer.All];

        /// <summary>销毁中/已销毁的实例不再代表 record：清理引用后按“无实例”处理。</summary>
        private static UIWindow NormalizeLiveView(UIWindowRecord record)
        {
            UIWindow view = record.View;
            if (view == null) return null;
            if (view.State is not (UIState.Destroying or UIState.Destroyed)) return view;
            if (record.View == view) record.View = null;
            return null;
        }

        private UIWindow ShowUISyncCore(UIWindowRecord record, object[] userDatas)
        {
            if (record == null) return null;
            if (record.Flight != null)
            {
                Log.Error("[UI] An asynchronous load for this window is in progress. Await it instead.");
                return null;
            }

            UIWindow view = NormalizeLiveView(record);
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
            return CommitWindow(record, view, userDatas).View;
        }

        private async UniTask<UIOpenResult> RequestShow(
            UIWindowRecord record, object[] userDatas, CancellationToken token, bool waitForClose = false)
        {
            if (token.IsCancellationRequested || _shuttingDown) return UIOpenResult.Cancelled;
            if (record == null) return UIOpenResult.Failed;

            UIWindow view = NormalizeLiveView(record);
            if (view != null && view.State == UIState.Closing)
            {
                if (!waitForClose)
                {
                    Log.Warning("[UI] Ignoring Show for {0} while it is closing.",
                        record.Metadata.UILogicTypeName);
                    return new UIOpenResult(null, UIOpenStatus.Ignored);
                }

                UniTask settled = view.AwaitClosed();
                if (token.CanBeCanceled)
                {
                    try { await settled.AttachExternalCancellation(token); }
                    catch (OperationCanceledException) { return UIOpenResult.Cancelled; }
                }
                else await settled;
                if (token.IsCancellationRequested || _shuttingDown) return UIOpenResult.Cancelled;
                view = NormalizeLiveView(record);
            }

            BringToFront(record);
            UIWindowLoad flight = record.Flight;
            if (flight == null && view != null)
            {
                UIOpenResult result = CommitWindow(record, view, userDatas);
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
                view = record.Metadata.CreateWindow(this);
                record.View = view;
                if (view == null)
                {
                    RemoveFromOpenStack(record);
                    return UIOpenResult.Failed;
                }
                flight = record.Flight = new UIWindowLoad(view);
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
                FailLoad(record, flight, UIOpenResult.Failed);
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
                FailLoad(record, flight, UIOpenResult.Cancelled);
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

        private void FailLoad(UIWindowRecord record, UIWindowLoad flight, UIOpenResult result)
        {
            RemoveFromOpenStack(record);
            if (record.View == flight.View) record.View = null;
            flight.View.DestroyNow();
            FinishLoadRequests(flight, result);
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
            if (record.View != view || view.State is UIState.Destroying or UIState.Destroyed)
                return UIOpenResult.Cancelled;
            if (view.DestroyRequested)
                return UIOpenResult.Cancelled;
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

        private async UniTask RequestClose(UIWindowRecord record, bool force, bool skipTransition,
            bool affectNavigation, CancellationToken token = default)
        {
            if (record?.View is not { } view || view.State is UIState.Destroying or UIState.Destroyed)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return;
            }
            if (record.Flight != null)
            {
                CancelWindowLoad(record);
                return;
            }
            if (record.IsCached)
            {
                if (force) view.DestroyNow();
                return;
            }
            if (view.State is UIState.CreatedUI or UIState.Loaded)
            {
                view.DestroyNow();
                return;
            }

            record.ForceClose |= force;
            if (view.State == UIState.Closed)
            {
                FinalizeAfterClosed(record, view);
                return;
            }
            if (affectNavigation && _currentView == view) ClearHistory();

            UniTask closing = view.InternalClose(skipTransition, destroy: record.ForceClose);
            if (!token.CanBeCanceled)
            {
                await closing;
                return;
            }
            try { await closing.AttachExternalCancellation(token); }
            catch (OperationCanceledException) { }
        }

        private void FinalizeAfterClosed(UIWindowRecord record, UIWindow view)
        {
            // 两个调用方（RequestClose / NotifyWindowClosed）都只在 Closed 且未缓存时进入这里。
            if (record.View != view) return;
            RemoveFromOpenStack(record);
            if (record.ForceClose || view.DestroyRequested || record.MetaInfo.CacheTime == 0)
                view.DestroyNow();
            else _cache.Add(record);
        }

        private void BringToFront(UIWindowRecord record)
        {
            List<UIWindowRecord> windows = _openUI[record.MetaInfo.UILayer];
            _cache.Remove(record);
            if (record.LayerIndex >= 0 && record.LayerIndex == windows.Count - 1) return;
            int start = record.LayerIndex < 0 ? windows.Count : record.LayerIndex;
            RemovePosition(record);
            windows.Add(record);
            record.LayerIndex = windows.Count - 1;
            Debug.Assert(windows.Count <= WindowsPerLayerLimit,
                "[UI] Too many windows in one layer: canvas sorting order overlaps the next layer.");
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
