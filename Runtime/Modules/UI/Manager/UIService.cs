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
        private enum UILifetime : byte { Created, Running, Stopped }

        internal readonly IUIResourceLoader ResourceLoader;
        private readonly Dictionary<RuntimeTypeHandle, UIWindowRecord> _windows = new(RuntimeTypeHandleComparer.Instance);
        private readonly UITickList _updateViews = new();
        private readonly UITickList _transitionViews = new();
        private UIWindowCache _cache;
        private UIBlocker _blocker;
        private ITimerService _timerService;
        private UILifetime _lifetime;
        private uint _tick;

        public UIService() : this(new UIResourceLoader()) { }
        internal UIService(IUIResourceLoader resourceLoader) => ResourceLoader = resourceLoader;

        protected override void OnDispose() => DestroyAllManagedUI();

        private UIWindowRecord GetWindowRecord(RuntimeTypeHandle handle)
        {
            if (_lifetime != UILifetime.Running)
            {
                Log.Error("[UI] UIService is not running.");
                return null;
            }
            if (_windows.TryGetValue(handle, out UIWindowRecord record)) return record;
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

        internal void LinkUpdate(UITickNode node) => _updateViews.Link(node, _tick);
        internal void UnlinkUpdate(UITickNode node) => _updateViews.Unlink(node);
        internal void LinkTransition(UITickNode node) => _transitionViews.Link(node, _tick);
        internal void UnlinkTransition(UITickNode node) => _transitionViews.Unlink(node);

        void IServiceTickable.Tick(float deltaTime)
        {
            uint tick = ++_tick;
            _updateViews.BeginTraversal();
            while (_updateViews.MoveNext(tick) is { } update) update.View.RunUpdate(deltaTime);
            _transitionViews.BeginTraversal();
            while (_transitionViews.MoveNext(tick) is { } transition) transition.View.Holder.AdvanceTransition(deltaTime);
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
            if (!_windows.ContainsKey(handle) && !IsValidWindowHandle(handle)) return UniTask.FromResult<UIWindow>(null);
            return GetView<UIWindow>(RequestShow(GetWindowRecord(handle), userDatas, cancellationToken));
        }

        private static UniTask<T> GetView<T>(UniTask<UIOpenResult> task) where T : UIWindow =>
            task.Status == UniTaskStatus.Succeeded
                ? UniTask.FromResult((T)task.GetAwaiter().GetResult().View)
                : AwaitView<T>(task);

        private static async UniTask<T> AwaitView<T>(UniTask<UIOpenResult> task) where T : UIWindow
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
            bool affectNavigation = true)
        {
            if (view.State is UIState.Destroying or UIState.Destroyed)
            {
                Log.Warning("[UI] Close ignored because the window is not open and not cached.");
                return UniTask.CompletedTask;
            }
            return RequestClose(view.Record, force, skipTransition, affectNavigation);
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

        private void DestroyAllManagedUI()
        {
            bool initialized = _lifetime == UILifetime.Running;
            _lifetime = UILifetime.Stopped;
            if (!initialized) return;
            StopNavigation();
            foreach (UIWindowRecord record in _windows.Values) record.View?.DestroyNow();
            _windows.Clear();
            _blocker.Dispose();
            _blocker = null;
            DestroyUIRoot();
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
            for (int i = 0; i < _layers.Length; i++) _layers[i] = null;
        }
    }
}
