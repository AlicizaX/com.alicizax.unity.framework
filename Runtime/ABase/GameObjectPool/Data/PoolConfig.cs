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

    public enum PoolCategory
    {
        Default = 0,
        Effect = 1,
        Monster = 2,
        Building = 3,
        UI = 4,
        Custom = 5
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
        public PoolCategory category = PoolCategory.Default;

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

            return IsPathPrefixMatch(requestedAssetPath, assetPath);
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

        public static bool IsPathPrefixMatch(string value, string prefix)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prefix))
            {
                return false;
            }

            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return IsPrefixBoundary(value, prefix.Length);
        }

        public static bool IsPrefixBoundary(string value, int matchedLength)
        {
            return matchedLength >= value.Length || value[matchedLength] == '/';
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
        public PoolCategory category;
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
                category = entry.category,
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
        private PoolPrefixTrie[] _globalPrefixTries;

        private PoolCompiledCatalog(
            PoolCompiledRule[] rules,
            StringOpenHashMap groupIndexMap,
            PoolCompiledGroup[] groups,
            PoolPrefixTrie[] globalPrefixTries)
        {
            _rules = rules;
            _groupIndexMap = groupIndexMap;
            _groups = groups;
            _globalPrefixTries = globalPrefixTries;
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
                    return _groups[groupIndex].Resolve(assetPath, loaderType);
                }

                return -1;
            }

            return _globalPrefixTries[(int)loaderType].Resolve(assetPath);
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
                new[]
                {
                    new PoolPrefixTrie(0),
                    new PoolPrefixTrie(0)
                });
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
            var groupPrefixChars = new int[entryCount * 2];
            int groupCount = 0;
            int[] globalPrefixChars = new int[2];

            for (int i = 0; i < entryCount; i++)
            {
                PoolEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                {
                    continue;
                }

                if (!groupIndexMap.TryGetValue(entry.group, out int groupIndex))
                {
                    groupIndex = groupCount;
                    groupIndexMap.AddOrUpdate(entry.group, groupIndex);
                    groupNames[groupCount] = entry.group;
                    groupCount++;
                }

                int pathLength = entry.assetPath.Length;
                int loaderIndex = (int)entry.loaderType;
                groupPrefixChars[groupIndex * 2 + loaderIndex] += pathLength;
                globalPrefixChars[loaderIndex] += pathLength;
            }

            var groups = new PoolCompiledGroup[groupCount];
            for (int i = 0; i < groupCount; i++)
            {
                groups[i] = new PoolCompiledGroup(
                    groupNames[i],
                    groupPrefixChars[i * 2 + (int)PoolResourceLoaderType.AssetBundle],
                    groupPrefixChars[i * 2 + (int)PoolResourceLoaderType.Resources]);
            }

            var rules = new PoolCompiledRule[entryCount];
            var globalPrefixTries = new PoolPrefixTrie[2];
            globalPrefixTries[(int)PoolResourceLoaderType.AssetBundle] = new PoolPrefixTrie(globalPrefixChars[(int)PoolResourceLoaderType.AssetBundle]);
            globalPrefixTries[(int)PoolResourceLoaderType.Resources] = new PoolPrefixTrie(globalPrefixChars[(int)PoolResourceLoaderType.Resources]);

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
                groups[groupIndex].Register(rule);
                globalPrefixTries[(int)rule.loaderType].Register(rule.assetPath, i);
            }

            return new PoolCompiledCatalog(rules, groupIndexMap, groups, globalPrefixTries);
        }
    }

    internal sealed class PoolCompiledGroup
    {
        private readonly string _name;
        private PoolPrefixTrie[] _prefixTries;

        public PoolCompiledGroup(string name, int assetBundlePrefixChars, int resourcesPrefixChars)
        {
            _name = name;
            _prefixTries = new PoolPrefixTrie[2];
            _prefixTries[(int)PoolResourceLoaderType.AssetBundle] = new PoolPrefixTrie(assetBundlePrefixChars);
            _prefixTries[(int)PoolResourceLoaderType.Resources] = new PoolPrefixTrie(resourcesPrefixChars);
        }

        public void Register(in PoolCompiledRule rule)
        {
            _prefixTries[(int)rule.loaderType].Register(rule.assetPath, rule.ruleIndex);
        }

        public int Resolve(string assetPath, PoolResourceLoaderType loaderType)
        {
            return _prefixTries[(int)loaderType].Resolve(assetPath);
        }
    }

    internal sealed class PoolPrefixTrie
    {
        private struct Node
        {
            public char character;
            public int firstChild;
            public int nextSibling;
            public int childCount;
            public int ruleIndex;
        }

        private struct Edge
        {
            public char character;
            public int nodeIndex;
        }

        private Node[] _nodes;
        private Edge[] _edges;
        private int _nodeCount;
        private bool _compiled;

        public PoolPrefixTrie(int prefixCharCount)
        {
            int capacity = Mathf.Max(1, prefixCharCount + 1);
            _nodes = new Node[capacity];
            _nodes[0].firstChild = -1;
            _nodes[0].nextSibling = -1;
            _nodes[0].ruleIndex = -1;
            _nodeCount = 1;
        }

        public void Register(string prefix, int ruleIndex)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return;
            }

            if (_compiled)
            {
                ResetCompiledChildLinks();
            }

            int nodeIndex = 0;
            int length = prefix.Length;
            for (int i = 0; i < length; i++)
            {
                nodeIndex = GetOrCreateChild(nodeIndex, prefix[i]);
            }

            if (_nodes[nodeIndex].ruleIndex < 0)
            {
                _nodes[nodeIndex].ruleIndex = ruleIndex;
            }
        }

        public int Resolve(string value)
        {
            if (string.IsNullOrEmpty(value) || _nodes == null)
            {
                return -1;
            }

            Compile();
            int nodeIndex = 0;
            int bestRuleIndex = -1;
            int length = value.Length;
            for (int i = 0; i < length; i++)
            {
                nodeIndex = FindChild(nodeIndex, value[i]);
                if (nodeIndex < 0)
                {
                    break;
                }

                int matchedRuleIndex = _nodes[nodeIndex].ruleIndex;
                if (matchedRuleIndex >= 0 && PoolEntry.IsPrefixBoundary(value, i + 1))
                {
                    bestRuleIndex = matchedRuleIndex;
                }
            }

            return bestRuleIndex;
        }

        private int GetOrCreateChild(int nodeIndex, char character)
        {
            int childIndex = _nodes[nodeIndex].firstChild;
            while (childIndex >= 0)
            {
                if (_nodes[childIndex].character == character)
                {
                    return childIndex;
                }

                childIndex = _nodes[childIndex].nextSibling;
            }

            EnsureCapacity(_nodeCount + 1);
            int newNodeIndex = _nodeCount++;
            _nodes[newNodeIndex].character = character;
            _nodes[newNodeIndex].firstChild = -1;
            _nodes[newNodeIndex].nextSibling = -1;
            _nodes[newNodeIndex].ruleIndex = -1;
            InsertChildSorted(nodeIndex, newNodeIndex);
            _compiled = false;
            return newNodeIndex;
        }

        private void InsertChildSorted(int nodeIndex, int newNodeIndex)
        {
            int childIndex = _nodes[nodeIndex].firstChild;
            if (childIndex < 0 || _nodes[newNodeIndex].character < _nodes[childIndex].character)
            {
                _nodes[newNodeIndex].nextSibling = childIndex;
                _nodes[nodeIndex].firstChild = newNodeIndex;
                _nodes[nodeIndex].childCount++;
                return;
            }

            int previousIndex = childIndex;
            childIndex = _nodes[childIndex].nextSibling;
            while (childIndex >= 0 && _nodes[childIndex].character < _nodes[newNodeIndex].character)
            {
                previousIndex = childIndex;
                childIndex = _nodes[childIndex].nextSibling;
            }

            _nodes[newNodeIndex].nextSibling = childIndex;
            _nodes[previousIndex].nextSibling = newNodeIndex;
            _nodes[nodeIndex].childCount++;
        }

        private int FindChild(int nodeIndex, char character)
        {
            int childCount = _nodes[nodeIndex].childCount;
            if (childCount <= 0)
            {
                return -1;
            }

            if (!_compiled)
            {
                int childIndex = _nodes[nodeIndex].firstChild;
                while (childIndex >= 0)
                {
                    char childCharacter = _nodes[childIndex].character;
                    if (childCharacter == character)
                    {
                        return childIndex;
                    }

                    if (childCharacter > character)
                    {
                        return -1;
                    }

                    childIndex = _nodes[childIndex].nextSibling;
                }

                return -1;
            }

            int left = 0;
            int right = childCount - 1;
            int firstChild = _nodes[nodeIndex].firstChild;
            while (left <= right)
            {
                int middle = (left + right) >> 1;
                ref Edge edge = ref _edges[firstChild + middle];
                char childCharacter = edge.character;
                if (childCharacter == character)
                {
                    return edge.nodeIndex;
                }

                if (childCharacter > character)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            return -1;
        }

        private void Compile()
        {
            if (_compiled)
            {
                return;
            }

            int edgeCount = 0;
            for (int i = 0; i < _nodeCount; i++)
            {
                edgeCount += _nodes[i].childCount;
            }

            if (_edges == null || _edges.Length < edgeCount)
            {
                _edges = new Edge[Mathf.Max(1, edgeCount)];
            }

            int writeIndex = 0;
            for (int nodeIndex = 0; nodeIndex < _nodeCount; nodeIndex++)
            {
                int childCount = _nodes[nodeIndex].childCount;
                if (childCount <= 0)
                {
                    _nodes[nodeIndex].firstChild = -1;
                    continue;
                }

                int childIndex = _nodes[nodeIndex].firstChild;
                _nodes[nodeIndex].firstChild = writeIndex;
                while (childIndex >= 0)
                {
                    _edges[writeIndex].character = _nodes[childIndex].character;
                    _edges[writeIndex].nodeIndex = childIndex;
                    childIndex = _nodes[childIndex].nextSibling;
                    writeIndex++;
                }
            }

            _compiled = true;
        }

        private void ResetCompiledChildLinks()
        {
            if (!_compiled || _edges == null)
            {
                return;
            }

            for (int nodeIndex = 0; nodeIndex < _nodeCount; nodeIndex++)
            {
                int childCount = _nodes[nodeIndex].childCount;
                if (childCount <= 0)
                {
                    _nodes[nodeIndex].firstChild = -1;
                    continue;
                }

                int firstEdge = _nodes[nodeIndex].firstChild;
                int firstNode = _edges[firstEdge].nodeIndex;
                _nodes[nodeIndex].firstChild = firstNode;
                for (int i = 0; i < childCount; i++)
                {
                    int childNode = _edges[firstEdge + i].nodeIndex;
                    _nodes[childNode].nextSibling = i + 1 < childCount
                        ? _edges[firstEdge + i + 1].nodeIndex
                        : -1;
                }
            }

            _compiled = false;
        }

        private void EnsureCapacity(int required)
        {
            if (_nodes.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _nodes.Length << 1);
            var newNodes = new Node[newCapacity];
            Array.Copy(_nodes, 0, newNodes, 0, _nodeCount);
            _nodes = newNodes;
        }
    }
}
