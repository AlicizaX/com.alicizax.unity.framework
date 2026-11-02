using System.IO;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceBackendContractTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void RemoteCandidatesRemainStableAcrossInterleavedDownloads(bool fallback)
        {
            IRemoteService remote = new RemoteServices("https://primary.example/resources", fallback ? "https://fallback.example/resources/" : null);
            var first = remote.GetRemoteUrls("first.bundle");
            var second = remote.GetRemoteUrls("second.bundle");
            Assert.That(first[0], Is.EqualTo("https://primary.example/resources/first.bundle"));
            Assert.That(second[0], Is.EqualTo("https://primary.example/resources/second.bundle"));
            Assert.That(first.Count, Is.EqualTo(fallback ? 2 : 1));
            if (fallback) Assert.That(first[1], Is.EqualTo("https://fallback.example/resources/first.bundle"));
        }

        [Test]
        public void DecryptionStreamRespectsReadOffsetShortReadAndDisposal()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new[] { (byte)(1 ^ BundleStream.KEY), (byte)(2 ^ BundleStream.KEY), (byte)(3 ^ BundleStream.KEY) });
                using (var stream = new BundleStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var buffer = new byte[] { 9, 9, 9, 9, 9, 9, 9 };
                    Assert.That(stream.Read(buffer, 2, 4), Is.EqualTo(3));
                    Assert.That(buffer, Is.EqualTo(new byte[] { 9, 9, 1, 2, 3, 9, 9 }));
                    Assert.That(stream.Read(buffer, 1, 2), Is.Zero);
                    Assert.That(buffer[1], Is.EqualTo(9));
                }
                using var reopened = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.That(reopened.Length, Is.EqualTo(3));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void GenerationsWrapPastZero()
        {
            Assert.That(ResourceService.NextGeneration(1u), Is.EqualTo(2u));
            Assert.That(ResourceService.NextGeneration(uint.MaxValue), Is.EqualTo(1u), "Zero is reserved for never-issued slots.");
        }

        [Test]
        public void SynchronousRequestsAreCompleteWhenReturned()
        {
            using var f = new ResourceFixture();
            f.Loader.CompleteImmediately = false;
            var asset = f.Text("a");
            var handle = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), true);
            Assert.That(handle.IsDone, Is.True);
            Assert.That(handle.AssetObject, Is.SameAs(asset));
            Assert.That(f.Loader.SynchronousLoads, Is.EqualTo(1));
            handle.Release();
        }

        [Test]
        public void WaitForCompletionFinishesAPendingRequest()
        {
            using var f = new ResourceFixture();
            f.Loader.CompleteImmediately = false;
            var asset = f.Text("a");
            var handle = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), false);
            Assert.That(handle.IsDone, Is.False);
            f.Loader.WaitForCompletion(handle);
            Assert.That(handle.IsDone, Is.True);
            Assert.That(handle.AssetObject, Is.SameAs(asset));
            Assert.That(f.Loader.WaitCalls, Is.EqualTo(1));
            handle.Release();
        }

        [Test]
        public void ReleasedRequestIsRecreatedWithTheCurrentAsset()
        {
            using var f = new ResourceFixture();
            f.Loader.CompleteImmediately = false;
            var oldAsset = f.Text("a");
            var oldHandle = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), false);
            var oldRequest = f.Loader.RequestFor("a");
            oldHandle.Release();
            var newAsset = f.Keep(new TextAsset("new"));
            f.Loader.Assets["a"] = newAsset;
            var newHandle = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), false);
            var newRequest = f.Loader.RequestFor("a");
            Assert.That(newRequest, Is.Not.SameAs(oldRequest));
            f.Loader.Complete(oldRequest);
            Assert.That(oldRequest.Asset, Is.SameAs(oldAsset));
            Assert.That(newHandle.IsDone, Is.False);
            f.Loader.Complete(newRequest);
            Assert.That(newHandle.AssetObject, Is.SameAs(newAsset));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            newHandle.Release();
        }
    }
}
