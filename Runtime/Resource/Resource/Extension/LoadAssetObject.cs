using System;
using AlicizaX;

namespace AlicizaX.Resource.Runtime
{
    [Serializable]
    public class LoadAssetObject : IMemory
    {
        public ISetAssetObject AssetObject { get; private set; }
        public UnityEngine.Object AssetTarget { get; private set; }
#if UNITY_EDITOR
        public bool IsSelect { get; set; }
#endif
        public static LoadAssetObject Create(ISetAssetObject obj, UnityEngine.Object assetTarget)
        {
            LoadAssetObject item = MemoryPool.Acquire<LoadAssetObject>();
            item.AssetObject = obj;
            item.AssetTarget = assetTarget;
            return item;
        }

        public void Clear()
        {
            AssetObject = null;
            AssetTarget = null;
#if UNITY_EDITOR
            IsSelect = false;
#endif
        }
    }
}
