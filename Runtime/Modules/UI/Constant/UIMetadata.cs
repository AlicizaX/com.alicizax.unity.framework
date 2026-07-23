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

        /// <summary>Window Show：CTS + version；UTCS 由 Wait 懒创建。</summary>
        public bool BeginShowOperation(out int operationVersion, out CancellationTokenSource loadCts)
        {
            return BeginLoadOperation(out operationVersion, out loadCts);
        }

        /// <summary>Widget 创建：与 Show 同事务骨架，无 join 语义（调用方不 Wait）。</summary>
        public bool BeginCreateOperation(out int operationVersion, out CancellationTokenSource loadCts)
        {
            return BeginLoadOperation(out operationVersion, out loadCts);
        }

        private bool BeginLoadOperation(out int operationVersion, out CancellationTokenSource loadCts)
        {
            if (_showInProgress)
            {
                operationVersion = -1;
                loadCts = null;
                return false;
            }

            _loadCancellationTokenSource?.Cancel();
            loadCts = new CancellationTokenSource();
            _loadCancellationTokenSource = loadCts;
            // 释放泄漏 waiter（若有）；本轮默认无 join，Wait 时再懒创建。
            CompleteShowOperation(null);
            operationVersion = ++_operationVersion;
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
            _closeInProgress = true;
            _showInProgress = false;
            // 打断进行中的 Show：必须释放 join waiter，避免永久挂起。
            CompleteShowOperation(null);
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

        public void CancelAsyncOperations()
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;
            _operationVersion++;
            _showInProgress = false;
            _closeInProgress = false;
            CompleteShowOperation(null);
        }

        public void RequestCancelShowLoad()
        {
            if (!_showInProgress)
            {
                return;
            }

            _loadCancellationTokenSource?.Cancel();
        }

        public void SetPendingShowUserDatas(System.Object[] userDatas)
        {
            _pendingShowUserDatas = userDatas;
            _hasPendingShowUserDatas = true;
            View?.RefreshParams(userDatas);
            View?.InternalRefreshAfterInitialize();
        }

        public System.Object[] GetPendingShowUserDatas(System.Object[] fallback)
        {
            return _hasPendingShowUserDatas ? _pendingShowUserDatas : fallback;
        }

        /// <summary>
        /// 并发 Show join：优先已有字段 UTCS；仍在 Show 时懒创建一次；否则同步返回当前结果。
        /// 顺序必须先字段再 ShowInProgress，避免 Close 清标志后吞掉未完成 waiter。
        /// </summary>
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

        /// <summary>
        /// 完成当前 Show 的 join waiter（幂等：无 waiter 时 no-op）。
        /// </summary>
        public void CompleteShowOperation(UIBase result)
        {
            UniTaskCompletionSource<UIBase> showCompletionSource = _showCompletionSource;
            _showCompletionSource = null;
            ClearPendingShowUserDatas();
            showCompletionSource?.TrySetResult(result);
        }

        /// <summary>
        /// 以异常完成 join waiter（幂等：无 waiter 时 no-op）。
        /// </summary>
        public void FailShowOperation(Exception exception)
        {
            UniTaskCompletionSource<UIBase> showCompletionSource = _showCompletionSource;
            _showCompletionSource = null;
            ClearPendingShowUserDatas();
            showCompletionSource?.TrySetException(exception);
        }

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
