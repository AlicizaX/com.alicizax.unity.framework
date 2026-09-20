using System.Collections;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIAsyncFrameTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [UnityTest]
        public IEnumerator IndependentWidgetLoadsCanFinishOutOfOrderAndCancelSeparately()
        {
            var window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            using var cancellation = new CancellationTokenSource();
            var cancelled = window.CreateAsync(token: cancellation.Token);
            var first = window.CreateAsync();
            var second = window.CreateAsync(false);
            cancellation.Cancel();
            Assert.That(cancelled.Status.IsCompleted(), Is.False);
            yield return null;
            _fixture.Loader.Deliver(2);
            var b = second.GetAwaiter().GetResult();
            Assert.That(b.IsOpen, Is.False);
            _fixture.Loader.Deliver(1);
            var a = first.GetAwaiter().GetResult();
            Assert.That(a.IsOpen, Is.True);
            var late = _fixture.Loader.Deliver();
            Assert.That(cancelled.GetAwaiter().GetResult(), Is.Null);
            yield return null;
            Assert.That(late == null, Is.True);
            Assert.That(a.IsVisible, Is.True);
            Assert.That(b.Initializes, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ParentCacheWhileMultipleWidgetsLoadDefersOnlyTheirActivity()
        {
            var window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            var first = window.CreateAsync();
            var second = window.CreateAsync();
            yield return UIFixture.Wait(_fixture.Service.CloseUI<UIProbeWindow>());
            _fixture.Loader.Deliver(1);
            _fixture.Loader.Deliver();
            var a = first.GetAwaiter().GetResult();
            var b = second.GetAwaiter().GetResult();
            Assert.That(a.Opens + b.Opens, Is.EqualTo(2));
            Assert.That(a.Registrations + b.Registrations, Is.Zero);
            yield return null;
            _fixture.Open();
            _fixture.Tick();
            Assert.That(a.Updates + b.Updates, Is.EqualTo(2));
            Assert.That(a.Opens + b.Opens, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator DestroyParentCancelsEveryPendingDescendantLoad()
        {
            var window = _fixture.Open();
            var child = window.Create();
            _fixture.Loader.Deferred = true;
            var first = window.CreateAsync();
            var second = child.CreateAsync();
            window.DestroyNow();
            var lateA = _fixture.Loader.Deliver();
            var lateB = _fixture.Loader.Deliver();
            Assert.That(first.GetAwaiter().GetResult(), Is.Null);
            Assert.That(second.GetAwaiter().GetResult(), Is.Null);
            yield return null;
            Assert.That(lateA == null && lateB == null, Is.True);
            Assert.That(child.Destroys, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BorrowedHolderDefaultAndRetainedOwnershipSurviveFrameBoundary()
        {
            var window = _fixture.Open();
            var owned = new GameObject("owned", typeof(RectTransform), typeof(UIWidgetHolder));
            var retained = new GameObject("retained", typeof(RectTransform), typeof(UIWidgetHolder));
            owned.transform.SetParent(window.Holder.transform, false);
            retained.transform.SetParent(window.Holder.transform, false);
            var a = window.Borrow(owned.GetComponent<UIWidgetHolder>());
            var b = window.Borrow(retained.GetComponent<UIWidgetHolder>(), false);
            a.Destroy().GetAwaiter().GetResult();
            b.Destroy().GetAwaiter().GetResult();
            yield return null;
            Assert.That(owned == null, Is.True);
            Assert.That(retained == null, Is.False);
            Assert.That(retained.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(window.Borrow(retained.GetComponent<UIWidgetHolder>(), false).IsOpen, Is.True);
        }

        [UnityTest]
        public IEnumerator TimedCacheExpiryDestroysTreeAndNextShowBuildsNewInstance()
        {
            var window = _fixture.Service.ShowUISync<UITimedCacheWindow>();
            var child = window.Create();
            var holder = child.Holder;
            yield return UIFixture.Wait(_fixture.Service.CloseUI<UITimedCacheWindow>());
            float deadline = Time.realtimeSinceStartup + 3f;
            while (child.Destroys == 0 && Time.realtimeSinceStartup < deadline)
            {
                ((IServiceTickable)_fixture.Timer).Tick(Time.deltaTime);
                yield return null;
            }
            yield return null;
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(holder == null, Is.True);
            Assert.That(_fixture.Service.ShowUISync<UITimedCacheWindow>(), Is.Not.SameAs(window));
        }

        [UnityTest]
        public IEnumerator ReopenRemovesOldCacheExpiry()
        {
            var window = _fixture.Service.ShowUISync<UITimedCacheWindow>();
            var child = window.Create();
            yield return UIFixture.Wait(_fixture.Service.CloseUI<UITimedCacheWindow>());
            Assert.That(_fixture.Service.ShowUISync<UITimedCacheWindow>(), Is.SameAs(window));
            float deadline = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                ((IServiceTickable)_fixture.Timer).Tick(Time.deltaTime);
                yield return null;
            }
            Assert.That(window.IsOpen, Is.True);
            Assert.That(child.Destroys, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyDuringRealAnimationReleasesCapturedWaitersAndObjects()
        {
            var window = _fixture.Open();
            var child = window.Create(false);
            var holder = child.Holder;
            holder.SetTransition(holder.gameObject.AddComponent<UIPresetTransition>());
            child.Open();
            var opening = child.AwaitTransition();
            yield return null;
            window.DestroyNow();
            yield return UIFixture.Wait(opening);
            yield return null;
            Assert.That(holder == null, Is.True);
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator WindowEventsResumeDuringOpeningButUpdatesWaitForAnimation()
        {
            var window = _fixture.Open();
            var child = window.Create();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            _fixture.Open();
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                UIFixture.Publish();
                _fixture.Tick();
            }
            Assert.That(child.Events, Is.EqualTo(3));
            Assert.That(window.Updates + child.Updates, Is.Zero);
            gate.Finish();
            yield return null;
            _fixture.Tick();
            Assert.That(window.Updates + child.Updates, Is.EqualTo(2));
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TabDestroyedDuringLoadSettlesRequestAndDestroysLateObject()
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            var tab = window.SwitchTab(0);
            yield return null;
            yield return UIFixture.Wait(_fixture.Service.CloseUI<UIProbeTabWindow>(true));
            Assert.That(tab.GetAwaiter().GetResult(), Is.Null);
            var late = _fixture.Loader.Deliver();
            yield return null;
            Assert.That(late == null, Is.True);
        }

        [UnityTest]
        public IEnumerator TabLoadsOutOfOrderOnlyOpenLatestSelection()
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            var first = window.SwitchTab(0, "first");
            var second = window.SwitchTab(1);
            Assert.That(first.GetAwaiter().GetResult(), Is.Null);
            yield return null;
            _fixture.Loader.Deliver(1);
            var selected = second.GetAwaiter().GetResult();
            _fixture.Loader.Deliver();
            yield return null;
            Assert.That(selected.IsOpen, Is.True);
            int loads = _fixture.Loader.Loads;
            var a = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            Assert.That(a.Opens, Is.EqualTo(1));
            Assert.That(selected.IsOpen, Is.False);
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(loads));
        }

        [UnityTest]
        public IEnumerator CachedWindowCanFinishTabSelectionWithoutWakingItsEvents()
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            var tab = window.SwitchTab(0);
            _fixture.Service.CloseUI<UIProbeTabWindow>().GetAwaiter().GetResult();
            yield return null;
            _fixture.Loader.Deliver();
            var child = (UIProbeWidget)tab.GetAwaiter().GetResult();
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.IsVisible, Is.False);
            Assert.That(child.Registrations, Is.Zero);
            _fixture.Service.ShowUISync<UIProbeTabWindow>();
            Assert.That(child.Registrations, Is.EqualTo(1));
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator NavigationResultSeparatesLogicalOpenFromTransition()
        {
            var gate = new UIGateTransition();
            _fixture.Loader.Bound = holder => holder.SetTransition(gate);
            _fixture.Loader.Deferred = true;
            var task = _fixture.Service.NavigateTo<UIProbeWindow>();
            yield return null;
            _fixture.Loader.Deliver();
            Assert.That(task.Status.IsCompleted(), Is.True);
            Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            var window = _fixture.Service.GetUI<UIProbeWindow>();
            var transition = window.AwaitTransition();
            Assert.That(window.IsOpen, Is.True);
            Assert.That(transition.Status.IsCompleted(), Is.False);
            yield return null;
            gate.Finish();
            yield return UIFixture.Wait(transition);
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
        }

        [UnityTest]
        public IEnumerator RepeatedForceCloseLeavesNoHolderObjectsOrEvents()
        {
            var holders = new List<UIHolderObjectBase>();
            var children = new List<UIProbeWidget>();
            for (int i = 0; i < 12; i++)
            {
                var window = _fixture.Open();
                holders.Add(window.Holder);
                for (int n = 0; n < 4; n++)
                {
                    var child = window.Create();
                    children.Add(child);
                    holders.Add(child.Holder);
                    var leaf = child.Create();
                    children.Add(leaf);
                    holders.Add(leaf.Holder);
                }
                yield return UIFixture.Wait(_fixture.Service.CloseUI<UIProbeWindow>(true));
                yield return null;
            }
            UIFixture.Publish();
            _fixture.Tick();
            foreach (var holder in holders) Assert.That(holder == null, Is.True);
            foreach (var child in children)
            {
                Assert.That(child.Destroys, Is.EqualTo(1));
                Assert.That(child.Events + child.Updates, Is.Zero);
            }
        }
    }
}
