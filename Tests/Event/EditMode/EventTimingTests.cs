using System;
using System.Diagnostics;
using NUnit.Framework;

namespace AlicizaX.EventTests.Editor
{
    public sealed class EventTimingTests
    {
        private struct Empty : IEmptyEventArgs { }
        private readonly struct Payload : IPayloadEventArgs
        {
            public readonly long A,B,C,D,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X,Y,Z,AA,AB,AC,AD,AE,AF;
            public Payload(long n) { A=B=C=D=E=F=G=H=I=J=K=L=M=N=O=P=Q=R=S=T=U=V=W=X=Y=Z=AA=AB=AC=AD=AE=AF=n; }
        }
        private sealed class Target { public long Count; public void Empty() { Count++; } public void Payload(in Payload e) { Count += e.A + e.AF; } }

        [Test]
        public void DefaultPublishTimingsAtBaselineLoads()
        {
            var payload = new Payload(7);
            try
            {
                foreach (bool diagnostics in new[] { true, false })
                {
                    EventDebugRegistry.BenchmarkReleaseLikeMode = !diagnostics;
                    foreach (int count in new[] { 0, 1, 16, 256, 1024 })
                    {
                        EventBus.ReserveEmptyCapacity<Empty>(count);
                        EventBus.ReservePayloadCapacity<Payload>(count);
                        var targets = new Target[count];
                        for (int i = 0; i < count; i++)
                        {
                            var target = targets[i] = new Target();
                            EventBus.Subscribe<Empty>(target.Empty);
                            EventBus.Subscribe<Payload>(target.Payload);
                        }
                        int iterations = Math.Max(2000, 1000000 / Math.Max(1, count));
                        Measure("new-empty-publish", diagnostics, count, iterations, EventBus.Publish<Empty>);
                        Measure("new-payload-publish", diagnostics, count, iterations, () => EventBus.Publish(in payload));
                        foreach (var target in targets) Assert.That(target.Count, Is.EqualTo((1000L + iterations * 7L) * 15));
                        EventBus.ClearEmpty<Empty>(); EventBus.ClearPayload<Payload>();
                    }
                }
            }
            finally { EventBus.ClearEmpty<Empty>(); EventBus.ClearPayload<Payload>(); EventDebugRegistry.BenchmarkReleaseLikeMode = false; }
        }

        private static void Measure(string name, bool diagnostics, int count, int iterations, Action action)
        {
            for (int i = 0; i < 1000; i++) action();
            var samples = new double[7]; var watch = new Stopwatch();
            for (int round = 0; round < samples.Length; round++)
            {
                watch.Restart();
                for (int i = 0; i < iterations; i++) action();
                watch.Stop(); samples[round] = watch.Elapsed.TotalMilliseconds;
            }
            TestContext.WriteLine($"ROUNDS,{name},{diagnostics},{count},{iterations},{string.Join(",", samples)}");
            Array.Sort(samples);
            TestContext.WriteLine($"TIME,{name},{diagnostics},{count},{iterations},{samples[3]:F6},{samples[0]:F6},{samples[6]:F6}");
        }
    }
}
