using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ObjectPoolTimeTests : ObjectPoolFixture
    {
        private float timeScale;

        [SetUp]
        public void SaveTimeScale()
        {
            timeScale = Time.timeScale;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreTimeScale() => Time.timeScale = timeScale;

        [UnityTest]
        public IEnumerator ExpireTimeReleasesOnlyUnusedObjectsThatHaveAged()
        {
            var pool = Pool(expire: 0.05f);
            var unused = Item("unused");
            var rented = Item("rented");
            var locked = Item("locked", locked: true);
            var unusedProbe = unused.Probe;
            var rentedProbe = rented.Probe;
            var lockedProbe = locked.Probe;
            pool.Register(unused, false);
            pool.Register(rented, true);
            pool.Register(locked, false);
            Assert.That(unused.LastUseTime, Is.GreaterThan(0f));
            float registered = unused.LastUseTime;
            Tick();
            Ledger((ObjectPoolBase)pool, 3, 2, 1);
            yield return new WaitForSecondsRealtime(0.08f);
            Tick();
            Assert.That(unusedProbe.Releases, Is.EqualTo(1));
            Assert.That(rentedProbe.Releases, Is.Zero);
            Assert.That(lockedProbe.Releases, Is.Zero);
            Ledger((ObjectPoolBase)pool, 2, 1, 1);
            Assert.That(registered, Is.LessThanOrEqualTo(Time.realtimeSinceStartup - 0.05f));
            pool.Unspawn(rented);
            locked.Locked = false;
            pool.ReleaseAllUnused();
        }

        [UnityTest]
        public IEnumerator EnablingExpireStampsPreviouslyUntimedUnusedObjectsInsteadOfTreatingZeroAsEpoch()
        {
            var pool = Pool();
            var obj = Item();
            var probe = obj.Probe;
            pool.Register(obj, false);
            Assert.That(obj.LastUseTime, Is.Zero);
            pool.ExpireTime = 0.05f;
            Tick();
            Assert.That(obj.LastUseTime, Is.GreaterThan(0f));
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            yield return new WaitForSecondsRealtime(0.08f);
            Tick();
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(probe.Releases, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TimeScaleZeroStillExpiresUsingRealtime()
        {
            var pool = Pool(expire: 0.05f);
            var obj = Item();
            var probe = obj.Probe;
            pool.Register(obj, false);
            Time.timeScale = 0f;
            double scaled = Time.timeAsDouble;
            yield return new WaitForSecondsRealtime(0.1f);
            Tick();
            Assert.That(Time.timeAsDouble, Is.EqualTo(scaled));
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(probe.Releases, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AutoReleaseIntervalIgnoresScaledTimeAndOnlyMarksWhenOverCapacity()
        {
            var pool = Pool(interval: 0.05f);
            var locked = new PoolObject[4];
            for (int i = 0; i < locked.Length; i++)
            {
                locked[i] = Item(locked: true);
                pool.Register(locked[i], false);
            }
            pool.Capacity = 2;
            pool.ReleaseAllUnused();
            Ledger((ObjectPoolBase)pool, 4, 4, 0);
            Time.timeScale = 0f;
            double scaled = Time.timeAsDouble;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < 0.05f)
            {
                yield return null;
                Tick();
            }
            Assert.That(Time.timeAsDouble, Is.EqualTo(scaled));
            Ledger((ObjectPoolBase)pool, 4, 4, 0);
            locked[0].Locked = false;
            locked[1].Locked = false;
            Tick();
            Ledger((ObjectPoolBase)pool, 2, 2, 0);
            locked[2].Locked = false;
            locked[3].Locked = false;
            pool.ReleaseAllUnused();
        }

        [UnityTest]
        public IEnumerator StoppedTicksFreezeExpiryAndResumeHonorsTheFrameBudget()
        {
            var pool = Pool(expire: 0.05f);
            for (int i = 0; i < 20; i++) pool.Register(Item(), false);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(pool.Count, Is.EqualTo(20));
            Tick();
            Assert.That(pool.Count, Is.EqualTo(12));
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(pool.Count, Is.EqualTo(12));
            Tick();
            Assert.That(pool.Count, Is.EqualTo(4));
            Tick();
            Assert.That(pool.Count, Is.Zero);
        }
    }
}
