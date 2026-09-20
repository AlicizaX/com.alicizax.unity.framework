using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ObjectPoolContractTests : ObjectPoolFixture
    {
        [Test]
        public void TypeAndOrdinalNameIdentifyPoolsAndExistingOptionsAreIgnored()
        {
            var unnamed = Pool();
            Assert.That(Service.GetOrCreatePool<PoolObject>(new ObjectPoolCreateOptions(null, true, 1, 1, 1, 7)), Is.SameAs(unnamed));
            Assert.That(unnamed.AllowMultiSpawn, Is.False);
            Assert.That(unnamed.Capacity, Is.EqualTo(int.MaxValue));
            Assert.That(unnamed.AutoReleaseInterval, Is.EqualTo(float.MaxValue));
            Assert.That(unnamed.ExpireTime, Is.EqualTo(float.MaxValue));
            Assert.That(unnamed.Priority, Is.Zero);
            var named = Pool(name: "pool");
            var upper = Pool(name: "POOL");
            var other = Service.GetOrCreatePool<OtherPoolObject>(new ObjectPoolCreateOptions("pool"));
            Assert.That(Service.Count, Is.EqualTo(4));
            Assert.That(Service.HasObjectPool<PoolObject>(null), Is.True);
            Assert.That(Service.HasObjectPool<OtherPoolObject>(), Is.False);
            Assert.That(Service.GetObjectPool<PoolObject>("missing"), Is.Null);
            Assert.That(Service.DestroyObjectPool<PoolObject>("pool"), Is.True);
            Assert.That(Service.GetObjectPool<OtherPoolObject>("pool"), Is.SameAs(other));
            Assert.That(Service.GetObjectPool<PoolObject>("POOL"), Is.SameAs(upper));
            Assert.That(Service.GetObjectPool<PoolObject>(), Is.SameAs(unnamed));
            Assert.That(Service.DestroyObjectPool<PoolObject>("missing"), Is.False);
            Assert.That(named.Count, Is.Zero);
        }

        [TestCase(false, false, "")]
        [TestCase(false, true, "")]
        [TestCase(true, false, "named")]
        [TestCase(true, true, "named")]
        public void RegisterAndRentPreserveCallbackOrderAndCountIsNotTheNumberOfLeases(bool multi, bool spawned, string name)
        {
            var pool = Pool(multi);
            var obj = Item(name);
            var probe = obj.Probe;
            Assert.That(pool.Register(obj, spawned), Is.True);
            Assert.That(probe.Spawns, Is.EqualTo(spawned ? 1 : 0));
            Ledger((ObjectPoolBase)pool, 1, spawned ? 0 : 1, spawned ? 1 : 0);
            if (!spawned || multi)
            {
                Assert.That(pool.Spawn(name), Is.SameAs(obj));
                if (spawned) pool.Unspawn(obj);
            }
            else Assert.That(pool.Spawn(name), Is.Null);
            Assert.That(pool.Count, Is.EqualTo(1));
            pool.UnspawnTarget(obj.Target);
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            pool.Release();
            Assert.That(probe.Calls[probe.Calls.Count - 2], Is.EqualTo("release"));
            Assert.That(probe.Calls[probe.Calls.Count - 1], Is.EqualTo("clear"));
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(Using<PoolObject>(), Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("named")]
        public void SpawnUsesExactNameAndNeverCreatesOnMiss(string name)
        {
            var pool = Pool();
            Assert.That(pool.Spawn(name), Is.Null);
            Assert.That(Using<PoolObject>(), Is.Zero);
            var obj = Item(name);
            pool.Register(obj, false);
            Assert.That(pool.Spawn("absent"), Is.Null);
            Assert.That(pool.Spawn(name), Is.SameAs(obj));
            Assert.That(pool.Spawn(name), Is.Null);
            pool.Unspawn(obj);
            if (string.IsNullOrEmpty(name)) Assert.That(pool.Spawn(), Is.SameAs(obj));
            else Assert.That(pool.Spawn(), Is.Null);
        }

        [Test]
        public void InvalidRegistrationRecyclesOnlyNonNullWrappersAndClearDoesNotDestroyTarget()
        {
            var pool = Pool();
            LogAssert.Expect(LogType.Error, new Regex("Object or target is invalid"));
            Assert.That(pool.Register(null, false), Is.False);
            var obj = Item();
            var probe = obj.Probe;
            obj.Bind(null);
            LogAssert.Expect(LogType.Error, new Regex("Object or target is invalid"));
            Assert.That(pool.Register(obj, true), Is.False);
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(probe.Releases, Is.Zero);
            Assert.That(Using<PoolObject>(), Is.Zero);
            obj = Item(locked: true);
            probe = obj.Probe;
            obj.LastUseTime = 5;
            MemoryPool<PoolObject>.Release(obj);
            Assert.That(obj.Target, Is.Null);
            Assert.That(obj.Name, Is.Null);
            Assert.That(obj.Locked, Is.False);
            Assert.That(obj.LastUseTime, Is.Zero);
            Assert.That(probe.Releases, Is.Zero);
        }

        [TestCase(0, false, false)]
        [TestCase(1, true, false)]
        [TestCase(1, false, true)]
        public void FullCapacityRejectsIncomingWrapperWhenNoUnusedObjectCanBeReleased(int capacity, bool spawned, bool locked)
        {
            var pool = Pool(capacity: capacity);
            if (capacity != 0) pool.Register(Item(locked: locked), spawned);
            var incoming = Item();
            var probe = incoming.Probe;
            LogAssert.Expect(LogType.Error, new Regex("capacity is full"));
            Assert.That(pool.Register(incoming, false), Is.False);
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(probe.Releases, Is.Zero);
            Ledger((ObjectPoolBase)pool, capacity, capacity == 0 || spawned ? 0 : 1, spawned ? 1 : 0);
        }

        [Test]
        public void FullCapacityReleasesOneUnusedEntryBeforeAcceptingIncomingWrapper()
        {
            var pool = Pool(capacity: 1);
            var old = Item("old");
            var probe = old.Probe;
            pool.Register(old, false);
            var incoming = Item("new");
            Assert.That(pool.Register(incoming, true), Is.True);
            Assert.That(probe.Releases, Is.EqualTo(1));
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(pool.Spawn("old"), Is.Null);
            Assert.That(Using<PoolObject>(), Is.EqualTo(1));
            Ledger((ObjectPoolBase)pool, 1, 0, 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReleaseSkipsLockedCustomBlockedAndRentedObjectsButTheyRemainRentable(bool multi)
        {
            var pool = Pool(multi);
            var locked = Item("locked", locked: true);
            var custom = Item("custom");
            custom.CanRelease = false;
            var rented = Item("rented");
            pool.Register(locked, false);
            pool.Register(custom, false);
            pool.Register(rented, true);
            pool.ReleaseAllUnused();
            Ledger((ObjectPoolBase)pool, 3, 2, 1);
            Assert.That(pool.Spawn("locked"), Is.SameAs(locked));
            pool.Unspawn(locked);
            Assert.That(pool.Spawn("custom"), Is.SameAs(custom));
            pool.Unspawn(custom);
            locked.Locked = false;
            custom.CanRelease = true;
            pool.Release(2);
            Ledger((ObjectPoolBase)pool, 1, 0, 1);
            pool.Unspawn(rented);
            pool.ReleaseAllUnused();
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
        }

        [TestCase(-1, 4)]
        [TestCase(0, 4)]
        [TestCase(1, 3)]
        [TestCase(2, 2)]
        [TestCase(20, 0)]
        public void ExplicitReleaseQuantityIsIndependentOfTickBudgetAndReleasesFifo(int quantity, int remaining)
        {
            var pool = Pool();
            var probes = new TargetProbe[4];
            for (int i = 0; i < 4; i++) { var obj = Item(); probes[i] = obj.Probe; pool.Register(obj, false); }
            pool.Release(quantity);
            Ledger((ObjectPoolBase)pool, remaining, remaining, 0);
            for (int i = 0; i < 4; i++) Assert.That(probes[i].Releases, Is.EqualTo(i < 4 - remaining ? 1 : 0));
            pool.Release();
            Assert.That(pool.Count, Is.Zero, "Parameterless Release means all unused.");
        }

        [Test]
        public void UnspawnNullAndUnknownTargetDoNotChangeAnyLedger()
        {
            var pool = Pool();
            var owned = Item();
            pool.Register(owned, true);
            pool.Unspawn(null);
            pool.UnspawnTarget(null);
            var stranger = Item();
            LogAssert.Expect(LogType.Error, new Regex("Cannot find target"));
            pool.Unspawn(stranger);
            LogAssert.Expect(LogType.Error, new Regex("Cannot find target"));
            pool.UnspawnTarget(new object());
            Ledger((ObjectPoolBase)pool, 1, 0, 1);
            MemoryPool<PoolObject>.Release(stranger);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameNameAndUnnamedChainsSurviveMiddleRemovalAndSlotReuse(bool multi)
        {
            var pool = Pool(multi);
            var a = Item("same", locked: true);
            var b = Item("same");
            var c = Item("same", locked: true);
            var unnamed = Item();
            pool.Register(a, false); pool.Register(b, false); pool.Register(c, false); pool.Register(unnamed, false);
            pool.Release(1);
            Ledger((ObjectPoolBase)pool, 3, 3, 0);
            Assert.That(pool.Spawn("same"), Is.SameAs(c));
            Assert.That(pool.Spawn(), Is.SameAs(unnamed));
            Ledger((ObjectPoolBase)pool, 3, 1, 2);
            var replacement = Item("new");
            pool.Register(replacement, true);
            Ledger((ObjectPoolBase)pool, 4, 1, 3);
            Assert.That(pool.Spawn("new"), multi ? Is.SameAs(replacement) : Is.Null);
        }

        [UnityTest]
        public IEnumerator CapacityDownshiftReleasesExactlyEightPerTickAndThousandObjectsTake125Ticks()
        {
            var pool = Pool();
            for (int i = 0; i < 1000; i++) pool.Register(Item(), false);
            Assert.That(Active(Service), Is.Zero);
            pool.Capacity = 0;
            Assert.That(pool.Count, Is.EqualTo(1000));
            for (int frame = 1; frame <= 125; frame++)
            {
                Tick();
                Assert.That(pool.Count, Is.EqualTo(1000 - frame * 8));
                Assert.That(Unused(pool), Is.EqualTo(pool.Count));
                Assert.That(Using<PoolObject>(), Is.EqualTo(pool.Count));
                if ((frame & 7) == 0) yield return null;
            }
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(Active(Service), Is.Zero);
            TestContext.WriteLine("BUDGET,objects=1000,perTick=8,ticks=125");
        }

        [Test]
        public void InvalidNegativePropertyValuesLogAndPreserveConfiguration()
        {
            var pool = Pool(capacity: 3, expire: 4, interval: 5);
            LogAssert.Expect(LogType.Error, new Regex("Capacity is invalid")); pool.Capacity = -1;
            LogAssert.Expect(LogType.Error, new Regex("ExpireTime is invalid")); pool.ExpireTime = -1;
            LogAssert.Expect(LogType.Error, new Regex("AutoReleaseInterval is invalid")); pool.AutoReleaseInterval = -1;
            Assert.That(pool.Capacity, Is.EqualTo(3));
            Assert.That(pool.ExpireTime, Is.EqualTo(4));
            Assert.That(pool.AutoReleaseInterval, Is.EqualTo(5));
        }

        [Test]
        public void DebugBuffersReturnActualCountAndStablePrioritySortOfTheCopiedPrefix()
        {
            var a = Pool(name: "a"); a.Priority = 2;
            var b = Pool(name: "b"); b.Priority = 1;
            var c = Pool(name: "c"); c.Priority = 1;
            LogAssert.Expect(LogType.Error, new Regex("Results is invalid"));
            Assert.That(Service.GetAllObjectPools(true, null), Is.Zero);
            var shortBuffer = new ObjectPoolBase[2];
            Assert.That(Service.GetAllObjectPools(true, shortBuffer), Is.EqualTo(3));
            Assert.That(shortBuffer.Select(p => p.Name), Is.EqualTo(new[] { "b", "a" }));
            var buffer = new ObjectPoolBase[5];
            Assert.That(Service.GetAllObjectPools(true, buffer), Is.EqualTo(3));
            Assert.That(buffer.Take(3).Select(p => p.Name), Is.EqualTo(new[] { "b", "c", "a" }));
            Assert.That(buffer[3], Is.Null);
            var concrete = (ObjectPoolBase)a;
            a.Register(Item("1"), false); a.Register(Item("2"), true);
            LogAssert.Expect(LogType.Error, new Regex("Results is invalid"));
            Assert.That(concrete.GetAllObjectInfos(null), Is.Zero);
            Assert.That(concrete.GetAllObjectInfos(Array.Empty<ObjectInfo>()), Is.EqualTo(2));
            Assert.That(concrete.GetAllObjectInfos(new ObjectInfo[1]), Is.EqualTo(2));
            Assert.That(a.FullName, Is.EqualTo(typeof(PoolObject).FullName + ".a"));
            Assert.That(ReferenceEquals(a.FullName, a.FullName), Is.True);
        }
    }
}
