using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolOwnershipTests : PoolFixture
    {
        [Test]
        public void ReturnClearsPayloadAndReusesIdentity()
        {
            var item = MemoryPool.Acquire<PoolItem>();
            item.Payload = new object();
            MemoryPool.Release(item);
            Assert.That(item.Payload, Is.Null);
            Assert.That(item.Clears, Is.EqualTo(1));
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(item));
            MemoryPool.Release(item);
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(1));
        }

        [Test]
        public void AcquireReturnsTheMostRecentlyReleasedObject()
        {
            var a = MemoryPool.Acquire<PoolItem>();
            var b = MemoryPool.Acquire<PoolItem>();
            MemoryPool.Release(a);
            MemoryPool.Release(b);
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(b));
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(a));
            MemoryPool.Release(a);
            MemoryPool.Release(b);
        }

        [Test]
        public void ConstructorFailureDoesNotCountALease()
        {
            int created = Info<ConstructorItem>().CreatedCount;
            var cause = new InvalidOperationException("constructor");
            ConstructorItem.Construct = () => throw cause;
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => MemoryPool.Acquire<ConstructorItem>());
            Assert.That(error.InnerException, Is.SameAs(cause));
            Assert.That(Info<ConstructorItem>().LeasedCount, Is.Zero);
            Assert.That(Info<ConstructorItem>().CreatedCount, Is.EqualTo(created));
            ConstructorItem.Construct = null;
            MemoryPool.Release(MemoryPool.Acquire<ConstructorItem>());
            Assert.That(Info<ConstructorItem>().CreatedCount, Is.EqualTo(created + 1));
        }

        [Test]
        public void ClearFailurePropagatesAndKeepsTheLeaseForRetry()
        {
            var item = MemoryPool.Acquire<PoolItem>();
            var cause = new InvalidOperationException("clear");
            item.OnClear = () => throw cause;
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(item)), Is.SameAs(cause));
            Assert.That(Info<PoolItem>().LeasedCount, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
            item.OnClear = null;
            MemoryPool.Release(item);
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(1));
        }

        [Test]
        public void DoubleReleaseIsRejectedWithoutCorruptingTheFreeList()
        {
            var item = MemoryPool.Acquire<PoolItem>();
            MemoryPool.Release(item);
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(item)).Message, Does.Contain("already released"));
            Assert.That(item.Clears, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
        }

        [Test]
        public void EvictedObjectCannotBeReleasedAgain()
        {
            var item = MemoryPool.Acquire<PoolItem>();
            MemoryPool.Release(item);
            MemoryPool<PoolItem>.Instance.Trim(0);
            Assert.That(item.Evictions, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(item));
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
        }

        [Test]
        public void ReleaseRoutesThroughTheOwningPoolOfTheRuntimeType()
        {
            PoolItem item = MemoryPool.Acquire<OtherItem>();
            MemoryPool.Release(item);
            Assert.That(Info<OtherItem>().FreeCount, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
        }

        [Test]
        public void CrossPoolNestedOwnershipIsAllowed()
        {
            var child = MemoryPool.Acquire<OtherItem>();
            var parent = MemoryPool.Acquire<PoolItem>();
            parent.OnClear = () => MemoryPool.Release(child);
            MemoryPool.Release(parent);
            parent.OnClear = null;
            Assert.That(Info<OtherItem>().LeasedCount, Is.Zero);
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
        }

        [Test]
        public void SamePoolNestedReleaseDuringClearKeepsBothObjects()
        {
            var child = MemoryPool.Acquire<PoolItem>();
            var parent = MemoryPool.Acquire<PoolItem>();
            parent.OnClear = () => MemoryPool.Release(child);
            MemoryPool.Release(parent);
            parent.OnClear = null;
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(2));
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(parent));
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(child));
            MemoryPool.Release(child);
            MemoryPool.Release(parent);
        }

        [Test]
        public void AcquireDuringClearOfTheSamePoolIsAllowed()
        {
            PoolItem nested = null;
            var item = MemoryPool.Acquire<PoolItem>();
            item.OnClear = () => nested = MemoryPool.Acquire<PoolItem>();
            MemoryPool.Release(item);
            item.OnClear = null;
            Assert.That(nested, Is.Not.SameAs(item));
            Assert.That(Info<PoolItem>().LeasedCount, Is.EqualTo(1));
            MemoryPool.Release(nested);
        }

        [Test]
        public void WorkerThreadIsRejected()
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { MemoryPool.Acquire<PoolItem>(); }
                catch (Exception e) { error = e; }
            });
            thread.Start();
            Assert.That(thread.Join(5000), Is.True);
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(33)]
        [TestCase(4097)]
        public void HardCapEvictsOnReturnWithoutGrowingTheFreeList(int count)
        {
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 4, 4, 16, 30f));
            var items = new PoolItem[count];
            for (int i = 0; i < count; i++) items[i] = MemoryPool.Acquire<PoolItem>();
            for (int i = 0; i < count; i++) MemoryPool.Release(items[i]);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(Math.Min(4, count)));
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
            int evicted = 0;
            foreach (var item in items) evicted += item.Evictions;
            Assert.That(evicted, Is.EqualTo(Math.Max(0, count - 4)));
        }

        [Test]
        public void TrimEvictsTheOldestFreeObjectsFirst()
        {
            var a = MemoryPool.Acquire<PoolItem>();
            var b = MemoryPool.Acquire<PoolItem>();
            var c = MemoryPool.Acquire<PoolItem>();
            MemoryPool.Release(a);
            MemoryPool.Release(b);
            MemoryPool.Release(c);
            MemoryPool<PoolItem>.Instance.Trim(1);
            Assert.That(a.Evictions, Is.EqualTo(1));
            Assert.That(b.Evictions, Is.EqualTo(1));
            Assert.That(c.Evictions, Is.Zero);
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(c));
            MemoryPool.Release(c);
        }

        [Test]
        public void EvictionFailurePropagatesAndKeepsTheRemainingFreeObjects()
        {
            var a = MemoryPool.Acquire<PoolItem>();
            var b = MemoryPool.Acquire<PoolItem>();
            var c = MemoryPool.Acquire<PoolItem>();
            var cause = new InvalidOperationException("evict");
            a.OnEviction = () => throw cause;
            MemoryPool.Release(a);
            MemoryPool.Release(b);
            MemoryPool.Release(c);
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool<PoolItem>.Instance.Trim(0)), Is.SameAs(cause));
            a.OnEviction = null;
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(2));
            MemoryPool<PoolItem>.Instance.Trim(0);
            Assert.That(b.Evictions, Is.EqualTo(1));
            Assert.That(c.Evictions, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
        }

        [Test]
        public void RandomLifetimesKeepTheOwnershipLedgerBalanced()
        {
            MemoryPool.SetBudget(new MemoryPoolBudget(2, 16, 64, 4, 1f));
            var random = new Random(314159);
            var leased = new List<PoolItem>();
            var seen = new HashSet<PoolItem>();
            int created = Info<PoolItem>().CreatedCount;
            for (int i = 0; i < 20000; i++)
            {
                if (leased.Count == 0 || random.Next(100) < 55)
                {
                    var item = MemoryPool.Acquire<PoolItem>();
                    Assert.That(leased.Contains(item), Is.False);
                    leased.Add(item);
                    seen.Add(item);
                }
                else
                {
                    int index = random.Next(leased.Count);
                    MemoryPool.Release(leased[index]);
                    leased.RemoveAt(index);
                }

                if (i % 113 == 0) MemoryPool<PoolItem>.Instance.Trim(3);
                if (i % 17 == 0) Tick(0.05f);
                MemoryPoolInfo info = Info<PoolItem>();
                Assert.That(info.LeasedCount, Is.EqualTo(leased.Count));
                Assert.That(info.FreeCount, Is.LessThanOrEqualTo(64));
            }

            foreach (var item in leased) MemoryPool.Release(item);
            int evicted = 0;
            foreach (var item in seen) evicted += item.Evictions;
            Assert.That(Info<PoolItem>().CreatedCount - created, Is.EqualTo(Info<PoolItem>().FreeCount + evicted));
            MemoryPool<PoolItem>.Instance.Trim(0);
            Assert.That(Info<PoolItem>().LeasedCount, Is.Zero);
            Assert.That(Info<PoolItem>().FreeCount, Is.Zero);
        }
    }
}
