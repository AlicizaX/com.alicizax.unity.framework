#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceStressTests
    {
        [UnityTest]
        public IEnumerator UniqueKeysAndContinuousEvictionConverge()
        {
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                using var f = new ResourceFixture();
                f.Service.IdleAssetCapacity = 64;
                var keys = new ResourceKey[count];
                var asset = f.Text("template");
                for (int i = 0; i < count; i++)
                {
                    string location = "key-" + i;
                    f.Loader.Assets.Add(location, asset);
                    keys[i] = ResourceKey.Asset<TextAsset>(location);
                }
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    int currentCycle = cycle;
                    yield return AllocationCapture.Measure("unique-key-churn-cycle-" + cycle, count, () =>
                    {
                        for (int i = 0; i < count; i++) f.Service.LoadLease<TextAsset>(keys[i]).Dispose();
                    }, sample => TestContext.WriteLine($"FRAMEWORK,unique-key-churn,{count},{currentCycle},{sample.Bytes - sample.BackendBytes}"));
                    Snapshot(f, "idle", count);
                    Assert.That(f.Loader.LiveHandles, Is.EqualTo(Math.Min(count, 64)));
                    Assert.That(CountReferences(f), Is.Zero);
                    Assert.That(ReadInt(f.Service, "_assetSlotNextIndex"), Is.LessThanOrEqualTo(65));
                    f.Service.UnloadUnusedAssets(true);
                    Assert.That(f.Loader.LiveHandles, Is.Zero);
                    Assert.That(ReadCount(f.Service, "_assetLoadingOperationByKey"), Is.Zero);
                    Assert.That(ReadCount(f.Service, "_resourceLocationIds"), Is.Zero);
                    Snapshot(f, "released", count);
                }
            }
        }

        [UnityTest]
        public IEnumerator SharedConcurrencyAndMassCancellationConverge()
        {
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                using var f = new ResourceFixture();
                f.Text("a");
                f.Loader.CompleteImmediately = false;
                f.Service.IdleAssetCapacity = 0;
                var tasks = new UniTask<ResourceAssetLease<TextAsset>>[count];
                using var canceled = new CancellationTokenSource();
                var key = ResourceKey.Asset<TextAsset>("a");
                yield return AllocationCapture.Measure("shared-async-start", count, () =>
                {
                    for (int i = 0; i < count; i++) tasks[i] = f.Service.LoadLeaseAsync<TextAsset>(key, i % 2 == 0 ? canceled.Token : default);
                }, sample => TestContext.WriteLine($"FRAMEWORK,shared-async-start,{count},{sample.Bytes - sample.BackendBytes}"));
                Assert.That(f.Loader.Loads, Is.EqualTo(1));
                Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
                canceled.Cancel();
                f.Loader.Complete("a");
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    yield return ResourceFixture.Wait(tasks[i], lease =>
                    {
                        Assert.That(lease.IsValid, Is.EqualTo(index % 2 != 0));
                        lease.Dispose();
                    });
                }
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                Assert.That(CountReferences(f), Is.Zero);
                Assert.That(ReadCount(f.Service, "_assetLoadingOperationByKey"), Is.Zero);
                Snapshot(f, "canceled-and-released", count);
            }
        }

        [UnityTest]
        public IEnumerator RepeatedSharedAsyncBurstsMeasureRequestReuse()
        {
            using var f = new ResourceFixture();
            f.Text("burst");
            f.Loader.CompleteImmediately = false;
            f.Service.IdleAssetCapacity = 0;
            var key = ResourceKey.Asset<TextAsset>("burst");
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                var tasks = new UniTask<ResourceAssetLease<TextAsset>>[count];
                for (int round = 0; round < 3; round++)
                {
                    yield return AllocationCapture.Measure("shared-burst-round-" + round, count, () =>
                    {
                        for (int i = 0; i < count; i++) tasks[i] = f.Service.LoadLeaseAsync<TextAsset>(key);
                    }, _ => { });
                    Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
                    f.Loader.Complete("burst");
                    yield return null;
                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(tasks[i].Status, Is.EqualTo(UniTaskStatus.Succeeded));
                        var lease = tasks[i].GetAwaiter().GetResult();
                        Assert.That(lease.IsValid, Is.True);
                        lease.Dispose();
                    }
                    Assert.That(f.Loader.LiveHandles, Is.Zero);
                    Assert.That(CountReferences(f), Is.Zero);
                    Assert.That(ReadCount(f.Service, "_assetLoadingOperationByKey"), Is.Zero);
                }
            }
        }

        [UnityTest]
        public IEnumerator OwnerCreationDestructionAndSparseMaintenanceConverge()
        {
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                using var f = new ResourceFixture();
                f.Sprite("sprite");
                f.Service.IdleAssetCapacity = 0;
                var owners = new ResourceOwner[count];
                var targets = new SpriteRenderer[count];
                var key = new ResourceKey("sprite", assetType: typeof(Sprite));
                yield return AllocationCapture.Measure("unity-owner-and-target-create", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        owners[i] = f.Owner(i % 2 == 0);
                        targets[i] = owners[i].gameObject.AddComponent<SpriteRenderer>();
                    }
                }, _ => { });
                yield return AllocationCapture.Measure("many-owner-binding", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Bindings.BindSprite(owners[i], targets[i], key);
                }, sample => TestContext.WriteLine($"FRAMEWORK,many-owner-binding,{count},{sample.Bytes - sample.BackendBytes}"));
                Assert.That(CountReferences(f), Is.EqualTo(count));
                yield return AllocationCapture.Measure("one-target-among-many-owners", 1000, () =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        f.Bindings.UnregisterTarget(owners[count - 1], targets[count - 1]);
                        f.Bindings.BindSprite(owners[count - 1], targets[count - 1], key);
                    }
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                for (int i = 0; i < count; i++) UnityEngine.Object.Destroy(owners[i].gameObject);
                yield return null;
                int sweeps = (count + 63) / 64 + 1;
                yield return AllocationCapture.Measure("destroyed-owner-maintenance", sweeps, () =>
                {
                    for (int i = 0; i < sweeps; i++) f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
                }, sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.Zero), true);
                Assert.That(CountReferences(f), Is.Zero);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                Assert.That(ReadCount(f.Bindings, "_ownerByObject"), Is.Zero);
                Assert.That(ReadCount(f.Bindings, "_targetByObject"), Is.Zero);
                Snapshot(f, "owners-destroyed", count);
            }
        }

        [UnityTest]
        public IEnumerator CacheWorkingSetCapacityTradeoff()
        {
            foreach (int capacity in new[] { 0, 16, 64, 256 })
            foreach (int workingSet in new[] { 16, 64, 256, 512 })
            {
                using var f = new ResourceFixture();
                f.Service.IdleAssetCapacity = capacity;
                var asset = f.Text("template");
                var keys = new ResourceKey[workingSet];
                for (int i = 0; i < workingSet; i++)
                {
                    string location = "icon-" + i;
                    f.Loader.Assets.Add(location, asset);
                    keys[i] = ResourceKey.Asset<TextAsset>(location);
                }
                for (int i = 0; i < workingSet; i++) f.Service.LoadLease<TextAsset>(keys[i]).Dispose();
                int firstPassLoads = f.Loader.Loads;
                yield return AllocationCapture.Measure($"cache-capacity-{capacity}-working-set-{workingSet}", workingSet * 3, () =>
                {
                    for (int round = 0; round < 3; round++)
                    for (int i = 0; i < workingSet; i++) f.Service.LoadLease<TextAsset>(keys[i]).Dispose();
                }, _ => { });
                int misses = f.Loader.Loads - firstPassLoads;
                Assert.That(misses, Is.EqualTo(capacity >= workingSet ? 0 : workingSet * 3));
                Assert.That(f.Loader.LiveHandles, Is.LessThanOrEqualTo(capacity));
                TestContext.WriteLine($"CACHE,{capacity},{workingSet},{workingSet * 3 - misses},{misses},{f.Loader.LiveHandles}");
                f.Service.UnloadUnusedAssets(true);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
            }
        }

        private static int CountReferences(ResourceFixture f)
        {
            var infos = new ResourceAssetInfo[f.Service.GetAssetInfos(null, 0, 0)];
            f.Service.GetAssetInfos(infos, 0, infos.Length);
            int count = 0;
            foreach (var info in infos)
            {
                Assert.That(info.DirectRefCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(info.BindingRefCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(info.PendingRefCount, Is.GreaterThanOrEqualTo(0));
                count += info.RefCountTotal;
            }
            return count;
        }

        private static int ReadInt(object value, string name)
            => (int)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);

        private static int ReadCount(object value, string name)
        {
            object field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
            return (int)field.GetType().GetProperty("Count").GetValue(field);
        }

        private static void Snapshot(ResourceFixture f, string phase, int scale)
        {
            TestContext.WriteLine($"STATE,{phase},{scale},handles={f.Loader.LiveHandles},refs={CountReferences(f)},idle={ReadInt(f.Service, "_idleCount")},slots={ReadInt(f.Service, "_assetSlotNextIndex")},managed={GC.GetTotalMemory(false)},unity={Profiler.GetTotalAllocatedMemoryLong()}");
        }
    }
}
#endif
