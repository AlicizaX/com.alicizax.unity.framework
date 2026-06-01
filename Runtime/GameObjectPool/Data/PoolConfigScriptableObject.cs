using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "GameplaySystem/PoolConfig", order = 10)]
    public sealed class PoolConfigScriptableObject : ScriptableObject
    {
        public List<PoolEntry> entries = new List<PoolEntry>();

        internal PoolCompiledCatalog BuildCatalog()
        {
            Normalize();

            if (entries == null || entries.Count == 0)
            {
                return PoolCompiledCatalog.Empty();
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
                return PoolCompiledCatalog.Empty();
            }

            var normalizedEntries = new PoolEntry[validCount];
            int writeIndex = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PoolEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                {
                    continue;
                }

                normalizedEntries[writeIndex++] = entry;
            }

            Array.Sort(normalizedEntries, PoolEntry.CompareByPriority);
            return PoolCompiledCatalog.Build(normalizedEntries);
        }

        public void Normalize()
        {
            if (entries == null)
            {
                entries = new List<PoolEntry>();
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                entries[i]?.Normalize();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Normalize();
        }
#endif
    }
}
