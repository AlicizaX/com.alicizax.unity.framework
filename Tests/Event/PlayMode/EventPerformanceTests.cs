#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.EventTests
{
    public sealed class EventPerformanceTests
    {
        private struct ColdEmpty<T> : IEmptyEventArgs { }
        private struct ColdPayload<T> : IPayloadEventArgs { }
        private struct NaturalEmpty : IEmptyEventArgs { }
        private struct NaturalPayload : IPayloadEventArgs { }
        private sealed class NaturalListener
        {
            public int Calls;
            public void Empty() { Calls++; }
            public void Payload(in NaturalPayload e) { Calls++; }
        }
        private static readonly Action EmptyHandler = Hit;
        private static readonly InEventHandler<ColdPayload<int>> ColdPayloadHandler = (in ColdPayload<int> e) => { };
        private static object sink;
        private static long result;
        private static void Hit() { result++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EventRuntimeHandle AddColdEmpty() => EventBus.Subscribe<ColdEmpty<int>>(EmptyHandler);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EventRuntimeHandle AddColdPayload() => EventBus.Subscribe<ColdPayload<int>>(ColdPayloadHandler);
        [StructLayout(LayoutKind.Sequential, Size = 1024)]
        private readonly struct LargePayload : IPayloadEventArgs { public readonly long First; public LargePayload(long first) { First = first; } }
        private static readonly InEventHandler<LargePayload> LargeHandler = HandleLarge;
        private static void HandleLarge(in LargePayload e) { result += e.First; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadValue(LargePayload e) { result += e.First; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadReference(in LargePayload e) { result += e.First; }

        [UnityTest]
        public IEnumerator CalibratedColdWarmGrowthAndReuseAllocations()
        {
            yield return AllocationCapture.Measure("calibration-empty", 1, () => { }, s => Assert.That(s.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("calibration-array", 1, () => sink = new byte[4096], s => Assert.That(s.Bytes, Is.GreaterThanOrEqualTo(4096)));
            EventRuntimeHandle cold = default;
            yield return AllocationCapture.Measure("first-empty-type", 1, () => cold = AddColdEmpty(), s => Assert.That(s.Bytes, Is.GreaterThan(0)), true);
            cold.Dispose();
            yield return AllocationCapture.Measure("first-payload-type", 1, () => cold = AddColdPayload(), s => Assert.That(s.Bytes, Is.GreaterThan(0)), true);
            cold.Dispose();
            foreach (bool payload in new[] { false, true })
            {
                using var f = new EventFixture(payload);
                var target = new Listener();
                Action subscribeAndDispose = payload
                    ? () => { for (int i = 0; i < 10000; i++) EventBus.Subscribe<PayloadProbe>(target.OnPayload).Dispose(); }
                    : () => { for (int i = 0; i < 10000; i++) EventBus.Subscribe<EmptyProbe>(target.OnEmpty).Dispose(); };
                Action emptyCached = target.OnEmpty; InEventHandler<PayloadProbe> payloadCached = target.OnPayload;
                Action cachedCycle = payload
                    ? () => { for (int i = 0; i < 10000; i++) EventBus.Subscribe<PayloadProbe>(payloadCached).Dispose(); }
                    : () => { for (int i = 0; i < 10000; i++) EventBus.Subscribe<EmptyProbe>(emptyCached).Dispose(); };
                cachedCycle();
                yield return AllocationCapture.Measure("cached-cycle-" + payload, 10000, cachedCycle, s => Assert.That(s.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("caller-new-delegate-" + payload, 10000, subscribeAndDispose, s => Assert.That(s.Bytes, Is.GreaterThan(0)));
                f.VerifyLedger(0);
                int capacity = f.Capacity;
                var listeners = new Listener[capacity + 1]; var handles = new EventRuntimeHandle[capacity + 1];
                var empty = new Action[capacity + 1]; var handlers = new InEventHandler<PayloadProbe>[capacity + 1];
                for (int i = 0; i < listeners.Length; i++) { listeners[i] = new Listener(); empty[i] = listeners[i].OnEmpty; handlers[i] = listeners[i].OnPayload; }
                Action add = () => { for (int i = 0; i < capacity; i++) handles[i] = payload ? EventBus.Subscribe<PayloadProbe>(handlers[i]) : EventBus.Subscribe<EmptyProbe>(empty[i]); };
                add(); foreach (var h in handles) h.Dispose();
                yield return AllocationCapture.Measure("new-listeners-within-capacity-" + payload, capacity, add, s => Assert.That(s.Bytes, Is.Zero));
                EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                try
                {
                    yield return AllocationCapture.Measure("capacity-growth-without-diagnostics-" + payload, 1,
                        () => handles[capacity] = payload ? EventBus.Subscribe<PayloadProbe>(handlers[capacity]) : EventBus.Subscribe<EmptyProbe>(empty[capacity]),
                        s => Assert.That(s.Bytes, Is.GreaterThan(0)), true);
                }
                finally { EventDebugRegistry.BenchmarkReleaseLikeMode = false; }
                yield return AllocationCapture.Measure("dispose-all-" + payload, handles.Length, () => { foreach (var h in handles) h.Dispose(); }, s => Assert.That(s.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("grown-storage-reuse-" + payload, 10000, cachedCycle, s => Assert.That(s.Bytes, Is.Zero));
                f.VerifyLedger(0);
            }
            sink = null;
        }

        [UnityTest]
        public IEnumerator StablePublishAndLargePayloadHaveNoManagedAllocation()
        {
            foreach (bool payload in new[] { false, true })
            {
                using var f = new EventFixture(payload);
                f.Reserve(1024);
                foreach (int count in new[] { 0, 1, 16, 256, 1024 })
                {
                    f.Clear();
                    for (int i = 0; i < count; i++) f.Subscribe(new Listener());
                    f.Publish();
                    yield return AllocationCapture.Measure($"publish-{payload}-{count}", 10000,
                        () => { for (int i = 0; i < 10000; i++) f.Publish(); }, s => Assert.That(s.Bytes, Is.Zero));
                    f.VerifyLedger(count);
                }
            }
            EventBus.ReservePayloadCapacity<LargePayload>(1);
            var handle = EventBus.Subscribe<LargePayload>(LargeHandler);
            var large = new LargePayload(17); EventBus.Publish(in large);
            yield return AllocationCapture.Measure("large-payload-1024-bytes", 100000,
                () => { for (int i = 0; i < 100000; i++) EventBus.Publish(in large); }, s => Assert.That(s.Bytes, Is.Zero));
            TestContext.WriteLine($"PAYLOAD_SIZE,{Marshal.SizeOf<LargePayload>()},{result}"); handle.Dispose();
        }

        [UnityTest]
        public IEnumerator RejectedDuplicatesAndExceptionsReportTheirOwnLogAllocations()
        {
            using var f = new EventFixture(false);
            var target = new Listener(); Action callback = target.OnEmpty;
            EventBus.Subscribe<EmptyProbe>(callback);
            EventRuntimeHandle duplicate = default;
            LogAssert.Expect(LogType.Warning, new Regex("Duplicate event handler subscription"));
            yield return AllocationCapture.Measure("duplicate-warning", 1, () => duplicate = EventBus.Subscribe<EmptyProbe>(callback),
                s => Assert.That(s.Bytes, Is.GreaterThan(0)), true);
            Assert.That(duplicate, Is.EqualTo(default(EventRuntimeHandle)));
            f.Subscribe(() => throw new NullReferenceException("measured-ui-fault"));
            LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException: measured-ui-fault"));
            yield return AllocationCapture.Measure("exception-and-log", 1, () => f.Publish(), s => Assert.That(s.Bytes, Is.GreaterThan(0)), true);
            Assert.That(target.Calls, Is.EqualTo(1)); f.VerifyLedger(2);
        }

        [UnityTest]
        public IEnumerator ReusedPendingStorageHasNoPerMutationAllocation()
        {
            foreach (bool payload in new[] { false, true })
            {
                using var f = new EventFixture(payload);
                var next = new Listener(); Action empty = next.OnEmpty; InEventHandler<PayloadProbe> handler = next.OnPayload;
                f.Subscribe(() =>
                {
                    var handle = payload ? EventBus.Subscribe<PayloadProbe>(handler) : EventBus.Subscribe<EmptyProbe>(empty);
                    handle.Dispose();
                });
                f.Publish();
                yield return AllocationCapture.Measure("pending-cycle-" + payload, 10000,
                    () => { for (int i = 0; i < 10000; i++) f.Publish(); }, s => Assert.That(s.Bytes, Is.Zero));
                Assert.That(next.Calls, Is.Zero); f.VerifyLedger(1);
            }
        }

        [UnityTest]
        public IEnumerator DefaultCapacityGrowsNaturallyThenReusesWithoutAllocation()
        {
            var listeners = new NaturalListener[32]; var handles = new EventRuntimeHandle[32];
            var empty = new Action[32]; var payload = new InEventHandler<NaturalPayload>[32];
            for (int i = 0; i < listeners.Length; i++)
            {
                listeners[i] = new NaturalListener(); empty[i] = listeners[i].Empty; payload[i] = listeners[i].Payload;
            }
            foreach (bool hasPayload in new[] { false, true })
            {
                Action add = () => { for (int i = 0; i < handles.Length; i++) handles[i] = hasPayload ? EventBus.Subscribe<NaturalPayload>(payload[i]) : EventBus.Subscribe<NaturalEmpty>(empty[i]); };
                Action release = () => { foreach (var handle in handles) handle.Dispose(); };
                EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                try
                {
                    yield return AllocationCapture.Measure("natural-first-and-growth-" + hasPayload, 32, add, s => Assert.That(s.Bytes, Is.GreaterThan(0)), true);
                    release();
                    yield return AllocationCapture.Measure("natural-growth-new-listeners-" + hasPayload, 32, add, s => Assert.That(s.Bytes, Is.Zero));
                    var value = new NaturalPayload();
                    yield return AllocationCapture.Measure("natural-growth-publish-" + hasPayload, 10000,
                        () => { for (int i = 0; i < 10000; i++) { if (hasPayload) EventBus.Publish(in value); else EventBus.Publish<NaturalEmpty>(); } },
                        s => Assert.That(s.Bytes, Is.Zero));
                    yield return AllocationCapture.Measure("natural-growth-dispose-" + hasPayload, 32, release, s => Assert.That(s.Bytes, Is.Zero));
                    yield return AllocationCapture.Measure("natural-growth-cycle-" + hasPayload, 3200,
                        () => { for (int i = 0; i < 100; i++) { add(); release(); } }, s => Assert.That(s.Bytes, Is.Zero));
                    foreach (var listener in listeners) Assert.That(listener.Calls, Is.EqualTo(hasPayload ? 20000 : 10000));
                    Assert.That(hasPayload ? EventBus.GetPayloadSubscriberCount<NaturalPayload>() : EventBus.GetEmptySubscriberCount<NaturalEmpty>(), Is.Zero);
                }
                finally
                {
                    release();
                    if (hasPayload) EventBus.ClearPayload<NaturalPayload>(); else EventBus.ClearEmpty<NaturalEmpty>();
                    EventDebugRegistry.BenchmarkReleaseLikeMode = false;
                }
            }
        }

        [Test]
        public void LargePayloadReferenceAndValueCopyTimingControls()
        {
            var payload = new LargePayload(7);
            const int count = 1000000;
            var handle = EventBus.Subscribe<LargePayload>(LargeHandler);
            try
            {
                EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                Action[] controls = {
                    () => { for (int i = 0; i < count; i++) ReadReference(in payload); },
                    () => { for (int i = 0; i < count; i++) ReadValue(payload); },
                    () => { for (int i = 0; i < count; i++) EventBus.Publish(in payload); }
                };
                string[] names = { "direct-in", "direct-value-copy", "event-publish-in" };
                var watch = new Stopwatch();
                for (int index = 0; index < controls.Length; index++)
                {
                    controls[index](); var times = new double[7]; long before = result;
                    for (int round = 0; round < times.Length; round++)
                    {
                        watch.Restart(); controls[index](); watch.Stop(); times[round] = watch.Elapsed.TotalMilliseconds;
                    }
                    Assert.That(result - before, Is.EqualTo(count * 7L * 7));
                    TestContext.WriteLine($"COPY_TIME,{names[index]},1024,{count},{string.Join(",", times)}");
                }
            }
            finally { handle.Dispose(); EventDebugRegistry.BenchmarkReleaseLikeMode = false; }
        }
    }
}
#endif
