using System;
using NUnit.Framework;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ShutdownTransactionTests : ObjectPoolFixture
    {
        [Test]
        public void DestroyContinuesAcrossPoolsAndObjectsWhenReleaseThrowsAndReturnsAllBuffers()
        {
            var probes = new TargetProbe[6];
            var pools = new ObjectPoolBase[3];
            for (int p = 0; p < pools.Length; p++)
            {
                var pool = Pool(name: p.ToString());
                pools[p] = (ObjectPoolBase)pool;
                for (int i = 0; i < 2; i++)
                {
                    var obj = Item("name", locked: true);
                    obj.CanRelease = false;
                    probes[p * 2 + i] = obj.Probe;
                    obj.Probe.ThrowRelease = true;
                    pool.Register(obj, i == 0);
                }
            }
            try
            {
                var error = Assert.Throws<AggregateException>(() => ((IServiceLifecycle)Service).Destroy());
                Assert.That(error.Flatten().InnerExceptions.Count, Is.EqualTo(6));
                Assert.That(Service.Count, Is.Zero);
                Assert.That(Active(Service), Is.Zero);
                Assert.That(Read<int>(Service, "m_PoolCount"), Is.Zero);
                Assert.That(Using<PoolObject>(), Is.Zero);
                foreach (var probe in probes)
                {
                    Assert.That(probe.Shutdowns, Is.EqualTo(1));
                    Assert.That(probe.Clears, Is.EqualTo(1));
                }
                foreach (var pool in pools)
                {
                    Ledger(pool, 0, 0, 0);
                    Assert.That(Read<Array>(pool, "m_Slots"), Is.Null);
                    Assert.That(Read<Array>(Read<ReferenceOpenHashMap>(pool, "m_TargetMap"), "m_Buckets"), Is.Null);
                    Assert.That(Read<Array>(Read<OpenHashMap<string>>(pool, "m_AvailableNameHeadMap"), "m_Buckets"), Is.Null);
                }
                Assert.That(Read<Array>(Read<OpenHashMap<ObjectPoolKey>>(Service, "m_PoolMap"), "m_Buckets"), Is.Null);
            }
            finally { foreach (var probe in probes) probe.ThrowRelease = false; }
        }

        [Test]
        public void DestroyDetachesStorageBeforeCallbackCanDestroyAndRecreateTheSamePool()
        {
            var pool = Pool(name: "old");
            var other = Pool(name: "other");
            var obj = Item();
            var probe = obj.Probe;
            pool.Register(obj, false);
            IObjectPool<PoolObject> replacement = null;
            probe.ReleaseAction = _ =>
            {
                probe.ReleaseAction = null;
                Assert.That(Service.DestroyObjectPool<PoolObject>("old"), Is.False);
                Assert.That(Service.DestroyObjectPool<PoolObject>("other"), Is.True);
                replacement = Pool(name: "old");
                replacement.Register(Item(), false);
            };
            Assert.That(Service.DestroyObjectPool<PoolObject>("old"), Is.True);
            Assert.That(Service.Count, Is.EqualTo(1));
            Assert.That(Service.GetObjectPool<PoolObject>("old"), Is.SameAs(replacement));
            Assert.That(replacement, Is.Not.SameAs(pool));
            Assert.That(probe.Shutdowns, Is.EqualTo(1));
            Ledger((ObjectPoolBase)replacement, 1, 1, 0);
            Ledger((ObjectPoolBase)other, 0, 0, 0);
        }

        [TestCase("register")]
        [TestCase("spawn")]
        [TestCase("unspawn")]
        [TestCase("release")]
        public void ShutdownRequestedInsideACallbackFinishesAfterThatCallbackWithoutDoubleRecycling(string operation)
        {
            var pool = Pool();
            var obj = Item();
            var probe = obj.Probe;
            Action destroy = () => ((IServiceLifecycle)Service).Destroy();
            if (operation == "register" || operation == "spawn") probe.SpawnAction = destroy;
            if (operation == "unspawn") probe.UnspawnAction = destroy;
            if (operation == "release") probe.ReleaseAction = _ => { probe.ReleaseAction = null; destroy(); };
            if (operation == "register") Assert.That(pool.Register(obj, true), Is.False);
            else
            {
                pool.Register(obj, operation == "unspawn");
                if (operation == "spawn") Assert.That(pool.Spawn(), Is.Null);
                if (operation == "unspawn") pool.Unspawn(obj);
                if (operation == "release") pool.ReleaseAllUnused();
            }
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(Service.Count, Is.Zero);
            Assert.That(Active(Service), Is.Zero);
            Assert.That(probe.Releases, Is.EqualTo(1));
            Assert.That(probe.Shutdowns, Is.EqualTo(operation == "release" ? 0 : 1));
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(Using<PoolObject>(), Is.Zero);
        }

        [Test]
        public void ShutdownUnspawnIsSilentAndDestroyedPoolRejectsNewLeases()
        {
            var pool = Pool();
            var obj = Item();
            var target = obj.Target;
            pool.Register(obj, true);
            obj.Probe.ReleaseAction = _ => { pool.Unspawn(obj); pool.UnspawnTarget(new object()); };
            Assert.That(Service.DestroyObjectPool<PoolObject>(), Is.True);
            pool.UnspawnTarget(target);
            pool.Unspawn(obj);
            Assert.That(pool.Count, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => pool.Spawn());
            var incoming = Item();
            Assert.Throws<ObjectDisposedException>(() => pool.Register(incoming, false));
            MemoryPool<PoolObject>.Release(incoming);
            Assert.Throws<ObjectDisposedException>(() => pool.Capacity = 1);
            Assert.That(Service.DestroyObjectPool<PoolObject>(), Is.False);
            Assert.That(Service.GetObjectPool<PoolObject>(), Is.Null);
        }

        [Test]
        public void TickProcessesEachSurvivingActivePoolOnceWhenCallbackDestroysAnEarlierPool()
        {
            var first = Pool(name: "first");
            var middle = Pool(name: "middle");
            var last = Pool(name: "last");
            foreach (var pool in new[] { first, middle, last })
            {
                for (int i = 0; i < 16; i++) pool.Register(Item(), false);
                pool.Capacity = 0;
            }
            var slots = Read<Array>(last, "m_Slots");
            for (int i = 0; i < slots.Length; i++)
                Read<PoolObject>(slots.GetValue(i), "Obj").Probe.ReleaseAction = shutdown =>
                {
                    if (!shutdown) Service.DestroyObjectPool<PoolObject>("middle");
                };
            Tick();
            Ledger((ObjectPoolBase)first, 8, 8, 0);
            Ledger((ObjectPoolBase)middle, 0, 0, 0);
            Ledger((ObjectPoolBase)last, 8, 8, 0);
            Assert.That(Active(Service), Is.EqualTo(2));
            Tick();
            Assert.That(first.Count + last.Count, Is.Zero);
            Assert.That(Active(Service), Is.Zero);
        }

        [Test]
        public void ServiceReleaseDoesNotFollowStorageSwapsOrReleasePoolsCreatedByTheCallback()
        {
            var first = Pool(name: "first");
            var second = Pool(name: "second");
            var third = Pool(name: "third");
            var obj = Item();
            first.Register(obj, false);
            second.Register(Item(), false);
            third.Register(Item(), false);
            IObjectPool<PoolObject> added = null;
            obj.Probe.ReleaseAction = _ =>
            {
                Service.DestroyObjectPool<PoolObject>("second");
                added = Pool(name: "added");
                added.Register(Item(), false);
            };
            Service.ReleaseAllUnused();
            Assert.That(third.Count, Is.Zero);
            Assert.That(added.Count, Is.EqualTo(1));
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
        }
    }
}
