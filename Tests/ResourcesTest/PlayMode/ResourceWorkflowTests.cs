using System.Collections;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceWorkflowTests
    {
        private ResourceFixture f;

        [SetUp]
        public void SetUp()
        {
            f = new ResourceFixture(true);
            AppServices.EnsureWorld();
            AppServices.App.Register<IResourceService>(f.Service);
        }

        [TearDown]
        public void TearDown()
        {
            AppServices.Shutdown();
            f.Dispose();
        }

        [UnityTest]
        public IEnumerator EveryImplicitOverloadAppliesThenClears()
        {
            var sprite = f.Sprite("s");
            var atlas = f.Sprite("atlas");
            var material = f.Material("m");
            var image = f.GameObject("image").AddComponent<Image>();
            var spriteRenderer = f.GameObject("sprite").AddComponent<SpriteRenderer>();
            var meshRenderer = f.GameObject("mesh").AddComponent<MeshRenderer>();
            var materialImage = f.GameObject("material-image").AddComponent<Image>();
            image.SetSprite("s");
            spriteRenderer.SetSubSprite("atlas", "atlas");
            materialImage.SetMaterial("m");
            meshRenderer.SetMaterialInstance("m");
            yield return null;
            yield return null;
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(spriteRenderer.sprite, Is.SameAs(atlas));
            Assert.That(materialImage.material, Is.SameAs(material));
            var instance = meshRenderer.sharedMaterial;
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance, Is.Not.SameAs(material));
            foreach (var target in new Component[] { image, spriteRenderer, meshRenderer, materialImage })
                Assert.That(target.GetComponent<ResourceOwner>(), Is.Not.Null);
            image.SetSprite("s", ResourceBindingOptions.SetNativeSize);
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(4f, 4f)));
            spriteRenderer.SetSprite("s");
            Assert.That(spriteRenderer.sprite, Is.SameAs(sprite));
            image.SetSubSprite("atlas", "atlas");
            Assert.That(image.sprite, Is.SameAs(atlas));
            meshRenderer.SetSharedMaterial("m");
            Assert.That(meshRenderer.sharedMaterial, Is.SameAs(material));
            yield return null;
            Assert.That(instance == null, Is.True);
            image.SetSprite("");
            spriteRenderer.SetSprite("");
            materialImage.SetMaterial("");
            meshRenderer.SetSharedMaterial("");
            Assert.That(image.sprite, Is.Null);
            Assert.That(spriteRenderer.sprite, Is.Null);
            Assert.That(meshRenderer.sharedMaterial, Is.Null);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            Assert.That(f.Service.GetStats().ReferenceCount, Is.Zero);
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator ImplicitBindingsFollowTheGameObjectLifetime()
        {
            f.Sprite("s");
            f.Material("m");
            var go = f.GameObject("item");
            var image = go.AddComponent<Image>();
            image.SetSprite("s", ResourceBindingOptions.Synchronous);
            go.AddComponent<MeshRenderer>().SetMaterialInstance("m", ResourceBindingOptions.Synchronous);
            var instance = go.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(f.Service.GetStats().ReferenceCount, Is.EqualTo(2));
            Object.Destroy(go);
            yield return null;
            Assert.That(f.Service.GetStats().ReferenceCount, Is.Zero);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            yield return null;
            Assert.That(instance == null, Is.True);
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator ViewScopeOwnsLoadsAndBindingsUntilDisposed()
        {
            var icon = f.Sprite("icon");
            var config = f.Text("config");
            var view = f.Service.CreateScope("View");
            var image = f.GameObject("icon").AddComponent<Image>();
            image.SetSprite(view, f.Service.Key<Sprite>("icon"));
            Assert.That(view.Load<TextAsset>("config"), Is.SameAs(config));
            TextAsset loaded = null;
            yield return f.Wait(view.LoadAsync<TextAsset>("config"), value => loaded = value);
            Assert.That(loaded, Is.SameAs(config));
            yield return null;
            Assert.That(image.sprite, Is.SameAs(icon));
            Assert.That(image.GetComponent<ResourceOwner>(), Is.Null, "An explicit scope never adds an owner component.");
            view.Dispose();
            Assert.That(image.sprite, Is.Null);
            Assert.That(f.Service.GetStats().ReferenceCount, Is.Zero);
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator UnavailableBackendLeavesTheTargetUntouched()
        {
            f.Sprite("s");
            var image = f.GameObject().AddComponent<Image>();
            f.Loader.IsAvailable = false;
            image.SetSprite("s");
            image.SetSprite("s", ResourceBindingOptions.Synchronous);
            yield return null;
            Assert.That(image.sprite, Is.Null);
            Assert.That(f.Loader.Loads, Is.Zero);
            Assert.That(f.Service.GetStats().ReferenceCount, Is.Zero);
            f.Loader.IsAvailable = true;
            image.SetSprite("s", ResourceBindingOptions.Synchronous);
            Assert.That(image.sprite, Is.Not.Null);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ImplicitCachedShortcutDoesNotAllocate()
        {
            string location = "s";
            f.Sprite(location);
            var target = f.GameObject().AddComponent<SpriteRenderer>();
            target.SetSprite(location, ResourceBindingOptions.Synchronous);
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                yield return AllocationCapture.Measure("implicit-shortcut-cached", count, () =>
                {
                    for (int i = 0; i < count; i++) target.SetSprite(location);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
            }
            Assert.That(f.Info("s").RefCount, Is.EqualTo(1));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
        }
#endif
    }
}
