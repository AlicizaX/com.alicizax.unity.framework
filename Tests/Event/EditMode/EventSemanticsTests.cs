using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.EventTests.Editor
{
    public sealed class EventSemanticsTests
    {
        private struct GrowthEvent : IEmptyEventArgs { }

        [TestCase(false)] [TestCase(true)]
        public void ClearInsidePublishStopsRemainingHandlers(bool payload)
        {
            using var f = new EventFixture(payload);
            var later = new Listener();
            f.Subscribe(() => f.Clear());
            f.Subscribe(later);
            f.Publish();
            Assert.That(later.Calls, Is.Zero);
            Assert.That(f.Count, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void SubscriberCountDuringPublishIsLive(bool payload)
        {
            using var f = new EventFixture(payload);
            EventRuntimeHandle other = default;
            int observed = -1;
            bool once = true;
            f.Subscribe(() =>
            {
                if (!once) return; once = false;
                other.Dispose();
                f.Subscribe(new Listener()); f.Subscribe(new Listener());
                observed = f.Count;
            });
            other = f.Subscribe(new Listener());
            f.Publish();
            Assert.That(observed, Is.EqualTo(3));
            Assert.That(f.Count, Is.EqualTo(3));
        }

        [TestCase(false)] [TestCase(true)]
        public void SubscriptionMadeDuringPublishReceivesNestedPublish(bool payload)
        {
            using var f = new EventFixture(payload);
            var added = new Listener();
            bool once = true;
            f.Subscribe(() =>
            {
                if (!once) return; once = false;
                f.Subscribe(added);
                f.Publish();
            });
            f.Publish();
            Assert.That(added.Calls, Is.EqualTo(1));
            f.Publish();
            Assert.That(added.Calls, Is.EqualTo(2));
        }

        [TestCase(false)] [TestCase(true)]
        public void DebugSubscribersDuringPublishExcludeDisposedHandlers(bool payload)
        {
            using var f = new EventFixture(payload);
            Type type = payload ? typeof(PayloadProbe) : typeof(EmptyProbe);
            EventRuntimeHandle victim = default;
            int listed = -1;
            f.Subscribe(() =>
            {
                victim.Dispose();
                EventDebugRegistry.TryGetDetails(type, out _, out var subscribers);
                listed = subscribers.Length;
            });
            victim = f.Subscribe(new Listener());
            f.Publish();
            Assert.That(listed, Is.EqualTo(1));
        }

        [Test]
        public void GrowthBeyondCapacityDoesNotWarn()
        {
            EventBus.ClearEmpty<GrowthEvent>();
            var listeners = new Listener[33];
            var warnings = new List<string>();
            Application.LogCallback capture = (condition, _, type) => { if (type == LogType.Warning) warnings.Add(condition); };
            Application.logMessageReceived += capture;
            try
            {
                for (int i = 0; i < listeners.Length; i++)
                {
                    listeners[i] = new Listener();
                    EventBus.Subscribe<GrowthEvent>(listeners[i].OnEmpty);
                }
                Assert.That(EventBus.GetEmptySubscriberCount<GrowthEvent>(), Is.EqualTo(listeners.Length));
            }
            finally
            {
                Application.logMessageReceived -= capture;
                EventBus.ClearEmpty<GrowthEvent>();
            }
            Assert.That(warnings, Is.Empty);
        }

        [TestCase(false)] [TestCase(true)]
        public void SubsystemRegistrationResetsSubscriptionsAndInvalidatesHandles(bool payload)
        {
            using var f = new EventFixture(payload);
            var stale = new Listener(); var fresh = new Listener();
            EventRuntimeHandle staleHandle = f.Subscribe(stale);
            InvokeSubsystemRegistration();
            Assert.That(f.Count, Is.Zero);
            f.Subscribe(fresh);
            staleHandle.Dispose();
            f.Publish();
            Assert.That(stale.Calls, Is.Zero);
            Assert.That(fresh.Calls, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void GrowthDuringPublishStillSkipsLaterDisposedHandlers(bool payload)
        {
            using var f = new EventFixture(payload);
            int capacity = f.Capacity;
            var victim = new Listener();
            EventRuntimeHandle victimHandle = default;
            bool once = true;
            f.Subscribe(() =>
            {
                if (!once) return; once = false;
                for (int i = 0; i < capacity; i++) f.Subscribe(new Listener());
                victimHandle.Dispose();
            });
            victimHandle = f.Subscribe(victim);
            for (int i = 2; i < capacity; i++) f.Subscribe(new Listener());
            f.Publish();
            Assert.That(victim.Calls, Is.Zero);
            f.VerifyLedger(capacity * 2 - 1);
        }

        [TestCase(false)] [TestCase(true)]
        public void ExceptionInsideNestedPublishKeepsBothLevelsConsistent(bool payload)
        {
            using var f = new EventFixture(payload);
            var tail = new Listener();
            EventRuntimeHandle tailHandle = default;
            int depth = 0;
            f.Subscribe(() =>
            {
                if (depth++ != 0) throw new InvalidOperationException("nested-fault");
                f.Publish();
                tailHandle.Dispose();
            });
            tailHandle = f.Subscribe(tail);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: nested-fault"));
            f.Publish();
            Assert.That(tail.Calls, Is.EqualTo(1));
            f.VerifyLedger(1);
        }

        private static void InvokeSubsystemRegistration()
        {
            foreach (Type type in typeof(EventBus).Assembly.GetTypes())
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attribute != null && attribute.loadType == RuntimeInitializeLoadType.SubsystemRegistration) method.Invoke(null, null);
            }
        }
    }
}
