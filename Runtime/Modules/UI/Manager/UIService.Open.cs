using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        internal const int WindowsPerLayerLimit = LAYER_DEEP / WINDOW_DEEP;

        private static readonly Action<object> CancelRequestCallback =
            static state => CancelRequestOnMainThread((UIOpenRequest)state).Forget();

        private readonly UILayerStack[] _layerStacks = CreateLayerStacks();

        private static UILayerStack[] CreateLayerStacks()
        {
            var stacks = new UILayerStack[(int)UILayer.All];
            for (int i = 0; i < stacks.Length; i++) stacks[i] = new UILayerStack(i);
            return stacks;
        }

        private UIWindow ShowUISyncCore(UIWindowRecord record, object[] userDatas)
        {
            if (record == null) return null;
            if (record.Flight != null)
            {
                Log.Error("[UI] An asynchronous load for this window is in progress. Await it instead.");
                return null;
            }

            UIWindow view = record.View;
            if (view != null && view.State == UIState.Closing)
            {
                Log.Warning("[UI] Ignoring Show for {0} while it is closing.",
                    record.Metadata.UILogicTypeName);
                return null;
            }
            if (view == null)
            {
                view = record.Metadata.CreateWindow(this, record);
                if (view == null) return null;
                record.View = view;
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
            if (token.IsCancellationRequested || _lifetime != UILifetime.Running) return UIOpenResult.Cancelled;
            if (record == null) return UIOpenResult.Failed;

            UIWindow view = record.View;
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
                if (token.IsCancellationRequested || _lifetime != UILifetime.Running) return UIOpenResult.Cancelled;
                view = record.View;
            }

            if (view != null && record.Flight == null)
            {
                BringToFront(record);
                UIOpenResult result = CommitWindow(record, view, userDatas);
                if (result.View == null || result.View.IsOpen) return result;
                UniTask<bool> opened = result.View.AwaitLogicalOpen();
                bool success;
                try { success = token.CanBeCanceled ? await opened.AttachExternalCancellation(token) : await opened; }
                catch (OperationCanceledException) { return UIOpenResult.Cancelled; }
                return success && result.View.IsOpen && record.View == result.View ? result : UIOpenResult.Cancelled;
            }

            bool start = view == null;
            if (start)
            {
                view = record.Metadata.CreateWindow(this, record);
                if (view == null) return UIOpenResult.Failed;
                record.View = view;
                record.Flight = new UIWindowLoad(record);
            }
            BringToFront(record);

            UIWindowLoad flight = record.Flight;
            var request = new UIOpenRequest(flight, userDatas, token);
            flight.Requests.Add(request);
            if (token.CanBeCanceled)
            {
                request.Registration = token.Register(CancelRequestCallback, request);
                if (request.Completed) request.Registration.Dispose();
            }
            if (start) LoadWindow(record, view, flight).Forget();
            return await request.Completion.Task;
        }

        private static async UniTask CancelRequestOnMainThread(UIOpenRequest request)
        {
            if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
            if (request.Completed) return;
            UIWindowLoad flight = request.Flight;
            UIWindowRecord record = flight.Record;
            if (record.Flight == flight && !flight.HasWaiterOtherThan(request)) record.View.DestroyNow();
            request.Finish(UIOpenResult.Cancelled);
        }

        private async UniTask LoadWindow(UIWindowRecord record, UIWindow view, UIWindowLoad flight)
        {
            bool loaded;
            try
            {
                loaded = !flight.Cancellation.IsCancellationRequested &&
                    await UIHolderFactory.CreateUIResourceAsync(view, GetLayerRect(record.MetaInfo.UILayer), flight.Cancellation.Token);
            }
            finally { flight.Cancellation.Dispose(); }
            if (record.Flight != flight) return;
            record.Flight = null;

            if (!loaded || !flight.HasWaiter())
            {
                view.DestroyNow();
                flight.Finish(loaded ? UIOpenResult.Cancelled : UIOpenResult.Failed);
                return;
            }

            UIOpenResult shared = CommitWindow(record, view, flight.LatestArguments());
            if (!flight.HasWaiter())
            {
                view.DestroyNow();
                flight.Finish(UIOpenResult.Cancelled);
                return;
            }
            for (int i = 0; i < flight.Requests.Count; i++)
            {
                UIOpenRequest request = flight.Requests[i];
                if (request.Completed) continue;
                request.Finish(request.Token.IsCancellationRequested || !view.IsOpen || record.View != view
                    ? UIOpenResult.Cancelled : shared);
            }
            flight.Requests.Clear();
        }

        private UIOpenResult CommitWindow(UIWindowRecord record, UIWindow view, object[] userDatas)
        {
            if (view.DestroyRequested || !view.InternalOpen(userDatas)) return UIOpenResult.Cancelled;
            return new UIOpenResult(view, UIOpenStatus.Opened);
        }

        private UniTask RequestClose(UIWindowRecord record, bool force, bool skipTransition, bool affectNavigation)
        {
            UIWindow view = record?.View;
            if (view == null)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return UniTask.CompletedTask;
            }
            if (record.Flight != null || view.State is UIState.CreatedUI or UIState.Loaded)
            {
                view.DestroyNow();
                return UniTask.CompletedTask;
            }
            if (record.IsCached)
            {
                if (force) view.DestroyNow();
                return UniTask.CompletedTask;
            }
            if (affectNavigation && CurrentRecord == record) ClearHistory();
            return view.InternalClose(skipTransition, force);
        }

        internal void FinalizeAfterClosed(UIWindowRecord record)
        {
            UIWindow view = record.View;
            if (record.IsStacked) _layerStacks[record.MetaInfo.UILayer].Remove(record);
            if (view.DestroyRequested || record.MetaInfo.CacheTime == 0) view.DestroyNow();
            else _cache.Add(record);
        }

        internal void ReleaseRecord(UIWindowRecord record)
        {
            UIWindowLoad flight = record.Flight;
            record.Flight = null;
            if (record.IsStacked) _layerStacks[record.MetaInfo.UILayer].Remove(record);
            _cache.Remove(record);
            if (CurrentRecord == record) ClearHistory();
            record.View = null;
            if (flight == null) return;
            flight.Cancellation.Cancel();
            flight.Finish(UIOpenResult.Cancelled);
        }

        private void BringToFront(UIWindowRecord record)
        {
            UILayerStack stack = _layerStacks[record.MetaInfo.UILayer];
            _cache.Remove(record);
            if (stack.Top == record) return;
            if (record.IsStacked) stack.Remove(record);
            stack.Push(record);
        }

        public async UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false)
        {
            if (predicate == null) return false;
            for (int layerIndex = _layerStacks.Length - 1; layerIndex >= 0; layerIndex--)
            {
                for (UIWindowRecord record = _layerStacks[layerIndex].Top; record != null; record = record.StackBelow)
                {
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
            for (int layerIndex = _layerStacks.Length - 1; layerIndex >= 0; layerIndex--)
            {
                for (UIWindowRecord record = _layerStacks[layerIndex].Top; record != null; record = record.StackBelow)
                {
                    UIWindow view = record.View;
                    if (!view.Visible) continue;
                    UIHolderObjectBase candidate = view.Holder;
                    if (predicate == null || predicate(candidate))
                    {
                        holder = candidate;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
