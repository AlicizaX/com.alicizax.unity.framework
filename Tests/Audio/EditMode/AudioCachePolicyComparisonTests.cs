using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioCachePolicyComparisonTests : AudioTestBase
    {
        private readonly struct Access
        {
            internal readonly int Id;
            internal readonly float Gap;
            internal Access(int id, float gap = 0) { Id = id; Gap = gap; }
        }

        private sealed class ReferenceCache
        {
            private readonly Dictionary<int, LinkedListNode<(int id, float time)>> entries;
            private readonly Stack<LinkedListNode<(int id, float time)>> free;
            private readonly int capacity;
            private readonly LinkedList<(int id, float time)> order = new LinkedList<(int, float)>();
            private readonly bool lru;
            private readonly float ttl;
            private float time;
            internal int Hits, Loads, Evictions;
            internal int Count => entries.Count;
            internal long ResidentBytes, PeakBytes;
            internal ReferenceCache(bool lru, float ttl, int capacity)
            {
                this.lru = lru; this.ttl = ttl; this.capacity = capacity;
                entries = new Dictionary<int, LinkedListNode<(int, float)>>(capacity);
                free = new Stack<LinkedListNode<(int, float)>>(capacity);
            }
            internal bool Read(Access access)
            {
                time += access.Gap;
                while (order.First != null && time - order.First.Value.time >= ttl) Evict();
                if (entries.TryGetValue(access.Id, out var node))
                {
                    Hits++;
                    if (lru)
                    {
                        order.Remove(node);
                        node.Value = (access.Id, time);
                        order.AddLast(node);
                    }
                    return true;
                }
                Loads++;
                if (entries.Count == capacity) Evict();
                node = free.Count > 0 ? free.Pop() : new LinkedListNode<(int, float)>(default);
                node.Value = (access.Id, time);
                order.AddLast(node);
                entries.Add(access.Id, node);
                ResidentBytes += ClipBytes(access.Id);
                PeakBytes = Math.Max(PeakBytes, ResidentBytes);
                return false;
            }
            private void Evict()
            {
                var node = order.First;
                ResidentBytes -= ClipBytes(node.Value.id);
                entries.Remove(node.Value.id);
                order.RemoveFirst();
                free.Push(node);
                Evictions++;
            }
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (string workload in new[] { "ui-two-clips", "battle-hot-and-scan", "dialogue-sequential", "scene-return-after-ttl" })
                foreach (int capacity in new[] { 16, 64, 256 })
                    foreach (int ttl in new[] { 1, 30, 120 })
                        yield return new TestCaseData(workload, capacity, ttl);
        }

        [TestCaseSource(nameof(Cases))]
        public void ActualCacheMatchesTtlLruModelAndReportsPolicyTradeoffs(string workload, int capacity, int ttl)
        {
            var trace = BuildTrace(workload);
            int catalog = 0;
            foreach (var access in trace) catalog = Math.Max(catalog, access.Id + 1);
            var names = new string[catalog];
            using var f = new AudioFixture(capacity, ttl: ttl);
            for (int i = 0; i < names.Length; i++) { names[i] = "policy-" + i; f.Clip(names[i], ClipBytes(i) / 32000f); }
            var ttlLru = new ReferenceCache(true, ttl, capacity);
            var lru = new ReferenceCache(true, float.MaxValue, capacity);
            var fifo = new ReferenceCache(false, float.MaxValue, capacity);
            int hits = 0, evictions = 0;
            foreach (var access in trace)
            {
                int beforeCount = f.Debug.ClipCacheCount;
                if (access.Gap > 0)
                {
                    for (var entry = f.Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext) entry.LastUseTime -= access.Gap;
                    f.Tick();
                }
                int loads = f.Loader.Loads;
                Assert.That(f.Audio.Preload(names[access.Id], AudioCachePolicy.Ttl), Is.True);
                bool hit = f.Loader.Loads == loads;
                if (hit) hits++;
                evictions += beforeCount + (hit ? 0 : 1) - f.Debug.ClipCacheCount;
                ttlLru.Read(access); lru.Read(access); fifo.Read(access);
            }
            Assert.That(hits, Is.EqualTo(ttlLru.Hits));
            Assert.That(f.Loader.Loads, Is.EqualTo(ttlLru.Loads));
            Assert.That(evictions, Is.EqualTo(ttlLru.Evictions));
            f.CheckCache();
            TestContext.WriteLine("POLICY_HEADER,workload,policy,accesses,hits,loads,evictions,retained_entries");
            TestContext.WriteLine($"POLICY,{workload},actual-ttl-lru,{trace.Count},{hits},{f.Loader.Loads},{evictions},{f.Debug.ClipCacheCount}");
            TestContext.WriteLine($"POLICY,{workload},lru,{trace.Count},{lru.Hits},{lru.Loads},{lru.Evictions},{lru.Count}");
            TestContext.WriteLine($"POLICY,{workload},fifo,{trace.Count},{fifo.Hits},{fifo.Loads},{fifo.Evictions},{fifo.Count}");
            long retained = 0;
            for (var entry = f.Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext)
                retained += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(entry.Clip);
            TestContext.WriteLine($"POLICY_RESIDENCY,{workload},{capacity},{ttl},{retained},{ttlLru.ResidentBytes},{lru.ResidentBytes},{fifo.ResidentBytes}");
            MeasurePolicy(workload, trace, capacity, ttl, true, "ttl-lru");
            MeasurePolicy(workload, trace, capacity, float.MaxValue, true, "lru");
            MeasurePolicy(workload, trace, capacity, float.MaxValue, false, "fifo");
            if (workload == "scene-return-after-ttl" && capacity >= 64 && ttl <= 60) Assert.That(lru.Hits, Is.GreaterThan(hits));
            for (var entry = f.Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext) entry.LastUseTime -= ttl + 1;
            f.Tick();
            TestContext.WriteLine($"POLICY_AFTER_IDLE,{workload},actual-ttl-lru,{f.Debug.ClipCacheCount},lru,{lru.Count},fifo,{fifo.Count}");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        private static long ClipBytes(int id) => 256L << (id % 7);

        private static void MeasurePolicy(string workload, List<Access> trace, int capacity, float ttl, bool lru, string policy)
        {
            var milliseconds = new double[7];
            var latency = new double[trace.Count];
            int loads = 0;
            long peak = 0;
            for (int round = 0; round < milliseconds.Length; round++)
            {
                var cache = new ReferenceCache(lru, ttl, capacity);
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < trace.Count; i++)
                {
                    bool hit = cache.Read(trace[i]);
                    // A stated model, not a network measurement: 2 ms setup plus 8 MiB/s transfer.
                    latency[i] = hit ? 0 : 2 + ClipBytes(trace[i].Id) * 1000.0 / (8 * 1024 * 1024);
                }
                long elapsed = Stopwatch.GetTimestamp() - start;
                milliseconds[round] = elapsed * 1000.0 / Stopwatch.Frequency;
                loads = cache.Loads;
                peak = cache.PeakBytes;
            }
            Array.Sort(milliseconds);
            Array.Sort(latency);
            TestContext.WriteLine($"POLICY_COST,{workload},{policy},{capacity},{ttl},{trace.Count},{milliseconds[3]:F6},{milliseconds[6]:F6},{loads},{peak},{latency[(latency.Length - 1) * 95 / 100]:F6},{latency[(latency.Length - 1) * 99 / 100]:F6}");
        }

        [UnityTest]
        public IEnumerator PolicyConstructionGrowthHitsAndEvictionUseCalibratedAllocationMeasurements()
        {
            foreach (int capacity in new[] { 16, 256, 4096 })
            foreach (string policy in new[] { "ttl-lru", "lru", "fifo" })
            {
                ReferenceCache cache = null;
                yield return AllocationCapture.Measure($"policy-cold-{policy}-{capacity}", capacity, () =>
                {
                    cache = new ReferenceCache(policy != "fifo", policy == "ttl-lru" ? 30 : float.MaxValue, capacity);
                    for (int i = 0; i < capacity; i++) cache.Read(new Access(i));
                }, sample => Assert.That(sample.Bytes, Is.InRange(1, 4096 + capacity * 192)));
                yield return AllocationCapture.Measure($"policy-hit-{policy}-{capacity}", 10000, () =>
                {
                    for (int i = 0; i < 10000; i++) cache.Read(new Access(i % capacity));
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(cache.Hits, Is.EqualTo(10000));
                yield return AllocationCapture.Measure($"policy-evict-{policy}-{capacity}", 10000, () =>
                {
                    for (int i = 0; i < 10000; i++) cache.Read(new Access(capacity + i));
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(cache.Loads, Is.EqualTo(capacity + 10000));
                Assert.That(cache.Count, Is.EqualTo(capacity));
                Assert.That(cache.Evictions, Is.EqualTo(10000));
            }
        }

        private static List<Access> BuildTrace(string workload)
        {
            var trace = new List<Access>();
            switch (workload)
            {
                case "ui-two-clips":
                    for (int i = 0; i < 10000; i++) trace.Add(new Access(i % 2));
                    break;
                case "battle-hot-and-scan":
                    for (int i = 0; i < 10000; i++) trace.Add(new Access(i % 5 == 0 ? 16 + (i / 5) % 240 : i % 16));
                    break;
                case "dialogue-sequential":
                    for (int i = 0; i < 4096; i++) trace.Add(new Access(i));
                    break;
                case "scene-return-after-ttl":
                    for (int wave = 0; wave < 8; wave++)
                        for (int i = 0; i < 64; i++) trace.Add(new Access(i, i == 0 ? 60 : 0));
                    break;
            }
            return trace;
        }
    }
}
