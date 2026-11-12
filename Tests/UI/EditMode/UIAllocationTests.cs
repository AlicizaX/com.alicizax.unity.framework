using System.Collections;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIAllocationTests
    {
        private const int Iterations = 100;
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private static void AssertZero(AllocationSample sample) => Assert.That(sample.Bytes, Is.Zero);

        [UnityTest]
        public IEnumerator TickWithEnabledViewsIsAllocationFree()
        {
            UIProbeWindow window = _fixture.Open();
            for (int i = 0; i < 31; i++) window.Create();
            for (int i = 0; i < 3; i++) _fixture.Tick();
            yield return AllocationCapture.Measure("ui-tick-32-views", 1000, () =>
            {
                for (int i = 0; i < 1000; i++) _fixture.Tick();
            }, AssertZero);
            Assert.That(window.Updates, Is.EqualTo(1003));
        }

        [UnityTest]
        public IEnumerator RefreshingOpenWindowIsAllocationFree()
        {
            _fixture.Open();
            for (int i = 0; i < 3; i++) Refresh();
            yield return AllocationCapture.Measure("ui-refresh", Iterations, () =>
            {
                for (int i = 0; i < Iterations; i++) Refresh();
            }, AssertZero);
        }

        [UnityTest]
        public IEnumerator OpenCloseCycleWithTransitionIsAllocationFree()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            for (int i = 0; i < 3; i++) Cycle(gate);
            yield return AllocationCapture.Measure("ui-open-close", Iterations, () =>
            {
                for (int i = 0; i < Iterations; i++) Cycle(gate);
            }, AssertZero);
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
        }

        [UnityTest]
        public IEnumerator DiscardedCloseWithTransitionIsAllocationFree()
        {
            UIProbeWindow window = _fixture.Open();
            UIGateTransition gate = _fixture.Gate(window);
            for (int i = 0; i < 3; i++) TransitionCycle(gate);
            yield return AllocationCapture.Measure("ui-close-discarded", Iterations, () =>
            {
                for (int i = 0; i < Iterations; i++) TransitionCycle(gate);
            }, AssertZero);
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
        }

        [UnityTest]
        public IEnumerator NavigatingBetweenLoadedPagesIsAllocationFree()
        {
            _fixture.Service.Navigation.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Service.Navigation.NavigateTo<UISecondWindow>().GetAwaiter().GetResult();
            _fixture.Service.Navigation.Back().GetAwaiter().GetResult();
            UIGateTransition probe = _fixture.Gate(_fixture.Service.GetUI<UIProbeWindow>());
            UIGateTransition second = _fixture.Gate(_fixture.Service.GetUI<UISecondWindow>());
            for (int i = 0; i < 3; i++) NavigationCycle(probe, second);
            yield return AllocationCapture.Measure("ui-navigation", Iterations, () =>
            {
                for (int i = 0; i < Iterations; i++) NavigationCycle(probe, second);
            }, AssertZero);
            Assert.That(_fixture.Service.Navigation.CurrentEntry.UIType, Is.EqualTo(typeof(UIProbeWindow)));
        }

        [UnityTest]
        public IEnumerator TopVisibleHolderQueryIsAllocationFree()
        {
            _fixture.Open();
            _fixture.Service.ShowUISync<UISecondWindow>();
            System.Predicate<UIHolderObjectBase> predicate = static holder => holder != null;
            for (int i = 0; i < 3; i++) _fixture.Service.TryGetTopVisibleHolder(predicate, out _);
            yield return AllocationCapture.Measure("ui-top-holder", 1000, () =>
            {
                for (int i = 0; i < 1000; i++) _fixture.Service.TryGetTopVisibleHolder(predicate, out _);
            }, AssertZero);
        }

        [UnityTest]
        public IEnumerator WidgetOpenCloseCycleIsAllocationFree()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            UIGateTransition gate = _fixture.Gate(widget);
            for (int i = 0; i < 3; i++) WidgetCycle(widget, gate);
            yield return AllocationCapture.Measure("ui-widget-cycle", Iterations, () =>
            {
                for (int i = 0; i < Iterations; i++) WidgetCycle(widget, gate);
            }, AssertZero);
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
        }

        [UnityTest]
        public IEnumerator SwitchingLoadedTabsIsAllocationFree()
        {
            UIProbeTabWindow window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            for (int i = 0; i < 3; i++) SwitchBoth(window);
            yield return AllocationCapture.Measure("ui-tab-switch", Iterations * 2, () =>
            {
                for (int i = 0; i < Iterations; i++) SwitchBoth(window);
            }, AssertZero);
        }

        private void Refresh()
        {
            _fixture.Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Service.ShowUISync<UIProbeWindow>();
        }

        private void Cycle(UIGateTransition gate)
        {
            _fixture.Service.CloseUI<UIProbeWindow>(snap: true).AwaitTransition().GetAwaiter().GetResult();
            _fixture.Open();
            gate.Finish();
        }

        private static void WidgetCycle(UIProbeWidget widget, UIGateTransition gate)
        {
            widget.Close();
            gate.Finish();
            widget.Open();
            gate.Finish();
        }

        private void TransitionCycle(UIGateTransition gate)
        {
            _fixture.Service.CloseUI<UIProbeWindow>();
            gate.Finish();
            _fixture.Open();
            gate.Finish();
        }

        private void NavigationCycle(UIGateTransition probe, UIGateTransition second)
        {
            _fixture.Service.Navigation.NavigateTo<UISecondWindow>().GetAwaiter().GetResult();
            second.Finish();
            probe.Finish();
            _fixture.Service.Navigation.Back().GetAwaiter().GetResult();
            probe.Finish();
            second.Finish();
        }

        private static void SwitchBoth(UIProbeTabWindow window)
        {
            window.SwitchTab(0).GetAwaiter().GetResult();
            window.SwitchTab(1).GetAwaiter().GetResult();
        }
    }
}
