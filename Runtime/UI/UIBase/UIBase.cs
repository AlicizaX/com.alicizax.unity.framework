using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase : IDisposable
    {
        protected UIBase()
        {
            _state = UIState.CreatedUI;
        }

        private bool _disposed;

        internal Canvas _canvas;

        internal GraphicRaycaster _raycaster;
        private int _lifecycleVersion;

        internal UIState _state = UIState.Uninitialized;
        internal UIState State => _state;


        private System.Object[] _userDatas;
        protected System.Object UserData => _userDatas != null && _userDatas.Length >= 1 ? _userDatas[0] : null;
        protected System.Object[] UserDatas => _userDatas;

        private RuntimeTypeHandle _runtimeTypeHandle;
        private int _uiTypeId = -1;

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
        /// 如果重写当前方法 则同步OnOpen不会调用
        /// </summary>
        protected virtual UniTask OnOpenAsync()
        {
            OnOpen();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnClose不会调用
        /// </summary>
        protected virtual UniTask OnCloseAsync()
        {
            OnClose();
            return UniTask.CompletedTask;
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
                if (Application.isPlaying)
                    Object.Destroy(Holder.gameObject);
                else
                    Object.DestroyImmediate(Holder.gameObject);
            }

            _disposed = true;
        }

        internal bool Visible
        {
            get => _canvas != null && _canvas.gameObject.layer == UIComponent.UIShowLayer;

            set
            {
                if (_canvas != null)
                {
                    int setLayer = value ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
                    if (_canvas.gameObject.layer == setLayer)
                        return;

                    _canvas.gameObject.layer = setLayer;
                    ChildVisible(value);
                    Interactable = value;
                }
            }
        }

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

        private EventListenerProxy EventListenerProxy => _eventListenerProxy ??= MemoryPool.Acquire<EventListenerProxy>();

        private void ReleaseEventListenerProxy()
        {
            if (_eventListenerProxy != null)
            {
                MemoryPool.Release(_eventListenerProxy);
                _eventListenerProxy = null;
            }
        }

        #endregion

        #region 管理器内部调用

        internal UIHolderObjectBase Holder;
        internal abstract Type UIHolderType { get; }

        internal abstract void BindUIHolder(UIHolderObjectBase holder, UIBase owner);

        internal async UniTask<bool> InternalInitlized(CancellationToken cancellationToken = default)
        {
            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Initialized))
                return false;

            _state = UIState.Initialized;
            Holder.OnWindowInitEvent?.Invoke();
            await OnInitializeAsync();
            if (cancellationToken.IsCancellationRequested)
                return false;

            OnRegisterEvent(EventListenerProxy);
            return true;
        }

        internal bool InternalInitlizedSync()
        {
            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Initialized))
                return false;

            _state = UIState.Initialized;
            Holder.OnWindowInitEvent?.Invoke();
            OnInitialize();
            OnRegisterEvent(EventListenerProxy);
            return true;
        }

        internal async UniTask<bool> InternalOpen(CancellationToken cancellationToken = default)
        {
            if (_state == UIState.Opened || _state == UIState.Opening)
                return _state == UIState.Opened;

            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Opening))
                return false;

            int lifecycleVersion = BeginLifecycleTransition();
            _state = UIState.Opening;
            Visible = true;
            Holder.OnWindowBeforeShowEvent?.Invoke();
            await OnOpenAsync();
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening) || cancellationToken.IsCancellationRequested)
            {
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            bool openCanceled = await Holder.PlayOpenTransitionAsync(cancellationToken).SuppressCancellationThrow();
            if (openCanceled || cancellationToken.IsCancellationRequested)
            {
                RollbackOpeningState(lifecycleVersion);
                return false;
            }

            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
                return false;

            _state = UIState.Opened;
            Holder.OnWindowAfterShowEvent?.Invoke();
            return true;
        }

        internal bool InternalOpenSync()
        {
            if (_state == UIState.Opened || _state == UIState.Opening)
                return _state == UIState.Opened;

            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Opening))
                return false;

            int lifecycleVersion = BeginLifecycleTransition();
            _state = UIState.Opening;
            Visible = true;
            Holder.OnWindowBeforeShowEvent?.Invoke();
            OnOpen();
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
                return false;

            FireAndForgetOpenTransition(lifecycleVersion);
            return true;
        }

        internal async UniTask<bool> InternalClose(CancellationToken cancellationToken = default)
        {
            if (_state == UIState.Closed || _state == UIState.Closing)
                return _state == UIState.Closed;

            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Closing))
                return false;

            int lifecycleVersion = BeginLifecycleTransition();
            _state = UIState.Closing;
            Holder.OnWindowBeforeClosedEvent?.Invoke();
            await OnCloseAsync();
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing) || cancellationToken.IsCancellationRequested)
                return false;

            bool closeCanceled = await Holder.PlayCloseTransitionAsync(cancellationToken).SuppressCancellationThrow();
            if (closeCanceled || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
                return false;

            Visible = false;
            _state = UIState.Closed;
            Holder.OnWindowAfterClosedEvent?.Invoke();
            return true;
        }

        internal bool InternalCloseSync()
        {
            if (_state == UIState.Closed || _state == UIState.Closing)
                return _state == UIState.Closed;

            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Closing))
                return false;

            int lifecycleVersion = BeginLifecycleTransition();
            _state = UIState.Closing;
            Holder.OnWindowBeforeClosedEvent?.Invoke();
            OnClose();
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
                return false;

            FireAndForgetCloseTransition(lifecycleVersion);
            return true;
        }

        internal void InternalUpdate()
        {
            if (_state != UIState.Opened) return;
            OnUpdate();
            UpdateChildren();
        }

        internal async UniTask InternalDestroy()
        {
            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Destroying))
                return;

            InterruptLifecycleTransition();
            _state = UIState.Destroying;
            Holder?.OnWindowDestroyEvent?.Invoke();
            await DestroyAllChildren();
            OnDestroy();
            ReleaseEventListenerProxy();
            Dispose();
            _state = UIState.Destroyed;
        }

        internal void InternalDestroyImmediate()
        {
            if (!UIStateMachine.ValidateTransition(GetType().Name, _state, UIState.Destroying))
            {
                return;
            }

            InterruptLifecycleTransition();
            _state = UIState.Destroying;
            Holder?.OnWindowDestroyEvent?.Invoke();
            DestroyAllChildrenImmediate();
            OnDestroy();
            ReleaseEventListenerProxy();
            Dispose();
            _state = UIState.Destroyed;
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

        private void InterruptLifecycleTransition()
        {
            _lifecycleVersion++;
            Holder?.StopTransition();
        }

        private bool IsCurrentLifecycleTransition(int lifecycleVersion, UIState state)
        {
            return lifecycleVersion == _lifecycleVersion && _state == state;
        }

        private void RollbackOpeningState(int lifecycleVersion)
        {
            if (!IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
                return;

            Visible = false;
            _state = UIState.Initialized;
        }

        private async UniTaskVoid FireAndForgetOpenTransition(int lifecycleVersion)
        {
            bool canceled = await Holder.PlayOpenTransitionAsync().SuppressCancellationThrow();
            if (canceled || !IsCurrentLifecycleTransition(lifecycleVersion, UIState.Opening))
                return;

            _state = UIState.Opened;
            Holder.OnWindowAfterShowEvent?.Invoke();
        }

        private async UniTaskVoid FireAndForgetCloseTransition(int lifecycleVersion)
        {
            bool canceled = await Holder.PlayCloseTransitionAsync().SuppressCancellationThrow();
            if (canceled || !IsCurrentLifecycleTransition(lifecycleVersion, UIState.Closing))
                return;

            Visible = false;
            _state = UIState.Closed;
            Holder.OnWindowAfterClosedEvent?.Invoke();
        }

        #endregion
    }
}
