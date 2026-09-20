using System;
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
        [TearDown] public void TearDown() => _fixture.Dispose();
        private void Success(UniTask<UIRouteResult> task) => Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));

        [TestCase("back")] [TestCase("root")] [TestCase("existing")]
        public void BackVariantsRestoreCorrectPageAndArguments(string operation)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>("root"));
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            var child = root.Create();
            Success(_fixture.Service.NavigateTo<UISecondWindow>("middle"));
            Success(_fixture.Service.NavigateTo<UIThirdWindow>("last"));
            if (operation == "back") Success(_fixture.Service.Back());
            else if (operation == "root") Success(_fixture.Service.BackToRoot());
            else Success(_fixture.Service.BackTo<UIProbeWindow>());
            Assert.That(_fixture.Service.Current, Is.EqualTo(operation == "back" ? typeof(UISecondWindow) : typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.CanBack, Is.EqualTo(operation == "back"));
            if (operation != "back")
            {
                Assert.That(root.Argument, Is.EqualTo("root"));
                Assert.That(child.Opens, Is.EqualTo(1));
                Assert.That(child.IsVisible, Is.True);
            }
            Assert.That(_fixture.Service.IsOpen<UIThirdWindow>(), Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void ReplaceAndResetHaveDifferentHistory(bool reset)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            Success(_fixture.Service.NavigateTo<UISecondWindow>());
            if (reset) Success(_fixture.Service.ResetTo<UIThirdWindow>());
            else Success(_fixture.Service.Replace<UIThirdWindow>());
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIThirdWindow)));
            Assert.That(_fixture.Service.CanBack, Is.EqualTo(!reset));
            Assert.That(_fixture.Service.IsOpen<UISecondWindow>(), Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.False);
            if (!reset)
            {
                Success(_fixture.Service.Back());
                Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIProbeWindow)));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void BackToMissingEitherFailsOrResets(bool open)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            var result = _fixture.Service.BackTo<UIThirdWindow>(open, "new").GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(open ? UIRouteStatus.Success : UIRouteStatus.NotFound));
            Assert.That(_fixture.Service.Current, Is.EqualTo(open ? typeof(UIThirdWindow) : typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.CanBack, Is.False);
        }

        [Test]
        public void NavigatingToExistingPageTruncatesHistoryWithoutDuplicatingInstance()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>("first"));
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            Success(_fixture.Service.NavigateTo<UISecondWindow>());
            Success(_fixture.Service.NavigateTo<UIProbeWindow>("return"));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(root));
            Assert.That(root.Argument, Is.EqualTo("return"));
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(2));
        }

        [TestCase("back")] [TestCase("root")] [TestCase("close")]
        public void EmptyHistoryReturnsNotFound(string operation)
        {
            var result = operation == "back" ? _fixture.Service.Back() : operation == "root"
                ? _fixture.Service.BackToRoot() : _fixture.Service.CloseCurrent();
            Assert.That(result.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
        }

        [Test]
        public void ResetHistoryDoesNotCloseVisiblePageAndSyncAdoptsIt()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            Success(_fixture.Service.ResetHistory());
            Assert.That(_fixture.Service.Current, Is.Null);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
            Success(_fixture.Service.SyncFromCurrentUI(typeof(UIProbeWindow), "adopt"));
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.CurrentEntry.Args[0], Is.EqualTo("adopt"));
            Assert.That(_fixture.Service.SyncFromCurrentUI(typeof(UISecondWindow)).GetAwaiter().GetResult().Status,
                Is.EqualTo(UIRouteStatus.NotFound));
        }

        [TestCase(false)] [TestCase(true)]
        public void CloseCurrentClearsHistoryAndHonorsForce(bool force)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            var window = _fixture.Service.GetUI<UIProbeWindow>();
            Success(_fixture.Service.CloseCurrent(force));
            Assert.That(_fixture.Service.Current, Is.Null);
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.Destroys, Is.EqualTo(force ? 1 : 0));
        }

        [Test]
        public void RouteArgumentsAreCopiedOnInputAndQuery()
        {
            object[] args = { "original" };
            Success(_fixture.Service.NavigateTo<UIProbeWindow>(args));
            args[0] = "changed";
            var entry = _fixture.Service.CurrentEntry;
            entry.Args[0] = "changed again";
            Assert.That(_fixture.Service.CurrentEntry.Args[0], Is.EqualTo("original"));
        }

        [TestCase("push")] [TestCase("replace")] [TestCase("reset")]
        public void FailedOpenLeavesCurrentAndHistoryIntact(string operation)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            var task = operation == "push" ? _fixture.Service.NavigateTo<UISecondWindow>()
                : operation == "replace" ? _fixture.Service.Replace<UISecondWindow>() : _fixture.Service.ResetTo<UISecondWindow>();
            Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.OpenFailed));
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
        }

        [Test]
        public void BackDuringLoadingCancelsNavigationWithoutClosingCurrent()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            var pending = _fixture.Service.NavigateTo<UISecondWindow>();
            Success(_fixture.Service.Back());
            Assert.That(pending.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.Null);
        }

        [TestCase("back")] [TestCase("root")] [TestCase("existing")]
        public void FailedHistoryReloadPreservesSourceAndCanBeRetried(string operation)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>("root"));
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            Success(_fixture.Service.NavigateTo<UISecondWindow>("source"));
            var source = _fixture.Service.GetUI<UISecondWindow>();
            root.DestroyNow();
            var before = _fixture.Service.CurrentEntry;
            Func<UniTask<UIRouteResult>> back = operation == "back" ? () => _fixture.Service.Back()
                : operation == "root" ? () => _fixture.Service.BackToRoot()
                : () => _fixture.Service.BackTo<UIProbeWindow>();
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            Assert.That(back().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.OpenFailed));
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.SameAs(source));
            Assert.That(source.IsOpen, Is.True);
            Assert.That(_fixture.Service.CurrentEntry.Sequence, Is.EqualTo(before.Sequence));
            Assert.That(_fixture.Service.CanBack, Is.True);
            _fixture.Loader.Fail = false;
            Success(back());
            var restored = _fixture.Service.GetUI<UIProbeWindow>();
            Assert.That(restored, Is.Not.SameAs(root));
            Assert.That(restored.Argument, Is.EqualTo("root"));
            Assert.That(source.IsOpen, Is.False);
            Assert.That(_fixture.Service.CanBack, Is.False);
        }

        [TestCase("replace")] [TestCase("reset")]
        public void BackCancelsOtherOpeningCommandsWithoutChangingCommittedHistory(string operation)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>("root"));
            Success(_fixture.Service.NavigateTo<UISecondWindow>("source"));
            var before = _fixture.Service.CurrentEntry;
            _fixture.Loader.Deferred = true;
            var opening = operation == "replace" ? _fixture.Service.Replace<UIThirdWindow>()
                : _fixture.Service.ResetTo<UIThirdWindow>();
            Success(_fixture.Service.Back());
            Assert.That(opening.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.CurrentEntry.Sequence, Is.EqualTo(before.Sequence));
            Assert.That(_fixture.Service.CanBack, Is.True);
            Assert.That(_fixture.Service.GetUI<UIThirdWindow>(), Is.Null);
        }

        [Test]
        public void LatestRequestSupersedesCancelledNavigationContinuation()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.NavigateTo<UISecondWindow>();
            UniTask<UIRouteResult> continuation = default;
            first.GetAwaiter().OnCompleted(() => continuation = _fixture.Service.ResetTo<UIZeroCacheWindow>());
            var replaced = _fixture.Service.NavigateTo<UIThirdWindow>();
            Assert.That(replaced.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            Assert.That(_fixture.Loader.PendingCount, Is.EqualTo(2));
            _fixture.Loader.Deliver(1);
            Success(continuation);
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIZeroCacheWindow)));
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.Null);
            Assert.That(_fixture.Service.GetUI<UIThirdWindow>(), Is.Null);
        }

        [TestCase(false)] [TestCase(true)]
        public void ResetHistoryDuringLoadCancelsUncommittedRouteAndPreservesSource(bool shutdown)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            var source = _fixture.Service.GetUI<UIProbeWindow>();
            _fixture.Loader.Deferred = true;
            var opening = _fixture.Service.NavigateTo<UISecondWindow>();
            if (shutdown) AppServices.Shutdown();
            else Success(_fixture.Service.ResetHistory());
            Assert.That(opening.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.Current, Is.Null);
            Assert.That(_fixture.Service.CurrentEntry, Is.Null);
            Assert.That(source.IsOpen, Is.EqualTo(!shutdown));
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.Null);
        }

        [TestCase("null")] [TestCase("empty-handle")] [TestCase("widget")] [TestCase("closed")]
        public void InvalidSyncDoesNotEraseCommittedRoute(string invalid)
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>("root"));
            var before = _fixture.Service.CurrentEntry;
            var result = invalid == "empty-handle" ? _fixture.Service.SyncFromCurrentUI(default(RuntimeTypeHandle))
                : _fixture.Service.SyncFromCurrentUI(invalid == "null" ? null
                    : invalid == "widget" ? typeof(UIProbeWidget) : typeof(UISecondWindow));
            Assert.That(result.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
            Assert.That(_fixture.Service.CurrentEntry.Sequence, Is.EqualTo(before.Sequence));
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void DirectCloseOrDestroyOfCurrentPageClearsNavigation(bool destroy)
        {
            Success(_fixture.Service.NavigateTo<UISecondWindow>());
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            if (destroy) _fixture.Service.GetUI<UIProbeWindow>().DestroyNow();
            else _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(_fixture.Service.Current, Is.Null);
            Assert.That(_fixture.Service.CurrentEntry, Is.Null);
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(_fixture.Service.Back().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.NotFound));
        }

        [Test]
        public void ShutdownReleasesEventSubscriptionsBeforeFixtureCleanup()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            var child = root.Create();
            var leaf = child.Create();
            Assert.That(EventBus.GetEmptySubscriberCount<UIProbeEvent>(), Is.EqualTo(2));
            AppServices.Shutdown();
            Assert.That(EventBus.GetEmptySubscriberCount<UIProbeEvent>(), Is.Zero);
            Assert.That(root.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(leaf.Destroys, Is.EqualTo(1));
            var info = default(MemoryPoolInfo);
            MemoryPool<EventListenerProxy>.GetInfo(ref info);
            Assert.That(info.UsingCount, Is.Zero);
            Assert.That(_fixture.Loader.PendingCount, Is.Zero);
        }

        [Test]
        public void LatestNavigationWinsDespiteOutOfOrderLoads()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.NavigateTo<UISecondWindow>();
            var second = _fixture.Service.NavigateTo<UIThirdWindow>();
            Assert.That(first.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            _fixture.Loader.Deliver(1);
            Success(second);
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIThirdWindow)));
            Assert.That(_fixture.Service.GetUI<UISecondWindow>(), Is.Null);
            Success(_fixture.Service.Back());
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIProbeWindow)));
        }

        [Test]
        public void NavigationWaitsForClosingCachedTarget()
        {
            Success(_fixture.Service.NavigateTo<UIProbeWindow>());
            var root = _fixture.Service.GetUI<UIProbeWindow>();
            var gate = new UIGateTransition();
            root.Holder.SetTransition(gate);
            Success(_fixture.Service.NavigateTo<UISecondWindow>());
            var back = _fixture.Service.Back();
            Assert.That(back.Status.IsCompleted(), Is.False);
            gate.Finish();
            Success(back);
            Assert.That(root.IsOpen, Is.True);
            Assert.That(root.State, Is.EqualTo(UIState.Opening));
            gate.Finish();
        }

        [Test]
        public void CompletedNavigationContinuationMayNavigateAgain()
        {
            _fixture.Loader.Deferred = true;
            var first = _fixture.Service.NavigateTo<UIProbeWindow>();
            UniTask<UIRouteResult> second = default;
            first.GetAwaiter().OnCompleted(() => second = _fixture.Service.NavigateTo<UISecondWindow>());
            _fixture.Loader.Deliver();
            _fixture.Loader.Deliver();
            Success(second);
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.CanBack, Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void NavigationRequestedInsideOnOpenSupersedesUncommittedRoute(bool cached)
        {
            UniTask<UIRouteResult> next = default;
            if (cached)
            {
                var window = _fixture.Open();
                _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
                window.Opening = () => next = _fixture.Service.NavigateTo<UISecondWindow>();
            }
            else UIProbeWindow.Initializing = window => window.Opening = () => next = _fixture.Service.NavigateTo<UISecondWindow>();
            var first = _fixture.Service.NavigateTo<UIProbeWindow>();
            Assert.That(first.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            Success(next);
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UISecondWindow)));
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.False);
        }

        [Test]
        public void ShutdownDuringNavigationCancelsCallerAndLateLoad()
        {
            _fixture.Loader.Deferred = true;
            var task = _fixture.Service.NavigateTo<UIProbeWindow>();
            AppServices.Shutdown();
            Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Cancelled));
            _fixture.Loader.Deliver();
            Assert.That(_fixture.Service.Current, Is.Null);
        }

        [Test]
        public void HistoryLimitRejectsNewPageWithoutLoadingAndBackStillWorks()
        {
            Type last = null;
            var method = typeof(UIService).GetMethod(nameof(UIService.NavigateTo));
            for (int i = 0; i < 65; i++)
            {
                Type page = typeof(UIHistoryPages).GetNestedType("Page" + i, System.Reflection.BindingFlags.NonPublic);
                UIMetaRegistry.Register(page, typeof(UIWindowHolder), UILayer.UI, -1, false);
                var task = (UniTask<UIRouteResult>)method.MakeGenericMethod(page).Invoke(_fixture.Service,
                    new object[] { Array.Empty<object>() });
                Assert.That(task.GetAwaiter().GetResult().Status, Is.EqualTo(i < 64 ? UIRouteStatus.Success : UIRouteStatus.RejectedLimit));
                if (i < 64) last = page;
            }
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(64));
            Assert.That(_fixture.Service.Current, Is.EqualTo(last));
            Success(_fixture.Service.BackToRoot());
            Assert.That(_fixture.Service.CanBack, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void TabSwitchLatestRequestWinsWhilePreviousCloses(bool returnToPrevious)
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var first = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            var child = first.Create();
            var gate = new UIGateTransition();
            first.Holder.SetTransition(gate);
            var second = window.SwitchTab(1);
            var latest = window.SwitchTab(returnToPrevious ? 0 : 1, "last");
            Assert.That(second.GetAwaiter().GetResult(), Is.Null);
            gate.Finish();
            var selected = latest.GetAwaiter().GetResult();
            Assert.That(selected, Is.Not.Null);
            Assert.That(first.IsOpen, Is.EqualTo(returnToPrevious));
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.IsVisible, Is.EqualTo(returnToPrevious));
            if (returnToPrevious) gate.Finish();
        }

        [Test]
        public void RemovedTabIsRecreatedOnNextSelection()
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var first = window.SwitchTab(0).GetAwaiter().GetResult();
            first.Destroy().GetAwaiter().GetResult();
            var next = window.SwitchTab(0).GetAwaiter().GetResult();
            Assert.That(next, Is.Not.SameAs(first));
            Assert.That(next.IsOpen, Is.True);
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
