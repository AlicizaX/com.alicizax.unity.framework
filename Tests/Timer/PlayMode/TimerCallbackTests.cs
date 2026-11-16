using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Timer.Tests
{
    public sealed class TimerCallbackTests
    {
        [UnityTest]
        public IEnumerator OneShotExpiresAndReusedSlotIgnoresOldHandle()
        {
            using var f = new TimerFixture();
            int calls = 0;
            ulong old = f.Service.AddTimer(() => calls++, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(f.Service.IsRunning(old), Is.False);
            Assert.That(f.Service.GetLeftTime(old), Is.Zero);
            TimerProbe.Statistics(f.Service, 0, 0);
            ulong next = f.Service.AddTimer(() => calls++, 0.02f, isUnscaled: true);
            Assert.That(TimerProbe.Slot(next), Is.EqualTo(TimerProbe.Slot(old)));
            f.Service.RemoveTimer(old);
            f.Service.Restart(old);
            yield return TimerProbe.Until(f, () => calls == 2);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator RemoveSelfDefersReuseAndCanDeleteSiblingAndAdd()
        {
            using var f = new TimerFixture();
            ulong self = 0, sibling = 0, added = 0;
            int fired = 0, siblingCalls = 0;
            int activeDuring = -1, freeDuring = -1, capacityDuring = -1;
            self = f.Service.AddTimer(() =>
            {
                fired++;
                f.Service.RemoveTimer(sibling);
                f.Service.RemoveTimer(self);
                f.Service.RemoveTimer(self);
                ((ITimerDebugService)f.Service).GetStatistics(out activeDuring, out capacityDuring, out _, out freeDuring);
                added = f.Service.AddTimer(() => fired++, 0.001f, isUnscaled: true);
                f.Service.Stop(self);
                f.Service.Resume(self);
                f.Service.Restart(self);
            }, 0.001f, true, true);
            sibling = f.Service.AddTimer(() => siblingCalls++, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => fired == 2);
            Assert.That(siblingCalls, Is.Zero);
            Assert.That(activeDuring, Is.Zero);
            Assert.That(freeDuring, Is.EqualTo(capacityDuring - 1));
            Assert.That(TimerProbe.Slot(added), Is.Not.EqualTo(TimerProbe.Slot(self)));
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator RestartOneShotInCallbackKeepsHandleUntilFinalExpiration()
        {
            using var f = new TimerFixture();
            ulong handle = 0;
            int calls = 0;
            handle = f.Service.AddTimer(() =>
            {
                calls++;
                if (calls < 3) f.Service.Restart(handle);
            }, 0.01f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 3);
            Assert.That(f.Service.IsRunning(handle), Is.False);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator RestartThenStopInCallbackKeepsRenewedOneShotPaused()
        {
            using var f = new TimerFixture();
            ulong h = 0;
            int calls = 0;
            h = f.Service.AddTimer(() =>
            {
                if (++calls == 1) { f.Service.Restart(h); f.Service.Stop(h); }
            }, 0.03f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 1);
            TimerProbe.Statistics(f.Service, 1, 0);
            Assert.That(f.Service.GetLeftTime(h), Is.EqualTo(0.03f));
            f.Service.Resume(h);
            yield return TimerProbe.Until(f, () => calls == 2);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator StopResumeOneShotInCallbackOverridesExpiration()
        {
            using var f = new TimerFixture();
            ulong handle = 0;
            int calls = 0;
            handle = f.Service.AddTimer(() =>
            {
                if (++calls == 1) { f.Service.Stop(handle); f.Service.Resume(handle); }
            }, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 2);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator StopAloneDoesNotUndoCompletedOneShot()
        {
            using var f = new TimerFixture();
            ulong h = 0;
            int calls = 0;
            float remaining = 0;
            h = f.Service.AddTimer(() => { calls++; f.Service.Stop(h); remaining = f.Service.GetLeftTime(h); }, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(remaining, Is.EqualTo(0.000001f));
            f.Service.Resume(h);
            Assert.That(f.Service.IsRunning(h), Is.False);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator LoopCallbackStopStoresFullDurationAndRestartStopRemainsPaused()
        {
            using var f = new TimerFixture();
            ulong h = 0;
            int calls = 0;
            h = f.Service.AddTimer(() => { calls++; f.Service.Stop(h); }, 0.03f, true, true);
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(f.Service.GetLeftTime(h), Is.EqualTo(0.03f));
            TimerProbe.Statistics(f.Service, 1, 0);
            yield return TimerProbe.Wait(0.1);
            f.Tick();
            Assert.That(calls, Is.EqualTo(1));
            f.Service.Resume(h);
            yield return TimerProbe.Until(f, () => calls == 2);
            f.Service.Restart(h);
            f.Service.Stop(h);
            TimerProbe.Statistics(f.Service, 1, 0);
            f.Service.RemoveTimer(h);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator LoopRestartInCallbackSchedulesOnlyOnce()
        {
            using var f = new TimerFixture();
            ulong h = 0;
            int calls = 0;
            h = f.Service.AddTimer(() =>
            {
                if (++calls < 3) { f.Service.Restart(h); f.Service.Resume(h); }
                else f.Service.RemoveTimer(h);
            }, 0.01f, true, true);
            yield return TimerProbe.Until(f, () => calls == 3);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator CallbackExceptionsLogAndBothHandlerKindsConverge()
        {
            using var f = new TimerFixture();
            var arg = new TimerArg();
            int good = 0;
            ulong loop = 0;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: timer-one-shot"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: timer-loop"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: timer-loop"));
            f.Service.AddTimer(() => throw new InvalidOperationException("timer-one-shot"), 0.001f, isUnscaled: true);
            loop = f.Service.AddTimer<TimerArg>(a =>
            {
                if (++a.Calls == 2) f.Service.RemoveTimer(loop);
                throw new InvalidOperationException("timer-loop");
            }, arg, 0.01f, true, true);
            f.Service.AddTimer(() => good++, 0.02f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => good == 1 && arg.Calls == 2);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator NestedTickDoesNotExecuteSiblingOrReleaseOuterSlotEarly()
        {
            using var f = new TimerFixture();
            ulong self = 0, added = 0;
            int siblingCalls = 0, observedInside = -1, calls = 0;
            self = f.Service.AddTimer(() =>
            {
                calls++;
                f.Tick();
                observedInside = siblingCalls;
                f.Service.RemoveTimer(self);
                added = f.Service.AddTimer(TimerProbe.NoOp, 30, isUnscaled: true);
            }, 0.001f, isUnscaled: true);
            f.Service.AddTimer(() => siblingCalls++, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 1 && siblingCalls == 1);
            Assert.That(observedInside, Is.Zero, "nested Tick must not overwrite callback ownership");
            Assert.That(TimerProbe.Slot(added), Is.Not.EqualTo(TimerProbe.Slot(self)));
            TimerProbe.Statistics(f.Service, 1, 1);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator ShutdownInsideCallbackKeepsExecutingSlotPendingAndRejectsAdd()
        {
            using var f = new TimerFixture();
            ulong self = 0, attempted = ulong.MaxValue;
            int calls = 0, free = -1, capacity = -1;
            self = f.Service.AddTimer(() =>
            {
                calls++;
                f.Dispose();
                ((ITimerDebugService)f.Service).GetStatistics(out _, out capacity, out _, out free);
                attempted = f.Service.AddTimer(TimerProbe.NoOp, 1);
                f.Service.Restart(self);
                f.Tick();
            }, 0.001f, true, true);
            ulong stopped = f.Service.AddTimer(TimerProbe.CountArg, new TimerArg(), 30, true);
            f.Service.Stop(stopped);
            f.Service.AddTimer(TimerProbe.NoOp, 30, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(free, Is.EqualTo(capacity - 1), "executing slot cannot be returned by ClearAll");
            Assert.That(attempted, Is.Zero);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator RemoveThenShutdownThenThrowStillReturnsPendingSlot()
        {
            using var f = new TimerFixture();
            ulong h = 0;
            int calls = 0;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: removed-shutdown"));
            h = f.Service.AddTimer(() =>
            {
                calls++;
                f.Service.RemoveTimer(h);
                f.Dispose();
                throw new InvalidOperationException("removed-shutdown");
            }, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => calls == 1);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator NullGenericArgumentAndPooledObjectReuseRemainExplicitCallerResponsibilities()
        {
            using var f = new TimerFixture();
            bool sawNull = false;
            int observed = -1;
            var arg = new TimerArg { Value = 1 };
            f.Service.AddTimer<TimerArg>(a => sawNull = a == null, null, 0.001f, isUnscaled: true);
            ulong cancelled = f.Service.AddTimer<TimerArg>(a => observed = -100, arg, 0.001f, isUnscaled: true);
            f.Service.RemoveTimer(cancelled);
            arg.Value = 2;
            f.Service.AddTimer<TimerArg>(a => observed = a.Value, arg, 0.001f, isUnscaled: true);
            yield return TimerProbe.Until(f, () => sawNull && observed == 2);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator DestroyedUnityArgumentIsDeliveredWithoutInventingAnOwnerLifetime()
        {
            using var f = new TimerFixture();
            var arg = new GameObject("timer borrowed argument");
            bool called = false, managedReference = false, unityDestroyed = false;
            f.Service.AddTimer<GameObject>(value =>
            {
                called = true;
                managedReference = !ReferenceEquals(value, null);
                unityDestroyed = value == null;
            }, arg, 0.02f, isUnscaled: true);
            UnityEngine.Object.Destroy(arg);
            yield return null;
            yield return TimerProbe.Until(f, () => called);
            Assert.That(managedReference, Is.True);
            Assert.That(unityDestroyed, Is.True);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator SameTickBurstPreservesInsertionOrderAcrossCascade()
        {
            using var f = new TimerFixture(4096);
            var order = new List<int>(4096);
            var handles = new ulong[4096];
            Action<TimerArg> callback = a => order.Add(a.Value);
            for (int i = 0; i < handles.Length; i++) handles[i] = f.Service.AddTimer(callback, new TimerArg { Value = i }, 0.3f, isUnscaled: true);
            Assert.That(TimerProbe.Due(f.Service, handles[0]), Is.EqualTo(TimerProbe.Due(f.Service, handles[4095])));
            yield return TimerProbe.Until(f, () => order.Count == 4096);
            for (int i = 0; i < order.Count; i++) Assert.That(order[i], Is.EqualTo(i));
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator TrimInsideCallbackReleasesTrailingPagesAndRetainsExecutingPage([Values(false, true)] bool removeSelf)
        {
            using var f = new TimerFixture();
            var fillers = new ulong[768];
            ulong self = 0;
            int calls = 0, capacityInside = -1, freeInside = -1;
            self = f.Service.AddTimer(() =>
            {
                calls++;
                if (removeSelf) f.Service.RemoveTimer(self);
                foreach (ulong filler in fillers) f.Service.RemoveTimer(filler);
                f.Service.TrimExcess();
                ((ITimerDebugService)f.Service).GetStatistics(out _, out capacityInside, out _, out freeInside);
            }, 0.001f, isUnscaled: true);
            for (int i = 0; i < fillers.Length; i++) fillers[i] = f.Service.AddTimer(TimerProbe.NoOp, 3600, isUnscaled: true);
            Assert.That(TimerProbe.Slot(self) >> 8, Is.Zero);
            TimerProbe.Statistics(f.Service, 769, 769, capacity: 1024);
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(capacityInside, Is.EqualTo(256), "trailing pages are trimmed while the executing page survives");
            Assert.That(freeInside, Is.EqualTo(255));
            TimerProbe.Statistics(f.Service, 0, 0, capacity: 256);
            TimerProbe.Ledger(f.Service);
            Assert.That(f.Service.AddTimer(TimerProbe.NoOp, 1), Is.Not.Zero);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator PeriodicTrimTimerShrinksCapacityAfterBurst()
        {
            using var f = new TimerFixture();
            int trims = 0;
            ulong trimmer = f.Service.AddTimer(() => { trims++; f.Service.TrimExcess(); }, 0.01f, true, true);
            Assert.That(TimerProbe.Slot(trimmer), Is.Zero);
            var burst = new ulong[2048];
            for (int i = 0; i < burst.Length; i++) burst[i] = f.Service.AddTimer(TimerProbe.NoOp, 3600, isUnscaled: true);
            TimerProbe.Statistics(f.Service, 2049, 2049, capacity: 2304);
            yield return TimerProbe.Until(f, () => trims >= 1);
            TimerProbe.Statistics(f.Service, 2049, 2049, capacity: 2304);
            foreach (ulong handle in burst) f.Service.RemoveTimer(handle);
            int before = trims;
            yield return TimerProbe.Until(f, () => trims > before);
            TimerProbe.Statistics(f.Service, 1, 1, capacity: 256);
            Assert.That(f.Service.IsRunning(trimmer), Is.True);
            TimerProbe.Ledger(f.Service);
        }
    }
}
