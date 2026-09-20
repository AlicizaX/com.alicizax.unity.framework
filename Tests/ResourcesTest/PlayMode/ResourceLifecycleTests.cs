using System.Collections;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceLifecycleTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [UnityTest]
        public IEnumerator ReentrantReplacementOfNeverActivatedOwnerKeepsOneRegistration()
        {
            f.Material("old");
            var expected = f.Material("new");
            var previous = f.Owner(false);
            var ownerObject = previous.gameObject;
            var image = f.Keep(new GameObject("active-target")).AddComponent<Image>();
            f.Bindings.BindImageMaterial(previous, image, new ResourceKey("old"));
            Object.Destroy(previous);
            yield return null;
            var replacement = ownerObject.AddComponent<ResourceOwner>();
            bool reentered = false;
            image.RegisterDirtyMaterialCallback(() =>
            {
                if (reentered) return;
                reentered = true;
                Assert.That(f.Bindings.BindImageMaterial(replacement, image, new ResourceKey("new")), Is.EqualTo(ResourceBindStatus.Success));
            });
            Assert.That(f.Bindings.RegisterOwner(replacement), Is.EqualTo(ResourceBindStatus.Success));
            Assert.That(reentered, Is.True);
            Assert.That(image.material, Is.SameAs(expected));
            var infos = new ResourceOwnerInfo[f.Bindings.GetOwnerInfos(null, 0, 0)];
            f.Bindings.GetOwnerInfos(infos, 0, infos.Length);
            int active = 0;
            foreach (var info in infos) if (info.Active) active++;
            Assert.That(active, Is.EqualTo(1), "Reentrant registration must not duplicate the replacement owner.");
            replacement.ReleaseBindings();
            f.AssertNoReferences("new");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NeverActivatedOwnerIsReclaimed()
        {
            f.Sprite("a");
            var owner = f.Owner(false);
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, target, new ResourceKey("a"));
            Object.Destroy(owner.gameObject);
            yield return null;
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            f.AssertNoReferences("a");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyedTargetReleasesWithoutDestroyingOwner()
        {
            f.Sprite("a");
            var owner = f.Owner();
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, target, new ResourceKey("a"));
            Object.Destroy(target);
            yield return null;
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            f.AssertNoReferences("a");
        }

        [UnityTest]
        public IEnumerator DisabledHierarchyRetainsUntilExplicitPoolReturn()
        {
            var sprite = f.Sprite("a");
            var parent = f.Keep(new GameObject("parent"));
            var owner = f.Owner();
            owner.transform.SetParent(parent.transform);
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, target, new ResourceKey("a"));
            owner.enabled = false;
            target.enabled = false;
            parent.SetActive(false);
            yield return null;
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            Assert.That(f.Info("a").BindingRefCount, Is.EqualTo(1));
            Assert.That(target.sprite, Is.SameAs(sprite));
            ResourceOwner.ReleaseBindingsInHierarchy(parent);
            f.AssertNoReferences("a");
            parent.SetActive(true);
            f.Bindings.BindSprite(owner, target, new ResourceKey("a"));
            Assert.That(f.Info("a").BindingRefCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SceneUnloadReleasesInactiveOwner()
        {
            f.Sprite("a");
            UnityEngine.SceneManagement.Scene scene = SceneManager.CreateScene("ResourceAuditScene");
            var owner = f.Owner(false);
            SceneManager.MoveGameObjectToScene(owner.gameObject, scene);
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, target, new ResourceKey("a"));
            yield return SceneManager.UnloadSceneAsync(scene);
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            f.AssertNoReferences("a");
        }
    }
}
