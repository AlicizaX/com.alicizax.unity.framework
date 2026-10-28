using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class CallbackTransactionTests : ObjectPoolFixture
    {
        [TestCase(false, "", false)]
        [TestCase(true, "named", false)]
        [TestCase(false, "", true)]
        [TestCase(true, "named", true)]
        public void SpawnFailureRollsBackOnlyTheAttemptedLease(bool multi, string name, bool registerSpawned)
        {
            var pool = Pool(multi);
            var obj = Item(name);
            var probe = obj.Probe;
            probe.ThrowSpawn = true;
            try
            {
                if (registerSpawned)
                    Assert.Throws<InvalidOperationException>(() => pool.Register(obj, true));
                else
                {
                    Assert.That(pool.Register(obj, false), Is.True);
                    Assert.Throws<InvalidOperationException>(() => pool.Spawn(name));
                }
                Ledger((ObjectPoolBase)pool, 1, 1, 0);
                Assert.That(Using<PoolObject>(), Is.EqualTo(1));
                Assert.That(probe.Clears, Is.Zero);
            }
            finally { probe.ThrowSpawn = false; }

            Assert.That(pool.Spawn(name), Is.SameAs(obj));
            if (multi)
            {
                probe.ThrowSpawn = true;
                Assert.Throws<InvalidOperationException>(() => pool.Spawn(name));
                probe.ThrowSpawn = false;
                Ledger((ObjectPoolBase)pool, 1, 0, 1);
            }
            pool.Unspawn(obj);
            pool.ReleaseAllUnused();
            Assert.That(probe.Releases, Is.EqualTo(1));
        }

        [TestCase(false, "")]
        [TestCase(true, "")]
        [TestCase(false, "named")]
        [TestCase(true, "named")]
        public void UnspawnFailureReturnsExactlyOneLeaseAndDuplicateUnspawnDoesNotCallTheCallback(bool multi, string name)
        {
            var pool = Pool(multi);
            var obj = Item(name);
            var probe = obj.Probe;
            pool.Register(obj, true);
            if (multi) pool.Spawn(name);
            probe.ThrowUnspawn = true;
            Assert.Throws<InvalidOperationException>(() => pool.Unspawn(obj));
            Ledger((ObjectPoolBase)pool, 1, multi ? 0 : 1, multi ? 1 : 0);
            if (multi) Assert.Throws<InvalidOperationException>(() => pool.UnspawnTarget(obj.Target));
            probe.ThrowUnspawn = false;
            LogAssert.Expect(LogType.Error, new Regex("Object '" + Regex.Escape(name) + "' is not spawned"));
            pool.Unspawn(obj);
            Assert.That(probe.Unspawns, Is.EqualTo(multi ? 2 : 1));
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
        }

        [TestCase(false, "")]
        [TestCase(true, "named")]
        public void FailedReleaseDetachesAndRecyclesTheWrapperInsteadOfRetainingAZombie(bool multi, string name)
        {
            var pool = Pool(multi);
            var obj = Item(name);
            var probe = obj.Probe;
            pool.Register(obj, false);
            probe.ThrowRelease = true;

            Assert.Throws<InvalidOperationException>(() => pool.ReleaseAllUnused());
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(Using<PoolObject>(), Is.Zero);
            Assert.That(pool.Spawn(name), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CapacityEvictionFailureRecyclesBothWrappers(bool multi)
        {
            var pool = Pool(multi, capacity: 1);
            var first = Item(multi ? "n" : "");
            var firstProbe = first.Probe;
            pool.Register(first, false);
            var incoming = Item(multi ? "n" : "");
            var incomingProbe = incoming.Probe;
            firstProbe.ThrowRelease = true;
            try
            {
                Assert.Throws<InvalidOperationException>(() => pool.Register(incoming, false));
                Assert.That(firstProbe.Clears, Is.EqualTo(1));
                Assert.That(incomingProbe.Clears, Is.EqualTo(1));
                Assert.That(Using<PoolObject>(), Is.Zero);
                Ledger((ObjectPoolBase)pool, 0, 0, 0);
            }
            finally { firstProbe.ThrowRelease = false; }
        }

        [Test]
        public void RecursiveReturnOfTheSameObjectIsRejectedWithoutTouchingTheOuterLease()
        {
            var pool = Pool(true);
            var obj = Item("named");
            var probe = obj.Probe;
            pool.Register(obj, true);
            pool.Spawn("named");
            probe.UnspawnAction = () => { probe.UnspawnAction = null; pool.Unspawn(obj); };
            Assert.Throws<InvalidOperationException>(() => pool.Unspawn(obj));
            Ledger((ObjectPoolBase)pool, 1, 0, 1);
            Assert.That(probe.Unspawns, Is.EqualTo(1));
            pool.Unspawn(obj);
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
        }
    }
}
