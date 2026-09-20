using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class RegistrationOwnershipTests : ObjectPoolFixture
    {
        [TestCase(false, "")]
        [TestCase(true, "")]
        [TestCase(false, "named")]
        [TestCase(true, "named")]
        public void RegisterSameWrapperTwiceRejectsWithoutClearingTheOwnedEntry(bool multi, string name)
        {
            var pool = Pool(multi);
            var obj = Item(name);
            var target = obj.Target;
            var probe = obj.Probe;
            Assert.That(pool.Register(obj, false), Is.True);
            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            Assert.That(pool.Register(obj, true), Is.False);
            Assert.That(obj.Target, Is.SameAs(target));
            Assert.That(probe.Clears, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WrapperCannotBelongToTwoPools(bool spawned)
        {
            var first = Pool();
            var second = Pool(name: "second");
            var obj = Item();
            Assert.That(first.Register(obj, spawned), Is.True);
            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            Assert.That(second.Register(obj, !spawned), Is.False);
            Ledger((ObjectPoolBase)first, 1, spawned ? 0 : 1, spawned ? 1 : 0);
            Ledger((ObjectPoolBase)second, 0, 0, 0);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
        }

        [Test]
        public void DistinctDuplicateWrapperRecyclesOnlyTheRejectedWrapper()
        {
            var pool = Pool();
            var first = Item();
            var second = Item(target: first.Target);
            var probe = second.Probe;
            Assert.That(pool.Register(first, false), Is.True);
            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            Assert.That(pool.Register(second, true), Is.False);
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(probe.Releases, Is.Zero, "Duplicate target still belongs to original wrapper.");
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Assert.That(pool.Spawn(), Is.SameAs(first));
            Ledger((ObjectPoolBase)pool, 1, 0, 1);
        }

        [Test]
        public void NonMemoryPoolWrapperIsReleasedAndClearedWithoutReturningToMemoryPool()
        {
            var pool = Pool();
            var probe = new TargetProbe();
            var obj = new PoolObject { Probe = probe };
            obj.Bind(new object());
            Assert.That(pool.Register(obj, false), Is.True);
            pool.ReleaseAllUnused();
            Assert.That(obj.Target, Is.Null);
            Assert.That(probe.Releases, Is.EqualTo(1));
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(Using<PoolObject>(), Is.Zero);
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }
    }
}
