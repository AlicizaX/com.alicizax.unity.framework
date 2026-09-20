using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace AlicizaX.EventTests
{
    public struct EmptyProbe : IEmptyEventArgs { }
    public readonly struct PayloadProbe : IPayloadEventArgs
    {
        public readonly int Value;
        public PayloadProbe(int value) { Value = value; }
    }
    public sealed class EventFixture : IDisposable
    {
        public readonly bool Payload;
        public readonly List<int> Calls = new List<int>();
        public EventFixture(bool payload)
        {
            Payload = payload;
            Clear(); Reserve(64);
        }
        public EventRuntimeHandle Subscribe(Action action)
            => Payload ? EventBus.Subscribe<PayloadProbe>((in PayloadProbe e) => action()) : EventBus.Subscribe<EmptyProbe>(action);
        public EventRuntimeHandle Subscribe(Listener target)
            => Payload ? EventBus.Subscribe<PayloadProbe>(target.OnPayload) : EventBus.Subscribe<EmptyProbe>(target.OnEmpty);
        public void Publish(int value = 7)
        {
            if (Payload) { var e = new PayloadProbe(value); EventBus.Publish(in e); }
            else EventBus.Publish<EmptyProbe>();
        }
        public int Count => Payload ? EventBus.GetPayloadSubscriberCount<PayloadProbe>() : EventBus.GetEmptySubscriberCount<EmptyProbe>();
        public Type Container => typeof(EventBus).Assembly.GetType("AlicizaX.EventContainer`2").MakeGenericType(
            Payload ? typeof(PayloadProbe) : typeof(EmptyProbe), Payload ? typeof(InEventHandler<PayloadProbe>) : typeof(Action));
        public object Field(string name) => Container.GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        public int Capacity => ((Array)Field("_callbacks")).Length;
        public void Reserve(int capacity)
        {
            if (Payload) EventBus.ReservePayloadCapacity<PayloadProbe>(capacity);
            else EventBus.ReserveEmptyCapacity<EmptyProbe>(capacity);
        }
        public void Clear()
        {
            if (Payload) EventBus.ClearPayload<PayloadProbe>(); else EventBus.ClearEmpty<EmptyProbe>();
        }
        public void VerifyLedger(int expected)
        {
            Assert.That(Count, Is.EqualTo(expected));
            Assert.That((int)Field("_publishDepth"), Is.Zero);
            Assert.That((int)Field("_pendingCount"), Is.Zero);
            var callbacks = (Array)Field("_callbacks");
            var versions = (int[])Field("_versions");
            var cancelled = (bool[])Field("_cancelled");
            var indices = (int[])Field("_packedIndices");
            var packed = (int[])Field("_packedSlots");
            var packedCallbacks = (Array)Field("_packedCallbacks");
            var free = (int[])Field("_freeSlots");
            int freeCount = (int)Field("_freeCount");
            var slots = new HashSet<int>();
            for (int i = 0; i < expected; i++)
            {
                int slot = packed[i];
                Assert.That(slots.Add(slot), Is.True);
                Assert.That(indices[slot], Is.EqualTo(i));
                Assert.That(versions[slot], Is.Not.Zero);
                Assert.That(cancelled[slot], Is.False);
                Assert.That(callbacks.GetValue(slot), Is.SameAs(packedCallbacks.GetValue(i)));
            }
            for (int i = 0; i < freeCount; i++)
            {
                int slot = free[i];
                Assert.That(slots.Add(slot), Is.True);
                Assert.That(versions[slot], Is.Zero);
                Assert.That(callbacks.GetValue(slot), Is.Null);
            }
            for (int i = expected; i < packedCallbacks.Length; i++) Assert.That(packedCallbacks.GetValue(i), Is.Null);
            Assert.That(slots.Count, Is.EqualTo(callbacks.Length));
            var subscriptions = Field("Subscriptions");
            Assert.That((int)subscriptions.GetType().GetProperty("Count").GetValue(subscriptions), Is.EqualTo(expected));
        }
        public void Dispose() { Clear(); VerifyLedger(0); }
    }
    public sealed class Listener
    {
        public int Calls;
        public long Sum;
        public Action Action;
        public void OnEmpty() { Calls++; Action?.Invoke(); }
        public void OnPayload(in PayloadProbe e) { Calls++; Sum += e.Value; Action?.Invoke(); }
    }
}
