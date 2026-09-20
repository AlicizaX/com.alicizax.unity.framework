using System;
using NUnit.Framework;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ObjectPoolMaintenanceTests : ObjectPoolFixture
    {
        [Test]
        public void ExpiryIncludesTheExactThresholdAndExcludesYoungerObjects()
        {
            var pool = Pool(expire: 1f);
            var item = Item();
            var probe = item.Probe;
            pool.Register(item, false);
            var release = pool.GetType().GetMethod("ReleaseUnused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            float stamp = item.LastUseTime;
            Assert.That((int)release.Invoke(pool, new object[] { 8, true, stamp - 0.01f }), Is.Zero);
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            Assert.That((int)release.Invoke(pool, new object[] { 8, true, stamp }), Is.EqualTo(1));
            Assert.That(probe.Releases, Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }

        [Test]
        public void AutoReleaseIntervalDoesNotActivateOrReleaseWhenCountIsAtOrBelowCapacity()
        {
            var pool = Pool(capacity: 8, interval: 0f);
            for (int i = 0; i < 8; i++) pool.Register(Item(), false);
            Assert.That(Active(Service), Is.Zero);
            for (int i = 0; i < 16; i++) Tick();
            Ledger((ObjectPoolBase)pool, 8, 8, 0);
            Assert.That(Active(Service), Is.Zero);
        }

        [Test]
        public void ExpireDisabledLeavesLastUseTimeUnstampedAndKeepsThePoolInactive()
        {
            var pool = Pool();
            var obj = Item();
            pool.Register(obj, false);
            Assert.That(obj.LastUseTime, Is.Zero);
            Assert.That(Active(Service), Is.Zero);
            Tick();
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            Assert.That(pool.Spawn(), Is.SameAs(obj));
            Assert.That(obj.LastUseTime, Is.Zero);
            pool.Unspawn(obj);
            Assert.That(obj.LastUseTime, Is.Zero);
        }

        [Test]
        public void EnablingExpireStampsPreviouslyUntimedUnusedObjectsInsteadOfTreatingZeroAsEpoch()
        {
            var pool = Pool();
            var obj = Item();
            pool.Register(obj, false);
            Assert.That(obj.LastUseTime, Is.Zero);
            pool.ExpireTime = 1f;
            Assert.That(obj.LastUseTime, Is.GreaterThan(0f));
            Tick();
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
        }

        [Test]
        public void NestedServiceTickThrowsAndLeavesTheSnapshotCleared()
        {
            var pool = Pool();
            var obj = Item();
            pool.Register(obj, false);
            pool.Capacity = 0;
            obj.Probe.ReleaseAction = _ =>
            {
                obj.Probe.ReleaseAction = null;
                Assert.Throws<InvalidOperationException>(() => Tick());
            };
            Tick();
            Assert.That(Read<bool>(Service, "m_IsTicking"), Is.False);
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }

        [Test]
        public void PendingCapacityReleaseIsIndependentOfParameterlessRelease()
        {
            var pool = Pool();
            for (int i = 0; i < 20; i++) pool.Register(Item(), false);
            pool.Capacity = 10;
            Assert.That(pool.Count, Is.EqualTo(20));
            Tick();
            Ledger((ObjectPoolBase)pool, 12, 12, 0);
            pool.Release();
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(Active(Service), Is.Zero);
        }

        [Test]
        public void RaisingCapacityStopsPendingOverflowRelease()
        {
            var pool = Pool();
            for (int i = 0; i < 20; i++) pool.Register(Item(), false);
            pool.Capacity = 4;
            pool.Capacity = 20;
            Tick();
            Ledger((ObjectPoolBase)pool, 20, 20, 0);
            Assert.That(Active(Service), Is.Zero);
        }

        [Test]
        public void RaisingCapacityWhileStillOverClampsPendingToCurrentOverflow()
        {
            var pool = Pool();
            for (int i = 0; i < 20; i++) pool.Register(Item(), false);
            pool.Capacity = 4;
            pool.Capacity = 15;
            Tick();
            Ledger((ObjectPoolBase)pool, 15, 15, 0);
            Assert.That(Active(Service), Is.Zero);
        }

        [Test]
        public void AutoReleaseIntervalRearmsAfterReleaseAllUnusedLeavesLockedOverflow()
        {
            var pool = Pool(interval: 0f);
            var locked = new PoolObject[4];
            for (int i = 0; i < locked.Length; i++)
            {
                locked[i] = Item(locked: true);
                pool.Register(locked[i], false);
            }
            pool.Capacity = 2;
            pool.ReleaseAllUnused();
            Ledger((ObjectPoolBase)pool, 4, 4, 0);
            Assert.That(Active(Service), Is.EqualTo(1));
            Tick();
            Ledger((ObjectPoolBase)pool, 4, 4, 0);
            locked[0].Locked = false;
            locked[1].Locked = false;
            Tick();
            Ledger((ObjectPoolBase)pool, 2, 2, 0);
            locked[2].Locked = false;
            locked[3].Locked = false;
        }

        [Test]
        public void ServiceReleaseAndPoolReleaseAreTheSameAllUnusedPath()
        {
            var first = Pool(name: "first");
            var second = Pool(name: "second");
            first.Register(Item(), false);
            var rented = Item();
            second.Register(rented, true);
            Service.Release();
            Ledger((ObjectPoolBase)first, 0, 0, 0);
            Ledger((ObjectPoolBase)second, 1, 0, 1);
            second.Unspawn(rented);
            second.Release();
            Ledger((ObjectPoolBase)second, 0, 0, 0);
        }
    }
}
