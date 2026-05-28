using System;
using System.Collections.Generic;
using AlicizaX;
using AlicizaX.ObjectPool;

namespace AlicizaX.UI.Runtime
{
    internal static class UIMetadataFactory
    {
        private static readonly Dictionary<RuntimeTypeHandle, UIMetadata> UIWindowMetadata = new();

        private static readonly IObjectPool<UIMetadataObject> m_UIMetadataPool;

        static UIMetadataFactory()
        {
            m_UIMetadataPool = AppServices.App.Require<IObjectPoolService>().CreatePool<UIMetadataObject>(
                new ObjectPoolCreateOptions(
                    name: "UI Metadata Pool",
                    allowMultiSpawn: false,
                    autoReleaseInterval: 60,
                    capacity: 16,
                    expireTime: 60f,
                    priority: 0));
        }

        internal static UIMetadata GetWindowMetadata<T>()
        {
            return GetWindowMetadata(typeof(T).TypeHandle);
        }

        internal static UIMetadata GetWindowMetadata(RuntimeTypeHandle handle)
        {
            if (!UIWindowMetadata.TryGetValue(handle, out var meta))
            {
                meta = new UIMetadata(Type.GetTypeFromHandle(handle));
                UIWindowMetadata[handle] = meta;
            }

            return meta;
        }

        internal static UIMetadata GetWidgetMetadata<T>()
        {
            return GetWidgetMetadata(typeof(T).TypeHandle);
        }

        internal static UIMetadata GetWidgetMetadata(RuntimeTypeHandle handle)
        {
            return GetFromPool(Type.GetTypeFromHandle(handle));
        }

        private static UIMetadata GetFromPool(Type type)
        {
            if (type == null) return null;


            string typeHandleKey = type.FullName;


            UIMetadataObject metadataObj = m_UIMetadataPool.Spawn(typeHandleKey);

            if (metadataObj != null && metadataObj.Target != null)
            {
                return (UIMetadata)metadataObj.Target;
            }


            UIMetadata newMetadata = new UIMetadata(type);
            UIMetadataObject newMetadataObj = UIMetadataObject.Create(newMetadata, typeHandleKey);

            m_UIMetadataPool.Register(newMetadataObj, true);

            return newMetadata;
        }


        internal static void ReturnToPool(UIMetadata metadata)
        {
            if (metadata == null) return;
            m_UIMetadataPool.UnspawnTarget(metadata);
        }
    }
}
