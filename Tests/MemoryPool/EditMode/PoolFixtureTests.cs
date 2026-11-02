using System;
using NUnit.Framework;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolFixtureTests
    {
        private sealed class Fixture : PoolFixture { }

        [Test]
        public void LeakedLeaseFailsTeardownAndStillRestoresTheBudget()
        {
            MemoryPoolBudget before = MemoryPoolRegistry.Budget;
            var fixture = new Fixture();
            fixture.SetUpPool();
            MemoryPool.SetBudget(new MemoryPoolBudget(1, 2, 3, 1, 1f));
            var item = MemoryPool.Acquire<PoolItem>();
            try
            {
                var error = Assert.Throws<AggregateException>(() => fixture.TearDownPool());
                Assert.That(error.InnerExceptions.Count, Is.EqualTo(1));
                Assert.That(error.InnerException, Is.TypeOf<AssertionException>());
                Assert.That(error.InnerException.Message, Does.Contain("unreturned leases"));
                Assert.That(MemoryPoolRegistry.Budget.SoftFreeCount, Is.EqualTo(before.SoftFreeCount));
                Assert.That(MemoryPoolRegistry.Budget.HardFreeCount, Is.EqualTo(before.HardFreeCount));
            }
            finally
            {
                MemoryPool.Release(item);
                MemoryPool<PoolItem>.Instance.Trim(0);
            }
        }
    }
}
