using System;
using System.Threading;
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
        internal UIService Service;
        internal UIMetadata Metadata;
        internal UIHolderObjectBase Holder;
        internal Canvas _canvas;
        internal GraphicRaycaster _raycaster;
        private UIState _state = UIState.CreatedUI;
        private int _transitionGeneration;
        private AsyncLazy<bool> _transition;
        private CancellationTokenSource _resourceLoadCancellation;
        private EventListenerProxy _eventListenerProxy;
        private EventListenerProxy _registeringEventProxy;
        private object[] _userDatas;
        private bool _visible;
        private bool _initializeInvoked;
        private bool _openInvoked;
        private bool _destroyHolderOnDispose = true;
        private bool _skipCloseTransition;
        private Hook _activeHook;

        private enum Hook : byte { None, Initialize, Open, Refresh, Close, Destroy, Update, RegisterEvent }

        internal UIState State => _state;
        internal int TransitionGeneration => _transitionGeneration;
        internal bool DestroyRequested { get; private set; }
        internal bool ChildrenCanOpen { get; private set; }
        internal bool InCloseCallback => _activeHook == Hook.Close;
        internal abstract Type UIHolderType { get; }
        internal abstract void BindUIHolder(UIHolderObjectBase holder);

        protected object UserData => _userDatas != null && _userDatas.Length > 0 ? _userDatas[0] : null;
        protected object[] UserDatas => _userDatas;
        protected virtual void OnInitialize() { }
        protected virtual void OnOpen() { }
        protected virtual void OnRefresh() { }
        protected virtual void OnClose() { }
        protected virtual void OnDestroy() { }
        protected virtual void OnUpdate() { }
        protected virtual void OnRegisterEvent(EventListenerProxy proxy) { }
        internal virtual void OnFrameworkClosed() { }
        internal virtual void OnFrameworkDestroyed() { }

        internal CancellationTokenSource BeginResourceLoad() =>
            _resourceLoadCancellation = new CancellationTokenSource();

        internal void EndResourceLoad(CancellationTokenSource cancellation)
        {
            _resourceLoadCancellation = null;
            cancellation.Dispose();
        }

        internal void CancelResourceLoad()
        {
            CancellationTokenSource cancellation = _resourceLoadCancellation;
            _resourceLoadCancellation = null;
            if (cancellation != null) CancelLoad(cancellation);
        }

        internal static void CancelLoad(CancellationTokenSource cancellation)
        {
            try { cancellation.Cancel(); }
            catch (Exception error) { Log.Exception(error); }
        }

        internal bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (Holder == null || !Holder.IsValid()) return;
                Holder.gameObject.layer = value ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
                SetInteractable(value);
#if UNITY_EDITOR
                if (Application.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (value) SceneVisibilityManager.instance.Show(Holder.gameObject, true);
                    else SceneVisibilityManager.instance.Hide(Holder.gameObject, true);
                }
#endif
            }
        }

        internal int Depth
        {
            get => _canvas != null ? _canvas.sortingOrder : 0;
            set
            {
                if (_canvas == null) return;
                _canvas.overrideSorting = true;
                _canvas.sortingOrder = value;
            }
        }

        internal void SetCanvasEnabled(bool value)
        {
            if (_canvas != null) _canvas.enabled = value;
        }

        internal void InternalEnterCache() => _state = UIState.Cached;
        internal void ClearUserData() => _userDatas = null;
        internal void SetDestroyHolderOnDispose(bool value) => _destroyHolderOnDispose = value;

        internal void RefreshParams(object[] userDatas)
        {
            if (userDatas != null && userDatas.Length > 0) _userDatas = userDatas;
        }

        protected void SetTransition(IUITransitionSource source) => Holder.SetTransition(source);
        protected void SetTransition(Func<bool, CancellationToken, UniTask> play, Action<bool> snap) =>
            Holder.SetTransition(play, snap);

        protected void BindHolderCommon(UIHolderObjectBase holder, bool overrideSorting, bool stretchToParent)
        {
            holder.BindOwner(this);
            Holder = holder;
            _canvas = holder.GetComponent<Canvas>();
            if (_canvas != null) _canvas.overrideSorting = overrideSorting;
            _visible = holder.gameObject.layer == UIComponent.UIShowLayer;
            _raycaster = holder.GetComponent<GraphicRaycaster>();
            if (stretchToParent)
            {
                RectTransform rect = holder.RectTransform;
                rect.localPosition = Vector3.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
            _state = UIState.Loaded;
        }

        internal bool InternalInitialize()
        {
            int generation = _transitionGeneration;
            _state = UIState.Initialized;
            Visible = false;
            Holder.InvokeWindowInit();
            if (!IsCurrent(generation) || State != UIState.Initialized) return false;
            _initializeInvoked = true;
            InvokeHook(Hook.Initialize);
            return IsCurrent(generation) && State == UIState.Initialized;
        }

        internal void ValidateOpen()
        {
            if (InCloseCallback)
                throw new InvalidOperationException("Cannot open a panel while its close transition is running.");
        }

        internal bool InternalOpen(bool skipTransition = false)
        {
            ValidateOpen();
            if (DestroyRequested) return false;
            if (State == UIState.Opening) return false;
            if (State == UIState.Opened)
            {
                // Refresh does not replace the current visual transition.
                int refreshGeneration = _transitionGeneration;
                InvokeHook(Hook.Refresh);
                return IsCurrent(refreshGeneration) && State == UIState.Opened && !DestroyRequested;
            }
            if (State != UIState.Initialized && State != UIState.Closed &&
                State != UIState.Cached && State != UIState.Closing) return false;

            int generation = ++_transitionGeneration;
            _state = UIState.Opening;
            ChildrenCanOpen = false;
            var operation = new AsyncLazy<bool>(() => FinishTransition(OpenView(generation, skipTransition)));
            _transition = operation;
            operation.Task.Forget();
            return IsCurrent(generation) && !DestroyRequested && (State == UIState.Opened || State == UIState.Opening);
        }

        private async UniTask<bool> OpenView(int generation, bool skipTransition)
        {
            Holder.StopTransition();
            if (!IsCurrent(generation)) return false;
            Visible = true;
            Holder.InvokeWindowBeforeShow();
            if (!IsCurrent(generation)) return false;
            _openInvoked = true;
            InvokeHook(Hook.Open);
            if (!IsCurrent(generation)) return false;
            RegisterEventListeners();
            if (!IsCurrent(generation)) return false;
            Service.SetUpdating(this, Metadata.MetaInfo.HasUpdate);
            Holder.InvokeWindowAfterShow();
            if (!IsCurrent(generation)) return false;
            _state = UIState.Opened;

            if (skipTransition) Holder.ApplyTransitionState(true);
            else await Holder.PlayOpenTransitionAsync();
            if (!IsCurrent(generation)) return false;
            ChildrenCanOpen = true;
            await OpenChildren(generation);
            return IsCurrent(generation);
        }

        internal UniTask<bool> InternalClose(bool skipTransition = false, bool destroy = false)
        {
            if (State == UIState.Destroyed) return UniTask.FromResult(true);
            if (State == UIState.Destroying) return CurrentTransition();
            bool beginDestroy = destroy && !DestroyRequested;
            DestroyRequested |= destroy;
            if (State == UIState.Closing)
            {
                bool skip = skipTransition && !_skipCloseTransition;
                _skipCloseTransition |= skipTransition;
                if (beginDestroy) CancelResourceLoad();
                if (beginDestroy || skip) CloseChildren(DestroyRequested, _skipCloseTransition).Forget();
                if (skip) Holder?.StopTransition();
                return CurrentTransition();
            }
            if ((State == UIState.Closed || State == UIState.Cached) && !destroy)
                return UniTask.FromResult(true);

            int generation = ++_transitionGeneration;
            _skipCloseTransition = skipTransition;
            _state = UIState.Closing;
            ChildrenCanOpen = false;
            var operation = new AsyncLazy<bool>(() => FinishTransition(CloseView(generation)));
            _transition = operation;
            return operation.Task;
        }

        private async UniTask<bool> CloseView(int generation)
        {
            if (DestroyRequested) CancelResourceLoad();
            if (!IsCurrent(generation)) return State == UIState.Destroyed;
            Holder?.StopTransition();
            if (!IsCurrent(generation)) return false;
            SetInteractable(false);
            Service.SetUpdating(this, false);
            ReleaseEventListeners();
            UniTask children = CloseChildren(DestroyRequested, _skipCloseTransition);
            if (!IsCurrent(generation)) return false;
            if (_openInvoked)
            {
                _openInvoked = false;
                Holder.InvokeWindowBeforeClosed();
                if (!IsCurrent(generation)) return false;
                InvokeHook(Hook.Close);
                if (!IsCurrent(generation)) return false;
            }
            await children;
            if (!IsCurrent(generation)) return false;
            if (Holder != null && Holder.IsValid())
            {
                if (!_skipCloseTransition) await Holder.PlayCloseTransitionAsync();
                if (!IsCurrent(generation)) return false;
                if (_skipCloseTransition) Holder.ApplyTransitionState(false);
            }
            if (!IsCurrent(generation)) return false;
            Visible = false;
            _state = UIState.Closed;
            UIHolderObjectBase holder = Holder;
            if (DestroyRequested) DestroyNow();
            else OnFrameworkClosed();
            if (State != UIState.Destroyed && !IsCurrent(generation)) return false;
            if (State != UIState.Closed && State != UIState.Cached && State != UIState.Destroyed) return false;
            holder?.InvokeWindowAfterClosed();
            return State == UIState.Destroyed || (IsCurrent(generation) && (State == UIState.Closed || State == UIState.Cached));
        }

        public UniTask AwaitTransition() => CurrentTransition().AsUniTask();
        private UniTask<bool> CurrentTransition() => _transition?.Task ?? UniTask.FromResult(true);

        private async UniTask<bool> FinishTransition(UniTask<bool> operation)
        {
            bool completed = await operation;
            // Cancelling a visual transition can resume it inside synchronous destruction.
            return State == UIState.Destroying ? await CurrentTransition() : completed;
        }

        internal void InternalUpdate()
        {
            if (State == UIState.Opened && Visible && !DestroyRequested) InvokeHook(Hook.Update);
        }

        internal UniTask<bool> InternalDestroy(bool skipTransition = false) =>
            InternalClose(skipTransition, destroy: true);

        internal void DestroyNow()
        {
            if (State == UIState.Destroyed || State == UIState.Destroying) return;
            ++_transitionGeneration;
            DestroyRequested = true;
            _state = UIState.Destroying;
            ChildrenCanOpen = false;
            var operation = new AsyncLazy<bool>(DisposeView);
            _transition = operation;
            operation.Task.Forget();
        }

        private UniTask<bool> DisposeView()
        {
            Holder?.StopTransition();
            CancelResourceLoad();
            Service.SetUpdating(this, false);
            DestroyChildrenImmediate();
            if (_openInvoked)
            {
                _openInvoked = false;
                InvokeHook(Hook.Close);
            }
            Holder?.InvokeWindowDestroy();
            if (_initializeInvoked) InvokeHook(Hook.Destroy);
            ReleaseEventListeners();
            try { DisposeResources(); }
            catch (Exception error) { Log.Exception(error); }
            Metadata = null;
            _visible = false;
            _state = UIState.Destroyed;
            try { OnFrameworkDestroyed(); }
            catch (Exception error) { Log.Exception(error); }
            return UniTask.FromResult(true);
        }

        private void InvokeHook(Hook hook)
        {
            Hook previous = _activeHook;
            _activeHook = hook;
            try
            {
                switch (hook)
                {
                    case Hook.Initialize: OnInitialize(); break;
                    case Hook.Open: OnOpen(); break;
                    case Hook.Refresh: OnRefresh(); break;
                    case Hook.Close: OnClose(); break;
                    case Hook.Destroy: OnDestroy(); break;
                    case Hook.Update: OnUpdate(); break;
                    case Hook.RegisterEvent: OnRegisterEvent(_eventListenerProxy); break;
                }
            }
            catch (Exception error) { Log.Exception(error); }
            finally { _activeHook = previous; }
        }

        private bool IsCurrent(int generation) =>
            generation == _transitionGeneration && State != UIState.Destroying && State != UIState.Destroyed;

        private void SetInteractable(bool value)
        {
            if (_raycaster != null) _raycaster.enabled = value;
        }

        private void RegisterEventListeners()
        {
            if (_eventListenerProxy != null) return;
            EventListenerProxy proxy = MemoryPool.Acquire<EventListenerProxy>();
            _eventListenerProxy = proxy;
            EventListenerProxy previous = _registeringEventProxy;
            _registeringEventProxy = proxy;
            InvokeHook(Hook.RegisterEvent);
            _registeringEventProxy = previous;
            if (_eventListenerProxy == proxy) return;
            try { MemoryPool.Release(proxy); }
            catch (Exception error) { Log.Exception(error); }
        }

        private void ReleaseEventListeners()
        {
            EventListenerProxy proxy = _eventListenerProxy;
            _eventListenerProxy = null;
            if (proxy == null) return;
            try
            {
                // The registration callback may continue using this proxy after closing its UI.
                if (_registeringEventProxy == proxy) proxy.Clear();
                else MemoryPool.Release(proxy);
            }
            catch (Exception error) { Log.Exception(error); }
        }

        private void DisposeResources()
        {
            UIHolderObjectBase holder = Holder;
            Holder = null;
            _canvas = null;
            _raycaster = null;
            _userDatas = null;
            if (holder == null) return;
            holder.UnbindOwner();
            if (!_destroyHolderOnDispose || !holder.IsValid()) return;
            if (Application.isPlaying) Object.Destroy(holder.gameObject);
            else Object.DestroyImmediate(holder.gameObject);
        }
    }
}
