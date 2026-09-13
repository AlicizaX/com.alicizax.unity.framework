#if UNITY_EDITOR
using System.Collections;
using System.Runtime.CompilerServices;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceValueTypePerformanceTests
    {
        [UnityTest]
        public IEnumerator ValueCopyAndReadonlyAccessCosts()
        {
            using var f = new ResourceFixture();
            f.Text("value-copy");
            var lease = f.Service.LoadLease<TextAsset>("value-copy");
            var key = ResourceKey.Asset<TextAsset>("value-copy");
            const int count = 1000000;
            long sum = 0;
            for (int round = 0; round < 3; round++)
            {
                yield return AllocationCapture.Measure("lease-read-in-round-" + round, count, () =>
                {
                    for (int i = 0; i < count; i++) sum += ReadLease(in lease);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("key-value-round-" + round, count, () =>
                {
                    for (int i = 0; i < count; i++) sum += ReadKey(key);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("key-in-round-" + round, count, () =>
                {
                    for (int i = 0; i < count; i++) sum += ReadKeyByReference(in key);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
            }
            Assert.That(sum, Is.GreaterThan(0));
            lease.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadLease(in ResourceAssetLease<TextAsset> lease)
            => lease.Handle.Index + (lease.IsValid ? 1 : 0);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadKey(ResourceKey key)
            => key.Location.Length + key.PackageName.Length + (int)key.AssetKind + key.LoadKeyId;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadKeyByReference(in ResourceKey key)
            => key.Location.Length + key.PackageName.Length + (int)key.AssetKind + key.LoadKeyId;
    }
}
#endif
