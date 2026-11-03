#if UNITY_EDITOR
using System;
using System.Text.RegularExpressions;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public abstract class UIWindowStateMatrixCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private void CloseBy(string operation)
        {
            UIProbeWindow window = Service.GetWindow(typeof(UIProbeWindow).TypeHandle) as UIProbeWindow;
            switch (operation)
            {
                case "close": Service.CloseUI<UIProbeWindow>().Forget(); break;
                case "force": Service.CloseUI<UIProbeWindow>(force: true).Forget(); break;
                case "skip": Service.CloseUI<UIProbeWindow>(skipTransition: true).Forget(); break;
                case "destroy": window.DestroyNow(); break;
                default: throw new ArgumentException(operation);
            }
        }

        private IUITransitionSource Gate(UIProbeWindow window, bool tick, out Action finish)
        {
            if (tick)
            {
                var source = new UITickGateTransition();
                window.Holder.SetTransition(source);
                finish = () =>
                {
                    source.Finished = true;
                    _fixture.Tick();
                };
                return source;
            }
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            finish = gate.Finish;
            return gate;
        }

        [Test]
        public void W1_ShowJoinsLoad()
        {
            _fixture.Loader.Deferred = true;
            var first = Service.ShowUI<UIProbeWindow>("first");
            var second = Service.ShowUI<UIProbeWindow>("second");
            _fixture.Verify();
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
            _fixture.Loader.Deliver();
            UIProbeWindow window = first.GetAwaiter().GetResult();
            Assert.That(second.GetAwaiter().GetResult(), Is.SameAs(window));
            Assert.That(window.Argument, Is.EqualTo("second"));
            Assert.That(window.Initializes, Is.EqualTo(1));
            Assert.That(window.Opens, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void W1_SyncIsRejected()
        {
            _fixture.Loader.Deferred = true;
            var load = Service.ShowUI<UIProbeWindow>();
            LogAssert.Expect(LogType.Error, new Regex("asynchronous load"));
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
            _fixture.Verify();
            _fixture.Loader.Deliver();
            Assert.That(load.GetAwaiter().GetResult(), Is.Not.Null);
        }

        [TestCase("close")] [TestCase("force")] [TestCase("skip")] [TestCase("destroy")]
        public void W1_CloseVariantsCancelLoad(string operation)
        {
            _fixture.Loader.Deferred = true;
            var a = Service.ShowUI<UIProbeWindow>();
            var b = Service.ShowUI<UIProbeWindow>();
            CloseBy(operation);
            Assert.That(a.GetAwaiter().GetResult(), Is.Null);
            Assert.That(b.GetAwaiter().GetResult(), Is.Null);
            Assert.That(Service.FindRecord(typeof(UIProbeWindow).TypeHandle).Flight, Is.Null);
            _fixture.Verify();
            _fixture.Loader.Deliver();
            _fixture.Loader.Deferred = false;
            Assert.That(Service.ShowUISync<UIProbeWindow>().Initializes, Is.EqualTo(1));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(2));
            _fixture.Verify();
        }

        [Test]
        public void W1_TokenCancelsOneWaiterThenLastCancelsLoad()
        {
            using var firstToken = new CancellationTokenSource();
            using var secondToken = new CancellationTokenSource();
            _fixture.Loader.Deferred = true;
            var a = Service.ShowUI<UIProbeWindow>(firstToken.Token);
            var b = Service.ShowUI<UIProbeWindow>(secondToken.Token);
            firstToken.Cancel();
            Assert.That(a.GetAwaiter().GetResult(), Is.Null);
            Assert.That(b.Status.IsCompleted(), Is.False);
            Assert.That(Service.FindRecord(typeof(UIProbeWindow).TypeHandle).Flight, Is.Not.Null);
            _fixture.Verify();
            secondToken.Cancel();
            Assert.That(b.GetAwaiter().GetResult(), Is.Null);
            Assert.That(Service.FindRecord(typeof(UIProbeWindow).TypeHandle).View, Is.Null);
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W2_NestedShowDuringSyncCreateReturnsNull(bool asynchronous)
        {
            UIProbeWindow nested = null;
            bool nestedDone = false;
            _fixture.Loader.Bound = holder =>
            {
                _fixture.Loader.Bound = null;
                nested = asynchronous
                    ? Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult()
                    : Service.ShowUISync<UIProbeWindow>();
                nestedDone = true;
            };
            UIProbeWindow window = Service.ShowUISync<UIProbeWindow>();
            Assert.That(nestedDone, Is.True);
            Assert.That(nested, Is.Null);
            Assert.That(window.Opens, Is.EqualTo(1));
            _fixture.Verify();
        }

        [TestCase("close")] [TestCase("force")] [TestCase("skip")]
        public void W2_CloseDuringSyncCreateAborts(string operation)
        {
            _fixture.Loader.Bound = holder =>
            {
                _fixture.Loader.Bound = null;
                CloseBy(operation);
            };
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
            Assert.That(Service.FindRecord(typeof(UIProbeWindow).TypeHandle).View, Is.Null);
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W4_ShowInInitializeWaitsAndRefreshesOnce(bool holderInit)
        {
            UniTask<UIProbeWindow> nested = default;
            Action show = () =>
            {
                nested = Service.ShowUI<UIProbeWindow>("nested");
                Assert.That(nested.Status.IsCompleted(), Is.False);
            };
            ArmInitialize(holderInit, show);
            UIProbeWindow window = Service.ShowUISync<UIProbeWindow>("outer");
            Assert.That(nested.GetAwaiter().GetResult(), Is.SameAs(window));
            Assert.That(window.Initializes, Is.EqualTo(1));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Refreshes, Is.EqualTo(1));
            Assert.That(window.Argument, Is.EqualTo("nested"));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W4_SyncInInitializeReturnsBeforeOpen(bool holderInit)
        {
            UIProbeWindow nested = null;
            int opensAtReturn = -1;
            ArmInitialize(holderInit, () =>
            {
                nested = Service.ShowUISync<UIProbeWindow>("nested");
                opensAtReturn = nested.Opens;
            });
            UIProbeWindow window = Service.ShowUISync<UIProbeWindow>("outer");
            Assert.That(nested, Is.SameAs(window));
            Assert.That(opensAtReturn, Is.Zero);
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Refreshes, Is.EqualTo(1));
            _fixture.Verify();
        }

        [TestCase(false, "close")] [TestCase(false, "skip")] [TestCase(true, "close")] [TestCase(true, "skip")]
        public void W4_CloseInInitializeEntersCacheWithoutOpen(bool holderInit, string operation)
        {
            UIProbeWindow created = null;
            UIProbeWindow.Initializing = window => created = window;
            ArmInitialize(holderInit, () => CloseBy(operation));
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
            Assert.That(created.Initializes, Is.EqualTo(1));
            Assert.That(created.Opens + created.Closes, Is.Zero);
            UIProbeWindow.Initializing = null;
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.SameAs(created));
            Assert.That(created.Initializes, Is.EqualTo(1));
            Assert.That(created.Opens, Is.EqualTo(1));
            _fixture.Verify();
        }

        [TestCase(false, "force")] [TestCase(false, "destroy")] [TestCase(true, "force")] [TestCase(true, "destroy")]
        public void W4_DestroyInInitializeReleasesInstance(bool holderInit, string operation)
        {
            UIProbeWindow created = null;
            if (holderInit)
            {
                _fixture.Loader.Bound = holder => holder.OnWindowInitEvent += () =>
                {
                    created = Service.GetWindow(typeof(UIProbeWindow).TypeHandle) as UIProbeWindow;
                    CloseBy(operation);
                };
            }
            else
            {
                UIProbeWindow.Initializing = window =>
                {
                    created = window;
                    CloseBy(operation);
                };
            }
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
            Assert.That(created.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(created.Opens + created.Closes, Is.Zero);
            Assert.That(created.Initializes, Is.EqualTo(holderInit ? 0 : 1));
            Assert.That(created.Destroys, Is.EqualTo(holderInit ? 0 : 1));
            _fixture.Verify();
        }

        [Test]
        public void W4_TokenCancelledDuringInitialize()
        {
            using var cancellation = new CancellationTokenSource();
            UIProbeWindow created = null;
            UIProbeWindow.Initializing = window =>
            {
                created = window;
                cancellation.Cancel();
            };
            Assert.That(Service.ShowUI<UIProbeWindow>(cancellation.Token).GetAwaiter().GetResult(), Is.Null);
            Assert.That(created.Opens, Is.EqualTo(1));
            Assert.That(created.Closes, Is.EqualTo(1));
            Assert.That(created.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W6_ShowDuringOpeningCoalescesRefresh(bool tick)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Gate(window, tick, out Action finish);
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            Assert.That(Service.ShowUISync<UIProbeWindow>("latest"), Is.SameAs(window));
            Assert.That(Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult(), Is.SameAs(window));
            Assert.That(window.Refreshes, Is.Zero);
            _fixture.Verify();
            finish();
            Assert.That(window.Refreshes, Is.EqualTo(1));
            Assert.That(window.Argument, Is.EqualTo("latest"));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W6_CloseDuringOpeningReleasesOldWaitersAfterOnClose(bool tick)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Gate(window, tick, out Action finish);
            _fixture.Open();
            int closesAtResume = -1;
            window.AwaitTransition().GetAwaiter().OnCompleted(() => closesAtResume = window.Closes);
            UniTask closing = Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            Assert.That(window.IsOpen, Is.False);
            Assert.That(closesAtResume, Is.EqualTo(2));
            Assert.That(closing.Status.IsCompleted(), Is.False);
            _fixture.Verify();
            finish();
            closing.GetAwaiter().GetResult();
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W6_ForceDuringOpeningDestroysAfterTransition(bool tick)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Gate(window, tick, out Action finish);
            _fixture.Open();
            UniTask closing = Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.Destroys, Is.Zero);
            finish();
            closing.GetAwaiter().GetResult();
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(window.Closes, Is.EqualTo(2));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W6_SkipDuringOpeningSnapsSynchronously(bool tick)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Gate(window, tick, out _);
            _fixture.Open();
            UniTask closing = Service.CloseUI<UIProbeWindow>(skipTransition: true);
            Assert.That(closing.Status.IsCompleted(), Is.True);
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W6_DestroyDuringOpeningPairsOnCloseOnly(bool tick)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Gate(window, tick, out _);
            int beforeClosed = 0, afterClosed = 0, destroyed = 0;
            window.Holder.OnWindowBeforeClosedEvent += () => beforeClosed++;
            window.Holder.OnWindowAfterClosedEvent += () => afterClosed++;
            window.Holder.OnWindowDestroyEvent += () => destroyed++;
            _fixture.Open();
            UniTask waiting = window.AwaitTransition();
            window.DestroyNow();
            Assert.That(waiting.Status.IsCompleted(), Is.True);
            Assert.That(window.Closes, Is.EqualTo(2));
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(beforeClosed + afterClosed, Is.Zero);
            Assert.That(destroyed, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void W7_ShowRefreshesAndMovesToTop()
        {
            UIProbeWindow window = _fixture.Open();
            UISecondWindow second = Service.ShowUISync<UISecondWindow>();
            Assert.That(second.Depth, Is.GreaterThan(window.Depth));
            _fixture.Open("again");
            Assert.That(window.Refreshes, Is.EqualTo(1));
            Assert.That(window.Depth, Is.GreaterThan(second.Depth));
            _fixture.Verify();
        }

        [Test]
        public void W7_CancelledTokenDoesNotRefresh()
        {
            UIProbeWindow window = _fixture.Open();
            UISecondWindow second = Service.ShowUISync<UISecondWindow>();
            int depth = window.Depth;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(Service.ShowUI<UIProbeWindow>(cancellation.Token).GetAwaiter().GetResult(), Is.Null);
            Assert.That(window.Refreshes, Is.Zero);
            Assert.That(window.Depth, Is.EqualTo(depth));
            Assert.That(second.Depth, Is.GreaterThan(depth));
        }

        [TestCase("force", 1, 1)] [TestCase("destroy", 1, 1)] [TestCase("skip", 1, 0)] [TestCase("close", 1, 0)]
        public void W7_CloseVariants(string operation, int closes, int destroys)
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            CloseBy(operation);
            Assert.That(window.Closes, Is.EqualTo(closes));
            Assert.That(window.Destroys, Is.EqualTo(destroys));
            Assert.That(child.Destroys, Is.EqualTo(destroys));
            Assert.That(child.Closes, Is.EqualTo(destroys));
            _fixture.Verify();
        }

        [Test]
        public void W8_ShowIsIgnoredWhileClosing()
        {
            UIProbeWindow window = _fixture.Open("old");
            Gate(window, false, out Action finish);
            Service.CloseUI<UIProbeWindow>().Forget();
            Assert.That(Service.ShowUISync<UIProbeWindow>("sync"), Is.Null);
            Assert.That(Service.ShowUI<UIProbeWindow>("async").GetAwaiter().GetResult(), Is.Null);
            Assert.That(window.Argument, Is.EqualTo("old"));
            _fixture.Verify();
            finish();
        }

        [Test]
        public void W8_RepeatedCloseSharesRoundAndSkipDoesNotInterrupt()
        {
            UIProbeWindow window = _fixture.Open();
            Gate(window, false, out Action finish);
            UniTask first = Service.CloseUI<UIProbeWindow>();
            UniTask second = Service.CloseUI<UIProbeWindow>(skipTransition: true);
            Assert.That(first.Status.IsCompleted() || second.Status.IsCompleted(), Is.False);
            Assert.That(window.Closes, Is.EqualTo(1));
            finish();
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.That(window.Closes, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void W8_ForceUpgradesRunningClose()
        {
            UIProbeWindow window = _fixture.Open();
            Gate(window, false, out Action finish);
            Service.CloseUI<UIProbeWindow>().Forget();
            UniTask force = Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.Destroys, Is.Zero);
            finish();
            force.GetAwaiter().GetResult();
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void W8_DestroyCompletesGateImmediately(bool tick)
        {
            UIProbeWindow window = _fixture.Open();
            Gate(window, tick, out _);
            UniTask closing = Service.CloseUI<UIProbeWindow>();
            window.DestroyNow();
            Assert.That(closing.Status.IsCompleted(), Is.True);
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            _fixture.Tick();
            _fixture.Verify();
        }

        [Test]
        public void W10_ShowRestoresCachedInstance()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            int loads = _fixture.Loader.Loads;
            Assert.That(_fixture.Open(), Is.SameAs(window));
            Assert.That(window.Initializes, Is.EqualTo(1));
            Assert.That(window.Opens, Is.EqualTo(2));
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(loads));
            _fixture.Verify();
        }

        [TestCase("close")] [TestCase("skip")]
        public void W10_CloseIsNoOp(string operation)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            CloseBy(operation);
            Assert.That(window.Closes, Is.EqualTo(1));
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
        }

        [TestCase("force")] [TestCase("destroy")]
        public void W10_DestroyLeavesCache(string operation)
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            CloseBy(operation);
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(Service.FindRecord(typeof(UIProbeWindow).TypeHandle).IsCached, Is.False);
            _fixture.Verify();
        }

        [Test]
        public void W10_CancelledTokenKeepsWindowCached()
        {
            _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(Service.ShowUI<UIProbeWindow>(cancellation.Token).GetAwaiter().GetResult(), Is.Null);
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
        }

        [Test]
        public void W12_ShowCreatesNewInstanceAndOldCloseSelfIsIgnored()
        {
            UIProbeWindow old = _fixture.Open();
            old.DestroyNow();
            UIProbeWindow current = _fixture.Open();
            Assert.That(current, Is.Not.SameAs(old));
            old.CloseThis().GetAwaiter().GetResult();
            Assert.That(current.IsOpen, Is.True);
            Assert.That(current.Closes, Is.Zero);
            old.DestroyNow();
            Assert.That(old.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void W12_CloseByTypeAffectsCurrentInstanceOnly()
        {
            UIProbeWindow old = _fixture.Open();
            old.DestroyNow();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            UIProbeWindow current = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(current.Closes, Is.EqualTo(1));
            Assert.That(old.Closes, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void ServiceStopped_RejectsAllRequests()
        {
            UIProbeWindow window = _fixture.Open();
            AppServices.Shutdown();
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            LogAssert.Expect(LogType.Error, new Regex("not running"));
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
            LogAssert.Expect(LogType.Error, new Regex("not running"));
            Assert.That(Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult(), Is.Null);
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
        }

        [Test]
        public void ServiceNotInitialized_RejectsShow()
        {
            var service = new UIService(_fixture.Loader);
            LogAssert.Expect(LogType.Error, new Regex("not running"));
            Assert.That(service.ShowUISync<UIProbeWindow>(), Is.Null);
            service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
        }

        private void ArmInitialize(bool holderInit, Action action)
        {
            if (holderInit)
            {
                _fixture.Loader.Bound = holder =>
                {
                    _fixture.Loader.Bound = null;
                    holder.OnWindowInitEvent += action;
                };
                return;
            }
            Action<UIProbeWindow> previous = UIProbeWindow.Initializing;
            UIProbeWindow.Initializing = window =>
            {
                previous?.Invoke(window);
                UIProbeWindow.Initializing = null;
                action();
            };
        }
    }

    internal static class UIRecordAssert
    {
        internal static void IsCached(UIService service, Type type)
        {
            UIWindowRecord record = service.FindRecord(type.TypeHandle);
            Assert.That(record.IsCached, Is.True, "Expected cached window: " + type.Name);
            Assert.That(record.IsStacked, Is.False);
        }
    }
}
#endif
