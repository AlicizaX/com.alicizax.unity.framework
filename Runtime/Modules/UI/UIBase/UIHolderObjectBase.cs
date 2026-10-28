using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    [DisallowMultipleComponent]
    public abstract class UIHolderObjectBase : MonoBehaviour
    {
        private UIBase _owner;
        private GameObject _target;
        [SerializeField, HideInInspector] private Component _transitionPlayerComponent;
        private IUITransitionSource _transitionSource;
        private bool _hasTransitionOverride;
        private CancellationTokenSource _transitionCts;
        private ITickTransitionSource _tickTransition;
        private UniTaskCompletionSource _tickCompletion;
        private bool _tickTransitionOpen;
        private bool _isAlive = true;

        // 生命周期事件：订阅变更时重建快照，invoke 路径零分配（GetInvocationList 只在订阅变化后的首次调用发生）。
        private Action _windowInitHandler;
        private Action[] _windowInitSnapshot;
        private Action _windowBeforeShowHandler;
        private Action[] _windowBeforeShowSnapshot;
        private Action _windowAfterShowHandler;
        private Action[] _windowAfterShowSnapshot;
        private Action _windowBeforeClosedHandler;
        private Action[] _windowBeforeClosedSnapshot;
        private Action _windowAfterClosedHandler;
        private Action[] _windowAfterClosedSnapshot;
        private Action _windowDestroyHandler;
        private Action[] _windowDestroySnapshot;

        public event Action OnWindowInitEvent
        {
            add { _windowInitHandler += value; _windowInitSnapshot = null; }
            remove { _windowInitHandler -= value; _windowInitSnapshot = null; }
        }

        public event Action OnWindowBeforeShowEvent
        {
            add { _windowBeforeShowHandler += value; _windowBeforeShowSnapshot = null; }
            remove { _windowBeforeShowHandler -= value; _windowBeforeShowSnapshot = null; }
        }

        public event Action OnWindowAfterShowEvent
        {
            add { _windowAfterShowHandler += value; _windowAfterShowSnapshot = null; }
            remove { _windowAfterShowHandler -= value; _windowAfterShowSnapshot = null; }
        }

        public event Action OnWindowBeforeClosedEvent
        {
            add { _windowBeforeClosedHandler += value; _windowBeforeClosedSnapshot = null; }
            remove { _windowBeforeClosedHandler -= value; _windowBeforeClosedSnapshot = null; }
        }

        public event Action OnWindowAfterClosedEvent
        {
            add { _windowAfterClosedHandler += value; _windowAfterClosedSnapshot = null; }
            remove { _windowAfterClosedHandler -= value; _windowAfterClosedSnapshot = null; }
        }

        public event Action OnWindowDestroyEvent
        {
            add { _windowDestroyHandler += value; _windowDestroySnapshot = null; }
            remove { _windowDestroyHandler -= value; _windowDestroySnapshot = null; }
        }

        public GameObject Target => _target ??= gameObject;

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= Target.transform as RectTransform;

        private IUITransitionSource TransitionSource => _hasTransitionOverride
            ? _transitionSource : _transitionPlayerComponent as IUITransitionSource;

        internal UIService Service => _owner != null ? _owner.Service : null;

        public virtual void Awake()
        {
            _target = gameObject;
        }

        internal void SetOwner(UIBase owner) => _owner = owner;

        internal void InvokeWindowInit() => InvokeEvent(_windowInitHandler, ref _windowInitSnapshot);
        internal void InvokeWindowBeforeShow() => InvokeEvent(_windowBeforeShowHandler, ref _windowBeforeShowSnapshot);
        internal void InvokeWindowAfterShow() => InvokeEvent(_windowAfterShowHandler, ref _windowAfterShowSnapshot);
        internal void InvokeWindowBeforeClosed() => InvokeEvent(_windowBeforeClosedHandler, ref _windowBeforeClosedSnapshot);
        internal void InvokeWindowAfterClosed() => InvokeEvent(_windowAfterClosedHandler, ref _windowAfterClosedSnapshot);
        internal void InvokeWindowDestroy() => InvokeEvent(_windowDestroyHandler, ref _windowDestroySnapshot);

        private static void InvokeEvent(Action handler, ref Action[] snapshot)
        {
            if (handler == null) return;
            Action[] callbacks = snapshot;
            if (callbacks == null)
            {
                Delegate[] handlers = handler.GetInvocationList();
                callbacks = new Action[handlers.Length];
                for (int i = 0; i < handlers.Length; i++) callbacks[i] = (Action)handlers[i];
                snapshot = callbacks;
            }

            for (int i = 0; i < callbacks.Length; i++)
            {
                try { callbacks[i](); }
                catch (Exception error) { Log.Exception(error); }
            }
        }

        public bool IsValid()
        {
            return this != null && _isAlive;
        }

        public void SetTransition(IUITransitionSource source)
        {
            _hasTransitionOverride = true;
            _transitionSource = source;
            CancelTransitionPlay();
        }

        public void SetTransition(Func<bool, CancellationToken, UniTask> play, Action<bool> snap)
        {
            SetTransition(new UIDelegateTransitionSource(play, snap));
        }

        internal bool HasTransition => TransitionSource != null;

        internal UniTask PlayOpenTransitionAsync() => PlayTransition(true);
        internal UniTask PlayCloseTransitionAsync() => PlayTransition(false);
        internal void ApplyTransitionState(bool open) => SnapTransition(open);

        /// <summary>由 <see cref="UIService"/> 的 Tick 推进；返回 true 表示本次播放已结束（成功或异常）。</summary>
        internal bool AdvanceTransition(float deltaTime)
        {
            ITickTransitionSource tick = _tickTransition;
            if (tick == null) return true;

            bool finished;
            try { finished = tick.Advance(deltaTime); }
            catch (Exception error)
            {
                Log.Exception(error);
                ApplySnapshot(tick, _tickTransitionOpen);
                finished = true;
            }

            if (!finished) return false;

            _tickTransition = null;
            UniTaskCompletionSource completion = _tickCompletion;
            _tickCompletion = null;
            Service?.UnregisterTransition(this);
            completion?.TrySetResult();
            return true;
        }

        internal void ResetRuntimeTransition()
        {
            CancelTransitionPlay();
            _hasTransitionOverride = false;
            _transitionSource = null;
        }

        internal void StopTransition() => CancelTransitionPlay();

        private UniTask PlayTransition(bool open)
        {
            if (!_isAlive || this == null) return UniTask.CompletedTask;

            // 新播放接管旧播放：任何未结束的旧转场先取消，避免遗留 CTS/播放循环。
            CancelTransitionPlay();

            IUITransitionSource source = TransitionSource;
            if (source == null) return UniTask.CompletedTask;

            UIService service = Service;
            if (source is ITickTransitionSource tick && service != null)
            {
                bool started;
                try { started = tick.Begin(open); }
                catch (Exception error)
                {
                    Log.Exception(error);
                    ApplySnapshot(tick, open);
                    return UniTask.CompletedTask;
                }
                if (!started) return UniTask.CompletedTask;
                _tickTransition = tick;
                _tickTransitionOpen = open;
                _tickCompletion = new UniTaskCompletionSource();
                service.RegisterTransition(this);
                return _tickCompletion.Task;
            }

            _transitionCts = new CancellationTokenSource();
            return PlayTransitionAsync(source, open, _transitionCts);
        }

        private async UniTask PlayTransitionAsync(
            IUITransitionSource source,
            bool open,
            CancellationTokenSource cancellation)
        {
            CancellationToken cancellationToken = cancellation.Token;
            try
            {
                await source.Play(open, cancellationToken);
            }
            catch (OperationCanceledException error) when (
                cancellationToken.IsCancellationRequested && error.CancellationToken == cancellationToken)
            {
            }
            catch (Exception error)
            {
                Log.Exception(error);
                if (!cancellationToken.IsCancellationRequested) ApplySnapshot(source, open);
            }
            finally
            {
                if (_transitionCts == cancellation) _transitionCts = null;
                cancellation.Dispose();
            }
        }

        private void SnapTransition(bool open)
        {
            if (!_isAlive || this == null) return;

            CancelTransitionPlay();
            ApplySnapshot(TransitionSource, open);
        }

        private void CancelTransitionPlay()
        {
            ITickTransitionSource tick = _tickTransition;
            if (tick != null)
            {
                _tickTransition = null;
                UniTaskCompletionSource completion = _tickCompletion;
                _tickCompletion = null;
                Service?.UnregisterTransition(this);
                completion?.TrySetResult();
            }

            CancellationTokenSource cancellation = _transitionCts;
            _transitionCts = null;
            if (cancellation != null) Cancel(cancellation);
        }

        internal static void Cancel(CancellationTokenSource cancellation) => cancellation.Cancel();

        private static void ApplySnapshot(IUITransitionSource source, bool open)
        {
            try { source?.Snap(open); }
            catch (Exception error) { Log.Exception(error); }
        }

        private void OnDestroy()
        {
            _isAlive = false;
            ResetRuntimeTransition();
            _owner = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RefreshTransitionPlayerCacheInEditor();
        }

        internal void RefreshTransitionPlayerCacheInEditor()
        {
            if (Application.isPlaying)
            {
                return;
            }

            Component transitionPlayer = FindTransitionPlayerInEditor();
            if (_transitionPlayerComponent == transitionPlayer)
            {
                return;
            }

            _transitionPlayerComponent = transitionPlayer;
        }

        internal Component FindTransitionPlayerInEditor()
        {
            Transform root = transform;
            Component transitionPlayer = FindTransitionPlayerOnObjectInEditor(root);
            if (transitionPlayer != null)
            {
                return transitionPlayer;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                transitionPlayer = FindTransitionPlayerInChildScopeInEditor(root.GetChild(i));
                if (transitionPlayer != null)
                {
                    return transitionPlayer;
                }
            }

            return null;
        }

        private static Component FindTransitionPlayerInChildScopeInEditor(Transform transform)
        {
            if (transform.GetComponent<UIHolderObjectBase>() != null)
            {
                return null;
            }

            Component transitionPlayer = FindTransitionPlayerOnObjectInEditor(transform);
            if (transitionPlayer != null)
            {
                return transitionPlayer;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                transitionPlayer = FindTransitionPlayerInChildScopeInEditor(transform.GetChild(i));
                if (transitionPlayer != null)
                {
                    return transitionPlayer;
                }
            }

            return null;
        }

        private static Component FindTransitionPlayerOnObjectInEditor(Transform transform)
        {
            Component[] components = transform.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IUITransitionSource)
                {
                    return components[i];
                }
            }

            return null;
        }
#endif
    }
}
