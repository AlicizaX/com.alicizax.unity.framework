using UnityEngine;
using Object = UnityEngine.Object;

public static class UnityObjectId
{
    public static ulong Get(Object target)
    {
        if (target == null)
        {
            return 0;
        }

#if UNITY_6000_5_OR_NEWER
            return EntityId.ToULong(target.GetEntityId());
#else
            return unchecked((ulong)(uint)target.GetInstanceID());
#endif
    }
}
