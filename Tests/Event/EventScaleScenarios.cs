#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;

namespace AlicizaX.EventTests
{
    public static class EventScaleScenarios
    {
        private struct Empty : IEmptyEventArgs { }
        private readonly struct Payload : IPayloadEventArgs
        {
            public readonly int Value;
            public Payload(int value) { Value = value; }
        }
        private sealed class Listener
        {
            public long Calls;
            public long Sum;
            public readonly Action EmptyHandler;
            public readonly InEventHandler<Payload> PayloadHandler;
            public Listener() { EmptyHandler = Empty; PayloadHandler = Payload; }
            private void Empty() { Calls++; }
            private void Payload(in Payload value) { Calls++; Sum += value.Value; }
        }
        private sealed class Publisher
        {
            public readonly int Value;
            public Publisher(int value) { Value = value; }
            public void Empty() => EventBus.Publish<Empty>();
            public void Payload() { var value = new Payload(Value); EventBus.Publish(in value); }
        }

        private sealed class Population : IDisposable
        {
            public readonly bool Payload;
            public readonly Listener[] Listeners;
            public readonly EventRuntimeHandle[] Handles;
            private readonly Type container;
            private bool[] seen = Array.Empty<bool>();
            public Population(bool payload, int count)
            {
                Payload = payload;
                container = payload ? typeof(EventContainer<Payload, InEventHandler<Payload>>) : typeof(EventContainer<Empty, Action>);
                Clear();
                Listeners = new Listener[count]; Handles = new EventRuntimeHandle[count];
                for (int i = 0; i < count; i++) Listeners[i] = new Listener();
            }
            public int Count => Payload ? EventBus.GetPayloadSubscriberCount<Payload>() : EventBus.GetEmptySubscriberCount<Empty>();
            public int Capacity => ((Array)Field("_callbacks")).Length;
            public int PendingCapacity => ((Array)Field("_pending")).Length;
            public object Field(string name) => container.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            public EventRuntimeHandle Subscribe(int index) => Payload ? EventBus.Subscribe<Payload>(Listeners[index].PayloadHandler) : EventBus.Subscribe<Empty>(Listeners[index].EmptyHandler);
            public void AddAll() { for (int i = 0; i < Handles.Length; i++) Handles[i] = Subscribe(i); }
            public void RemoveAll() { for (int i = 0; i < Handles.Length; i++) Handles[i].Dispose(); }
            public void Publish(int value = 1)
            {
                if (Payload) { var e = new Payload(value); EventBus.Publish(in e); }
                else EventBus.Publish<Empty>();
            }
            public void Clear() { if (Payload) EventBus.ClearPayload<Payload>(); else EventBus.ClearEmpty<Empty>(); }
            public void ResetCalls() { foreach (var listener in Listeners) { listener.Calls = 0; listener.Sum = 0; } }
            public void CheckCalls(long calls, long sum)
            {
                for (int i = 0; i < Listeners.Length; i++)
                    if (Listeners[i].Calls != calls || Listeners[i].Sum != (Payload ? sum : 0))
                        Assert.Fail($"listener {i}: calls {Listeners[i].Calls}/{calls}, sum {Listeners[i].Sum}/{sum}");
            }
            public void Ledger(int expected)
            {
                Assert.That(Count, Is.EqualTo(expected));
                Assert.That((int)Field("_publishDepth"), Is.Zero);
                Assert.That((int)Field("_pendingCount"), Is.Zero);
                var callbacks = (Delegate[])Field("_callbacks"); var packedCallbacks = (Delegate[])Field("_packedCallbacks");
                var versions = (int[])Field("_versions"); var cancelled = (bool[])Field("_cancelled");
                var indices = (int[])Field("_packedIndices"); var packed = (int[])Field("_packedSlots");
                var free = (int[])Field("_freeSlots"); int freeCount = (int)Field("_freeCount");
                if (seen.Length != callbacks.Length) seen = new bool[callbacks.Length];
                else Array.Clear(seen, 0, seen.Length);
                Assert.That(expected + freeCount, Is.EqualTo(callbacks.Length));
                for (int i = 0; i < expected; i++)
                {
                    int slot = packed[i];
                    if ((uint)slot >= (uint)seen.Length || seen[slot] || indices[slot] != i || versions[slot] == 0 || cancelled[slot] || callbacks[slot] == null || !ReferenceEquals(callbacks[slot], packedCallbacks[i]))
                        Assert.Fail($"packed invariant at {i}");
                    seen[slot] = true;
                }
                for (int i = 0; i < freeCount; i++)
                {
                    int slot = free[i];
                    if ((uint)slot >= (uint)seen.Length || seen[slot] || versions[slot] != 0 || callbacks[slot] != null || cancelled[slot])
                        Assert.Fail($"free invariant at {i}");
                    seen[slot] = true;
                }
                for (int i = expected; i < packedCallbacks.Length; i++)
                    if (packedCallbacks[i] != null) Assert.Fail($"retained packed delegate at {i}");
                Assert.That(((IDictionary)Field("Subscriptions")).Count, Is.EqualTo(expected));
            }
            public void Dispose() { Clear(); Ledger(0); }
        }

        private sealed class PendingDriver
        {
            private readonly Population population;
            public EventRuntimeHandle Handle;
            public int Peak;
            private readonly Action empty;
            private readonly InEventHandler<Payload> payload;
            public PendingDriver(Population population) { this.population = population; empty = Mutate; payload = OnPayload; }
            public void Bind() => Handle = population.Payload ? EventBus.Subscribe<Payload>(payload) : EventBus.Subscribe<Empty>(empty);
            private void OnPayload(in Payload value) => Mutate();
            private void Mutate()
            {
                Handle.Dispose();
                population.AddAll();
                for (int i = 0; i < population.Handles.Length; i += 2) population.Handles[i].Dispose();
                Peak = 1 + population.Handles.Length + (population.Handles.Length + 1) / 2;
                if (population.Count != 1) Assert.Fail("pending changed committed count");
                population.Publish();
            }
        }

        private static object calibrationSink;
        private static IEnumerator Calibrate()
        {
            yield return AllocationCapture.Measure("scale-calibration-empty", 1, () => { }, s => Assert.That(s.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("scale-calibration-array", 1, () => calibrationSink = new byte[4096], s => Assert.That(s.Bytes, Is.EqualTo(4128)));
            calibrationSink = null;
        }

        public static IEnumerator CallerAllocationControls()
        {
            yield return Calibrate();
            var sources = new Publisher[1024];
            var listeners = new Listener[1024];
            yield return AllocationCapture.Measure("scale-caller-publisher-objects", sources.Length,
                () => { for (int i = 0; i < sources.Length; i++) sources[i] = new Publisher(i); },
                sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            yield return AllocationCapture.Measure("scale-caller-listeners-and-cached-delegates", listeners.Length,
                () => { for (int i = 0; i < listeners.Length; i++) listeners[i] = new Listener(); },
                sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            foreach (int count in new[] { 100000, 300000 })
            {
                yield return AllocationCapture.Measure("scale-caller-publisher-array-" + count, 1,
                    () => calibrationSink = new Publisher[count], sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
                calibrationSink = null;
                yield return AllocationCapture.Measure("scale-caller-listener-array-" + count, 1,
                    () => calibrationSink = new Listener[count], sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
                calibrationSink = null;
                yield return AllocationCapture.Measure("scale-caller-handle-array-" + count, 1,
                    () => calibrationSink = new EventRuntimeHandle[count], sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
                calibrationSink = null;
            }
        }

        private static IEnumerator Rounds(string name, int operations, Action prepare, Action action, Action verify)
        {
            var times = new double[5];
            int collections = 0;
            for (int round = 0; round < times.Length; round++)
            {
                prepare?.Invoke();
                int index = round;
                yield return AllocationCapture.Measure(name + "-round-" + round, operations, action, sample =>
                {
                    times[index] = sample.Milliseconds;
                    collections += sample.Gen0Collections + sample.Gen1Collections + sample.Gen2Collections;
                    Assert.That(sample.Bytes, Is.Zero, name);
                    Assert.That(sample.Allocations, Is.Zero, name);
                });
                verify();
            }
            TestContext.WriteLine($"SCALE_ROUNDS,{name},{operations},{string.Join(",", Array.ConvertAll(times, t => t.ToString("F4", CultureInfo.InvariantCulture)))}");
            Array.Sort(times);
            TestContext.WriteLine($"SCALE_SUMMARY,{name},{operations},{times[2]:F4},{times[0]:F4},{times[4]:F4},0,{collections}");
        }

        public static IEnumerator PublisherSources(int count)
        {
            yield return Calibrate();
            var sources = new Publisher[count];
            for (int i = 0; i < count; i++) sources[i] = new Publisher(i + 1);
            TestContext.WriteLine($"SCALE_SETUP,publishers,{count},outside-framework-sample");
            bool previous = EventDebugRegistry.BenchmarkReleaseLikeMode;
            try
            {
                foreach (bool payload in new[] { false, true })
                foreach (int listeners in new[] { 0, 1, 8, 32 })
                {
                    using var p = new Population(payload, listeners);
                    EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                    p.AddAll(); p.Publish(); p.ResetCalls();
                    Action publish = payload
                        ? () => { for (int i = 0; i < sources.Length; i++) sources[i].Payload(); }
                        : () => { for (int i = 0; i < sources.Length; i++) sources[i].Empty(); };
                    foreach (bool diagnostics in new[] { false, true })
                    {
                        EventDebugRegistry.BenchmarkReleaseLikeMode = !diagnostics;
                        string name = $"sources-{count}-payload-{payload}-listeners-{listeners}-diagnostics-{diagnostics}";
                        TestContext.WriteLine($"SCALE_WORK,{name},publishers,{count},publishes,{count},callbacks,{(long)count * listeners}");
                        yield return Rounds(name, count, p.ResetCalls, publish, () =>
                        {
                            p.CheckCalls(count, (long)count * (count + 1) / 2); p.Ledger(listeners);
                        });
                    }
                }
            }
            finally { EventDebugRegistry.BenchmarkReleaseLikeMode = previous; }
        }

        public static IEnumerator LiveSubscriptions(int count)
        {
            yield return Calibrate();
            bool previous = EventDebugRegistry.BenchmarkReleaseLikeMode;
            try
            {
                foreach (bool payload in new[] { false, true })
                {
                    EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                    using var p = new Population(payload, count);
                    TestContext.WriteLine($"SCALE_SETUP,listeners-{payload},{count},outside-framework-sample");
                    int before = p.Capacity;
                    yield return AllocationCapture.Measure($"growth-{count}-payload-{payload}-from-{before}", count, p.AddAll,
                        sample => { if (before < count) Assert.That(sample.Bytes, Is.GreaterThan(0)); });
                    p.Ledger(count); p.RemoveAll(); p.Ledger(0);
                    TestContext.WriteLine($"SCALE_STORAGE,live-{count}-payload-{payload},{before},{p.Capacity},{p.PendingCapacity}");
                    foreach (bool diagnostics in new[] { false, true })
                    {
                        EventDebugRegistry.BenchmarkReleaseLikeMode = !diagnostics;
                        string suffix = $"-{count}-payload-{payload}-diagnostics-{diagnostics}";
                        yield return Rounds("subscribe" + suffix, count, null, p.AddAll, () => { p.Ledger(count); p.RemoveAll(); });
                        yield return Rounds("dispose" + suffix, count, p.AddAll, p.RemoveAll, () => p.Ledger(0));
                        p.AddAll();
                        TestContext.WriteLine($"SCALE_WORK,fanout{suffix},publishes,32,callbacks,{32L * count}");
                        yield return Rounds("fanout" + suffix, 32, p.ResetCalls,
                            () => { for (int i = 0; i < 32; i++) p.Publish(7); },
                            () => { p.CheckCalls(32, 224); p.Ledger(count); });
                        p.RemoveAll(); p.Ledger(0);
                    }
                }
            }
            finally { EventDebugRegistry.BenchmarkReleaseLikeMode = previous; }
        }

        public static IEnumerator InterleavedOperations(int count)
        {
            yield return Calibrate();
            bool previous = EventDebugRegistry.BenchmarkReleaseLikeMode;
            try
            {
                foreach (bool payload in new[] { false, true })
                {
                    using var p = new Population(payload, 32);
                    EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                    p.AddAll();
                    Action mixed = () =>
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int slot = i & 31;
                            var old = p.Handles[slot]; old.Dispose(); p.Handles[slot] = p.Subscribe(slot); old.Dispose();
                            p.Publish(i + 1);
                        }
                    };
                    foreach (bool diagnostics in new[] { false, true })
                    {
                        EventDebugRegistry.BenchmarkReleaseLikeMode = !diagnostics;
                        string name = $"mixed-{count}-payload-{payload}-diagnostics-{diagnostics}";
                        TestContext.WriteLine($"SCALE_WORK,{name},publishes,{count},subscribes,{count},dispose,{2L * count},callbacks,{32L * count}");
                        yield return Rounds(name, count, p.ResetCalls, mixed, () => { p.CheckCalls(count, (long)count * (count + 1) / 2); p.Ledger(32); });
                    }
                }
            }
            finally { EventDebugRegistry.BenchmarkReleaseLikeMode = previous; }
        }

        public static IEnumerator PendingMutations(int count)
        {
            yield return Calibrate();
            bool previous = EventDebugRegistry.BenchmarkReleaseLikeMode;
            try
            {
                foreach (bool payload in new[] { false, true })
                {
                    EventDebugRegistry.BenchmarkReleaseLikeMode = true;
                    using var p = new Population(payload, count);
                    var driver = new PendingDriver(p);
                    Action prepare = () => { p.Clear(); p.ResetCalls(); driver.Bind(); };
                    Action publish = () => p.Publish();
                    Action verify = () =>
                    {
                        p.CheckCalls(0, 0); p.Ledger(count / 2);
                        p.Publish(7);
                        for (int i = 0; i < count; i++)
                            if (p.Listeners[i].Calls != (i % 2 == 0 ? 0 : 1) || p.Listeners[i].Sum != (payload && i % 2 != 0 ? 7 : 0))
                                Assert.Fail($"pending listener {i}");
                        p.Clear(); p.Ledger(0);
                    };
                    prepare();
                    int pendingBefore = p.PendingCapacity;
                    yield return AllocationCapture.Measure($"pending-growth-{count}-payload-{payload}", count, publish,
                        sample => { if (pendingBefore < driver.Peak) Assert.That(sample.Bytes, Is.GreaterThan(0)); });
                    verify();
                    TestContext.WriteLine($"SCALE_STORAGE,pending-{count}-payload-{payload},slots,{p.Capacity},queue,{p.PendingCapacity},expected-peak,{driver.Peak}");
                    foreach (bool diagnostics in new[] { false, true })
                    {
                        EventDebugRegistry.BenchmarkReleaseLikeMode = !diagnostics;
                        string name = $"pending-{count}-payload-{payload}-diagnostics-{diagnostics}";
                        yield return Rounds(name, driver.Peak, prepare, publish, verify);
                        if (diagnostics)
                        {
                            EventDebugRegistry.TryGetDetails(payload ? typeof(Payload) : typeof(Empty), out var summary, out _);
                            Assert.That(summary.PeakPendingCount, Is.GreaterThanOrEqualTo(driver.Peak));
                            TestContext.WriteLine($"SCALE_PENDING_OBSERVED,{name},lifetime-peak,{summary.PeakPendingCount},expected-operation-peak,{driver.Peak}");
                        }
                    }
                }
            }
            finally { EventDebugRegistry.BenchmarkReleaseLikeMode = previous; }
        }
    }
}
#endif
