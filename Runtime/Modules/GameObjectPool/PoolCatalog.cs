using System;
using System.Collections.Generic;
using AlicizaX.ObjectPool;

namespace AlicizaX
{
    internal readonly struct PoolCompiledRule
    {
        public readonly int RuleIndex;
        public readonly string EntryName;
        public readonly string Group;
        public readonly string Pattern;
        public readonly PoolPolicy Policy;
        public readonly int MinIdle;
        public readonly int SoftCapacity;
        public readonly int HardCapacity;
        public readonly float IdleSeconds;
        public readonly bool UnloadPrefab;
        public readonly int Priority;
        public readonly PoolGlobMatcher Matcher;

        public bool IsLiteralPattern => Matcher.IsValid && Matcher.IsLiteralPattern;

        public PoolCompiledRule(
            int ruleIndex,
            string entryName,
            string group,
            string pattern,
            PoolPolicy policy,
            int minIdle,
            int softCapacity,
            int hardCapacity,
            float idleSeconds,
            bool unloadPrefab,
            int priority,
            PoolGlobMatcher matcher)
        {
            RuleIndex = ruleIndex;
            EntryName = entryName;
            Group = group;
            Pattern = pattern;
            Policy = policy;
            MinIdle = minIdle;
            SoftCapacity = softCapacity;
            HardCapacity = hardCapacity;
            IdleSeconds = idleSeconds;
            UnloadPrefab = unloadPrefab;
            Priority = priority;
            Matcher = matcher;
        }

        public static PoolCompiledRule FromEntry(PoolEntry entry, int ruleIndex)
        {
            return new PoolCompiledRule(
                ruleIndex,
                entry.entryName,
                entry.group,
                entry.assetPath,
                entry.policy,
                entry.minIdle,
                entry.softCapacity,
                entry.hardCapacity,
                entry.idleSeconds,
                entry.unloadPrefab,
                entry.priority,
                PoolGlobMatcher.Compile(entry.assetPath));
        }
    }

    internal sealed class PoolCompiledCatalog
    {
        private readonly PoolCompiledRule[] _rules;
        private readonly int[] _globRuleIndices;
        private readonly int _globRuleCount;
        private StringOpenHashMap _exactRuleMap;

        private PoolCompiledCatalog(PoolCompiledRule[] rules, int[] globRuleIndices, int globRuleCount, StringOpenHashMap exactRuleMap)
        {
            _rules = rules;
            _globRuleIndices = globRuleIndices;
            _globRuleCount = globRuleCount;
            _exactRuleMap = exactRuleMap;
        }

        public int RuleCount => _rules.Length;

        public ref readonly PoolCompiledRule GetRule(int ruleIndex)
        {
            return ref _rules[ruleIndex];
        }

        public int Resolve(string location)
        {
            if (string.IsNullOrEmpty(location) || _rules.Length == 0)
            {
                return -1;
            }

            if (_exactRuleMap.TryGetValue(location, out int exactIndex))
            {
                return exactIndex;
            }

            for (int i = 0; i < _globRuleCount; i++)
            {
                int ruleIndex = _globRuleIndices[i];
                if (_rules[ruleIndex].Matcher.IsMatch(location))
                {
                    _exactRuleMap.AddOrUpdate(location, ruleIndex);
                    return ruleIndex;
                }
            }

            return -1;
        }

        public void Dispose()
        {
            _exactRuleMap.Dispose();
        }

        public static PoolCompiledCatalog Empty()
        {
            return new PoolCompiledCatalog(
                Array.Empty<PoolCompiledRule>(),
                Array.Empty<int>(),
                0,
                new StringOpenHashMap(8));
        }

        public static PoolCompiledCatalog Build(IList<PoolEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return Empty();
            }

            int validCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PoolEntry entry = entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.assetPath))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return Empty();
            }

            var normalized = new PoolEntry[validCount];
            int write = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PoolEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                {
                    continue;
                }

                normalized[write++] = entry;
            }

            var order = new int[normalized.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (left, right) =>
            {
                int compare = PoolEntry.CompareByPriority(normalized[left], normalized[right]);
                return compare != 0 ? compare : left.CompareTo(right);
            });

            var rules = new PoolCompiledRule[normalized.Length];
            var globIndices = new int[normalized.Length];
            var exactMap = new StringOpenHashMap(normalized.Length);
            int globCount = 0;
            for (int i = 0; i < order.Length; i++)
            {
                PoolCompiledRule rule = PoolCompiledRule.FromEntry(normalized[order[i]], i);
                rules[i] = rule;
                if (rule.IsLiteralPattern && !exactMap.ContainsKey(rule.Pattern))
                {
                    exactMap.AddOrUpdate(rule.Pattern, i);
                }
                else
                {
                    globIndices[globCount++] = i;
                }
            }

            return new PoolCompiledCatalog(rules, globIndices, globCount, exactMap);
        }
    }
}
