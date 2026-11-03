#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    [Window(UILayer.Popup, -1)]
    internal sealed class UIReflectedWindow : UIWindow<UIWindowHolder>
    {
        internal int Opens;
        protected override void OnOpen() => Opens++;
    }

    internal sealed class UIFactoryWindow : UIWindow<UIWindowHolder>
    {
        internal static int Created;
    }

    internal sealed class UIFactoryPlainWindow : UIWindow<UIWindowHolder> { }

    public abstract class UILayerStackCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private List<UIWindow> OpenPages(int count)
        {
            var windows = new List<UIWindow>(count);
            var method = typeof(UIService).GetMethod(nameof(UIService.ShowUISync));
            for (int i = 0; i < count; i++)
            {
                Type page = typeof(UIHistoryPages).GetNestedType("Page" + i, System.Reflection.BindingFlags.NonPublic);
                UIMetaRegistry.Register(page, typeof(UIWindowHolder), UILayer.UI, -1, false);
                windows.Add((UIWindow)method.MakeGenericMethod(page).Invoke(Service, new object[] { Array.Empty<object>() }));
            }
            return windows;
        }

        [Test]
        public void ClosingMiddleWindowKeepsOtherSortingOrders()
        {
            List<UIWindow> windows = OpenPages(8);
            var orders = new int[windows.Count];
            for (int i = 0; i < windows.Count; i++) orders[i] = windows[i].Canvas.sortingOrder;
            windows[3].DestroyNow();
            for (int i = 0; i < windows.Count; i++)
                if (i != 3) Assert.That(windows[i].Canvas.sortingOrder, Is.EqualTo(orders[i]));
            Assert.That(Service.LayerStack((int)UILayer.UI).Count, Is.EqualTo(7));
            _fixture.Verify();
        }

        [Test]
        public void RemovingTopReusesOrderAndMiddleDoesNot()
        {
            UIProbeWindow a = _fixture.Open();
            UISecondWindow b = Service.ShowUISync<UISecondWindow>();
            UIThirdWindow c = Service.ShowUISync<UIThirdWindow>();
            int topDepth = c.Depth;
            c.DestroyNow();
            UIThirdWindow next = Service.ShowUISync<UIThirdWindow>();
            Assert.That(next.Depth, Is.EqualTo(topDepth));
            Assert.That(a.Depth, Is.LessThan(b.Depth));
            _fixture.Verify();
        }

        [Test]
        public void RepeatedBringToFrontRenumbersWithinLayerRange()
        {
            UIProbeWindow a = _fixture.Open();
            UISecondWindow b = Service.ShowUISync<UISecondWindow>();
            for (int i = 0; i < 500; i++)
            {
                _fixture.Open();
                Service.ShowUISync<UISecondWindow>();
                Assert.That(b.Depth, Is.GreaterThan(a.Depth));
                Assert.That(b.Depth, Is.LessThan(((int)UILayer.UI + 1) * UIService.LAYER_DEEP));
            }
            _fixture.Verify();
        }

        [Test]
        public void TopVisibleHolderFollowsStackOrder()
        {
            UIProbeWindow a = _fixture.Open();
            UISecondWindow b = Service.ShowUISync<UISecondWindow>();
            Assert.That(Service.TryGetTopVisibleHolder(null, out UIHolderObjectBase top), Is.True);
            Assert.That(top, Is.SameAs(b.Holder));
            Service.CloseUI<UISecondWindow>().GetAwaiter().GetResult();
            Assert.That(Service.TryGetTopVisibleHolder(null, out top), Is.True);
            Assert.That(top, Is.SameAs(a.Holder));
        }

        [Test]
        public void TryCloseTopClosesHighestMatchingWindow()
        {
            _fixture.Open();
            Service.ShowUISync<UISecondWindow>();
            RuntimeTypeHandle first = typeof(UIProbeWindow).TypeHandle;
            Assert.That(Service.TryCloseTopAsync(handle => handle.Equals(first)).GetAwaiter().GetResult(), Is.True);
            Assert.That(Service.IsOpen<UIProbeWindow>(), Is.False);
            Assert.That(Service.IsOpen<UISecondWindow>(), Is.True);
            _fixture.Verify();
        }
    }

    public abstract class UIRegistryCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [Test]
        public void ReflectionFallbackOpensUnregisteredWindow()
        {
            UIReflectedWindow window = Service.ShowUISync<UIReflectedWindow>();
            Assert.That(window, Is.Not.Null);
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(UIMetaRegistry.TryGet(typeof(UIReflectedWindow).TypeHandle, out var info), Is.True);
            Assert.That(info.UILayer, Is.EqualTo((int)UILayer.Popup));
            Assert.That(info.CacheTime, Is.EqualTo(-1));
            Assert.That(info.Create, Is.Not.Null);
        }

        [Test]
        public void RegisteredWindowUsesFactoryOncePerInstance()
        {
            UIMetaRegistry.Register(typeof(UIFactoryWindow), typeof(UIWindowHolder), UILayer.UI, 0, false, static () =>
            {
                UIFactoryWindow.Created++;
                return new UIFactoryWindow();
            });
            int before = UIFactoryWindow.Created;
            UIFactoryWindow first = Service.ShowUISync<UIFactoryWindow>();
            Assert.That(first, Is.Not.Null);
            Assert.That(UIFactoryWindow.Created, Is.EqualTo(before + 1));
            Service.CloseUI<UIFactoryWindow>().GetAwaiter().GetResult();
            Assert.That(Service.ShowUISync<UIFactoryWindow>(), Is.Not.SameAs(first));
            Assert.That(UIFactoryWindow.Created, Is.EqualTo(before + 2));
        }

        [Test]
        public void RegisterWithoutFactoryCreatesThroughReflectionFactory()
        {
            UIMetaRegistry.Register(typeof(UIFactoryPlainWindow), typeof(UIWindowHolder), UILayer.UI, -1, false);
            Assert.That(UIMetaRegistry.TryGet(typeof(UIFactoryPlainWindow).TypeHandle, out var info), Is.True);
            Assert.That(info.Create(), Is.TypeOf<UIFactoryPlainWindow>());
            Assert.That(Service.ShowUISync<UIFactoryPlainWindow>(), Is.Not.Null);
        }

        [TestCase(false)] [TestCase(true)]
        public void ThrowingConstructorReturnsNull(bool asynchronous)
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to create"));
            UIThrowingWindow window = asynchronous
                ? Service.ShowUI<UIThrowingWindow>().GetAwaiter().GetResult()
                : Service.ShowUISync<UIThrowingWindow>();
            Assert.That(window, Is.Null);
            Assert.That(Service.FindRecord(typeof(UIThrowingWindow).TypeHandle).View, Is.Null);
            _fixture.Verify();
        }

        [Test]
        public void ThrowingTabConstructorCompletesSwitch()
        {
            var window = Service.ShowUISync<UIThrowingTabWindow>();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to create"));
            Assert.That(window.SwitchTab(0).GetAwaiter().GetResult(), Is.Null);
            _fixture.Verify();
        }

        [Test]
        public void StringTypeResolvesRegisteredWindow()
        {
            UIWindow window = Service.ShowUI(typeof(UIProbeWindow).FullName).GetAwaiter().GetResult();
            Assert.That(window, Is.TypeOf<UIProbeWindow>());
        }
    }

    public abstract class UINavigationTabAdditionCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [Test]
        public void CurrentFollowsHistoryTopAndClearsOnDestroy()
        {
            Service.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult();
            Service.NavigateTo<UISecondWindow>().GetAwaiter().GetResult();
            Assert.That(Service.Current, Is.EqualTo(typeof(UISecondWindow)));
            Service.GetUI<UISecondWindow>().DestroyNow();
            Assert.That(Service.Current, Is.Null);
            Assert.That(Service.CanBack, Is.False);
            _fixture.Verify();
        }

        [Test]
        public void CommandQueuedDuringCloseCurrentRunsAfterClosing()
        {
            Service.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult();
            UIProbeWindow window = Service.GetUI<UIProbeWindow>();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            UniTask<UIRouteResult> closing = Service.CloseCurrent();
            UniTask<UIRouteResult> next = Service.ResetHistory();
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            Assert.That(closing.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            Assert.That(next.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
        }

        [Test]
        public void ClosingCurrentDuringLoadLeavesTargetAsOnlyHistoryEntry()
        {
            Service.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Loader.Deferred = true;
            UniTask<UIRouteResult> opening = Service.NavigateTo<UISecondWindow>();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Loader.Deliver();
            Assert.That(opening.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            Assert.That(Service.Current, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(Service.CanBack, Is.False);
            _fixture.Verify();
        }

        [Test]
        public void TabSwitchBetweenLoadedTabsCompletesSynchronously()
        {
            var window = Service.ShowUISync<UIProbeTabWindow>();
            UIWidget a = window.SwitchTab(0).GetAwaiter().GetResult();
            UIWidget b = window.SwitchTab(1).GetAwaiter().GetResult();
            UniTask<UIWidget> back = window.SwitchTab(0);
            Assert.That(back.Status.IsCompleted(), Is.True);
            Assert.That(back.GetAwaiter().GetResult(), Is.SameAs(a));
            Assert.That(a.IsOpen, Is.True);
            Assert.That(b.IsOpen, Is.False);
            _fixture.Verify();
        }

        [Test]
        public void TabSwitchWaitsForClosingPreviousTab()
        {
            var window = Service.ShowUISync<UIProbeTabWindow>();
            var a = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            UIWidget b = window.SwitchTab(1).GetAwaiter().GetResult();
            var switched = window.SwitchTab(0).GetAwaiter().GetResult();
            Assert.That(switched, Is.SameAs(a));
            var gate = new UIGateTransition();
            a.Holder.SetTransition(gate);
            UniTask<UIWidget> next = window.SwitchTab(1);
            Assert.That(next.Status.IsCompleted(), Is.False);
            Assert.That(a.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            Assert.That(next.GetAwaiter().GetResult(), Is.SameAs(b));
            _fixture.Verify();
        }

        [Test]
        public void TabSwitchFromPreviousTabCloseCallbackSupersedesOuterRequest()
        {
            var window = Service.ShowUISync<UIProbeTabWindow>();
            var a = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            UIWidget b = window.SwitchTab(1).GetAwaiter().GetResult();
            window.SwitchTab(0).GetAwaiter().GetResult();
            UniTask<UIWidget> nested = default;
            a.Closing = () =>
            {
                a.Closing = null;
                nested = window.SwitchTab(0);
            };
            UniTask<UIWidget> outer = window.SwitchTab(1);
            Assert.That(outer.GetAwaiter().GetResult(), Is.Null);
            Assert.That(nested.GetAwaiter().GetResult(), Is.SameAs(a));
            Assert.That(a.IsOpen, Is.True);
            Assert.That(b.IsOpen, Is.False);
            _fixture.Verify();
        }
    }

    public abstract class UIAllocationCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private static void IsZero(AllocationSample sample) => Assert.That(sample.Bytes, Is.Zero);
        private static void LogOnly(AllocationSample sample) { }

        private void WarmSharedCode()
        {
            Service.ShowUISync<UIThirdWindow>();
            Service.CloseUI<UIThirdWindow>().GetAwaiter().GetResult();
            Service.ShowUISync<UIThirdWindow>();
            Service.CloseUI<UIThirdWindow>().GetAwaiter().GetResult();
        }

        [UnityTest]
        public System.Collections.IEnumerator CalibrationDetectsKnownAllocation()
        {
            yield return AllocationCapture.Measure("calibration-known", 1, () => GC.KeepAlive(new byte[4096]),
                sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096)));
            yield return AllocationCapture.Measure("calibration-empty", 1, () => { }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator TickSteadyState([Values(1, 16, 512)] int views)
        {
            UIProbeWindow window = _fixture.Open();
            for (int i = 1; i < views; i++) window.Create();
            _fixture.Tick();
            yield return AllocationCapture.Measure("tick-steady-" + views, 1000,
                () => { for (int i = 0; i < 1000; i++) _fixture.Tick(); }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator TickWithActiveTransitions([Values(1, 16, 64)] int transitions)
        {
            UIProbeWindow window = _fixture.Open();
            for (int i = 0; i < transitions; i++)
            {
                UIProbeWidget child = window.Create(false);
                child.Holder.SetTransition(new UITickGateTransition());
                child.Open();
            }
            yield return AllocationCapture.Measure("tick-transitions-" + transitions, 1000,
                () => { for (int i = 0; i < 1000; i++) _fixture.Tick(); }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator CachedCycleFirstCall()
        {
            WarmSharedCode();
            Service.ShowUISync<UISecondWindow>();
            yield return AllocationCapture.Measure("cached-close-first", 1,
                () => Service.CloseUI<UISecondWindow>().GetAwaiter().GetResult(), IsZero);
            yield return AllocationCapture.Measure("cached-open-first", 1,
                () => Service.ShowUISync<UISecondWindow>(), IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator CachedCycleSteady([Values(16, 512)] int widgets)
        {
            UIProbeWindow window = _fixture.Open();
            for (int i = 0; i < widgets; i++) window.Create();
            yield return AllocationCapture.Measure("cached-cycle-" + widgets, 100, () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
                    _fixture.Open();
                }
            }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator TransitionCycleFirstCallAndSteady()
        {
            WarmSharedCode();
            UISecondWindow window = Service.ShowUISync<UISecondWindow>();
            var source = new UITickGateTransition();
            window.Holder.SetTransition(source);
            Action cycle = () =>
            {
                UniTask close = Service.CloseUI<UISecondWindow>();
                source.Finished = true;
                _fixture.Tick();
                close.GetAwaiter().GetResult();
                Service.ShowUISync<UISecondWindow>();
                source.Finished = true;
                _fixture.Tick();
            };
            yield return AllocationCapture.Measure("transition-cycle-first", 1, cycle, IsZero);
            yield return AllocationCapture.Measure("transition-cycle-steady", 100,
                () => { for (int i = 0; i < 100; i++) cycle(); }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator AsyncShowCachedPath()
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Open();
            Service.ShowUISync<UISecondWindow>();
            yield return AllocationCapture.Measure("async-show-first-call", 1,
                () => Service.ShowUI<UISecondWindow>().GetAwaiter().GetResult(), LogOnly);
            yield return AllocationCapture.Measure("async-show-opened", 100,
                () => { for (int i = 0; i < 100; i++) Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult(); }, LogOnly);
            yield return AllocationCapture.Measure("async-show-opened-steady", 100,
                () => { for (int i = 0; i < 100; i++) Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult(); }, IsZero);
            yield return AllocationCapture.Measure("async-show-cached", 100, () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
                    Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult();
                }
            }, IsZero);
            Assert.That(window.IsOpen, Is.True);
        }

        [UnityTest]
        public System.Collections.IEnumerator CloseWithPendingAwait()
        {
            UIProbeWindow window = _fixture.Open();
            var source = new UITickGateTransition();
            window.Holder.SetTransition(source);
            var awaited = new StrongBox();
            Action<object> resume = _ => awaited.Count++;
            Action cycle = () =>
            {
                UniTask task = Service.CloseUI<UIProbeWindow>();
                UniTask.Awaiter first = task.GetAwaiter();
                first.SourceOnCompleted(resume, null);
                first.SourceOnCompleted(resume, null);
                source.Finished = true;
                _fixture.Tick();
                Service.ShowUISync<UIProbeWindow>();
                source.Finished = true;
                _fixture.Tick();
            };
            yield return AllocationCapture.Measure("close-first-wait", 1, cycle, LogOnly);
            yield return AllocationCapture.Measure("close-two-waiters", 100,
                () => { for (int i = 0; i < 100; i++) cycle(); }, IsZero);
            Assert.That(awaited.Count, Is.EqualTo(202));
        }

        private sealed class StrongBox { internal int Count; }

        [UnityTest]
        public System.Collections.IEnumerator WidgetToggle([Values(false, true)] bool transition)
        {
            UIProbeWidget child = _fixture.Open().Create();
            var source = new UITickGateTransition();
            if (transition) child.Holder.SetTransition(source);
            Action finish = () =>
            {
                if (!transition) return;
                source.Finished = true;
                _fixture.Tick();
            };
            yield return AllocationCapture.Measure("widget-toggle-" + transition, 100, () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    child.Close();
                    finish();
                    child.Open();
                    finish();
                }
            }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator TabSwitchLoaded()
        {
            var window = Service.ShowUISync<UIProbeTabWindow>();
            window.SwitchTab(0).GetAwaiter().GetResult();
            window.SwitchTab(1).GetAwaiter().GetResult();
            window.SwitchTab(0).GetAwaiter().GetResult();
            yield return AllocationCapture.Measure("tab-switch-loaded", 100, () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    window.SwitchTab(1).GetAwaiter().GetResult();
                    window.SwitchTab(0).GetAwaiter().GetResult();
                }
            }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator TimedCacheCycle()
        {
            Service.ShowUISync<UITimedCacheWindow>();
            yield return AllocationCapture.Measure("timed-cache-close-first", 1,
                () => Service.CloseUI<UITimedCacheWindow>().GetAwaiter().GetResult(), LogOnly);
            yield return AllocationCapture.Measure("timed-cache-open-first", 1,
                () => Service.ShowUISync<UITimedCacheWindow>(), LogOnly);
            yield return AllocationCapture.Measure("timed-cache-cycle", 100, () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Service.CloseUI<UITimedCacheWindow>().GetAwaiter().GetResult();
                    Service.ShowUISync<UITimedCacheWindow>();
                }
            }, IsZero);
            Assert.That(Service.IsOpen<UITimedCacheWindow>(), Is.True);
        }

        [UnityTest]
        public System.Collections.IEnumerator EventRegistrationWithCachedDelegate()
        {
            int events = 0;
            Action handler = () => events++;
            var fresh = new EventListenerProxy();
            yield return AllocationCapture.Measure("event-add-first-capacity", 1,
                () => fresh.AddUIEvent<UIProbeEvent>(handler), LogOnly);
            fresh.Clear();
            var proxy = new EventListenerProxy();
            proxy.AddUIEvent<UIProbeEvent>(handler);
            proxy.Clear();
            yield return AllocationCapture.Measure("event-add-cached-delegate", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    proxy.AddUIEvent<UIProbeEvent>(handler);
                    proxy.Clear();
                }
            }, IsZero);
            yield return AllocationCapture.Measure("event-suspend-resume", 1000, () =>
            {
                proxy.AddUIEvent<UIProbeEvent>(handler);
                for (int i = 0; i < 1000; i++)
                {
                    proxy.Suspend();
                    proxy.Resume();
                }
                proxy.Clear();
            }, IsZero);
            UIFixture.Publish();
            Assert.That(events, Is.Zero);
        }

        [UnityTest]
        public System.Collections.IEnumerator EventResumeWithMethodGroup()
        {
            UIMethodGroupEventWindow window = Service.ShowUISync<UIMethodGroupEventWindow>();
            Service.CloseUI<UIMethodGroupEventWindow>().GetAwaiter().GetResult();
            Service.ShowUISync<UIMethodGroupEventWindow>();
            yield return AllocationCapture.Measure("event-resume-method-group", 100, () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Service.CloseUI<UIMethodGroupEventWindow>().GetAwaiter().GetResult();
                    Service.ShowUISync<UIMethodGroupEventWindow>();
                }
            }, IsZero);
            UIFixture.PublishWindow();
            Assert.That(window.Events, Is.EqualTo(1));
        }

        [UnityTest]
        public System.Collections.IEnumerator QueryPaths()
        {
            _fixture.Open();
            RuntimeTypeHandle handle = typeof(UIProbeWindow).TypeHandle;
            yield return AllocationCapture.Measure("query-paths", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    Service.GetUI<UIProbeWindow>();
                    Service.IsOpen<UIProbeWindow>();
                    Service.IsOpen(handle);
                    Service.TryGetTopVisibleHolder(null, out _);
                }
            }, IsZero);
        }

        [UnityTest]
        public System.Collections.IEnumerator PresetTransitionAdvance([Values] UITransitionPreset preset)
        {
            var obj = new GameObject("preset-alloc", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var transition = obj.AddComponent<UIPresetTransition>();
                Set(transition, "targetRect", (RectTransform)obj.transform);
                Set(transition, "canvasGroup", obj.GetComponent<CanvasGroup>());
                Set(transition, "useUnscaledTime", false);
                Set(transition, "openPreset", preset);
                Set(transition, "closePreset", preset);
                Set(transition, "closeDuration", 1000f);
                transition.Snap(true);
                transition.Begin(false);
                yield return AllocationCapture.Measure("preset-advance-" + preset, 1000,
                    () => { for (int i = 0; i < 1000; i++) transition.Advance(0.001f); }, IsZero);
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        private static void Set(UIPresetTransition transition, string field, object value) =>
            typeof(UIPresetTransition).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transition, value);

        [UnityTest]
        public System.Collections.IEnumerator LowFrequencyPathsStayWithinObjectBudget()
        {
            WarmSharedCode();
            UIProbeWindow window = _fixture.Open();
            yield return AllocationCapture.Measure("budget-create-widget", 1, () => window.Create(), sample =>
                TestContext.WriteLine("budget-create-widget bytes=" + sample.Bytes + " allocations=" + sample.Allocations));
            _fixture.Loader.Deferred = true;
            UniTask<UISecondWindow> loading = default;
            yield return AllocationCapture.Measure("budget-first-async-load", 1, () => loading = Service.ShowUI<UISecondWindow>(), sample =>
                TestContext.WriteLine("budget-first-async-load bytes=" + sample.Bytes + " allocations=" + sample.Allocations));
            _fixture.Loader.Deliver();
            loading.GetAwaiter().GetResult();
            yield return AllocationCapture.Measure("budget-open-with-argument", 1, () => _fixture.Open(1), sample =>
                Assert.That(sample.Allocations, Is.LessThanOrEqualTo(2)));
        }
    }

    public abstract class UIScaleCases
    {
        private const int Rounds = 31;
        private const double MaxCostRatio = 1.5;
        private const double MaxLargeTraversalCostRatio = 2.0;
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private static double FastestMilliseconds(Action action)
        {
            double fastest = double.MaxValue;
            var clock = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < Rounds; i++)
            {
                clock.Restart();
                action();
                clock.Stop();
                fastest = Math.Min(fastest, clock.Elapsed.TotalMilliseconds);
            }
            return fastest;
        }

        [Test]
        public void CostRatioDetectsLinearOperation()
        {
            var small = new List<int>(new int[16]);
            var large = new List<int>(new int[4096]);
            double smallCost = FastestMilliseconds(() => { for (int i = 0; i < 4096; i++) { small.RemoveAt(0); small.Add(i); } });
            double largeCost = FastestMilliseconds(() => { for (int i = 0; i < 4096; i++) { large.RemoveAt(0); large.Add(i); } });
            TestContext.WriteLine($"PERF,cost-ratio-control,{smallCost:F4},{largeCost:F4}");
            Assert.That(largeCost / smallCost, Is.GreaterThan(MaxLargeTraversalCostRatio));
        }

        [Test]
        public void TickIgnoresWidgetsWithoutUpdate()
        {
            UIProbeWindow window = _fixture.Open();
            for (int i = 0; i < 8; i++) window.Create();
            double small = FastestMilliseconds(() => { for (int i = 0; i < 2000; i++) _fixture.Tick(); });
            for (int i = 0; i < 4096; i++) window.CreatePlain();
            Assert.That(_fixture.Service.UpdateViews.Count, Is.EqualTo(9));
            double large = FastestMilliseconds(() => { for (int i = 0; i < 2000; i++) _fixture.Tick(); });
            TestContext.WriteLine($"PERF,tick-non-update,{small:F4},{large:F4}");
            Assert.That(large / small, Is.LessThanOrEqualTo(1.5));
            _fixture.Verify();
        }

        [Test]
        public void TickCostPerViewIsFlat()
        {
            UIProbeWindow window = _fixture.Open();
            int[] sizes = { 16, 256, 4096 };
            var perView = new double[sizes.Length];
            for (int s = 0; s < sizes.Length; s++)
            {
                while (_fixture.Service.UpdateViews.Count < sizes[s] + 1) window.Create();
                int ticks = 40960 / sizes[s];
                double milliseconds = FastestMilliseconds(() => { for (int i = 0; i < ticks; i++) _fixture.Tick(); });
                perView[s] = milliseconds / (ticks * (sizes[s] + 1));
                TestContext.WriteLine($"PERF,tick-per-view-{sizes[s]},{perView[s] * 1000000:F2}");
            }
            Assert.That(perView[1] / perView[0], Is.LessThanOrEqualTo(MaxCostRatio));
            Assert.That(perView[2] / perView[0], Is.LessThanOrEqualTo(MaxLargeTraversalCostRatio));
        }

        [Test]
        public void WidgetToggleCostIgnoresSiblingCount()
        {
            UIProbeWindow window = _fixture.Open();
            int[] sizes = { 16, 256, 4096 };
            var costs = new double[sizes.Length];
            var widgets = new List<UIProbeWidget>(sizes[2]);
            for (int s = 0; s < sizes.Length; s++)
            {
                while (widgets.Count < sizes[s]) widgets.Add(window.Create());
                UIProbeWidget middle = widgets[sizes[s] / 2];
                costs[s] = FastestMilliseconds(() =>
                {
                    for (int i = 0; i < 500; i++)
                    {
                        middle.Close();
                        middle.Open();
                    }
                });
                TestContext.WriteLine($"PERF,widget-toggle-siblings-{sizes[s]},{costs[s]:F4}");
            }
            Assert.That(costs[1] / costs[0], Is.LessThanOrEqualTo(MaxCostRatio));
            Assert.That(costs[2] / costs[0], Is.LessThanOrEqualTo(MaxCostRatio));
            _fixture.Verify();
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void TickListChurnCostIgnoresLength(int position)
        {
            int[] sizes = { 16, 256, 4096 };
            var costs = new double[sizes.Length];
            for (int s = 0; s < sizes.Length; s++)
            {
                var list = new UITickList();
                var nodes = new UITickNode[sizes[s]];
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i] = new UITickNode(null);
                    list.Link(nodes[i], 0);
                }
                int cursor = 0;
                costs[s] = FastestMilliseconds(() =>
                {
                    for (int i = 0; i < 65536; i++)
                    {
                        UITickNode node = position switch
                        {
                            0 => list.First,
                            1 => nodes[cursor++ % nodes.Length],
                            _ => nodes[nodes.Length - 1],
                        };
                        list.Unlink(node);
                        list.Link(node, 0);
                    }
                });
                Assert.That(list.Count, Is.EqualTo(sizes[s]));
                int walked = 0;
                for (UITickNode node = list.First; node != null; node = node.Next) walked++;
                Assert.That(walked, Is.EqualTo(sizes[s]));
                TestContext.WriteLine($"PERF,tick-list-churn-{position}-{sizes[s]},{costs[s]:F4}");
            }
            Assert.That(costs[1] / costs[0], Is.LessThanOrEqualTo(MaxCostRatio));
            Assert.That(costs[2] / costs[0], Is.LessThanOrEqualTo(MaxCostRatio));
        }

        private static (UILayerStack Stack, UIWindowRecord[] Records) CreateStack(int count)
        {
            var stack = new UILayerStack(0);
            var records = new UIWindowRecord[count];
            for (int i = 0; i < count; i++)
            {
                records[i] = new UIWindowRecord(null) { View = new UISecondWindow() };
                stack.Push(records[i]);
            }
            return (stack, records);
        }

        [TestCase(8)] [TestCase(64)] [TestCase(190)]
        public void RemovingMiddleOfLargeStackKeepsOtherDepths(int count)
        {
            (UILayerStack stack, UIWindowRecord[] records) = CreateStack(count);
            var depths = new int[count];
            for (int i = 0; i < count; i++) depths[i] = records[i].View.Depth;
            stack.Remove(records[count / 2]);
            for (int i = 0; i < count; i++)
                if (i != count / 2) Assert.That(records[i].View.Depth, Is.EqualTo(depths[i]));
            Assert.That(stack.Count, Is.EqualTo(count - 1));
        }

        [Test]
        public void BringToFrontCostIgnoresStackHeight()
        {
            int[] sizes = { 8, 64, 190 };
            var costs = new double[sizes.Length];
            for (int s = 0; s < sizes.Length; s++)
            {
                (UILayerStack stack, UIWindowRecord[] records) = CreateStack(sizes[s]);
                int cursor = 0;
                costs[s] = FastestMilliseconds(() =>
                {
                    for (int i = 0; i < 65536; i++)
                    {
                        UIWindowRecord record = records[cursor++ % records.Length];
                        stack.Remove(record);
                        stack.Push(record);
                    }
                });
                int previous = -1;
                for (UIWindowRecord record = stack.Bottom; record != null; record = record.StackAbove)
                {
                    Assert.That(record.Order, Is.GreaterThan(previous));
                    previous = record.Order;
                }
                Assert.That(previous, Is.LessThan(UIService.WindowsPerLayerLimit));
                TestContext.WriteLine($"PERF,bring-to-front-{sizes[s]},{costs[s]:F4}");
            }
            Assert.That(costs[1] / costs[0], Is.LessThanOrEqualTo(MaxCostRatio));
        }

        [Test]
        public void TickHookCountEqualsUpdateViews([Values(16, 256, 1024)] int views)
        {
            UIProbeWindow window = _fixture.Open();
            var children = new List<UIProbeWidget>(views);
            for (int i = 0; i < views; i++) children.Add(window.Create());
            _fixture.Tick();
            int updates = window.Updates;
            foreach (UIProbeWidget child in children) updates += child.Updates;
            Assert.That(updates, Is.EqualTo(views + 1));
        }

        [Test]
        public void ClosingMiddleWindowDoesNotTouchOtherOrders()
        {
            UIProbeWindow bottom = _fixture.Open();
            UISecondWindow middle = _fixture.Service.ShowUISync<UISecondWindow>();
            UIThirdWindow top = _fixture.Service.ShowUISync<UIThirdWindow>();
            int bottomOrder = bottom.Canvas.sortingOrder, topOrder = top.Canvas.sortingOrder;
            middle.DestroyNow();
            Assert.That(bottom.Canvas.sortingOrder, Is.EqualTo(bottomOrder));
            Assert.That(top.Canvas.sortingOrder, Is.EqualTo(topOrder));
        }

        [Test]
        public void DestroyManyWidgetsVisitsEachOnce([Values(16, 256, 2048)] int count)
        {
            UIProbeWindow window = _fixture.Open();
            var children = new List<UIProbeWidget>(count);
            for (int i = 0; i < count; i++) children.Add(window.Create());
            _fixture.Service.CloseUI<UIProbeWindow>(force: true).GetAwaiter().GetResult();
            foreach (UIProbeWidget child in children) Assert.That(child.Destroys, Is.EqualTo(1));
        }
    }
}
#endif
