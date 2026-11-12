#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text.RegularExpressions;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public abstract class UIMatrixCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private IUIDebugService Debug => _fixture.Service;

        private UIProbeWindow OpenCached()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>();
            return window;
        }

        [Test]
        public void ForceCloseOfCachedWindowDestroysIt()
        {
            UIProbeWindow window = OpenCached();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(Debug.CacheWindowCount, Is.Zero);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [Test]
        public void CloseOfCachedWindowIsIgnored()
        {
            UIProbeWindow window = OpenCached();
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
        }

        [Test]
        public void SnapCloseSkipsTheTransition()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Service.CloseUI<UIProbeWindow>(snap: true);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(gate.Closes, Is.EqualTo(1));
            Assert.That(gate.LastSnap, Is.True);
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
        }

        [Test]
        public void ShowDuringOpeningRefreshesWithoutRestartingTheTransition()
        {
            UIProbeWindow window = OpenCached();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            _fixture.Open("again");
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            Assert.That(window.Refreshes, Is.EqualTo(1));
            Assert.That(window.Argument, Is.EqualTo("again"));
            Assert.That(gate.Opens, Is.EqualTo(1));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void RepeatedCloseDuringClosingIsIgnored()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Service.CloseUI<UIProbeWindow>();
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(gate.Closes, Is.EqualTo(1));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(Debug.CacheWindowCount, Is.EqualTo(1));
        }

        [Test]
        public void ForceCloseDuringOpeningDestroysAfterTheCloseTransition()
        {
            UIProbeWindow window = OpenCached();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
        }

        [Test]
        public void CloseHandleOfDestroyedOrMissingWindowCompletesImmediately()
        {
            _fixture.Open();
            UniTask destroyed = _fixture.Service.CloseUI<UIProbeWindow>(force: true).AwaitTransition();
            Assert.That(destroyed.Status.IsCompleted(), Is.True);
            LogAssert.Expect(LogType.Warning, new Regex("Close ignored"));
            UniTask missing = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            Assert.That(missing.Status.IsCompleted(), Is.True);
        }

        [UnityTest]
        public IEnumerator EveryAwaiterOfOneCloseCompletes()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            UniTask first = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            UniTask second = window.AwaitTransition();
            gate.Finish();
            yield return _fixture.Wait(first);
            yield return _fixture.Wait(second);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
        }

        [UnityTest]
        public IEnumerator LateNodeOfACancelledLoadIsDestroyedAndReopenLoadsAgain()
        {
            _fixture.Loader.Deferred = true;
            _fixture.Loader.IgnoreCancellation = true;
            UniTask<UIProbeWindow> show = _fixture.Service.ShowUI<UIProbeWindow>();
            _fixture.Service.CloseUI<UIProbeWindow>();
            UIHolderObjectBase late = _fixture.Loader.Deliver();
            UIProbeWindow result = null;
            yield return _fixture.Wait(show, value => result = value);
            yield return null;
            Assert.That(result, Is.Null);
            Assert.That(late == null, Is.True);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
            UniTask<UIProbeWindow> again = _fixture.Service.ShowUI<UIProbeWindow>();
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(2));
            _fixture.Loader.Deliver();
            yield return _fixture.Wait(again, value => result = value);
            Assert.That(result.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void ForceCloseInsideOnOpenMakesShowReturnNull()
        {
            UIProbeWindow window = OpenCached();
            window.Opening = () => window.CloseThis(force: true);
            Assert.That(_fixture.Open(), Is.Null);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [Test]
        public void ForceCloseInsideOnInitializeMakesShowReturnNull()
        {
            UIProbeWindow created = null;
            UIProbeWindow.Initializing = window =>
            {
                created = window;
                window.CloseThis(force: true);
            };
            Assert.That(_fixture.Open(), Is.Null);
            Assert.That(created.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(created.Opens, Is.Zero);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [Test]
        public void ReopenInsideOnDestroyCreatesAFreshInstance()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWindow fresh = null;
            window.Destroying = () =>
            {
                window.Destroying = null;
                fresh = _fixture.Open();
            };
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(fresh, Is.Not.Null.And.Not.SameAs(window));
            Assert.That(fresh.State, Is.EqualTo(UIState.Opened));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(fresh));
        }

        [Test]
        public void OpenInsideOnInitializeOpensOnceAndStaysVisible()
        {
            UIProbeWindow.Initializing = _ =>
            {
                UIProbeWindow.Initializing = null;
                _fixture.Open();
            };
            UIProbeWindow window = _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Refreshes, Is.Zero);
            Assert.That(window.Holder.GetComponent<Canvas>().enabled, Is.True);
        }

        [Test]
        public void CloseAndReopenInsideOnOpenPlaysOneOpenTransition()
        {
            UIProbeWindow window = OpenCached();
            UIGateTransition gate = _fixture.Gate(window);
            window.Opening = () =>
            {
                window.Opening = null;
                window.CloseThis();
                _fixture.Open();
            };
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            Assert.That(gate.Opens, Is.EqualTo(1));
            Assert.That(gate.Closes, Is.EqualTo(1));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.Opens, Is.EqualTo(3));
        }

        [Test]
        public void ShowEventSubscriberClosingTheWindowSkipsOnOpen()
        {
            UIProbeWindow window = OpenCached();
            Action close = () => _fixture.Service.CloseUI<UIProbeWindow>();
            window.Holder.OnShowEvent += close;
            _fixture.Open();
            window.Holder.OnShowEvent -= close;
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(window.Opens, Is.EqualTo(1));
            Assert.That(window.Closes, Is.EqualTo(2));
        }

        [Test]
        public void CloseEventSubscriberReopeningTheWindowSkipsOnClose()
        {
            UIProbeWindow window = _fixture.Open();
            Action reopen = null;
            reopen = () =>
            {
                window.Holder.OnCloseEvent -= reopen;
                _fixture.Open();
            };
            window.Holder.OnCloseEvent += reopen;
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.Closes, Is.Zero);
            Assert.That(window.Opens, Is.EqualTo(2));
        }

        [Test]
        public void UpdateClosingAnotherViewSkipsItInTheSameTick()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            window.Updating = () => widget.Close();
            _fixture.Tick();
            Assert.That(widget.Updates, Is.Zero);
            window.Updating = null;
            widget.Open();
            _fixture.Tick();
            Assert.That(widget.Updates, Is.EqualTo(1));
        }

        [Test]
        public void ViewEnabledDuringUpdateStartsOnTheNextTick()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = null;
            window.Updating = () =>
            {
                window.Updating = null;
                widget = window.Create();
            };
            _fixture.Tick();
            Assert.That(widget.Updates, Is.Zero);
            _fixture.Tick();
            Assert.That(widget.Updates, Is.EqualTo(1));
        }

        [Test]
        public void UpdateClosingItselfStopsFurtherUpdates()
        {
            UIProbeWindow window = _fixture.Open();
            window.Updating = () => window.CloseThis();
            _fixture.Tick();
            _fixture.Tick();
            Assert.That(window.Updates, Is.EqualTo(1));
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
        }

        [Test]
        public void WidgetCreatedInOnInitializeIsEnabledOnceTheWindowOpens()
        {
            UIProbeWidget widget = null;
            UIProbeWindow.Initializing = window => widget = window.Create();
            _fixture.Open();
            _fixture.Tick();
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            Assert.That(widget.Updates, Is.EqualTo(1));
            UIFixture.Publish();
            Assert.That(widget.Events, Is.EqualTo(1));
        }

        [Test]
        public void NestedWidgetsFollowEveryAncestor()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget middle = window.Create();
            UIProbeWidget leaf = middle.Create();
            Assert.That(leaf.Holder.IsEnabled, Is.True);
            middle.Close();
            Assert.That(leaf.Holder.IsEnabled, Is.False);
            _fixture.Tick();
            Assert.That(leaf.State, Is.EqualTo(UIState.Opened));
            Assert.That(leaf.Updates, Is.Zero);
            middle.Open();
            Assert.That(leaf.Holder.IsEnabled, Is.True);
            _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(leaf.Holder.IsEnabled, Is.False);
            _fixture.Tick();
            Assert.That(leaf.Updates, Is.Zero);
            _fixture.Open();
            Assert.That(leaf.Holder.IsEnabled, Is.True);
            _fixture.Tick();
            Assert.That(leaf.Updates, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AsyncWidgetIsDestroyedWhenItsWindowIsDestroyedDuringLoad()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWidget> loading = window.CreateAsync();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            UIProbeWidget widget = null;
            yield return _fixture.Wait(loading, value => widget = value);
            Assert.That(widget, Is.Null);
        }

        [UnityTest]
        public IEnumerator AsyncWidgetLoadedUnderClosedWindowStaysDisabled()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWidget> loading = window.CreateAsync();
            _fixture.Service.CloseUI<UIProbeWindow>();
            _fixture.Loader.Deliver();
            UIProbeWidget widget = null;
            yield return _fixture.Wait(loading, value => widget = value);
            _fixture.Tick();
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            Assert.That(widget.Updates, Is.Zero);
            _fixture.Open();
            _fixture.Tick();
            Assert.That(widget.Updates, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SwitchingToTheSameLoadingTabTwiceKeepsOnlyTheLatest()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            UniTask<UIWidget> first = window.SwitchTab(0, "a");
            UniTask<UIWidget> second = window.SwitchTab(0, "b");
            Assert.That(_fixture.Loader.PendingCount, Is.EqualTo(1));
            _fixture.Loader.Deliver();
            UIWidget a = null, b = null;
            yield return _fixture.Wait(first, value => a = value);
            yield return _fixture.Wait(second, value => b = value);
            Assert.That(a, Is.Null);
            var tab = (UIProbeWidget)b;
            Assert.That(tab.Argument, Is.EqualTo("b"));
            Assert.That(tab.Opens, Is.EqualTo(1));
            Assert.That(window.ActiveTab, Is.SameAs(tab));
        }

        [UnityTest]
        public IEnumerator OutOfOrderTabLoadsActivateOnlyTheLatestRequest()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            UniTask<UIWidget> toFirst = window.SwitchTab(0);
            UniTask<UIWidget> toSecond = window.SwitchTab(1);
            UniTask<UIWidget> back = window.SwitchTab(0);
            _fixture.Loader.Deliver(1);
            _fixture.Loader.Deliver(0);
            UIWidget a = null, b = null, c = null;
            yield return _fixture.Wait(toFirst, value => a = value);
            yield return _fixture.Wait(toSecond, value => b = value);
            yield return _fixture.Wait(back, value => c = value);
            Assert.That(a, Is.Null);
            Assert.That(b, Is.Null);
            Assert.That(c, Is.InstanceOf<UIProbeWidget>());
            Assert.That(window.ActiveTab, Is.SameAs(c));
            _fixture.Loader.Deferred = false;
            UIWidget second = window.SwitchTab(1).GetAwaiter().GetResult();
            Assert.That(second, Is.InstanceOf<UISecondWidget>());
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(3));
            Assert.That(c.State, Is.EqualTo(UIState.Closed));
        }

        [Test]
        public void TabSwitchedWhileWindowIsClosedStaysDisabledUntilReopen()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            window.SwitchTab(1).GetAwaiter().GetResult();
            _fixture.Service.CloseUI<UIProbeTabWindow>();
            var tab = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            _fixture.Tick();
            Assert.That(tab.State, Is.EqualTo(UIState.Opened));
            Assert.That(tab.Updates, Is.Zero);
            _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Tick();
            Assert.That(tab.Updates, Is.EqualTo(1));
            Assert.That(window.ActiveTab, Is.SameAs(tab));
        }

        [Test]
        public void FailedTabLoadReturnsNullAndTheNextSwitchRetries()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            Assert.That(window.SwitchTab(0).GetAwaiter().GetResult(), Is.Null);
            Assert.That(window.ActiveTab, Is.Null);
            _fixture.Loader.Fail = false;
            UIWidget tab = window.SwitchTab(0).GetAwaiter().GetResult();
            Assert.That(tab.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.ActiveTab, Is.SameAs(tab));
        }

        [Test]
        public void SwitchingToTheActiveTabRefreshesIt()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var tab = (UIProbeWidget)window.SwitchTab(0, "a").GetAwaiter().GetResult();
            UIWidget again = window.SwitchTab(0, "b").GetAwaiter().GetResult();
            Assert.That(again, Is.SameAs(tab));
            Assert.That(tab.Opens, Is.EqualTo(1));
            Assert.That(tab.Refreshes, Is.EqualTo(1));
            Assert.That(tab.Argument, Is.EqualTo("b"));
        }

        [UnityTest]
        public IEnumerator DestroyingTheTabWindowDuringATabLoadReturnsNull()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            _fixture.Loader.Deferred = true;
            UniTask<UIWidget> loading = window.SwitchTab(0);
            _fixture.Service.CloseUI<UIProbeTabWindow>(force: true);
            UIWidget tab = null;
            yield return _fixture.Wait(loading, value => tab = value);
            Assert.That(tab, Is.Null);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
        }

        [Test]
        public void DestroyedActiveTabDoesNotBlockSwitchingAway()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            UIWidget first = window.SwitchTab(0).GetAwaiter().GetResult();
            first.Destroy();
            UIWidget second = window.SwitchTab(1).GetAwaiter().GetResult();
            Assert.That(second.State, Is.EqualTo(UIState.Opened));
            Assert.That(window.ActiveTab, Is.SameAs(second));
            UIWidget recreated = window.SwitchTab(0).GetAwaiter().GetResult();
            Assert.That(recreated, Is.Not.SameAs(first));
            Assert.That(second.State, Is.EqualTo(UIState.Closed));
        }

        [Test]
        public void CachedTabWindowKeepsItsActiveTabAcrossReopen()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            UIWidget tab = window.SwitchTab(1).GetAwaiter().GetResult();
            _fixture.Service.CloseUI<UIProbeTabWindow>();
            Assert.That(_fixture.Service.ShowUISync<UIProbeTabWindow>(), Is.SameAs(window));
            Assert.That(window.ActiveTab, Is.SameAs(tab));
            Assert.That(tab.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void ShutdownDuringCloseTransitionIgnoresTheLateFinish()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Service.CloseUI<UIProbeWindow>();
            AppServices.Shutdown();
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            LogAssert.Expect(LogType.Error, new Regex("not running"));
            Assert.That(_fixture.Service.ShowUISync<UIProbeWindow>(), Is.Null);
        }
    }
}
#endif
