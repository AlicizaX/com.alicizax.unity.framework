using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.EventTests.Editor
{
    public sealed class EventLedgerTests
    {
        [TestCase(false, 0)] [TestCase(true, 0)]
        [TestCase(false, 1)] [TestCase(true, 1)]
        [TestCase(false, 32)] [TestCase(true, 32)]
        public void SubscribersReceivePayloadAndHandlesOwnOnlyTheirSlot(bool payload, int count)
        {
            using var f = new EventFixture(payload);
            var listeners = new Listener[count]; var handles = new EventRuntimeHandle[count];
            for (int i = 0; i < count; i++) { listeners[i] = new Listener(); handles[i] = f.Subscribe(listeners[i]); }
            f.Publish(13); f.Publish(19);
            foreach (var target in listeners) { Assert.That(target.Calls, Is.EqualTo(2)); Assert.That(target.Sum, Is.EqualTo(payload ? 32 : 0)); }
            for (int i = 0; i < count; i += 2) { handles[i].Dispose(); handles[i].Dispose(); }
            f.Publish();
            for (int i = 0; i < count; i++) Assert.That(listeners[i].Calls, Is.EqualTo(i % 2 == 0 ? 2 : 3));
            f.VerifyLedger(count / 2);
        }

        [TestCase(false)] [TestCase(true)]
        public void SwapRemoveDefinesNextDispatchOrder(bool payload)
        {
            using var f = new EventFixture(payload);
            var handles = new EventRuntimeHandle[4];
            for (int i = 0; i < 4; i++) { int n = i; handles[i] = f.Subscribe(() => f.Calls.Add(n)); }
            handles[1].Dispose(); f.Publish();
            Assert.That(f.Calls, Is.EqualTo(new[] { 0, 3, 2 })); f.VerifyLedger(3);
        }

        [TestCase(false)] [TestCase(true)]
        public void MutationsInsidePublishApplyImmediatelyAndNestedDispatchSkipsDisposed(bool payload)
        {
            using var f = new EventFixture(payload);
            EventRuntimeHandle self = default, other = default;
            int depth = 0;
            self = f.Subscribe(() =>
            {
                f.Calls.Add(1);
                if (depth++ != 0) return;
                other.Dispose(); self.Dispose();
                f.Subscribe(() => f.Calls.Add(3));
                int capacity = f.Capacity;
                f.Reserve(capacity + 16);
                Assert.That(f.Capacity, Is.EqualTo(capacity + 16));
                Assert.That(f.Count, Is.EqualTo(1));
                f.Publish();
                Assert.That(f.Count, Is.EqualTo(1));
            });
            other = f.Subscribe(() => f.Calls.Add(2));
            f.Publish(); Assert.That(f.Calls, Is.EqualTo(new[] { 1, 3 }));
            f.VerifyLedger(1); f.Publish(); Assert.That(f.Calls, Is.EqualTo(new[] { 1, 3, 3 }));
        }

        [TestCase(false)] [TestCase(true)]
        public void ClearThenResubscribeInsidePublishKeepsOnlyReplacement(bool payload)
        {
            using var f = new EventFixture(payload);
            var target = new Listener(); EventRuntimeHandle replacement = default;
            f.Subscribe(() => { f.Clear(); replacement = f.Subscribe(target); Assert.That(f.Count, Is.EqualTo(1)); });
            var old = f.Subscribe(target);
            f.Publish(); Assert.That(target.Calls, Is.Zero); f.VerifyLedger(1);
            old.Dispose(); f.Publish(); Assert.That(target.Calls, Is.EqualTo(1));
            replacement.Dispose(); f.VerifyLedger(0);
        }

        [TestCase(false)] [TestCase(true)]
        public void ClearInsideNestedDispatchStopsOuterRemainderAndKeepsLaterSubscription(bool payload)
        {
            using var f = new EventFixture(payload);
            var old = new Listener(); var beforeClear = new Listener(); var afterClear = new Listener();
            bool first = true;
            f.Subscribe(() =>
            {
                f.Calls.Add(1);
                if (!first) return; first = false;
                f.Subscribe(beforeClear); f.Clear(); f.Subscribe(afterClear); f.Publish();
                Assert.That(f.Count, Is.EqualTo(1));
                Assert.That(beforeClear.Calls, Is.Zero); Assert.That(afterClear.Calls, Is.EqualTo(1));
            });
            f.Subscribe(old); f.Publish();
            Assert.That(f.Calls, Is.EqualTo(new[] { 1 })); Assert.That(old.Calls, Is.Zero);
            f.VerifyLedger(1); f.Publish(); Assert.That(afterClear.Calls, Is.EqualTo(2)); Assert.That(beforeClear.Calls, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void RepeatedClearAndReserveKeepOnlyTheLastAcceptedSubscription(bool payload)
        {
            using var f = new EventFixture(payload);
            int initial = f.Capacity; var target = new Listener();
            f.Reserve(0); f.Reserve(initial - 1); Assert.That(f.Capacity, Is.EqualTo(initial));
            f.Subscribe(() =>
            {
                f.Clear(); var stale = f.Subscribe(target); f.Reserve(initial + 7);
                f.Clear(); f.Subscribe(target); stale.Dispose(); f.Reserve(1);
            });
            f.Publish(); f.VerifyLedger(1); Assert.That(f.Capacity, Is.EqualTo(initial + 7));
            Assert.That(target.Calls, Is.Zero); f.Publish(); Assert.That(target.Calls, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void SubscribeClearSubscribeInsidePublishDisposeDoesNotStealReplacement(bool payload)
        {
            using var f = new EventFixture(payload);
            var target = new Listener(); EventRuntimeHandle next = default;
            f.Subscribe(() =>
            {
                var beforeClear = f.Subscribe(target);
                f.Clear(); next = f.Subscribe(target); beforeClear.Dispose();
            });
            f.Publish(); f.VerifyLedger(1); Assert.That(target.Calls, Is.Zero);
            f.Publish(); Assert.That(target.Calls, Is.EqualTo(1)); next.Dispose(); f.VerifyLedger(0);
        }

        [TestCase(false)] [TestCase(true)]
        public void DuplicatesInsidePublishAreRejectedAndDisposePermitsReplacement(bool payload)
        {
            using var f = new EventFixture(payload);
            var target = new Listener(); bool once = true;
            f.Subscribe(() =>
            {
                if (!once) return; once = false;
                var first = f.Subscribe(target);
                LogAssert.Expect(LogType.Warning, new Regex("Duplicate event handler subscription"));
                Assert.That(f.Subscribe(target), Is.EqualTo(default(EventRuntimeHandle)));
                first.Dispose(); first.Dispose();
                Assert.That(f.Subscribe(target), Is.Not.EqualTo(default(EventRuntimeHandle)));
            });
            f.Publish(); Assert.That(target.Calls, Is.Zero); f.VerifyLedger(2);
            f.Publish(); Assert.That(target.Calls, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void ClearDisposeAndReusedSlotRejectOldVersions(bool payload)
        {
            using var f = new EventFixture(payload);
            var old = f.Subscribe(new Listener()); old.Dispose();
            var target = new Listener(); var current = f.Subscribe(target);
            old.Dispose(); default(EventRuntimeHandle).Dispose();
            f.Publish(); Assert.That(target.Calls, Is.EqualTo(1));
            f.Clear(); current.Dispose(); f.VerifyLedger(0);
            f.Subscribe(target); current.Dispose(); f.Publish(); Assert.That(target.Calls, Is.EqualTo(2));
        }

        [TestCase(false)] [TestCase(true)]
        public void NegativeReserveAndNullHandlerRejectWithoutChangingLedger(bool payload)
        {
            using var f = new EventFixture(payload);
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Reserve(-1));
            if (payload) Assert.Throws<ArgumentNullException>(() => EventBus.Subscribe<PayloadProbe>((InEventHandler<PayloadProbe>)null));
            else Assert.Throws<ArgumentNullException>(() => EventBus.Subscribe<EmptyProbe>((Action)null));
            f.VerifyLedger(0);
        }

        [Test]
        public void EachTypeAppliesMutationsToPublishesStartedAfterThem()
        {
            using var a = new EventFixture(false); using var b = new EventFixture(true);
            bool once = true, recurse = true;
            var added = new Listener();
            a.Subscribe(() =>
            {
                if (!recurse) return; recurse = false;
                b.Publish(); Assert.That(added.Calls, Is.Zero);
                b.Publish(); Assert.That(added.Calls, Is.EqualTo(1));
            });
            b.Subscribe(() => { if (!once) return; once = false; b.Subscribe(added); a.Subscribe(() => a.Calls.Add(4)); a.Publish(); });
            a.Publish(); Assert.That(a.Calls, Is.EqualTo(new[] { 4 }));
            a.Publish(); Assert.That(a.Calls, Is.EqualTo(new[] { 4, 4 })); a.VerifyLedger(2); b.VerifyLedger(2);
        }

        [TestCase(false)] [TestCase(true)]
        public void GrowthInsidePublishKeepsIndicesForNestedDispatch(bool payload)
        {
            using var f = new EventFixture(payload);
            int capacity = f.Capacity;
            var next = new Listener(); var victim = new Listener();
            EventRuntimeHandle victimHandle = default;
            bool once = true;
            f.Subscribe(() =>
            {
                if (!once) return; once = false;
                var handlers = f.Field("Handlers");
                f.Subscribe(next);
                Assert.That(f.Capacity, Is.EqualTo(capacity * 2));
                Assert.That(f.Field("Handlers"), Is.Not.SameAs(handlers));
                victimHandle.Dispose(); f.Publish();
                Assert.That(next.Calls, Is.EqualTo(1)); Assert.That(victim.Calls, Is.Zero);
                Assert.That(f.Count, Is.EqualTo(capacity));
            });
            victimHandle = f.Subscribe(victim);
            for (int i = 2; i < capacity; i++) f.Subscribe(new Listener());
            f.Publish(); f.VerifyLedger(capacity);
            Assert.That(next.Calls, Is.EqualTo(1)); Assert.That(victim.Calls, Is.Zero);
            f.Publish(); Assert.That(next.Calls, Is.EqualTo(2));
        }

        [TestCase(false)] [TestCase(true)]
        public void StaleVersionsCannotRemoveCurrentListener(bool payload)
        {
            using var f = new EventFixture(payload);
            var listener = new Listener(); f.Subscribe(listener);
            var channel = (EventChannel)f.Channel;
            int slot = ((int[])f.Field("_handlerSlots"))[0];
            var slots = (Array)f.Field("_slots");
            object entry = slots.GetValue(slot);
            int version = (int)entry.GetType().GetField("Version", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entry);
            new EventRuntimeHandle(channel, slot, unchecked(version - 1)).Dispose();
            new EventRuntimeHandle(channel, slot, unchecked(version + 1)).Dispose();
            f.Publish(); Assert.That(listener.Calls, Is.EqualTo(1)); f.VerifyLedger(1);
        }

        [TestCase(false)] [TestCase(true)]
        public void EachCommittedDuplicateWarnsAndDifferentInstancesRemainIndependent(bool payload)
        {
            using var f = new EventFixture(payload);
            var first = new Listener(); var second = new Listener();
            var original = f.Subscribe(first); f.Subscribe(second);
            for (int i = 0; i < 3; i++)
            {
                LogAssert.Expect(LogType.Warning, new Regex("Duplicate event handler subscription"));
                var rejected = f.Subscribe(first);
                Assert.That(rejected, Is.EqualTo(default(EventRuntimeHandle))); rejected.Dispose();
            }
            f.Publish(); Assert.That(first.Calls, Is.EqualTo(1)); Assert.That(second.Calls, Is.EqualTo(1));
            original.Dispose(); f.Subscribe(first); original.Dispose();
            f.Publish(); Assert.That(first.Calls, Is.EqualTo(2)); Assert.That(second.Calls, Is.EqualTo(2)); f.VerifyLedger(2);
        }

        [TestCase(false)] [TestCase(true)]
        public void DebugRegistrationAndCountsFollowTheSameDispatchContract(bool payload)
        {
            using var f = new EventFixture(payload);
            Type type = payload ? typeof(PayloadProbe) : typeof(EmptyProbe);
            EventDebugRegistry.BeginRecording();
            try
            {
                EventDebugRegistry.TryGetDetails(type, out var before, out _);
                EventRuntimeHandle self = default;
                self = f.Subscribe(() =>
                {
                    self.Dispose();
                    Assert.That(EventDebugRegistry.TryGetDetails(type, out var live, out var liveEntries), Is.True);
                    Assert.That(live.SubscriberCount, Is.Zero);
                    Assert.That(liveEntries.Length, Is.Zero);
                    f.Subscribe(new Listener()); throw new NullReferenceException("debug-fault");
                });
                LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException: debug-fault"));
                f.Publish();
                Assert.That(EventDebugRegistry.TryGetDetails(type, out var after, out var entries), Is.True);
                Assert.That(after.Counters.PublishCount - before.Counters.PublishCount, Is.EqualTo(1));
                Assert.That(after.Counters.HandlerExceptionCount - before.Counters.HandlerExceptionCount, Is.EqualTo(1));
                Assert.That(after.Counters.SubscribeCount - before.Counters.SubscribeCount, Is.EqualTo(2));
                Assert.That(after.Counters.UnsubscribeCount - before.Counters.UnsubscribeCount, Is.EqualTo(1));
                Assert.That(after.Parameterless, Is.EqualTo(!payload));
                Assert.That(after.SubscriberCount, Is.EqualTo(1));
                Assert.That(entries.Length, Is.EqualTo(1));
                Assert.That(entries[0].IsParameterless, Is.EqualTo(!payload));
                f.VerifyLedger(1);
            }
            finally
            {
                EventDebugRegistry.EndRecording();
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void DebugCountersStayUntouchedWhileNotRecording(bool payload)
        {
            using var f = new EventFixture(payload);
            Type type = payload ? typeof(PayloadProbe) : typeof(EmptyProbe);
            EventDebugRegistry.TryGetDetails(type, out var before, out _);
            var handle = f.Subscribe(new Listener());
            f.Publish(); handle.Dispose();
            EventDebugRegistry.TryGetDetails(type, out var after, out _);
            Assert.That(after.Counters.PublishCount, Is.EqualTo(before.Counters.PublishCount));
            Assert.That(after.Counters.SubscribeCount, Is.EqualTo(before.Counters.SubscribeCount));
            Assert.That(after.Counters.UnsubscribeCount, Is.EqualTo(before.Counters.UnsubscribeCount));
        }
    }
}
