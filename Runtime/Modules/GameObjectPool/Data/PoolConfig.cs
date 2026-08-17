using System;
using UnityEngine;

namespace AlicizaX
{
    [Serializable]
    public sealed class PoolEntry
    {
        public const string DefaultGroup = "DefaultGroup";
        public const string DefaultEntryName = "PoolRule";

        public string entryName = DefaultEntryName;
        public string group = DefaultGroup;
        public string assetPath = string.Empty;
        public PoolPolicy policy = PoolPolicy.Burst;

        [Min(0)]
        public int minIdle;

        [Min(1)]
        public int softCapacity = 8;

        [Min(1)]
        public int hardCapacity = 16;

        [Min(0f)]
        public float idleSeconds = 15f;

        public bool unloadPrefab = true;
        public int priority;

        public void Normalize()
        {
            entryName = string.IsNullOrWhiteSpace(entryName) ? DefaultEntryName : entryName.Trim();
            group = string.IsNullOrWhiteSpace(group) ? DefaultGroup : group.Trim();
            assetPath = NormalizeLocation(assetPath);
            if (!Enum.IsDefined(typeof(PoolPolicy), policy))
            {
                policy = PoolPolicy.Burst;
            }

            minIdle = Mathf.Max(0, minIdle);
            softCapacity = Mathf.Max(1, softCapacity);
            hardCapacity = Mathf.Max(softCapacity, hardCapacity);
            if (minIdle > hardCapacity)
            {
                minIdle = hardCapacity;
            }

            idleSeconds = policy == PoolPolicy.Burst ? Mathf.Max(0f, idleSeconds) : 0f;
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

        public static string NormalizeLocation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (end >= start && char.IsWhiteSpace(value[end]))
            {
                end--;
            }

            while (end >= start && (value[end] == '/' || value[end] == '\\'))
            {
                end--;
            }

            if (end < start)
            {
                return string.Empty;
            }

            bool hasBackslash = false;
            for (int i = start; i <= end; i++)
            {
                if (value[i] == '\\')
                {
                    hasBackslash = true;
                    break;
                }
            }

            string normalized = start == 0 && end == value.Length - 1
                ? value
                : value.Substring(start, end - start + 1);
            if (hasBackslash)
            {
                normalized = normalized.Replace('\\', '/');
            }

            int lastSlash = normalized.LastIndexOf('/');
            int extension = normalized.LastIndexOf('.');
            if (extension > lastSlash)
            {
                normalized = normalized.Substring(0, extension);
            }

            if (normalized.StartsWith("Assets/Bundles/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Assets/Bundles/".Length);
            }
            else if (normalized.StartsWith("Assets/Bundle/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Assets/Bundle/".Length);
            }

            return normalized;
        }
    }
}
