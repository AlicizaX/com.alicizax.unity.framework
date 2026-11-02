using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolMaintenanceTests : PoolFixture
    {
        private struct DecayTag { }
        private struct SoftTag { }
        private struct RetainTag { }
        private struct SequentialTag { }
        private struct HardTag { }

        private static void Burst<T>(int count) where T : MemoryObject, new()
        {
            var items = new T[count];
            for (int i = 0; i < count; i++) items[i] = MemoryPool.Acquire<T>();
            for (int i = 0; i < count; i++) MemoryPool.Release(items[i]);
        }

        [Test]
        public void RecentPeakProtectsFreeObjectsForTheWholeWindowThenTrimsAtTheTickBudget()
        {
            Use<FreshItem<DecayTag>>();
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 128, 512, 4, 8f));
            Burst<FreshItem<DecayTag>>(20);
            Tick();
            Assert.That(Info<FreshItem<DecayTag>>().FreeCount, Is.EqualTo(20));
            Assert.That(Info<FreshItem<DecayTag>>().TargetFreeCount, Is.EqualTo(20));
            Assert.That(Info<FreshItem<DecayTag>>().PeakLeasedCount, Is.EqualTo(20));
            for (int i = 0; i < 7; i++)
            {
                Tick(1f);
                Assert.That(Info<FreshItem<DecayTag>>().FreeCount, Is.EqualTo(20), "tick " + i);
            }

            for (int expected = 16; expected >= 0; expected -= 4)
            {
                Tick(1f);
                Assert.That(Info<FreshItem<DecayTag>>().FreeCount, Is.EqualTo(expected));
            }

            Assert.That(Info<FreshItem<DecayTag>>().TargetFreeCount, Is.Zero);
        }

        [Test]
        public void SoftLimitBoundsTheTargetEvenWhenThePeakIsHigher()
        {
            Use<FreshItem<SoftTag>>();
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 8, 512, 64, 8f));
            Burst<FreshItem<SoftTag>>(100);
            Assert.That(Info<FreshItem<SoftTag>>().FreeCount, Is.EqualTo(100));
            Tick();
            Assert.That(Info<FreshItem<SoftTag>>().FreeCount, Is.EqualTo(36));
            Tick();
            Assert.That(Info<FreshItem<SoftTag>>().FreeCount, Is.EqualTo(8));
            Assert.That(Info<FreshItem<SoftTag>>().TargetFreeCount, Is.EqualTo(8));
        }

        [Test]
        public void RetainFloorSurvivesIdleDecay()
        {
            Use<FreshItem<RetainTag>>();
            MemoryPool.SetBudget(new MemoryPoolBudget(5, 128, 512, 64, 8f));
            Burst<FreshItem<RetainTag>>(20);
            Tick();
            for (int i = 0; i < 20; i++) Tick(1f);
            Assert.That(Info<FreshItem<RetainTag>>().FreeCount, Is.EqualTo(5));
            Assert.That(Info<FreshItem<RetainTag>>().TargetFreeCount, Is.EqualTo(5));
        }

        [Test]
        public void SequentialDemandCreatesASingleObject()
        {
            Use<FreshItem<SequentialTag>>();
            int created = Info<FreshItem<SequentialTag>>().CreatedCount;
            for (int frame = 0; frame < 120; frame++)
            {
                for (int i = 0; i < 100; i++) MemoryPool.Release(MemoryPool.Acquire<FreshItem<SequentialTag>>());
                Tick(1f / 60f);
            }

            Assert.That(Info<FreshItem<SequentialTag>>().CreatedCount - created, Is.EqualTo(1));
            Assert.That(Info<FreshItem<SequentialTag>>().FreeCount, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void LoweringTheHardLimitTrimsImmediately()
        {
            Use<FreshItem<HardTag>>();
            Burst<FreshItem<HardTag>>(50);
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 10, 10, 16, 30f));
            Assert.That(Info<FreshItem<HardTag>>().FreeCount, Is.EqualTo(10));
        }

        [Test]
        public void TrimAllEvictsEveryPoolAndLowMemoryKeepsTheRetainFloor()
        {
            Burst<PoolItem>(6);
            Burst<OtherItem>(6);
            MemoryPoolRegistry.TrimAll(3);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(3));
            Assert.That(Info<OtherItem>().FreeCount, Is.EqualTo(3));
            MemoryPool.TrimAll();
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
            Assert.That(Info<OtherItem>().FreeCount, Is.Zero);
        }

        [Test]
        public void EvictionThatReleasesIntoAnotherPoolSchedulesThatPool()
        {
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 0, 512, 16, 8f));
            var other = MemoryPool.Acquire<OtherItem>();
            var item = MemoryPool.Acquire<PoolItem>();
            item.OnEviction = () => MemoryPool.Release(other);
            MemoryPool.Release(item);
            Tick();
            item.OnEviction = null;
            Assert.That(item.Evictions, Is.EqualTo(1));
            Assert.That(Info<OtherItem>().LeasedCount, Is.Zero);
            Tick();
            Assert.That(other.Evictions, Is.EqualTo(1));
            Assert.That(Info<OtherItem>().FreeCount, Is.Zero);
            Assert.That(Info<OtherItem>().LeasedCount, Is.Zero);
        }

        [Test]
        public void EvictionFailureDuringTickPropagatesAndTheNextTickContinues()
        {
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 0, 512, 16, 8f));
            var a = MemoryPool.Acquire<PoolItem>();
            var b = MemoryPool.Acquire<PoolItem>();
            var cause = new InvalidOperationException("evict");
            a.OnEviction = () => throw cause;
            MemoryPool.Release(a);
            MemoryPool.Release(b);
            Assert.That(Assert.Throws<InvalidOperationException>(() => Tick()), Is.SameAs(cause));
            a.OnEviction = null;
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(1));
            Tick();
            Assert.That(b.Evictions, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
        }

        [Test]
        public void RegistryGrowthRegistersAndSchedulesEveryType()
        {
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 0, 512, 16, 8f));
            MethodInfo acquire = typeof(MemoryPool).GetMethod(nameof(MemoryPool.Acquire));
            var pools = new List<MemoryPoolBase>();
            foreach (Type tag in typeof(int).Assembly.GetExportedTypes())
            {
                if (!tag.IsEnum || tag.ContainsGenericParameters) continue;
                Type itemType = typeof(ColdItem<>).MakeGenericType(tag);
                var item = (MemoryObject)acquire.MakeGenericMethod(itemType).Invoke(null, null);
                MemoryPool.Release(item);
                pools.Add(item.Pool);
                if (pools.Count == 130) break;
            }

            Assert.That(pools.Count, Is.EqualTo(130));
            Assert.That(MemoryPool.Count, Is.GreaterThanOrEqualTo(130));
            Tick();
            foreach (MemoryPoolBase pool in pools) Assert.That(pool.GetInfo().FreeCount, Is.Zero, pool.GetInfo().Type.Name);
        }

        [Test]
        public void GetInfosWritesAtMostTheDestinationLength()
        {
            MemoryPool.Release(MemoryPool.Acquire<PoolItem>());
            var infos = new MemoryPoolInfo[MemoryPool.Count + 4];
            int count = MemoryPool.GetInfos(infos);
            Assert.That(count, Is.EqualTo(MemoryPool.Count));
            bool found = false;
            for (int i = 0; i < count; i++)
                if (infos[i].Type == typeof(PoolItem)) found = infos[i].FreeCount == 1;
            Assert.That(found, Is.True);
            Assert.That(MemoryPool.GetInfos(new MemoryPoolInfo[2]), Is.EqualTo(2));
            Assert.That(MemoryPool.GetInfos(Span<MemoryPoolInfo>.Empty), Is.Zero);
        }

        [TestCase(-1, 0, 0, 1, 1f, "retainFreeCount")]
        [TestCase(2, 1, 4, 1, 1f, "softFreeCount")]
        [TestCase(0, 4, 3, 1, 1f, "hardFreeCount")]
        [TestCase(0, 1, 1, 0, 1f, "trimPerTick")]
        [TestCase(0, 1, 1, 1, 0f, "peakWindowSeconds")]
        public void InvalidBudgetIsRejected(int retain, int soft, int hard, int trim, float window, string parameter)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryPoolBudget(retain, soft, hard, trim, window));
            Assert.That(error.ParamName, Is.EqualTo(parameter));
        }
    }
}
