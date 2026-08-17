using UnityEngine;

namespace AlicizaX
{
    internal readonly struct PoolRecyclePlan
    {
        public readonly int RetainTarget;
        public readonly int TrimBudget;
        public readonly bool ForceTrim;
        public readonly bool UnloadPrefab;

        public PoolRecyclePlan(int retainTarget, int trimBudget, bool forceTrim, bool unloadPrefab)
        {
            RetainTarget = retainTarget;
            TrimBudget = trimBudget;
            ForceTrim = forceTrim;
            UnloadPrefab = unloadPrefab;
        }
    }

    internal static class PoolPolicyPlanner
    {
        private const int TrimBudgetCap = 16;

        public static PoolRecyclePlan Plan(in PoolCompiledRule rule, int totalCount, bool lowMemory)
        {
            int retain = rule.MinIdle;
            if (!lowMemory)
            {
                switch (rule.Policy)
                {
                    case PoolPolicy.Fixed:
                        retain = Mathf.Clamp(rule.MinIdle, 0, rule.SoftCapacity);
                        break;
                    case PoolPolicy.Burst:
                        retain = Mathf.Clamp(rule.MinIdle, 0, rule.SoftCapacity);
                        break;
                    case PoolPolicy.Sticky:
                        retain = Mathf.Max(rule.MinIdle, totalCount);
                        break;
                }
            }

            int budget = Mathf.Clamp(rule.SoftCapacity >> 2, 1, TrimBudgetCap);
            if (lowMemory)
            {
                budget = TrimBudgetCap;
            }

            bool unloadPrefab = rule.UnloadPrefab && (lowMemory || rule.Policy != PoolPolicy.Sticky);
            return new PoolRecyclePlan(retain, budget, lowMemory, unloadPrefab);
        }
    }
}
