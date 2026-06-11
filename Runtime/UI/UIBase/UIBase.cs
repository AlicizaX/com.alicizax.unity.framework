using System;
using System.Collections.Generic;
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

        internal UIState _state = UIState.Uninitialized;
        internal UIState State => _state;
#if UNITY_EDITOR
        private int _occlusionInterruptedLifecycleVersion = -1;
        private float _stateEnteredRealtime;
        internal float StateDuration => Time.realtimeSinceStartup - _stateEnteredRealtime;
#endif


        private System.Object[] _userDatas;
        protected System.Object UserData => _userDatas != null && _userDatas.Length >= 1 ? _userDatas[0] : null;
        protected System.Object[] UserDatas => _userDatas;

        private RuntimeTypeHandle _runtimeTypeHandle;
        private int _uiTypeId = -1;
        private string _cachedTypeName;

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

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDestroy()
        {
        }

        protected virtual void OnOpen()
        {
        }

        protected virtual void OnClose()
        {
        }

        protected virtual void OnUpdate()
        {
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnInitialize不会调用
        /// </summary>
        protected virtual UniTask OnInitializeAsync()
        {
            OnInitialize();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 带取消令牌版本。令牌在显示/关闭操作被新操作打断时取消。
        /// 默认转发到无参版本，保持旧子类兼容。
        /// </summary>
        protected virtual UniTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            return OnInitializeAsync();
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnOpen不会调用
        /// </summary>
        protected virtual UniTask OnOpenAsync()
        {
            OnOpen();
            return UniTask.CompletedTask;
        }

        protected virtual UniTask OnOpenAsync(CancellationToken cancellationToken)
        {
            return OnOpenAsync();
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnClose不会调用
        /// </summary>
        protected virtual UniTask OnCloseAsync()
        {
            OnClose();
            return UniTask.CompletedTask;
        }

        protected virtual UniTask OnCloseAsync(CancellationToken cancellationToken)
        {
            return OnCloseAsync();
        }

        /// <summary>
        /// 事件在窗口销毁后会自动移除
        /// </summary>
        /// <param name="proxy"></param>
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
                // 托管资源释放
                _canvas = null;
                _raycaster = null;
            }

            _userDatas = null;

            // 非托管资源释放
            if (Holder != null)
            {
                ResourceOwner.ReleaseBindingsInHierarchy(Holder.gameObject);
                if (Application.isPlaying)
                    Object.Destroy(Holder.gameObject);
                else
                    Object.DestroyImmediate(Holder.gameObject);

                Holder = null;
            }

            _disposed = true;
        }

        private bool _visible;

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

        /// <summary>
        /// 窗口深度值。
        /// </summary>
        internal int Depth
        {
            get => _canvas != null ? _canvas.sortingOrder : 0;

            set
            {
                if (_canvas != null && _canvas.sortingOrder != value)
                {
                    // 设置父类
                    _canvas.sortingOrder = value;
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

        #region 管理器内部调用

        internal UIHolderObjectBase Holder;
        internal abstract Type UIHolderType { get; }

        internal abstract void BindUIHolder(UIHolderObjectBase holder, UIBase owner);

        protected void BindHolderCommon(UIHolderObjectBase holder, bool overrideSorting, bool stretchToParent)
        {
            Holder = holder;
            _canvas = Holder.transform.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.overrideSorting = overrideSorting;
            }

            // 与预制体实际层保持同步，避免缓存的可见标记与真实状态脱节
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

        internal async UniTask<bool> InternalInitlized(CancellationToken cancellationToken, UIMetadata metadata, int operationVersion)
        {
            if (!TryBeginInitialize())
                return false;

            await OnInitializeAsync(cancellationToken);
            if (!IsInitializeStillValid(cancellationToken, metadata, operationVersion))
                return false;

            CompleteInitialize();
            return true;
        }

        internal bool InternalInitlizedSync()
        {
            if (!TryBeginInitialize())
                return false;

            OnInitialize();
            CompleteInitialize();
            return true;
        }

        internal async UniTask<bool> InternalOpen(CancellationToken cancellationToken = default, bool causedByOcclusion = false)
        {
            if (!TryBeginOpen(out int lifecycleVersion, out bool skippedResult, causedByOcclusion))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Open skipped", $"TryBeginOpen returned false. SkippedResult={skippedResult}.");
#endif
                return skippedResult;
            }

            UniTask<bool> openTransitionTask = Holder.PlayOpenTransitionAsync(cancellationToken).SuppressCancellationThrow();

            try
            {
                await OnOpenAsync(cancellationToken);
            }
            catch
            {
                Holder.StopTransition();
                throw;
            }

            if (IsOpeningCanceled(lifecycleVersion, cancellationToken))
            {
#if UNITY_EDITOR
                if (ShouldWarnOpenInterruption(lifecycleVersion, cancellationToken)) WarnLifecycleOperation("Open interrupted after OnOpenAsync", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening, cancellationToken), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            bool openCanceled = await openTransitionTask;
            if (openCanceled || cancellationToken.IsCancellationRequested)
            {
#if UNITY_EDITOR
                if (ShouldWarnOpenInterruption(lifecycleVersion, cancellationToken)) WarnLifecycleOperation("Open transition interrupted", $"Open transition canceled={openCanceled}, tokenCanceled={cancellationToken.IsCancellationRequested}. {FormatLifecycleInterruption(lifecycleVersion, UIState.Opening, cancellationToken)}", IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            bool completed = CompleteOpenTransition(lifecycleVersion);
#if UNITY_EDITOR
            if (!completed)
            {
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Open completion interrupted", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening, cancellationToken), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
            }
#endif
            return completed;
        }

        internal bool InternalOpenSync(bool causedByOcclusion = false)
        {
            if (!TryBeginOpen(out int lifecycleVersion, out bool skippedResult, causedByOcclusion))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("OpenSync skipped", $"TryBeginOpen returned false. SkippedResult={skippedResult}.");
#endif
                return skippedResult;
            }

            OnOpen();
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("OpenSync interrupted after OnOpen", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening, CancellationToken.None), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                return false;
            }

            FireAndForgetOpenTransition(lifecycleVersion).Forget();
            return true;
        }

        internal async UniTask<bool> InternalClose(CancellationToken cancellationToken = default, bool causedByOcclusion = false, bool skipTransition = false)
        {
            if (!TryBeginClose(out int lifecycleVersion, out bool skippedResult, causedByOcclusion))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close skipped", $"TryBeginClose returned false. SkippedResult={skippedResult}.");
#endif
                return skippedResult;
            }

            if (skipTransition)
            {
                // 跳过关闭动画：窗口已不可见（被全屏窗口遮挡或正在销毁），直接进入 Closed 透明层有需要则自己Fork框架到本地改就行
                Holder.ApplyClosedTransitionState();
            }
            else
            {
                bool closeCanceled = await Holder.PlayCloseTransitionAsync(cancellationToken).SuppressCancellationThrow();
                if (closeCanceled || cancellationToken.IsCancellationRequested)
                {
#if UNITY_EDITOR
                    if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close transition interrupted", $"Close transition canceled={closeCanceled}, tokenCanceled={cancellationToken.IsCancellationRequested}. {FormatLifecycleInterruption(lifecycleVersion, UIState.Closing, cancellationToken)}", IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Closing));
#endif
                    return false;
                }
            }

            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing) || cancellationToken.IsCancellationRequested)
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close interrupted after transition", FormatLifecycleInterruption(lifecycleVersion, UIState.Closing, cancellationToken), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Closing));
#endif
                return false;
            }

            await OnCloseAsync(cancellationToken);
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing) || cancellationToken.IsCancellationRequested)
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close interrupted after OnCloseAsync", FormatLifecycleInterruption(lifecycleVersion, UIState.Closing, cancellationToken), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Closing));
#endif
                return false;
            }

            bool completed = CompleteCloseTransition(lifecycleVersion);
#if UNITY_EDITOR
            if (!completed)
            {
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Close completion interrupted", FormatLifecycleInterruption(lifecycleVersion, UIState.Closing, cancellationToken), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Closing));
            }
#endif
            return completed;
        }

        internal bool InternalCloseSync(bool causedByOcclusion = false, bool skipTransition = false)
        {
            if (!TryBeginClose(out int lifecycleVersion, out bool skippedResult, causedByOcclusion))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("CloseSync skipped", $"TryBeginClose returned false. SkippedResult={skippedResult}.");
#endif
                return skippedResult;
            }

            OnClose();
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("CloseSync interrupted after OnClose", FormatLifecycleInterruption(lifecycleVersion, UIState.Closing, CancellationToken.None), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Closing));
#endif
                return false;
            }

            if (skipTransition)
            {
                Holder.ApplyClosedTransitionState();
                return CompleteCloseTransition(lifecycleVersion);
            }

            FireAndForgetCloseTransition(lifecycleVersion).Forget();
            return true;
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

        private int BeginLifecycleTransition(bool causedByOcclusion = false)
        {
            InterruptLifecycleTransition(causedByOcclusion);
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

        private bool IsInitializeStillValid(CancellationToken cancellationToken, UIMetadata metadata, int operationVersion)
        {
            return !cancellationToken.IsCancellationRequested
                   && (metadata == null || metadata.OperationVersion == operationVersion);
        }

        private void CompleteInitialize()
        {
            RegisterEventListenersIfNeeded();
        }

        private bool TryBeginOpen(out int lifecycleVersion, out bool skippedResult, bool causedByOcclusion = false)
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

            lifecycleVersion = BeginLifecycleTransition(causedByOcclusion);
            SetState(UIState.Opening);
            Visible = true;
            Interactable = true;
            Holder.OnWindowBeforeShowEvent?.Invoke();
            return true;
        }

        private bool IsOpeningCanceled(int lifecycleVersion, CancellationToken cancellationToken)
        {
            return !IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening)
                   || cancellationToken.IsCancellationRequested;
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

        private bool TryBeginClose(out int lifecycleVersion, out bool skippedResult, bool causedByOcclusion = false)
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

            lifecycleVersion = BeginLifecycleTransition(causedByOcclusion);
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

        private void InterruptLifecycleTransition(bool causedByOcclusion = false)
        {
            _lifecycleVersion++;
#if UNITY_EDITOR
            _occlusionInterruptedLifecycleVersion = causedByOcclusion ? _lifecycleVersion - 1 : -1;
#endif
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
#if UNITY_EDITOR
                if (ShouldWarnOpenRollbackSkipped(lifecycleVersion)) WarnLifecycleOperation("Open rollback skipped", FormatLifecycleInterruption(lifecycleVersion, UIState.Opening, CancellationToken.None), IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                return;
            }

            Holder?.StopTransition();
            Visible = false;
            SetState(UIState.Initialized);
        }

        private async UniTaskVoid FireAndForgetOpenTransition(int lifecycleVersion)
        {
            bool canceled = await Holder.PlayOpenTransitionAsync().SuppressCancellationThrow();
            if (canceled || !IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Fire-and-forget open transition interrupted", $"Transition canceled={canceled}. {FormatLifecycleInterruption(lifecycleVersion, UIState.Opening, CancellationToken.None)}", IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
#endif
                return;
            }

            CompleteOpenTransition(lifecycleVersion);
        }

        private async UniTaskVoid FireAndForgetCloseTransition(int lifecycleVersion)
        {
            bool canceled = await Holder.PlayCloseTransitionAsync().SuppressCancellationThrow();
            if (canceled || !IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
            {
#if UNITY_EDITOR
                if (UIWarningSettings.AnyWarningsEnabled) WarnLifecycleOperation("Fire-and-forget close transition interrupted", $"Transition canceled={canceled}. {FormatLifecycleInterruption(lifecycleVersion, UIState.Closing, CancellationToken.None)}", IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Closing));
#endif
                return;
            }

            CompleteCloseTransition(lifecycleVersion);
        }

#if UNITY_EDITOR
        private void WarnLifecycleOperation(string title, string reason, bool occlusionWarning = false)
        {
            if (occlusionWarning)
            {
                if (!UIWarningSettings.OcclusionWarningsEnabled)
                {
                    return;
                }
            }
            else if (!UIWarningSettings.OtherWarningsEnabled)
            {
                return;
            }

            Log.Warning($"[UI] {title}. Reason: {reason} UI={CachedTypeName}, State={_state}, Visible={Visible}, Depth={Depth}, LifecycleVersion={_lifecycleVersion}.");
        }

        private string FormatLifecycleInterruption(int expectedLifecycleVersion, UIState expectedState, CancellationToken cancellationToken)
        {
            return $"ExpectedState={expectedState}, ActualState={_state}, ExpectedLifecycleVersion={expectedLifecycleVersion}, ActualLifecycleVersion={_lifecycleVersion}, TokenCanceled={cancellationToken.IsCancellationRequested}.";
        }

        private bool IsOcclusionLifecycleInterruption(int expectedLifecycleVersion, UIState expectedState)
        {
            if (_occlusionInterruptedLifecycleVersion != expectedLifecycleVersion || expectedLifecycleVersion == _lifecycleVersion)
            {
                return false;
            }

            return expectedState == UIState.Opening && (_state == UIState.Closing || _state == UIState.Closed)
                   || expectedState == UIState.Closing && (_state == UIState.Opening || _state == UIState.Opened);
        }

        internal bool WasLifecycleInterruptedByOcclusion(int lifecycleVersion)
        {
            return _occlusionInterruptedLifecycleVersion == lifecycleVersion;
        }

        private bool ShouldWarnOpenInterruption(int lifecycleVersion, CancellationToken cancellationToken)
        {
            return UIWarningSettings.AnyWarningsEnabled
                   && (!cancellationToken.IsCancellationRequested || IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening));
        }

        private bool ShouldWarnOpenRollbackSkipped(int lifecycleVersion)
        {
            return UIWarningSettings.AnyWarningsEnabled && IsOcclusionLifecycleInterruption(lifecycleVersion, UIState.Opening);
        }
#endif

        #endregion
    }
}
