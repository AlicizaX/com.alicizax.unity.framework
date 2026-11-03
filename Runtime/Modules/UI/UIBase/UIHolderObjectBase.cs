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
        private int _transitionRound;
        private bool _tickTransitionOpen;
        private bool _isAlive = true;

        private Action[] _windowInitCallbacks = Array.Empty<Action>();
        private Action[] _windowBeforeShowCallbacks = Array.Empty<Action>();
        private Action[] _windowAfterShowCallbacks = Array.Empty<Action>();
        private Action[] _windowBeforeClosedCallbacks = Array.Empty<Action>();
        private Action[] _windowAfterClosedCallbacks = Array.Empty<Action>();
        private Action[] _windowDestroyCallbacks = Array.Empty<Action>();

        public event Action OnWindowInitEvent
        {
            add => AddCallbacks(ref _windowInitCallbacks, value);
            remove => RemoveCallbacks(ref _windowInitCallbacks, value);
        }

        public event Action OnWindowBeforeShowEvent
        {
            add => AddCallbacks(ref _windowBeforeShowCallbacks, value);
            remove => RemoveCallbacks(ref _windowBeforeShowCallbacks, value);
        }

        public event Action OnWindowAfterShowEvent
        {
            add => AddCallbacks(ref _windowAfterShowCallbacks, value);
            remove => RemoveCallbacks(ref _windowAfterShowCallbacks, value);
        }

        public event Action OnWindowBeforeClosedEvent
        {
            add => AddCallbacks(ref _windowBeforeClosedCallbacks, value);
            remove => RemoveCallbacks(ref _windowBeforeClosedCallbacks, value);
        }

        public event Action OnWindowAfterClosedEvent
        {
            add => AddCallbacks(ref _windowAfterClosedCallbacks, value);
            remove => RemoveCallbacks(ref _windowAfterClosedCallbacks, value);
        }

        public event Action OnWindowDestroyEvent
        {
            add => AddCallbacks(ref _windowDestroyCallbacks, value);
            remove => RemoveCallbacks(ref _windowDestroyCallbacks, value);
        }

        public GameObject Target => _target ??= gameObject;

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= Target.transform as RectTransform;

        private IUITransitionSource TransitionSource => _hasTransitionOverride
            ? _transitionSource : _transitionPlayerComponent as IUITransitionSource;

        internal bool UsesTickTransition => TransitionSource is ITickTransitionSource;

        public virtual void Awake()
        {
            _target = gameObject;
        }

        internal void SetOwner(UIBase owner) => _owner = owner;

        internal void InvokeWindowInit() => InvokeCallbacks(_windowInitCallbacks);
        internal void InvokeWindowBeforeShow() => InvokeCallbacks(_windowBeforeShowCallbacks);
        internal void InvokeWindowAfterShow() => InvokeCallbacks(_windowAfterShowCallbacks);
        internal void InvokeWindowBeforeClosed() => InvokeCallbacks(_windowBeforeClosedCallbacks);
        internal void InvokeWindowAfterClosed() => InvokeCallbacks(_windowAfterClosedCallbacks);
        internal void InvokeWindowDestroy() => InvokeCallbacks(_windowDestroyCallbacks);

        private static void AddCallbacks(ref Action[] callbacks, Action value)
        {
            if (value == null) return;
            Delegate[] added = value.GetInvocationList();
            var merged = new Action[callbacks.Length + added.Length];
            Array.Copy(callbacks, merged, callbacks.Length);
            for (int i = 0; i < added.Length; i++) merged[callbacks.Length + i] = (Action)added[i];
            callbacks = merged;
        }

        private static void RemoveCallbacks(ref Action[] callbacks, Action value)
        {
            if (value == null) return;
            Delegate[] removed = value.GetInvocationList();
            for (int i = removed.Length - 1; i >= 0; i--)
            {
                int index = Array.LastIndexOf(callbacks, (Action)removed[i]);
                if (index < 0) continue;
                var remaining = new Action[callbacks.Length - 1];
                Array.Copy(callbacks, remaining, index);
                Array.Copy(callbacks, index + 1, remaining, index, remaining.Length - index);
                callbacks = remaining;
            }
        }

        private static void InvokeCallbacks(Action[] callbacks)
        {
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
            _hasTransitionOverride = source != null;
            _transitionSource = source;
            _owner?.ReserveTransitionNode();
            if (_tickTransition == null && _transitionCts == null) return;
            int round = _transitionRound + 1;
            CancelTransitionPlay();
            if (round == _transitionRound) _owner.FinishTransition();
        }

        public void SetTransition(Func<bool, CancellationToken, UniTask> play, Action<bool> snap)
        {
            SetTransition(new UIDelegateTransitionSource(play, snap));
        }

        internal bool HasTransition => TransitionSource != null;

        internal void PlayOpenTransition() => PlayTransition(true);
        internal void PlayCloseTransition() => PlayTransition(false);

        internal void ApplyTransitionState(bool open)
        {
            CancelTransitionPlay();
            ApplySnapshot(TransitionSource, open);
        }

        internal void AdvanceTransition(float deltaTime)
        {
            ITickTransitionSource tick = _tickTransition;
            int round = _transitionRound;
            bool finished;
            try { finished = tick.Advance(deltaTime); }
            catch (Exception error)
            {
                Log.Exception(error);
                if (round == _transitionRound) ApplySnapshot(tick, _tickTransitionOpen);
                finished = true;
            }
            if (!finished || round != _transitionRound) return;
            _transitionRound++;
            _tickTransition = null;
            _owner.UnlinkTransition();
            _owner.FinishTransition();
        }

        internal void ResetRuntimeTransition()
        {
            CancelTransitionPlay();
            _hasTransitionOverride = false;
            _transitionSource = null;
        }

        internal void StopTransition() => CancelTransitionPlay();

        private void PlayTransition(bool open)
        {
            IUITransitionSource source = TransitionSource;
            if (source == null) _owner.FinishTransition();
            else if (source is ITickTransitionSource tick) PlayTickTransition(tick, open);
            else
            {
                var cancellation = new CancellationTokenSource();
                _transitionCts = cancellation;
                RunTransition(source, open, cancellation, ++_transitionRound).Forget();
            }
        }

        private void PlayTickTransition(ITickTransitionSource tick, bool open)
        {
            int round = ++_transitionRound;
            bool started;
            try { started = tick.Begin(open); }
            catch (Exception error)
            {
                Log.Exception(error);
                if (round == _transitionRound) ApplySnapshot(tick, open);
                started = false;
            }
            if (round != _transitionRound) return;
            if (!started)
            {
                _owner.FinishTransition();
                return;
            }
            _tickTransition = tick;
            _tickTransitionOpen = open;
            _owner.LinkTransition();
        }

        private async UniTaskVoid RunTransition(IUITransitionSource source, bool open,
            CancellationTokenSource cancellation, int round)
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
            if (round != _transitionRound) return;
            _transitionRound++;
            _owner.FinishTransition();
        }

        private void CancelTransitionPlay()
        {
            _transitionRound++;
            if (_tickTransition != null)
            {
                _tickTransition = null;
                _owner.UnlinkTransition();
            }

            CancellationTokenSource cancellation = _transitionCts;
            _transitionCts = null;
            if (cancellation != null) cancellation.Cancel();
        }

        private static void ApplySnapshot(IUITransitionSource source, bool open)
        {
            try { source?.Snap(open); }
            catch (Exception error) { Log.Exception(error); }
        }

        private void OnDestroy()
        {
            _isAlive = false;
            _owner?.LoseHolder();
            ResetRuntimeTransition();
            _owner = null;
        }

#if UNITY_EDITOR
        internal bool HasActiveTickTransition => _tickTransition != null;

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
