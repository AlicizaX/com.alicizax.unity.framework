using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIPerformanceTests
    {
        private static object allocationSink;

        [UnityTest]
        public IEnumerator CachedCloseAllocationComesFromCompletionSourceAndReopenDoesNotAllocate()
        {
            yield return AllocationCapture.Measure("ui-close-calibration", 1,
                () => allocationSink = new byte[4096], sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096)));
            using var fixture = new UIFixture();
            var window = fixture.Open();
            for (int i = 0; i < 20; i++) Cycle(fixture);
            long completionBytes = 0;
            int completionAllocations = 0;
            UniTaskCompletionSource completion = null;
            Action createCompletion = () => completion = new UniTaskCompletionSource();
            createCompletion();
            completion.TrySetResult();
            completion.Task.GetAwaiter().GetResult();
            yield return AllocationCapture.Measure("ui-close-completion-source-construction", 1,
                createCompletion, sample =>
                {
                    completionBytes = sample.Bytes;
                    completionAllocations = sample.Allocations;
                    Assert.That(completionBytes, Is.GreaterThan(0));
                });
            yield return AllocationCapture.Measure("ui-close-completion-source-signal", 1,
                () => completion.TrySetResult(), sample =>
                {
                    completionBytes += sample.Bytes;
                    completionAllocations += sample.Allocations;
                    Assert.That(sample.Bytes, Is.GreaterThan(0));
                });
            completion.Task.GetAwaiter().GetResult();
            allocationSink = null;
            int loads = fixture.Loader.Loads;
            yield return AllocationCapture.Measure("ui-close-cached-window-only", 1,
                () => fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult(), sample =>
                {
                    Assert.That(sample.Bytes, Is.EqualTo(completionBytes));
                    Assert.That(sample.Allocations, Is.EqualTo(completionAllocations));
                });
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.Destroys, Is.Zero);
            UIProbeWindow reopened = null;
            yield return AllocationCapture.Measure("ui-reopen-cached-window-only", 1,
                () => reopened = fixture.Open(), sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(reopened, Is.SameAs(window));
            Assert.That(fixture.Loader.Loads, Is.EqualTo(loads));
        }

        [UnityTest]
        public IEnumerator CachedWindowWithSixteenWidgetsAndSteadyUpdate()
        {
            yield return AllocationCapture.Measure("calibration", 1,
                () => GC.KeepAlive(new byte[4096]), sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096)));
            using var fixture = new UIFixture();
            UIProbeWindow window = fixture.Open();
            for (int i = 0; i < 16; i++) window.Create();
            for (int i = 0; i < 20; i++) Cycle(fixture);
            yield return AllocationCapture.Measure("cache-cycle-16-widgets", 400,
                () => { for (int i = 0; i < 400; i++) Cycle(fixture); }, sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(0)));
            for (int i = 0; i < 20; i++) fixture.Tick();
            yield return AllocationCapture.Measure("steady-update-17-views", 10000,
                () => { for (int i = 0; i < 10000; i++) fixture.Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
        }

        private static void Cycle(UIFixture fixture)
        {
            fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            fixture.Open();
        }

        [UnityTest]
        public IEnumerator WideAndDeepTrees()
        {
            using var fixture = new UIFixture();
            UIProbeWindow window = fixture.Open();
            yield return AllocationCapture.Measure("create-512-widgets", 512,
                () => { for (int i = 0; i < 512; i++) window.Create(); }, sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            for (int i = 0; i < 10; i++) fixture.Tick();
            yield return AllocationCapture.Measure("steady-update-513-views", 1000,
                () => { for (int i = 0; i < 1000; i++) fixture.Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("cache-cycle-512-widgets", 40,
                () => { for (int i = 0; i < 40; i++) Cycle(fixture); }, sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(0)));
            UIProbeWidget deep = window.Create();
            for (int i = 0; i < 64; i++) deep = deep.Create();
            for (int i = 0; i < 10; i++) fixture.Tick();
            yield return AllocationCapture.Measure("steady-update-plus-65-depth", 1000,
                () => { for (int i = 0; i < 1000; i++) fixture.Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
        }
    }
}
