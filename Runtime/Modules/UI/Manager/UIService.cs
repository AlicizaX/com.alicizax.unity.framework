using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Timer.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService : ServiceBase, IUIService,
#if UNITY_EDITOR
        IUIDebugService,
#endif
        IServiceTickable
    {
        private readonly Dictionary<RuntimeTypeHandle, UIWindow> _windows = new(RuntimeTypeHandleComparer.Instance);
        private readonly List<UIBase> _updateViews = new(16);
        private readonly Action<UIWindow> _onCacheExpired;
        private readonly ITimerService _timerService;
        private readonly UIBlocker _blocker;
        private readonly Navigator _navigator;
        private UIBase[] _updateBuffer = new UIBase[16];
        private bool _stopped;

        /// <summary>在 UI 根节点上建立层级与输入遮挡层；根节点下没有带 worldCamera 的 Canvas 时抛出异常。</summary>
        internal UIService(Transform root, ITimerService timerService)
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null) throw new InvalidOperationException("[UI] UI root must contain a Canvas.");
            if (canvas.worldCamera == null) throw new InvalidOperationException("[UI] UI Canvas must have a worldCamera.");
            UIRoot = root;
            UICanvas = canvas;
            UICamera = canvas.worldCamera;
            UICanvasRoot = canvas.transform;
            root.position = new Vector3(UI_ROOT_OFFSET, UI_ROOT_OFFSET, 0);
            for (int i = LayerCount - 1; i >= 0; i--) _layers[i] = CreateLayer(i);
            _timerService = timerService;
            _onCacheExpired = OnCacheExpired;
            _blocker = new UIBlocker(timerService, UICanvasRoot);
            _navigator = new Navigator(this);
        }

        /// <summary>结束进行中的导航，销毁全部窗口与 UI 根节点。销毁钩子里再打开窗口会被拒绝。</summary>
        protected override void OnDispose()
        {
            _stopped = true;
            _navigator.Stop();
            var windows = new UIWindow[_windows.Count];
            _windows.Values.CopyTo(windows, 0);
            for (int i = 0; i < windows.Length; i++)
                if (windows[i].State != UIState.Destroyed) DestroyWindow(windows[i]);
            _blocker.Dispose();
            if (UIRoot != null) UIRoot.gameObject.SafeDestroySelf();
        }

        internal void AddUpdate(UIBase view) => _updateViews.Add(view);

        internal void RemoveUpdate(UIBase view) => _updateViews.Remove(view);

        /// <summary>驱动 Update 生命周期。O(U)，U 为启用中的 Update 视图数；零分配。</summary>
        void IServiceTickable.Tick(float deltaTime)
        {
            int count = _updateViews.Count;
            if (_updateBuffer.Length < count) _updateBuffer = new UIBase[Mathf.NextPowerOfTwo(count)];
            _updateViews.CopyTo(_updateBuffer);
            for (int i = 0; i < count; i++)
            {
                UIBase view = _updateBuffer[i];
                _updateBuffer[i] = null;
                if (view.IsEnabled) view.Update(deltaTime);
            }
        }

        public UniTask<T> ShowUI<T>(object userData = null) where T : UIWindow =>
            ShowUI<T>(CancellationToken.None, userData);

        public UniTask<T> ShowUI<T>(CancellationToken cancellationToken, object userData = null) where T : UIWindow =>
            ShowAsync<T>(UIMetaRegistry.Require(typeof(T)), userData, cancellationToken);

        public UniTask<UIWindow> ShowUI(string type, object userData = null) =>
            ShowUI(type, CancellationToken.None, userData);

        public UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, object userData = null) =>
            ShowAsync<UIWindow>(UIMetaRegistry.Require(type), userData, cancellationToken);

        public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, object userData = null) =>
            ShowUI(handle, CancellationToken.None, userData);

        public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, object userData = null) =>
            ShowAsync<UIWindow>(UIMetaRegistry.Require(handle), userData, cancellationToken);

        public T ShowUISync<T>(object userData = null) where T : UIWindow
        {
            UIWindow window = ShowWindow(UIMetaRegistry.Require(typeof(T)), userData, sync: true);
            return window != null ? ShownOrNull<T>(window) : null;
        }

        public UIViewTransition CloseUI<T>(bool force = false, bool snap = false) where T : UIWindow =>
            CloseUI(typeof(T).TypeHandle, force, snap);

        public UIViewTransition CloseUI(RuntimeTypeHandle handle, bool force = false, bool snap = false)
        {
            UIWindow window = GetWindow(handle);
            if (window != null) return CloseWindow(window, force, snap);
            Log.Warning("[UI] Close ignored because the window is not open and not cached.");
            return default;
        }

        public T GetUI<T>() where T : UIWindow
        {
            UIWindow window = GetWindow(typeof(T).TypeHandle);
            return window != null && window.State is UIState.Opening or UIState.Opened or UIState.Closed ? (T)window : null;
        }

        public bool IsOpen<T>() where T : UIWindow => IsOpen(typeof(T).TypeHandle);

        public bool IsOpen(RuntimeTypeHandle handle)
        {
            UIWindow window = GetWindow(handle);
            return window != null && window.State is UIState.Opening or UIState.Opened;
        }

        internal UIWindow GetWindow(RuntimeTypeHandle handle) =>
            _windows.TryGetValue(handle, out UIWindow window) ? window : null;
    }
}
