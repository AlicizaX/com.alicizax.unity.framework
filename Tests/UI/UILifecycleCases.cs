#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.UI.Tests
{
    public abstract class UILifecycleCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private IUIDebugService Debug => _fixture.Service;

        [Test]
        public void RepeatedShowKeepsOneInstanceAndRefreshes()
        {
            UIProbeWindow window = _fixture.Open("first");
            UIProbeWindow again = _fixture.Service.ShowUISync<UIProbeWindow>("second");
            UIProbeWindow keep = _fixture.Service.ShowUISync<UIProbeWindow>();
            Assert.That(again, Is.SameAs(window));
            Assert.That(keep, Is.SameAs(window));
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Refreshes, Is.EqualTo(2));
            Assert.That(window.Argument, Is.EqualTo("second"));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RepeatedAsyncShowsShareOneLoad()
        {
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWindow> first = _fixture.Service.ShowUI<UIProbeWindow>("first");
            UniTask<UIProbeWindow> second = _fixture.Service.ShowUI<UIProbeWindow>("second");
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
            _fixture.Loader.Deliver();
            UIProbeWindow a = null, b = null;
            yield return _fixture.Wait(first, result => a = result);
            yield return _fixture.Wait(second, result => b = result);
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.SameAs(a));
            Assert.That(a.Argument, Is.EqualTo("second"));
            Assert.That(a.Opens, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CloseDuringLoadDestroysWindow()
        {
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWindow> show = _fixture.Service.ShowUI<UIProbeWindow>();
            UniTask close = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            Assert.That(close.Status.IsCompleted(), Is.True);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
            UIProbeWindow result = null;
            yield return _fixture.Wait(show, value => result = value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void SyncShowDuringAsyncLoadWarnsAndReturnsNull()
        {
            _fixture.Loader.Deferred = true;
            _fixture.Service.ShowUI<UIProbeWindow>().Forget();
            LogAssert.Expect(LogType.Warning, new Regex("loading asynchronously"));
            Assert.That(_fixture.Service.ShowUISync<UIProbeWindow>(), Is.Null);
            Assert.That(_fixture.Loader.PendingCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CancelledShowOnlyCancelsItsCaller()
        {
            _fixture.Loader.Deferred = true;
            using var cancellation = new CancellationTokenSource();
            UniTask<UIProbeWindow> cancelled = _fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token);
            UniTask<UIProbeWindow> kept = _fixture.Service.ShowUI<UIProbeWindow>();
            cancellation.Cancel();
            UIProbeWindow a = null, b = null;
            yield return _fixture.Wait(cancelled, result => a = result);
            Assert.That(a, Is.Null);
            _fixture.Loader.Deliver();
            yield return _fixture.Wait(kept, result => b = result);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void ClosingCachesAndReopeningReusesInstance()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(window));
            Assert.That(_fixture.Open(), Is.SameAs(window));
            Assert.That(Debug.CacheWindowCount, Is.Zero);
            Assert.That(window.Initializes, Is.EqualTo(1));
            Assert.That(window.Opens, Is.EqualTo(2));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(1));
        }

        [Test]
        public void ForceCloseAndZeroCacheDestroy()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(window.Destroys, Is.EqualTo(1));
            UIZeroCacheWindow zero = _fixture.Service.ShowUISync<UIZeroCacheWindow>();
            _fixture.Service.CloseUI<UIZeroCacheWindow>();
            Assert.That(zero.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(zero.Destroys, Is.EqualTo(1));
            Assert.That(Debug.CacheWindowCount, Is.Zero);
        }

        [Test]
        public void ForceCloseDuringCloseTransitionDestroysAtTheEnd()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Service.CloseUI<UIProbeWindow>();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(Debug.CacheWindowCount, Is.Zero);
        }

        [Test]
        public void ReopenCancelsPendingForceClose()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            _fixture.Open();
            gate.Finish();
            _fixture.Service.CloseUI<UIProbeWindow>();
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
        }

        [Test]
        public void HookExceptionIsLoggedAndTheViewKeepsWorking()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>();
            window.Opening = () => throw new InvalidOperationException("open hook");
            LogAssert.Expect(LogType.Exception, new Regex("open hook"));
            Assert.That(_fixture.Open(), Is.SameAs(window));
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            window.Opening = null;
            window.Refreshing = () => throw new InvalidOperationException("refresh hook");
            LogAssert.Expect(LogType.Exception, new Regex("refresh hook"));
            _fixture.Open();
            window.Refreshing = null;
            window.Closing = () => throw new InvalidOperationException("close hook");
            LogAssert.Expect(LogType.Exception, new Regex("close hook"));
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
            window.Closing = null;
        }

        [Test]
        public void HolderEventExceptionDoesNotStallTheView()
        {
            UIProbeWindow window = _fixture.Open();
            Action fail = () => throw new InvalidOperationException("holder event");
            window.Holder.OnShowEvent += fail;
            window.Holder.OnCloseEvent += fail;
            LogAssert.Expect(LogType.Exception, new Regex("holder event"));
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(window.Closes, Is.EqualTo(1));
            LogAssert.Expect(LogType.Exception, new Regex("holder event"));
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.Opens, Is.EqualTo(2));
            window.Holder.OnShowEvent -= fail;
            window.Holder.OnCloseEvent -= fail;
        }

        [Test]
        public void ThrowingTransitionStillReachesTheEndState()
        {
            UIProbeWindow window = _fixture.Open();
            window.Holder.SetTransition(new UIThrowingTransition());
            LogAssert.Expect(LogType.Exception, new Regex("transition failure"));
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            LogAssert.Expect(LogType.Exception, new Regex("transition failure"));
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void ThrowingDestroyHookStillReleasesTheTree()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            window.Destroying = () => throw new InvalidOperationException("destroy hook");
            LogAssert.Expect(LogType.Exception, new Regex("destroy hook"));
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(widget.State, Is.EqualTo(UIState.Destroyed));
        }

        [Test]
        public void EventProxyAfterDestroyThrows()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.Throws<InvalidOperationException>(() => _ = window.Proxy);
        }

        [Test]
        public void WidgetWithCanvasTogglesCanvasInsteadOfAlpha()
        {
            UIProbeWindow window = _fixture.Open();
            var node = new GameObject("CanvasWidget", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            node.transform.SetParent(window.Holder.transform, false);
            UIProbeWidget widget = window.Borrow(node.AddComponent<UIWidgetHolder>(), open: false);
            Canvas canvas = node.GetComponent<Canvas>();
            GraphicRaycaster raycaster = node.GetComponent<GraphicRaycaster>();
            Assert.That(node.GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(canvas.enabled, Is.False);
            widget.Open();
            Assert.That(canvas.enabled, Is.True);
            Assert.That(raycaster.enabled, Is.True);
            widget.Close();
            Assert.That(canvas.enabled, Is.False);
            Assert.That(raycaster.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator ReopenDuringCloseTransitionReverses()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            UniTask close = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            yield return _fixture.Wait(close);
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(Debug.CacheWindowCount, Is.Zero);
            Assert.That(window.Opens, Is.EqualTo(2));
            Assert.That(window.Closes, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CloseDuringOpenTransitionReverses()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            UniTask close = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            Assert.That(gate.Closes, Is.EqualTo(1));
            gate.Finish();
            yield return _fixture.Wait(close);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
        }

        [Test]
        public void CloseInsideOnOpenStopsTheOpen()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>();
            UIGateTransition gate = _fixture.Gate(window);
            window.Opening = () => window.CloseThis(snap: true);
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(gate.Opens, Is.Zero);
            Assert.That(window.Closes, Is.EqualTo(2));
        }

        [Test]
        public void OpenInsideOnCloseKeepsWindowOpen()
        {
            UIProbeWindow window = _fixture.Open();
            window.Closing = () =>
            {
                window.Closing = null;
                _fixture.Open();
            };
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.Opens, Is.EqualTo(2));
            Assert.That(Debug.CacheWindowCount, Is.Zero);
        }

        [Test]
        public void EventsAndUpdateFollowOpenState()
        {
            UIProbeWindow window = _fixture.Open();
            UIFixture.PublishWindow();
            _fixture.Tick();
            Assert.That(window.Events, Is.EqualTo(1));
            Assert.That(window.Updates, Is.EqualTo(1));
            _fixture.Service.CloseUI<UIProbeWindow>();
            UIFixture.PublishWindow();
            _fixture.Tick();
            Assert.That(window.Events, Is.EqualTo(1));
            Assert.That(window.Updates, Is.EqualTo(1));
            _fixture.Open();
            UIFixture.PublishWindow();
            _fixture.Tick();
            Assert.That(window.Events, Is.EqualTo(2));
            Assert.That(window.Updates, Is.EqualTo(2));
        }

        [Test]
        public void WidgetFollowsWindowEnabledState()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            Assert.That(widget.Events, Is.EqualTo(1));
            Assert.That(widget.Updates, Is.EqualTo(1));
            _fixture.Service.CloseUI<UIProbeWindow>();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            Assert.That(widget.Events, Is.EqualTo(1));
            Assert.That(widget.Updates, Is.EqualTo(1));
            _fixture.Open();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(widget.Events, Is.EqualTo(2));
            Assert.That(widget.Updates, Is.EqualTo(2));
        }

        [Test]
        public void WidgetOpenedUnderClosedWindowStartsDisabled()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>();
            UIProbeWidget widget = window.Create();
            _fixture.Tick();
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            Assert.That(widget.Updates, Is.Zero);
            _fixture.Open();
            _fixture.Tick();
            Assert.That(widget.Updates, Is.EqualTo(1));
        }

        [Test]
        public void DestroyingWindowDestroysWidgetTree()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            UIProbeWidget nested = widget.Create();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(widget.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(nested.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(widget.Closes, Is.EqualTo(1));
            Assert.That(widget.Destroys, Is.EqualTo(1));
            Assert.That(nested.Destroys, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator WidgetDestroyClosesFirst()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            UIGateTransition gate = _fixture.Gate(widget);
            UniTask destroy = widget.Destroy().AwaitTransition();
            Assert.That(widget.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            yield return _fixture.Wait(destroy);
            Assert.That(widget.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(widget.Destroys, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator WidgetReopenedWhileClosingIsNotDestroyed()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            UIGateTransition gate = _fixture.Gate(widget);
            UniTask destroy = widget.Destroy().AwaitTransition();
            widget.Open();
            gate.Finish();
            yield return _fixture.Wait(destroy);
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            Assert.That(widget.Destroys, Is.Zero);
        }

        [UnityTest]
        public IEnumerator BorrowedHolderIsDestroyedWithWidget()
        {
            UIProbeWindow window = _fixture.Open();
            var node = new GameObject("Borrowed", typeof(RectTransform));
            node.transform.SetParent(window.Holder.transform, false);
            UIWidgetHolder holder = node.AddComponent<UIWidgetHolder>();
            UIProbeWidget widget = window.Borrow(holder);
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            yield return _fixture.Wait(widget.Destroy().AwaitTransition());
            yield return null;
            Assert.That(widget.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(holder == null, Is.True);
        }

        [Test]
        public void WidgetUnderInactiveNodeIsRejected()
        {
            UIProbeWindow window = _fixture.Open();
            var node = new GameObject("Inactive", typeof(RectTransform));
            node.transform.SetParent(window.Holder.transform, false);
            node.SetActive(false);
            LogAssert.Expect(LogType.Error, new Regex("must be created under an active node"));
            Assert.That(window.CreateUnder(node.transform), Is.Null);
        }

        [UnityTest]
        public IEnumerator AsyncWidgetLoadsAndCancellationDestroysIt()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWidget> loading = window.CreateAsync();
            _fixture.Loader.Deliver();
            UIProbeWidget widget = null;
            yield return _fixture.Wait(loading, result => widget = result);
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));

            using var cancellation = new CancellationTokenSource();
            UniTask<UIProbeWidget> cancelled = window.CreateAsync(token: cancellation.Token);
            cancellation.Cancel();
            UIProbeWidget missing = widget;
            yield return _fixture.Wait(cancelled, result => missing = result);
            Assert.That(missing, Is.Null);
        }

        [UnityTest]
        public IEnumerator TabsSwitchAndSupersededSwitchReturnsNull()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            UniTask<UIWidget> first = window.SwitchTab(0);
            UniTask<UIWidget> second = window.SwitchTab(1);
            _fixture.Loader.Deliver();
            _fixture.Loader.Deliver();
            UIWidget a = null, b = null;
            yield return _fixture.Wait(first, result => a = result);
            yield return _fixture.Wait(second, result => b = result);
            Assert.That(a, Is.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(window.ActiveTab, Is.SameAs(b));
            Assert.That(b.State, Is.EqualTo(UIState.Opened));

            UniTask<UIWidget> back = window.SwitchTab(0);
            Assert.That(back.Status.IsCompleted(), Is.True);
            UIWidget tab = back.GetAwaiter().GetResult();
            Assert.That(tab.State, Is.EqualTo(UIState.Opened));
            Assert.That(b.State, Is.EqualTo(UIState.Closed));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(3));
        }
    }
}
#endif
