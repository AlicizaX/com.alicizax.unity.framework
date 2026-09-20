using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.EventTests.Editor
{
    public sealed class EventContractTests
    {
        private struct Empty : IEmptyEventArgs { }
        private readonly struct Payload : IPayloadEventArgs { public readonly int Value; public Payload(int value) { Value = value; } }
        private sealed class Counter { public int Calls; public void Hit() { Calls++; } }

        [TearDown]
        public void Clean() { EventBus.ClearEmpty<Empty>(); EventBus.ClearPayload<Payload>(); }

        [TestCase(false)]
        [TestCase(true)]
        public void MiddleNullReferenceDoesNotStopRemainingListenersOrNextPublish(bool payload)
        {
            var calls = new List<int>();
            if (payload)
            {
                EventBus.Subscribe<Payload>((in Payload e) => calls.Add(e.Value));
                EventBus.Subscribe<Payload>((in Payload e) => throw new NullReferenceException("event-null-ui"));
                EventBus.Subscribe<Payload>((in Payload e) => calls.Add(e.Value + 10));
                for (int i = 0; i < 2; i++)
                {
                    LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException: event-null-ui"));
                    var e = new Payload(i);
                    Assert.DoesNotThrow(() => EventBus.Publish(in e));
                }
                Assert.That(calls, Is.EqualTo(new[] { 0, 10, 1, 11 }));
            }
            else
            {
                EventBus.Subscribe<Empty>(() => calls.Add(1));
                EventBus.Subscribe<Empty>(() => throw new NullReferenceException("event-null-ui"));
                EventBus.Subscribe<Empty>(() => calls.Add(3));
                EventBus.Subscribe<Empty>(() => calls.Add(4));
                for (int i = 0; i < 2; i++)
                {
                    LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException: event-null-ui"));
                    Assert.DoesNotThrow(EventBus.Publish<Empty>);
                }
                Assert.That(calls, Is.EqualTo(new[] { 1, 3, 4, 1, 3, 4 }));
            }
        }

        [Test]
        public void DisposedUpcomingListenerIsSkippedAndPendingSubscriptionWaits()
        {
            var calls = new List<int>();
            EventRuntimeHandle upcoming = default;
            bool first = true;
            EventBus.Subscribe<Empty>(() =>
            {
                calls.Add(1);
                if (!first) return;
                first = false;
                upcoming.Dispose();
                EventBus.Subscribe<Empty>(() => calls.Add(3));
                Assert.That(EventBus.GetEmptySubscriberCount<Empty>(), Is.EqualTo(2));
            });
            upcoming = EventBus.Subscribe<Empty>(() => calls.Add(2));
            EventBus.Publish<Empty>();
            Assert.That(calls, Is.EqualTo(new[] { 1 }));
            EventBus.Publish<Empty>();
            Assert.That(calls, Is.EqualTo(new[] { 1, 1, 3 }));
        }

        [Test]
        public void DuplicateSubscriptionReturnsDefaultWithoutOwningOriginal()
        {
            var target = new Counter();
            var original = EventBus.Subscribe<Empty>(target.Hit);
            LogAssert.Expect(LogType.Warning, new Regex("Duplicate event handler subscription"));
            var duplicate = EventBus.Subscribe<Empty>(target.Hit);
            Assert.That(duplicate, Is.EqualTo(default(EventRuntimeHandle)));
            duplicate.Dispose();
            EventBus.Publish<Empty>();
            Assert.That(target.Calls, Is.EqualTo(1));
            original.Dispose();
            Assert.That(EventBus.GetEmptySubscriberCount<Empty>(), Is.Zero);
        }

        [Test]
        public void OnlyEventBusDispatchAndOneContainerSourceRemain()
        {
            var assembly = typeof(EventBus).Assembly;
            Assert.That(assembly.GetType("AlicizaX.SafePublisher"), Is.Null);
            Assert.That(assembly.GetType("AlicizaX.EmptyEventContainer`1"), Is.Null);
            Assert.That(assembly.GetType("AlicizaX.EventContainer`1"), Is.Null);
            Assert.That(assembly.GetType("AlicizaX.EventContainer`2").IsPublic, Is.False);
            Assert.That(typeof(EventBus).GetMethods().Any(m => m.Name == "SafePublish"), Is.False);
            Assert.That(typeof(EventBus).GetMethods().Any(m => m.Name.StartsWith("Ensure")), Is.False);
        }
    }
}
