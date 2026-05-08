using AlicizaX.ObjectPool;
using AlicizaX;

namespace AlicizaX.Resource.Runtime
{
    public class AssetItemObject : ObjectBase<UnityEngine.Object>
    {
        public static AssetItemObject Create(string location, UnityEngine.Object target)
        {
            AssetItemObject item = MemoryPool.Acquire<AssetItemObject>();
            item.Initialize(location, target);
            return item;
        }

        protected internal override void Release(bool isShutdown)
        {
        }
    }
}
