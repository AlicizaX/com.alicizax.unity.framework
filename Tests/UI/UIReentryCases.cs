using AlicizaX.UI.Runtime;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    public abstract class UIReentryCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [Test]
        public void HolderInitShowWaitsForBusinessInitialization()
        {
            UIProbeWindow nested = null;
            int opensInInit = -1;
            _fixture.Loader.Bound = holder => holder.OnWindowInitEvent += () =>
            {
                nested = _fixture.Service.ShowUISync<UIProbeWindow>("nested");
                opensInInit = nested.Opens;
            };
            var window = _fixture.Service.ShowUISync<UIProbeWindow>("original");
            Assert.That(opensInInit, Is.Zero);
            Assert.That(nested, Is.SameAs(window));
            Assert.That(window.Initializes, Is.EqualTo(1));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Argument, Is.EqualTo("nested"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InitializeCloseCancelsWidgetAutomaticOpen(bool reopen)
        {
            UIProbeWidget.Initializing = widget =>
            {
                widget.Close();
                if (reopen) widget.Open("again");
            };
            var child = _fixture.Open().Create();
            Assert.That(child.Initializes, Is.EqualTo(1));
            Assert.That(child.IsOpen, Is.EqualTo(reopen));
            Assert.That(child.Opens, Is.EqualTo(reopen ? 1 : 0));
            Assert.That(child.Closes, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WidgetOpenInInitializeUsesLatestArguments(bool automaticOpen)
        {
            UIProbeWidget.Initializing = widget => { widget.Open("latest"); widget.Open(); };
            var child = _fixture.Open().Create(automaticOpen);
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Argument, Is.EqualTo("latest"));
        }

        [Test]
        public void WindowInitializeShowDoesNotOverwriteNewArguments()
        {
            UIProbeWindow nested = null;
            UIProbeWindow.Initializing = window => nested = _fixture.Service.ShowUISync<UIProbeWindow>("new");
            var opened = _fixture.Service.ShowUISync<UIProbeWindow>("old");
            Assert.That(nested, Is.SameAs(opened));
            Assert.That(opened.Argument, Is.EqualTo("new"));
            Assert.That(opened.Opens, Is.EqualTo(1));
        }

        [Test]
        public void OpenInsideOnOpenCoalescesAfterAnimation()
        {
            var child = _fixture.Open().Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Opening = () => { child.Open("new"); child.Open(); };
            child.Open("old");
            Assert.That(child.Argument, Is.EqualTo("new"));
            Assert.That(child.Refreshes, Is.Zero);
            gate.Finish();
            Assert.That(child.Refreshes, Is.EqualTo(1));
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [Test]
        public void OpenInsideRefreshOnlyUpdatesData()
        {
            var child = _fixture.Open().Create();
            child.Refreshing = () => child.Open("inside");
            child.Open("outside");
            Assert.That(child.Argument, Is.EqualTo("inside"));
            Assert.That(child.Refreshes, Is.EqualTo(1));
        }

        [Test]
        public void OpenInsideRegistrationRefreshesAfterCallback()
        {
            var window = _fixture.Open();
            var child = window.Create();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            int refreshesInside = -1;
            child.Registering = () =>
            {
                child.Open("registered");
                child.Open();
                refreshesInside = child.Refreshes;
            };
            _fixture.Open();
            Assert.That(refreshesInside, Is.Zero);
            Assert.That(child.Refreshes, Is.EqualTo(1));
            Assert.That(child.Argument, Is.EqualTo("registered"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void RootReopenDuringUpdateEndsOldTraversal(int depth)
        {
            var window = _fixture.Open();
            UIProbeWidget actor = null;
            for (int i = 0; i < depth; i++) actor = actor == null ? window.Create() : actor.Create();
            var sibling = window.Create();
            var gate = new UIGateTransition();
            void Reopen()
            {
                _fixture.Service.CloseUI<UIProbeWindow>(skipTransition: true).GetAwaiter().GetResult();
                window.Holder.SetTransition(gate);
                _fixture.Open();
            }
            if (actor == null) window.Updating = Reopen;
            else actor.Updating = Reopen;
            _fixture.Tick();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            Assert.That(sibling.Updates, Is.Zero);
            window.Updating = null;
            if (actor != null) actor.Updating = null;
            gate.Finish();
            _fixture.Tick();
            Assert.That(sibling.Updates, Is.EqualTo(1));
        }

        [Test]
        public void InterruptedOpeningWaiterMayDestroyParent()
        {
            var window = _fixture.Open();
            var child = window.Create(false);
            child.Holder.SetTransition(new UIGateTransition());
            child.Open();
            bool resumed = false;
            child.AwaitTransition().GetAwaiter().OnCompleted(() =>
            {
                resumed = true;
                window.DestroyNow();
            });
            child.Close();
            Assert.That(resumed, Is.True);
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.State, Is.EqualTo(UIState.Destroyed));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AsyncShowInsideOnOpenWaitsForLogicalOpen(bool close)
        {
            UniTask<UIProbeWindow> nested = default;
            bool completedInside = true;
            UIProbeWindow.Initializing = window => window.Opening = () =>
            {
                nested = _fixture.Service.ShowUI<UIProbeWindow>("inner");
                completedInside = nested.Status.IsCompleted();
                if (close) _fixture.Service.CloseUI<UIProbeWindow>().Forget();
            };
            var outer = _fixture.Open();
            Assert.That(completedInside, Is.False);
            Assert.That(nested.GetAwaiter().GetResult(), Is.SameAs(outer));
            if (close) Assert.That(outer, Is.Null);
            else Assert.That(outer.Argument, Is.EqualTo("inner"));
        }

        [Test]
        public void DestroySettlesCloseEvenWhenAnimationIgnoresCancellation()
        {
            var window = _fixture.Open();
            var delayed = new UniTaskCompletionSource();
            window.Holder.SetTransition((_, token) => delayed.Task, _ => { });
            var closing = _fixture.Service.CloseUI<UIProbeWindow>();
            window.DestroyNow();
            bool completed = closing.Status.IsCompleted();
            delayed.TrySetResult();
            Assert.That(completed, Is.True);
            closing.GetAwaiter().GetResult();
            Assert.That(window.Destroys, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void StopAnimationCallbackMayDestroyDuringOppositeTransition(bool opening)
        {
            var child = _fixture.Open().Create(false);
            var pending = new UniTaskCompletionSource();
            child.Holder.SetTransition((_, token) =>
            {
                token.Register(child.DestroyNow);
                return pending.Task;
            }, _ => { });
            child.Open();
            if (opening) { pending.TrySetResult(); child.Close(); }
            else child.Close();
            if (opening) child.DestroyNow();
            pending.TrySetResult();
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(child.IsOpen, Is.False);
        }

        [Test]
        public void RefreshCanCloseThenRequestNewOpen()
        {
            var child = _fixture.Open().Create();
            child.Refreshing = () =>
            {
                child.Refreshing = null;
                child.Close();
                child.Open("reopened");
            };
            child.Open("refresh");
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.Opens, Is.EqualTo(2));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Refreshes, Is.EqualTo(1));
            Assert.That(child.Argument, Is.EqualTo("reopened"));
        }

        [Test]
        public void HolderAfterCloseCanReopenWithoutOldCloseCachingNewLifetime()
        {
            var window = _fixture.Open();
            Action reopen = null;
            reopen = () =>
            {
                window.Holder.OnWindowAfterClosedEvent -= reopen;
                _fixture.Service.ShowUISync<UIProbeWindow>("reopened");
            };
            window.Holder.OnWindowAfterClosedEvent += reopen;
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(window.IsOpen, Is.True);
            Assert.That(window.Argument, Is.EqualTo("reopened"));
            Assert.That(window.Holder.GetComponent<UnityEngine.Canvas>().enabled, Is.True);
            _fixture.Tick();
            Assert.That(window.Updates, Is.EqualTo(1));
        }

        [Test]
        public void AsyncShowFromAfterCloseWaitsForDeferredReopen()
        {
            var window = _fixture.Open();
            UniTask<UIProbeWindow> opening = default;
            Action reopen = null;
            reopen = () =>
            {
                window.Holder.OnWindowAfterClosedEvent -= reopen;
                opening = _fixture.Service.ShowUI<UIProbeWindow>("again");
            };
            window.Holder.OnWindowAfterClosedEvent += reopen;
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(opening.GetAwaiter().GetResult(), Is.SameAs(window));
            Assert.That(window.IsOpen, Is.True);
        }

        [Test]
        public void DeferredWidgetOpenRequestedThenClosedInsideCallbackStaysClosed()
        {
            var child = _fixture.Open().Create();
            Action reopen = null;
            reopen = () =>
            {
                child.Holder.OnWindowAfterClosedEvent -= reopen;
                child.Open("cancelled");
                child.Close();
            };
            child.Holder.OnWindowAfterClosedEvent += reopen;
            child.Close();
            Assert.That(child.IsOpen, Is.False);
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [Test]
        public void OpenInUpdateCoalescesUntilCallbackReturns()
        {
            var child = _fixture.Open().Create();
            int inside = -1;
            child.Updating = () => { child.Open("one"); child.Open("two"); inside = child.Refreshes; };
            _fixture.Tick();
            Assert.That(inside, Is.Zero);
            Assert.That(child.Refreshes, Is.EqualTo(1));
            Assert.That(child.Argument, Is.EqualTo("two"));
        }

        [Test]
        public void CancelNestedAsyncShowDoesNotCancelOuterOpen()
        {
            using var cancellation = new CancellationTokenSource();
            UniTask<UIProbeWindow> nested = default;
            UIProbeWindow.Initializing = window => window.Opening = () =>
            {
                nested = _fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token, "inner");
                cancellation.Cancel();
            };
            var result = _fixture.Open();
            Assert.That(nested.GetAwaiter().GetResult(), Is.Null);
            Assert.That(result.IsOpen, Is.True);
        }

        [Test]
        public void CloseCompletionMayStartAnotherCloseWithoutJoiningOldSettlement()
        {
            var window = _fixture.Open();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            var first = _fixture.Service.CloseUI<UIProbeWindow>();
            UniTask second = default;
            first.GetAwaiter().OnCompleted(() =>
            {
                _fixture.Open();
                second = _fixture.Service.CloseUI<UIProbeWindow>();
            });
            gate.Finish();
            Assert.That(second.Status.IsCompleted(), Is.False);
            gate.Finish();
            second.GetAwaiter().GetResult();
            Assert.That(window.Closes, Is.EqualTo(2));
        }
    }
}
