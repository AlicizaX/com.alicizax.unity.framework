using System.Collections;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class GameObjectPoolTimeTests : GameObjectPoolFixture
    {
        private float _timeScale;

        [SetUp]
        public void SaveTimeScale()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreTimeScale() => Time.timeScale = _timeScale;

        [UnityTest]
        public IEnumerator BurstTrimUsesUnscaledTimeAndHonorsIdleSeconds()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Burst, minIdle: 0, soft: 8, hard: 16, idle: 0.05f));
            FillIdle("fx/hit", 3);
            Tick();
            Ledger("fx/hit", 3, 0, 3, true);
            yield return new WaitForSecondsRealtime(0.08f);
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Ledger("fx/hit", 0, 0, 0, false);
        }

        [UnityTest]
        public IEnumerator TimeScaleZeroDoesNotFreezeBurstIdleTrim()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Burst, minIdle: 0, soft: 8, hard: 16, idle: 0.05f));
            FillIdle("fx/hit", 3);
            Time.timeScale = 0f;
            Tick();
            Ledger("fx/hit", 3, 0, 3, true);
            yield return new WaitForSecondsRealtime(0.08f);
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Ledger("fx/hit", 0, 0, 0, false);
        }
    }
}
