using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ObjectPoolPerformanceTests : ObjectPoolFixture
    {
        private static PoolObject Silent(object target, string name = "")
        {
            var obj = MemoryPool<PoolObject>.Acquire();
            obj.Bind(target, name);
            obj.Probe = null;
            return obj;
        }

        [UnityTest]
        public IEnumerator FirstAndSteadyRentReturnAndRegisterRecycleHaveNoPerOperationAllocation()
        {
            var target = new object();
            var pool = Pool();
            var obj = Silent(target);
            pool.Register(obj, false);
            yield return AllocationCapture.Measure("first-rent-return", 1, () => { pool.Spawn(); pool.Unspawn(obj); }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("steady-rent-return", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) { pool.Spawn(); pool.Unspawn(obj); }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            pool.ReleaseAllUnused();
            yield return AllocationCapture.Measure("register-recycle-existing-capacity", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) { pool.Register(Silent(target), true); pool.UnspawnTarget(target); pool.ReleaseAllUnused(); }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(Using<PoolObject>(), Is.Zero);
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }

        [UnityTest]
        public IEnumerator NonExpiredUnusedScanCostAtOneFourAndTenThousandObjects()
        {
            foreach (int size in new[] { 1000, 4000, 10000 })
            {
                var pool = Pool(expire: float.MaxValue / 2);
                for (int i = 0; i < size; i++) pool.Register(Silent(new object()), false);
                Tick();
                yield return AllocationCapture.Measure("nonexpired-tick-" + size, 16, () =>
                {
                    for (int i = 0; i < 16; i++) Tick();
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(pool.Count, Is.EqualTo(size));
                Assert.That(Active(Service), Is.EqualTo(1));
                Assert.That(Using<PoolObject>(), Is.EqualTo(size));
                Ledger((ObjectPoolBase)pool, size, size, 0);
                Service.DestroyObjectPool<PoolObject>();
                Assert.That(Using<PoolObject>(), Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator FirstServicePoolNameHashGrowthAndSlotGrowthAreMeasuredSeparately()
        {
            ObjectPoolService created = null;
            yield return AllocationCapture.Measure("new-service-instance", 1, () => created = new ObjectPoolService(), sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            ((IServiceLifecycle)created).Initialize(null, null);
            ((IServiceLifecycle)created).Destroy();
            IObjectPool<PoolObject> pool = null;
            yield return AllocationCapture.Measure("first-pool", 1, () => pool = Pool(), sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            var obj = Silent(new object(), "first-name");
            yield return AllocationCapture.Measure("first-name-maps", 1, () => pool.Register(obj, false), sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(0)));
            Assert.That(Read<bool>(pool, "m_HasNameMap"), Is.True);
            pool.ReleaseAllUnused();
            Service.DestroyObjectPool<PoolObject>();
            pool = Pool();
            for (int i = 0; i < 12; i++) pool.Register(Silent(new object()), true);
            Assert.That(Read<int>(Read<ReferenceOpenHashMap>(pool, "m_TargetMap"), "m_Mask"), Is.EqualTo(15));
            obj = Silent(new object());
            yield return AllocationCapture.Measure("target-map-grow-16-to-32", 1, () => pool.Register(obj, true), sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(0)));
            Assert.That(Read<int>(Read<ReferenceOpenHashMap>(pool, "m_TargetMap"), "m_Mask"), Is.EqualTo(31));
            for (int i = 0; i < 3; i++) pool.Register(Silent(new object()), true);
            obj = Silent(new object());
            yield return AllocationCapture.Measure("slot-grow-16-to-32", 1, () => pool.Register(obj, true), sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            Assert.That(Read<int>(pool, "m_SlotCount"), Is.EqualTo(32));
            Ledger((ObjectPoolBase)pool, 17, 0, 17);
            string fullName = null;
            yield return AllocationCapture.Measure("first-full-name", 1, () => fullName = pool.FullName, sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            yield return AllocationCapture.Measure("cached-full-name", 10000, () => { for (int i = 0; i < 10000; i++) fullName = pool.FullName; }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(fullName, Is.EqualTo(typeof(PoolObject).FullName));
        }

        [UnityTest]
        public IEnumerator InactivePoolsAndNamedMultiSpawnScaleWithoutSteadyAllocations()
        {
            var pools = new IObjectPool<PoolObject>[1000];
            var names = new string[pools.Length];
            for (int i = 0; i < pools.Length; i++)
            {
                names[i] = "p" + i;
                pools[i] = Pool(true, name: names[i]);
                pools[i].Register(Silent(new object(), "audio"), false);
            }
            Assert.That(Active(Service), Is.Zero);
            yield return AllocationCapture.Measure("inactive-thousand-pools-tick", 64, () => { for (int i = 0; i < 64; i++) Tick(); }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("named-multi-rent-lookup-thousand-pools", 10000, () =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    var pool = Service.GetObjectPool<PoolObject>(names[i % pools.Length]);
                    var obj = pool.Spawn("audio");
                    pool.Spawn("audio");
                    pool.Unspawn(obj); pool.Unspawn(obj);
                }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(Using<PoolObject>(), Is.EqualTo(1000));
            Service.ReleaseAllUnused();
            Assert.That(Using<PoolObject>(), Is.Zero);
        }
    }
}
