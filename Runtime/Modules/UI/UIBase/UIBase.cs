using System;
using System.Threading;
using AlicizaX.Resource.Runtime;
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
        internal UIWindow RootWindow;
        private readonly UITransitionGate _transitionGate = new();
        private UIState _state = UIState.CreatedUI;
        private int _transitionGeneration;
        private UniTaskCompletionSource<bool> _logicalOpen;
        private EventListenerProxy _eventListenerProxy;
        private UITickNode _updateNode;
        private UITickNode _transitionNode;
        private bool _proxyReleasePending;
        private object[] _userDatas;
        private bool _visible;
        private bool _initializeInvoked;
        private bool _openInvoked;
        private bool _destroyHolderOnDispose = true;
        private bool _holderLost;
        private bool _pendingRefresh;
        private bool _pendingOpen;
        private Hook _activeHook;
        private ResourceScope _resources;

        private enum Hook : byte
        {
            None, Initialize, Open, Refresh, Close, Destroy, Update, RegisterEvent,
            HolderInit, BeforeShow, AfterShow, BeforeClose, AfterClose, HolderDestroy,
        }

        internal UIState State => _state;
        internal bool DestroyRequested { get; private set; }
        internal bool Effective { get; private set; }
        internal bool IsUpdating => Effective && _updateNode != null;
        internal virtual bool ParentEffective => true;
        public bool IsOpen { get; private set; }
        public virtual bool IsVisible => _visible;
        internal abstract Type UIHolderType { get; }
        internal abstract void BindUIHolder(UIHolderObjectBase holder);

        protected ResourceScope Res
        {
            get
            {
                if (_resources.Service == null)
                    _resources = AppServices.Require<IResourceService>().CreateScope(GetType().Name);
                return _resources;
            }
        }

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

        internal bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (_holderLost) return;
                ApplyVisible(value);
#if UNITY_EDITOR
                if (Holder != null && Application.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
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

        internal UniTask AwaitClosed() => State == UIState.Closing ? _transitionGate.Task : UniTask.CompletedTask;

        protected void SetTransition(IUITransitionSource source) => Holder.SetTransition(source);
        protected void SetTransition(Func<bool, CancellationToken, UniTask> play, Action<bool> snap) =>
            Holder.SetTransition(play, snap);

        protected void BindHolderCommon(UIHolderObjectBase holder)
        {
            Holder = holder;
            holder.SetOwner(this);
            if (Metadata.MetaInfo.HasUpdate) _updateNode = new UITickNode(this);
            ReserveTransitionNode();
            _state = UIState.Loaded;
        }

        internal void ReserveTransitionNode()
        {
            if (_transitionNode == null && Holder.UsesTickTransition) _transitionNode = new UITickNode(this);
        }

        internal void LinkTransition() => Service.LinkTransition(_transitionNode);
        internal void UnlinkTransition() => Service.UnlinkTransition(_transitionNode);

        internal void LoseHolder()
        {
            _holderLost = true;
            DestroyNow();
        }

        internal bool InternalInitialize(bool open = false)
        {
            _state = UIState.Initializing;
            _pendingOpen = open;
            Visible = false;
            if (DestroyRequested) return false;
            SetInteractable(false);
            if (DestroyRequested) return false;
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
                int generation = _transitionGeneration;
                _state = UIState.Closed;
                Visible = false;
                if (IsCurrent(generation)) OnClosed();
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
            _transitionGate.Restart();
            Holder.StopTransition();
            if (!IsCurrent(generation)) return false;
            Visible = true;
            if (!IsCurrent(generation)) return false;
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
            PlayOpenVisual(generation);
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

        private void PlayOpenVisual(int generation)
        {
            if (ParentEffective)
            {
                Holder.PlayOpenTransition();
                return;
            }
            Holder.ApplyTransitionState(true);
            if (IsCurrent(generation)) FinishOpen();
        }

        internal void FinishTransition()
        {
            if (_state == UIState.Opening) FinishOpen();
            else if (_state == UIState.Closing) FinishClose();
        }

        private void FinishOpen()
        {
            int generation = _transitionGeneration;
            _state = UIState.Opened;
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                InvokeHook(Hook.Refresh);
                if (!IsCurrent(generation)) return;
            }
            _transitionGate.Release(_transitionGate.Settle());
        }

        internal UniTask InternalClose(bool skipTransition = false, bool destroy = false)
        {
            BeginClose(skipTransition, destroy);
            return AwaitClosed();
        }

        internal void BeginClose(bool skipTransition, bool destroy)
        {
            if (State is UIState.Destroyed or UIState.Destroying) return;
            DestroyRequested |= destroy;
            _pendingOpen = false;
            _pendingRefresh = false;
            if (destroy)
            {
                CancelChildCreations();
                if (State is UIState.Destroyed or UIState.Destroying) return;
            }
            if (State == UIState.Closing) return;
            if (State == UIState.Initializing)
            {
                if (destroy) DestroyNow();
                else CompleteLogicalOpen(false);
                return;
            }
            if (State == UIState.Closed)
            {
                if (destroy) DestroyNow();
                CompleteLogicalOpen(false);
                return;
            }
            if (State is UIState.CreatedUI or UIState.Loaded or UIState.Initialized)
            {
                DestroyNow();
                return;
            }

            int generation = ++_transitionGeneration;
            UITransitionWaiters opening = _transitionGate.Restart();
            UniTaskCompletionSource<bool> logicalOpen = TakeLogicalOpen();
            _state = UIState.Closing;
            IsOpen = false;
            RefreshEffective();
            CloseView(generation, skipTransition || !ParentEffective);
            _transitionGate.Release(opening);
            logicalOpen?.TrySetResult(false);
        }

        private void CloseView(int generation, bool skipTransition)
        {
            if (!IsCurrent(generation)) return;
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
            if (!skipTransition)
            {
                Holder.PlayCloseTransition();
                return;
            }
            Holder.ApplyTransitionState(false);
            if (IsCurrent(generation)) FinishClose();
        }

        private void FinishClose()
        {
            int generation = _transitionGeneration;
            _state = UIState.Closed;
            UITransitionWaiters closing = _transitionGate.Settle();
            Visible = false;
            if (IsCurrent(generation)) InvokeHook(Hook.AfterClose);
            if (IsCurrent(generation))
            {
                OnClosed();
                if (DestroyRequested && State == UIState.Closed) DestroyNow();
            }
            _transitionGate.Release(closing);
        }

        public UniTask AwaitTransition() => State is UIState.Opening or UIState.Closing
            ? _transitionGate.Task : UniTask.CompletedTask;

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
            if (_updateNode != null)
            {
                if (effective) Service.LinkUpdate(_updateNode);
                else Service.UnlinkUpdate(_updateNode);
            }
            if (!_holderLost)
            {
                SetInteractable(effective);
                if (Effective != effective) return;
            }
            if (effective) ResumeEventListeners();
            else _eventListenerProxy?.Suspend();
            RefreshChildrenEffective();
        }

        internal void RunUpdate(float deltaTime)
        {
            if (State == UIState.Opened && RootWindow.State == UIState.Opened) InvokeHook(Hook.Update, deltaTime);
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
            RefreshEffective();
            UITransitionWaiters transition = _transitionGate.Settle();
            UniTaskCompletionSource<bool> logicalOpen = TakeLogicalOpen();
            try
            {
                OnDestroyStarted();
                if (Holder is not null) Holder.StopTransition();
                CancelChildCreations();
                DisposeView();
            }
            finally
            {
                _state = UIState.Destroyed;
                _transitionGate.Release(transition);
                logicalOpen?.TrySetResult(false);
            }
        }

        private void DisposeView()
        {
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
            _resources.Dispose();
            ReleaseEventListenerProxy();
            Metadata = null;
            _visible = false;
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

#if UNITY_EDITOR
        internal bool HasPendingOpen => _pendingOpen;
        internal bool IsTransitionPending => _transitionGate.UnsafeGetStatus() == UniTaskStatus.Pending;
        internal EventListenerProxy EventListeners => _eventListenerProxy;
        internal UITickNode UpdateNode => _updateNode;
#endif

        private void ResumeEventListeners()
        {
            if (_eventListenerProxy != null)
            {
                _eventListenerProxy.Resume();
                return;
            }
            _eventListenerProxy = MemoryPool.Acquire<EventListenerProxy>();
            InvokeHook(Hook.RegisterEvent);
        }

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
            UIHolderObjectBase holder = Holder;
            if (holder != null) Visible = false;
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
