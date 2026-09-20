#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace AlicizaX.Timer.Tests
{
    public sealed class TimerPerformanceTests
    {
        private sealed class ColdArg { }
        private static readonly Action<ColdArg> ColdCallback = ColdCall;
        private static readonly Action<TimerArg> CachedMethodGroup = StaticGenericCallback;
        private static void ColdCall(ColdArg arg) { }
        private static void StaticGenericCallback(TimerArg arg) { }
        private static object allocationSink;
        private static double timeSink;
        private static ulong handleSink;

        [UnityTest]
        public IEnumerator ProfilerCalibrationSeparatesHarnessAndUnityTime()
        {
            yield return AllocationCapture.Measure("empty-harness", 1, () => { }, s => Assert.That(s.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("known-4096-byte-array", 1, () => allocationSink = new byte[4096],
                s => Assert.That(s.Bytes, Is.GreaterThanOrEqualTo(4096)), true);
            yield return AllocationCapture.Measure("unity-time-only", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) timeSink = Time.timeAsDouble + Time.unscaledTimeAsDouble;
            }, s => Assert.That(s.Bytes, Is.Zero));
            allocationSink = null;
        }

        [UnityTest]
        public IEnumerator ColdConstructionFirstCallNewGenericAndDemandGrowthAreSeparated()
        {
            TimerService service = null;
            yield return AllocationCapture.Measure("new-service-256", 1, () => service = new TimerService(256),
                s => Assert.That(s.Bytes, Is.InRange(140000, 250000)), true);
            ulong handle = 0;
            yield return AllocationCapture.Measure("first-no-args-add-remove", 1, () =>
            {
                handle = service.AddTimer(TimerProbe.NoOp, 60);
                service.RemoveTimer(handle);
            }, s => Assert.That(s.Bytes, Is.Zero), true);
            var cold = new ColdArg();
            yield return AllocationCapture.Measure("first-new-generic-type", 1, () => ColdAddRemove(service, cold),
                s => Assert.That(s.ServiceBytes, Is.EqualTo(128)), true);
            yield return AllocationCapture.Measure("same-generic-type-10000", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) ColdAddRemove(service, cold);
            }, s => Assert.That(s.Bytes, Is.Zero));
            var handles = new ulong[4096];
            for (int i = 0; i < 256; i++) handles[i] = service.AddTimer(TimerProbe.NoOp, 60);
            yield return AllocationCapture.Measure("single-new-page", 1, () => handles[256] = service.AddTimer(TimerProbe.NoOp, 60),
                s => Assert.That(s.Bytes, Is.EqualTo(28968)), true);
            yield return AllocationCapture.Measure("continued-growth-to-4096", 3839, () =>
            {
                for (int i = 257; i < handles.Length; i++) handles[i] = service.AddTimer(TimerProbe.NoOp, 60);
            }, s => Assert.That(s.Bytes, Is.EqualTo(14 * 28968)));
            TimerProbe.Statistics(service, 4096, 4096, capacity: 4096);
            foreach (ulong h in handles) service.RemoveTimer(h);
            yield return AllocationCapture.Measure("demand-grown-reuse-10000", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) service.RemoveTimer(service.AddTimer(TimerProbe.NoOp, 60));
            }, s => Assert.That(s.Bytes, Is.Zero));
            TimerProbe.Ledger(service);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ColdAddRemove(TimerService service, ColdArg arg)
        {
            Profiler.BeginSample("TimerAudit.Service");
            try { service.RemoveTimer(service.AddTimer(ColdCallback, arg, 60)); }
            finally { Profiler.EndSample(); }
        }

        [UnityTest]
        public IEnumerator UncachedStaticMethodGroupAllocatesAtTheCallerOnThisCompiler()
        {
            using var f = new TimerFixture();
            var arg = new TimerArg();
            f.Service.RemoveTimer(f.Service.AddTimer(CachedMethodGroup, arg, 60));
            yield return AllocationCapture.Measure("caller-static-method-groups-1000", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                    f.Service.RemoveTimer(f.Service.AddTimer(StaticGenericCallback, arg, 60));
            }, s => Assert.That(s.Bytes, Is.EqualTo(128000)));
            yield return AllocationCapture.Measure("caller-cached-static-delegate-1000", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                    f.Service.RemoveTimer(f.Service.AddTimer(CachedMethodGroup, arg, 60));
            }, s => Assert.That(s.Bytes, Is.Zero));
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator CapturingLambdaAllocationsBelongToCaller()
        {
            using var f = new TimerFixture();
            var arg = new TimerArg();
            yield return AllocationCapture.Measure("caller-captures-1000", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    int captured = i;
                    f.Service.RemoveTimer(f.Service.AddTimer(() => arg.Value = captured, 60));
                }
            }, s => Assert.That(s.Bytes, Is.EqualTo(160000)));
            yield return AllocationCapture.Measure("cached-generic-1000", 1000, () =>
            {
                for (int i = 0; i < 1000; i++) f.Service.RemoveTimer(f.Service.AddTimer(TimerProbe.CountArg, arg, 60));
            }, s => Assert.That(s.Bytes, Is.Zero));
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator ScaledWorkloadsMeasureControlFireLoopAndDebugAtEverySize()
        {
            foreach (int count in new[] { 1000, 4096, 10000 })
            {
                using var f = new TimerFixture(count);
                var handles = new ulong[count];
                var infos = new TimerDebugInfo[count];
                var arg = new TimerArg();
                var debug = (ITimerDebugService)f.Service;
                for (int i = 0; i < count; i++) handles[i] = f.Service.AddTimer(TimerProbe.CountArg, arg, 60, true, true);
                yield return AllocationCapture.Measure("control-" + count, count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        ulong h = handles[i];
                        f.Service.Stop(h); f.Service.Resume(h); f.Service.Restart(h);
                        timeSink = f.Service.GetLeftTime(h);
                        if (f.Service.IsRunning(h)) handleSink = h;
                        f.Service.RemoveTimer(h);
                        handles[i] = f.Service.AddTimer(TimerProbe.CountArg, arg, 60, true, true);
                    }
                }, s => Assert.That(s.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("debug-copy-" + count, count, () => debug.GetAllTimers(infos),
                    s => Assert.That(s.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("idle-ticks-" + count, 10000, () =>
                {
                    for (int i = 0; i < 10000; i++) f.Tick();
                }, s => Assert.That(s.Bytes, Is.Zero));
                foreach (ulong h in handles) f.Service.RemoveTimer(h);
                TimerProbe.Calls = 0;
                for (int i = 0; i < count; i++) handles[i] = f.Service.AddTimer(TimerProbe.Counter, 0.001f, isUnscaled: true);
                yield return TimerProbe.Wait(0.02);
                yield return AllocationCapture.Measure("one-shot-burst-" + count, count, () =>
                {
                    for (int i = 0; i < 64; i++) f.Tick();
                }, s => Assert.That(s.Bytes, Is.Zero));
                Assert.That(TimerProbe.Calls, Is.EqualTo(count));
                TimerProbe.Statistics(f.Service, 0, 0);
                TimerProbe.Calls = 0;
                for (int i = 0; i < count; i++) handles[i] = f.Service.AddTimer(TimerProbe.Counter, 0.001f, true, true);
                for (int round = 0; round < 3; round++)
                {
                    yield return TimerProbe.Wait(0.02);
                    yield return AllocationCapture.Measure("loop-burst-" + count + "-" + round, count, () =>
                    {
                        for (int i = 0; i < 64; i++) f.Tick();
                    }, s => Assert.That(s.Bytes, Is.Zero));
                    Assert.That(TimerProbe.Calls, Is.EqualTo(count * (round + 1)));
                    TimerProbe.Statistics(f.Service, count, count);
                }
                yield return AllocationCapture.Measure("cancel-all-" + count, count, () =>
                {
                    foreach (ulong h in handles) f.Service.RemoveTimer(h);
                }, s => Assert.That(s.Bytes, Is.Zero));
                TimerProbe.Ledger(f.Service);
                debug.GetStatistics(out int active, out int capacity, out int peak, out int free);
                TestContext.WriteLine($"SCALE,{count},{active},{free},{capacity},{peak},{TimerProbe.Queued(f.Service)},{Profiler.GetMonoUsedSizeLong()}");
            }
            TestContext.WriteLine($"SIZE,TimerDebugInfo,{Marshal.SizeOf<TimerDebugInfo>()},ulong,{sizeof(ulong)}");
        }

        [UnityTest]
        public IEnumerator RepeatedNewServicesHaveBoundedStorageAndReleaseAllHandlers()
        {
            var services = new TimerService[32];
            yield return AllocationCapture.Measure("32-new-services", 32, () =>
            {
                for (int i = 0; i < services.Length; i++) services[i] = new TimerService(256);
            }, s => Assert.That(s.Bytes, Is.InRange(32 * 140000, 32 * 180000)));
            for (int i = 0; i < services.Length; i++)
            {
                var world = new ServiceWorld();
                world.App.Register<ITimerService>(services[i]);
                services[i].AddTimer(TimerProbe.CountArg, new TimerArg(), 30, true);
                world.Dispose();
                TimerProbe.Statistics(services[i], 0, 0);
                TimerProbe.Ledger(services[i]);
            }
            var weak = new WeakReference[services.Length];
            for (int i = 0; i < services.Length; i++) weak[i] = new WeakReference(services[i]);
            Array.Clear(services, 0, services.Length);
            yield return null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int retained = 0;
            foreach (var reference in weak) if (reference.IsAlive) retained++;
            Assert.That(retained, Is.Zero);
            TestContext.WriteLine($"RECLAIM,services,32,retained,{retained},mono-used,{Profiler.GetMonoUsedSizeLong()}");
        }

        [Test]
        public void CpuBatchMedians()
        {
            bool enabled = ProfilerDriver.enabled;
            ProfilerDriver.enabled = false;
            try
            {
                using var f = new TimerFixture(10240);
                var arg = new TimerArg();
                var debug = (ITimerDebugService)f.Service;
                var infos = new TimerDebugInfo[10000];
                MeasureCpu("10000-add-remove", () =>
                {
                    for (int i = 0; i < 10000; i++) f.Service.RemoveTimer(f.Service.AddTimer(TimerProbe.NoOp, 60));
                });
                MeasureCpu("10000-generic-control", () =>
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        ulong h = f.Service.AddTimer(TimerProbe.CountArg, arg, 60);
                        f.Service.Stop(h); f.Service.Resume(h); f.Service.Restart(h);
                        timeSink = f.Service.GetLeftTime(h);
                        if (f.Service.IsRunning(h)) handleSink = h;
                        f.Service.RemoveTimer(h);
                    }
                });
                for (int i = 0; i < 10000; i++) f.Service.AddTimer(TimerProbe.NoOp, 60);
                f.Tick();
                MeasureCpu("10000-idle-ticks-10000-resident", () => { for (int i = 0; i < 10000; i++) f.Tick(); });
                MeasureCpu("100-debug-copy-10000", () => { for (int i = 0; i < 100; i++) debug.GetAllTimers(infos); });
                MeasureCpu("10000-time-only", () => { for (int i = 0; i < 10000; i++) timeSink = Time.timeAsDouble + Time.unscaledTimeAsDouble; });
                TimerProbe.Ledger(f.Service);
            }
            finally { ProfilerDriver.enabled = enabled; }
        }

        private static void MeasureCpu(string name, Action action)
        {
            action();
            var samples = new double[21];
            var watch = new Stopwatch();
            for (int i = 0; i < samples.Length; i++)
            {
                watch.Restart(); action(); watch.Stop(); samples[i] = watch.Elapsed.TotalMilliseconds;
            }
            Array.Sort(samples);
            TestContext.WriteLine($"CPU,{name},median-ms,{samples[10]:F4},p95-ms,{samples[19]:F4}");
        }
    }
}
#endif
