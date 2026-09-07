using System;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal enum UIOperationKind : byte
    {
        Idle = 0,
        Showing = 1,
        Closing = 2,
    }

    internal enum UIRequestKind : byte
    {
        Show = 0,
        Close = 1,
    }

    internal sealed class UIRequest : MemoryObject
    {
        public UIRequestKind Kind;
        public object[] UserDatas;
        public bool Force;
        public bool SkipTransition;
        public bool Cancelled;
        public UniTaskCompletionSource<UIShowResult> ShowCompletion;
        public UniTaskCompletionSource<bool> CloseCompletion;

        public static UIRequest AcquireShow(object[] userDatas)
        {
            UIRequest request = MemoryPool.Acquire<UIRequest>();
            request.Kind = UIRequestKind.Show;
            request.UserDatas = userDatas;
            request.ShowCompletion = new UniTaskCompletionSource<UIShowResult>();
            return request;
        }

        public static UIRequest AcquireClose(bool force, bool skipTransition)
        {
            UIRequest request = MemoryPool.Acquire<UIRequest>();
            request.Kind = UIRequestKind.Close;
            request.Force = force;
            request.SkipTransition = skipTransition;
            request.CloseCompletion = new UniTaskCompletionSource<bool>();
            return request;
        }

        public override void Clear()
        {
            Kind = UIRequestKind.Show;
            UserDatas = null;
            Force = false;
            SkipTransition = false;
            Cancelled = false;
            ShowCompletion = null;
            CloseCompletion = null;
        }
    }

    internal sealed class UIMetadata
    {
        public UIBase View { get; private set; }
        public readonly UIMetaRegistry.UIMetaInfo MetaInfo;
        public readonly UIResRegistry.UIResInfo ResInfo;
        public readonly Type UILogicType;
        public readonly string UILogicTypeName;
        public readonly string UIHolderTypeName;
        public readonly bool IsValid;
        internal ulong CacheTimerHandle;
        internal int LastShowOrder;

        private UIRequest _active;
        private UIRequest _pendingShow;
        private UIRequest _pendingClose;
        private CancellationTokenSource _resourceLoadCancellation;
        private UIOperationKind _operation;
        private bool _processorRunning;
        private bool _retainView;

        public UIState State => View == null ? UIState.Uninitialized : View.State;
        internal bool IsProcessing => _processorRunning;
        internal bool IsShowing => _operation == UIOperationKind.Showing;
        internal bool IsClosing => _operation == UIOperationKind.Closing;
        internal bool HasPendingWork => _pendingShow != null || _pendingClose != null;
        internal bool HasPendingClose => _pendingClose != null;
        internal bool IsShowLoadCancelled => _active != null && _active.Kind == UIRequestKind.Show && _active.Cancelled;
        internal bool ShouldRetainView => _retainView;
        internal object[] LatestShowUserDatas => _active != null ? _active.UserDatas : _pendingShow?.UserDatas;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateUI()
        {
            if (View != null)
                return;
            if (!UIStateMachine.ValidateTransition(UILogicTypeName, UIState.Uninitialized, UIState.CreatedUI))
                return;
            View = (UIBase)Utility.InstanceFactory.CreateInstanceOptimized(UILogicType);
            if (View == null)
                Log.Error("[UI] Failed to create UI instance: {0}", UILogicTypeName);
        }

        internal UniTask<UIShowResult> EnqueueShow(object[] userDatas)
        {
            if (_operation == UIOperationKind.Showing && _active != null && !_active.Cancelled)
            {
                _active.UserDatas = userDatas;
                RefreshLiveShowUserDatas(userDatas);
                return _active.ShowCompletion.Task;
            }

            if (_pendingShow != null)
            {
                _pendingShow.UserDatas = userDatas;
                View?.RefreshParams(userDatas);
                return _pendingShow.ShowCompletion.Task;
            }

            _pendingShow = UIRequest.AcquireShow(userDatas);
            View?.RefreshParams(userDatas);
            return _pendingShow.ShowCompletion.Task;
        }

        internal UniTask<bool> EnqueueClose(bool force, bool skipTransition)
        {
            if (_operation == UIOperationKind.Showing && _active != null)
            {
                _active.Cancelled = true;
                CancelResourceLoad();
            }

            if (_pendingClose != null)
            {
                _pendingClose.Force |= force;
                _pendingClose.SkipTransition |= skipTransition;
                return _pendingClose.CloseCompletion.Task;
            }

            _pendingClose = UIRequest.AcquireClose(force, skipTransition);
            return _pendingClose.CloseCompletion.Task;
        }

        internal bool TryStartProcessor()
        {
            if (_processorRunning)
                return false;

            _processorRunning = true;
            return true;
        }

        internal bool TryDequeueClose(out UIRequest request)
        {
            request = _pendingClose;
            if (request == null)
                return false;

            _pendingClose = null;
            _active = request;
            _operation = UIOperationKind.Closing;
            return true;
        }

        internal bool TryDequeueShow(out UIRequest request)
        {
            request = _pendingShow;
            if (request == null)
                return false;

            _pendingShow = null;
            request.Cancelled = false;
            _active = request;
            _operation = UIOperationKind.Showing;
            return true;
        }

        internal void CompleteRequest(UIRequest request, UIShowResult result)
        {
            if (request == null)
                return;

            request.ShowCompletion?.TrySetResult(result);
            FinishActive(request);
            MemoryPool.Release(request);
        }

        internal void CompleteRequest(UIRequest request, bool closed)
        {
            if (request == null)
                return;

            request.CloseCompletion?.TrySetResult(closed);
            FinishActive(request);
            MemoryPool.Release(request);
        }

        internal void StopProcessor()
        {
            _processorRunning = false;
        }

        internal void DropPendingClose()
        {
            CompleteAndRelease(ref _pendingClose, cancelled: false);
        }

        internal void CancelActiveShowLoad()
        {
            if (_operation != UIOperationKind.Showing || _active == null)
                return;

            _active.Cancelled = true;
            CancelResourceLoad();
        }

        internal void RetainView()
        {
            _retainView = true;
        }

        internal void ReleaseRetainView()
        {
            _retainView = false;
        }

        internal CancellationTokenSource BeginResourceLoad()
        {
            if (_resourceLoadCancellation != null)
            {
                if (!_resourceLoadCancellation.IsCancellationRequested)
                    return _resourceLoadCancellation;

                _resourceLoadCancellation.Dispose();
                _resourceLoadCancellation = null;
            }

            _resourceLoadCancellation = new CancellationTokenSource();
            return _resourceLoadCancellation;
        }

        internal void EndResourceLoad(CancellationTokenSource cancellation)
        {
            if (!ReferenceEquals(_resourceLoadCancellation, cancellation))
                cancellation?.Dispose();
        }

        internal void CancelResourceLoad()
        {
            if (_resourceLoadCancellation == null || _resourceLoadCancellation.IsCancellationRequested)
                return;

            _resourceLoadCancellation.Cancel();
        }

        internal void RefreshLiveShowUserDatas(object[] userDatas)
        {
            if (_active != null && _active.Kind == UIRequestKind.Show)
                _active.UserDatas = userDatas;
            else if (_pendingShow != null)
                _pendingShow.UserDatas = userDatas;

            View?.RefreshParams(userDatas);
            if (State == UIState.Opened)
                View.InternalRefreshOpened();
        }

        internal void CancelRequests()
        {
            CancelResourceLoad();
            if (_active != null)
            {
                _active.Cancelled = true;
                if (!_processorRunning)
                {
                    _active.ShowCompletion?.TrySetResult(UIShowResult.Cancelled);
                    _active.CloseCompletion?.TrySetResult(false);
                    MemoryPool.Release(_active);
                    _active = null;
                    _operation = UIOperationKind.Idle;
                }
            }
            else
            {
                _operation = UIOperationKind.Idle;
            }

            CompleteAndRelease(ref _pendingShow, cancelled: true);
            CompleteAndRelease(ref _pendingClose, cancelled: false);
        }

        internal void ResetRuntimeState()
        {
            CancelRequests();
            DisposeResourceLoadCancellation();
            CacheTimerHandle = 0UL;
            _retainView = false;
            View = null;
        }

        internal async UniTask DisposeAsync()
        {
            PrepareDispose();
            if (View == null)
                return;

            UIState state = State;
            if (state != UIState.Uninitialized && state != UIState.Destroying && state != UIState.Destroyed)
                await View.InternalDestroy();

            View = null;
        }

        internal void DisposeImmediate()
        {
            PrepareDispose();
            if (View == null)
                return;

            UIState state = State;
            if (state != UIState.Uninitialized && state != UIState.Destroying && state != UIState.Destroyed)
                View.InternalDestroyImmediate();

            View = null;
        }

        public UIMetadata(Type uiType)
        {
            if (uiType == null)
            {
                UILogicType = null;
                UILogicTypeName = string.Empty;
                MetaInfo = default;
                ResInfo = default;
                UIHolderTypeName = string.Empty;
                IsValid = false;
                Log.Error("[UI] Metadata create failed: ui type is null.");
                return;
            }

            UILogicType = uiType;
            UILogicTypeName = uiType.Name;

            if (!UIMetaRegistry.TryGet(UILogicType.TypeHandle, out MetaInfo))
            {
                ResInfo = default;
                UIHolderTypeName = string.Empty;
                IsValid = false;
                Log.Error("[UI] Metadata not registered for {0}", UILogicType.FullName);
                return;
            }

            if (!UIResRegistry.TryGet(MetaInfo.HolderRuntimeTypeHandle, out ResInfo))
            {
                UIHolderTypeName = Type.GetTypeFromHandle(MetaInfo.HolderRuntimeTypeHandle)?.Name;
                IsValid = false;
                Log.Error("[UI] Resource metadata not registered for holder of {0}", UILogicType.FullName);
                return;
            }

            UIHolderTypeName = Type.GetTypeFromHandle(MetaInfo.HolderRuntimeTypeHandle)?.Name;
            IsValid = true;
        }

        private void PrepareDispose()
        {
            if (_processorRunning)
                CancelResourceLoad();
            else
                CancelRequests();
        }

        private void FinishActive(UIRequest request)
        {
            if (!ReferenceEquals(_active, request))
                return;

            _active = null;
            if (_operation != UIOperationKind.Idle)
                _operation = UIOperationKind.Idle;
        }

        private static void CompleteAndRelease(ref UIRequest request, bool cancelled)
        {
            if (request == null)
                return;

            request.ShowCompletion?.TrySetResult(cancelled ? UIShowResult.Cancelled : UIShowResult.Failed);
            request.CloseCompletion?.TrySetResult(false);
            MemoryPool.Release(request);
            request = null;
        }

        private void DisposeResourceLoadCancellation()
        {
            if (_resourceLoadCancellation == null)
                return;

            if (!_resourceLoadCancellation.IsCancellationRequested)
                _resourceLoadCancellation.Cancel();
            _resourceLoadCancellation.Dispose();
            _resourceLoadCancellation = null;
        }
    }
}
