using System;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed class UIMetadata
    {
        public UIBase View { get; private set; }
        public readonly UIMetaRegistry.UIMetaInfo MetaInfo;
        public readonly UIResRegistry.UIResInfo ResInfo;
        public readonly Type UILogicType;
        public readonly string UILogicTypeName;
        public readonly string UIHolderTypeName;
        public readonly bool HasSyncInitialize;
        public readonly bool HasAsyncInitialize;
        public bool InCache = false;
        public readonly bool IsValid;

        private CancellationTokenSource _loadCancellationTokenSource;
        private UniTaskCompletionSource<UIBase> _showCompletionSource;
        private System.Object[] _pendingShowUserDatas;
        private bool _hasPendingShowUserDatas;
        private int _operationVersion;
        private bool _cancelRequested;
        private bool _showInProgress;
        private bool _closeInProgress;

        public int OperationVersion => _operationVersion;
        public bool CancelRequested => _cancelRequested;
        public bool ShowInProgress => _showInProgress;
        public bool CloseInProgress => _closeInProgress;

        public bool IsOperationCurrent(int operationVersion)
        {
            return _operationVersion == operationVersion;
        }

        public UIState State
        {
            get
            {
                if (View == null) return UIState.Uninitialized;
                return View.State;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateUI()
        {
            if (View is null)
            {
                if (!UIStateMachine.ValidateTransition(UILogicTypeName, UIState.Uninitialized, UIState.CreatedUI))
                    return;

                View = (UIBase)Utility.InstanceFactory.CreateInstanceOptimized(UILogicType);
                if (View == null)
                {
                    Log.Error("[UI] Failed to create UI instance: {0}", UILogicTypeName);
                }
            }
        }

        public bool BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts, out UniTaskCompletionSource<UIBase> showCompletionSource)
        {
            if (_showInProgress)
            {
                operationVersion = -1;
                loadCts = null;
                showCompletionSource = null;
                return false;
            }

            _loadCancellationTokenSource?.Cancel();
            loadCts = new CancellationTokenSource();
            _loadCancellationTokenSource = loadCts;
            showCompletionSource = new UniTaskCompletionSource<UIBase>();
            _showCompletionSource = showCompletionSource;
            operationVersion = ++_operationVersion;
            _cancelRequested = false;
            _showInProgress = true;
            _closeInProgress = false;
            return true;
        }

        public bool BeginShowOperationSync(out int operationVersion)
        {
            if (_showInProgress)
            {
                operationVersion = -1;
                return false;
            }

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;
            operationVersion = ++_operationVersion;
            _cancelRequested = false;
            _showInProgress = true;
            _closeInProgress = false;
            return true;
        }

        public bool BeginCloseOperation(out int operationVersion)
        {
            if (_closeInProgress)
            {
                operationVersion = -1;
                return false;
            }

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;
            operationVersion = ++_operationVersion;
            _cancelRequested = false;
            _closeInProgress = true;
            _showInProgress = false;
            return true;
        }

        public void EndShowOperation(int operationVersion, CancellationTokenSource loadCts)
        {
            if (_operationVersion == operationVersion)
            {
                _showInProgress = false;
            }

            if (ReferenceEquals(_loadCancellationTokenSource, loadCts))
            {
                _loadCancellationTokenSource = null;
            }
        }

        public void EndShowOperationSync(int operationVersion)
        {
            if (_operationVersion == operationVersion)
            {
                _showInProgress = false;
            }
        }

        public void EndCloseOperation(int operationVersion)
        {
            if (_operationVersion == operationVersion)
            {
                _closeInProgress = false;
            }
        }

        public void CancelAsyncOperations()
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;
            _cancelRequested = true;
            _operationVersion++;
            _showInProgress = false;
            _closeInProgress = false;
            CompleteCurrentShowOperation(null);
        }

        public void SetPendingShowUserDatas(System.Object[] userDatas)
        {
            _pendingShowUserDatas = userDatas;
            _hasPendingShowUserDatas = true;
            View?.RefreshParams(userDatas);
        }

        public System.Object[] GetPendingShowUserDatas(System.Object[] fallback)
        {
            return _hasPendingShowUserDatas ? _pendingShowUserDatas : fallback;
        }

        public UniTask<UIBase> WaitForShowOperationAsync()
        {
            return _showCompletionSource != null
                ? _showCompletionSource.Task
                : UniTask.FromResult(State == UIState.Opened ? View : null);
        }

        public void CompleteShowOperation(UniTaskCompletionSource<UIBase> showCompletionSource, UIBase result)
        {
            showCompletionSource?.TrySetResult(result);
            ClearShowCompletionIfCurrent(showCompletionSource);
        }

        public void FailShowOperation(UniTaskCompletionSource<UIBase> showCompletionSource, Exception exception)
        {
            showCompletionSource?.TrySetException(exception);
            ClearShowCompletionIfCurrent(showCompletionSource);
        }

        private void CompleteCurrentShowOperation(UIBase result)
        {
            UniTaskCompletionSource<UIBase> showCompletionSource = _showCompletionSource;
            _showCompletionSource = null;
            ClearPendingShowUserDatas();
            showCompletionSource?.TrySetResult(result);
        }

        private void ClearShowCompletionIfCurrent(UniTaskCompletionSource<UIBase> showCompletionSource)
        {
            if (ReferenceEquals(_showCompletionSource, showCompletionSource))
            {
                _showCompletionSource = null;
                ClearPendingShowUserDatas();
            }
        }

        private void ClearPendingShowUserDatas()
        {
            _pendingShowUserDatas = null;
            _hasPendingShowUserDatas = false;
        }

        internal void ResetRuntimeState()
        {
            CancelAsyncOperations();
            _cancelRequested = false;
            _showInProgress = false;
            _closeInProgress = false;
            View = null;
            InCache = false;
        }

        public void Dispose()
        {
            DisposeAsync().Forget();
        }

        internal async UniTask DisposeAsync()
        {
            CancelAsyncOperations();

            if (State != UIState.Uninitialized && State != UIState.Destroying)
            {
                await View.InternalDestroy();
                View = null;
            }
        }

        internal void DisposeImmediate()
        {
            CancelAsyncOperations();

            if (State != UIState.Uninitialized && State != UIState.Destroying && View != null)
            {
                View.InternalDestroyImmediate();
                View = null;
            }
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
            HasSyncInitialize = typeof(IUISyncInitialize).IsAssignableFrom(uiType);
            HasAsyncInitialize = typeof(IUIAsyncInitialize).IsAssignableFrom(uiType);

            if (HasSyncInitialize && HasAsyncInitialize)
            {
                MetaInfo = default;
                ResInfo = default;
                UIHolderTypeName = string.Empty;
                IsValid = false;
                Log.Error("[UI] {0} cannot implement both IUISyncInitialize and IUIAsyncInitialize.", uiType.FullName);
                return;
            }

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
    }
}
