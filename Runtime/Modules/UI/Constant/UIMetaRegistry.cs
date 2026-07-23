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
            public readonly bool NeedUpdate;
            public readonly int TypeId;

            public UIMetaInfo(RuntimeTypeHandle runtimeTypeHandle, RuntimeTypeHandle holderRuntimeTypeHandle, UILayer windowLayer, int cacheTime, bool needUpdate, int typeId)
            {
                RuntimeTypeHandle = runtimeTypeHandle;
                HolderRuntimeTypeHandle = holderRuntimeTypeHandle;
                UILayer = (int)windowLayer;
                CacheTime = cacheTime;
                NeedUpdate = needUpdate;
                TypeId = typeId;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _typeHandleMap = new();
        private static readonly Dictionary<string, RuntimeTypeHandle> _stringHandleMap = new();
        private static readonly HashSet<string> _ambiguousTypeNames = new();
        private static int _nextTypeId;
        public static int TypeCount => _nextTypeId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register(Type uiType, Type holderType, UILayer layer = UILayer.UI, int cacheTime = 0, bool needUpdate = false)
        {
            RuntimeTypeHandle holderHandle = holderType.TypeHandle;
            RuntimeTypeHandle uiHandle = uiType.TypeHandle;
            layer = SanitizeLayer(uiType, layer);
            int typeId = _typeHandleMap.TryGetValue(uiHandle, out UIMetaInfo oldInfo) ? oldInfo.TypeId : _nextTypeId++;
            _typeHandleMap[uiHandle] = new UIMetaInfo(uiHandle, holderHandle, layer, cacheTime, needUpdate, typeId);
            RegisterTypeName(uiType, uiHandle);
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
        public static bool TryGetRegisteredOnly(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            return _typeHandleMap.TryGetValue(handle, out info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(string typeName, out UIMetaInfo info)
        {
            if (_ambiguousTypeNames.Contains(typeName))
            {
                Log.Error($"[UI] Ambiguous UI type name '{typeName}'. Use the full type name instead.");
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
            if (uiType == null)
            {
                info = default;
                return false;
            }

#if UNITY_EDITOR
            if (UIWarningSettings.OtherWarningsEnabled)
            {
                Log.Warning($"[UI] UI not pre-registered: {uiType.FullName}, using reflection fallback.");
            }
#endif
            return TryReflectAndRegisterInternal(uiType, out info);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegisterInternal(Type uiType, out UIMetaInfo info)
        {
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
                string attributeName = attribute.AttributeType.Name;
                if (attributeName == nameof(WindowAttribute))
                {
                    IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
                    if (args.Count > 0)
                    {
                        layer = ReadLayerArgument(args[0].Value, UILayer.UI);
                    }

                    if (args.Count > 1)
                    {
                        cacheTime = ReadIntArgument(args[1].Value, 0);
                    }
                }
                else if (attributeName == nameof(UIUpdateAttribute))
                {
                    needUpdate = true;
                }
            }

            Register(uiType, holderType, layer, cacheTime, needUpdate);
            info = _typeHandleMap[uiType.TypeHandle];
            return true;
        }

        private static void RegisterTypeName(Type uiType, RuntimeTypeHandle uiHandle)
        {
            string fullName = uiType.FullName;
            if (!string.IsNullOrEmpty(fullName))
            {
                _stringHandleMap[fullName] = uiHandle;
            }

            string shortName = uiType.Name;
            if (string.IsNullOrEmpty(shortName) || _ambiguousTypeNames.Contains(shortName))
            {
                return;
            }

            if (_stringHandleMap.TryGetValue(shortName, out RuntimeTypeHandle existingHandle) && !existingHandle.Equals(uiHandle))
            {
                _stringHandleMap.Remove(shortName);
                _ambiguousTypeNames.Add(shortName);
                Log.Error($"[UI] Ambiguous UI type name '{shortName}' between '{Type.GetTypeFromHandle(existingHandle)?.FullName}' and '{fullName}'. Use the full type name instead.");
                return;
            }

            _stringHandleMap[shortName] = uiHandle;
        }

        private static UILayer SanitizeLayer(Type uiType, UILayer layer)
        {
            if ((uint)layer < (uint)UILayer.All)
            {
                return layer;
            }

            Log.Error($"[UI] Invalid layer '{layer}' for UI type {uiType?.FullName}. UILayer.All is not a window layer; fallback to UILayer.UI.");
            return UILayer.UI;
        }

        private static UILayer ReadLayerArgument(object value, UILayer fallback)
        {
            return value == null ? fallback : (UILayer)Convert.ToInt32(value);
        }

        private static int ReadIntArgument(object value, int fallback)
        {
            return value == null ? fallback : Convert.ToInt32(value);
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
