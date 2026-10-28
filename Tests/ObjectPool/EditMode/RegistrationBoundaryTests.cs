using System;
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
        public void EvictionCallbackCannotRegisterIntoTheSamePool()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            pool.Register(old, false);
            var incoming = Item();
            var incomingProbe = incoming.Probe;
            var replacement = Item("replacement");
            var replacementProbe = replacement.Probe;
            old.Probe.ReleaseAction = _ => pool.Register(replacement, false);

            Assert.Throws<InvalidOperationException>(() => pool.Register(incoming, false));

            // 旧条目已摘除，新包装对象被拒绝；这次替换从未注册成功，必须由它的所有者释放。
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            MemoryPool<PoolObject>.Release(replacement);
            Assert.That(Using<PoolObject>(), Is.Zero);
            Assert.That(incomingProbe.Clears, Is.EqualTo(1));
            Assert.That(replacementProbe.Clears, Is.EqualTo(1), "the unregistered replacement is cleared when its owner releases it");
        }

        [Test]
        public void EvictionCallbackCanMutateAnotherPool()
        {
            var first = Pool(capacity: 1);
            var second = Pool(name: "second");
            var old = Item();
            first.Register(old, false);
            var incoming = Item();
            var other = Item("other", "payload");
            second.Register(other, false);

            old.Probe.ReleaseAction = _ => Assert.That(second.Spawn("other"), Is.SameAs(other));

            Assert.That(first.Register(incoming, false), Is.True);
            Assert.That(other.Probe.Spawns, Is.EqualTo(1));
            Ledger((ObjectPoolBase)first, 1, 1, 0);
            Ledger((ObjectPoolBase)second, 1, 0, 1);
            second.Unspawn(other);
        }

        [Test]
        public void EvictionCallbackCannotDestroyThePoolMidMutation()
        {
            var pool = Pool(capacity: 1);
            var old = Item();
            pool.Register(old, false);
            var incoming = Item();
            var incomingProbe = incoming.Probe;
            old.Probe.ReleaseAction = _ => Service.DestroyObjectPool<PoolObject>();

            Assert.Throws<InvalidOperationException>(() => pool.Register(incoming, false));

            Assert.That(incomingProbe.Clears, Is.EqualTo(1));
            Assert.That(incomingProbe.Releases, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.Zero);
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }
    }
}
