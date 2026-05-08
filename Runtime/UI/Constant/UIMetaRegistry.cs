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
            public readonly bool FullScreen;
            public readonly int CacheTime;
            public readonly bool NeedUpdate;
            public readonly int TypeId;

            public UIMetaInfo(RuntimeTypeHandle runtimeTypeHandle, RuntimeTypeHandle holderRuntimeTypeHandle, UILayer windowLayer, bool fullScreen, int cacheTime, bool needUpdate, int typeId)
            {
                RuntimeTypeHandle = runtimeTypeHandle;
                HolderRuntimeTypeHandle = holderRuntimeTypeHandle;
                UILayer = (int)windowLayer;
                FullScreen = fullScreen;
                CacheTime = cacheTime;
                NeedUpdate = needUpdate;
                TypeId = typeId;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _typeHandleMap = new();
        private static readonly Dictionary<string, RuntimeTypeHandle> _stringHandleMap = new();
        private static int _nextTypeId;
        public static int TypeCount => _nextTypeId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register(Type uiType, Type holderType, UILayer layer = UILayer.UI, bool fullScreen = false, int cacheTime = 0, bool needUpdate = false)
        {
            RuntimeTypeHandle holderHandle = holderType.TypeHandle;
            RuntimeTypeHandle uiHandle = uiType.TypeHandle;
            int typeId = _typeHandleMap.TryGetValue(uiHandle, out UIMetaInfo oldInfo) ? oldInfo.TypeId : _nextTypeId++;
            _typeHandleMap[uiHandle] = new UIMetaInfo(uiHandle, holderHandle, layer, fullScreen, cacheTime, needUpdate, typeId);
            _stringHandleMap[uiType.Name] = uiHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            if (_typeHandleMap.TryGetValue(handle, out info))
            {
                return true;
            }

            return TryReflectAndRegister(Type.GetTypeFromHandle(handle), out info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(string typeName, out UIMetaInfo info)
        {
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
            if (uiType == null)
            {
                info = default;
                return false;
            }

            Log.Warning($"[UI] UI not pre-registered: {uiType.FullName}, using reflection fallback.");
            return TryReflectAndRegisterInternal(uiType, out info);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegisterInternal(Type uiType, out UIMetaInfo info)
        {
            try
            {
                Type holderType = ResolveHolderType(uiType);
                if (holderType == null)
                {
                    info = default;
                    return false;
                }

                UILayer layer = UILayer.UI;
                bool fullScreen = false;
                int cacheTime = 0;
                bool needUpdate = false;

                IList<CustomAttributeData> attributes = CustomAttributeData.GetCustomAttributes(uiType);
                for (int i = 0; i < attributes.Count; i++)
                {
                    CustomAttributeData attribute = attributes[i];
                    string attributeName = attribute.AttributeType.Name;
                    if (attributeName == nameof(WindowAttribute))
                    {
                        IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
                        if (args.Count > 0)
                        {
                            layer = (UILayer)(args[0].Value ?? UILayer.UI);
                        }

                        if (args.Count > 1)
                        {
                            fullScreen = (bool)(args[1].Value ?? false);
                        }

                        if (args.Count > 2)
                        {
                            cacheTime = (int)(args[2].Value ?? 0);
                        }
                    }
                    else if (attributeName == nameof(UIUpdateAttribute))
                    {
                        needUpdate = true;
                    }
                }

                Register(uiType, holderType, layer, fullScreen, cacheTime, needUpdate);
                info = _typeHandleMap[uiType.TypeHandle];
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[UI] Failed to register UI type {uiType.FullName}: {ex.Message}");
            }

            info = default;
            return false;
        }

        private static Type ResolveHolderType(Type uiType)
        {
            Type current = uiType;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType)
                {
                    Type[] genericArgs = current.GetGenericArguments();
                    if (genericArgs.Length == 1 && typeof(UIHolderObjectBase).IsAssignableFrom(genericArgs[0]))
                    {
                        return genericArgs[0];
                    }
                }

                current = current.BaseType;
            }

            return null;
        }
    }
}
