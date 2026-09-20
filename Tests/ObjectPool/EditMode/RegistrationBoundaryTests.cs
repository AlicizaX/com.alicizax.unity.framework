using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class RegistrationBoundaryTests : ObjectPoolFixture
    {
        [Test]
        public void UnspawnWrapperCannotReturnAnotherWrappersLeaseEvenWithTheSameTarget()
        {
            var pool = Pool();
            var owned = Item();
            var foreign = Item(target: owned.Target);
            pool.Register(owned, true);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("not registered"));
                pool.Unspawn(foreign);
                Ledger((ObjectPoolBase)pool, 1, 0, 1);
                Assert.That(owned.Probe.Unspawns, Is.Zero);
            }
            finally { MemoryPool<PoolObject>.Release(foreign); }
            pool.UnspawnTarget(owned.Target);
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
        }

        [Test]
        public void EvictionCallbackCannotRegisterTheIncomingWrapperASecondTime()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            pool.Register(old, false);
            var incoming = Item();
            var probe = incoming.Probe;
            old.Probe.ReleaseAction = _ =>
            {
                LogAssert.Expect(LogType.Error, new Regex("already registered"));
                Assert.That(pool.Register(incoming, true), Is.False);
            };
            Assert.That(pool.Register(incoming, false), Is.True);
            Assert.That(probe.Clears, Is.Zero);
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
        }

        [Test]
        public void EvictionCallbackShutdownRejectsAndRecyclesTheIncomingWrapper()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            pool.Register(old, false);
            var incoming = Item();
            var probe = incoming.Probe;
            old.Probe.ReleaseAction = _ => ((IServiceLifecycle)Service).Destroy();
            Assert.That(pool.Register(incoming, false), Is.False);
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(probe.Releases, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.Zero);
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }

        [Test]
        public void EvictionCallbackFillingCapacityRejectsIncomingWrapper()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            pool.Register(old, false);
            var incoming = Item();
            var probe = incoming.Probe;
            old.Probe.ReleaseAction = _ =>
            {
                Assert.That(pool.Register(Item("replacement"), false), Is.True);
            };
            LogAssert.Expect(LogType.Error, new Regex("capacity is full"));
            Assert.That(pool.Register(incoming, false), Is.False);
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(probe.Releases, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            Assert.That(pool.Spawn("replacement"), Is.Not.Null);
        }

        [Test]
        public void EvictionCallbackSecondNestedRegisterIsRejectedWithoutNestedRelease()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            pool.Register(old, false);
            var incoming = Item();
            var incomingProbe = incoming.Probe;
            PoolObject rejected = null;
            old.Probe.ReleaseAction = _ =>
            {
                Assert.That(pool.Register(Item("replacement"), false), Is.True);
                rejected = Item("overflow");
                var overflowProbe = rejected.Probe;
                LogAssert.Expect(LogType.Error, new Regex("capacity is full"));
                Assert.That(pool.Register(rejected, false), Is.False);
                Assert.That(overflowProbe.Clears, Is.EqualTo(1));
                Assert.That(overflowProbe.Releases, Is.Zero);
            };
            LogAssert.Expect(LogType.Error, new Regex("capacity is full"));
            Assert.That(pool.Register(incoming, false), Is.False);
            Assert.That(incomingProbe.Clears, Is.EqualTo(1));
            Assert.That(incomingProbe.Releases, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            Assert.That(pool.Spawn("replacement"), Is.Not.Null);
            Assert.That(pool.Spawn("overflow"), Is.Null);
        }

        [Test]
        public void EvictionCallbackCanRegisterReplacementWithTheSameTarget()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            var target = old.Target;
            pool.Register(old, false);
            var incoming = Item();
            var incomingProbe = incoming.Probe;
            old.Probe.ReleaseAction = _ =>
            {
                Assert.That(pool.Register(Item(target: target), false), Is.True);
            };
            LogAssert.Expect(LogType.Error, new Regex("capacity is full"));
            Assert.That(pool.Register(incoming, false), Is.False);
            Assert.That(incomingProbe.Clears, Is.EqualTo(1));
            Assert.That(incomingProbe.Releases, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            var spawned = pool.Spawn();
            Assert.That(spawned, Is.Not.Null);
            Assert.That(spawned.Target, Is.SameAs(target));
            Assert.That(spawned, Is.Not.SameAs(old));
        }
    }
}
