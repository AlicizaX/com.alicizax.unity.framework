using System;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 窗口与 Widget 的公共基类：一个视图的生命周期状态机。打开、关闭、销毁按当前 <see cref="UIState"/> 直接切换。
    /// <list type="number">
    /// <item>钩子或节点事件里的打开、关闭、销毁立即生效，原来的流程不再继续。</item>
    /// <item>反向操作直接打断转场：Closing 中打开从当前外观反向播放，Opening 中关闭同理。</item>
    /// <item>已打开时再次打开只更新参数并刷新一次。</item>
    /// </list>
    /// </summary>
    public abstract partial class UIBase
    {
        private static readonly Action<object> CancelLoading = static state =>
        {
            var view = (UIBase)state;
            if (view._state == UIState.Loading) view.DestroySelf();
        };

        internal UIService Service;
        internal UIMetaRegistry.UIMetaInfo MetaInfo;

        private UIHolderObjectBase _holder;
        private UIState _state;
        private object _userData;
        private int _lifecycleVersion;
        private bool _destroyOnClosed;
        private EventListenerProxy _events;
        private ResourceScope _resources;
        private CancellationTokenSource _destroyCancellation;

        public UIState State => _state;

        public bool Visible => _state is UIState.Opening or UIState.Opened or UIState.Closing;

        protected object UserData => _userData;

        internal UIHolderObjectBase Holder => _holder;

        internal bool IsEnabled => _state is UIState.Opening or UIState.Opened && ParentEnabled;

        private protected virtual bool ParentEnabled => true;

        protected EventListenerProxy EventProxy
        {
            get
            {
                if (_events != null) return _events;
                if (_state == UIState.Destroyed) throw new InvalidOperationException($"[UI] {MetaInfo.Name} is destroyed.");
                _events = MemoryPool.Acquire<EventListenerProxy>();
                if (!IsEnabled) _events.Suspend();
                return _events;
            }
        }

        protected ResourceScope Res
        {
            get
            {
                if (_resources.Service != null) return _resources;
                if (_state == UIState.Destroyed) throw new InvalidOperationException($"[UI] {MetaInfo.Name} is destroyed.");
                _resources = AppServices.Require<IResourceService>().CreateScope(GetType().Name);
                return _resources;
            }
        }

        /// <summary>视图销毁时取消：资源加载与业务异步用它跟随视图的生命周期。</summary>
        protected internal CancellationToken DestroyCancellationToken
        {
            get
            {
                if (_state == UIState.Destroyed) return new CancellationToken(true);
                _destroyCancellation ??= new CancellationTokenSource();
                return _destroyCancellation.Token;
            }
        }

        /// <summary>等到 Opening/Closing 结束（状态稳定为 Opened、Closed 或 Destroyed）。</summary>
        public UniTask AwaitTransition() =>
            _state is UIState.Opening or UIState.Closing
                ? UniTask.WaitUntil(this, static view => view._state is not (UIState.Opening or UIState.Closing))
                : UniTask.CompletedTask;

        internal UniTask AwaitLoaded(CancellationToken cancellationToken = default) =>
            _state == UIState.Loading
                ? UniTask.WaitUntil(this, static view => view._state != UIState.Loading, PlayerLoopTiming.Update, cancellationToken)
                : UniTask.CompletedTask;

        protected virtual void OnInitialize() { }
        protected virtual void OnOpen() { }
        protected virtual void OnRefresh() { }
        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnClose() { }
        protected virtual void OnDestroy() { }

        /// <summary>绑定节点时准备外观组件。</summary>
        private protected abstract void BindVisuals(UIHolderObjectBase holder);
        private protected abstract void ApplyVisible(bool visible);
        private protected abstract void SetInteractable(bool interactable);
        private protected abstract void ReleaseClosed(bool destroy);

        internal abstract void DestroySelf();

        internal void LoadHolder(Transform parent, bool open) =>
            AttachHolder(UIHolderFactory.Load(MetaInfo.HolderType, parent), open);

        /// <param name="cancellationToken">调用方的取消：视图仍在加载时取消即销毁它。</param>
        internal async UniTask LoadHolderAsync(Transform parent, bool open, CancellationToken cancellationToken)
        {
            UIHolderObjectBase holder;
            using (cancellationToken.Register(CancelLoading, this))
            {
                try
                {
                    holder = await UIHolderFactory.LoadAsync(MetaInfo.HolderType, parent, DestroyCancellationToken);
                }
                catch (OperationCanceledException) when (_state == UIState.Destroyed)
                {
                    return;
                }
            }
            // 加载期间视图已被销毁：迟到的节点直接回收
            if (_state == UIState.Destroyed)
            {
                if (holder != null) UIHolderFactory.DestroyNode(holder.gameObject);
                return;
            }
            AttachHolder(holder, open);
        }

        private void AttachHolder(UIHolderObjectBase holder, bool open)
        {
            if (holder == null)
            {
                DestroySelf();
                return;
            }
            _holder = holder;
            holder.Bind(this);
            _state = UIState.Closed;
            BindVisuals(holder);
            if (!InvokeLifecycle(UILifecycle.Initialize)) return;
            SetVisible(false);
            if (open) OpenView(null);
        }

        /// <summary>打开；已打开时只更新参数并刷新一次。userData 为 null 时保留原参数。</summary>
        internal void OpenView(object userData)
        {
            if (userData != null) _userData = userData;
            switch (_state)
            {
                case UIState.Opening:
                case UIState.Opened:
                    InvokeLifecycle(UILifecycle.Refresh);
                    break;
                case UIState.Closed:
                case UIState.Closing:
                    BeginOpen();
                    break;
            }
        }

        /// <param name="snap">关闭转场直接落到终态。</param>
        internal void CloseView(bool snap, bool destroy)
        {
            if (_state == UIState.Closing) _destroyOnClosed |= destroy;
            if (_state is not (UIState.Opening or UIState.Opened)) return;
            _destroyOnClosed = destroy;
            bool parentEnabled = ParentEnabled;
            _state = UIState.Closing;
            SetInteractable(false);
            if (parentEnabled) SetEnabled(false);
            if (InvokeLifecycle(UILifecycle.Close)) PlayTransition(false, snap || !parentEnabled).Forget();
        }

        /// <summary>立即销毁，不播关闭转场。</summary>
        internal void DestroyView(bool destroyNode)
        {
            UIState previous = _state;
            bool wasEnabled = IsEnabled;
            _state = UIState.Destroyed;
            _lifecycleVersion++;
            if (wasEnabled) SetEnabled(false);
            _destroyCancellation?.Cancel();
            if (previous is UIState.Opening or UIState.Opened) InvokeLifecycle(UILifecycle.Close);
            if (previous != UIState.Loading) InvokeLifecycle(UILifecycle.Destroy);
            DestroyWidgets();
            if (previous != UIState.Loading)
            {
                if (_holder.TryUnbind() && destroyNode) UIHolderFactory.DestroyNode(_holder.gameObject);
                _holder = null;
            }
            if (_events != null)
            {
                MemoryPool.Release(_events);
                _events = null;
            }
            _resources.Dispose();
            _resources = default;
            _userData = null;
        }

        internal bool InvokeLifecycle(UILifecycle stage, float deltaTime = 0f)
        {
            int version = _lifecycleVersion;
            _holder.Raise(stage);
            if (version != _lifecycleVersion) return false;
            try
            {
                switch (stage)
                {
                    case UILifecycle.Initialize: OnInitialize(); break;
                    case UILifecycle.Open: OnOpen(); break;
                    case UILifecycle.Refresh: OnRefresh(); break;
                    case UILifecycle.Update: OnUpdate(deltaTime); break;
                    case UILifecycle.Close: OnClose(); break;
                    case UILifecycle.Destroy: OnDestroy(); break;
                }
            }
            catch (Exception error)
            {
                Log.Exception(error);
            }
            return version == _lifecycleVersion;
        }

        internal void ClearUserData() => _userData = null;

        private void BeginOpen()
        {
            if (_state == UIState.Closed) SetVisible(true);
            _state = UIState.Opening;
            _destroyOnClosed = false;
            bool parentEnabled = ParentEnabled;
            if (parentEnabled) SetEnabled(true);
            if (InvokeLifecycle(UILifecycle.Open)) PlayTransition(true, !parentEnabled).Forget();
        }

        private async UniTaskVoid PlayTransition(bool open, bool snap)
        {
            int version = ++_lifecycleVersion;
            IUITransition transition = _holder.UITransition;
            if (transition != null)
            {
                try
                {
                    await (open ? transition.Open(snap) : transition.Close(snap));
                }
                catch (Exception error)
                {
                    if (error is not OperationCanceledException) Log.Exception(error);
                }
            }
            if (version != _lifecycleVersion) return;
            if (open)
            {
                _state = UIState.Opened;
                SetInteractable(true);
                return;
            }
            _state = UIState.Closed;
            SetVisible(false);
            ReleaseClosed(_destroyOnClosed);
        }

        private void SetEnabled(bool enabled)
        {
            if (MetaInfo.HasUpdate)
            {
                if (enabled) Service.AddUpdate(this);
                else Service.RemoveUpdate(this);
            }
            if (_events != null)
            {
                if (enabled) _events.Resume();
                else _events.Suspend();
            }
            SetWidgetsEnabled(enabled);
        }

        private void SetVisible(bool visible)
        {
            ApplyVisible(visible);
#if UNITY_EDITOR
            SyncSceneVisibility(visible);
#endif
        }

#if UNITY_EDITOR
        /// <summary>编辑器播放中同步 Scene 视图的显隐，隐藏的界面不干扰场景拾取。</summary>
        private void SyncSceneVisibility(bool visible)
        {
            if (!Application.isPlaying || !EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (visible) SceneVisibilityManager.instance.Show(_holder.gameObject, true);
            else SceneVisibilityManager.instance.Hide(_holder.gameObject, true);
        }
#endif
    }
}
