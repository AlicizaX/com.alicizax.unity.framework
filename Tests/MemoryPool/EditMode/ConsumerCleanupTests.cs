using AlicizaX.Audio.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class ConsumerCleanupTests : PoolFixture
    {
        private struct PoolEvent : IEmptyEventArgs { }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(65)]
        public void AudioCacheReturnReleasesItsResourceAndEveryPendingRequest(int pendingCount)
        {
            Use<AudioClipCacheEntry>();
            Use<AudioLoadRequest>();
            using var f = new ResourceFixture();
            f.Service.IdleAssetCapacity = 0;
            var clip = f.Keep(AudioClip.Create("pool-clip", 128, 1, 8000, false));
            f.Loader.Assets.Add("clip", clip);
            var entry = MemoryPool<AudioClipCacheEntry>.Acquire();
            entry.Clip = clip;
            entry.Lease = f.Service.LoadLease<AudioClip>("clip");
            var requests = new AudioLoadRequest[pendingCount];
            for (int i = 0; i < requests.Length; i++)
            {
                requests[i] = MemoryPool<AudioLoadRequest>.Acquire();
                requests[i].Completed = _ => Assert.Fail("Returning a request must not invoke its completion callback.");
                entry.AddPending(requests[i]);
            }
            MemoryPool.Release(entry);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(Info<AudioClipCacheEntry>().UsingCount, Is.Zero);
            Assert.That(Info<AudioLoadRequest>().UsingCount, Is.Zero);
            Assert.That(entry.Clip, Is.Null);
            Assert.That(entry.PendingHead, Is.Null);
            foreach (var request in requests)
            {
                Assert.That(request.Entry, Is.Null);
                Assert.That(request.Next, Is.Null);
                Assert.That(request.Completed, Is.Null);
            }
        }

        [Test]
        public void ReturningUiEventProxyUnsubscribesEveryListenerBeforeReuse()
        {
            Use<EventListenerProxy>();
            var proxy = MemoryPool<EventListenerProxy>.Acquire();
            int calls = 0;
            try
            {
                for (int i = 0; i < 65; i++) proxy.AddUIEvent<PoolEvent>(() => calls++);
                EmptyEventContainer<PoolEvent>.Publish();
                Assert.That(calls, Is.EqualTo(65));
                MemoryPool.Release(proxy);
                Assert.That(EmptyEventContainer<PoolEvent>.SubscriberCount, Is.Zero);
                EmptyEventContainer<PoolEvent>.Publish();
                Assert.That(calls, Is.EqualTo(65));
                proxy = MemoryPool<EventListenerProxy>.Acquire();
                proxy.AddUIEvent<PoolEvent>(() => calls++);
                EmptyEventContainer<PoolEvent>.Publish();
                Assert.That(calls, Is.EqualTo(66));
                MemoryPool.Release(proxy);
                Assert.That(Info<EventListenerProxy>().UsingCount, Is.Zero);
            }
            finally { EmptyEventContainer<PoolEvent>.Clear(); }
        }
    }
}
