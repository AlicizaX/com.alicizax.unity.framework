using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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

    public sealed class UIWindowHolder : UIHolderObjectBase { }
    public sealed class UIWidgetHolder : UIHolderObjectBase { }

    internal sealed class UIProbeWindow : UIWindow<UIWindowHolder>
    {
        internal static Action<UIProbeWindow> Initializing;
        internal int Initializes, Opens, Closes, Destroys, Refreshes, Updates;
        internal Action Opening, Closing, Updating, Destroying, Refreshing;
        internal object Argument => UserData;
        internal UIProbeWidget Create(bool open = true) => CreateWidgetSync<UIProbeWidget>(baseui != null ? baseui.RectTransform : null, open);
        internal UniTask<UIProbeWidget> CreateAsync(bool open = true, CancellationToken token = default) =>
            CreateWidgetAsync<UIProbeWidget>(baseui.RectTransform, open, token);
        internal UIProbeWidget Borrow(UIWidgetHolder holder, bool destroyHolder = true) => CreateWidgetSync<UIProbeWidget>(holder, destroyHolder);
        protected override void OnInitialize() { Initializes++; Initializing?.Invoke(this); }
        protected override void OnOpen() { Opens++; Opening?.Invoke(); }
        protected override void OnClose() { Closes++; Closing?.Invoke(); }
        protected override void OnDestroy() { Destroys++; Destroying?.Invoke(); }
        protected override void OnRefresh() { Refreshes++; Refreshing?.Invoke(); }
        protected override void OnUpdate() { Updates++; Updating?.Invoke(); }
    }

    internal sealed class UISecondWindow : UIWindow<UIWindowHolder> { }
    internal sealed class UIThirdWindow : UIWindow<UIWindowHolder> { }

    internal sealed class UIZeroCacheWindow : UIWindow<UIWindowHolder>
    {
        internal UIProbeWidget Create() => CreateWidgetSync<UIProbeWidget>(baseui.RectTransform);
    }

    internal sealed class UITimedCacheWindow : UIWindow<UIWindowHolder>
    {
        internal UIProbeWidget Create() => CreateWidgetSync<UIProbeWidget>(baseui.RectTransform);
    }

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
        internal static Action<UIProbeWidget> Initializing;
        internal int Initializes, Opens, Closes, Destroys, Refreshes, Updates, Events, Registrations;
        internal Action Opening, Closing, Registering, Updating, Destroying, Refreshing, Receiving;
        internal object Argument => UserData;
        private Action _eventHandler;
        internal UIProbeWidget Create(bool open = true) => CreateWidgetSync<UIProbeWidget>(baseui.RectTransform, open);
        internal UniTask<UIProbeWidget> CreateAsync(bool open = true, CancellationToken token = default) =>
            CreateWidgetAsync<UIProbeWidget>(baseui.RectTransform, open, token);
        protected override void OnInitialize() { Initializes++; Initializing?.Invoke(this); }
        protected override void OnOpen() { Opens++; Opening?.Invoke(); }
        protected override void OnClose() { Closes++; Closing?.Invoke(); }
        protected override void OnDestroy() { Destroys++; Destroying?.Invoke(); }
        protected override void OnRefresh() { Refreshes++; Refreshing?.Invoke(); }
        protected override void OnUpdate() { Updates++; Updating?.Invoke(); }
        protected override void OnRegisterEvent(EventListenerProxy proxy)
        {
            Registrations++;
            proxy.AddUIEvent<UIProbeEvent>(_eventHandler ??= Receive);
            Registering?.Invoke();
        }
        private void Receive() { Events++; Receiving?.Invoke(); }
    }

    internal sealed class UIControlledLoader : IUIResourceLoader
    {
        internal readonly List<GameObject> Objects = new();
        internal bool Deferred, Fail;
        internal int Loads;
        private readonly List<LoadRequest> _pending = new();
        internal UniTaskCompletionSource<GameObject> Pending => _pending.Count == 0 ? null : _pending[0].Completion;
        internal CancellationToken PendingToken => _pending[0].Token;
        internal int PendingCount => _pending.Count;
        internal Action<UIHolderObjectBase> Bound;
        internal Exception Failure;

        private sealed class LoadRequest
        {
            internal readonly UniTaskCompletionSource<GameObject> Completion = new();
            internal UIResRegistry.UIResInfo Resource;
            internal Transform Parent;
            internal CancellationToken Token;
        }

        public GameObject Load(UIResRegistry.UIResInfo resource, Transform parent)
        {
            Loads++;
            if (Failure != null) throw Failure;
            return Fail ? null : Make(resource, parent);
        }

        public UniTask<GameObject> LoadAsync(UIResRegistry.UIResInfo resource, Transform parent, CancellationToken token)
        {
            if (!Deferred) return UniTask.FromResult(Load(resource, parent));
            Loads++;
            var request = new LoadRequest { Resource = resource, Parent = parent, Token = token };
            _pending.Add(request);
            return request.Completion.Task;
        }

        internal GameObject Deliver(int index = 0)
        {
            LoadRequest request = _pending[index];
            _pending.RemoveAt(index);
            if (Failure != null)
            {
                request.Completion.TrySetException(Failure);
                return null;
            }
            GameObject obj = Fail ? null : Make(request.Resource, request.Parent);
            request.Completion.TrySetResult(obj);
            return obj;
        }

        private GameObject Make(UIResRegistry.UIResInfo resource, Transform parent)
        {
            var obj = new GameObject(resource.Location, typeof(RectTransform));
            Objects.Add(obj);
            obj.transform.SetParent(parent, false);
            UIHolderObjectBase holder;
            if (resource.Location == "UIProbeWindow")
            {
                obj.AddComponent<Canvas>();
                obj.AddComponent<GraphicRaycaster>();
                holder = obj.AddComponent<UIWindowHolder>();
            }
            else holder = obj.AddComponent<UIWidgetHolder>();
            Bound?.Invoke(holder);
            return obj;
        }
    }

    internal sealed class UIGateTransition : IUITransitionSource
    {
        internal int OpenPlays, ClosePlays, Snaps;
        internal bool LastSnap;
        internal UniTaskCompletionSource Pending;
        public UniTask Play(bool open, CancellationToken token)
        {
            if (open) OpenPlays++; else ClosePlays++;
            Pending = new UniTaskCompletionSource();
            return Pending.Task.AttachExternalCancellation(token);
        }
        public void Snap(bool open) { Snaps++; LastSnap = open; }
        internal void Finish() => Pending.TrySetResult();
    }

    internal sealed class UIFixture : IDisposable
    {
        internal readonly UIControlledLoader Loader = new();
        internal readonly UIService Service;
        internal readonly GameObject Root;
        internal readonly TimerService Timer;

        internal UIFixture()
        {
            Assert.That(AppServices.HasWorld, Is.False, "UI tests require an isolated service world.");
            MemoryPoolRegistry.InitializeMainThread();
            AppServices.EnsureWorld();
            Timer = new TimerService(256);
            AppServices.App.Register<ITimerService>(Timer);
            Register<UIProbeWindow, UIWindowHolder>("UIProbeWindow", -1, true);
            Register<UISecondWindow, UIWindowHolder>("UIProbeWindow", -1, false);
            Register<UIThirdWindow, UIWindowHolder>("UIProbeWindow", -1, false);
            Register<UIZeroCacheWindow, UIWindowHolder>("UIProbeWindow", 0, false);
            Register<UITimedCacheWindow, UIWindowHolder>("UIProbeWindow", 1, false);
            Register<UIProbeTabWindow, UIWindowHolder>("UIProbeWindow", -1, false);
            Register<UIProbeWidget, UIWidgetHolder>("UIProbeWidget", 0, true);
            Register<UISecondWidget, UIWidgetHolder>("UIProbeWidget", 0, false);
            Root = new GameObject("UIRefactorTestRoot", typeof(RectTransform), typeof(Canvas));
            var camera = new GameObject("UIRefactorTestCamera", typeof(Camera));
            camera.transform.SetParent(Root.transform);
            camera.transform.localPosition = new Vector3(0, 0, -100);
            Canvas canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera.GetComponent<Camera>();
            canvas.worldCamera.cullingMask = 1 << UIComponent.UIShowLayer;
            Service = new UIService(Loader);
            Service.Initialize(Root.transform, true);
            AppServices.App.Register<IUIService>(Service);
        }

        private static void Register<T, THolder>(string resource, int cacheTime, bool update)
        {
            UIMetaRegistry.Register(typeof(T), typeof(THolder), UILayer.UI, cacheTime, update);
            UIResRegistry.Register(typeof(THolder), resource, EUIResLoadType.Resources);
        }

        internal UIProbeWindow Open() => Service.ShowUISync<UIProbeWindow>();
        internal void Tick() => ((IServiceTickable)Service).Tick(0.016f);
        internal static void Publish() => EventBus.Publish<UIProbeEvent>();

        public void Dispose()
        {
            UIProbeWindow.Initializing = null;
            UIProbeWidget.Initializing = null;
            AppServices.Shutdown();
            while (Loader.Pending != null) Loader.Deliver();
            foreach (GameObject obj in Loader.Objects)
                if (obj != null) Object.DestroyImmediate(obj);
            if (Root != null) Object.DestroyImmediate(Root);
            EventBus.ClearEmpty<UIProbeEvent>();
        }

        internal static IEnumerator Wait(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            for (int i = 0; !awaiter.IsCompleted && i < 180; i++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "UI operation did not settle.");
            awaiter.GetResult();
        }
    }
}
