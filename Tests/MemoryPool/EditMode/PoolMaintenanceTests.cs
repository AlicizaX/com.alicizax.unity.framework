using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolMaintenanceTests : PoolFixture
    {
        [Test]
        public void DestroyReleasesStorageRejectsLeasesAndCanBeReactivated()
        {
            var item = MemoryPool<PoolItem>.Acquire();
            Assert.Throws<InvalidOperationException>(() => MemoryPool<PoolItem>.Destroy());

            MemoryPool.Release(item);
            Assert.That(MemoryPool<PoolItem>.Destroy(), Is.True);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<PoolItem>().PageCapacity, Is.Zero);
            Assert.That(MemoryPool<PoolItem>.Destroy(), Is.False);

            MemoryPool.Release(MemoryPool<PoolItem>.Acquire());
            Assert.That(MemoryPool.Count, Is.GreaterThan(0));
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(1));
            MemoryPool<PoolItem>.ClearAll();
        }

        [Test]
        public void RepeatedConstructorFailuresDoNotConsumeSlots()
        {
            var cause = new InvalidOperationException("constructor");
            ConstructorItem.Construct = () => throw cause;
            for (int i = 0; i < 100; i++)
                Assert.That(Assert.Throws<TargetInvocationException>(() => MemoryPool<ConstructorItem>.Acquire()).InnerException, Is.SameAs(cause));
            Assert.That(Info<ConstructorItem>().UsingCount, Is.Zero);
            Assert.That(Info<ConstructorItem>().CreateCount, Is.Zero);
            Assert.That(Info<ConstructorItem>().PageCapacity, Is.Zero);
            ConstructorItem.Construct = null;
            var items = new ConstructorItem[257];
            for (int i = 0; i < items.Length; i++) items[i] = MemoryPool<ConstructorItem>.Acquire();
            foreach (var item in items) MemoryPool.Release(item);
            Assert.That(Info<ConstructorItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void ConstructorReentryCannotMutateItsPool()
        {
            ConstructorItem.Construct = () => Assert.Throws<InvalidOperationException>(() => MemoryPool<ConstructorItem>.Add(1));
            MemoryPool.Release(MemoryPool<ConstructorItem>.Acquire());
            ConstructorItem.Construct = null;
            Assert.That(Info<ConstructorItem>().UnusedCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlobalMaintenanceFromCallbackIsRejectedBeforePartialCleanup(bool evict)
        {
            var other = MemoryPool<OtherItem>.Acquire();
            MemoryPool.Release(other);
            var item = MemoryPool<PoolItem>.Acquire();
            Action callback = () =>
            {
                Assert.Throws<InvalidOperationException>(() => MemoryPool.ClearAll());
                Assert.Throws<InvalidOperationException>(() => MemoryPool.TrimAllNativeMetadata());
                Assert.Throws<InvalidOperationException>(() => MemoryPoolRegistry.TickAll(++Frame));
                int softCapacity = MemoryPool.DefaultSoftFreeReserveLimit;
                int hardCapacity = MemoryPool.DefaultHardFreeReserveLimit;
                Assert.Throws<InvalidOperationException>(() => MemoryPool.SetDefaultCapacity(4, 4));
                Assert.That(MemoryPool.DefaultSoftFreeReserveLimit, Is.EqualTo(softCapacity));
                Assert.That(MemoryPool.DefaultHardFreeReserveLimit, Is.EqualTo(hardCapacity));
                Assert.That(other.Evictions, Is.Zero);
            };
            if (evict) item.OnEviction = callback;
            else item.OnClear = callback;
            MemoryPool.Release(item);
            MemoryPool<PoolItem>.ClearAll();
            Assert.That(other.Evictions, Is.Zero);
        }

        [Test]
        public void ShrinkCompletesRequestedCleanupWhenAnEvictionThrows()
        {
            var items = new PoolItem[12];
            for (int i = 0; i < items.Length; i++) items[i] = MemoryPool<PoolItem>.Acquire();
            var cause = new InvalidOperationException("evict");
            foreach (var item in items)
            {
                item.OnEviction = () => throw cause;
                MemoryPool.Release(item);
            }
            var error = Assert.Throws<AggregateException>(() => MemoryPool<PoolItem>.Shrink(0));
            Assert.That(error.Flatten().InnerExceptions.Count, Is.EqualTo(items.Length));
            foreach (var failure in error.Flatten().InnerExceptions)
            {
                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure.Message, Does.Contain("OnEvict() failed"));
                Assert.That(failure.InnerException, Is.SameAs(cause));
            }
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void ExplicitAddCreatesUpToTheBudgetAndTickCompletesTheRequest()
        {
            MemoryPool<PoolItem>.Add(100);
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(64));
            Tick();
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(100));
            Tick();
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(100), "the requested reserve is kept while the pool is active");
        }

        [Test]
        public void AddIntMaxValueClampsWithoutOverflow()
        {
            MemoryPool<PoolItem>.SetCapacity(64, 64);
            MemoryPool<PoolItem>.Add(int.MaxValue);
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(64));
            Tick(3);
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(64));
        }

        [Test]
        public void ExplicitRemoveCancelsQueuedGrowth()
        {
            MemoryPool<PoolItem>.Add(100);
            MemoryPool<PoolItem>.Shrink(0);
            Tick(100);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
        }

        [Test]
        public void ShrinkEvictsImmediatelyAndPreservesLeases()
        {
            var items = new PoolItem[81];
            for (int i = 0; i < items.Length; i++) items[i] = MemoryPool<PoolItem>.Acquire();
            for (int i = 0; i < 80; i++) MemoryPool.Release(items[i]);

            MemoryPool<PoolItem>.Shrink(0);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<PoolItem>().UsingCount, Is.EqualTo(1));
            Assert.That(items[80].Evictions, Is.Zero);

            MemoryPool.Release(items[80]);
            MemoryPool<PoolItem>.Shrink(0);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
            MemoryPool<PoolItem>.ClearAll();
        }

        [Test]
        public void IdleExpiryReleasesNativeStorageAndStopsScheduling()
        {
            MemoryPool.ShortDecayStartFrames = 1;
            MemoryPool.ZeroFreeReserveStartFrames = 2;
            MemoryPool.UnscheduleIdleFrames = 3;
            MemoryPool.AutoTrimNativeMetadataFrames = 4;
            MemoryPool.Release(MemoryPool<PoolItem>.Acquire());
            Tick(200);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<PoolItem>().PageCapacity, Is.Zero);
            Assert.That(MemoryPool.GetHandle(typeof(PoolItem)).Inner.ActiveIndex, Is.EqualTo(-1));
            Assert.That(typeof(MemoryPool<PoolItem>).GetField("s_PageCapacity", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), Is.EqualTo(0));
            MemoryPool.Release(MemoryPool<PoolItem>.Acquire());
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(1));
        }

        [Test]
        public void TrimPreservesActiveLeaseAndThenReclaimsMetadata()
        {
            var item = MemoryPool<PoolItem>.Acquire();
            var payload = new object();
            item.Payload = payload;
            MemoryPool<PoolItem>.TrimNativeMetadata();
            Assert.That(item.Payload, Is.SameAs(payload));
            Assert.That(Info<PoolItem>().UsingCount, Is.EqualTo(1));
            MemoryPool.Release(item);
            MemoryPool<PoolItem>.TrimNativeMetadata();
            Assert.That(Info<PoolItem>().PageCapacity, Is.Zero);
            Assert.That(item.Evictions, Is.EqualTo(1));
        }

        [Test]
        public void ClearReleasesEveryEmptyPageWhileOtherPagesAreLeased()
        {
            var items = new PoolItem[1000];
            for (int i = 0; i < items.Length; i++) items[i] = MemoryPool<PoolItem>.Acquire();
            for (int i = 1; i < items.Length; i++) MemoryPool.Release(items[i]);
            MemoryPool<PoolItem>.ClearAll();
            Assert.That(Info<PoolItem>().PageCapacity, Is.EqualTo(32));
            MemoryPool.Release(items[0]);
            Assert.That(Info<PoolItem>().PageCapacity, Is.Zero);
        }

        [Test]
        public void PageRetirementAndReuseCannotStrandFreeSlots()
        {
            var held = new PoolItem[129];
            MemoryPool<PoolItem>.SetCapacity(160, 160);
            for (int round = 0; round < 300; round++)
            {
                for (int i = 0; i < held.Length; i++) held[i] = MemoryPool<PoolItem>.Acquire();
                for (int i = 0; i < held.Length; i++) MemoryPool.Release(held[i]);
                MemoryPool<PoolItem>.Shrink(round % 33);
                int available = Info<PoolItem>().UnusedCount;
                int created = Info<PoolItem>().CreateCount;
                for (int i = 0; i < available; i++) held[i] = MemoryPool<PoolItem>.Acquire();
                Assert.That(Info<PoolItem>().CreateCount, Is.EqualTo(created));
                for (int i = 0; i < available; i++) MemoryPool.Release(held[i]);
            }
        }

        [Test]
        public void RegistryGrowthSchedulesEveryTypeWithoutARepairScan()
        {
            var handles = new List<MemoryPoolHandle>();
            Type[] arguments = { typeof(int), typeof(long), typeof(string), typeof(byte), typeof(short), typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(Guid) };
            foreach (Type a in arguments)
            foreach (Type b in arguments)
            {
                Type pair = typeof(KeyValuePair<,>).MakeGenericType(a, b);
                var handle = MemoryPool.GetHandle(typeof(ColdItem<>).MakeGenericType(pair));
                handles.Add(handle);
                handle.Release(handle.Acquire());
            }
            try
            {
                Tick(2);
                foreach (var handle in handles)
                {
                    MemoryPoolInfo info = default;
                    handle.Inner.GetInfo(ref info);
                    Assert.That(info.IdleFrames, Is.EqualTo(1));
                    Assert.That(handle.Inner.ActiveIndex, Is.GreaterThanOrEqualTo(0));
                }
            }
            finally { foreach (var handle in handles) handle.Inner.Clear(); }
        }

        [Test]
        public void InfoBufferIsCallerOwnedAndValidated()
        {
            Assert.Throws<ArgumentNullException>(() => MemoryPool.GetAllMemoryPoolInfos(null));
            Assert.Throws<ArgumentException>(() => MemoryPool.GetAllMemoryPoolInfos(Array.Empty<MemoryPoolInfo>()));
            var buffer = new MemoryPoolInfo[MemoryPool.Count];
            Assert.That(MemoryPool.GetAllMemoryPoolInfos(buffer), Is.EqualTo(buffer.Length));
            foreach (var info in buffer) Assert.That(info.Type, Is.Not.Null);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NonPositiveRemovalDoesNotOverflowOrEvict(int count)
        {
            MemoryPool.Release(MemoryPool<PoolItem>.Acquire());
            MemoryPool.Remove<PoolItem>(count);
            MemoryPool.Remove(typeof(PoolItem), count);
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(1));
            MemoryPool.Remove<PoolItem>(int.MaxValue);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
        }

        [Test]
        public void GlobalClearPreservesSchedulingOfObjectsCreatedByAnotherPoolsEviction()
        {
            // 注册顺序是全局的且会跨测试保留；按已知顺序重新注册两个池，使 PoolItem 先被清理，
            // 从而回调在 ClearAll 已经越过它时触发。回调重新填充的池必须继续安排到下一 tick。
            MemoryPool<PoolItem>.Destroy();
            MemoryPool<OtherItem>.Destroy();
            MemoryPool<PoolItem>.SetCapacity(128, 512);
            MemoryPool<OtherItem>.SetCapacity(128, 512);

            var earlier = MemoryPool.GetHandle(typeof(PoolItem));
            var later = MemoryPool.GetHandle(typeof(OtherItem));
            earlier.Release(earlier.Acquire());
            var trigger = (PoolItem)later.Acquire();
            trigger.OnEviction = () => earlier.Release(earlier.Acquire());
            later.Release(trigger);

            MemoryPool.ClearAll();

            MemoryPoolInfo info = default;
            earlier.Inner.GetInfo(ref info);
            Assert.That(info.UnusedCount, Is.EqualTo(1));
            Assert.That(earlier.Inner.ActiveIndex, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void BatchCleanupReportsEveryCallbackFailure()
        {
            var first = MemoryPool<PoolItem>.Acquire();
            var second = MemoryPool<OtherItem>.Acquire();
            first.OnEviction = () => throw new InvalidOperationException("first");
            second.OnEviction = () => throw new InvalidOperationException("second");
            MemoryPool.Release(first);
            MemoryPool.Release(second);
            var error = Assert.Throws<AggregateException>(() => MemoryPool.ClearAll());
            Assert.That(error.Flatten().InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<OtherItem>().UnusedCount, Is.Zero);
        }

        [Test]
        public void GlobalMaintenanceContinuesWhenOtherPoolsEvictionCallbacksFail()
        {
            Use<ColdItem<PoolMaintenanceTests>>();
            var first = MemoryPool<PoolItem>.Acquire();
            var second = MemoryPool<OtherItem>.Acquire();
            first.OnEviction = () => throw new InvalidOperationException("first");
            second.OnEviction = () => throw new InvalidOperationException("second");
            MemoryPool.Release(first);
            MemoryPool.Release(second);
            MemoryPool.Release(MemoryPool<ColdItem<PoolMaintenanceTests>>.Acquire());
            var error = Assert.Throws<AggregateException>(() => MemoryPool.ClearAll());
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<OtherItem>().UnusedCount, Is.Zero);
            Assert.That(Info<ColdItem<PoolMaintenanceTests>>().UnusedCount, Is.Zero);
            Assert.That(error.Flatten().InnerExceptions.Count, Is.EqualTo(2));
        }
    }
}
