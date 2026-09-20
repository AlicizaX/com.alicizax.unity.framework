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
    public abstract class UIWindowCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [TestCase(false)] [TestCase(true)]
        public void RepeatedShowKeepsOneInstanceAndLatestNonEmptyArguments(bool async)
        {
            var window = async ? _fixture.Service.ShowUI<UIProbeWindow>("first").GetAwaiter().GetResult()
                : _fixture.Service.ShowUISync<UIProbeWindow>("first");
            var second = _fixture.Service.ShowUI<UIProbeWindow>("second").GetAwaiter().GetResult();
            var third = _fixture.Service.ShowUISync<UIProbeWindow>();
            Assert.That(second, Is.SameAs(window));
            Assert.That(third, Is.SameAs(window));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Refreshes, Is.EqualTo(2));
            Assert.That(window.Argument, Is.EqualTo("second"));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SharedLoadCancellationOnlyAffectsCancelledCaller(int cancelled)
        {
            _fixture.Loader.Deferred = true;
            using var a = new CancellationTokenSource();
            using var b = new CancellationTokenSource();
            using var c = new CancellationTokenSource();
            var tokens = new[] { a, b, c };
            var first = _fixture.Service.ShowUI<UIProbeWindow>(a.Token, "first");
            var second = _fixture.Service.ShowUI<UIProbeWindow>(b.Token, "second");
            var third = _fixture.Service.ShowUI<UIProbeWindow>(c.Token);
            tokens[cancelled].Cancel();
            Assert.That(_fixture.Loader.PendingToken.IsCancellationRequested, Is.False);
            _fixture.Loader.Deliver();
            var results = new[] { first.GetAwaiter().GetResult(), second.GetAwaiter().GetResult(), third.GetAwaiter().GetResult() };
            Assert.That(results[cancelled], Is.Null);
            var window = results[(cancelled + 1) % 3];
            Assert.That(window.Argument, Is.EqualTo(cancelled == 1 ? "first" : "second"));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void CancelledLoadMayBeReplacedBeforeOldResourceArrives(bool closeInsteadOfCancel)
        {
            _fixture.Loader.Deferred = true;
            using var cancellation = new CancellationTokenSource();
            var first = _fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token, "old");
            if (closeInsteadOfCancel) _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            else cancellation.Cancel();
            Assert.That(first.GetAwaiter().GetResult(), Is.Null);
            var second = _fixture.Service.ShowUI<UIProbeWindow>("new");
            _fixture.Loader.Deliver(1);
            var replacement = second.GetAwaiter().GetResult();
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(replacement));
            Assert.That(replacement.Argument, Is.EqualTo("new"));
            Assert.That(replacement.IsOpen, Is.True);
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(2));
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void LoadFailuresReturnNullAndAllowRetry(bool async, bool throws)
        {
            _fixture.Loader.Deferred = async;
            if (throws) _fixture.Loader.Failure = new InvalidOperationException("loader failure");
            else _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex(throws ? "Failed to load" : "could not be loaded"));
            if (async)
            {
                var task = _fixture.Service.ShowUI<UIProbeWindow>();
                _fixture.Loader.Deliver();
                Assert.That(task.GetAwaiter().GetResult(), Is.Null);
            }
            else Assert.That(_fixture.Open(), Is.Null);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
            _fixture.Loader.Fail = false;
            _fixture.Loader.Failure = null;
            _fixture.Loader.Deferred = false;
            Assert.That(_fixture.Open().IsOpen, Is.True);
        }

        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void InitializeMayCloseOrDestroyWindow(bool async, bool destroy)
        {
            UIProbeWindow instance = null;
            UIProbeWindow.Initializing = window =>
            {
                instance = window;
                _fixture.Service.CloseUI<UIProbeWindow>(force: destroy).Forget();
            };
            var result = async ? _fixture.Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult() : _fixture.Open();
            Assert.That(result, Is.Null);
            Assert.That(instance.Opens + instance.Closes, Is.Zero);
            Assert.That(instance.Destroys, Is.EqualTo(destroy ? 1 : 0));
            if (!destroy)
            {
                UIProbeWindow.Initializing = null;
                Assert.That(_fixture.Open(), Is.SameAs(instance));
                Assert.That(instance.Initializes, Is.EqualTo(1));
                Assert.That(instance.Opens, Is.EqualTo(1));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void RepeatedCloseSharesOneTransitionAndForceMayUpgrade(bool force)
        {
            var window = _fixture.Open();
            var child = window.Create();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            var first = _fixture.Service.CloseUI<UIProbeWindow>();
            var second = _fixture.Service.CloseUI<UIProbeWindow>(force);
            LogAssert.Expect(LogType.Warning, new Regex("Ignoring Show"));
            Assert.That(_fixture.Service.ShowUI<UIProbeWindow>("ignored").GetAwaiter().GetResult(), Is.Null);
            Assert.That(first.Status.IsCompleted(), Is.False);
            Assert.That(second.Status.IsCompleted(), Is.False);
            Assert.That(window.Closes, Is.EqualTo(1));
            gate.Finish();
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.That(window.Destroys, Is.EqualTo(force ? 1 : 0));
            Assert.That(child.Destroys, Is.EqualTo(force ? 1 : 0));
            Assert.That(gate.ClosePlays, Is.EqualTo(1));
        }

        [TestCase("initialize")] [TestCase("open")] [TestCase("refresh")]
        [TestCase("update")] [TestCase("close")] [TestCase("destroy")]
        [TestCase("register")]
        public void BusinessExceptionsAreLoggedAndLifecycleContinues(string hook)
        {
            var window = _fixture.Open();
            Action fail = () => throw new InvalidOperationException("expected UI hook failure");
            if (hook == "initialize") UIProbeWidget.Initializing = _ => fail();
            LogAssert.Expect(LogType.Exception, new Regex("expected UI hook failure"));
            var child = window.Create(false);
            switch (hook)
            {
                case "open": child.Opening = fail; break;
                case "refresh": child.Refreshing = fail; break;
                case "update": child.Updating = fail; break;
                case "close": child.Closing = fail; break;
                case "destroy": child.Destroying = fail; break;
                case "register": child.Registering = fail; break;
            }
            child.Open();
            if (hook == "refresh") child.Open();
            if (hook == "update") _fixture.Tick();
            child.Destroy().GetAwaiter().GetResult();
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(window.IsOpen, Is.True);
            UIProbeWidget.Initializing = null;
            Assert.That(window.Create().IsOpen, Is.True);
        }

        [Test]
        public void HolderNotificationsKeepOrderAndIsolateSubscribers()
        {
            var order = new System.Collections.Generic.List<string>();
            _fixture.Loader.Bound = holder =>
            {
                holder.OnWindowInitEvent += () => order.Add("init");
                holder.OnWindowBeforeShowEvent += () => throw new InvalidOperationException("holder callback");
                holder.OnWindowBeforeShowEvent += () => order.Add("before");
                holder.OnWindowAfterShowEvent += () => order.Add("after");
                holder.OnWindowBeforeClosedEvent += () => order.Add("closing");
                holder.OnWindowAfterClosedEvent += () => order.Add("closed");
                holder.OnWindowDestroyEvent += () => order.Add("destroy");
            };
            LogAssert.Expect(LogType.Exception, new Regex("holder callback"));
            var window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>(true).GetAwaiter().GetResult();
            Assert.That(order, Is.EqualTo(new[] { "init", "before", "after", "closing", "closed", "destroy" }));
            Assert.That(window.Destroys, Is.EqualTo(1));
        }

        [Test]
        public void WindowWithoutOwnUpdateStillUpdatesWidgets()
        {
            var tabWindow = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var widget = (UIProbeWidget)tabWindow.SwitchTab(0).GetAwaiter().GetResult();
            _fixture.Tick();
            Assert.That(widget.Updates, Is.EqualTo(1));
        }

        [Test]
        public void LastWaiterCancelledInsideOnOpenDoesNotLeaveOrphanWindow()
        {
            using var cancellation = new CancellationTokenSource();
            UIProbeWindow instance = null;
            UIProbeWindow.Initializing = window =>
            {
                instance = window;
                window.Opening = cancellation.Cancel;
            };
            Assert.That(_fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token).GetAwaiter().GetResult(), Is.Null);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
            Assert.That(instance.Destroys, Is.EqualTo(1));
        }

        [Test]
        public void CancellationDuringOnOpenKeepsOtherSharedWaiterAlive()
        {
            using var cancellation = new CancellationTokenSource();
            UIProbeWindow.Initializing = window => window.Opening = cancellation.Cancel;
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token);
            var second = _fixture.Service.ShowUI<UIProbeWindow>();
            _fixture.Loader.Deliver();
            Assert.That(first.GetAwaiter().GetResult(), Is.Null);
            Assert.That(second.GetAwaiter().GetResult().IsOpen, Is.True);
        }

        [Test]
        public void ShutdownCancelsAllLoadsAndDestroysOpenTrees()
        {
            var window = _fixture.Open();
            var child = window.Create();
            _fixture.Loader.Deferred = true;
            var pendingChild = child.CreateAsync();
            var pendingWindow = _fixture.Service.ShowUI<UISecondWindow>();
            AppServices.Shutdown();
            Assert.That(pendingWindow.GetAwaiter().GetResult(), Is.Null);
            while (_fixture.Loader.Pending != null) _fixture.Loader.Deliver();
            Assert.That(pendingChild.GetAwaiter().GetResult(), Is.Null);
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
        }
    }
}
