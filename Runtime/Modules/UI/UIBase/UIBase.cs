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
        private UniTaskCompletionSource _viewTransitionCompletion;

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

        internal bool Visible
        {
            get => _visible;

            set
            {
                if (_visible == value)
                {
#if UNITY_EDITOR
                    SetSceneViewVisible(value);
#endif
                    return;
                }

                _visible = value;
                ApplyHolderLayer(value);
                ChildVisible(value);
                Interactable = value;
#if UNITY_EDITOR
                SetSceneViewVisible(value);
#endif
            }
        }

        private void ApplyHolderLayer(bool visible)
        {
            if (Holder == null || !Holder.IsValid())
            {
                return;
            }

            int layer = visible ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
            GameObject go = Holder.gameObject;
            if (go.layer != layer)
            {
                go.layer = layer;
            }
        }

        internal void SetCanvasEnabled(bool value)
        {
            if (_canvas != null && _canvas.enabled != value)
            {
                _canvas.enabled = value;
            }
        }

        internal void EnterCacheVisual() => SetCanvasEnabled(false);
        internal void ExitCacheVisual() => SetCanvasEnabled(true);

        internal void ClearUserData()
        {
            _userDatas = null;
        }

#if UNITY_EDITOR
        private void SetSceneViewVisible(bool visible)
        {
            if (Holder == null || !Holder.IsValid() || !Application.isPlaying || !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (visible)
            {
                SceneVisibilityManager.instance.Show(Holder.gameObject, true);
            }
            else
            {
                SceneVisibilityManager.instance.Hide(Holder.gameObject, true);
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

        protected void SetTransition(IUITransitionSource source)
        {
            Holder?.SetTransition(source);
        }

        protected void SetTransition(Func<bool, CancellationToken, UniTask> play, Action<bool> snap)
        {
            Holder?.SetTransition(play, snap);
        }

        protected void BindHolderCommon(UIHolderObjectBase holder, bool overrideSorting, bool stretchToParent)
        {
            Holder = holder;
            _canvas = Holder.transform.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.overrideSorting = overrideSorting;
            }

            _visible = Holder.gameObject.layer == UIComponent.UIShowLayer;

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

        internal bool InternalOpen()
        {
            return InternalOpenCore(null, -1);
        }

        internal bool InternalOpen(UIMetadata metadata, int expectedOperationVersion)
        {
            return InternalOpenCore(metadata, expectedOperationVersion);
        }

        private bool InternalOpenCore(UIMetadata metadata, int expectedOperationVersion)
        {
            if (!TryBeginOpen(out bool skippedResult))
            {
                if (skippedResult)
                {
                    InternalRefreshOpened();
                }

                return skippedResult;
            }

            OnOpen();

            if (!IsStillInState(UIState.Opening, metadata, expectedOperationVersion))
            {
                RollbackOpeningState();
                return false;
            }

            if (!CompleteOpenTransition(metadata, expectedOperationVersion))
            {
                return false;
            }

            StartOpenTransitionInBackground();
            return true;
        }

        internal void InternalRefreshOpened()
        {
            if (_state == UIState.Opened)
            {
                OnRefresh();
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
                OnRefresh();
            }
        }

        internal UniTask<bool> InternalClose(bool skipTransition = false)
        {
            return InternalCloseCore(skipTransition, null, -1);
        }

        internal UniTask<bool> InternalClose(UIMetadata metadata, int expectedOperationVersion, bool skipTransition = false)
        {
            return InternalCloseCore(skipTransition, metadata, expectedOperationVersion);
        }


        private async UniTask<bool> InternalCloseCore(bool skipTransition, UIMetadata metadata, int expectedOperationVersion)
        {
            if (_state == UIState.Closed)
            {
                return true;
            }

            if (_state == UIState.Closing)
            {
                await AwaitViewTransition();
                return _state == UIState.Closed;
            }

            if (!TryBeginClose(out bool skippedResult))
            {
                return skippedResult;
            }

            OnClose();

            if (!IsStillInState(UIState.Closing, metadata, expectedOperationVersion))
            {
                return false;
            }

            BeginViewTransitionTracking();
            bool closed = false;
            try
            {
                if (skipTransition)
                {
                    Holder?.ApplyClosedTransitionState();
                }
                else if (Holder != null)
                {
                    await Holder.PlayCloseTransitionAsync();
                }
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Close transition failed for {0}.", CachedTypeName);
                Log.Exception(exception);
                Holder?.StopTransition();
                try { Holder?.ApplyClosedTransitionState(); }
                catch (Exception applyException) { Log.Exception(applyException); }
            }
            finally
            {
                if (_state == UIState.Closing)
                {
                    closed = CompleteCloseTransition();
                }

                CompleteViewTransitionTracking();
            }

            return closed;
        }

        public UniTask AwaitViewTransition()
        {
            return _viewTransitionCompletion != null
                ? _viewTransitionCompletion.Task
                : UniTask.CompletedTask;
        }

        internal void InternalUpdate()
        {
            if (_state != UIState.Opened || !_visible) return;
            OnUpdate();
            UpdateChildren();
        }

        internal async UniTask InternalDestroy()
        {
            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Destroying))
                return;

            InterruptViewTransition();
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

            InterruptViewTransition();
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
            return metadata != null && metadata.IsOperationCurrent(operationVersion);
        }

        private void CompleteInitialize()
        {
            RegisterEventListenersIfNeeded();
        }

        private bool TryBeginOpen(out bool skippedResult)
        {
            skippedResult = false;
            if (_state == UIState.Opened || _state == UIState.Opening)
            {
                skippedResult = _state == UIState.Opened;
                return false;
            }

            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Opening))
                return false;

            InterruptViewTransition();
            SetState(UIState.Opening);
            Visible = true;
            Interactable = true;
            Holder.OnWindowBeforeShowEvent?.Invoke();
            return true;
        }

        private bool CompleteOpenTransition(UIMetadata metadata, int expectedOperationVersion)
        {
            if (!IsStillInState(UIState.Opening, metadata, expectedOperationVersion))
                return false;

            SetState(UIState.Opened);
            RegisterEventListenersIfNeeded();
            Holder.OnWindowAfterShowEvent?.Invoke();
            return true;
        }

        private bool TryBeginClose(out bool skippedResult)
        {
            skippedResult = false;
            if (_state == UIState.Closed || _state == UIState.Closing)
            {
                skippedResult = _state == UIState.Closed;
                return false;
            }

            if (!UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Closing))
                return false;

            InterruptViewTransition();
            SetState(UIState.Closing);
            Interactable = false;
            Holder.OnWindowBeforeClosedEvent?.Invoke();
            return true;
        }

        private bool CompleteCloseTransition()
        {
            if (_state != UIState.Closing)
                return false;

            Visible = false;
            SetState(UIState.Closed);
            Holder.OnWindowAfterClosedEvent?.Invoke();
            PauseEventListeners();
            return true;
        }

        private void InterruptViewTransition()
        {
            Holder?.StopTransition();
            CompleteViewTransitionTracking();
        }


        private bool IsStillInState(UIState state, UIMetadata metadata, int expectedOperationVersion)
        {
            if (_state != state)
            {
                return false;
            }

            if (expectedOperationVersion < 0 || metadata == null)
            {
                return true;
            }

            return metadata.IsOperationCurrent(expectedOperationVersion);
        }

        private void BeginViewTransitionTracking()
        {
            CompleteViewTransitionTracking();
            _viewTransitionCompletion = new UniTaskCompletionSource();
        }

        private void CompleteViewTransitionTracking()
        {
            UniTaskCompletionSource completion = _viewTransitionCompletion;
            _viewTransitionCompletion = null;
            completion?.TrySetResult();
        }

        private void StartOpenTransitionInBackground()
        {
            BeginViewTransitionTracking();
            PlayOpenTransitionInBackgroundAsync().Forget();
        }

        private async UniTaskVoid PlayOpenTransitionInBackgroundAsync()
        {
            try
            {
                if (Holder != null && Holder.IsValid())
                {
                    await Holder.PlayOpenTransitionAsync();
                }
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Open transition failed for {0}.", CachedTypeName);
                Log.Exception(exception);
            }
            finally
            {
                CompleteViewTransitionTracking();
            }
        }

        protected void SetState(UIState state)
        {
            _state = state;
#if UNITY_EDITOR
            _stateEnteredRealtime = Time.realtimeSinceStartup;
#endif
        }

        private void RollbackOpeningState()
        {
            if (_state != UIState.Opening)
            {
                return;
            }

            InterruptViewTransition();
            try
            {
                Holder?.ApplyClosedTransitionState();
            }
            catch (Exception exception)
            {
                Log.Error("[UI] Rollback opening state failed for {0}.", CachedTypeName);
                Log.Exception(exception);
            }

            Visible = false;
            Interactable = false;
            SetState(UIState.Initialized);
            PauseEventListeners();
        }
    }
}
