using System.Collections;
using System.Threading;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceAsyncPrefabTests
    {
        [UnityTest]
        public IEnumerator CanceledWaiterDoesNotInstantiateOrCancelAnotherPrefabConsumer()
        {
            using var f = new ResourceFixture();
            var source = f.Keep(new GameObject("source"));
            f.Loader.Assets["prefab"] = source;
            f.Loader.CompleteImmediately = false;
            using var cts = new CancellationTokenSource();
            var canceled = f.Service.LoadGameObjectAsync("prefab", cancellationToken: cts.Token);
            var surviving = f.Service.LoadGameObjectAsync("prefab");
            cts.Cancel();
            f.Loader.Complete("prefab");
            yield return ResourceFixture.Wait(canceled, value => Assert.That(value, Is.Null));
            GameObject instance = null;
            yield return ResourceFixture.Wait(surviving, value => instance = f.Keep(value));
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.GetComponent<ResourceOwner>(), Is.Not.Null);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Info("prefab").RefCountTotal, Is.EqualTo(1));
            Object.Destroy(instance);
            yield return null;
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            f.AssertNoReferences("prefab");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyedParentRejectsLatePrefabWithoutInstantiatingAtTheSceneRoot()
        {
            using var f = new ResourceFixture();
            var source = f.Keep(new GameObject("source"));
            f.Loader.Assets["prefab"] = source;
            var parent = f.Keep(new GameObject("parent"));
            f.Loader.CompleteImmediately = false;
            var pending = f.Service.LoadGameObjectAsync("prefab", parent.transform);
            Object.Destroy(parent);
            yield return null;
            f.Loader.Complete("prefab");
            yield return ResourceFixture.Wait(pending, value => Assert.That(value, Is.Null));
            f.AssertNoReferences("prefab");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PreCanceledPrefabRequestDoesNotReachTheBackend()
        {
            using var f = new ResourceFixture();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            yield return ResourceFixture.Wait(f.Service.LoadGameObjectAsync("prefab", cancellationToken: cts.Token), value => Assert.That(value, Is.Null));
            Assert.That(f.Loader.Loads, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
