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
        public bool InCache = false;
        public readonly bool IsValid;

        private enum OperationKind : byte
        {
            Idle = 0,
            Showing = 1,
            Closing = 2,
        }

        private CancellationTokenSource _loadCancellationTokenSource;
        private UniTaskCompletionSource<UIBase> _showCompletionSource;
        private UniTaskCompletionSource<bool> _closeCompletionSource;
        private System.Object[] _pendingShowUserDatas;
        private bool _hasPendingShowUserDatas;
        private int _operationVersion;
        private OperationKind _operation;

        public int OperationVersion => _operationVersion;
        public bool ShowInProgress => _operation == OperationKind.Showing;
        public bool CloseInProgress => _operation == OperationKind.Closing;

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

        public bool BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts)
        {
            if (_operation != OperationKind.Idle)
            {
                operationVersion = -1;
                loadCts = null;
                return false;
            }

            _loadCancellationTokenSource?.Cancel();
            loadCts = new CancellationTokenSource();
            _loadCancellationTokenSource = loadCts;

            CompleteShowOperation(null);
            operationVersion = ++_operationVersion;
            _operation = OperationKind.Showing;
            return true;
        }

        public bool BeginCloseOperation(out int operationVersion)
        {
            if (_operation == OperationKind.Closing)
            {
                operationVersion = -1;
                return false;
            }

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;
            operationVersion = ++_operationVersion;
            _operation = OperationKind.Closing;

            CompleteShowOperation(null, clearPendingUserDatas: true);
            _closeCompletionSource ??= new UniTaskCompletionSource<bool>();
            return true;
        }

        public void EndShowOperation(int operationVersion, CancellationTokenSource loadCts)
        {
            if (_operationVersion == operationVersion && _operation == OperationKind.Showing)
            {
                _operation = OperationKind.Idle;
            }

            if (ReferenceEquals(_loadCancellationTokenSource, loadCts))
            {
                _loadCancellationTokenSource = null;
            }
        }

        public void EndCloseOperation(int operationVersion)
        {
            if (_operationVersion == operationVersion && _operation == OperationKind.Closing)
            {
                _operation = OperationKind.Idle;
            }
        }

        public void CompleteCloseOperation(bool success)
        {
            UniTaskCompletionSource<bool> closeCompletionSource = _closeCompletionSource;
            _closeCompletionSource = null;
            closeCompletionSource?.TrySetResult(success);
        }

        public UniTask<bool> WaitForCloseOperationAsync()
        {
            if (_closeCompletionSource != null)
            {
                return _closeCompletionSource.Task;
            }

            if (_operation == OperationKind.Closing)
            {
                _closeCompletionSource = new UniTaskCompletionSource<bool>();
                return _closeCompletionSource.Task;
            }

            return UniTask.FromResult(true);
        }

        public void CancelAsyncOperations()
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;
            _operationVersion++;
            _operation = OperationKind.Idle;
            CompleteShowOperation(null);
            CompleteCloseOperation(false);
        }

        public void RequestCancelShowLoad()
        {
            if (_operation != OperationKind.Showing)
            {
                return;
            }

            _loadCancellationTokenSource?.Cancel();
        }

        public void RefreshLiveShowUserDatas(System.Object[] userDatas)
        {
            View?.RefreshParams(userDatas);
            if (State == UIState.Opened)
            {
                View?.InternalRefreshOpened();
            }
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
            if (_showCompletionSource != null)
            {
                return _showCompletionSource.Task;
            }

            if (_operation == OperationKind.Showing)
            {
                _showCompletionSource = new UniTaskCompletionSource<UIBase>();
                return _showCompletionSource.Task;
            }

            return UniTask.FromResult(State == UIState.Opened ? View : null);
        }

        public int BeginWidgetCreate(out CancellationTokenSource loadCts)
        {
            _loadCancellationTokenSource?.Cancel();
            loadCts = new CancellationTokenSource();
            _loadCancellationTokenSource = loadCts;
            return ++_operationVersion;
        }

        public void EndWidgetCreate(int operationVersion, CancellationTokenSource loadCts)
        {
            if (ReferenceEquals(_loadCancellationTokenSource, loadCts))
            {
                _loadCancellationTokenSource = null;
            }
        }


        public void CompleteShowOperation(UIBase result, bool clearPendingUserDatas = true)
        {
            UniTaskCompletionSource<UIBase> showCompletionSource = _showCompletionSource;
            _showCompletionSource = null;
            if (clearPendingUserDatas)
            {
                ClearPendingShowUserDatas();
            }

            showCompletionSource?.TrySetResult(result);
        }


        public void FailShowOperation(Exception exception)
        {
            UniTaskCompletionSource<UIBase> showCompletionSource = _showCompletionSource;
            _showCompletionSource = null;
            ClearPendingShowUserDatas();
            showCompletionSource?.TrySetException(exception);
        }

        public bool HasPendingShowUserDatas => _hasPendingShowUserDatas;

        private void ClearPendingShowUserDatas()
        {
            _pendingShowUserDatas = null;
            _hasPendingShowUserDatas = false;
        }

        internal void ResetRuntimeState()
        {
            CancelAsyncOperations();
            _operation = OperationKind.Idle;
            View = null;
            InCache = false;
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
