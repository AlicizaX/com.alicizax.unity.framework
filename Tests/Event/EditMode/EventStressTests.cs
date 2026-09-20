using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.EventTests.Editor
{
    public sealed class EventStressTests
    {
        private struct TypeEvent<T> : IEmptyEventArgs { }
        private struct TypeKey<T, U> { }
        private static int calls;
        private static void Hit() { calls++; }

        [TestCase(32)] [TestCase(128)]
        public void RegistryGrowthRoutesEveryTypeToItsOwnContainer(int count)
        {
            var types = new Type[count]; var handles = new EventRuntimeHandle[count];
            var subscribe = typeof(EventBus).GetMethods().Single(m => m.Name == "Subscribe" && m.GetParameters()[0].ParameterType == typeof(Action));
            var publish = typeof(EventBus).GetMethods().Single(m => m.Name == "Publish" && m.GetParameters().Length == 0);
            Type[] keys = { typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(object), typeof(bool), typeof(char), typeof(uint), typeof(ulong), typeof(ushort), typeof(sbyte), typeof(DateTime) };
            calls = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Type key = typeof(TypeKey<,>).MakeGenericType(keys[i / keys.Length], keys[i % keys.Length]);
                    types[i] = typeof(TypeEvent<>).MakeGenericType(key);
                    handles[i] = (EventRuntimeHandle)subscribe.MakeGenericMethod(types[i]).Invoke(null, new object[] { (Action)Hit });
                }
                for (int i = 0; i < count; i += 2) { handles[i].Dispose(); handles[i].Dispose(); }
                foreach (var type in types) publish.MakeGenericMethod(type).Invoke(null, null);
                Assert.That(calls, Is.EqualTo(count / 2));
                int nextId = (int)typeof(UnsubscribeRegistry).GetField("_nextId", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                new EventRuntimeHandle(nextId, 0, 1).Dispose();
                foreach (var h in handles) h.Dispose();
                foreach (var type in types) publish.MakeGenericMethod(type).Invoke(null, null);
                Assert.That(calls, Is.EqualTo(count / 2));
                TestContext.WriteLine($"REGISTRY,{count},{nextId}");
            }
            finally { foreach (var h in handles) h.Dispose(); }
        }

        [TestCase(false, 64)] [TestCase(true, 64)]
        [TestCase(false, 1024)] [TestCase(true, 1024)]
        [TestCase(false, 10000)] [TestCase(true, 10000)]
        public void LargePendingQueueAndCapacityGrowthConverge(bool payload, int count)
        {
            using var f = new EventFixture(payload);
            f.Reserve(count + 1);
            var listeners = new Listener[count]; var handles = new EventRuntimeHandle[count];
            for (int i = 0; i < count; i++) listeners[i] = new Listener();
            EventRuntimeHandle driver = default;
            driver = f.Subscribe(() =>
            {
                driver.Dispose();
                for (int i = 0; i < count; i++) handles[i] = f.Subscribe(listeners[i]);
                for (int i = 0; i < count; i += 2) handles[i].Dispose();
                Assert.That(f.Count, Is.EqualTo(1));
                TestContext.WriteLine($"PENDING,{payload},{count},{f.Field("_pendingCount")}");
            });
            var watch = Stopwatch.StartNew(); f.Publish(); watch.Stop();
            f.VerifyLedger(count / 2); f.Publish(29);
            for (int i = 0; i < count; i++) Assert.That(listeners[i].Calls, Is.EqualTo(i % 2 == 0 ? 0 : 1));
            foreach (var h in handles) h.Dispose(); f.VerifyLedger(0);
            TestContext.WriteLine($"STRESS,{payload},{count},{watch.Elapsed.TotalMilliseconds:F4},{f.Capacity}");
        }

        [TestCase(false)] [TestCase(true)]
        public void EveryThrowingListenerLogsAndLeavesLedgerDisposable(bool payload)
        {
            using var f = new EventFixture(payload);
            f.Reserve(65);
            var handles = new EventRuntimeHandle[64];
            for (int i = 0; i < handles.Length; i++)
            {
                int id = i;
                handles[i] = f.Subscribe(() => throw new InvalidOperationException("event-fault-" + id));
            }
            var healthy = new Listener(); f.Subscribe(healthy);
            foreach (int publish in new[] { 0, 1 })
            {
                for (int i = 0; i < handles.Length; i++) LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: event-fault-" + i + "(?:\\r?\\n|$)"));
                f.Publish(); Assert.That(healthy.Calls, Is.EqualTo(publish + 1));
            }
            foreach (var h in handles) h.Dispose(); f.VerifyLedger(1); f.Publish(); Assert.That(healthy.Calls, Is.EqualTo(3));
        }
    }
}
