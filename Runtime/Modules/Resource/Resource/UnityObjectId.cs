using UnityEngine;
using Object = UnityEngine.Object;

public static class UnityObjectId
{
#if UNITY_6000_5_OR_NEWER
    public static ulong Get(Object target)
#else
    public static int Get(Object target)
#endif
    {
        if (target == null)
        {
            return 0;
        }

#if UNITY_6000_5_OR_NEWER
        return UnityEngine.EntityId.ToULong(target.GetEntityId());
#elif UNITY_6000_4_OR_NEWER
        return target.GetEntityId();
#else
        return target.GetInstanceID();
#endif
    }

    public static int GetInt(Object target)
    {
        if (target == null)
        {
            return 0;
        }

#if UNITY_6000_5_OR_NEWER
        return unchecked((int)UnityEngine.EntityId.ToULong(target.GetEntityId()));
#elif UNITY_6000_4_OR_NEWER
        return target.GetEntityId();
#else
        return target.GetInstanceID();
#endif
    }
}
