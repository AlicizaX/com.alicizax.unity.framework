using System;
using System.Diagnostics;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioCpuRegressionTests : AudioTestBase
    {
        private const int Rounds = 21;

        internal static double Measure(string name, int operations, Action action, Action verify)
        {
            var times = new double[Rounds];
            for (int round = 0; round < times.Length; round++)
            {
                long start = Stopwatch.GetTimestamp();
                action();
                long elapsed = Stopwatch.GetTimestamp() - start;
                times[round] = elapsed * 1000.0 / Stopwatch.Frequency;
                verify();
            }
            Array.Sort(times);
            TestContext.WriteLine($"CPU_BATCH,{name},{operations},{Rounds},{times[0]:F6},{times[10]:F6},{times[19]:F6},{times[20]:F6}");
            return times[10];
        }

        [Test]
        public void RandomCacheHitsAndReferenceChangesStayWithinCapacityScalingBudget()
        {
            double smallest = 0;
            foreach (int capacity in new[] { 16, 256, 4096 })
            {
                using var f = new AudioFixture(capacity);
                var names = new string[capacity];
                var entries = new AudioClipCacheEntry[capacity];
                for (int i = 0; i < capacity; i++)
                {
                    names[i] = "cpu-" + i.ToString("D5");
                    f.Clip(names[i], 0.001f);
                    Assert.That(f.Audio.Preload(names[i], AudioCachePolicy.Ttl), Is.True);
                    entries[i] = f.Entry(names[i]);
                }
                int hits = 0;
                double median = Measure("random-hit-ref-" + capacity, 100000, () =>
                {
                    hits = 0;
                    for (int i = 0; i < 100000; i++)
                    {
                        int index = (i * 7919) & (capacity - 1);
                        if (f.Audio.Preload(names[index], AudioCachePolicy.Ttl)) hits++;
                        f.Audio.RetainClip(entries[index]);
                        f.Audio.ReleaseClip(entries[index]);
                    }
                }, () => Assert.That(hits, Is.EqualTo(100000)));
                f.CheckOwnership();
                Assert.That(f.Loader.Loads, Is.EqualTo(capacity));
                if (capacity == 16) smallest = median;
                else Assert.That(median, Is.LessThan(smallest * 3.5), "Capacity grew without increasing work per call.");
            }
        }

        [Test]
        public void NonExpiredResidentCacheDoesNotRequireAFullScanPerTick()
        {
            double smallest = 0;
            foreach (int capacity in new[] { 16, 256, 4096 })
            {
                using var f = new AudioFixture(capacity, ttl: 3600);
                for (int i = 0; i < capacity; i++)
                {
                    string name = "idle-" + i;
                    f.Clip(name, 0.001f);
                    Assert.That(f.Audio.Preload(name, AudioCachePolicy.Ttl), Is.True);
                }
                double median = Measure("resident-idle-tick-" + capacity, 100000, () =>
                {
                    for (int i = 0; i < 100000; i++) f.Tick();
                }, () => Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(capacity)));
                Assert.That(f.Loader.LiveHandles, Is.EqualTo(capacity));
                f.CheckOwnership();
                if (capacity == 16) smallest = median;
                else Assert.That(median, Is.LessThan(smallest * 3.5));
            }
        }

        [Test]
        public void CachedPlaybackAndPrioritySelectionHaveMeasuredCpu()
        {
            using var f = new AudioFixture(voices: 32);
            f.Clip("a");
            // Demand creates one source; the measured loop needs no additional storage.
            ulong first = f.Audio.Play(AudioType.Sound, "a", true);
            Assert.That(first, Is.Not.Zero);
            f.Audio.Stop(first);
            int stopped = 0;
            Measure("cached-play-stop", 10000, () =>
            {
                stopped = 0;
                for (int i = 0; i < 10000; i++) if (f.Audio.Stop(f.Audio.Play(AudioType.Sound, "a", true))) stopped++;
            }, () => Assert.That(stopped, Is.EqualTo(10000)));
            f.CheckOwnership();
            for (int i = 0; i < 32; i++) Assert.That(f.Audio.Play(AudioType.Sound, "a", true), Is.Not.Zero);
            int accepted = 0;
            Measure("steal-32", 10000, () =>
            {
                accepted = 0;
                for (int i = 0; i < 10000; i++) if (f.Audio.Play(AudioType.Sound, "a", true) != 0) accepted++;
            }, () => { Assert.That(accepted, Is.EqualTo(10000)); f.CheckActive(AudioType.Sound, 32, 32); });
            Measure("active-tick-32", 1000, () =>
            {
                for (int i = 0; i < 1000; i++) f.Tick();
            }, () => f.CheckActive(AudioType.Sound, 32, 32));
            f.CheckOwnership();
        }
    }
}
