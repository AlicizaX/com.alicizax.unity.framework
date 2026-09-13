using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    [DisallowMultipleComponent]
    public abstract class UIHolderObjectBase : MonoBehaviour
    {
        public event Action OnWindowInitEvent;
        public event Action OnWindowBeforeShowEvent;
        public event Action OnWindowAfterShowEvent;
        public event Action OnWindowBeforeClosedEvent;
        public event Action OnWindowAfterClosedEvent;
        public event Action OnWindowDestroyEvent;

        private UIBase _owner;

        private GameObject _target;
        [SerializeField, HideInInspector] private Component _transitionPlayerComponent;
        private IUITransitionSource _transitionSource;
        private bool _hasTransitionOverride;
        private CancellationTokenSource _transitionCts;
        public GameObject Target => _target ??= gameObject;

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= Target.transform as RectTransform;

        private IUITransitionSource TransitionSource => _hasTransitionOverride
            ? _transitionSource : _transitionPlayerComponent as IUITransitionSource;

        public virtual void Awake()
        {
            _target = gameObject;
        }

        internal void BindOwner(UIBase owner)
        {
            _owner = owner;
        }

        internal void UnbindOwner()
        {
            _owner = null;
            ResetRuntimeTransition();
        }

        internal void InvokeWindowInit() => InvokeEvent(OnWindowInitEvent);
        internal void InvokeWindowBeforeShow() => InvokeEvent(OnWindowBeforeShowEvent);
        internal void InvokeWindowAfterShow() => InvokeEvent(OnWindowAfterShowEvent);
        internal void InvokeWindowBeforeClosed() => InvokeEvent(OnWindowBeforeClosedEvent);
        internal void InvokeWindowAfterClosed() => InvokeEvent(OnWindowAfterClosedEvent);
        internal void InvokeWindowDestroy() => InvokeEvent(OnWindowDestroyEvent);

        private static void InvokeEvent(Action callbacks)
        {
            if (callbacks == null) return;
            foreach (Action callback in callbacks.GetInvocationList())
            {
                try { callback(); }
                catch (Exception error) { Log.Exception(error); }
            }
        }

        private bool _isAlive = true;

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

        internal UniTask PlayOpenTransitionAsync()
        {
            return PlayTransition(true);
        }

        internal UniTask PlayCloseTransitionAsync()
        {
            return PlayTransition(false);
        }

        internal void ApplyTransitionState(bool open) => SnapTransition(open);

        internal void ResetRuntimeTransition()
        {
            CancellationTokenSource cancellation = _transitionCts;
            _transitionCts = null;
            _hasTransitionOverride = false;
            _transitionSource = null;
            if (cancellation != null) UIBase.CancelLoad(cancellation);
        }

        internal void StopTransition()
        {
            CancelTransitionPlay();
        }

        private UniTask PlayTransition(bool open)
        {
            if (!_isAlive || this == null)
            {
                return UniTask.CompletedTask;
            }

            IUITransitionSource source = TransitionSource;
            if (source == null)
            {
                return UniTask.CompletedTask;
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
            if (!_isAlive || this == null)
            {
                return;
            }

            CancelTransitionPlay();
            ApplySnapshot(TransitionSource, open);
        }

        private void CancelTransitionPlay()
        {
            CancellationTokenSource cancellation = _transitionCts;
            _transitionCts = null;
            if (cancellation != null) UIBase.CancelLoad(cancellation);
        }

        private static void ApplySnapshot(IUITransitionSource source, bool open)
        {
            try { source?.Snap(open); }
            catch (Exception error) { Log.Exception(error); }
        }

        private void OnDestroy()
        {
            _isAlive = false;
            _owner = null;
            ResetRuntimeTransition();
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
