using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX.Resource.Runtime;
using AlicizaX.Timer.Runtime;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("AlicizaX.Framework.UI.Editor.Tests")]

namespace AlicizaX.UI.Tests
{
    internal struct UIProbeEvent : IEmptyEventArgs { }
    internal struct UIProbeWindowEvent : IEmptyEventArgs { }

    public sealed class UIWindowHolder : UIHolderObjectBase { }
    public sealed class UIWidgetHolder : UIHolderObjectBase { }

    internal sealed class UIProbeWindow : UIWindow<UIWindowHolder>
    {
        internal static Action<UIProbeWindow> Initializing;
        internal int Initializes, Opens, Closes, Destroys, Refreshes, Updates, Events;
        internal Action Opening, Closing, Updating, Destroying, Refreshing;
        internal object Argument => UserData;
        internal UIProbeWidget Create(bool open = true) => CreateWidgetSync<UIProbeWidget>(baseui.RectTransform, open);
        internal UIProbeWidget CreateUnder(Transform parent, bool open = true) => CreateWidgetSync<UIProbeWidget>(parent, open);
        internal UniTask<UIProbeWidget> CreateAsync(bool open = true, CancellationToken token = default) =>
            CreateWidgetAsync<UIProbeWidget>(baseui.RectTransform, open, token);
        internal UIProbeWidget Borrow(UIWidgetHolder holder, bool open = true) => CreateWidgetSync<UIProbeWidget>(holder, open);
        internal UIViewTransition CloseThis(bool force = false, bool snap = false) => CloseSelf(force, snap);
        internal EventListenerProxy Proxy => EventProxy;
        internal UIWidget CreateByName(string typeName) => CreateWidgetSync(typeName, baseui.RectTransform);
        internal UniTask<UIWidget> CreateByNameAsync(string typeName) => CreateWidgetAsync(typeName, baseui.RectTransform);

        protected override void OnInitialize()
        {
            Initializes++;
            EventProxy.AddUIEvent<UIProbeWindowEvent>(Receive);
            Initializing?.Invoke(this);
        }

        protected override void OnOpen() { Opens++; Opening?.Invoke(); }
        protected override void OnClose() { Closes++; Closing?.Invoke(); }
        protected override void OnDestroy() { Destroys++; Destroying?.Invoke(); }
        protected override void OnRefresh() { Refreshes++; Refreshing?.Invoke(); }
        protected override void OnUpdate(float deltaTime) { Updates++; Updating?.Invoke(); }
        private void Receive() => Events++;
    }

    internal sealed class UISecondWindow : UIWindow<UIWindowHolder> { }
    internal sealed class UIThirdWindow : UIWindow<UIWindowHolder> { }
    internal sealed class UIPopupWindow : UIWindow<UIWindowHolder> { }

    internal sealed class UIZeroCacheWindow : UIWindow<UIWindowHolder>
    {
        internal int Destroys;
        protected override void OnDestroy() => Destroys++;
    }

    internal sealed class UITimedCacheWindow : UIWindow<UIWindowHolder> { }

    internal sealed class UIProbeTabWindow : UITabWindow<UIWindowHolder>
    {
        protected override void OnInitialize()
        {
            InitTabVirtuallyView<UIProbeWidget>();
            InitTabVirtuallyView<UISecondWidget>();
        }
    }

    internal sealed class UISecondWidget : UIWidget<UIWidgetHolder> { }

    internal sealed class UIProbeWidget : UIWidget<UIWidgetHolder>
    {
        internal int Initializes, Opens, Closes, Destroys, Refreshes, Updates, Events;
        internal Action Opening, Closing;
        internal object Argument => UserData;
        internal UIProbeWidget Create(bool open = true) => CreateWidgetSync<UIProbeWidget>(baseui.RectTransform, open);

        protected override void OnInitialize()
        {
            Initializes++;
            EventProxy.AddUIEvent<UIProbeEvent>(Receive);
        }

        protected override void OnOpen() { Opens++; Opening?.Invoke(); }
        protected override void OnClose() { Closes++; Closing?.Invoke(); }
        protected override void OnDestroy() => Destroys++;
        protected override void OnRefresh() => Refreshes++;
        protected override void OnUpdate(float deltaTime) => Updates++;
        private void Receive() => Events++;
    }

    internal sealed class UIControlledLoader : ServiceBase, IResourceService
    {
        private sealed class LoadRequest
        {
            internal readonly UniTaskCompletionSource<GameObject> Completion = new();
            internal string Location;
            internal Transform Parent;
        }

        internal readonly List<GameObject> Objects = new();
        internal bool Deferred, Fail, IgnoreCancellation;
        internal int Loads;
        internal Action<UIHolderObjectBase> Bound;
        private readonly List<LoadRequest> _pending = new();

        internal int PendingCount => _pending.Count;

        public GameObject Instantiate(string location, Transform parent = null, string packageName = null)
        {
            Loads++;
            return Fail ? null : Make(location, parent).gameObject;
        }

        public UniTask<GameObject> InstantiateAsync(string location, Transform parent = null,
            CancellationToken token = default, string packageName = null)
        {
            if (!Deferred) return UniTask.FromResult(Instantiate(location, parent));
            Loads++;
            var request = new LoadRequest { Location = location, Parent = parent };
            _pending.Add(request);
            if (!IgnoreCancellation) token.Register(() => request.Completion.TrySetCanceled(token));
            return request.Completion.Task;
        }

        internal UIHolderObjectBase Deliver(int index = 0)
        {
            LoadRequest request = _pending[index];
            _pending.RemoveAt(index);
            UIHolderObjectBase holder = Fail ? null : Make(request.Location, request.Parent);
            request.Completion.TrySetResult(holder != null ? holder.gameObject : null);
            return holder;
        }

        private UIHolderObjectBase Make(string location, Transform parent)
        {
            var obj = new GameObject(location, typeof(RectTransform));
            Objects.Add(obj);
            obj.transform.SetParent(parent, false);
            UIHolderObjectBase holder;
            if (location == nameof(UIWindowHolder))
            {
                obj.AddComponent<Canvas>();
                obj.AddComponent<GraphicRaycaster>();
                holder = obj.AddComponent<UIWindowHolder>();
            }
            else holder = obj.AddComponent<UIWidgetHolder>();
            Bound?.Invoke(holder);
            return holder;
        }

        bool IResourceService.CanLoadResources => throw new NotSupportedException();

        float IResourceService.IdleAssetExpireTime
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        int IResourceService.IdleAssetCapacity
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        int IResourceService.IdleReleaseCountPerTick
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        ResourceScope IResourceService.App => throw new NotSupportedException();

        ResourceScope IResourceService.CreateScope(string debugName) => throw new NotSupportedException();

        AssetRef<T> IResourceService.Key<T>(string location, string packageName) => throw new NotSupportedException();

        AssetLease<T> IResourceService.LoadLease<T>(AssetRef<T> key) => throw new NotSupportedException();

        AssetLease<T> IResourceService.LoadLease<T>(string location, string packageName) => throw new NotSupportedException();

        UniTask<AssetLease<T>> IResourceService.LoadLeaseAsync<T>(AssetRef<T> key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        UniTask<AssetLease<T>> IResourceService.LoadLeaseAsync<T>(string location, CancellationToken cancellationToken,
            string packageName) => throw new NotSupportedException();

        void IResourceService.UnloadUnusedAssets(bool releaseIdleAssets) => throw new NotSupportedException();

        void IResourceService.ForceUnloadAllAssets() => throw new NotSupportedException();

        void IResourceService.ForceUnloadUnusedAssets(bool performGCCollect) => throw new NotSupportedException();

        void IResourceService.SetForceUnloadUnusedAssetsAction(Action<bool> action) => throw new NotSupportedException();

        void IResourceService.OnLowMemory() => throw new NotSupportedException();
    }

    internal sealed class UIThrowingTransition : IUITransition
    {
        public UniTask Open(bool snap) => throw new InvalidOperationException("transition failure");

        public UniTask Close(bool snap) => throw new InvalidOperationException("transition failure");
    }

    internal sealed class UIGateTransition : IUITransition
    {
        internal int Opens, Closes;
        internal bool LastSnap;
        private AutoResetUniTaskCompletionSource _completion;

        internal bool Playing => _completion != null;

        public UniTask Open(bool snap) => Play(true, snap);

        public UniTask Close(bool snap) => Play(false, snap);

        internal void Finish()
        {
            AutoResetUniTaskCompletionSource completion = _completion;
            _completion = null;
            completion?.TrySetResult();
        }

        private UniTask Play(bool open, bool snap)
        {
            if (open) Opens++;
            else Closes++;
            LastSnap = snap;
            Finish();
            if (snap) return UniTask.CompletedTask;
            _completion = AutoResetUniTaskCompletionSource.Create();
            return _completion.Task;
        }
    }

    internal sealed class UIFixture : IDisposable
    {
        internal readonly UIControlledLoader Loader = new();
        internal readonly UIService Service;
        internal readonly GameObject Root;
        internal readonly TimerService Timer;

        internal UIFixture()
        {
            Assert.That(AppServices.IsAvailable, Is.False, "UI tests require an isolated service registry.");
            MemoryPoolRegistry.InitializeMainThread();
            AppServices.GetOrCreateRegistry();
            Timer = new TimerService(256);
            AppServices.Register<ITimerService>(Timer);
            AppServices.Register<IResourceService>(Loader);
            Register<UIProbeWindow, UIWindowHolder>(UICacheTime.Forever, true);
            Register<UISecondWindow, UIWindowHolder>(UICacheTime.Forever, false);
            Register<UIThirdWindow, UIWindowHolder>(UICacheTime.Forever, false);
            Register<UIZeroCacheWindow, UIWindowHolder>(UICacheTime.None, false);
            Register<UITimedCacheWindow, UIWindowHolder>(1, false);
            Register<UIProbeTabWindow, UIWindowHolder>(UICacheTime.Forever, false);
            Register<UIPopupWindow, UIWindowHolder>(UICacheTime.Forever, false, UILayer.Popup);
            Register<UIProbeWidget, UIWidgetHolder>(UICacheTime.None, true);
            Register<UISecondWidget, UIWidgetHolder>(UICacheTime.None, false);
            Root = new GameObject("UIRefactorTestRoot", typeof(RectTransform), typeof(Canvas));
            var camera = new GameObject("UIRefactorTestCamera", typeof(Camera));
            camera.transform.SetParent(Root.transform);
            camera.transform.localPosition = new Vector3(0, 0, -100);
            Canvas canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera.GetComponent<Camera>();
            canvas.worldCamera.cullingMask = 1 << UIComponent.UIShowLayer;
            Service = new UIService();
            Service.Initialize(Root.transform, true);
            AppServices.Register<IUIService>(Service);
        }

        private static void Register<T, THolder>(int cacheTime, bool update, UILayer layer = UILayer.UI)
        {
            UIMetaRegistry.Register(typeof(T), typeof(THolder), layer, cacheTime, update);
            UIResRegistry.Register(typeof(THolder), typeof(THolder).Name, EUIResLoadType.AssetBundle);
        }

        internal UIProbeWindow Open(object userData = null) => Service.ShowUISync<UIProbeWindow>(userData);

        internal UIGateTransition Gate(UIBase view)
        {
            var gate = new UIGateTransition();
            view.Holder.SetTransition(gate);
            return gate;
        }

        internal void Tick() => ((IServiceTickable)Service).Tick(0.016f);

        internal static void Publish() => EventBus.Publish<UIProbeEvent>();

        internal static void PublishWindow() => EventBus.Publish<UIProbeWindowEvent>();

        internal static void ConfigurePreset(UIPresetTransition transition, UITransitionPreset preset, float duration)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(UIPresetTransition).GetField("openPreset", flags).SetValue(transition, preset);
            typeof(UIPresetTransition).GetField("closePreset", flags).SetValue(transition, preset);
            typeof(UIPresetTransition).GetField("openDuration", flags).SetValue(transition, duration);
            typeof(UIPresetTransition).GetField("closeDuration", flags).SetValue(transition, duration);
        }

#if UNITY_EDITOR
        internal void Verify() => UIInvariantChecker.Verify(Service);
#endif

        private const float SettleSeconds = 5f;

        internal IEnumerator Wait(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            for (float deadline = Time.realtimeSinceStartup + SettleSeconds; !awaiter.IsCompleted && Time.realtimeSinceStartup < deadline;)
            {
                Tick();
                yield return null;
            }
            Assert.That(awaiter.IsCompleted, Is.True, "UI operation did not settle.");
            awaiter.GetResult();
        }

        internal IEnumerator Wait<T>(UniTask<T> task, Action<T> result)
        {
            var awaiter = task.GetAwaiter();
            for (float deadline = Time.realtimeSinceStartup + SettleSeconds; !awaiter.IsCompleted && Time.realtimeSinceStartup < deadline;)
            {
                Tick();
                yield return null;
            }
            Assert.That(awaiter.IsCompleted, Is.True, "UI operation did not settle.");
            result(awaiter.GetResult());
        }

        internal IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Tick();
                yield return null;
            }
        }

        public void Dispose()
        {
            UIProbeWindow.Initializing = null;
            int probeSubscribers, windowSubscribers, leasedProxies;
            try
            {
#if UNITY_EDITOR
                if (AppServices.IsAvailable) Verify();
#endif
            }
            finally
            {
                AppServices.Shutdown();
                while (Loader.PendingCount > 0) Loader.Deliver();
                foreach (GameObject obj in Loader.Objects)
                    if (obj != null) Object.DestroyImmediate(obj);
                if (Root != null) Object.DestroyImmediate(Root);
                probeSubscribers = EventBus.GetEmptySubscriberCount<UIProbeEvent>();
                windowSubscribers = EventBus.GetEmptySubscriberCount<UIProbeWindowEvent>();
                leasedProxies = MemoryPool<EventListenerProxy>.Instance.GetInfo().LeasedCount;
                EventBus.ClearEmpty<UIProbeEvent>();
                EventBus.ClearEmpty<UIProbeWindowEvent>();
            }
            Assert.That(probeSubscribers, Is.Zero, "Widget event subscribers left after shutdown.");
            Assert.That(windowSubscribers, Is.Zero, "Window event subscribers left after shutdown.");
            Assert.That(leasedProxies, Is.Zero, "Event listener proxies left after shutdown.");
        }
    }
}
