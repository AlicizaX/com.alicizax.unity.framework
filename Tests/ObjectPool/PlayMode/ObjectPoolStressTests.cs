using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ObjectPoolStressTests : ObjectPoolFixture
    {
        private static PoolObject Silent(object target, string name = "")
        {
            var obj = MemoryPool<PoolObject>.Acquire();
            obj.Bind(target, name);
            obj.Probe = null;
            return obj;
        }

        [UnityTest]
        public IEnumerator ScaleRegisterSpawnUnspawnAndOverCapacityConverge()
        {
            foreach (int size in new[] { 1000, 4000, 10000 })
            {
                var named = Pool(true, name: "n" + size);
                var unnamed = Pool(name: "u" + size);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < size; i++)
                {
                    named.Register(Silent(new object(), i % 8 == 0 ? "audio" : "fx"), i % 3 == 0);
                    unnamed.Register(Silent(new object()), false);
                }
                int namedUnused = Unused(named);
                Ledger((ObjectPoolBase)named, size, namedUnused);
                Ledger((ObjectPoolBase)unnamed, size, size, 0);
                for (int i = 0; i < size; i++)
                {
                    var rented = named.Spawn("audio") ?? named.Spawn("fx");
                    if (rented != null) named.Unspawn(rented);
                    var idle = unnamed.Spawn();
                    if (idle != null) unnamed.Unspawn(idle);
                }
                unnamed.Capacity = 0;
                int frames = 0;
                while (unnamed.Count > 0)
                {
                    Tick();
                    frames++;
                    Assert.That(frames, Is.LessThanOrEqualTo((size + 7) / 8));
                    if ((frames & 15) == 0) yield return null;
                }
                clock.Stop();
                TestContext.WriteLine($"STRESS,size={size},ms={clock.Elapsed.TotalMilliseconds:F3},capacityTicks={frames},named={named.Count},unused={Unused(named)},active={Active(Service)},using={Using<PoolObject>()}");
                Assert.That(Using<PoolObject>(), Is.EqualTo(named.Count));
                Service.DestroyObjectPool<PoolObject>("n" + size);
                Service.DestroyObjectPool<PoolObject>("u" + size);
                Assert.That(Using<PoolObject>(), Is.Zero);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RepeatedCreateDestroyDoesNotLeakMemoryPoolLeases()
        {
            for (int round = 0; round < 50; round++)
            {
                var pool = Pool(name: "round");
                for (int i = 0; i < 32; i++) pool.Register(Silent(new object(), i % 2 == 0 ? "a" : ""), i % 2 == 0);
                Assert.That(Service.DestroyObjectPool<PoolObject>("round"), Is.True);
            }
            Assert.That(Using<PoolObject>(), Is.Zero);
            Assert.That(Service.Count, Is.Zero);
            yield return null;
        }
    }
}
