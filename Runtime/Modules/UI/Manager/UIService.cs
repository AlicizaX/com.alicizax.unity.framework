using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Timer.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService : ServiceBase, IUIService,
#if UNITY_EDITOR
        IUIDebugService,
#endif
        IServiceTickable
    {
        internal readonly IUIResourceLoader ResourceLoader;
        private readonly Dictionary<RuntimeTypeHandle, UIWindowRecord> _windows = new(RuntimeTypeHandleComparer.Instance);
        private readonly List<UIWindow> _updating = new(8);
        private readonly List<UIWindow> _tickBuffer = new(8);
        private readonly List<UIHolderObjectBase> _transitions = new(8);
        private readonly List<UIHolderObjectBase> _transitionBuffer = new(8);
        private UIWindowCache _cache;
        private UIBlocker _blocker;
        private ITimerService _timerService;
        private bool _initialized;
        private bool _shuttingDown;

        public UIService() : this(new UIResourceLoader()) { }
        internal UIService(IUIResourceLoader resourceLoader) => ResourceLoader = resourceLoader;

        protected override void OnDestroyService() => DestroyAllManagedUI();

        private UIWindowRecord GetWindowRecord(RuntimeTypeHandle handle)
        {
            if (_windows.TryGetValue(handle, out UIWindowRecord record)) return record;
            if (_shuttingDown || !_initialized)
            {
                Log.Error("[UI] UIService is not running.");
                return null;
            }
            UIMetadata metadata = UIMetadata.Create(Type.GetTypeFromHandle(handle));
            if (metadata == null) return null;
            record = new UIWindowRecord(metadata);
            _windows.Add(handle, record);
            return record;
        }

        private UIWindowRecord TryGetWindowRecord(RuntimeTypeHandle handle) =>
            _windows.TryGetValue(handle, out var record) ? record : null;

        private static bool IsValidWindowHandle(RuntimeTypeHandle handle)
        {
            if (handle.Value == IntPtr.Zero)
            {
                Log.Error("[UI] Window type handle is empty.");
                return false;
            }
            Type type = Type.GetTypeFromHandle(handle);
            if (type == null || !typeof(UIWindow).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
            {
                Log.Error("[UI] Unknown or invalid window type: {0}.", type);
                return false;
            }
            return true;
        }

        internal void SetUpdating(UIWindow view, bool enabled)
        {
            if (enabled)
            {
                if (!_updating.Contains(view)) _updating.Add(view);
            }
            else _updating.Remove(view);
        }

        internal void RegisterTransition(UIHolderObjectBase holder)
        {
            if (!_transitions.Contains(holder)) _transitions.Add(holder);
        }

        internal void UnregisterTransition(UIHolderObjectBase holder) => _transitions.Remove(holder);

        void IServiceTickable.Tick(float deltaTime)
        {
            // 帧初冻结两份快照：本帧 update 中新建的窗口/转场从下一帧开始推进，
            // 避免“刚打开就在同一帧被推进一帧”的时间差。
            _tickBuffer.Clear();
            _tickBuffer.AddRange(_updating);
            _transitionBuffer.Clear();
            _transitionBuffer.AddRange(_transitions);

            for (int i = 0; i < _tickBuffer.Count; i++) _tickBuffer[i].InternalUpdate(deltaTime);
            for (int i = 0; i < _transitionBuffer.Count; i++) _transitionBuffer[i].AdvanceTransition(deltaTime);
        }

        public UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIWindow =>
            ShowUI<T>(CancellationToken.None, userDatas);

        public UniTask<T> ShowUI<T>(CancellationToken cancellationToken, params object[] userDatas) where T : UIWindow =>
            GetView<T>(RequestShow(GetWindowRecord(typeof(T).TypeHandle), userDatas, cancellationToken));

        public UniTask<UIWindow> ShowUI(string type, params object[] userDatas) =>
            ShowUI(type, CancellationToken.None, userDatas);

        public UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, params object[] userDatas)
        {
            if (UIMetaRegistry.TryGet(type, out var metadata))
                return ShowUI(metadata.RuntimeTypeHandle, cancellationToken, userDatas);
            Log.Error("[UI] Unknown UI type: {0}.", type);
            return UniTask.FromResult<UIWindow>(null);
        }

        public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, params object[] userDatas) =>
            ShowUI(handle, CancellationToken.None, userDatas);

        public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, params object[] userDatas)
        {
            if (!IsValidWindowHandle(handle)) return UniTask.FromResult<UIWindow>(null);
            return GetView<UIWindow>(RequestShow(GetWindowRecord(handle), userDatas, cancellationToken));
        }

        private static async UniTask<T> GetView<T>(UniTask<UIOpenResult> task) where T : UIWindow
        {
            UIOpenResult result = await task;
            return (T)result.View;
        }

        public T ShowUISync<T>(params object[] userDatas) where T : UIWindow =>
            (T)ShowUISyncCore(GetWindowRecord(typeof(T).TypeHandle), userDatas);

        public UniTask CloseUI<T>(bool force = false, bool skipTransition = false) where T : UIWindow =>
            CloseUI(typeof(T).TypeHandle, force, skipTransition);

        public UniTask CloseUI(RuntimeTypeHandle handle, bool force = false, bool skipTransition = false) =>
            RequestClose(TryGetWindowRecord(handle), force, skipTransition, affectNavigation: true);

        internal UniTask CloseWindow(UIWindow view, bool force = false, bool skipTransition = false,
            bool affectNavigation = true, CancellationToken token = default)
        {
            UIWindowRecord record = TryGetWindowRecord(view.GetType().TypeHandle);
            if (record == null || record.View != view)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return UniTask.CompletedTask;
            }
            return RequestClose(record, force, skipTransition, affectNavigation, token);
        }

        public T GetUI<T>() where T : UIWindow
        {
            UIWindowRecord record = TryGetWindowRecord(typeof(T).TypeHandle);
            UIWindow view = record?.View;
            if (view == null) return null;
            if (record.IsCached) return (T)view;
            return view.State is UIState.Opening or UIState.Opened ? (T)view : null;
        }

        public bool IsOpen<T>() where T : UIWindow => IsOpen(typeof(T).TypeHandle);
        public bool IsOpen(RuntimeTypeHandle handle) => TryGetWindowRecord(handle)?.View?.IsOpen ?? false;
        internal UIWindow GetWindow(RuntimeTypeHandle handle) => TryGetWindowRecord(handle)?.View;

        internal void NotifyWindowClosed(UIWindow view)
        {
            UIWindowRecord record = TryGetWindowRecord(view.GetType().TypeHandle);
            if (record != null && record.View == view) FinalizeAfterClosed(record, view);
        }

        internal void NotifyWindowDestroying(UIWindow view)
        {
            UIWindowRecord record = TryGetWindowRecord(view.GetType().TypeHandle);
            if (record != null && record.View == view)
            {
                RemoveFromOpenStack(record);
                _cache.Remove(record);
                record.View = null;
            }
            if (_currentView == view) ClearHistory();
        }

        private void DestroyAllManagedUI()
        {
            _shuttingDown = true;
            StopNavigation();
            foreach (UIWindowRecord record in _windows.Values)
            {
                CancelWindowLoad(record);
                record.View?.DestroyNow();
            }
            _windows.Clear();
            _cache.Clear();
            _blocker?.Dispose();
            _blocker = null;
            _updating.Clear();
            _tickBuffer.Clear();
            _transitions.Clear();
            _transitionBuffer.Clear();
            for (int i = 0; i < _openUI.Length; i++) _openUI[i] = null;
            DestroyUIRoot();
            _initialized = false;
        }

        private void DestroyUIRoot()
        {
            if (UIRoot != null)
            {
                if (Application.isPlaying) Object.Destroy(UIRoot.gameObject);
                else Object.DestroyImmediate(UIRoot.gameObject);
            }
            UIRoot = null;
            UICanvas = null;
            UICanvasRoot = null;
            UICamera = null;
            _cacheLayer = null;
            for (int i = 0; i < _layers.Length; i++) _layers[i] = null;
        }
    }
}
