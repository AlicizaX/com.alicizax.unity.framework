using System;
using System.IO;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
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
    }
}
