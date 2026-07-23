using System;
using System.Threading;
using AlicizaX;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase : IDisposable
    {
        protected UIBase()
        {
            SetState(UIState.CreatedUI);
        }

        private bool _disposed;

        internal Canvas _canvas;

        internal GraphicRaycaster _raycaster;
        private int _lifecycleVersion;
        private UIState _openingPreviousState;
        private UIState _closingPreviousState;

        internal UIState _state = UIState.Uninitialized;
        internal UIState State => _state;
#if UNITY_EDITOR
        private float _stateEnteredRealtime;
        internal float StateDuration => Time.realtimeSinceStartup - _stateEnteredRealtime;
#endif


        private System.Object[] _userDatas;
        protected System.Object UserData => _userDatas != null && _userDatas.Length >= 1 ? _userDatas[0] : null;
        protected System.Object[] UserDatas => _userDatas;

        private RuntimeTypeHandle _runtimeTypeHandle;
        private int _uiTypeId = -1;
        private string _cachedTypeName;
        private bool _destroyHolderOnDispose = true;

        internal string CachedTypeName => _cachedTypeName ??= GetType().Name;

        internal RuntimeTypeHandle RuntimeTypeHandler
        {
            get
            {
                if (_runtimeTypeHandle.Value == IntPtr.Zero)
                {
                    _runtimeTypeHandle = GetType().TypeHandle;
                }

                return _runtimeTypeHandle;
            }
        }

        protected virtual void OnDestroy()
        {
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual UniTask OnInitializeAsync()
        {
            OnInitialize();
            return UniTask.CompletedTask;
        }

        protected virtual void OnOpen()
        {
        }

        protected virtual void OnRefresh()
        {
        }

        protected virtual void OnClose()
        {
        }

        protected virtual void OnUpdate()
        {
        }

        protected virtual void OnRegisterEvent(EventListenerProxy proxy)
        {
        }

        public void Dispose()
        {
            Dispose(true);
        }

        internal int UITypeId
        {
            get
            {
                if (_uiTypeId < 0 && UIMetaRegistry.TryGet(RuntimeTypeHandler, out UIMetaRegistry.UIMetaInfo metaInfo))
                {
                    _uiTypeId = metaInfo.TypeId;
                }

                return _uiTypeId;
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _canvas = null;
                _raycaster = null;
            }

            _userDatas = null;

            UIHolderObjectBase holder = Holder;
            if (!ReferenceEquals(holder, null))
            {
                if (_destroyHolderOnDispose && holder.IsValid())
                {
                    ResourceOwner.ReleaseBindingsInHierarchy(holder.gameObject);
                    if (Application.isPlaying)
                        Object.Destroy(holder.gameObject);
                    else
                        Object.DestroyImmediate(holder.gameObject);
                }

            }

            Holder = null;
            _disposed = true;
        }

        private bool _visible;

        // 显示层：Layer + Raycaster。缓存渲染开关见 SetCanvasEnabled
        internal bool Visible
        {
            get => _visible && _canvas != null;

            set
            {
                if (_canvas == null)
                {
                    return;
                }

                if (_visible == value)
                {
#if UNITY_EDITOR
                    SetSceneViewVisible(value);
#endif
                    return;
                }

                _visible = value;
                _canvas.gameObject.layer = value ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
                ChildVisible(value);
                Interactable = value;
#if UNITY_EDITOR
                SetSceneViewVisible(value);
#endif
            }
        }

        // 缓存路径关闭/恢复渲染；与 Visible 的 Layer 开关独立
        internal void SetCanvasEnabled(bool value)
        {
            if (_canvas != null && _canvas.enabled != value)
            {
                _canvas.enabled = value;
            }
        }

        internal void ClearUserData()
        {
            _userDatas = null;
        }

#if UNITY_EDITOR
        private void SetSceneViewVisible(bool visible)
        {
            if (_canvas == null)
            {
                return;
            }

            if (visible)
            {
                SceneVisibilityManager.instance.Show(_canvas.gameObject, true);
            }
            else
            {
                SceneVisibilityManager.instance.Hide(_canvas.gameObject, true);
            }
        }
#endif

        private bool Interactable
        {
            get => _raycaster != null && _raycaster.enabled;

            set
            {
                if (_raycaster != null && _raycaster.enabled != value)
                {
                    _raycaster.enabled = value;
                }
            }
        }

        internal int Depth
        {
            get => _canvas != null ? _canvas.sortingOrder : 0;

            set
            {
                if (_canvas != null && _canvas.sortingOrder != value)
                {
                    _canvas.sortingOrder = value;
                    SyncChildDepth();
                }
            }
        }

        #region Event

        private EventListenerProxy _eventListenerProxy;
        private bool _eventsRegistered;

        private EventListenerProxy EventListenerProxy => _eventListenerProxy ??= MemoryPool.Acquire<EventListenerProxy>();

        private void ReleaseEventListenerProxy()
        {
            if (_eventListenerProxy != null)
            {
                MemoryPool.Release(_eventListenerProxy);
                _eventListenerProxy = null;
            }

            _eventsRegistered = false;
        }

        private void RegisterEventListenersIfNeeded()
        {
            if (_eventsRegistered)
            {
                return;
            }

            OnRegisterEvent(EventListenerProxy);
            _eventsRegistered = true;
        }

        internal void PauseEventListeners()
        {
            if (!_eventsRegistered)
            {
                return;
            }

            ReleaseEventListenerProxy();
        }

        #endregion


        internal UIHolderObjectBase Holder;
        internal abstract Type UIHolderType { get; }

        internal abstract void BindUIHolder(UIHolderObjectBase holder, UIBase owner);

        internal void SetDestroyHolderOnDispose(bool value)
        {
            _destroyHolderOnDispose = value;
        }

        protected void BindHolderCommon(UIHolderObjectBase holder, bool overrideSorting, bool stretchToParent)
        {
            Holder = holder;
            _canvas = Holder.transform.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.overrideSorting = overrideSorting;
            }

            _visible = _canvas != null && _canvas.gameObject.layer == UIComponent.UIShowLayer;

            _raycaster = Holder.transform.GetComponent<GraphicRaycaster>();
            if (stretchToParent)
            {
                RectTransform rectTransform = Holder.RectTransform;
                rectTransform.localPosition = Vector3.zero;
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
                rectTransform.localScale = Vector3.one;
            }

            SetState(UIState.Loaded);
        }

        internal async UniTask<bool> InternalInitlized(UIMetadata metadata, int operationVersion)
        {
            if (!TryBeginInitialize())
                return false;

            try
            {
                await OnInitializeAsync();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Async initialize failed for {0}.", CachedTypeName);
                Log.Exception(exception);
                return false;
            }

            if (!IsInitializeStillValid(metadata, operationVersion))
                return false;

            CompleteInitialize();
            return true;
        }

        internal bool InternalInitlizedSync(UIMetadata metadata, int operationVersion)
        {
            if (!TryBeginInitialize())
                return false;

            try
            {
                OnInitialize();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Sync initialize failed for {0}.", CachedTypeName);
                Log.Exception(exception);
                return false;
            }

            if (!IsInitializeStillValid(metadata, operationVersion))
                return false;

            CompleteInitialize();
            return true;
        }

        internal async UniTask<bool> InternalOpen()
        {
            if (!TryBeginOpen(out int lifecycleVersion, out bool skippedResult))
            {
                if (skippedResult)
                {
                    InternalRefreshOpened();
                }

                return skippedResult;
            }

            if (!TryInvokeOnOpen())
            {
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Open interrupted after OnOpen", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            try
            {
                await Holder.PlayOpenTransitionAsync();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Open transition failed for {0}.", CachedTypeName);
                Log.Exception(exception);
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Open interrupted after transition", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            bool completed = CompleteOpenTransition(lifecycleVersion);
#if UNITY_EDITOR
            if (!completed)
            {
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Open completion interrupted", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening));
            }
#endif
            return completed;
        }

        internal void InternalRefreshOpened()
        {
            if (_state == UIState.Opened)
            {
                TryInvokeOnRefresh();
            }
        }

        internal void InternalRefreshAfterInitialize()
        {
            if (_state == UIState.Initialized
                || _state == UIState.Opening
                || _state == UIState.Opened
                || _state == UIState.Closing
                || _state == UIState.Closed)
            {
                TryInvokeOnRefresh();
            }
        }

        internal async UniTask<bool> InternalClose(bool skipTransition = false)
        {
            if (!TryBeginClose(out int lifecycleVersion, out bool skippedResult))
            {
                return skippedResult;
            }

            if (skipTransition)
            {
                try
                {
                    Holder.ApplyClosedTransitionState();
                }
                catch (Exception exception)
                {
                    Log.Error("[UI] Close transition state failed for {0}.", CachedTypeName);
                    Log.Exception(exception);
                    RollbackClosingState(lifecycleVersion);
                    return false;
                }
            }
            else
            {
                try
                {
                    await Holder.PlayCloseTransitionAsync();
                }
                catch (Exception exception)
                {
                    Log.Error("[UI] Close transition failed for {0}.", CachedTypeName);
                    Log.Exception(exception);
                    RollbackClosingState(lifecycleVersion);
                    return false;
                }
            }

            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close interrupted after transition", FormatLifecycleInterruption(lifecycleVersion, UIState.Closing));
#endif
                RollbackClosingState(lifecycleVersion);
                return false;
            }

            InvokeOnCloseSafely();
            bool completed = CompleteCloseTransition(lifecycleVersion);
#if UNITY_EDITOR
            if (!completed)
            {
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close completion interrupted", FormatLifecycleInterruption(lifecycleVersion, UIState.Closing));
            }
#endif
            return completed;
        }

        internal void InternalUpdate()
        {
            if (_state != UIState.Opened || !Visible) return;
            OnUpdate();
            UpdateChildren();
        }

        internal async UniTask InternalDestroy()
        {
            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Destroying))
                return;

            InterruptLifecycleTransition();
            SetState(UIState.Destroying);
            Holder?.OnWindowDestroyEvent?.Invoke();
            await DestroyAllChildren();
            OnDestroy();
            ReleaseEventListenerProxy();
            Dispose();
            SetState(UIState.Destroyed);
        }

        internal void InternalDestroyImmediate()
        {
            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Destroying))
            {
                return;
            }

            InterruptLifecycleTransition();
            SetState(UIState.Destroying);
            Holder?.OnWindowDestroyEvent?.Invoke();
            DestroyAllChildrenImmediate();
            OnDestroy();
            ReleaseEventListenerProxy();
            Dispose();
            SetState(UIState.Destroyed);
        }

        internal void RefreshParams(params System.Object[] userDatas)
        {
            this._userDatas = userDatas;
        }

        private int BeginLifecycleTransition()
        {
            InterruptLifecycleTransition();
            return _lifecycleVersion;
        }

        private bool TryBeginInitialize()
        {
            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Initialized))
                return false;

            SetState(UIState.Initialized);
            Holder.OnWindowInitEvent?.Invoke();
            return true;
        }

        private bool IsInitializeStillValid(UIMetadata metadata, int operationVersion)
        {


            return metadata != null && metadata.OperationVersion == operationVersion;
        }

        private void CompleteInitialize()
        {
            RegisterEventListenersIfNeeded();
        }

        private bool TryBeginOpen(out int lifecycleVersion, out bool skippedResult)
        {
            lifecycleVersion = 0;
            skippedResult = false;
            if (_state == UIState.Opened || _state == UIState.Opening)
            {
                skippedResult = _state == UIState.Opened;
                return false;
            }

            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Opening))
                return false;

            _openingPreviousState = _state;
            lifecycleVersion = BeginLifecycleTransition();
            SetState(UIState.Opening);
            Visible = true;
            Interactable = true;
            Holder.OnWindowBeforeShowEvent?.Invoke();
            return true;
        }

        private bool CompleteOpenTransition(int lifecycleVersion)
        {
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
                return false;

            SetState(UIState.Opened);
            RegisterEventListenersIfNeeded();
            Holder.OnWindowAfterShowEvent?.Invoke();
            return true;
        }

        private bool TryBeginClose(out int lifecycleVersion, out bool skippedResult)
        {
            lifecycleVersion = 0;
            skippedResult = false;
            if (_state == UIState.Closed || _state == UIState.Closing)
            {
                skippedResult = _state == UIState.Closed;
                return false;
            }

            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Closing))
                return false;

            _closingPreviousState = _state;
            lifecycleVersion = BeginLifecycleTransition();
            SetState(UIState.Closing);
            Interactable = false;
            Holder.OnWindowBeforeClosedEvent?.Invoke();
            return true;
        }

        private bool CompleteCloseTransition(int lifecycleVersion)
        {
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
                return false;

            Visible = false;
            SetState(UIState.Closed);
            Holder.OnWindowAfterClosedEvent?.Invoke();
            PauseEventListeners();
            return true;
        }

        private bool TryInvokeOnOpen()
        {
            try
            {
                OnOpen();
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("[UI] OnOpen failed for {0}.", CachedTypeName);
                Log.Exception(exception);
                return false;
            }
        }

        private bool TryInvokeOnRefresh()
        {
            try
            {
                OnRefresh();
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("[UI] OnRefresh failed for {0}.", CachedTypeName);
                Log.Exception(exception);
                return false;
            }
        }

        private void InvokeOnCloseSafely()
        {
            try
            {
                OnClose();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] OnClose failed for {0}.", CachedTypeName);
                Log.Exception(exception);
            }
        }

        private void InterruptLifecycleTransition()
        {
            _lifecycleVersion++;
            Holder?.StopTransition();
        }

        private bool IsCurrentLifecycleTransition(int lifecycleVersion, UIState state)
        {
            return lifecycleVersion == _lifecycleVersion && _state == state;
        }

        protected void SetState(UIState state)
        {
            _state = state;
#if UNITY_EDITOR
            _stateEnteredRealtime = Time.realtimeSinceStartup;
#endif
        }

        private void RollbackOpeningState(int lifecycleVersion)
        {
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
            {
                return;
            }

            Holder?.StopTransition();
            ApplyRollbackState(NormalizeOpeningRollbackState(_openingPreviousState));
        }

        private void RollbackClosingState(int lifecycleVersion)
        {
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
            {
                return;
            }

            Holder?.StopTransition();
            ApplyRollbackState(NormalizeClosingRollbackState(_closingPreviousState));
        }

        private static UIState NormalizeOpeningRollbackState(UIState previousState)
        {
            return previousState == UIState.Closed || previousState == UIState.Closing
                ? UIState.Closed
                : UIState.Initialized;
        }

        private static UIState NormalizeClosingRollbackState(UIState previousState)
        {
            return previousState == UIState.Opened
                ? UIState.Opened
                : UIState.Initialized;
        }

        private void ApplyRollbackState(UIState targetState)
        {
            if (targetState == UIState.Opened)
            {
                Visible = true;
                ApplyOpenTransitionStateSafely();
                Interactable = true;
                SetState(UIState.Opened);
                RegisterEventListenersIfNeeded();
                return;
            }

            ApplyClosedTransitionStateSafely();
            Visible = false;
            if (targetState == UIState.Closed)
            {
                SetState(UIState.Closed);
                PauseEventListeners();
                return;
            }

            SetState(UIState.Initialized);
        }

        private void ApplyOpenTransitionStateSafely()
        {
            try
            {
                Holder.ApplyOpenTransitionState();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Rollback open transition state failed for {0}.", CachedTypeName);
                Log.Exception(exception);
            }
        }

        private void ApplyClosedTransitionStateSafely()
        {
            try
            {
                Holder.ApplyClosedTransitionState();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Rollback closed transition state failed for {0}.", CachedTypeName);
                Log.Exception(exception);
            }
        }

#if UNITY_EDITOR
        private void WarnLifecycleOperation(string title, string reason)
        {
            if (!UIWarningSettings.OtherWarningsEnabled)
            {
                return;
            }

            Log.Warning($"[UI] {title}. Reason: {reason} UI={CachedTypeName}, State={_state}, Visible={Visible}, Depth={Depth}, LifecycleVersion={_lifecycleVersion}.");
        }

        private string FormatLifecycleInterruption(int expectedLifecycleVersion, UIState expectedState)
        {
            return $"ExpectedState={expectedState}, ActualState={_state}, ExpectedLifecycleVersion={expectedLifecycleVersion}, ActualLifecycleVersion={_lifecycleVersion}.";
        }

#endif

    }
}
