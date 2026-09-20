using System.Collections;
using System.Text.RegularExpressions;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class GameObjectPoolStressTests : GameObjectPoolFixture
    {
        [UnityTest]
        public IEnumerator InterleavedSpawnDespawnFlushAndExternalDestroyConverge()
        {
            Prefab("fx/a");
            Prefab("fx/b");
            Catalog(
                Entry("fx/a", PoolPolicy.Burst, minIdle: 2, soft: 32, hard: 64, idle: 60f, group: "A"),
                Entry("fx/b", PoolPolicy.Fixed, minIdle: 1, soft: 16, hard: 32, group: "B"));
            yield return Wait(Service.WarmupAsync("fx/a", 24));
            yield return Wait(Service.WarmupAsync("fx/b", 12));
            Ledger("fx/a", 24, 0, 24, true);
            Ledger("fx/b", 12, 0, 12, true);
            var live = new GameObject[20];
            for (int i = 0; i < live.Length; i++)
                live[i] = MustSpawn(i % 2 == 0 ? "fx/a" : "fx/b");
            for (int i = 0; i < live.Length; i += 2)
                Service.Despawn(live[i]);
            LogAssert.Expect(LogType.Warning, new Regex("destroyed outside pool"));
            UnityEngine.Object.Destroy(live[1]);
            yield return null;
            Service.Flush("fx/b");
            TickUntil("fx/b", pool => pool.InactiveCount <= 1, 8);
            for (int i = 3; i < live.Length; i += 2)
                Service.Despawn(live[i]);
            Assert.That(Summary().ActiveInstanceCount, Is.Zero);
            Assert.That(RuntimePool("fx/a").TotalCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(RuntimePool("fx/a").ActiveCount + RuntimePool("fx/a").InactiveCount, Is.EqualTo(RuntimePool("fx/a").TotalCount));
            Assert.That(RuntimePool("fx/b").ActiveCount + RuntimePool("fx/b").InactiveCount, Is.EqualTo(RuntimePool("fx/b").TotalCount));
        }

        [UnityTest]
        public IEnumerator WarmupAndCycleFourThousandInstancesKeepsLedger()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 0, soft: 4096, hard: 4096));
            yield return Wait(Service.WarmupAsync("fx/hit", 4096), 8192);
            Ledger("fx/hit", 4096, 0, 4096, true);
            Assert.That(ExpandCount("fx/hit"), Is.EqualTo(4096));
            for (int i = 0; i < 4096; i++)
            {
                GameObject instance = MustSpawn("fx/hit");
                Service.Despawn(instance);
            }

            Ledger("fx/hit", 4096, 0, 4096, true);
            Assert.That(HitCount("fx/hit"), Is.EqualTo(4096));
            Assert.That(RuntimePool("fx/hit").ActiveCount + RuntimePool("fx/hit").InactiveCount,
                Is.EqualTo(RuntimePool("fx/hit").TotalCount));
        }
    }
}
