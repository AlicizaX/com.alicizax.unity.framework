#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public abstract class UINavigationCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();
        private void Success(UniTask<UIRouteResult> task) => Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
        private static bool IsOpen(UIBase view) => view.State is UIState.Opening or UIState.Opened;

        private IEnumerator Expect(UniTask<UIRouteResult> task, UIRouteStatus status)
        {
            UIRouteResult result = default;
            yield return _fixture.Wait(task, value => result = value);
            Assert.That(result.Status, Is.EqualTo(status));
        }

        [TestCase("back")] [TestCase("root")] [TestCase("existing")]
        public void BackVariantsRestoreCorrectPageAndArguments(string operation)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>("root"));
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            var child = root.Create();
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>("middle"));
            Success(_fixture.Service.Navigation.NavigateTo<UIThirdWindow>("last"));
            if (operation == "back") Success(_fixture.Service.Navigation.Back());
            else if (operation == "root") Success(_fixture.Service.Navigation.BackToRoot());
            else Success(_fixture.Service.Navigation.BackTo<UIProbeWindow>());
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(operation == "back" ? typeof(UISecondWindow) : typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.EqualTo(operation == "back"));
            if (operation != "back")
            {
                Assert.That(root.Argument, Is.EqualTo("root"));
                Assert.That(child.Opens, Is.EqualTo(1));
                Assert.That(child.Visible, Is.True);
            }
            Assert.That(_fixture.Service.IsOpen<UIThirdWindow>(), Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void ReplaceAndResetHaveDifferentHistory(bool reset)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            if (reset) Success(_fixture.Service.Navigation.ResetTo<UIThirdWindow>());
            else Success(_fixture.Service.Navigation.Replace<UIThirdWindow>());
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIThirdWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.EqualTo(!reset));
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.False);
            if (!reset)
            {
                Success(_fixture.Service.Navigation.Back());
                Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void BackToMissingEitherFailsOrResets(bool open)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            var result = _fixture.Service.Navigation.BackTo<UIThirdWindow>(open, "new").GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(open ? UIRouteStatus.Success : UIRouteStatus.NotFound));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(open ? typeof(UIThirdWindow) : typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
        }

        [Test]
        public void NavigatingToExistingPageTruncatesHistoryWithoutDuplicatingInstance()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>("first"));
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>("return"));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(root));
            Assert.That(root.Argument, Is.EqualTo("return"));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(2));
        }

        [TestCase("back")] [TestCase("root")] [TestCase("close")]
        public void EmptyHistoryReturnsNotFound(string operation)
        {
            var result = operation == "back" ? _fixture.Service.Navigation.Back() : operation == "root"
                ? _fixture.Service.Navigation.BackToRoot() : _fixture.Service.Navigation.CloseCurrent();
            Assert.That(result.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
        }

        [Test]
        public void ResetHistoryDoesNotCloseVisiblePageAndSyncAdoptsIt()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            Success(_fixture.Service.Navigation.ResetHistory());
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.Null);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
            Success(_fixture.Service.Navigation.SyncFromCurrentUI(typeof(UIProbeWindow), "adopt"));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UserData, Is.EqualTo("adopt"));
            Assert.That(_fixture.Service.Navigation.SyncFromCurrentUI(typeof(UISecondWindow)).GetAwaiter().GetResult().Status,
                Is.EqualTo(UIRouteStatus.NotFound));
        }

        [TestCase(false)] [TestCase(true)]
        public void CloseCurrentClearsHistoryAndHonorsForce(bool force)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            var window = _fixture.Service.GetUI<UIProbeWindow>();
            Success(_fixture.Service.Navigation.CloseCurrent(force));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.Null);
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
            Assert.That(IsOpen(window), Is.False);
            Assert.That(window.Destroys, Is.EqualTo(force ? 1 : 0));
        }

        [Test]
        public void LeavingZeroCachePageKeepsHistoryAndBackReloadsIt()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIZeroCacheWindow>("zero"));
            var zero = _fixture.Service.GetUI<UIZeroCacheWindow>();
            // 前一页面关闭即销毁（cacheTime 为 0、无转场），不能清掉刚提交的 history
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            Assert.That(zero.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.True);
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            Success(_fixture.Service.Navigation.Back());
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Success(_fixture.Service.Navigation.Back());
            var reloaded = _fixture.Service.GetUI<UIZeroCacheWindow>();
            Assert.That(reloaded, Is.Not.Null.And.Not.SameAs(zero));
            Assert.That(IsOpen(reloaded), Is.True);
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UserData, Is.EqualTo("zero"));
        }

        [UnityTest]
        public IEnumerator RepeatedNavigationToLoadingTargetReusesTheLoad()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.Navigation.NavigateTo<UISecondWindow>("a");
            var second = _fixture.Service.Navigation.NavigateTo<UISecondWindow>("b");
            Assert.That(_fixture.Loader.PendingCount, Is.EqualTo(1));
            _fixture.Loader.Deliver();
            yield return Expect(first, UIRouteStatus.Cancelled);
            yield return Expect(second, UIRouteStatus.Success);
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(2));
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.Not.Null);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UserData, Is.EqualTo("b"));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.True);
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator SupersededNavigationKeepsTheWindowShowUIRequested([Values(false, true)] bool showFirst)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            UniTask<UISecondWindow> shown = default;
            if (showFirst) shown = _fixture.Service.ShowUI<UISecondWindow>();
            var navigation = _fixture.Service.Navigation.NavigateTo<UISecondWindow>();
            if (!showFirst) shown = _fixture.Service.ShowUI<UISecondWindow>();
            var other = _fixture.Service.Navigation.NavigateTo<UIThirdWindow>();
            Assert.That(_fixture.Loader.PendingCount, Is.EqualTo(2));
            _fixture.Loader.Deliver();
            yield return Expect(navigation, UIRouteStatus.Cancelled);
            yield return _fixture.Wait(shown, _ => { });
            _fixture.Loader.Deliver();
            yield return Expect(other, UIRouteStatus.Success);
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.True);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIThirdWindow)));
            _fixture.Verify();
        }

        [Test]
        public void RouteEntryCarriesUserDataAsValue()
        {
            var data = new object();
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>(data));
            UIRouteEntry entry = _fixture.Service.Navigation.CurrentEntry;
            Assert.That(entry.IsValid, Is.True);
            Assert.That(entry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(entry.UserData, Is.SameAs(data));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>().Argument, Is.SameAs(data));
        }

        [TestCase("push")] [TestCase("replace")] [TestCase("reset")]
        public void FailedOpenLeavesCurrentAndHistoryIntact(string operation)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            var task = operation == "push" ? _fixture.Service.Navigation.NavigateTo<UISecondWindow>()
                : operation == "replace" ? _fixture.Service.Navigation.Replace<UISecondWindow>() : _fixture.Service.Navigation.ResetTo<UISecondWindow>();
            Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.OpenFailed));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
        }

        [UnityTest]
        public IEnumerator BackDuringLoadingCancelsNavigationWithoutClosingCurrent()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            var pending = _fixture.Service.Navigation.NavigateTo<UISecondWindow>();
            Success(_fixture.Service.Navigation.Back());
            Assert.That(_fixture.Service.GetWindow(typeof(UISecondWindow).TypeHandle), Is.Null);
            _fixture.Loader.Deliver();
            yield return Expect(pending, UIRouteStatus.Cancelled);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.False);
        }

        [TestCase("back")] [TestCase("root")] [TestCase("existing")]
        public void FailedHistoryReloadPreservesSourceAndCanBeRetried(string operation)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>("root"));
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>("source"));
            var source = _fixture.Service.GetUI<UISecondWindow>();
            _fixture.Service.DestroyWindow(root);
            var before = _fixture.Service.Navigation.CurrentEntry;
            Func<UniTask<UIRouteResult>> back = operation == "back" ? () => _fixture.Service.Navigation.Back()
                : operation == "root" ? () => _fixture.Service.Navigation.BackToRoot()
                : () => _fixture.Service.Navigation.BackTo<UIProbeWindow>();
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            Assert.That(back().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.OpenFailed));
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.SameAs(source));
            Assert.That(IsOpen(source), Is.True);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(before.UIType));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UserData, Is.SameAs(before.UserData));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.True);
            _fixture.Loader.Fail = false;
            Success(back());
            var restored = _fixture.Service.GetUI<UIProbeWindow>();
            Assert.That(restored, Is.Not.SameAs(root));
            Assert.That(restored.Argument, Is.EqualTo("root"));
            Assert.That(IsOpen(source), Is.False);
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
        }

        [UnityTest]
        public IEnumerator BackCancelsOtherOpeningCommandsWithoutChangingCommittedHistory([Values("replace", "reset")] string operation)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>("root"));
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>("source"));
            var before = _fixture.Service.Navigation.CurrentEntry;
            _fixture.Loader.Deferred = true;
            var opening = operation == "replace" ? _fixture.Service.Navigation.Replace<UIThirdWindow>()
                : _fixture.Service.Navigation.ResetTo<UIThirdWindow>();
            Success(_fixture.Service.Navigation.Back());
            Assert.That(_fixture.Service.GetWindow(typeof(UIThirdWindow).TypeHandle), Is.Null);
            _fixture.Loader.Deliver();
            yield return Expect(opening, UIRouteStatus.Cancelled);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UserData, Is.SameAs(before.UserData));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.True);
            Assert.That(_fixture.Service.IsOpen<UIThirdWindow>(), Is.False);
        }

        [UnityTest]
        public IEnumerator ResetHistoryDuringLoadCancelsUncommittedRouteAndPreservesSource([Values(false, true)] bool shutdown)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            var source = _fixture.Service.GetUI<UIProbeWindow>();
            _fixture.Loader.Deferred = true;
            var opening = _fixture.Service.Navigation.NavigateTo<UISecondWindow>();
            if (shutdown) AppServices.Shutdown();
            else Success(_fixture.Service.Navigation.ResetHistory());
            Assert.That(_fixture.Service.GetWindow(typeof(UISecondWindow).TypeHandle), Is.Null);
            _fixture.Loader.Deliver();
            yield return Expect(opening, UIRouteStatus.Cancelled);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.Null);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.IsValid, Is.False);
            Assert.That(IsOpen(source), Is.EqualTo(!shutdown));
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.False);
        }

        [TestCase("null")] [TestCase("widget")] [TestCase("closed")]
        public void InvalidSyncDoesNotEraseCommittedRoute(string invalid)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>("root"));
            var before = _fixture.Service.Navigation.CurrentEntry;
            var result = _fixture.Service.Navigation.SyncFromCurrentUI(invalid == "null" ? null
                : invalid == "widget" ? typeof(UIProbeWidget) : typeof(UISecondWindow));
            Assert.That(result.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(before.UIType));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UserData, Is.SameAs(before.UserData));
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void DirectCloseOrDestroyOfCurrentPageClearsNavigation(bool destroy)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            if (destroy) _fixture.Service.DestroyLostWindow(_fixture.Service.GetUI<UIProbeWindow>());
            else _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition().GetAwaiter().GetResult();
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.Null);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.IsValid, Is.False);
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
            Assert.That(_fixture.Service.Navigation.Back().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
        }

        [Test]
        public void ShutdownReleasesEventSubscriptionsBeforeFixtureCleanup()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            var child = root.Create();
            var leaf = child.Create();
            Assert.That(EventBus.GetEmptySubscriberCount<UIProbeEvent>(), Is.EqualTo(2));
            AppServices.Shutdown();
            Assert.That(EventBus.GetEmptySubscriberCount<UIProbeEvent>(), Is.Zero);
            Assert.That(root.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(leaf.Destroys, Is.EqualTo(1));
            Assert.That(MemoryPool<EventListenerProxy>.Instance.GetInfo().LeasedCount, Is.Zero);
            Assert.That(_fixture.Loader.PendingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LatestNavigationWinsDespiteOutOfOrderLoads()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.Navigation.NavigateTo<UISecondWindow>();
            var second = _fixture.Service.Navigation.NavigateTo<UIThirdWindow>();
            Assert.That(_fixture.Service.GetWindow(typeof(UISecondWindow).TypeHandle), Is.Null);
            _fixture.Loader.Deliver(1);
            yield return Expect(second, UIRouteStatus.Success);
            _fixture.Loader.Deliver();
            yield return Expect(first, UIRouteStatus.Cancelled);
            _fixture.Loader.Deferred = false;
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIThirdWindow)));
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.False);
            Success(_fixture.Service.Navigation.Back());
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
        }

        [Test]
        public void BackToClosingTargetReversesItsCloseImmediately()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>());
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            var gate = _fixture.Gate(root);
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            Assert.That(root.State, Is.EqualTo(UIState.Closing));
            // 关闭转场中的目标直接反向打开，导航在逻辑打开后即完成
            var back = _fixture.Service.Navigation.Back();
            Assert.That(back.Status.IsCompleted(), Is.True);
            Success(back);
            Assert.That(root.State, Is.EqualTo(UIState.Opening));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            gate.Finish();
            Assert.That(root.State, Is.EqualTo(UIState.Opened));
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator CompletedNavigationContinuationMayNavigateAgain()
        {
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            UniTask<UIRouteResult> second = default;
            first.GetAwaiter().OnCompleted(() => second = _fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            _fixture.Loader.Deliver();
            for (int i = 0; _fixture.Loader.PendingCount == 0 && i < 10; i++) yield return _fixture.Frames(1);
            _fixture.Loader.Deliver();
            yield return Expect(second, UIRouteStatus.Success);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void NavigationRequestedInsideOnOpenSupersedesUncommittedRoute(bool cached)
        {
            UniTask<UIRouteResult> next = default;
            if (cached)
            {
                var window = _fixture.Open();
                _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition().GetAwaiter().GetResult();
                window.Opening = () => next = _fixture.Service.Navigation.NavigateTo<UISecondWindow>();
            }
            else UIProbeWindow.Initializing = window => window.Opening = () => next = _fixture.Service.Navigation.NavigateTo<UISecondWindow>();
            var first = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            Assert.That(first.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            Success(next);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.False);
        }

        private void OnProbeOpening(bool cached, Action<UIProbeWindow> opening)
        {
            Action<UIProbeWindow> install = window => window.Opening = () =>
            {
                window.Opening = null;
                opening(window);
            };
            if (cached)
            {
                UIProbeWindow window = _fixture.Open();
                _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition().GetAwaiter().GetResult();
                install(window);
            }
            else UIProbeWindow.Initializing = install;
        }

        [Test]
        public void FailedNavigationReleasesItsOwnership()
        {
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            Assert.That(_fixture.Service.Navigation.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.OpenFailed));
            Assert.That(_fixture.Service.Navigation.Back().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void BackInsideTargetOnOpenUndoesOnlyThatNavigation(bool cached)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UIThirdWindow>());
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            UniTask<UIRouteResult> back = default;
            OnProbeOpening(cached, _ => back = _fixture.Service.Navigation.Back());
            var first = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            Assert.That(first.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            Success(back);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.Navigation.CanBack, Is.True);
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.True);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void ShowUISyncInsideNavigationHookAdoptsTheWindow(bool cached)
        {
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            UniTask<UIRouteResult> next = default;
            OnProbeOpening(cached, _ =>
            {
                _fixture.Service.ShowUISync<UIProbeWindow>();
                next = _fixture.Service.Navigation.NavigateTo<UIThirdWindow>();
            });
            var first = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            Assert.That(first.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            Success(next);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIThirdWindow)));
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void SameTargetNavigationInsideOnOpenKeepsTheTargetOpen(bool cached)
        {
            UniTask<UIRouteResult> next = default;
            Success(_fixture.Service.Navigation.NavigateTo<UISecondWindow>());
            Action<UIProbeWindow> navigateOnOpen = window => window.Opening = () =>
            {
                window.Opening = null;
                next = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            };
            if (cached)
            {
                UIProbeWindow window = _fixture.Open();
                _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition().GetAwaiter().GetResult();
                navigateOnOpen(window);
            }
            else UIProbeWindow.Initializing = navigateOnOpen;
            var first = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            Assert.That(first.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            Success(next);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.False);
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator ShutdownDuringNavigationCancelsCallerAndLateLoad()
        {
            _fixture.Loader.Deferred = true;
            var task = _fixture.Service.Navigation.NavigateTo<UIProbeWindow>();
            AppServices.Shutdown();
            _fixture.Loader.Deliver();
            yield return Expect(task, UIRouteStatus.Cancelled);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.Null);
        }

        [Test]
        public void HistoryHasNoPageLimitAndBackToRootStillWorks()
        {
            const int pages = 65;
            Type last = null;
            var register = typeof(UIFixture).GetMethod(nameof(UIFixture.RegisterWindow), BindingFlags.Static | BindingFlags.NonPublic);
            var navigate = typeof(IUINavigationService).GetMethod(nameof(IUINavigationService.NavigateTo));
            for (int i = 0; i < pages; i++)
            {
                last = typeof(UIHistoryPages).GetNestedType("Page" + i, BindingFlags.NonPublic);
                register.MakeGenericMethod(last).Invoke(null, new object[] { UICacheTime.Forever, false, UILayer.UI });
                var task = (UniTask<UIRouteResult>)navigate.MakeGenericMethod(last).Invoke(_fixture.Service.Navigation,
                    new object[] { null });
                Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            }
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(pages));
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(last));
            Success(_fixture.Service.Navigation.BackToRoot());
            Assert.That(_fixture.Service.Navigation.CanBack, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void TabSwitchLatestRequestWinsWhilePreviousCloses(bool returnToPrevious)
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var first = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            var child = first.Create();
            var gate = _fixture.Gate(first);
            // 已加载与同步加载的页签切换立即完成；前一个页签的关闭转场与切换并行
            UIWidget second = window.SwitchTab(1).GetAwaiter().GetResult();
            Assert.That(second, Is.Not.Null);
            var latest = window.SwitchTab(returnToPrevious ? 0 : 1, "last");
            gate.Finish();
            var selected = latest.GetAwaiter().GetResult();
            Assert.That(selected, Is.SameAs(returnToPrevious ? first : second));
            Assert.That(window.ActiveTab, Is.SameAs(selected));
            Assert.That(IsOpen(first), Is.EqualTo(returnToPrevious));
            Assert.That(IsOpen(child), Is.True);
            Assert.That(child.IsEnabled, Is.EqualTo(returnToPrevious));
            if (returnToPrevious) gate.Finish();
        }

        [Test]
        public void RemovedTabIsRecreatedOnNextSelection()
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var first = window.SwitchTab(0).GetAwaiter().GetResult();
            first.Destroy().AwaitTransition().GetAwaiter().GetResult();
            var next = window.SwitchTab(0).GetAwaiter().GetResult();
            Assert.That(next, Is.Not.SameAs(first));
            Assert.That(IsOpen(next), Is.True);
        }

        [TestCase(-1)] [TestCase(2)]
        public void InvalidTabIndexReturnsNull(int index)
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            LogAssert.Expect(LogType.Error, new Regex("Invalid tab index"));
            Assert.That(window.SwitchTab(index).GetAwaiter().GetResult(), Is.Null);
        }
    }
}
#endif
