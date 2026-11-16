using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;
using UnityEngine;

[assembly: InternalsVisibleTo("AlicizaX.Framework.Timer.Editor.Tests")]

namespace AlicizaX.Timer.Tests
{
    internal sealed class TimerFixture : IDisposable
    {
        internal readonly TimerService Service;
        internal readonly ServiceRegistry Registry = new ServiceRegistry();

        internal TimerFixture(int capacity = 0)
        {
            Service = new TimerService();
            Registry.Register<ITimerService>(Service);
            var handles = new ulong[capacity];
            for (int i = 0; i < handles.Length; i++) handles[i] = Service.AddTimer(TimerProbe.NoOp, 3600);
            for (int i = handles.Length - 1; i >= 0; i--) Service.RemoveTimer(handles[i]);
        }

        internal void Tick(float delta = 0) => ((IServiceTickable)Service).Tick(delta);
        public void Dispose() => Registry.Dispose();
    }

    internal sealed class TimerArg
    {
        internal int Calls;
        internal int Value;
    }

    internal static class TimerProbe
    {
        internal const int IndexBits = 21;
        internal const ulong IndexMask = (1UL << IndexBits) - 1UL;
        internal static readonly TimerHandlerNoArgs NoOp = Empty;
        internal static readonly Action<TimerArg> CountArg = Count;
        internal static int Calls;
        internal static readonly TimerHandlerNoArgs Counter = Increment;
        private static void Empty() { }
        private static void Count(TimerArg arg) { if (arg != null) arg.Calls++; }
        private static void Increment() => Calls++;

        internal static T Read<T>(object target, string field)
            => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);

        internal static object Invoke(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        internal static T[] SlotArray<T>(TimerService service, ulong handle, string field)
            => Read<T[]>(Read<Array>(service, "_pages").GetValue(Slot(handle) >> 8), field);

        internal static int Slot(ulong handle) => (int)(handle & IndexMask) - 1;
        internal static ulong Generation(ulong handle) => handle >> IndexBits;
        internal static int Offset(ulong handle) => Slot(handle) & 255;
        internal static object Wheel(TimerService service, bool unscaled = true) => Read<object>(service, unscaled ? "_unscaledWheel" : "_scaledWheel");
        internal static long Due(TimerService service, ulong handle) => SlotArray<long>(service, handle, "DueTicks")[Offset(handle)];
        internal static long Cursor(TimerService service, bool unscaled = true) => Read<long>(Wheel(service, unscaled), "CursorTick");
        internal static int Queued(TimerService service) => Read<int>(Wheel(service, false), "Count") + Read<int>(Wheel(service), "Count");

        internal static void Statistics(TimerService service, int active, int queue, int pending = 0, int capacity = -1)
        {
            ((ITimerDebugService)service).GetStatistics(out int a, out int c, out int peak, out int free);
            Assert.That(a, Is.EqualTo(active), "active");
            Assert.That(free + a + pending, Is.EqualTo(c), "free + active + executing release pending = capacity");
            Assert.That(Queued(service), Is.EqualTo(queue), "queue");
            Assert.That(peak, Is.GreaterThanOrEqualTo(a));
            if (capacity >= 0) Assert.That(c, Is.EqualTo(capacity));
        }

        internal static void Ledger(TimerService service)
        {
            ((ITimerDebugService)service).GetStatistics(out int active, out int capacity, out _, out int free);
            var membership = new byte[capacity];
            Array pages = Read<Array>(service, "_pages");
            int freeLinked = 0;
            for (int slot = Read<int>(service, "_freeHead"); slot >= 0; slot = Read<int[]>(pages.GetValue(slot >> 8), "NextIndices")[slot & 255])
            {
                Assert.That(slot, Is.InRange(0, capacity - 1));
                Assert.That(membership[slot], Is.Zero, "duplicate free slot");
                membership[slot] = 1;
                freeLinked++;
            }

            Assert.That(freeLinked, Is.EqualTo(free), "free list length");
            int[] pageActiveCounts = Read<int[]>(service, "_pageActiveCounts");
            int pending = 0, running = 0, activeSeen = 0;
            for (int p = 0; p < capacity / 256; p++)
            {
                object page = pages.GetValue(p);
                byte[] states = Read<byte[]>(page, "States");
                ulong[] handles = Read<ulong[]>(page, "Handles");
                int[] queues = Read<int[]>(page, "QueueIndices");
                var invokers = Read<TimerGenericInvoker[]>(page, "Invokers");
                var handlers = Read<object[]>(page, "Handlers");
                var args = Read<object[]>(page, "Args");
                int pageActive = 0;
                for (int i = 0; i < 256; i++)
                {
                    int slot = p * 256 + i;
                    if ((states[i] & 1) != 0)
                    {
                        Assert.That(membership[slot], Is.Zero, "active/free overlap");
                        membership[slot] = 2;
                        activeSeen++;
                        pageActive++;
                        Assert.That(Slot(handles[i]), Is.EqualTo(slot));
                        Assert.That(Generation(handles[i]), Is.Not.Zero);
                        Assert.That(invokers[i] != null || handlers[i] is TimerHandlerNoArgs, Is.True, "callback representation");
                        if (queues[i] >= 0) { Assert.That(states[i] & 2, Is.Not.Zero); running++; }
                    }
                    else
                    {
                        if ((states[i] & 16) != 0) { pending++; Assert.That(membership[slot], Is.Zero); }
                        else Assert.That(membership[slot], Is.EqualTo(1));
                        Assert.That(handles[i], Is.Zero);
                        Assert.That(queues[i], Is.EqualTo(-1));
                        Assert.That(invokers[i], Is.Null);
                        Assert.That(handlers[i], Is.Null);
                        Assert.That(args[i], Is.Null);
                    }
                }

                Assert.That(pageActiveCounts[p], Is.EqualTo(pageActive), "page active count");
            }

            Assert.That(activeSeen, Is.EqualTo(active));
            Assert.That(active + free + pending, Is.EqualTo(capacity));
            int total = 0;
            var linked = new bool[capacity];
            foreach (bool unscaled in new[] { false, true })
            {
                object wheel = Wheel(service, unscaled);
                int count = 0;
                int[] heads = Read<int[]>(wheel, "Heads"), tails = Read<int[]>(wheel, "Tails");
                for (int bucket = 0; bucket < 1024; bucket++)
                {
                    int previous = -1;
                    for (int slot = heads[bucket]; slot >= 0; )
                    {
                        Assert.That(linked[slot], Is.False, "cycle or double insertion");
                        linked[slot] = true;
                        object page = pages.GetValue(slot >> 8);
                        int offset = slot & 255;
                        Assert.That(Read<int[]>(page, "QueueIndices")[offset], Is.EqualTo(bucket));
                        Assert.That(Read<int[]>(page, "QueuePrevIndices")[offset], Is.EqualTo(previous));
                        Assert.That((Read<byte[]>(page, "States")[offset] & 8) != 0, Is.EqualTo(unscaled));
                        previous = slot;
                        slot = Read<int[]>(page, "NextIndices")[offset];
                        count++;
                    }
                    Assert.That(tails[bucket], Is.EqualTo(previous));
                }
                Assert.That(count, Is.EqualTo(Read<int>(wheel, "Count")));
                total += count;
            }
            Assert.That(total, Is.EqualTo(running));
        }

        internal static IEnumerator Until(TimerFixture fixture, Func<bool> done, double timeout = 5)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeout;
            while (!done() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                fixture.Tick();
            }
            Assert.That(done(), Is.True, "real-time timer deadline");
        }

        internal static IEnumerator Wait(double seconds)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < end) yield return null;
        }
    }
}
