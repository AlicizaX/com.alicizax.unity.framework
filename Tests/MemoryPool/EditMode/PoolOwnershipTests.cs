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
            var item = MemoryPool<PoolItem>.Acquire();
            item.Payload = new object();
            MemoryPool.Release(item);
            Assert.That(item.Payload, Is.Null);
            Assert.That(item.Clears, Is.EqualTo(1));
            Assert.That(MemoryPool<PoolItem>.Acquire(), Is.SameAs(item));
            MemoryPool.Release(item);
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void ConstructorFailureDoesNotAcquireOwnership()
        {
            var cause = new InvalidOperationException("constructor");
            ConstructorItem.Construct = () => throw cause;
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => MemoryPool<ConstructorItem>.Acquire());
            Assert.That(error.InnerException, Is.SameAs(cause));
            Assert.That(Info<ConstructorItem>().UsingCount, Is.Zero);
            Assert.That(Info<ConstructorItem>().AcquireCount, Is.Zero);
            ConstructorItem.Construct = null;
            var item = MemoryPool<ConstructorItem>.Acquire();
            MemoryPool.Release(item);
        }

        [Test]
        public void ClearFailureKeepsLeaseForRetry()
        {
            var item = MemoryPool<PoolItem>.Acquire();
            var cause = new InvalidOperationException("clear");
            item.OnClear = () => throw cause;
            var error = Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(item));
            Assert.That(error.Message, Does.Contain("Clear() failed"));
            Assert.That(error.InnerException, Is.SameAs(cause));
            Assert.That(Info<PoolItem>().UsingCount, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            item.OnClear = null;
            MemoryPool.Release(item);
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void EvictionFailureStillRetiresEveryFreeObject()
        {
            var a = MemoryPool<PoolItem>.Acquire();
            var b = MemoryPool<PoolItem>.Acquire();
            var cause = new InvalidOperationException("evict");
            a.OnEviction = () => throw cause;
            MemoryPool.Release(a);
            MemoryPool.Release(b);
            var error = Assert.Throws<InvalidOperationException>(() => MemoryPool<PoolItem>.ClearAll());
            Assert.That(error.Message, Does.Contain("OnEvict() failed"));
            Assert.That(error.InnerException, Is.SameAs(cause));
            Assert.That(b.Evictions, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void ClearRetiresOldLeasesWithoutEvictingNewGeneration()
        {
            var old = MemoryPool<PoolItem>.Acquire();
            MemoryPool<PoolItem>.ClearAll();
            var current = MemoryPool<PoolItem>.Acquire();
            MemoryPool.Release(current);
            MemoryPool.Release(old);
            Assert.That(old.Evictions, Is.EqualTo(1));
            Assert.That(current.Evictions, Is.Zero);
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(1));
        }

        [TestCase("Acquire")]
        [TestCase("Add")]
        [TestCase("Shrink")]
        [TestCase("Compact")]
        [TestCase("SetCapacity")]
        [TestCase("ClearAll")]
        [TestCase("Trim")]
        [TestCase("ResetStats")]
        public void SamePoolMutationDuringClearIsRejected(string operation)
        {
            Action mutation = operation switch
            {
                "Acquire" => () => MemoryPool<PoolItem>.Acquire(),
                "Add" => () => MemoryPool<PoolItem>.Add(1),
                "Shrink" => () => MemoryPool<PoolItem>.Shrink(0),
                "Compact" => () => MemoryPool<PoolItem>.Compact(),
                "SetCapacity" => () => MemoryPool<PoolItem>.SetCapacity(4, 4),
                "ClearAll" => () => MemoryPool<PoolItem>.ClearAll(),
                "Trim" => () => MemoryPool<PoolItem>.TrimNativeMetadata(),
                _ => () => MemoryPool<PoolItem>.ResetStats()
            };
            var item = MemoryPool<PoolItem>.Acquire();
            bool rejected = false;
            item.OnClear = () =>
            {
                try { mutation(); }
                catch (InvalidOperationException) { rejected = true; }
            };
            MemoryPool.Release(item);
            item.OnClear = null;
            Assert.That(rejected, Is.True, operation);
        }

        [Test]
        public void CrossPoolNestedOwnershipIsAllowed()
        {
            var child = MemoryPool<OtherItem>.Acquire();
            var parent = MemoryPool<PoolItem>.Acquire();
            parent.OnClear = () => MemoryPool.Release(child);
            MemoryPool.Release(parent);
            parent.OnClear = null;
            Assert.That(Info<OtherItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void DoubleForeignAndUnownedReturnsAreRejected()
        {
            var item = MemoryPool<OtherItem>.Acquire();
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool<PoolItem>.Release(item)).Message, Does.Contain("belongs to another pool"));
            MemoryPool.Release((MemoryObject)item);
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool.Release((MemoryObject)item)).Message, Does.Contain("not leased"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(new PoolItem())).Message, Does.Contain("no owner pool"));
            Assert.That(Info<OtherItem>().UsingCount, Is.Zero);
        }

        [Test]
        public void TypeAndHandleApisKeepOwnerIdentity()
        {
            var handle = MemoryPool.GetHandle(typeof(PoolItem));
            var item = handle.Acquire();
            MemoryPool.Release(item);
            Assert.That(handle.Acquire(), Is.SameAs(item));
            handle.Release(item);
            Assert.That(MemoryPool.Acquire(typeof(PoolItem)), Is.SameAs(item));
            handle.Release(item);
            Assert.That(Assert.Throws<InvalidOperationException>(() => default(MemoryPoolHandle).Acquire()).Message, Does.Contain("invalid"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool.GetHandle(typeof(string))).Message, Does.Contain("must inherit MemoryObject"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => MemoryPool.GetHandle(typeof(MemoryObject))).Message, Does.Contain("must not be abstract"));
            Assert.Throws<ArgumentNullException>(() => MemoryPool.GetHandle(null));
        }

        [Test]
        public void WorkerThreadCannotMutateAnInitializedPool()
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { MemoryPool<PoolItem>.Acquire(); }
                catch (Exception e) { error = e; }
            });
            thread.Start();
            Assert.That(thread.Join(5000), Is.True);
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
        }

        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(4097)]
        public void BurstOverflowRetiresOnlyReturnedObjects(int count)
        {
            MemoryPool<PoolItem>.SetCapacity(4, 4);
            var items = new PoolItem[count];
            for (int i = 0; i < count; i++) items[i] = MemoryPool<PoolItem>.Acquire();
            for (int i = 0; i < count; i++) MemoryPool.Release(items[i]);
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(Math.Min(4, count)));
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
            int evicted = 0;
            foreach (var item in items) evicted += item.Evictions;
            Assert.That(evicted, Is.EqualTo(Math.Max(0, count - 4)));
        }

        [Test]
        public void RandomBusinessLifetimesKeepTheOwnershipLedgerBalanced()
        {
            var random = new Random(314159);
            var leased = new List<PoolItem>();
            for (int i = 0; i < 20000; i++)
            {
                if (leased.Count == 0 || random.Next(100) < 55)
                {
                    var item = MemoryPool<PoolItem>.Acquire();
                    Assert.That(leased.Contains(item), Is.False);
                    leased.Add(item);
                }
                else
                {
                    int index = random.Next(leased.Count);
                    MemoryPool.Release(leased[index]);
                    leased.RemoveAt(index);
                }
                if (i % 113 == 0) MemoryPool<PoolItem>.ClearAll();
                if (i % 71 == 0) MemoryPool<PoolItem>.Shrink(3);
                if (i % 17 == 0) Tick();
                Assert.That(Info<PoolItem>().UsingCount, Is.EqualTo(leased.Count));
            }
            foreach (var item in leased) MemoryPool.Release(item);
            MemoryPool<PoolItem>.ClearAll();
            Assert.That(Info<PoolItem>().UsingCount, Is.Zero);
            Assert.That(Info<PoolItem>().UnusedCount, Is.Zero);
        }
    }
}
