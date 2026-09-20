using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UILifetimeTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [Test]
        public void SelfCloseDuringOnOpenPairsHooksAndDoesNotSubscribe()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            bool openInsideHook = true;
            child.Opening = () => { openInsideHook = child.IsOpen; child.Close(); };
            child.Open();
            Assert.That(openInsideHook, Is.False);
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.IsOpen, Is.False);
            Assert.That(child.IsVisible, Is.False);
            Assert.That(child.Registrations, Is.Zero);
        }

        [Test]
        public void SelfDestroyDuringRegistrationReleasesPartiallyRegisteredProxy()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            child.Registering = () => child.Destroy().GetAwaiter().GetResult();
            child.Open();
            UIFixture.Publish();
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(child.Events, Is.Zero);
            Assert.That(child.IsOpen, Is.False);
        }

        [Test]
        public void ParentOnDestroySeesSuspendedButIntactChildrenAndRunsFirst()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            UIProbeWidget grandchild = child.Create();
            var order = new List<string>();
            bool childIntact = false;
            window.Destroying = () =>
            {
                childIntact = child.Holder != null && child.Destroys == 0;
                order.Add("window");
                UIFixture.Publish();
                _fixture.Tick();
            };
            child.Destroying = () => order.Add("child");
            grandchild.Destroying = () => order.Add("grandchild");
            window.DestroyNow();
            Assert.That(childIntact, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "window", "child", "grandchild" }));
            Assert.That(child.Events + child.Updates + grandchild.Events + grandchild.Updates, Is.Zero);
        }

        [Test]
        public void RegistrationMayDestroySiblingWithoutSkippingRemainingChildren()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(), b = window.Create(), c = window.Create();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            a.Registering = () => b.Destroy().GetAwaiter().GetResult();
            _fixture.Open();
            UIFixture.Publish();
            Assert.That(b.Destroys, Is.EqualTo(1));
            Assert.That(b.Events, Is.Zero);
            Assert.That(a.Events, Is.EqualTo(1));
            Assert.That(c.Events, Is.EqualTo(1));
        }

        [Test]
        public void WidgetCloseBeforeFirstOpenDoesNothing()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            child.Close();
            Assert.That(child.State, Is.EqualTo(UIState.Initialized));
            Assert.That(child.Closes, Is.Zero);
            child.Open();
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [Test]
        public void SharedLoadUsesLatestArgumentsAndIndependentCancellation()
        {
            _fixture.Loader.Deferred = true;
            using var firstCancel = new CancellationTokenSource();
            var first = _fixture.Service.ShowUI<UIProbeWindow>(firstCancel.Token, "first");
            var second = _fixture.Service.ShowUI<UIProbeWindow>("second");
            firstCancel.Cancel();
            Assert.That(first.GetAwaiter().GetResult(), Is.Null);
            Assert.That(_fixture.Loader.PendingToken.IsCancellationRequested, Is.False);
            _fixture.Loader.Deliver();
            UIProbeWindow window = second.GetAwaiter().GetResult();
            Assert.That(window.Argument, Is.EqualTo("second"));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
        }

        [Test]
        public void LastCancellationDestroysLateResource()
        {
            _fixture.Loader.Deferred = true;
            using var cancellation = new CancellationTokenSource();
            var task = _fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token);
            cancellation.Cancel();
            Assert.That(task.GetAwaiter().GetResult(), Is.Null);
            Assert.That(_fixture.Loader.PendingToken.IsCancellationRequested, Is.True);
            GameObject late = _fixture.Loader.Deliver();
            Assert.That(late == null, Is.True);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [Test]
        public void ParentDestroyCancelsInFlightChildLoad()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            var task = window.CreateAsync();
            window.DestroyNow();
            Assert.That(_fixture.Loader.PendingToken.IsCancellationRequested, Is.True);
            GameObject late = _fixture.Loader.Deliver();
            Assert.That(late == null, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.Null);
        }

        [Test]
        public void SyncShowDuringSharedLoadIsRejectedWithoutReplacingIt()
        {
            _fixture.Loader.Deferred = true;
            var task = _fixture.Service.ShowUI<UIProbeWindow>();
            LogAssert.Expect(LogType.Error, new Regex("asynchronous load"));
            Assert.That(_fixture.Open(), Is.Null);
            _fixture.Loader.Deliver();
            Assert.That(task.GetAwaiter().GetResult().IsOpen, Is.True);
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
        }

        [Test]
        public void ResourceFailureReachesEveryWaiterAndRetryCanSucceed()
        {
            _fixture.Loader.Deferred = true;
            _fixture.Loader.Fail = true;
            var a = _fixture.Service.ShowUI<UIProbeWindow>();
            var b = _fixture.Service.ShowUI<UIProbeWindow>();
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            _fixture.Loader.Deliver();
            Assert.That(a.GetAwaiter().GetResult(), Is.Null);
            Assert.That(b.GetAwaiter().GetResult(), Is.Null);
            _fixture.Loader.Fail = false;
            Assert.That(_fixture.Open().IsOpen, Is.True);
        }

        [Test]
        public void SyncResourceFailureDoesNotLeaveAnOpenWindow()
        {
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            Assert.That(_fixture.Open(), Is.Null);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [Test]
        public void LifecycleFailureIsLoggedAndOtherWidgetsRemainUsable()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(false), b = window.Create();
            a.Opening = () => throw new InvalidOperationException("ui-probe-open");
            LogAssert.Expect(LogType.Exception, new Regex("ui-probe-open"));
            a.Open();
            Assert.That(a.IsOpen, Is.True);
            _fixture.Tick();
            Assert.That(b.Updates, Is.EqualTo(1));
        }

        [Test]
        public void WindowInitializationCanAbortShowWithoutReportingLoadFailure()
        {
            UIProbeWindow.Initializing = window => window.DestroyNow();
            Assert.That(_fixture.Open(), Is.Null);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [Test]
        public void WindowInitializationCanCloseToAbortShow()
        {
            UIProbeWindow.Initializing = window => _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(_fixture.Open(), Is.Null);
        }

        [Test]
        public void DestroyingBorrowedWidgetHidesItsRetainedHolder()
        {
            UIProbeWindow window = _fixture.Open();
            var obj = new GameObject("borrowed", typeof(RectTransform), typeof(UIWidgetHolder));
            obj.transform.SetParent(window.Holder.transform, false);
            UIProbeWidget child = window.Borrow(obj.GetComponent<UIWidgetHolder>(), false);
            child.DestroyNow();
            Assert.That(obj != null, Is.True);
            Assert.That(obj.GetComponent<CanvasGroup>().alpha, Is.Zero);
        }

        [Test]
        public void DestroyedParentRejectsWidgetCreation()
        {
            UIProbeWindow window = _fixture.Open();
            window.DestroyNow();
            LogAssert.Expect(LogType.Error, new Regex("parent requested destruction"));
            Assert.That(window.Create(), Is.Null);
        }

        [Test]
        public void RepeatedOpenWithoutArgumentsPreservesDataAndRefreshes()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            child.Open("data");
            child.Open();
            Assert.That(child.Argument, Is.EqualTo("data"));
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Refreshes, Is.EqualTo(1));
        }
    }
}
