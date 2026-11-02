#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceValueTypePerformanceTests
    {
        private const int Count = 2000000;

        [UnityTest]
        public IEnumerator HandleReadsDoNotAllocate()
        {
            using var f = new ResourceFixture();
            f.Text("value-copy");
            var lease = f.Service.LoadLease<TextAsset>("value-copy");
            var scope = f.Service.CreateScope();
            const int count = 20000;
            long sum = 0;
            yield return AllocationCapture.Measure("lease-read-in", count, () =>
            {
                for (int i = 0; i < count; i++) sum += ReadLease(in lease);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("scope-read-value", count, () =>
            {
                for (int i = 0; i < count; i++) sum += ReadScope(scope);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("scope-read-in", count, () =>
            {
                for (int i = 0; i < count; i++) sum += ReadScopeIn(in scope);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(sum, Is.GreaterThan(0));
            lease.Dispose();
            scope.Dispose();
        }

        [Test]
        public void PassingTheScopeByReferenceIsNotSlowerThanByValue()
        {
            using var f = new ResourceFixture();
            var scope = f.Service.CreateScope();
            long sum = 0;
            sum += ByValue(scope, 1000) + ByReference(in scope, 1000);
            double value = double.MaxValue;
            double reference = double.MaxValue;
            for (int round = 0; round < 7; round++)
            {
                var clock = Stopwatch.StartNew();
                sum += ByValue(scope, Count);
                value = Math.Min(value, clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                sum += ByReference(in scope, Count);
                reference = Math.Min(reference, clock.Elapsed.TotalMilliseconds);
            }
            TestContext.WriteLine($"STRUCT-PASS,ResourceScope,value={value:F3}ms,in={reference:F3}ms,count={Count}");
            Assert.That(sum, Is.Not.Zero);
            Assert.That(reference, Is.LessThanOrEqualTo(value * 1.10 + 0.5), "Scope entry points take in because by-value passing measured slower.");
            scope.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ByValue(ResourceScope scope, int count)
        {
            long sum = 0;
            for (int i = 0; i < count; i++) sum += ReadScope(scope);
            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ByReference(in ResourceScope scope, int count)
        {
            long sum = 0;
            for (int i = 0; i < count; i++) sum += ReadScopeIn(in scope);
            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadLease(in AssetLease<TextAsset> lease)
            => lease.Asset == null ? 0 : 1;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadScope(ResourceScope scope)
            => scope.Index + (int)scope.Generation;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadScopeIn(in ResourceScope scope)
            => scope.Index + (int)scope.Generation;
    }
}
#endif
