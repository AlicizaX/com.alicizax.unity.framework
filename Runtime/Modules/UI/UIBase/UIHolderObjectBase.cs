using System;
using System.Collections.Generic;
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
        private IUITransitionPlayer _transitionPlayer;
        public GameObject Target => _target ??= gameObject;

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= Target.transform as RectTransform;

        private IUITransitionPlayer TransitionPlayer
        {
            get
            {
                if (_transitionPlayerComponent == null)
                {
                    return null;
                }

                return _transitionPlayer ??= _transitionPlayerComponent as IUITransitionPlayer;
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
            _transitionPlayer = _transitionPlayerComponent as IUITransitionPlayer;
        }

        private bool _isAlive = true;

        public bool IsValid()
        {
            return this != null && _isAlive;
        }

        internal UniTask PlayOpenTransitionAsync()
        {
            if (!_isAlive || this == null)
            {
                return UniTask.CompletedTask;
            }

            IUITransitionPlayer transitionPlayer = TransitionPlayer;
            return transitionPlayer != null
                ? transitionPlayer.PlayOpenAsync()
                : UniTask.CompletedTask;
        }

        internal UniTask PlayCloseTransitionAsync()
        {
            if (!_isAlive || this == null)
            {
                return UniTask.CompletedTask;
            }

            IUITransitionPlayer transitionPlayer = TransitionPlayer;
            return transitionPlayer != null
                ? transitionPlayer.PlayCloseAsync()
                : UniTask.CompletedTask;
        }

        internal void ApplyOpenTransitionState()
        {
            if (!_isAlive || this == null)
            {
                return;
            }

            IUITransitionPlayer transitionPlayer = TransitionPlayer;
            transitionPlayer?.ApplyOpenState();
        }

        internal void ApplyClosedTransitionState()
        {
            if (!_isAlive || this == null)
            {
                return;
            }

            IUITransitionPlayer transitionPlayer = TransitionPlayer;
            transitionPlayer?.ApplyClosedState();
        }

        internal void StopTransition()
        {
            if (!_isAlive || this == null)
            {
                return;
            }

            IUITransitionPlayer transitionPlayer = TransitionPlayer;
            if (transitionPlayer != null)
            {
                transitionPlayer.Stop();
            }
        }

        private void OnDestroy()
        {
            _isAlive = false;
            _transitionPlayer = null;
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
            _transitionPlayer = transitionPlayer as IUITransitionPlayer;
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
                if (components[i] is IUITransitionPlayer)
                {
                    return components[i];
                }
            }

            return null;
        }
#endif
    }
}
