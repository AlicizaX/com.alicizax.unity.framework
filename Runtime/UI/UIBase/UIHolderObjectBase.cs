using System;
using System.Collections.Generic;
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
        private IUITransitionPlayer _transitionPlayer;
        public GameObject Target => _target ??= gameObject;

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= Target.transform as RectTransform;

        public bool Visible
        {
            get => Target.activeSelf;
            internal set => Target.SetActive(value);
        }

        public virtual void Awake()
        {
            _target = gameObject;
        }

        private bool _isAlive = true;

        public bool IsValid()
        {
            return this != null && _isAlive;
        }

        internal UniTask PlayOpenTransitionAsync(CancellationToken cancellationToken = default)
        {
            return TryGetTransitionPlayer(out IUITransitionPlayer transitionPlayer)
                ? transitionPlayer.PlayOpenAsync(cancellationToken)
                : UniTask.CompletedTask;
        }

        internal UniTask PlayCloseTransitionAsync(CancellationToken cancellationToken = default)
        {
            return TryGetTransitionPlayer(out IUITransitionPlayer transitionPlayer)
                ? transitionPlayer.PlayCloseAsync(cancellationToken)
                : UniTask.CompletedTask;
        }

        internal void StopTransition()
        {
            if (TryGetTransitionPlayer(out IUITransitionPlayer transitionPlayer))
            {
                transitionPlayer.Stop();
            }
        }

        private bool TryGetTransitionPlayer(out IUITransitionPlayer transitionPlayer)
        {
            if (_transitionPlayer is Behaviour cachedBehaviour && cachedBehaviour.isActiveAndEnabled)
            {
                transitionPlayer = _transitionPlayer;
                return true;
            }

            if (!_isAlive || this == null)
            {
                transitionPlayer = null;
                return false;
            }

            _transitionPlayer = GetComponent<IUITransitionPlayer>();
            transitionPlayer = _transitionPlayer;
            return transitionPlayer != null;
        }

        private void OnDestroy()
        {
            _isAlive = false;
            _transitionPlayer = null;
        }
    }
}
