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
        public void DeferredMutationsKeepPackedSnapshotAndSkipCancelledAcrossNestedDispatch(bool payload)
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
                Assert.That(f.Capacity, Is.EqualTo(capacity));
                Assert.That(f.Count, Is.EqualTo(2));
                f.Publish();
                Assert.That(f.Count, Is.EqualTo(2));
            });
            other = f.Subscribe(() => f.Calls.Add(2));
            f.Publish(); Assert.That(f.Calls, Is.EqualTo(new[] { 1 }));
            f.VerifyLedger(1); f.Publish(); Assert.That(f.Calls, Is.EqualTo(new[] { 1, 3 }));
        }

        [TestCase(false)] [TestCase(true)]
        public void ClearThenResubscribeKeepsCurrentSnapshotAndRebuildsInQueueOrder(bool payload)
        {
            using var f = new EventFixture(payload);
            var target = new Listener(); EventRuntimeHandle replacement = default;
            f.Subscribe(() => { f.Clear(); replacement = f.Subscribe(target); Assert.That(f.Count, Is.EqualTo(2)); });
            var old = f.Subscribe(target);
            f.Publish(); Assert.That(target.Calls, Is.EqualTo(1)); f.VerifyLedger(1);
            old.Dispose(); f.Publish(); Assert.That(target.Calls, Is.EqualTo(2));
            replacement.Dispose(); f.VerifyLedger(0);
        }

        [TestCase(false)] [TestCase(true)]
        public void ClearInsideNestedDispatchKeepsOldSnapshotUntilOutermostReturn(bool payload)
        {
            using var f = new EventFixture(payload);
            var old = new Listener(); var beforeClear = new Listener(); var afterClear = new Listener();
            bool first = true;
            f.Subscribe(() =>
            {
                f.Calls.Add(1);
                if (!first) return; first = false;
                f.Subscribe(beforeClear); f.Clear(); f.Subscribe(afterClear); f.Publish();
                Assert.That(f.Count, Is.EqualTo(2));
                Assert.That(beforeClear.Calls, Is.Zero); Assert.That(afterClear.Calls, Is.Zero);
            });
            f.Subscribe(old); f.Publish();
            Assert.That(f.Calls, Is.EqualTo(new[] { 1, 1 })); Assert.That(old.Calls, Is.EqualTo(2));
            f.VerifyLedger(1); f.Publish(); Assert.That(afterClear.Calls, Is.EqualTo(1)); Assert.That(beforeClear.Calls, Is.Zero);
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
        public void PendingSubscribeClearSubscribeDisposeDoesNotStealReplacement(bool payload)
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
        public void PendingDuplicatesAreRejectedAndDisposePermitsReplacement(bool payload)
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

        [TestCase(-1)] [TestCase(0)] [TestCase(int.MaxValue)]
        public void InvalidTypeIdsAreNoOps(int typeId)
        {
            using var f = new EventFixture(false);
            var target = new Listener(); f.Subscribe(target);
            new EventRuntimeHandle(typeId, 0, 1).Dispose();
            f.Publish(); Assert.That(target.Calls, Is.EqualTo(1)); f.VerifyLedger(1);
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
        public void EachTypeCommitsAtItsOwnOutermostReturn()
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
            a.Publish(); Assert.That(a.Calls, Is.Empty);
            a.Publish(); Assert.That(a.Calls, Is.EqualTo(new[] { 4 })); a.VerifyLedger(2); b.VerifyLedger(2);
        }

        [TestCase(false)] [TestCase(true)]
        public void ReservedVersionZeroIsSkipped(bool payload)
        {
            using var f = new EventFixture(payload);
            f.Container.GetField("_version", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, -1);
            var h = f.Subscribe(new Listener()); f.VerifyLedger(1); h.Dispose(); f.VerifyLedger(0);
        }

        [TestCase(false)] [TestCase(true)]
        public void GrowingReservedSlotsPreservesCancellationAndPackedSnapshot(bool payload)
        {
            using var f = new EventFixture(payload);
            int capacity = f.Capacity;
            var next = new Listener(); var victim = new Listener();
            EventRuntimeHandle victimHandle = default;
            bool once = true;
            f.Subscribe(() =>
            {
                if (!once) return; once = false;
                var packed = f.Field("_packedCallbacks");
                LogAssert.Expect(LogType.Warning, new Regex("EventContainer<.*> capacity grew to"));
                f.Subscribe(next);
                Assert.That(f.Capacity, Is.EqualTo(capacity * 2));
                Assert.That(f.Field("_packedCallbacks"), Is.SameAs(packed));
                victimHandle.Dispose(); f.Publish();
                Assert.That(next.Calls, Is.Zero); Assert.That(victim.Calls, Is.Zero);
                Assert.That(f.Count, Is.EqualTo(capacity));
            });
            victimHandle = f.Subscribe(victim);
            for (int i = 2; i < capacity; i++) f.Subscribe(new Listener());
            f.Publish(); f.VerifyLedger(capacity);
            Assert.That(next.Calls, Is.Zero); Assert.That(victim.Calls, Is.Zero);
            f.Publish(); Assert.That(next.Calls, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void InvalidSlotsAndVersionsCannotRemoveCurrentListener(bool payload)
        {
            using var f = new EventFixture(payload);
            var listener = new Listener(); f.Subscribe(listener);
            int id = (int)f.Container.GetField("TypeId", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            int slot = ((int[])f.Field("_packedSlots"))[0];
            int version = ((int[])f.Field("_versions"))[slot];
            new EventRuntimeHandle(id, -1, version).Dispose();
            new EventRuntimeHandle(id, int.MaxValue, version).Dispose();
            new EventRuntimeHandle(id, slot, 0).Dispose();
            new EventRuntimeHandle(id, slot, unchecked(version + 1)).Dispose();
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
            EventDebugRegistry.TryGetDetails(type, out var before, out _);
            EventRuntimeHandle self = default;
            self = f.Subscribe(() =>
            {
                self.Dispose();
                Assert.That(EventDebugRegistry.TryGetDetails(type, out var pending, out var pendingEntries), Is.True);
                Assert.That(pending.SubscriberCount, Is.EqualTo(1));
                Assert.That(pendingEntries.Length, Is.EqualTo(1));
                f.Subscribe(new Listener()); throw new NullReferenceException("debug-fault");
            });
            LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException: debug-fault"));
            f.Publish();
            Assert.That(EventDebugRegistry.TryGetDetails(type, out var after, out var entries), Is.True);
            Assert.That(after.PublishCount - before.PublishCount, Is.EqualTo(1));
            Assert.That(after.HandlerExceptionCount - before.HandlerExceptionCount, Is.EqualTo(1));
            Assert.That(after.SubscribeCount - before.SubscribeCount, Is.EqualTo(2));
            Assert.That(after.UnsubscribeCount - before.UnsubscribeCount, Is.EqualTo(1));
            Assert.That(after.DeferredMutationCount - before.DeferredMutationCount, Is.EqualTo(2));
            Assert.That(after.FlushCount - before.FlushCount, Is.EqualTo(1));
            Assert.That(after.EmptySubscriberCount, Is.EqualTo(payload ? 0 : 1));
            Assert.That(after.InSubscriberCount, Is.EqualTo(payload ? 1 : 0));
            Assert.That(entries.Length, Is.EqualTo(1));
            Assert.That(entries[0].IsParameterless, Is.EqualTo(!payload));
            f.VerifyLedger(1);
        }
    }
}
