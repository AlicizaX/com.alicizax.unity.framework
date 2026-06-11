using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal static class ResourceUnityObjectId
    {
        public static int Get(Object target)
        {
            if (target == null)
            {
                return 0;
            }

#if UNITY_6000_4_OR_NEWER
            return target.GetEntityId();
#else
            return target.GetInstanceID();
#endif
        }
    }
}
