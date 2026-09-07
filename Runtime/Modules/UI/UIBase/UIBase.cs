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
    public abstract partial class UIBase
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

        private void DisposeResources()
        {
            if (_disposed) return;

            _canvas = null;
            _raycaster = null;
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

        internal void InternalEnterCache()
        {
            if (UIStateMachine.ValidateTransition(CachedTypeName, _state, UIState.Cached))
                SetState(UIState.Cached);
        }

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
                if (_canvas == null)
                    return;

                bool changed = !_canvas.overrideSorting || _canvas.sortingOrder != value;
                _canvas.overrideSorting = true;
                if (_canvas.sortingOrder != value)
                    _canvas.sortingOrder = value;
                if (changed)
                    SyncChildDepth();
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
            if (_eventsRegistered)
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
            if (_canvas != null && overrideSorting)
                _canvas.overrideSorting = true;

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

        internal async UniTask<bool> InternalInitlized()
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

            return true;
        }

        internal bool InternalInitlizedSync()
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

            return true;
        }

        internal bool InternalOpen()
        {
            return InternalOpenCore();
        }

        private bool InternalOpenCore()
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

            if (!CompleteOpenTransition())
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

        internal UniTask<bool> InternalClose(bool skipTransition = false)
        {
            return InternalCloseCore(skipTransition);
        }


        private async UniTask<bool> InternalCloseCore(bool skipTransition)
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

            bool closed = false;
            UniTaskCompletionSource completion = null;
            try
            {
                if (skipTransition || Holder == null || !Holder.HasTransition)
                {
                    if (skipTransition)
                        Holder?.ApplyClosedTransitionState();
                }
                else
                {
                    completion = BeginViewTransitionTracking();
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
                    closed = CompleteCloseTransition();
                CompleteViewTransitionTracking(completion);
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
            DisposeResources();
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
            DisposeResources();
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

        private bool CompleteOpenTransition()
        {
            if (_state != UIState.Opening)
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
            PauseEventListeners();
            SetState(UIState.Closed);
            Holder.OnWindowAfterClosedEvent?.Invoke();
            return true;
        }

        private void InterruptViewTransition()
        {
            Holder?.StopTransition();
            UniTaskCompletionSource completion = _viewTransitionCompletion;
            _viewTransitionCompletion = null;
            completion?.TrySetResult();
        }


        private UniTaskCompletionSource BeginViewTransitionTracking()
        {
            InterruptViewTransition();
            if (Holder == null || !Holder.HasTransition)
                return null;

            UniTaskCompletionSource completion = new UniTaskCompletionSource();
            _viewTransitionCompletion = completion;
            return completion;
        }

        private void CompleteViewTransitionTracking(UniTaskCompletionSource completion)
        {
            if (completion == null)
                return;
            if (ReferenceEquals(_viewTransitionCompletion, completion))
                _viewTransitionCompletion = null;
            completion.TrySetResult();
        }

        private void StartOpenTransitionInBackground()
        {
            if (Holder == null || !Holder.HasTransition)
                return;

            UniTaskCompletionSource completion = BeginViewTransitionTracking();
            PlayOpenTransitionInBackgroundAsync(completion).Forget();
        }

        private async UniTaskVoid PlayOpenTransitionInBackgroundAsync(UniTaskCompletionSource completion)
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
                CompleteViewTransitionTracking(completion);
            }
        }

        protected void SetState(UIState state)
        {
            _state = state;
#if UNITY_EDITOR
            _stateEnteredRealtime = Time.realtimeSinceStartup;
#endif
        }

    }
}
