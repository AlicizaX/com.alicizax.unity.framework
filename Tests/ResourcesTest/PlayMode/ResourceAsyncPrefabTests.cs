using System.Collections;
using System.Threading;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceAsyncPrefabTests
    {
        [UnityTest]
        public IEnumerator InstanceOwnsOneReferenceUntilDestroyed()
        {
            using var f = new ResourceFixture(true);
            f.Prefab("prefab");
            var instance = f.Keep(f.Service.Instantiate("prefab"));
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.GetComponent<ResourceOwner>().Scope.IsAlive, Is.True);
            Assert.That(f.Info("prefab").RefCount, Is.EqualTo(1));
            var second = f.Keep(f.Service.Instantiate("prefab"));
            Assert.That(f.Info("prefab").RefCount, Is.EqualTo(2));
            Object.Destroy(instance);
            Object.Destroy(second);
            yield return null;
            f.AssertNoReferences("prefab");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CanceledWaiterDoesNotInstantiateOrCancelAnotherConsumer()
        {
            using var f = new ResourceFixture(true);
            f.Prefab("prefab");
            f.Loader.CompleteImmediately = false;
            using var cts = new CancellationTokenSource();
            var canceled = f.Service.InstantiateAsync("prefab", null, cts.Token);
            var surviving = f.Service.InstantiateAsync("prefab");
            cts.Cancel();
            yield return null;
            f.Loader.Complete("prefab");
            yield return f.Wait(canceled, value => Assert.That(value, Is.Null));
            GameObject instance = null;
            yield return f.Wait(surviving, value => instance = f.Keep(value));
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.GetComponent<ResourceOwner>(), Is.Not.Null);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Info("prefab").RefCount, Is.EqualTo(1));
            Object.Destroy(instance);
            yield return null;
            f.AssertNoReferences("prefab");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyedParentRejectsALatePrefab()
        {
            using var f = new ResourceFixture(true);
            f.Prefab("prefab");
            var parent = f.GameObject("parent");
            f.Loader.CompleteImmediately = false;
            var pending = f.Service.InstantiateAsync("prefab", parent.transform);
            Object.Destroy(parent);
            yield return null;
            f.Loader.Complete("prefab");
            yield return f.Wait(pending, value => Assert.That(value, Is.Null));
            f.AssertNoReferences("prefab");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PreCanceledRequestDoesNotReachTheBackend()
        {
            using var f = new ResourceFixture(true);
            var token = new CancellationToken(true);
            yield return f.Wait(f.Service.InstantiateAsync("prefab", null, token), value => Assert.That(value, Is.Null));
            Assert.That(f.Loader.Loads, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InstanceUnderAnInactiveParentIsReleasedByTheSweep()
        {
            using var f = new ResourceFixture(true);
            f.Prefab("prefab");
            var parent = f.GameObject("inactive", false);
            var instance = f.Service.Instantiate("prefab", parent.transform);
            Assert.That(instance.activeInHierarchy, Is.False);
            Assert.That(f.Info("prefab").RefCount, Is.EqualTo(1));
            Object.Destroy(parent);
            yield return null;
            yield return null;
            f.AssertNoReferences("prefab");
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator ResetDuringAsyncInstantiateReturnsNull()
        {
            using var f = new ResourceFixture(true);
            f.Prefab("prefab");
            f.Loader.CompleteImmediately = false;
            var pending = f.Service.InstantiateAsync("prefab");
            f.Service.ForceUnloadAllAssets();
            yield return f.Wait(pending, value => Assert.That(value, Is.Null));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FailedPrefabLoadReturnsNull()
        {
            using var f = new ResourceFixture(true);
            f.Prefab("prefab");
            f.Loader.CompleteImmediately = false;
            var pending = f.Service.InstantiateAsync("prefab");
            f.Loader.Complete("prefab", false);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Resource load failed"));
            yield return f.Wait(pending, value => Assert.That(value, Is.Null));
            f.AssertReleased("prefab");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
