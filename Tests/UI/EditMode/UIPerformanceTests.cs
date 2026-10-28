using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIPerformanceTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [UnityTest]
        public IEnumerator CachedCloseWithoutTransitionAndReopenDoNotAllocate()
        {
            var window = _fixture.Open();
            for (int i = 0; i < 20; i++) Cycle();
            int loads = _fixture.Loader.Loads;
            yield return AllocationCapture.Measure("ui-close-cached-window-only", 1,
                () => _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult(),
                sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.Destroys, Is.Zero);
            UIProbeWindow reopened = null;
            yield return AllocationCapture.Measure("ui-reopen-cached-window-only", 1,
                () => reopened = _fixture.Open(), sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(reopened, Is.SameAs(window));
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(loads));
        }

        [UnityTest]
        public IEnumerator CloseWithTransitionAllocatesOnlyTransitionState()
        {
            var window = _fixture.Open();
            UIProbeWidget child = window.Create();
            var source = new UITickGateTransition();
            window.Holder.SetTransition(source);

            // 预热：首次异步关闭会分配一次 UniTask 状态机池对象；本次测量只覆盖稳定态。
            UniTask warm = _fixture.Service.CloseUI<UIProbeWindow>();
            source.Finished = true;
            _fixture.Tick();
            warm.GetAwaiter().GetResult();
            _fixture.Open();

            yield return AllocationCapture.Measure("ui-close-with-transition", 1,
                () =>
                {
                    UniTask close = _fixture.Service.CloseUI<UIProbeWindow>();
                    source.Finished = true;
                    _fixture.Tick();
                    close.GetAwaiter().GetResult();
                },
                sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(256)));
            Assert.That(window.IsOpen, Is.False);
            Assert.That(child.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator CachedWindowWithSixteenWidgetsAndSteadyUpdate()
        {
            yield return AllocationCapture.Measure("calibration", 1,
                () => GC.KeepAlive(new byte[4096]), sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096)));
            UIProbeWindow window = _fixture.Open();
            for (int i = 0; i < 16; i++) window.Create();
            for (int i = 0; i < 20; i++) Cycle();
            yield return AllocationCapture.Measure("cache-cycle-16-widgets", 400,
                () => { for (int i = 0; i < 400; i++) Cycle(); }, sample => Assert.That(sample.Bytes, Is.Zero));
            for (int i = 0; i < 20; i++) _fixture.Tick();
            yield return AllocationCapture.Measure("steady-update-17-views", 10000,
                () => { for (int i = 0; i < 10000; i++) _fixture.Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
        }

        private void Cycle()
        {
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Open();
        }

        [UnityTest]
        public IEnumerator WideAndDeepTrees()
        {
            UIProbeWindow window = _fixture.Open();
            yield return AllocationCapture.Measure("create-512-widgets", 512,
                () => { for (int i = 0; i < 512; i++) window.Create(); }, sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            for (int i = 0; i < 10; i++) _fixture.Tick();
            yield return AllocationCapture.Measure("steady-update-513-views", 1000,
                () => { for (int i = 0; i < 1000; i++) _fixture.Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("cache-cycle-512-widgets", 40,
                () => { for (int i = 0; i < 40; i++) Cycle(); }, sample => Assert.That(sample.Bytes, Is.Zero));
            UIProbeWidget deep = window.Create();
            for (int i = 0; i < 64; i++) deep = deep.Create();
            for (int i = 0; i < 10; i++) _fixture.Tick();
            yield return AllocationCapture.Measure("steady-update-plus-65-depth", 1000,
                () => { for (int i = 0; i < 1000; i++) _fixture.Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
        }
    }
}
