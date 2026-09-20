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
        private ITimerService _timerService;
        internal readonly IUIResourceLoader ResourceLoader;
        private readonly Dictionary<RuntimeTypeHandle, UIWindowRecord> _windows = new(RuntimeTypeHandleComparer.Instance);
        private readonly List<UIWindow> _updating = new(8);
        private readonly HashSet<UIWindow> _updateMembers = new();
        private readonly HashSet<UIWindow> _pendingUpdateAdds = new();
        private bool _ticking;
        private bool _shuttingDown;

        public UIService() : this(new UIResourceLoader()) { }
        internal UIService(IUIResourceLoader resourceLoader) => ResourceLoader = resourceLoader;

        private UIWindowRecord GetWindowRecord(RuntimeTypeHandle handle)
        {
            if (_shuttingDown || !_initialized)
            {
                Log.Error("[UI] UIService is not running.");
                return null;
            }
            if (handle.Value == IntPtr.Zero)
            {
                Log.Error("[UI] Window type handle is empty.");
                return null;
            }
            if (_windows.TryGetValue(handle, out var record)) return record;
            Type type = Type.GetTypeFromHandle(handle);
            if (type == null || !typeof(UIWindow).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
            {
                Log.Error("[UI] Unknown or invalid window type: {0}.", type);
                return null;
            }
            UIMetadata metadata = UIMetadata.Create(type);
            if (metadata == null) return null;
            if ((uint)metadata.MetaInfo.UILayer >= (uint)UILayer.All)
            {
                Log.Error("[UI] Invalid window layer: {0}.", metadata.MetaInfo.UILayer);
                return null;
            }
            record = new UIWindowRecord(metadata);
            _windows.Add(handle, record);
            return record;
        }

        private UIWindowRecord TryGetWindowRecord(RuntimeTypeHandle handle) =>
            _windows.TryGetValue(handle, out var record) ? record : null;

        protected override void OnDestroyService() => DestroyAllManagedUI();

        internal void SetUpdating(UIWindow view, bool enabled)
        {
            if (enabled && !_shuttingDown)
            {
                if (_ticking) _pendingUpdateAdds.Add(view);
                else if (_updateMembers.Add(view)) _updating.Add(view);
            }
            else
            {
                _pendingUpdateAdds.Remove(view);
                if (_updateMembers.Remove(view) && !_ticking) _updating.Remove(view);
            }
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            _ticking = true;
            for (int i = 0; i < _updating.Count; i++)
                if (_updateMembers.Contains(_updating[i])) _updating[i].InternalUpdate();
            _ticking = false;
            for (int i = _updating.Count - 1; i >= 0; i--)
                if (!_updateMembers.Contains(_updating[i])) _updating.RemoveAt(i);
            foreach (UIWindow view in _pendingUpdateAdds)
                if (view.Effective && view.State == UIState.Opened && _updateMembers.Add(view))
                    _updating.Add(view);
            _pendingUpdateAdds.Clear();
        }

        public UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIWindow =>
            ShowUI<T>(CancellationToken.None, userDatas);

        public UniTask<T> ShowUI<T>(CancellationToken cancellationToken, params object[] userDatas) where T : UIWindow =>
            GetView<T>(RequestShow(GetWindowRecord(typeof(T).TypeHandle), userDatas, cancellationToken));

        public UniTask<UIWindow> ShowUI(string type, params object[] userDatas) =>
            ShowUI(type, CancellationToken.None, userDatas);

        public UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, params object[] userDatas)
        {
            if (!string.IsNullOrEmpty(type) && UIMetaRegistry.TryGet(type, out var metadata))
                return ShowUI(metadata.RuntimeTypeHandle, cancellationToken, userDatas);
            Log.Error("[UI] Unknown UI type: {0}.", type);
            return UniTask.FromResult<UIWindow>(null);
        }

        public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, params object[] userDatas) =>
            ShowUI(handle, CancellationToken.None, userDatas);

        public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, params object[] userDatas) =>
            GetView<UIWindow>(RequestShow(GetWindowRecord(handle), userDatas, cancellationToken));

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

        internal UniTask CloseWindow(UIWindow view, bool force = false, bool skipTransition = false, bool affectNavigation = true)
        {
            UIWindowRecord record = TryGetWindowRecord(view.GetType().TypeHandle);
            if (record == null || record.View != view)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return UniTask.CompletedTask;
            }
            return RequestClose(record, force, skipTransition, affectNavigation);
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
                RemoveFromCache(record);
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
            for (int i = 0; i < _openUI.Length; i++) _openUI[i] = null;
            _updating.Clear();
            _updateMembers.Clear();
            _pendingUpdateAdds.Clear();
            if (m_LastCountDownHandle != 0)
            {
                _timerService.RemoveTimer(m_LastCountDownHandle);
                m_LastCountDownHandle = 0;
            }
            if (UIRoot != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(UIRoot.gameObject);
                else UnityEngine.Object.DestroyImmediate(UIRoot.gameObject);
            }
            m_LayerBlock = null;
            UICacheLayer = null;
            UICanvasRoot = null;
            UICanvas = null;
            UICamera = null;
            UIRoot = null;
            _initialized = false;
        }
    }
}
