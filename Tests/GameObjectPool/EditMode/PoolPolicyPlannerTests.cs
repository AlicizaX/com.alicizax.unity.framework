using AlicizaX;
using NUnit.Framework;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class PoolPolicyPlannerTests
    {
        private static PoolCompiledRule Rule(PoolPolicy policy, int minIdle, int soft, int hard, bool unload = true)
            => PoolCompiledRule.FromEntry(GameObjectPoolFixture.Entry("fx/a", policy, minIdle, soft, hard, unload: unload), 0);

        [Test]
        public void FixedAndBurstRetainMinIdleClampedToSoftAndStickyRetainsTotal()
        {
            var fixedPlan = PoolPolicyPlanner.Plan(Rule(PoolPolicy.Fixed, 3, 8, 16), 10, false);
            Assert.That(fixedPlan.RetainTarget, Is.EqualTo(3));
            Assert.That(fixedPlan.ForceTrim, Is.False);
            Assert.That(fixedPlan.UnloadPrefab, Is.True);
            var burst = PoolPolicyPlanner.Plan(Rule(PoolPolicy.Burst, 100, 8, 16), 4, false);
            Assert.That(burst.RetainTarget, Is.EqualTo(8));
            var sticky = PoolPolicyPlanner.Plan(Rule(PoolPolicy.Sticky, 2, 8, 16), 10, false);
            Assert.That(sticky.RetainTarget, Is.EqualTo(10));
            Assert.That(sticky.UnloadPrefab, Is.False);
        }

        [Test]
        public void LowMemoryForcesTrimToMinIdleWithBudgetCapAndAllowsStickyUnload()
        {
            var sticky = PoolPolicyPlanner.Plan(Rule(PoolPolicy.Sticky, 2, 32, 64), 40, true);
            Assert.That(sticky.RetainTarget, Is.EqualTo(2));
            Assert.That(sticky.ForceTrim, Is.True);
            Assert.That(sticky.TrimBudget, Is.EqualTo(16));
            Assert.That(sticky.UnloadPrefab, Is.True);
            var kept = PoolPolicyPlanner.Plan(Rule(PoolPolicy.Burst, 1, 32, 64, unload: false), 8, true);
            Assert.That(kept.UnloadPrefab, Is.False);
        }

        [Test]
        public void NormalBudgetIsSoftShiftedTwoBitsClampedToSixteen()
        {
            Assert.That(PoolPolicyPlanner.Plan(Rule(PoolPolicy.Fixed, 0, 4, 16), 4, false).TrimBudget, Is.EqualTo(1));
            Assert.That(PoolPolicyPlanner.Plan(Rule(PoolPolicy.Fixed, 0, 32, 64), 4, false).TrimBudget, Is.EqualTo(8));
            Assert.That(PoolPolicyPlanner.Plan(Rule(PoolPolicy.Fixed, 0, 256, 256), 4, false).TrimBudget, Is.EqualTo(16));
        }
    }
}
