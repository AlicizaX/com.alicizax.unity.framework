#if UNITY_EDITOR
using System.Collections;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceOwnerPerformanceTests
    {
        [UnityTest]
        public IEnumerator ContinuousNewOwnersDoNotAllocatePerOwnerStorage()
        {
            using var f = new ResourceFixture();
            string location = "owned";
            string package = f.Service.DefaultPackageName;
            var expected = f.Text(location);
            var held = f.Service.LoadLease<TextAsset>(location);
            var first = f.Owner();
            f.Service.LoadAsset<TextAsset>(first, location, package);
            first.ReleaseBindings();
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                var owners = new ResourceOwner[count];
                for (int i = 0; i < count; i++) owners[i] = f.Owner();
                TextAsset result = null;
                long allocated = 0;
                yield return AllocationCapture.Measure("continuous-new-owner-load-release", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        result = f.Service.LoadAsset<TextAsset>(owners[i], location, package);
                        owners[i].ReleaseBindings();
                    }
                }, sample => allocated = sample.Bytes);
                Assert.That(result, Is.SameAs(expected));
                Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(1));
                Assert.That(allocated, Is.Zero, "Existing shared storage has enough capacity; allocating another dictionary per new Owner is not a capacity-growth cost.");
            }
            held.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ConcurrentOwnerCapacityGrowthAndFreshOwnerReuse()
        {
            using var f = new ResourceFixture();
            string location = "owned-capacity";
            string package = f.Service.DefaultPackageName;
            f.Text(location);
            var held = f.Service.LoadLease<TextAsset>(location);
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                var owners = new ResourceOwner[count];
                var replacements = new ResourceOwner[count];
                for (int i = 0; i < count; i++)
                {
                    owners[i] = f.Owner();
                    replacements[i] = f.Owner();
                }
                yield return AllocationCapture.Measure("owner-concurrent-growth", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Service.LoadAsset<TextAsset>(owners[i], location, package);
                }, _ => { });
                Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(count + 1));
                yield return AllocationCapture.Measure("owner-bulk-release", count, () =>
                {
                    for (int i = 0; i < count; i++) owners[i].ReleaseBindings();
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(1));
                yield return AllocationCapture.Measure("owner-concurrent-fresh-reuse", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Service.LoadAsset<TextAsset>(replacements[i], location, package);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(count + 1));
                for (int i = 0; i < count; i++) replacements[i].ReleaseBindings();
            }
            held.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AutomaticOwnershipMeasuresFirstUseDeduplicationAndReuse()
        {
            using var f = new ResourceFixture();
            string location = "owned";
            string package = f.Service.DefaultPackageName;
            var expected = f.Text(location);
            var owner = f.Owner();
            TextAsset result = null;
            yield return AllocationCapture.Measure("owner-load-first-use", 1,
                () => result = f.Service.LoadAsset<TextAsset>(owner, location, package), _ => { });
            Assert.That(result, Is.SameAs(expected));
            var other = f.Owner();
            yield return AllocationCapture.Measure("owner-load-new-owner", 1,
                () => result = f.Service.LoadAsset<TextAsset>(other, location, package), _ => { });
            Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(2));
            yield return AllocationCapture.Measure("owner-first-release", 1, () => owner.ReleaseBindings(), _ => { }, true);
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(1));
            f.Service.LoadAsset<TextAsset>(owner, location, package);
            yield return AllocationCapture.Measure("owner-load-deduplicate", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) result = f.Service.LoadAsset<TextAsset>(owner, location, package);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("owner-release-and-reuse", 10000, () =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    owner.ReleaseBindings();
                    result = f.Service.LoadAsset<TextAsset>(owner, location, package);
                }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(f.Info(location).DirectRefCount, Is.EqualTo(2));
            owner.ReleaseBindings();
            other.ReleaseBindings();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
#endif
