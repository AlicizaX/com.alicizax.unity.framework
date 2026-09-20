using System;
using System.Diagnostics;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class CachePolicyComparisonTests : PoolFixture
    {
        [TestCase("ui-reopen", 32)]
        [TestCase("audio-hotset", 32)]
        [TestCase("scene-scan", 32)]
        [TestCase("hotset-plus-scan", 32)]
        [TestCase("working-set-at-capacity", 32)]
        [TestCase("scene-transition", 32)]
        [TestCase("audio-hotset", 8)]
        [TestCase("audio-hotset", 128)]
        public void CompareProductionCacheWithLruFifoAndSegmentedLru(string workload, int capacity)
        {
            int[] trace = BuildTrace(workload, 4000);
            var lru = new TraceCache(capacity, true);
            var fifo = new TraceCache(capacity, false);
            var probation = new TraceCache(Math.Max(1, capacity / 4), true);
            var protectedCache = new TraceCache(capacity - Math.Max(1, capacity / 4), true);
            int lruHits = 0, fifoHits = 0, slruHits = 0;
            foreach (int key in trace)
            {
                if (lru.Access(key)) lruHits++;
                if (fifo.Access(key)) fifoHits++;
                if (protectedCache.Contains(key))
                {
                    slruHits++;
                    protectedCache.Access(key);
                }
                else if (probation.Contains(key))
                {
                    slruHits++;
                    probation.Remove(key);
                    protectedCache.Access(key);
                    if (protectedCache.Evicted >= 0) probation.Access(protectedCache.Evicted);
                }
                else probation.Access(key);
            }

            using var f = new ResourceFixture();
            f.Service.IdleAssetCapacity = capacity;
            f.Service.IdleAssetExpireTime = 3600;
            var names = new string[256];
            for (int i = 0; i < names.Length; i++) { names[i] = "trace-" + i; f.Text(names[i]); }
            var timer = Stopwatch.StartNew();
            int largestReloadBatch = 0, batchReloads = 0;
            for (int i = 0; i < trace.Length; i++)
            {
                int loads = f.Loader.Loads;
                f.Service.LoadLease<TextAsset>(names[trace[i]]).Dispose();
                batchReloads += f.Loader.Loads - loads;
                if ((i & 63) == 63)
                {
                    largestReloadBatch = Math.Max(largestReloadBatch, batchReloads);
                    batchReloads = 0;
                }
            }
            timer.Stop();
            largestReloadBatch = Math.Max(largestReloadBatch, batchReloads);
            Assert.That(trace.Length - f.Loader.Loads, Is.EqualTo(lruHits), "Immediate acquire/release workloads should match LRU order exactly.");
            Assert.That(f.Loader.LiveHandles, Is.LessThanOrEqualTo(capacity));
            long coveredAssetBytes = 0;
            for (int i = 0; i < names.Length; i++)
                if (f.TryFindInfo(names[i], out var info) && info.HandleValid) coveredAssetBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(f.Loader.Assets[names[i]]);
            TestContext.WriteLine($"CACHE,{workload},{capacity},{trace.Length},{lruHits},{fifoHits},{slruHits},{f.Loader.LiveHandles},{coveredAssetBytes},{largestReloadBatch},{timer.Elapsed.TotalMilliseconds:F3}");
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        private static int[] BuildTrace(string workload, int count)
        {
            var trace = new int[count];
            var random = new System.Random(20260910);
            for (int i = 0; i < count; i++)
                trace[i] = workload switch
                {
                    "ui-reopen" => i % 8,
                    "audio-hotset" => random.Next(100) < 90 ? random.Next(8) : 8 + random.Next(248),
                    "scene-scan" => i % 256,
                    "hotset-plus-scan" => i % 512 < 320 ? i % 8 : 8 + i % 192,
                    "working-set-at-capacity" => i % 32,
                    _ => ((i / 500) % 8) * 32 + random.Next(16)
                };
            return trace;
        }

        private sealed class TraceCache
        {
            private readonly int capacity;
            private readonly bool refresh;
            private readonly int[] previous = new int[256];
            private readonly int[] next = new int[256];
            private readonly bool[] present = new bool[256];
            private int head = -1, tail = -1, count;
            internal int Evicted;

            internal TraceCache(int capacity, bool refresh) { this.capacity = capacity; this.refresh = refresh; }
            internal bool Contains(int key) => present[key];
            internal bool Access(int key)
            {
                Evicted = -1;
                bool hit = present[key];
                if (hit && !refresh) return true;
                if (hit) Remove(key);
                else if (count == capacity) { Evicted = head; Remove(head); }
                previous[key] = tail;
                next[key] = -1;
                if (tail >= 0) next[tail] = key;
                else head = key;
                tail = key;
                present[key] = true;
                count++;
                return hit;
            }

            internal void Remove(int key)
            {
                if (previous[key] >= 0) next[previous[key]] = next[key];
                else head = next[key];
                if (next[key] >= 0) previous[next[key]] = previous[key];
                else tail = previous[key];
                present[key] = false;
                count--;
            }
        }
    }
}
