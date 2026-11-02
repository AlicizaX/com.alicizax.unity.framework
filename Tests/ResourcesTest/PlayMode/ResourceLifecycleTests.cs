using System.Collections;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceLifecycleTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [UnityTest]
        public IEnumerator DestroyingAnActiveOwnerDisposesItsScope()
        {
            f.Text("a");
            f.Sprite("s");
            var go = f.GameObject("owner");
            var scope = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            scope.Load<TextAsset>("a");
            go.AddComponent<SpriteRenderer>().SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            Object.Destroy(go);
            yield return null;
            Assert.That(scope.IsAlive, Is.False);
            f.AssertNoReferences("a");
            f.AssertNoReferences("s");
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator NeverActivatedOwnerIsReclaimedByTheSweep()
        {
            f.Text("a");
            var go = f.GameObject("inactive-owner", false);
            var scope = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            scope.Load<TextAsset>("a");
            Object.Destroy(go);
            yield return null;
            Assert.That(scope.IsAlive, Is.True, "OnDestroy never runs for an object that was never activated.");
            f.Tick();
            Assert.That(scope.IsAlive, Is.False);
            f.AssertNoReferences("a");
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator DestroyedTargetReleasesItsBindingWhileTheOwnerSurvives()
        {
            f.Text("a");
            f.Sprite("s");
            var go = f.GameObject("owner");
            var scope = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            scope.Load<TextAsset>("a");
            var child = f.GameObject("child");
            child.transform.SetParent(go.transform);
            child.AddComponent<SpriteRenderer>().SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            Object.Destroy(child);
            yield return null;
            f.Tick();
            Assert.That(scope.IsAlive, Is.True);
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            f.AssertNoReferences("s");
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator DisabledHierarchyRetainsResourcesUntilTheScopeIsDisposed()
        {
            var sprite = f.Sprite("s");
            var parent = f.GameObject("parent");
            var go = f.GameObject("owner");
            go.transform.SetParent(parent.transform);
            var scope = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            var target = go.AddComponent<SpriteRenderer>();
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            parent.SetActive(false);
            yield return null;
            f.Tick();
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Info("s").RefCount, Is.EqualTo(1));
            scope.Dispose();
            Assert.That(target.sprite, Is.Null);
            f.AssertNoReferences("s");
            parent.SetActive(true);
            var next = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            target.SetSprite(next, "s");
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Info("s").RefCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SceneUnloadReleasesAnInactiveOwner()
        {
            f.Sprite("s");
            var scene = SceneManager.CreateScene("ResourceAuditScene");
            var go = new GameObject("scene-owner");
            go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, scene);
            var scope = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            go.AddComponent<SpriteRenderer>().SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            yield return SceneManager.UnloadSceneAsync(scene);
            f.Tick();
            Assert.That(scope.IsAlive, Is.False);
            f.AssertNoReferences("s");
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator OwnerFromAnotherServiceIsReboundToTheRequestingService()
        {
            f.Text("a");
            using var other = new ResourceFixture();
            other.Text("a");
            var go = f.GameObject("shared-owner");
            var first = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            first.Load<TextAsset>("a");
            var second = other.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            second.Load<TextAsset>("a");
            Assert.That(go.GetComponents<ResourceOwner>().Length, Is.EqualTo(1));
            Object.Destroy(go);
            yield return null;
            f.Tick();
            other.Tick();
            f.AssertNoReferences("a");
            other.AssertNoReferences("a");
        }
    }
}
