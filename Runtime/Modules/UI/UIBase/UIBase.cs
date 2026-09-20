using System;
using System.Threading;
using Cysharp.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        internal UIService Service;
        internal UIMetadata Metadata;
        internal UIHolderObjectBase Holder;
        private UIState _state = UIState.CreatedUI;
        private int _transitionGeneration;
        private UniTaskCompletionSource _transition;
        private UniTaskCompletionSource<bool> _logicalOpen;
        private EventListenerProxy _eventListenerProxy;
        private object[] _userDatas;
        private bool _visible;
        private bool _initializeInvoked;
        private bool _openInvoked;
        private bool _destroyHolderOnDispose = true;
        private bool _pendingRefresh;
        private bool _pendingOpen;
        private Hook _activeHook;

        private enum Hook : byte
        {
            None, Initialize, Open, Refresh, Close, Destroy, Update, RegisterEvent,
            HolderInit, BeforeShow, AfterShow, BeforeClose, AfterClose, HolderDestroy,
        }

        internal UIState State => _state;
        internal bool DestroyRequested { get; private set; }
        internal bool Effective { get; private set; }
        internal virtual bool ParentEffective => true;
        public bool IsOpen { get; private set; }
        public virtual bool IsVisible => _visible;
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
        internal virtual void OnDestroyStarted() { }
        internal virtual void OnDestroyed() { }
        internal virtual void OnClosed() { }
        internal virtual void OnActivityChanged() { }

        internal bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (Holder == null || !Holder.IsValid()) return;
                ApplyVisible(value);
#if UNITY_EDITOR
                if (Application.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (value) SceneVisibilityManager.instance.Show(Holder.gameObject, true);
                    else SceneVisibilityManager.instance.Hide(Holder.gameObject, true);
                }
#endif
            }
        }

        private protected abstract void ApplyVisible(bool value);
        private protected virtual void SetInteractable(bool value) { }
        private protected virtual void ReleaseVisuals() { }

        internal void ClearUserData() => _userDatas = null;
        internal void SetDestroyHolderOnDispose(bool value) => _destroyHolderOnDispose = value;

        internal void RefreshParams(object[] userDatas)
        {
            if (userDatas != null && userDatas.Length > 0) _userDatas = userDatas;
        }

        internal UniTask<bool> AwaitLogicalOpen() => IsOpen ? UniTask.FromResult(true)
            : _pendingOpen || State is UIState.Initializing or UIState.Opening
                ? (_logicalOpen ??= new UniTaskCompletionSource<bool>()).Task : UniTask.FromResult(false);

        internal UniTask AwaitClosed() => State == UIState.Closing ? AwaitTransition() : UniTask.CompletedTask;

        protected void SetTransition(IUITransitionSource source) => Holder.SetTransition(source);
        protected void SetTransition(Func<bool, CancellationToken, UniTask> play, Action<bool> snap) =>
            Holder.SetTransition(play, snap);

        protected void BindHolderCommon(UIHolderObjectBase holder)
        {
            Holder = holder;
            _state = UIState.Loaded;
        }

        internal bool InternalInitialize(bool open = false)
        {
            if (_initializeInvoked)
                return State != UIState.Destroying && State != UIState.Destroyed;
            _state = UIState.Initializing;
            _pendingOpen = open;
            Visible = false;
            SetInteractable(false);
            InvokeHook(Hook.HolderInit);
            if (DestroyRequested) return false;
            _initializeInvoked = true;
            InvokeHook(Hook.Initialize);
            if (DestroyRequested) return false;
            bool requested = _pendingOpen;
            _pendingOpen = false;
            _state = UIState.Initialized;
            if (requested) OpenView();
            else if (open)
            {
                _state = UIState.Closed;
                OnClosed();
            }
            return !DestroyRequested;
        }

        internal bool InternalOpen(object[] userDatas = null)
        {
            if (DestroyRequested || State == UIState.Closing) return false;
            RefreshParams(userDatas);
            if (_activeHook == Hook.Refresh && State is UIState.Opening or UIState.Opened) return true;
            if (State == UIState.Initializing)
            {
                _pendingRefresh |= _pendingOpen;
                _pendingOpen = true;
                return true;
            }
            if (State == UIState.Opening)
            {
                _pendingRefresh = true;
                return true;
            }
            if (State == UIState.Opened)
            {
                _pendingRefresh = true;
                FlushPendingOpen();
                return IsOpen && !DestroyRequested;
            }
            if (State == UIState.Loaded) return InternalInitialize(true) && IsOpen;
            if (State != UIState.Initialized && State != UIState.Closed) return false;
            if (_activeHook != Hook.None)
            {
                _pendingOpen = true;
                return true;
            }
            _pendingRefresh = false;
            return OpenView();
        }

        private bool OpenView()
        {
            int generation = ++_transitionGeneration;
            _state = UIState.Opening;
            Holder.StopTransition();
            if (!IsCurrent(generation)) return false;
            Visible = true;
            SetInteractable(false);
            InvokeHook(Hook.BeforeShow);
            if (!IsCurrent(generation)) return false;
            _openInvoked = true;
            InvokeHook(Hook.Open);
            if (!IsCurrent(generation)) return false;
            IsOpen = true;
            RefreshEffective();
            if (!IsCurrent(generation)) return false;
            InvokeHook(Hook.AfterShow);
            if (!IsCurrent(generation)) return false;
            PlayOpenVisual(generation, !ParentEffective).Forget();
            if (!IsCurrent(generation)) return false;
            UniTaskCompletionSource<bool> opened = _logicalOpen;
            _logicalOpen = null;
            opened?.TrySetResult(IsOpen);
            return IsOpen;
        }

        private void FlushPendingOpen()
        {
            if (_activeHook != Hook.None || DestroyRequested || State == UIState.Initializing) return;
            if (_pendingOpen && State is UIState.Initialized or UIState.Closed)
            {
                _pendingOpen = false;
                OpenView();
            }
            if (_pendingRefresh && State == UIState.Opened)
            {
                _pendingRefresh = false;
                InvokeHook(Hook.Refresh);
            }
        }

        private async UniTaskVoid PlayOpenVisual(int generation, bool skipTransition)
        {
            if (skipTransition) Holder.ApplyTransitionState(true);
            else await Holder.PlayOpenTransitionAsync();
            if (!IsCurrent(generation) || State != UIState.Opening) return;
            _state = UIState.Opened;
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                InvokeHook(Hook.Refresh);
                if (!IsCurrent(generation)) return;
            }
            OnActivityChanged();
            if (IsCurrent(generation)) CompleteTransition();
        }

        internal UniTask InternalClose(bool skipTransition = false, bool destroy = false)
        {
            if (State == UIState.Destroyed || State == UIState.Destroying) return UniTask.CompletedTask;
            DestroyRequested |= destroy;
            _pendingOpen = false;
            _pendingRefresh = false;
            if (destroy) CancelChildCreations();
            if (State == UIState.Initializing)
            {
                if (destroy) DestroyNow();
                else
                {
                    UniTaskCompletionSource<bool> pending = _logicalOpen;
                    _logicalOpen = null;
                    pending?.TrySetResult(false);
                }
                return UniTask.CompletedTask;
            }
            if (State == UIState.Closing) return AwaitClosed();
            if (State == UIState.Closed)
            {
                UniTaskCompletionSource<bool> pending = _logicalOpen;
                _logicalOpen = null;
                if (destroy) DestroyNow();
                pending?.TrySetResult(false);
                return UniTask.CompletedTask;
            }
            if (State == UIState.CreatedUI || State == UIState.Loaded)
            {
                DestroyNow();
                return UniTask.CompletedTask;
            }
            if (State == UIState.Initialized)
            {
                if (destroy) DestroyNow();
                else
                {
                    _state = UIState.Closed;
                    OnClosed();
                }
                return UniTask.CompletedTask;
            }

            int generation = ++_transitionGeneration;
            UniTaskCompletionSource opening = _transition;
            _transition = null;
            UniTaskCompletionSource<bool> logicalOpen = _logicalOpen;
            _logicalOpen = null;
            _state = UIState.Closing;
            IsOpen = false;
            RefreshEffective();
            CloseView(generation, skipTransition || !ParentEffective).Forget();
            UniTask closing = IsCurrent(generation) ? AwaitClosed() : UniTask.CompletedTask;
            Complete(opening);
            logicalOpen?.TrySetResult(false);
            return closing;
        }

        private async UniTaskVoid CloseView(int generation, bool skipTransition)
        {
            Holder.StopTransition();
            if (!IsCurrent(generation)) return;
            if (_openInvoked)
            {
                InvokeHook(Hook.BeforeClose);
                if (!IsCurrent(generation)) return;
                _openInvoked = false;
                InvokeHook(Hook.Close);
                if (!IsCurrent(generation)) return;
            }
            if (skipTransition) Holder.ApplyTransitionState(false);
            else await Holder.PlayCloseTransitionAsync();
            if (!IsCurrent(generation) || State != UIState.Closing) return;
            Visible = false;
            _state = UIState.Closed;
            UniTaskCompletionSource closing = _transition;
            _transition = null;
            try
            {
                InvokeHook(Hook.AfterClose);
                if (!IsCurrent(generation)) return;
                OnClosed();
                if (DestroyRequested && State == UIState.Closed) DestroyNow();
            }
            finally { Complete(closing); }
        }

        public UniTask AwaitTransition() => State is UIState.Opening or UIState.Closing
            ? (_transition ??= new UniTaskCompletionSource()).Task : UniTask.CompletedTask;

        private void CompleteTransition()
        {
            UniTaskCompletionSource completion = _transition;
            _transition = null;
            Complete(completion);
        }

        internal void RefreshEffective()
        {
            bool effective = IsOpen && !DestroyRequested && ParentEffective;
            bool changed = Effective != effective;
            Effective = effective;
            if (changed)
            {
                SetInteractable(effective);
                if (effective) RegisterEventListeners();
                else ReleaseEventListeners();
                OnActivityChanged();
            }
            RefreshChildrenEffective();
        }

        internal void InternalUpdate() => InternalUpdate(this, _transitionGeneration);

        private void InternalUpdate(UIBase root, int generation)
        {
            if (!Effective) return;
            if (State == UIState.Opened && Metadata.MetaInfo.HasUpdate) InvokeHook(Hook.Update);
            if (Effective && root.IsCurrent(generation)) UpdateChildren(root, generation);
        }

        internal UniTask InternalDestroy(bool skipTransition = false) =>
            InternalClose(skipTransition, destroy: true);

        internal void DestroyNow()
        {
            if (State == UIState.Destroyed || State == UIState.Destroying) return;
            ++_transitionGeneration;
            DestroyRequested = true;
            _pendingOpen = false;
            _pendingRefresh = false;
            _state = UIState.Destroying;
            IsOpen = false;
            CancelChildCreations();
            RefreshEffective();
            UniTaskCompletionSource transition = _transition;
            _transition = null;
            UniTaskCompletionSource<bool> logicalOpen = _logicalOpen;
            _logicalOpen = null;
            try
            {
                OnDestroyStarted();
                DisposeView();
            }
            finally
            {
                _state = UIState.Destroyed;
                Complete(transition);
                logicalOpen?.TrySetResult(false);
            }
        }

        private void DisposeView()
        {
            Holder?.StopTransition();
            if (_openInvoked)
            {
                _openInvoked = false;
                InvokeHook(Hook.Close);
            }
            if (Holder != null) InvokeHook(Hook.HolderDestroy);
            if (_initializeInvoked) InvokeHook(Hook.Destroy);
            OnDestroyed();
            DestroyChildrenImmediate();
            DetachHolder();
            if (_activeHook != Hook.RegisterEvent) DisposeEventListeners();
            Metadata = null;
            _visible = false;
            _state = UIState.Destroyed;
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
                    case Hook.HolderInit: Holder.InvokeWindowInit(); break;
                    case Hook.BeforeShow: Holder.InvokeWindowBeforeShow(); break;
                    case Hook.AfterShow: Holder.InvokeWindowAfterShow(); break;
                    case Hook.BeforeClose: Holder.InvokeWindowBeforeClosed(); break;
                    case Hook.AfterClose: Holder.InvokeWindowAfterClosed(); break;
                    case Hook.HolderDestroy: Holder.InvokeWindowDestroy(); break;
                }
            }
            catch (Exception error) { Log.Exception(error); }
            finally { _activeHook = previous; }
            FlushPendingOpen();
        }

        private bool IsCurrent(int generation) => generation == _transitionGeneration;

        private static void Complete(UniTaskCompletionSource source) => source?.TrySetResult();

        private void RegisterEventListeners()
        {
            _eventListenerProxy ??= MemoryPool.Acquire<EventListenerProxy>();
            InvokeHook(Hook.RegisterEvent);
            if (State is UIState.Destroying or UIState.Destroyed) DisposeEventListeners();
        }

        private void ReleaseEventListeners() => _eventListenerProxy?.Clear();

        private void DisposeEventListeners()
        {
            EventListenerProxy proxy = _eventListenerProxy;
            if (proxy == null) return;
            _eventListenerProxy = null;
            MemoryPool.Release(proxy);
        }

        private void DetachHolder()
        {
            Visible = false;
            UIHolderObjectBase holder = Holder;
            Holder = null;
            ReleaseVisuals();
            _userDatas = null;
            if (holder == null) return;
            holder.ResetRuntimeTransition();
            if (!_destroyHolderOnDispose || !holder.IsValid()) return;
            if (Application.isPlaying) Object.Destroy(holder.gameObject);
            else Object.DestroyImmediate(holder.gameObject);
        }
    }
}
