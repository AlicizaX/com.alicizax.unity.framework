using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
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
        private readonly EventLedger ledger;
        public EventFixture(bool payload)
        {
            Payload = payload;
            ledger = new EventLedger(Channel);
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
        public object Channel => Payload ? EventContainer<PayloadProbe, InEventHandler<PayloadProbe>>.Channel : EventContainer<EmptyProbe, Action>.Channel;
        public object Field(string name) => ledger.Field(name);
        public int Capacity => ledger.Capacity;
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
            ledger.Verify(expected);
        }
        public void Dispose() { Clear(); VerifyLedger(0); }
    }
    internal sealed class EventLedger
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object channel;
        private int[] slotWords = Array.Empty<int>();
        private bool[] seen = Array.Empty<bool>();
        public EventLedger(object channel) { this.channel = channel; }
        public object Field(string name) => channel.GetType().GetField(name, InstanceFields).GetValue(channel);
        public int Capacity => ((Array)Field("Handlers")).Length;
        public void Verify(int expected)
        {
            Assert.That((int)Field("_publishDepth"), Is.Zero);
            Assert.That((int)Field("_holeCount"), Is.Zero);
            Assert.That((int)Field("_count"), Is.EqualTo(expected));
            var handlers = (Delegate[])Field("Handlers");
            var handlerSlots = (int[])Field("_handlerSlots");
            var slots = (Array)Field("_slots");
            Assert.That(handlerSlots.Length, Is.EqualTo(handlers.Length));
            Assert.That(slots.Length, Is.EqualTo(handlers.Length));
            ReadSlotWords(slots);
            if (seen.Length != slots.Length) seen = new bool[slots.Length];
            else Array.Clear(seen, 0, seen.Length);
            for (int i = 0; i < expected; i++)
            {
                int slot = handlerSlots[i];
                if (handlers[i] == null || (uint)slot >= (uint)seen.Length || seen[slot] || slotWords[slot * 2] != i)
                    Assert.Fail($"live invariant at {i}");
                seen[slot] = true;
            }
            for (int i = expected; i < handlers.Length; i++)
                if (handlers[i] != null) Assert.Fail($"retained delegate at {i}");
            int free = 0;
            for (int slot = (int)Field("_freeSlot"); slot != -1; slot = slotWords[slot * 2])
            {
                if ((uint)slot >= (uint)seen.Length || seen[slot]) Assert.Fail($"free invariant at {slot}");
                seen[slot] = true;
                free++;
            }
            Assert.That(expected + free, Is.EqualTo(slots.Length));
            object subscribed = Field("_subscribedHandlers");
            Assert.That((int)subscribed.GetType().GetProperty("Count").GetValue(subscribed), Is.EqualTo(expected));
        }
        private void ReadSlotWords(Array slots)
        {
            Type slotType = slots.GetType().GetElementType();
            Assert.That(Marshal.SizeOf(slotType), Is.EqualTo(8));
            Assert.That((int)Marshal.OffsetOf(slotType, "Index"), Is.Zero);
            Assert.That((int)Marshal.OffsetOf(slotType, "Version"), Is.EqualTo(4));
            if (slotWords.Length != slots.Length * 2) slotWords = new int[slots.Length * 2];
            if (slots.Length == 0) return;
            GCHandle handle = GCHandle.Alloc(slots, GCHandleType.Pinned);
            try { Marshal.Copy(handle.AddrOfPinnedObject(), slotWords, 0, slotWords.Length); }
            finally { handle.Free(); }
        }
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
