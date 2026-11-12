using System;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 节点：持有转场与四个生命周期事件。何时播放转场由绑定的视图决定；节点被外部销毁时通知绑定的视图。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIHolderObjectBase : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private Component _defaultTransition;
        private IUITransition _transitionOverride;
        private RectTransform _rectTransform;
        private UIBase _owner;

        /// <summary>绑定的逻辑视图及其父级是否启用，与 MonoBehaviour.enabled 无关。</summary>
        public bool IsEnabled => _owner != null && _owner.IsEnabled;

        public event Action OnInitEvent;

        public event Action OnShowEvent;

        public event Action OnCloseEvent;

        public event Action OnDestroyEvent;

        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform is null) _rectTransform = (RectTransform)transform;
                return _rectTransform;
            }
        }

        /// <summary>当前转场：运行时设置的优先，其次是序列化的默认转场。</summary>
        public IUITransition UITransition => _transitionOverride ?? _defaultTransition as IUITransition;

        /// <summary>设置运行时转场；传 null 恢复序列化的默认转场。</summary>
        public void SetTransition(IUITransition transition) => _transitionOverride = transition;

        internal void Bind(UIBase owner) => _owner = owner;

        internal bool TryUnbind()
        {
            if (_owner == null) return false;
            _owner = null;
            return true;
        }

        internal void Raise(UILifecycle stage)
        {
            Action handler = stage switch
            {
                UILifecycle.Initialize => OnInitEvent,
                UILifecycle.Open => OnShowEvent,
                UILifecycle.Close => OnCloseEvent,
                UILifecycle.Destroy => OnDestroyEvent,
                _ => null,
            };
            if (handler == null) return;
            try
            {
                handler();
            }
            catch (Exception error)
            {
                Log.Exception(error);
            }
        }

        private void OnDestroy()
        {
            if (_owner == null) return;
            UIBase owner = _owner;
            _owner = null;
            owner.DestroySelf();
        }
    }
}
