using System;
using NUnit.Framework;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class HashMapTests : ObjectPoolFixture
    {
        private readonly struct Collision : IEquatable<Collision>
        {
            private readonly int value;
            public Collision(int value) { this.value = value; }
            public bool Equals(Collision other) => value == other.value;
            public override int GetHashCode() => 7;
        }
        private sealed class EqualTarget
        {
            public int Calls;
            public override bool Equals(object other) { Calls++; return true; }
            public override int GetHashCode() { Calls++; return 0; }
        }

        [TestCase(8)]
        [TestCase(1000)]
        [TestCase(4000)]
        public void CollisionChainsGrowRemoveAndReuseEverySlotWithoutLosingMappings(int count)
        {
            var map = new OpenHashMap<Collision>(1);
            try
            {
                Assert.That(Read<int>(map, "m_Mask"), Is.EqualTo(7));
                Assert.That(Read<int[]>(map, "m_Buckets").Length, Is.GreaterThanOrEqualTo(8));
                for (int i = 0; i < count; i++) map.AddOrUpdate(new Collision(i), i);
                for (int i = 0; i < count; i += 2) Assert.That(map.Remove(new Collision(i)), Is.True);
                Assert.That(map.Remove(new Collision(-1)), Is.False);
                for (int i = 1; i < count; i += 2) { Assert.That(map.TryGetValue(new Collision(i), out int value), Is.True); Assert.That(value, Is.EqualTo(i)); }
                for (int i = 0; i < count; i++) map.AddOrUpdate(new Collision(i), -i);
                Assert.That(map.Count, Is.EqualTo(count));
                for (int i = 0; i < count; i++) { Assert.That(map.TryGetValue(new Collision(i), out int value), Is.True); Assert.That(value, Is.EqualTo(-i)); }
            }
            finally { map.Dispose(); }
            map.Dispose();
            Assert.That(map.Count, Is.Zero);
            Assert.That(map.TryGetValue(new Collision(1), out _), Is.False);
            Assert.That(map.Remove(new Collision(1)), Is.False);
            foreach (string field in new[] { "m_Buckets", "m_Keys", "m_Values", "m_Next" }) Assert.That(Read<Array>(map, field), Is.Null);
        }

        [Test]
        public void ReferenceMapAndObjectPoolIgnoreTargetValueEqualityAndVirtualHashCode()
        {
            var first = new EqualTarget(); var second = new EqualTarget();
            var firstKey = new RefKey(first);
            var secondKey = new RefKey(second);
            var map = new OpenHashMap<RefKey>(1);
            try
            {
                map.AddOrUpdate(firstKey, 3); map.AddOrUpdate(secondKey, 9);
                Assert.That(map.Count, Is.EqualTo(2));
                Assert.That(map.TryGetValue(firstKey, out int value), Is.True); Assert.That(value, Is.EqualTo(3));
                Assert.That(map.Remove(firstKey), Is.True);
                Assert.That(map.TryGetValue(secondKey, out value), Is.True); Assert.That(value, Is.EqualTo(9));
            }
            finally { map.Dispose(); }
            Assert.That(Read<Array>(map, "m_Keys"), Is.Null);
            var pool = Pool(); pool.Register(Item(target: first), true); pool.Register(Item(target: second), true);
            pool.UnspawnTarget(first);
            Ledger((ObjectPoolBase)pool, 2, 1, 1);
            Assert.That(first.Calls + second.Calls, Is.Zero);
        }

        [Test]
        public void PoolKeyNormalizesNullNameAndRejectsNullType()
        {
            var key = new ObjectPoolKey(typeof(PoolObject), null);
            Assert.That(key.Equals(new ObjectPoolKey(typeof(PoolObject), "")), Is.True);
            Assert.That(key.GetHashCode(), Is.EqualTo(new ObjectPoolKey(typeof(PoolObject), "").GetHashCode()));
            Assert.That(key.Equals(new ObjectPoolKey(typeof(OtherPoolObject), "")), Is.False);
            Assert.That(key.ToString(), Is.EqualTo(typeof(PoolObject).FullName));
            Assert.Throws<ArgumentNullException>(() => new ObjectPoolKey(null, ""));
        }
    }
}
