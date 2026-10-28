using System;
using System.Collections;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Timer.Tests
{
    public sealed class TimerTimeTests
    {
        private float timeScale;
        [SetUp] public void SaveTimeScale() { timeScale = Time.timeScale; Time.timeScale = 1; }
        [TearDown] public void RestoreTimeScale() => Time.timeScale = timeScale;

        [UnityTest]
        public IEnumerator ScaledFreezesAtZeroWhileUnscaledTtlExpires()
        {
            using var f = new TimerFixture();
            Time.timeScale = 0;
            int scaled = 0, unscaled = 0;
            ulong s = f.Service.AddTimer(() => scaled++, 0.03f);
            f.Service.AddTimer(() => unscaled++, 0.03f, isUnscaled: true);
            double before = Time.timeAsDouble;
            yield return TimerProbe.Until(f, () => unscaled == 1);
            Assert.That(Time.timeAsDouble, Is.EqualTo(before));
            Assert.That(scaled, Is.Zero);
            Assert.That(f.Service.GetLeftTime(s), Is.EqualTo(0.03f));
            TimerProbe.Statistics(f.Service, 1, 1);
            Time.timeScale = 1;
            yield return TimerProbe.Until(f, () => scaled == 1);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator ChangedTimeScaleUsesAbsoluteUnityClocksNotTickDelta()
        {
            using var f = new TimerFixture();
            int calls = 0;
            Time.timeScale = 0.5f;
            ulong h = f.Service.AddTimer(() => calls++, 0.4f);
            double start = Time.timeAsDouble;
            yield return TimerProbe.Wait(0.1);
            f.Tick(float.MaxValue);
            Assert.That(calls, Is.Zero);
            Assert.That(f.Service.GetLeftTime(h), Is.EqualTo((float)(0.4f - (Time.timeAsDouble - start))).Within(0.000001f));
            Time.timeScale = 2;
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(Time.timeAsDouble - start, Is.GreaterThanOrEqualTo(0.4f));
            TimerProbe.Statistics(f.Service, 0, 0);
        }

        [UnityTest]
        public IEnumerator StopPreservesRemainingWhileTimePassesAndResumeUsesIt()
        {
            using var f = new TimerFixture();
            int calls = 0;
            ulong h = f.Service.AddTimer(() => calls++, 0.2f, isUnscaled: true);
            yield return TimerProbe.Wait(0.04);
            f.Service.Stop(h);
            float remaining = f.Service.GetLeftTime(h);
            yield return TimerProbe.Wait(0.25);
            f.Tick();
            Assert.That(calls, Is.Zero);
            Assert.That(f.Service.GetLeftTime(h), Is.EqualTo(remaining));
            double resumed = Time.unscaledTimeAsDouble;
            f.Service.Resume(h);
            yield return TimerProbe.Until(f, () => calls == 1);
            Assert.That(Time.unscaledTimeAsDouble - resumed, Is.GreaterThanOrEqualTo(remaining));
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator MissedSecondFiresOnTheNextTickWithoutTickByTickCatchup()
        {
            using var f = new TimerFixture();
            int calls = 0;
            ulong h = f.Service.AddTimer(() => calls++, 0.95f, isUnscaled: true);
            ulong sentinel = f.Service.AddTimer(TimerProbe.NoOp, 60, isUnscaled: true);
            long start = TimerProbe.Cursor(f.Service);
            yield return TimerProbe.Wait(1.05);
            Assert.That(calls, Is.Zero, "without Tick only the clock advances");
            Assert.That(f.Service.GetLeftTime(h), Is.Zero);

            long target = (long)(Time.unscaledTimeAsDouble * 1000);
            f.Tick();
            Assert.That(calls, Is.EqualTo(1), "an overdue timer fires on the first tick after the stall");
            Assert.That(TimerProbe.Cursor(f.Service), Is.GreaterThanOrEqualTo(target));
            Assert.That(f.Service.IsRunning(h), Is.False);
            TestContext.WriteLine($"CATCHUP,missed-ms,{target - start},tick-calls,1,budget-per-clock,{256}");
            f.Service.RemoveTimer(sentinel);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator BothClocksAdvanceToTheCurrentTickIndependently()
        {
            using var f = new TimerFixture();
            f.Service.AddTimer(TimerProbe.NoOp, 60);
            f.Service.AddTimer(TimerProbe.NoOp, 60, isUnscaled: true);
            yield return TimerProbe.Wait(0.2);

            long scaledTarget = (long)(Time.timeAsDouble * 1000);
            long unscaledTarget = (long)(Time.unscaledTimeAsDouble * 1000);
            f.Tick();
            Assert.That(TimerProbe.Cursor(f.Service, false), Is.GreaterThanOrEqualTo(scaledTarget));
            Assert.That(TimerProbe.Cursor(f.Service), Is.GreaterThanOrEqualTo(unscaledTarget));
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator SubmillisecondLoopCoalescesMissedPeriodsWithoutBurstOrStall()
        {
            using var f = new TimerFixture();
            int calls = 0;
            ulong h = f.Service.AddTimer(() => calls++, 0, true, true);
            yield return TimerProbe.Wait(0.2);
            for (int i = 0; i < 10; i++) f.Tick();
            Assert.That(calls, Is.EqualTo(1), "overdue intervals are coalesced at the current Unity time");
            double nextTrigger = TimerProbe.SlotArray<double>(f.Service, h, "TriggerTimes")[TimerProbe.Offset(h)];
            Assert.That(nextTrigger, Is.GreaterThan(Time.unscaledTimeAsDouble));
            yield return TimerProbe.Until(f, () => calls >= 3);
            Assert.That(calls, Is.EqualTo(3));
            f.Service.RemoveTimer(h);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator FutureTimerDoesNotFireDuringCatchupAndCancelledTimerNeverArrives()
        {
            using var f = new TimerFixture();
            int oldCalls = 0, replacementCalls = 0;
            ulong old = f.Service.AddTimer(() => oldCalls++, 0.01f, isUnscaled: true);
            yield return TimerProbe.Wait(0.2);
            f.Service.RemoveTimer(old);
            ulong next = f.Service.AddTimer(() => replacementCalls++, 0.03f, isUnscaled: true);
            for (int i = 0; i < 8; i++) f.Tick();
            Assert.That(oldCalls, Is.Zero);
            Assert.That(replacementCalls, Is.Zero);
            Assert.That(f.Service.IsRunning(next), Is.True);
            yield return TimerProbe.Until(f, () => replacementCalls == 1);
            Assert.That(oldCalls, Is.Zero);
            TimerProbe.Ledger(f.Service);
        }

        [UnityTest]
        public IEnumerator RepeatedCacheHitsAndCountdownReplacementCancelPreviousCallback()
        {
            using var f = new TimerFixture();
            var arg = new TimerArg();
            ulong ttl = 0, countdown = 0;
            int blocks = 0;
            TimerHandlerNoArgs unblock = () => blocks++;
            for (int i = 0; i < 10000; i++)
            {
                f.Service.RemoveTimer(ttl);
                ttl = f.Service.AddTimer(TimerProbe.CountArg, arg, 0.02f, isUnscaled: true);
                f.Service.RemoveTimer(countdown);
                countdown = f.Service.AddTimer(unblock, 0.02f);
            }
            TimerProbe.Statistics(f.Service, 2, 2, capacity: 256);
            Time.timeScale = 0;
            yield return TimerProbe.Until(f, () => arg.Calls == 1);
            Assert.That(blocks, Is.Zero);
            Time.timeScale = 1;
            yield return TimerProbe.Until(f, () => blocks == 1);
            TimerProbe.Statistics(f.Service, 0, 0);
            TimerProbe.Ledger(f.Service);
        }
    }
}
