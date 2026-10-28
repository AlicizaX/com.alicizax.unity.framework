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
        private bool _proxyReleasePending;
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
        protected ReadOnlyMemory<object> UserDatas => _userDatas;
        protected virtual void OnInitialize() { }
        protected virtual void OnOpen() { }
        protected virtual void OnRefresh() { }
        protected virtual void OnClose() { }
        protected virtual void OnDestroy() { }
        protected virtual void OnUpdate(float deltaTime) { }
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
            holder.SetOwner(this);
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
            if (State is not (UIState.Initialized or UIState.Closed)) return false;
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
            CompleteLogicalOpen(IsOpen);
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
            if (State is UIState.Destroyed or UIState.Destroying) return UniTask.CompletedTask;
            DestroyRequested |= destroy;
            _pendingOpen = false;
            _pendingRefresh = false;
            if (destroy) CancelChildCreations();
            if (State == UIState.Initializing)
            {
                if (destroy) DestroyNow();
                else CompleteLogicalOpen(false);
                return UniTask.CompletedTask;
            }
            if (State == UIState.Closing) return AwaitClosed();
            if (State == UIState.Closed)
            {
                if (destroy) DestroyNow();
                CompleteLogicalOpen(false);
                return UniTask.CompletedTask;
            }
            if (State is UIState.CreatedUI or UIState.Loaded)
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
            UniTaskCompletionSource opening = TakeTransition();
            UniTaskCompletionSource<bool> logicalOpen = TakeLogicalOpen();
            _state = UIState.Closing;
            IsOpen = false;
            RefreshEffective();
            CloseView(generation, skipTransition || !ParentEffective).Forget();
            UniTask closing = IsCurrent(generation) ? AwaitClosed() : UniTask.CompletedTask;
            opening?.TrySetResult();
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
            UniTaskCompletionSource closing = TakeTransition();
            try
            {
                InvokeHook(Hook.AfterClose);
                if (!IsCurrent(generation)) return;
                OnClosed();
                if (DestroyRequested && State == UIState.Closed) DestroyNow();
            }
            finally { closing?.TrySetResult(); }
        }

        public UniTask AwaitTransition() => State is UIState.Opening or UIState.Closing
            ? (_transition ??= new UniTaskCompletionSource()).Task : UniTask.CompletedTask;

        private void CompleteTransition()
        {
            UniTaskCompletionSource completion = TakeTransition();
            completion?.TrySetResult();
        }

        private UniTaskCompletionSource TakeTransition()
        {
            UniTaskCompletionSource source = _transition;
            _transition = null;
            return source;
        }

        private UniTaskCompletionSource<bool> TakeLogicalOpen()
        {
            UniTaskCompletionSource<bool> source = _logicalOpen;
            _logicalOpen = null;
            return source;
        }

        private void CompleteLogicalOpen(bool result)
        {
            UniTaskCompletionSource<bool> source = TakeLogicalOpen();
            source?.TrySetResult(result);
        }

        internal void RefreshEffective()
        {
            bool effective = IsOpen && !DestroyRequested && ParentEffective;
            if (Effective == effective) return;
            Effective = effective;
            SetInteractable(effective);
            if (effective) RegisterEventListeners();
            else ReleaseEventListeners();
            OnActivityChanged();
            RefreshChildrenEffective();
        }

        internal void InternalUpdate(float deltaTime) => InternalUpdate(this, _transitionGeneration, deltaTime);

        private void InternalUpdate(UIBase root, int generation, float deltaTime)
        {
            if (!Effective) return;
            if (State == UIState.Opened && Metadata.MetaInfo.HasUpdate) InvokeHook(Hook.Update, deltaTime);
            if (Effective && root.IsCurrent(generation)) UpdateChildren(root, generation, deltaTime);
        }

        internal UniTask InternalDestroy(bool skipTransition = false) =>
            InternalClose(skipTransition, destroy: true);

        internal void DestroyNow()
        {
            if (State is UIState.Destroyed or UIState.Destroying) return;
            ++_transitionGeneration;
            DestroyRequested = true;
            _pendingOpen = false;
            _pendingRefresh = false;
            _state = UIState.Destroying;
            IsOpen = false;
            CancelChildCreations();
            RefreshEffective();
            UniTaskCompletionSource transition = TakeTransition();
            UniTaskCompletionSource<bool> logicalOpen = TakeLogicalOpen();
            try
            {
                OnDestroyStarted();
                DisposeView();
            }
            finally
            {
                _state = UIState.Destroyed;
                transition?.TrySetResult();
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
            ReleaseEventListenerProxy();
            Metadata = null;
            _visible = false;
            _state = UIState.Destroyed;
            _childBuffer = null;
        }

        private void InvokeHook(Hook hook, float updateDeltaTime = 0f)
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
                    case Hook.Update: OnUpdate(updateDeltaTime); break;
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
            finally
            {
                _activeHook = previous;
                if (_proxyReleasePending) ReleaseEventListenerProxy();
            }
            FlushPendingOpen();
        }

        private bool IsCurrent(int generation) => generation == _transitionGeneration;

        private void RegisterEventListeners()
        {
            _eventListenerProxy ??= MemoryPool.Acquire<EventListenerProxy>();
            InvokeHook(Hook.RegisterEvent);
        }

        private void ReleaseEventListeners() => _eventListenerProxy?.Clear();

        /// <summary>
        /// 释放事件代理。若正处于用户钩子内（例如 OnRegisterEvent 中销毁自身），延迟到钩子返回后释放，
        /// 避免用户代码继续向已归还池的代理写入句柄。
        /// </summary>
        private void ReleaseEventListenerProxy()
        {
            if (_activeHook != Hook.None)
            {
                _proxyReleasePending = true;
                return;
            }
            _proxyReleasePending = false;
            EventListenerProxy proxy = _eventListenerProxy;
            _eventListenerProxy = null;
            if (proxy != null) MemoryPool.Release(proxy);
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
            holder.SetOwner(null);
            if (!_destroyHolderOnDispose || !holder.IsValid()) return;
            if (Application.isPlaying) Object.Destroy(holder.gameObject);
            else Object.DestroyImmediate(holder.gameObject);
        }
    }
}
