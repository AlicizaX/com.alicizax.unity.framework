using System;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX;
using Cysharp.Text;
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

        private CancellationTokenSource _cancellationTokenSource;

        public CancellationToken ExistingCancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
        private int _operationVersion;
        private bool _cancelRequested;
        private bool _showInProgress;
        private bool _closeInProgress;

        public int OperationVersion => _operationVersion;
        public bool CancelRequested => _cancelRequested;
        public bool ShowInProgress => _showInProgress;
        public bool CloseInProgress => _closeInProgress;

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

        public bool BeginShowOperation(bool createCancellationToken, out int operationVersion)
        {
            if (_showInProgress)
            {
                operationVersion = -1;
                return false;
            }

            CancellationTokenSource previousCts = _cancellationTokenSource;
            _cancellationTokenSource = createCancellationToken ? new CancellationTokenSource() : null;
            operationVersion = ++_operationVersion;
            _cancelRequested = false;
            _showInProgress = true;
            _closeInProgress = false;
            previousCts?.Cancel();
            return true;
        }

        public bool BeginCloseOperation(bool createCancellationToken, out int operationVersion)
        {
            if (_closeInProgress)
            {
                operationVersion = -1;
                return false;
            }

            CancellationTokenSource previousCts = _cancellationTokenSource;
            _cancellationTokenSource = createCancellationToken ? new CancellationTokenSource() : null;
            operationVersion = ++_operationVersion;
            _cancelRequested = false;
            _closeInProgress = true;
            _showInProgress = false;
            previousCts?.Cancel();
            return true;
        }

        public void EndShowOperation(int operationVersion)
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
            _cancelRequested = true;
            _operationVersion++;
            _showInProgress = false;
            _closeInProgress = false;
            CancelTokenOnly();
        }

        private void CancelTokenOnly()
        {
            CancellationTokenSource cts = _cancellationTokenSource;
            _cancellationTokenSource = null;
            cts?.Cancel();
        }

        internal void ResetRuntimeState()
        {
            _cancelRequested = false;
            _operationVersion++;
            _showInProgress = false;
            _closeInProgress = false;
            View = null;
            InCache = false;
            CancelTokenOnly();
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
