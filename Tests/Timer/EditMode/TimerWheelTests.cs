using System;
using System.Reflection;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;

namespace AlicizaX.Timer.Tests
{
    public sealed class TimerWheelTests
    {
        [TestCase(70f, 2, false)]
        [TestCase(70f, 2, true)]
        [TestCase(17000f, 3, false)]
        [TestCase(17000f, 3, true)]
        public void UpperLevelsAdvanceThroughEveryTickAndDeliverOnlyDueSurvivors(float delay, int level, bool unscaled)
        {
            using var f = new TimerFixture();
            var calls = new System.Collections.Generic.List<int>();
            ulong first = f.Service.AddTimer(() => calls.Add(1), delay, isUnscaled: unscaled);
            ulong canceled = f.Service.AddTimer(() => calls.Add(2), delay, isUnscaled: unscaled);
            ulong second = f.Service.AddTimer(() => calls.Add(3), delay, isUnscaled: unscaled);
            ulong future = f.Service.AddTimer(() => calls.Add(4), delay + 1, isUnscaled: unscaled);
            Assert.That(TimerProbe.SlotArray<int>(f.Service, first, "QueueIndices")[TimerProbe.Offset(first)] / 256, Is.EqualTo(level));
            long due = TimerProbe.Due(f.Service, first);
            Assert.That(TimerProbe.Due(f.Service, second), Is.EqualTo(due));
            f.Service.RemoveTimer(canceled);
            var advance = (Action<bool, double>)Delegate.CreateDelegate(typeof(Action<bool, double>), f.Service,
                typeof(TimerService).GetMethod("AdvanceQueue", BindingFlags.Instance | BindingFlags.NonPublic));
            long start = TimerProbe.Cursor(f.Service, unscaled);
            int steps = (int)((due - start + 63) / 64);
            for (int i = 0; i < steps; i++) advance(unscaled, (due - 0.5d) / 1000d);
            Assert.That(TimerProbe.Cursor(f.Service, unscaled), Is.EqualTo(due));
            Assert.That(calls, Is.Empty);
            TimerProbe.Ledger(f.Service);
            advance(unscaled, (due + 0.5d) / 1000d);
            Assert.That(calls, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(f.Service.IsRunning(future), Is.True);
            TimerProbe.Statistics(f.Service, 1, 1);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(0L, 0)]
        [TestCase(255L, 0)]
        [TestCase(256L, 1)]
        [TestCase(65535L, 1)]
        [TestCase(65536L, 2)]
        [TestCase(16777215L, 2)]
        [TestCase(16777216L, 3)]
        [TestCase(4294967296L, 3)]
        public void BucketSelectionUsesFourLevelsAtExactBoundaries(long delta, int level)
        {
            MethodInfo method = typeof(TimerService).GetMethod("GetWheelBucketIndex", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (long cursor in new[] { 0L, 255L, 65535L, 16777215L, 4294967295L })
            {
                int bucket = (int)method.Invoke(null, new object[] { cursor + delta, cursor });
                Assert.That(bucket / 256, Is.EqualTo(level));
                Assert.That(bucket % 256, Is.EqualTo(((cursor + delta) >> (level * 8)) & 255));
            }
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public void CascadeRelinksOnlyTouchedBucketWithoutChangingQueueCount(int level, bool unscaled)
        {
            // Structural boundary test, not simulated expiration. Real Tick/cascade/FIFO is covered in PlayMode.
            using var f = new TimerFixture(1024);
            var handles = new ulong[1000];
            float delay = level == 1 ? 2 : level == 2 ? 120 : 20000;
            for (int i = 0; i < handles.Length; i++) handles[i] = f.Service.AddTimer(TimerProbe.NoOp, delay, isUnscaled: unscaled);
            long due = TimerProbe.Due(f.Service, handles[0]);
            long boundary = due & ~((1L << (level * 8)) - 1);
            TimerProbe.Invoke(f.Service, "CascadeWheelLevel", unscaled, boundary, level);
            TimerProbe.Statistics(f.Service, 1000, 1000);
            TimerProbe.Ledger(f.Service);
            for (int i = 0; i < handles.Length; i++)
            {
                int bucket = TimerProbe.SlotArray<int>(f.Service, handles[i], "QueueIndices")[TimerProbe.Offset(handles[i])];
                Assert.That(bucket / 256, Is.LessThan(level));
                Assert.That(f.Service.IsRunning(handles[i]), Is.True);
            }
        }

        [Test]
        public void OverdueInsertionClampsToCursorWithoutDuplicatingQueueMembership()
        {
            using var f = new TimerFixture();
            ulong h = f.Service.AddTimer(TimerProbe.NoOp, 60, isUnscaled: true);
            TimerProbe.Invoke(f.Service, "RemoveFromQueue", TimerProbe.Slot(h), true);
            long cursor = TimerProbe.Cursor(f.Service);
            TimerProbe.SlotArray<double>(f.Service, h, "TriggerTimes")[TimerProbe.Offset(h)] = (cursor - 2) / 1000d;
            TimerProbe.Invoke(f.Service, "AddToQueue", TimerProbe.Slot(h), true);
            Assert.That(TimerProbe.Due(f.Service, h), Is.EqualTo(cursor));
            TimerProbe.Statistics(f.Service, 1, 1);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void FarFutureTopLevelWrapKeepsNodesAndDoesNotTrigger()
        {
            using var f = new TimerFixture();
            ulong handle = f.Service.AddTimer(TimerProbe.NoOp, 5000000, isUnscaled: true);
            long due = TimerProbe.Due(f.Service, handle);
            long boundary = (due & ~((1L << 24) - 1)) - (1L << 32);
            TimerProbe.Invoke(f.Service, "CascadeWheelLevel", true, boundary, 3);
            Assert.That(TimerProbe.Due(f.Service, handle), Is.EqualTo(due));
            TimerProbe.Statistics(f.Service, 1, 1);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(1000)]
        [TestCase(4096)]
        [TestCase(10000)]
        public void IndependentDelaysAcrossAllLevelsSurviveRandomControlOperations(int count)
        {
            using var f = new TimerFixture();
            var random = new Random(1729);
            var handles = new ulong[count];
            float[] spans = { 0.1f, 20f, 2000f, 200000f };
            int levels = 0;
            for (int i = 0; i < count; i++)
            {
                float delay = spans[i & 3] * (1 + (float)random.NextDouble());
                ulong h = handles[i] = f.Service.AddTimer(TimerProbe.NoOp, delay, (i & 4) != 0, (i & 8) != 0);
                int bucket = TimerProbe.SlotArray<int>(f.Service, h, "QueueIndices")[TimerProbe.Offset(h)];
                levels |= 1 << (bucket / 256);
            }
            Assert.That(levels, Is.EqualTo(15));
            for (int i = 0; i < count * 3; i++)
            {
                ulong h = handles[random.Next(count)];
                switch (random.Next(4))
                {
                    case 0: f.Service.Stop(h); break;
                    case 1: f.Service.Resume(h); break;
                    case 2: f.Service.Restart(h); break;
                    case 3: f.Service.RemoveTimer(h); break;
                }
            }
            TimerProbe.Ledger(f.Service);
            f.Dispose();
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }
    }
}
