using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;

namespace AlicizaX.Timer.Tests
{
    public sealed class TimerLedgerTests
    {
        [TestCase(0, 256)]
        [TestCase(1, 256)]
        [TestCase(256, 256)]
        [TestCase(257, 512)]
        [TestCase(1025, 1280)]
        public void CapacityStartsWithOnePageAndGrowsOnePageAtATime(int timers, int expected)
        {
            using var f = new TimerFixture();
            TimerProbe.Statistics(f.Service, 0, 0, capacity: 256);
            var handles = new ulong[timers];
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = f.Service.AddTimer(TimerProbe.NoOp, 3600);
                ((ITimerDebugService)f.Service).GetStatistics(out _, out int capacity, out _, out _);
                Assert.That(capacity, Is.EqualTo(((i >> 8) + 1) * 256), "a page is added only when the previous one is full");
            }

            TimerProbe.Statistics(f.Service, timers, timers, capacity: expected);
            TimerProbe.Ledger(f.Service);
            foreach (ulong handle in handles) f.Service.RemoveTimer(handle);
            TimerProbe.Statistics(f.Service, 0, 0, capacity: expected);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void TrimExcessReleasesTrailingPagesAndRegrowsOnDemand()
        {
            using var f = new TimerFixture();
            var handles = new ulong[1024];
            for (int i = 0; i < handles.Length; i++)
                handles[i] = f.Service.AddTimer(TimerProbe.NoOp, 3600);
            TimerProbe.Statistics(f.Service, handles.Length, handles.Length, capacity: 1024);
            foreach (ulong handle in handles)
                f.Service.RemoveTimer(handle);

            f.Service.TrimExcess();
            TimerProbe.Statistics(f.Service, 0, 0, capacity: 0);
            TimerProbe.Ledger(f.Service);

            Assert.That(f.Service.AddTimer(TimerProbe.NoOp, 1), Is.Not.Zero);
            TimerProbe.Statistics(f.Service, 1, 1, capacity: 256);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReusedSlotRejectsEveryOperationOnOldHandle(bool generic)
        {
            using var f = new TimerFixture();
            ulong first = f.Service.AddTimer(TimerProbe.NoOp, 5);
            f.Service.RemoveTimer(first);
            ulong second = generic ? f.Service.AddTimer(TimerProbe.CountArg, new TimerArg(), 5) : f.Service.AddTimer(TimerProbe.NoOp, 5);
            Assert.That(TimerProbe.Slot(second), Is.EqualTo(TimerProbe.Slot(first)));
            Assert.That(TimerProbe.Generation(second), Is.EqualTo(TimerProbe.Generation(first) + 1));
            f.Service.RemoveTimer(first);
            f.Service.Stop(first);
            f.Service.Resume(first);
            f.Service.Restart(first);
            Assert.That(f.Service.IsRunning(first), Is.False);
            Assert.That(f.Service.GetLeftTime(first), Is.Zero);
            Assert.That(f.Service.IsRunning(second), Is.True);
            TimerProbe.Statistics(f.Service, 1, 1);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void HandleGenerationIsServiceWideAndMonotonic()
        {
            using var f = new TimerFixture();
            ulong a = f.Service.AddTimer(TimerProbe.NoOp, 10);
            ulong b = f.Service.AddTimer(TimerProbe.CountArg, new TimerArg(), 10);
            Assert.That(TimerProbe.Slot(b), Is.Not.EqualTo(TimerProbe.Slot(a)));
            Assert.That(TimerProbe.Generation(b), Is.EqualTo(TimerProbe.Generation(a) + 1));
            f.Service.RemoveTimer(a);
            ulong c = f.Service.AddTimer(TimerProbe.NoOp, 10);
            Assert.That(TimerProbe.Slot(c), Is.EqualTo(TimerProbe.Slot(a)));
            Assert.That(TimerProbe.Generation(c), Is.EqualTo(TimerProbe.Generation(b) + 1));
            Assert.That(f.Service.IsRunning(a), Is.False);
            Assert.That(f.Service.IsRunning(c), Is.True);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void HandlesStayUniqueAcrossServiceInstances()
        {
            ulong old;
            using (var first = new TimerFixture())
            {
                old = first.Service.AddTimer(TimerProbe.NoOp, 60);
            }

            using var second = new TimerFixture();
            ulong current = second.Service.AddTimer(TimerProbe.NoOp, 60);
            Assert.That(TimerProbe.Slot(current), Is.EqualTo(TimerProbe.Slot(old)));
            Assert.That(current, Is.Not.EqualTo(old));
            second.Service.RemoveTimer(old);
            second.Service.Stop(old);
            Assert.That(second.Service.IsRunning(old), Is.False);
            Assert.That(second.Service.IsRunning(current), Is.True);
            TimerProbe.Ledger(second.Service);
        }

        [TestCase(256)]
        [TestCase(1024)]
        [TestCase(4096)]
        public void TrimmedAndRegrownPagesNeverReissueOldHandles(int count)
        {
            using var f = new TimerFixture();
            var old = new ulong[count];
            for (int i = 0; i < count; i++) old[i] = f.Service.AddTimer(TimerProbe.NoOp, 3600, (i & 1) != 0, (i & 2) != 0);
            foreach (ulong handle in old) f.Service.RemoveTimer(handle);
            f.Service.TrimExcess();
            TimerProbe.Statistics(f.Service, 0, 0, capacity: 0);

            var issued = new System.Collections.Generic.HashSet<ulong>(old);
            var fresh = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                fresh[i] = f.Service.AddTimer(TimerProbe.NoOp, 3600, (i & 1) != 0, (i & 2) != 0);
                Assert.That(issued.Add(fresh[i]), Is.True, "trimmed page reissued a handle");
            }

            foreach (ulong handle in old)
            {
                f.Service.RemoveTimer(handle);
                f.Service.Stop(handle);
                f.Service.Restart(handle);
                Assert.That(f.Service.IsRunning(handle), Is.False);
            }

            foreach (ulong handle in fresh) Assert.That(f.Service.IsRunning(handle), Is.True);
            TimerProbe.Statistics(f.Service, count, count);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void GenerationAdvancesOnEveryAcquireRegardlessOfSlotReuse()
        {
            using var f = new TimerFixture();
            ulong first = f.Service.AddTimer(TimerProbe.NoOp, 10);
            for (int i = 0; i < 1000; i++)
            {
                ulong h = f.Service.AddTimer(TimerProbe.NoOp, 10);
                f.Service.RemoveTimer(h);
            }

            ulong last = f.Service.AddTimer(TimerProbe.NoOp, 10);
            Assert.That(TimerProbe.Generation(last), Is.EqualTo(TimerProbe.Generation(first) + 1001));
            Assert.That(TimerProbe.Slot(last), Is.Not.EqualTo(TimerProbe.Slot(first)));
            TimerProbe.Statistics(f.Service, 2, 2);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(0UL)]
        [TestCase(1UL)]
        [TestCase(0x1fffffUL)]
        [TestCase(0x200000UL)]
        [TestCase(0x200001UL)]
        [TestCase(0x200101UL)]
        [TestCase(0xffffffffUL)]
        [TestCase(0x100000000UL)]
        [TestCase(0x100000101UL)]
        [TestCase(0xffffffffffffffffUL)]
        public void MalformedHandlesAreNoOps(ulong handle)
        {
            using var f = new TimerFixture();
            f.Service.RemoveTimer(handle);
            f.Service.RemoveTimer(handle);
            f.Service.Stop(handle);
            f.Service.Resume(handle);
            f.Service.Restart(handle);
            Assert.That(f.Service.IsRunning(handle), Is.False);
            Assert.That(f.Service.GetLeftTime(handle), Is.Zero);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void NullCallbacksFailWithoutAcquiringAndRetrySucceeds()
        {
            using var f = new TimerFixture();
            Assert.That(f.Service.AddTimer(null, 1), Is.Zero);
            Assert.That(f.Service.AddTimer<TimerArg>(null, null, 1), Is.Zero);
            TimerProbe.Statistics(f.Service, 0, 0);
            Assert.That(f.Service.AddTimer(TimerProbe.CountArg, null, 1), Is.Not.Zero);
            TimerProbe.Statistics(f.Service, 1, 1);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteDelaysFailWithoutAcquiring(float delay)
        {
            using var f = new TimerFixture();
            Assert.That(f.Service.AddTimer(TimerProbe.NoOp, delay), Is.Zero);
            Assert.That(f.Service.AddTimer(TimerProbe.CountArg, null, delay), Is.Zero);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(-5f)]
        [TestCase(0f)]
        [TestCase(0.0000001f)]
        [TestCase(0.000001f)]
        public void FiniteSmallDelaysNormalizeToMinimum(float delay)
        {
            using var f = new TimerFixture();
            ulong handle = f.Service.AddTimer(TimerProbe.NoOp, delay);
            Assert.That(TimerProbe.SlotArray<double>(f.Service, handle, "Durations")[TimerProbe.Offset(handle)], Is.EqualTo(0.000001d));
            Assert.That(TimerProbe.Due(f.Service, handle), Is.GreaterThanOrEqualTo(TimerProbe.Cursor(f.Service, false)));
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void HugeFiniteDelayCannotWrapIntoAnImmediateTick()
        {
            using var f = new TimerFixture();
            ulong handle = f.Service.AddTimer(TimerProbe.NoOp, float.MaxValue);
            Assert.That(TimerProbe.Due(f.Service, handle), Is.EqualTo(long.MaxValue));
            Assert.That(f.Service.GetLeftTime(handle), Is.EqualTo(float.MaxValue));
            f.Service.Stop(handle);
            f.Service.Resume(handle);
            f.Service.Restart(handle);
            Assert.That(TimerProbe.Due(f.Service, handle), Is.EqualTo(long.MaxValue));
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ControlStateMachineKeepsExactlyOneQueueNode(bool loop, bool unscaled)
        {
            using var f = new TimerFixture();
            ulong h = f.Service.AddTimer(TimerProbe.NoOp, 30, loop, unscaled);
            f.Service.Resume(h);
            TimerProbe.Statistics(f.Service, 1, 1);
            f.Service.Stop(h);
            float remaining = f.Service.GetLeftTime(h);
            f.Service.Stop(h);
            Assert.That(f.Service.GetLeftTime(h), Is.EqualTo(remaining));
            Assert.That(f.Service.IsRunning(h), Is.False);
            TimerProbe.Statistics(f.Service, 1, 0);
            TimerProbe.Ledger(f.Service);
            f.Service.Resume(h);
            f.Service.Resume(h);
            Assert.That(f.Service.IsRunning(h), Is.True);
            f.Service.Restart(h);
            f.Service.Restart(h);
            TimerProbe.Statistics(f.Service, 1, 1);
            f.Service.Stop(h);
            f.Service.Restart(h);
            Assert.That(f.Service.GetLeftTime(h), Is.GreaterThan(29));
            TimerProbe.Ledger(f.Service);
            f.Service.RemoveTimer(h);
            f.Service.RemoveTimer(h);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [TestCase(1000)]
        [TestCase(4096)]
        [TestCase(10000)]
        public void GrowthCancellationAndLedgerReconcile(int count)
        {
            using var f = new TimerFixture();
            var handles = new ulong[count];
            for (int i = 0; i < count; i++) handles[i] = f.Service.AddTimer(TimerProbe.NoOp, i + 1, (i & 1) != 0, (i & 2) != 0);
            int capacity = ((count + 255) / 256) * 256;
            TimerProbe.Statistics(f.Service, count, count, capacity: capacity);
            TimerProbe.Ledger(f.Service);
            for (int i = 0; i < count; i += 2) f.Service.RemoveTimer(handles[i]);
            TimerProbe.Ledger(f.Service);
            for (int i = 1; i < count; i += 2) f.Service.Stop(handles[i]);
            TimerProbe.Statistics(f.Service, count / 2, 0);
            TimerProbe.Ledger(f.Service);
            f.Dispose();
            TimerProbe.Statistics(f.Service, 0, 0, capacity: 0);
            ((ITimerDebugService)f.Service).GetStatistics(out _, out _, out int peak, out _);
            Assert.That(peak, Is.EqualTo(count));
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void ExhaustedCapacityRejectsBothCallbacksAndCanRetryAfterRemove()
        {
            using var f = new TimerFixture();
            ulong last = 0;
            int accepted = 0;
            for (int i = 0; i < 1048576; i++)
            {
                last = f.Service.AddTimer(TimerProbe.NoOp, 3600);
                if (last != 0) accepted++;
            }
            Assert.That(accepted, Is.EqualTo(1048576));
            Assert.That(f.Service.AddTimer(TimerProbe.NoOp, 1), Is.Zero);
            Assert.That(f.Service.AddTimer(TimerProbe.CountArg, null, 1), Is.Zero);
            TimerProbe.Statistics(f.Service, 1048576, 1048576, capacity: 1048576);
            f.Service.RemoveTimer(last);
            ulong retried = f.Service.AddTimer(TimerProbe.CountArg, null, 1);
            Assert.That(retried, Is.Not.Zero.And.Not.EqualTo(last));
            f.Dispose();
            TimerProbe.Statistics(f.Service, 0, 0, capacity: 0);
            Array pages = TimerProbe.Read<Array>(f.Service, "_pages");
            foreach (object page in pages)
            {
                Assert.That(page, Is.Null, "destroyed service keeps no timer pages");
            }
        }

        [Test]
        public void DestroyedServiceRejectsNewTimersAndClearsReferences()
        {
            using var f = new TimerFixture();
            ulong h = f.Service.AddTimer(TimerProbe.CountArg, new TimerArg(), 1, true);
            f.Service.Stop(h);
            f.Dispose();
            f.Service.Restart(h);
            Assert.That(f.Service.IsRunning(h), Is.False);
            Assert.That(f.Service.AddTimer(TimerProbe.NoOp, 1), Is.Zero);
            Assert.That(f.Service.AddTimer(TimerProbe.CountArg, null, 1), Is.Zero);
            f.Tick();
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [Test]
        public void StoppedLoopRetainsArgumentUntilExplicitRemove()
        {
            using var f = new TimerFixture();
            WeakReference weak = RegisterArgument(f.Service, out ulong handle);
            f.Service.Stop(handle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.That(weak.IsAlive, Is.True);
            TimerProbe.Statistics(f.Service, 1, 0);
            f.Service.RemoveTimer(handle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.That(weak.IsAlive, Is.False);
            TimerProbe.Ledger(f.Service);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterArgument(TimerService service, out ulong handle)
        {
            var arg = new TimerArg();
            handle = service.AddTimer(TimerProbe.CountArg, arg, 60, true);
            return new WeakReference(arg);
        }

        [Test]
        public void DebugViewsUseCallerBufferWithoutChangingOwnership()
        {
            using var f = new TimerFixture();
            ulong a = f.Service.AddTimer(TimerProbe.NoOp, 1);
            ulong b = f.Service.AddTimer(TimerProbe.NoOp, 2, true, true);
            f.Service.Stop(b);
            var debug = (ITimerDebugService)f.Service;
            Assert.That(debug.GetAllTimers(null), Is.Zero);
            Assert.That(debug.GetAllTimers(Array.Empty<TimerDebugInfo>()), Is.Zero);
            var info = new TimerDebugInfo[2];
            Assert.That(debug.GetAllTimers(new TimerDebugInfo[1]), Is.EqualTo(1));
            Assert.That(debug.GetAllTimers(info), Is.EqualTo(2));
            Assert.That(info[0].TimerHandle, Is.EqualTo(a));
            Assert.That(info[0].Flags, Is.EqualTo(TimerDebugFlags.Running));
            Assert.That(info[1].Flags, Is.EqualTo(TimerDebugFlags.Loop | TimerDebugFlags.Unscaled));
            var editor = (ITimerEditorDebugService)f.Service;
            Assert.That(editor.GetStaleOneShotTimers(null), Is.Zero);
            Assert.That(editor.GetStaleOneShotTimers(Array.Empty<TimerDebugInfo>()), Is.Zero);
            Assert.That(editor.GetStaleOneShotTimers(info), Is.Zero);
            TimerProbe.SlotArray<double>(f.Service, a, "CreationTimes")[TimerProbe.Offset(a)] -= 301;
            TimerProbe.SlotArray<double>(f.Service, b, "CreationTimes")[TimerProbe.Offset(b)] -= 301;
            Assert.That(editor.GetStaleOneShotTimers(info), Is.EqualTo(1));
            Assert.That(info[0].TimerHandle, Is.EqualTo(a));
            TimerProbe.Statistics(f.Service, 2, 1);
            TimerProbe.Ledger(f.Service);
            TestContext.WriteLine($"SIZE,TimerDebugInfo,{Marshal.SizeOf<TimerDebugInfo>()},handle,{sizeof(ulong)}");
        }
    }
}
