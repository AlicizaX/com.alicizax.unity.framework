using System.Collections;
using AlicizaX.Resource.Runtime;
using AlicizaX.Timer.Runtime;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResUIWindowHolder : UIHolderObjectBase
    {
        public Image Icon;
    }

    public sealed class ResUIWidgetHolder : UIHolderObjectBase { }

    internal sealed class ResUIWindow : UIWindow<ResUIWindowHolder>
    {
        internal ResUIWidget Widget;

        protected override void OnInitialize()
        {
            Assert.That(Res.Load<TextAsset>(ResourceUIWorkflowTests.Config), Is.Not.Null);
            baseui.Icon.SetSprite(Res, ResourceUIWorkflowTests.Icon, ResourceBindingOptions.Synchronous);
            Widget = CreateWidgetSync<ResUIWidget>(baseui.RectTransform);
        }
    }

    internal sealed class ResUICachedWindow : UIWindow<ResUIWindowHolder>
    {
        internal ResUIWidget Widget;

        protected override void OnInitialize()
        {
            Assert.That(Res.Load<TextAsset>(ResourceUIWorkflowTests.Config), Is.Not.Null);
            baseui.Icon.SetSprite(Res, ResourceUIWorkflowTests.Icon, ResourceBindingOptions.Synchronous);
            Widget = CreateWidgetSync<ResUIWidget>(baseui.RectTransform);
        }
    }

    internal sealed class ResUIWidget : UIWidget<ResUIWidgetHolder>
    {
        protected override void OnInitialize()
        {
            Assert.That(Res.Load<TextAsset>(ResourceUIWorkflowTests.Config), Is.Not.Null);
        }
    }

    public sealed class ResourceUIWorkflowTests
    {
        internal const string WindowLocation = "ui/ResWindow";
        internal const string WidgetLocation = "ui/ResWidget";
        internal const string Config = "ui/config";
        internal const string Icon = "ui/icon";
        private const int AssetsPerWindow = 4;
        private const int ReferencesPerWindow = 5;
        private const int ScopesPerWindow = 4;

        private ResourceFixture f;
        private UIService ui;
        private GameObject uiRoot;

        [SetUp]
        public void SetUp()
        {
            if (AppServices.IsAvailable) AppServices.Shutdown();
            f = new ResourceFixture(true);
            var templates = f.GameObject("ui-templates");
            templates.SetActive(false);
            Template(templates.transform, WindowLocation, true);
            Template(templates.transform, WidgetLocation, false);
            f.Text(Config);
            f.Sprite(Icon);

            UIMetaRegistry.Register(typeof(ResUIWindow), typeof(ResUIWindowHolder), UILayer.UI, 0, false);
            UIMetaRegistry.Register(typeof(ResUICachedWindow), typeof(ResUIWindowHolder), UILayer.UI, -1, false);
            UIMetaRegistry.Register(typeof(ResUIWidget), typeof(ResUIWidgetHolder), UILayer.UI, 0, false);
            UIResRegistry.Register(typeof(ResUIWindowHolder), WindowLocation, EUIResLoadType.AssetBundle);
            UIResRegistry.Register(typeof(ResUIWidgetHolder), WidgetLocation, EUIResLoadType.AssetBundle);

            MemoryPoolRegistry.InitializeMainThread();
            AppServices.GetOrCreateRegistry();
            AppServices.Register<IResourceService>(f.Service);
            AppServices.Register<ITimerService>(new TimerService(256));
            uiRoot = f.Keep(new GameObject("ResourceUIRoot", typeof(RectTransform), typeof(Canvas)));
            Canvas canvas = uiRoot.GetComponent<Canvas>();
            var camera = new GameObject("ResourceUICamera", typeof(Camera));
            camera.transform.SetParent(uiRoot.transform);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera.GetComponent<Camera>();
            ui = new UIService();
            ui.Initialize(uiRoot.transform, true);
            AppServices.Register<IUIService>(ui);
        }

        [TearDown]
        public void TearDown()
        {
            AppServices.Shutdown();
            f.Dispose();
        }

        private void Template(Transform parent, string location, bool window)
        {
            var go = new GameObject(location, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (window)
            {
                go.AddComponent<Canvas>();
                go.AddComponent<GraphicRaycaster>();
                var icon = new GameObject("icon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(go.transform, false);
                go.AddComponent<ResUIWindowHolder>().Icon = icon.GetComponent<Image>();
            }
            else
            {
                go.AddComponent<ResUIWidgetHolder>();
            }

            f.Loader.Assets.Add(location, go);
        }

        private IEnumerator Wait(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 180; frame++)
            {
                ((IServiceTickable)ui).Tick(0.016f);
                yield return null;
            }

            Assert.That(awaiter.IsCompleted, Is.True, "UI operation did not settle.");
            awaiter.GetResult();
        }

        private IEnumerator Open<T>(bool sync) where T : UIWindow
        {
            if (sync)
            {
                Assert.That(ui.ShowUISync<T>(), Is.Not.Null);
                yield break;
            }

            var awaiter = ui.ShowUI<T>().GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 180; frame++)
            {
                ((IServiceTickable)ui).Tick(0.016f);
                yield return null;
            }

            Assert.That(awaiter.IsCompleted, Is.True, "Window open did not settle.");
            Assert.That(awaiter.GetResult(), Is.Not.Null);
        }

        private ResourceScopeInfo[] Scopes()
        {
            var infos = new ResourceScopeInfo[64];
            int count = f.Service.GetScopeInfos(infos, 0);
            var result = new ResourceScopeInfo[count];
            System.Array.Copy(infos, result, count);
            return result;
        }

        private void AssertWindowResources(UIWindow window, ResUIWidget widget)
        {
            ResourceStats stats = f.Service.GetStats();
            Assert.That(stats.ReferenceCount, Is.EqualTo(ReferencesPerWindow));
            Assert.That(stats.ScopeCount, Is.EqualTo(1 + ScopesPerWindow));
            Assert.That(f.Info(WindowLocation).RefCount, Is.EqualTo(1));
            Assert.That(f.Info(WidgetLocation).RefCount, Is.EqualTo(1));
            Assert.That(f.Info(Config).RefCount, Is.EqualTo(2));
            Assert.That(f.Info(Icon).RefCount, Is.EqualTo(1));
            Assert.That(((ResUIWindowHolder)window.Holder).Icon.sprite, Is.SameAs(f.Loader.Assets[Icon]));

            bool windowInstance = false, widgetInstance = false, windowRes = false, widgetRes = false;
            foreach (ResourceScopeInfo info in Scopes())
            {
                if (info.Source == ResourceScopeSource.Instance && info.Name == WindowLocation)
                {
                    windowInstance = true;
                    Assert.That(info.OwnerObject, Is.SameAs(window.Holder.gameObject));
                    Assert.That(info.EntryCount, Is.EqualTo(1));
                }
                else if (info.Source == ResourceScopeSource.Instance && info.Name == WidgetLocation)
                {
                    widgetInstance = true;
                    Assert.That(info.OwnerObject, Is.SameAs(widget.Holder.gameObject));
                }
                else if (info.Source == ResourceScopeSource.Manual && info.Name == window.GetType().Name)
                {
                    windowRes = true;
                    Assert.That(info.EntryCount, Is.EqualTo(1));
                    Assert.That(info.BindingCount, Is.EqualTo(1));
                }
                else if (info.Source == ResourceScopeSource.Manual && info.Name == nameof(ResUIWidget))
                {
                    widgetRes = true;
                    Assert.That(info.EntryCount, Is.EqualTo(1));
                }
            }

            Assert.That(windowInstance && widgetInstance && windowRes && widgetRes, Is.True,
                $"instance={windowInstance} widget-instance={widgetInstance} res={windowRes} widget-res={widgetRes}");
            f.AssertInvariants();
        }

        private IEnumerator AssertEverythingReleased()
        {
            yield return null;
            yield return null;
            ResourceStats stats = f.Service.GetStats();
            Assert.That(stats.ReferenceCount, Is.Zero);
            Assert.That(stats.ScopeCount, Is.EqualTo(1), "Only the App scope may remain.");
            Assert.That(stats.BindingCount, Is.Zero);
            Assert.That(Object.FindObjectsOfType<ResUIWindowHolder>().Length, Is.Zero);
            Assert.That(Object.FindObjectsOfType<ResUIWidgetHolder>().Length, Is.Zero);
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator AsyncOpenAndZeroCacheCloseReleaseEverythingTheWindowTouched() => OpenCloseRelease(false);

        [UnityTest]
        public IEnumerator SyncOpenAndZeroCacheCloseReleaseEverythingTheWindowTouched() => OpenCloseRelease(true);

        private IEnumerator OpenCloseRelease(bool sync)
        {
            yield return Open<ResUIWindow>(sync);
            var window = ui.GetUI<ResUIWindow>();
            AssertWindowResources(window, window.Widget);
            Assert.That(f.Loader.Loads, Is.EqualTo(AssetsPerWindow));

            yield return Wait(ui.CloseUI<ResUIWindow>());
            yield return AssertEverythingReleased();
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RepeatedOpenCloseHitsTheIdleCacheAndLeaksNothing()
        {
            const int cycles = 30;
            int scopeSlots = 0;
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                yield return Open<ResUIWindow>(cycle % 2 == 0);
                var window = ui.GetUI<ResUIWindow>();
                AssertWindowResources(window, window.Widget);
                yield return Wait(ui.CloseUI<ResUIWindow>());
                yield return AssertEverythingReleased();
                if (cycle == 0) scopeSlots = f.Service.ScopeSlotCount;
                else Assert.That(f.Service.ScopeSlotCount, Is.EqualTo(scopeSlots), "Scope slots must be reused, not grown, by repeated windows.");
            }

            ResourceStats stats = f.Service.GetStats();
            TestContext.WriteLine($"UI-CYCLES,{cycles},loads={f.Loader.Loads},hit={stats.HitCount},idle-hit={stats.IdleHitCount},join={stats.JoinCount},miss={stats.MissCount}");
            Assert.That(f.Loader.Loads, Is.EqualTo(AssetsPerWindow), "Reopening must be served from the idle cache.");
            Assert.That(stats.MissCount, Is.EqualTo(AssetsPerWindow));
            Assert.That(stats.IdleHitCount, Is.EqualTo((cycles - 1) * AssetsPerWindow));
            Assert.That(stats.HitCount, Is.EqualTo(cycles), "The widget's config load joins the window's active record once per open.");
        }

        [UnityTest]
        public IEnumerator CachedWindowKeepsItsResourcesUntilTheUIServiceIsDestroyed()
        {
            yield return Open<ResUICachedWindow>(false);
            var window = ui.GetUI<ResUICachedWindow>();
            AssertWindowResources(window, window.Widget);
            yield return Wait(ui.CloseUI<ResUICachedWindow>());
            yield return null;
            Assert.That(f.Service.GetStats().ReferenceCount, Is.EqualTo(ReferencesPerWindow), "A cached window keeps what it owns.");

            long misses = f.Service.GetStats().MissCount;
            yield return Open<ResUICachedWindow>(false);
            Assert.That(ui.GetUI<ResUICachedWindow>(), Is.SameAs(window), "Reopen must reuse the cached instance.");
            Assert.That(f.Service.GetStats().MissCount, Is.EqualTo(misses));
            Assert.That(f.Loader.Loads, Is.EqualTo(AssetsPerWindow));
            AssertWindowResources(window, window.Widget);

            AppServices.Unregister(ui);
            yield return AssertEverythingReleased();
        }
    }
}
