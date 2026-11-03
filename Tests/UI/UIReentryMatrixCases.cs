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
    public abstract class UIReentryMatrixCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private UIProbeWindow Cached()
        {
            UIProbeWindow window = _fixture.Open();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            return window;
        }

        [Test]
        public void BeforeShow_CloseSkipsOpenAndClose()
        {
            UIProbeWindow window = Cached();
            Action hook = null;
            hook = () =>
            {
                window.Holder.OnWindowBeforeShowEvent -= hook;
                Service.CloseUI<UIProbeWindow>().Forget();
            };
            window.Holder.OnWindowBeforeShowEvent += hook;
            Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Closes, Is.EqualTo(1));
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
        }

        [Test]
        public void OnOpen_SyncShowReturnsBeforeLogicalOpen()
        {
            UIProbeWindow window = Cached();
            UIProbeWindow nested = null;
            bool openInside = true;
            window.Opening = () =>
            {
                window.Opening = null;
                nested = Service.ShowUISync<UIProbeWindow>();
                openInside = nested.IsOpen;
            };
            _fixture.Open();
            Assert.That(nested, Is.SameAs(window));
            Assert.That(openInside, Is.False);
            Assert.That(window.Refreshes, Is.EqualTo(1));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void OnOpen_ClosePairsHooksWithoutRegistration(bool force)
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            child.Opening = () =>
            {
                child.Opening = null;
                if (force) child.Destroy().Forget();
                else child.Close();
            };
            child.Open();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.Zero);
            Assert.That(child.IsOpen, Is.False);
            Assert.That(child.Destroys, Is.EqualTo(force ? 1 : 0));
            _fixture.Verify();
        }

        [Test]
        public void AfterShow_ShowRefreshesAfterHook()
        {
            UIProbeWindow window = Cached();
            Action hook = null;
            bool openInside = false;
            hook = () =>
            {
                window.Holder.OnWindowAfterShowEvent -= hook;
                openInside = window.IsOpen;
                Assert.That(Service.ShowUISync<UIProbeWindow>("hook"), Is.SameAs(window));
            };
            window.Holder.OnWindowAfterShowEvent += hook;
            _fixture.Open();
            Assert.That(openInside, Is.True);
            Assert.That(window.Refreshes, Is.EqualTo(1));
            Assert.That(window.Argument, Is.EqualTo("hook"));
            _fixture.Verify();
        }

        [Test]
        public void OnRefresh_CloseThenOpenWithRunningTransitionIgnoresOpen()
        {
            UIProbeWidget child = _fixture.Open().Create();
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Refreshing = () =>
            {
                child.Refreshing = null;
                child.Close();
                child.Open("ignored");
            };
            child.Open("refresh");
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            Assert.That(child.Opens, Is.EqualTo(1));
            gate.Finish();
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            _fixture.Verify();
        }

        [Test]
        public void OnRegisterEvent_DestroyReleasesProxyAfterHook()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            child.Registering = () => child.DestroyNow();
            child.Open();
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(EventBus.GetEmptySubscriberCount<UIProbeEvent>(), Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void OnRegisterEvent_CloseLeavesNoUpdateNode()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            child.Registering = () => child.Close();
            child.Open();
            Assert.That(child.IsOpen, Is.False);
            Assert.That(child.Effective, Is.False);
            _fixture.Tick();
            Assert.That(child.Updates, Is.Zero);
            UIFixture.Publish();
            Assert.That(child.Events, Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void OnUpdate_RemoveNextAndAllNodes()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(), b = window.Create(), c = window.Create();
            window.Updating = () =>
            {
                window.Updating = null;
                a.DestroyNow();
                b.DestroyNow();
                c.DestroyNow();
            };
            _fixture.Tick();
            Assert.That(a.Updates + b.Updates + c.Updates, Is.Zero);
            Assert.That(Service.UpdateViews.Count, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void OnUpdate_ExceptionDoesNotStopOtherNodes()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(), b = window.Create();
            a.Updating = () => throw new InvalidOperationException("expected update failure");
            LogAssert.Expect(LogType.Exception, new Regex("expected update failure"));
            _fixture.Tick();
            Assert.That(b.Updates, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void OnClose_ShowIsIgnoredAndCloseShares()
        {
            UIProbeWindow window = _fixture.Open();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            window.Closing = () =>
            {
                window.Closing = null;
                Assert.That(Service.ShowUISync<UIProbeWindow>(), Is.Null);
                Service.CloseUI<UIProbeWindow>().Forget();
            };
            UniTask closing = Service.CloseUI<UIProbeWindow>();
            Assert.That(window.Closes, Is.EqualTo(1));
            gate.Finish();
            closing.GetAwaiter().GetResult();
            Assert.That(window.Closes, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void BeforeClose_DestroyStillPairsOnClose()
        {
            UIProbeWindow window = _fixture.Open();
            Action hook = null;
            hook = () =>
            {
                window.Holder.OnWindowBeforeClosedEvent -= hook;
                window.DestroyNow();
            };
            window.Holder.OnWindowBeforeClosedEvent += hook;
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void OnClose_DestroyDoesNotDuplicateClose()
        {
            UIProbeWindow window = _fixture.Open();
            window.Closing = () => window.DestroyNow();
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void B5_AfterClose_ShowThenCloseStaysCached()
        {
            UIProbeWindow window = _fixture.Open();
            UniTask<UIProbeWindow> reopening = default;
            Action hook = null;
            hook = () =>
            {
                window.Holder.OnWindowAfterClosedEvent -= hook;
                reopening = Service.ShowUI<UIProbeWindow>("cancelled");
                Service.CloseUI<UIProbeWindow>().Forget();
            };
            window.Holder.OnWindowAfterClosedEvent += hook;
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(reopening.GetAwaiter().GetResult(), Is.Null);
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Canvas.enabled, Is.False);
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            _fixture.Verify();
            Assert.That(_fixture.Open(), Is.SameAs(window));
            Assert.That(window.Canvas.enabled, Is.True);
            _fixture.Verify();
        }

        [Test]
        public void AfterClose_CloseFinalizesOnce()
        {
            UIProbeWindow window = _fixture.Open();
            Action hook = null;
            hook = () =>
            {
                window.Holder.OnWindowAfterClosedEvent -= hook;
                Service.CloseUI<UIProbeWindow>().Forget();
                Assert.That(Service.FindRecord(typeof(UIProbeWindow).TypeHandle).IsCached, Is.False);
            };
            window.Holder.OnWindowAfterClosedEvent += hook;
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            UIRecordAssert.IsCached(Service, typeof(UIProbeWindow));
            Assert.That(((IUIDebugService)Service).CacheWindowCount, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void AfterClose_ForceDestroys()
        {
            UIProbeWindow window = _fixture.Open();
            Action hook = null;
            hook = () =>
            {
                window.Holder.OnWindowAfterClosedEvent -= hook;
                Service.CloseUI<UIProbeWindow>(force: true).Forget();
            };
            window.Holder.OnWindowAfterClosedEvent += hook;
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(window.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void OnDestroy_ShowCreatesNewInstance()
        {
            UIProbeWindow old = _fixture.Open();
            UIProbeWindow created = null;
            old.Destroying = () =>
            {
                old.Destroying = null;
                created = _fixture.Open();
            };
            old.DestroyNow();
            Assert.That(created, Is.Not.Null.And.Not.SameAs(old));
            Assert.That(created.IsOpen, Is.True);
            Assert.That(old.State, Is.EqualTo(UIState.Destroyed));
            _fixture.Verify();
        }

        [Test]
        public void OnDestroy_CreateWidgetIsRejected()
        {
            UIProbeWindow window = _fixture.Open();
            window.Destroying = () =>
            {
                LogAssert.Expect(LogType.Error, new Regex("requested destruction"));
                Assert.That(window.Create(), Is.Null);
            };
            window.DestroyNow();
            _fixture.Verify();
        }

        [Test]
        public void B3_ShowDuringStopCreatesNoWindow()
        {
            UIProbeWindow window = _fixture.Open();
            Service.ShowUISync<UISecondWindow>();
            UIProbeWindow nested = _fixture.Open();
            int created = _fixture.Loader.Objects.Count;
            window.Destroying = () =>
            {
                LogAssert.Expect(LogType.Error, new Regex("not running"));
                nested = Service.ShowUISync<UIProbeWindow>();
            };
            AppServices.Shutdown();
            Assert.That(nested, Is.Null);
            Assert.That(_fixture.Loader.Objects.Count, Is.EqualTo(created));
        }

        [Test]
        public void Transition_BeginCallbackMayDestroyView()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition();
            source.BeginAction = () => child.DestroyNow();
            child.Holder.SetTransition(source);
            child.Open();
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(Service.TransitionViews.Count, Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void Transition_AdvanceCallbackMayDestroyView()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            child.Open();
            source.AdvanceAction = () =>
            {
                child.DestroyNow();
                source.Finished = true;
            };
            _fixture.Tick();
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(Service.TransitionViews.Count, Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void Transition_AdvanceCallbackMayStartNextRoundOnSameSource()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            child.Open();
            source.AdvanceAction = () =>
            {
                source.AdvanceAction = null;
                child.Close();
                source.Finished = true;
            };
            _fixture.Tick();
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            Assert.That(source.Begins, Is.EqualTo(2));
            source.Finished = true;
            _fixture.Tick();
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            _fixture.Verify();
        }

        [TestCase(true)] [TestCase(false)]
        public void Transition_SetTransitionDuringPlayFinishesCurrentPhase(bool tick)
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            if (tick) child.Holder.SetTransition(new UITickGateTransition());
            else child.Holder.SetTransition(new UIGateTransition());
            child.Open();
            Assert.That(child.State, Is.EqualTo(UIState.Opening));
            child.Holder.SetTransition(null);
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            _fixture.Verify();
            child.Holder.SetTransition(tick ? new UITickGateTransition() : (IUITransitionSource)new UIGateTransition());
            child.Close();
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            child.Holder.SetTransition(null);
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            _fixture.Verify();
        }

        [Test]
        public void Transition_SnapCallbackMayDestroyView()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            source.SnapAction = () => child.DestroyNow();
            child.Open();
            Assert.That(child.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Continuation_GateCompletionMayStartNewRound()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Open();
            UniTask first = child.AwaitTransition();
            bool resumed = false;
            first.GetAwaiter().OnCompleted(() =>
            {
                resumed = true;
                child.Close();
            });
            gate.Finish();
            Assert.That(resumed, Is.True);
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            Assert.That(first.Status.IsCompleted(), Is.True);
            Assert.That(child.AwaitTransition().Status.IsCompleted(), Is.False);
            gate.Finish();
            _fixture.Verify();
        }
    }
}
#endif
