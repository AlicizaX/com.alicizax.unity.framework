using System.Collections;
using System.Text.RegularExpressions;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class GameObjectPoolLifecycleTests : GameObjectPoolFixture
    {
        [UnityTest]
        public IEnumerator ExternalDestroyRemovesSlotWithoutSecondDestroy()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            LogAssert.Expect(LogType.Warning, new Regex("destroyed outside pool"));
            UnityEngine.Object.Destroy(instance);
            yield return null;
            Assert.That(instance == null, Is.True);
            Ledger("fx/hit", 0, 0, 0, true);
            Assert.That(DestroyCount("fx/hit"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExternalDestroyOfIdleInstanceUpdatesInactiveLedger()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            FillIdle("fx/hit", 2);
            GameObject idle = MustSpawn("fx/hit");
            Service.Despawn(idle);
            LogAssert.Expect(LogType.Warning, new Regex("destroyed outside pool"));
            UnityEngine.Object.Destroy(idle);
            yield return null;
            Ledger("fx/hit", 1, 0, 1, true);
        }

        [UnityTest]
        public IEnumerator SceneUnloadDestroysParentedInstancesAndKeepsService()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            var scene = SceneManager.CreateScene("gop-scene");
            var parentObject = new GameObject("gop-scene-parent");
            SceneManager.MoveGameObjectToScene(parentObject, scene);
            LogAssert.Expect(LogType.Warning, new Regex("destroyed outside pool"));
            MustSpawn("fx/hit", parentObject.transform);
            yield return SceneManager.UnloadSceneAsync(scene);
            Ledger("fx/hit", 0, 0, 0, true);
            Assert.That(Service.LoadPrefab("fx/hit"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator SetActiveAndParentSurvivePlayerLoop()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            var parent = Keep(new GameObject("live-parent")).transform;
            GameObject instance = MustSpawn("fx/hit", parent);
            yield return null;
            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.transform.parent, Is.EqualTo(parent));
            Service.Despawn(instance);
            yield return null;
            Assert.That(instance.activeSelf, Is.False);
            Assert.That(instance.transform.parent, Is.EqualTo(GroupRoot(PoolEntry.DefaultGroup)));
        }
    }
}
