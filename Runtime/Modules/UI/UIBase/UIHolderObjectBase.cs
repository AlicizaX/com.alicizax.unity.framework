using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    [DisallowMultipleComponent]
    public abstract class UIHolderObjectBase : MonoBehaviour
    {
        public Action OnWindowInitEvent;
        public Action OnWindowBeforeShowEvent;
        public Action OnWindowAfterShowEvent;
        public Action OnWindowBeforeClosedEvent;
        public Action OnWindowAfterClosedEvent;
        public Action OnWindowDestroyEvent;

        private GameObject _target;
        [SerializeField, HideInInspector] private Component _transitionPlayerComponent;
        private IUITransitionSource _transitionSource;
        private CancellationTokenSource _transitionCts;
        public GameObject Target => _target ??= gameObject;

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= Target.transform as RectTransform;

        private IUITransitionSource TransitionSource
        {
            get
            {
                if (_transitionSource != null)
                {
                    return _transitionSource;
                }

                if (_transitionPlayerComponent == null)
                {
                    return null;
                }

                return _transitionSource = _transitionPlayerComponent as IUITransitionSource;
            }
        }

        public bool Visible
        {
            get => Target.activeSelf;
            internal set => Target.SetActive(value);
        }

        public virtual void Awake()
        {
            _target = gameObject;
            if (_transitionSource == null)
            {
                _transitionSource = _transitionPlayerComponent as IUITransitionSource;
            }
        }

        private bool _isAlive = true;

        public bool IsValid()
        {
            return this != null && _isAlive;
        }

        public void SetTransition(IUITransitionSource source)
        {
            CancelTransitionPlay();
            _transitionSource = source;
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

        internal void ApplyClosedTransitionState()
        {
            SnapTransition(false);
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

            CancellationToken cancellationToken = RestartTransitionPlay();
            return PlayTransitionGuarded(source, open, cancellationToken);
        }

        private async UniTask PlayTransitionGuarded(
            IUITransitionSource source,
            bool open,
            CancellationToken cancellationToken)
        {
            try
            {
                await source.Play(open, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void SnapTransition(bool open)
        {
            if (!_isAlive || this == null)
            {
                return;
            }

            CancelTransitionPlay();
            TransitionSource?.Snap(open);
        }

        private CancellationToken RestartTransitionPlay()
        {
            if (_transitionCts != null)
            {
                if (!_transitionCts.IsCancellationRequested)
                    return _transitionCts.Token;

                _transitionCts.Dispose();
                _transitionCts = null;
            }

            _transitionCts = new CancellationTokenSource();
            return _transitionCts.Token;
        }

        private void CancelTransitionPlay()
        {
            if (_transitionCts == null || _transitionCts.IsCancellationRequested)
                return;

            _transitionCts.Cancel();
        }

        private void OnDestroy()
        {
            _isAlive = false;
            CancelTransitionPlay();
            if (_transitionCts != null)
            {
                _transitionCts.Dispose();
                _transitionCts = null;
            }
            _transitionSource = null;
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
            _transitionSource = transitionPlayer as IUITransitionSource;
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
