using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIPerformanceTests
    {
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
