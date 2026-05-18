using System;
using AlicizaX.ObjectPool;
using UnityEngine;

namespace AlicizaX
{
    public enum PoolResourceLoaderType
    {
        AssetBundle = 0,
        Resources = 1
    }

    [Serializable]
    public sealed class PoolEntry
    {
        public const string DefaultGroup = "DefaultGroup";
        public const string DefaultEntryName = "PoolRule";

        public string entryName = DefaultEntryName;
        public string group = DefaultGroup;
        public string assetPath = string.Empty;
        public PoolResourceLoaderType loaderType = PoolResourceLoaderType.AssetBundle;

        [Min(1)]
        public int softCapacity = 8;

        [Min(1)]
        public int hardCapacity = 16;
        public int priority;

        public void Normalize()
        {
            entryName = string.IsNullOrWhiteSpace(entryName) ? DefaultEntryName : entryName.Trim();
            group = string.IsNullOrWhiteSpace(group) ? DefaultGroup : group.Trim();
            assetPath = NormalizeConfigAssetPath(assetPath, loaderType);
            softCapacity = Mathf.Max(1, softCapacity);
            hardCapacity = Mathf.Max(softCapacity, hardCapacity);
        }

        public bool Matches(string requestedAssetPath, string requestedGroup = null)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(requestedAssetPath))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(requestedGroup) &&
                !string.Equals(group, requestedGroup, StringComparison.Ordinal))
            {
                return false;
            }

            return PoolGlobMatcher.Compile(assetPath).IsMatch(requestedAssetPath);
        }

        public static int CompareByPriority(PoolEntry left, PoolEntry right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int priorityCompare = right.priority.CompareTo(left.priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            int leftLength = left.assetPath == null ? 0 : left.assetPath.Length;
            int rightLength = right.assetPath == null ? 0 : right.assetPath.Length;
            int pathLengthCompare = rightLength.CompareTo(leftLength);
            if (pathLengthCompare != 0)
            {
                return pathLengthCompare;
            }

            return string.Compare(left.group, right.group, StringComparison.Ordinal);
        }

        public static string NormalizeAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return TrimTrailingSeparators(value.Trim().Replace('\\', '/'));
        }

        public static string NormalizeConfigAssetPath(string value, PoolResourceLoaderType loaderType)
        {
            string normalized = NormalizeAssetPath(value);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            if (loaderType == PoolResourceLoaderType.AssetBundle)
            {
                normalized = TrimAssetBundleRoot(normalized);
            }
            else
            {
                int resourcesMarkerIndex = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
                if (resourcesMarkerIndex >= 0)
                {
                    normalized = normalized.Substring(resourcesMarkerIndex + "/Resources/".Length);
                }
                else if (normalized.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("Assets/Resources/".Length);
                }
            }

            int lastSlashIndex = normalized.LastIndexOf('/');
            int extensionIndex = normalized.LastIndexOf('.');
            if (extensionIndex > lastSlashIndex)
            {
                normalized = normalized.Substring(0, extensionIndex);
            }

            return TrimTrailingSeparators(normalized);
        }

        private static string TrimTrailingSeparators(string value)
        {
            int length = value.Length;
            while (length > 0 && value[length - 1] == '/')
            {
                length--;
            }

            return length == value.Length ? value : value.Substring(0, length);
        }

        private static string TrimAssetBundleRoot(string value)
        {
            if (!value.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            int firstSlash = value.IndexOf('/');
            if (firstSlash < 0)
            {
                return value;
            }

            int secondSlash = value.IndexOf('/', firstSlash + 1);
            if (secondSlash < 0)
            {
                return value;
            }

            string rootFolder = value.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
            if (string.Equals(rootFolder, "Bundle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rootFolder, "Bundles", StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(secondSlash + 1);
            }

            return value;
        }
    }

    internal struct PoolCompiledRule
    {
        public int ruleIndex;
        public string entryName;
        public string group;
        public string assetPath;
        public PoolResourceLoaderType loaderType;
        public PoolGlobMatcher globMatcher;
        public int softCapacity;
        public int hardCapacity;
        public int priority;

        public static PoolCompiledRule FromEntry(PoolEntry entry, int ruleIndex)
        {
            return new PoolCompiledRule
            {
                ruleIndex = ruleIndex,
                entryName = entry.entryName,
                group = entry.group,
                assetPath = entry.assetPath,
                loaderType = entry.loaderType,
                globMatcher = PoolGlobMatcher.Compile(entry.assetPath),
                softCapacity = entry.softCapacity,
                hardCapacity = entry.hardCapacity,
                priority = entry.priority
            };
        }
    }

    internal sealed class PoolCompiledCatalog
    {
        private readonly PoolCompiledRule[] _rules;
        private StringOpenHashMap _groupIndexMap;
        private PoolCompiledGroup[] _groups;
        private int[][] _globalRuleIndices;
        private int[] _globalRuleCounts;

        private PoolCompiledCatalog(
            PoolCompiledRule[] rules,
            StringOpenHashMap groupIndexMap,
            PoolCompiledGroup[] groups,
            int[][] globalRuleIndices,
            int[] globalRuleCounts)
        {
            _rules = rules;
            _groupIndexMap = groupIndexMap;
            _groups = groups;
            _globalRuleIndices = globalRuleIndices;
            _globalRuleCounts = globalRuleCounts;
        }

        public bool IsEmpty => _rules == null || _rules.Length == 0;

        public int RuleCount => _rules == null ? 0 : _rules.Length;

        public ref readonly PoolCompiledRule GetRule(int ruleIndex)
        {
            return ref _rules[ruleIndex];
        }

        public int Resolve(string assetPath, PoolResourceLoaderType loaderType, string group)
        {
            if (string.IsNullOrEmpty(assetPath) || _rules == null || _rules.Length == 0)
            {
                return -1;
            }

            if (!string.IsNullOrEmpty(group))
            {
                if (_groupIndexMap.TryGetValue(group, out int groupIndex))
                {
                    return _groups[groupIndex].Resolve(assetPath, loaderType, _rules);
                }

                return -1;
            }

            int loaderIndex = (int)loaderType;
            int count = _globalRuleCounts[loaderIndex];
            int[] indices = _globalRuleIndices[loaderIndex];
            for (int i = 0; i < count; i++)
            {
                if (_rules[indices[i]].globMatcher.IsMatch(assetPath))
                {
                    return indices[i];
                }
            }

            return -1;
        }

        public void Dispose()
        {
            _groupIndexMap.Dispose();
        }

        public static PoolCompiledCatalog Empty()
        {
            return new PoolCompiledCatalog(
                Array.Empty<PoolCompiledRule>(),
                new StringOpenHashMap(8),
                Array.Empty<PoolCompiledGroup>(),
                new[] { Array.Empty<int>(), Array.Empty<int>() },
                new int[2]);
        }

        public static PoolCompiledCatalog Build(PoolEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return Empty();
            }

            int entryCount = entries.Length;
            var groupIndexMap = new StringOpenHashMap(entryCount);
            var groupNames = new string[entryCount];
            int groupCount = 0;

            for (int i = 0; i < entryCount; i++)
            {
                PoolEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                {
                    continue;
                }

                if (!groupIndexMap.TryGetValue(entry.group, out _))
                {
                    groupIndexMap.AddOrUpdate(entry.group, groupCount);
                    groupNames[groupCount] = entry.group;
                    groupCount++;
                }
            }

            var groups = new PoolCompiledGroup[groupCount];
            for (int i = 0; i < groupCount; i++)
            {
                groups[i] = new PoolCompiledGroup(groupNames[i]);
            }

            var rules = new PoolCompiledRule[entryCount];
            var globalRuleIndices = new int[2][];
            var globalRuleCounts = new int[2];
            globalRuleIndices[0] = new int[entryCount];
            globalRuleIndices[1] = new int[entryCount];

            for (int i = 0; i < entryCount; i++)
            {
                PoolEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                {
                    continue;
                }

                PoolCompiledRule rule = PoolCompiledRule.FromEntry(entry, i);
                rules[i] = rule;

                groupIndexMap.TryGetValue(rule.group, out int groupIndex);
                groups[groupIndex].Register(in rule);

                int loaderIndex = (int)rule.loaderType;
                globalRuleIndices[loaderIndex][globalRuleCounts[loaderIndex]++] = i;
            }

            return new PoolCompiledCatalog(rules, groupIndexMap, groups, globalRuleIndices, globalRuleCounts);
        }
    }

    internal sealed class PoolCompiledGroup
    {
        private readonly string _name;
        private int[][] _ruleIndices;
        private int[] _ruleCounts;

        public PoolCompiledGroup(string name)
        {
            _name = name;
            _ruleIndices = new int[2][];
            _ruleIndices[0] = new int[4];
            _ruleIndices[1] = new int[4];
            _ruleCounts = new int[2];
        }

        public void Register(in PoolCompiledRule rule)
        {
            int loaderIndex = (int)rule.loaderType;
            int count = _ruleCounts[loaderIndex];
            if (count >= _ruleIndices[loaderIndex].Length)
            {
                int newCapacity = _ruleIndices[loaderIndex].Length << 1;
                var newArray = new int[newCapacity];
                Array.Copy(_ruleIndices[loaderIndex], 0, newArray, 0, count);
                _ruleIndices[loaderIndex] = newArray;
            }

            _ruleIndices[loaderIndex][count] = rule.ruleIndex;
            _ruleCounts[loaderIndex] = count + 1;
        }

        public int Resolve(string assetPath, PoolResourceLoaderType loaderType, PoolCompiledRule[] rules)
        {
            int loaderIndex = (int)loaderType;
            int count = _ruleCounts[loaderIndex];
            int[] indices = _ruleIndices[loaderIndex];
            for (int i = 0; i < count; i++)
            {
                if (rules[indices[i]].globMatcher.IsMatch(assetPath))
                {
                    return indices[i];
                }
            }

            return -1;
        }
    }
}
