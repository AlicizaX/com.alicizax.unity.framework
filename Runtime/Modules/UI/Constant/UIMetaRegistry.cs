using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlicizaX.UI.Runtime
{
    public static class UIMetaRegistry
    {
        public readonly struct UIMetaInfo
        {
            public readonly RuntimeTypeHandle RuntimeTypeHandle;
            public readonly RuntimeTypeHandle HolderRuntimeTypeHandle;
            public readonly int UILayer;
            public readonly int CacheTime;
            public readonly bool HasUpdate;

            public UIMetaInfo(RuntimeTypeHandle runtimeTypeHandle, RuntimeTypeHandle holderRuntimeTypeHandle,
                UILayer windowLayer, int cacheTime, bool needUpdate)
            {
                RuntimeTypeHandle = runtimeTypeHandle;
                HolderRuntimeTypeHandle = holderRuntimeTypeHandle;
                UILayer = (int)windowLayer;
                CacheTime = cacheTime;
                HasUpdate = needUpdate;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _typeHandleMap = new();
        private static readonly Dictionary<string, RuntimeTypeHandle> _stringHandleMap = new();

        public static void Register(Type uiType, Type holderType, UILayer layer = UILayer.UI, int cacheTime = 0,
            bool needUpdate = false)
        {
            RuntimeTypeHandle holderHandle = holderType.TypeHandle;
            RuntimeTypeHandle uiHandle = uiType.TypeHandle;
            _typeHandleMap[uiHandle] = new UIMetaInfo(uiHandle, holderHandle, layer, cacheTime, needUpdate);

            string fullName = uiType.FullName;
            if (!string.IsNullOrEmpty(fullName))
            {
                _stringHandleMap[fullName] = uiHandle;
            }
        }

        public static bool TryGet(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            if (_typeHandleMap.TryGetValue(handle, out info))
            {
                return true;
            }

            return TryReflectAndRegister(Type.GetTypeFromHandle(handle), out info);
        }

        public static bool TryGet(string typeName, out UIMetaInfo info)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                info = default;
                return false;
            }
            if (_stringHandleMap.TryGetValue(typeName, out RuntimeTypeHandle handle))
            {
                return TryGet(handle, out info);
            }

            Type type = AlicizaX.Utility.Assembly.GetType(typeName);
            if (type != null && TryReflectAndRegister(type, out info))
            {
                return true;
            }

            info = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegister(Type uiType, out UIMetaInfo info)
        {
            if (uiType == null || uiType.IsAbstract || uiType.ContainsGenericParameters)
            {
                info = default;
                return false;
            }

            Log.Warning($"[UI] UI not pre-registered: {uiType.FullName}, using reflection fallback.");

            Type holderType = ResolveHolderType(uiType);
            if (holderType == null)
            {
                Log.Error($"[UI] Failed to register UI type {uiType.FullName}: holder type not found.");
                info = default;
                return false;
            }

            UILayer layer = UILayer.UI;
            int cacheTime = 0;
            bool needUpdate = false;

            IList<CustomAttributeData> attributes = CustomAttributeData.GetCustomAttributes(uiType);
            for (int i = 0; i < attributes.Count; i++)
            {
                CustomAttributeData attribute = attributes[i];
                if (attribute.AttributeType == typeof(WindowAttribute) && typeof(UIWindow).IsAssignableFrom(uiType))
                {
                    IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
                    layer = (UILayer)Convert.ToInt32(args[0].Value);
                    cacheTime = (int)args[1].Value;
                }
                else if (attribute.AttributeType == typeof(UIUpdateAttribute))
                {
                    needUpdate = true;
                }
            }

            Register(uiType, holderType, layer, cacheTime, needUpdate);
            info = _typeHandleMap[uiType.TypeHandle];
            return true;
        }

        private static Type ResolveHolderType(Type uiType)
        {
            Type current = uiType;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType)
                {
                    Type definition = current.GetGenericTypeDefinition();
                    if (definition == typeof(UIWindow<>) || definition == typeof(UIWidget<>))
                    {
                        return current.GetGenericArguments()[0];
                    }
                }

                current = current.BaseType;
            }

            return null;
        }
    }
}
