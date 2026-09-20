using System;
using NUnit.Framework;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolFixtureTests
    {
        private sealed class Fixture : PoolFixture { }

        [Test]
        public void LeakedLeaseFailsTeardownAndStillRestoresGlobalSettings()
        {
            var fixture = new Fixture();
            fixture.SetUpPool();
            int decay = MemoryPool.ShortDecayStartFrames;
            MemoryPool.ShortDecayStartFrames = decay + 1;
            var item = MemoryPool<PoolItem>.Acquire();
            try
            {
                var error = Assert.Throws<AggregateException>(() => fixture.TearDownPool());
                Assert.That(error.InnerExceptions.Count, Is.EqualTo(1));
                Assert.That(error.InnerException, Is.TypeOf<AssertionException>());
                Assert.That(error.InnerException.Message, Does.Contain("unreturned leases"));
                Assert.That(MemoryPool.ShortDecayStartFrames, Is.EqualTo(decay));
            }
            finally
            {
                MemoryPool.Release(item);
                MemoryPool<PoolItem>.ClearAll();
            }
        }
    }
}
