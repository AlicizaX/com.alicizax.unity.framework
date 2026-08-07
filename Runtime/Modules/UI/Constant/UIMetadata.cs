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
        public bool StackRemovalPending;
        public readonly bool IsValid;

        private CancellationTokenSource _loadCancellationTokenSource;
        private UniTaskCompletionSource<UIBase> _showCompletionSource;
        private UniTaskCompletionSource<bool> _closeCompletionSource;
        private System.Object[] _pendingShowUserDatas;
        private bool _hasPendingShowUserDatas;
        private int _operationVersion;
        private bool _showInProgress;
        private bool _closeInProgress;

        public int OperationVersion => _operationVersion;
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


        // Window Show / Widget Create 共用同一操作入口
        public bool BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts)
        {
            // 关闭进行中禁止开加载，避免清掉 Close 标志 / version 错位
            if (_showInProgress || _closeInProgress)
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
            _showInProgress = true;
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
            _closeInProgress = true;
            _showInProgress = false;

            // 打断 Show 等待者并清 pending；Closing 再开由之后的 Show 重新写入
            CompleteShowOperation(null, clearPendingUserDatas: true);
            // 预先创建，确保 Close join / Show-after-close 可挂接
            _closeCompletionSource ??= new UniTaskCompletionSource<bool>();
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

        public void EndCloseOperation(int operationVersion)
        {
            if (_operationVersion == operationVersion)
            {
                _closeInProgress = false;
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

            if (_closeInProgress)
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
            _showInProgress = false;
            _closeInProgress = false;
            CompleteShowOperation(null);
            CompleteCloseOperation(false);
        }

        public void RequestCancelShowLoad()
        {
            if (!_showInProgress)
            {
                return;
            }

            _loadCancellationTokenSource?.Cancel();
        }

        // Opened/稳定显示：只刷 View，不写 sticky pending
        public void RefreshLiveShowUserDatas(System.Object[] userDatas)
        {
            View?.RefreshParams(userDatas);
            if (State == UIState.Opened)
            {
                View?.InternalRefreshOpened();
            }
        }

        // 仅 ShowInProgress（加载中 latest）或 Closing 再开意图
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

            if (_showInProgress)
            {
                _showCompletionSource = new UniTaskCompletionSource<UIBase>();
                return _showCompletionSource.Task;
            }

            return UniTask.FromResult(State == UIState.Opened ? View : null);
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
            _showInProgress = false;
            _closeInProgress = false;
            StackRemovalPending = false;
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
