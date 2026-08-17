using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX
{
    [CreateAssetMenu(fileName = "GameObjectPoolConfig", menuName = "AlicizaX/PoolConfig", order = 10)]
    public sealed class PoolConfigScriptableObject : ScriptableObject
    {
        public List<PoolEntry> entries = new List<PoolEntry>();

        internal PoolCompiledCatalog BuildCatalog()
        {
            Normalize();
            return PoolCompiledCatalog.Build(entries);
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
