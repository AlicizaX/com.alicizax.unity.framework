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

        private CancellationTokenSource _cancellationTokenSource;
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
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
                if (!UIStateMachine.ValidateTransition(UILogicType.Name, UIState.Uninitialized, UIState.CreatedUI))
                    return;

                View = (UIBase)InstanceFactory.CreateInstanceOptimized(UILogicType);
                EnsureCancellationToken();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCancellationToken()
        {
            if (_cancellationTokenSource == null)
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }
        }

        public bool BeginShowOperation()
        {
            if (_showInProgress)
            {
                return false;
            }

            RequestCancelCurrentOperation();
            EnsureCancellationToken();
            _operationVersion++;
            _cancelRequested = false;
            _showInProgress = true;
            _closeInProgress = false;
            return true;
        }

        public bool BeginCloseOperation()
        {
            if (_closeInProgress)
            {
                return false;
            }

            RequestCancelCurrentOperation();
            EnsureCancellationToken();
            _operationVersion++;
            _cancelRequested = false;
            _closeInProgress = true;
            _showInProgress = false;
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
            RequestCancelCurrentOperation();
            _showInProgress = false;
            _closeInProgress = false;
        }

        public void RequestCancelCurrentOperation()
        {
            _cancelRequested = true;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
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
                throw new ArgumentNullException(nameof(uiType));
            }

            UILogicType = uiType;
            UILogicTypeName = uiType.Name;

            if (!UIMetaRegistry.TryGet(UILogicType.TypeHandle, out MetaInfo))
            {
                throw new InvalidOperationException(ZString.Format("[UI] Metadata not registered for {0}", UILogicType.FullName));
            }

            if (!UIResRegistry.TryGet(MetaInfo.HolderRuntimeTypeHandle, out ResInfo))
            {
                throw new InvalidOperationException(ZString.Format("[UI] Resource metadata not registered for holder of {0}", UILogicType.FullName));
            }

            UIHolderTypeName = Type.GetTypeFromHandle(MetaInfo.HolderRuntimeTypeHandle)?.Name;
        }
    }
}
