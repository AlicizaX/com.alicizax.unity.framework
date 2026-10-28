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

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _byHandle = new(RuntimeTypeHandleComparer.Instance);
        private static readonly Dictionary<string, UIMetaInfo> _byName = new();

        /// <summary>
        /// 注册 UI 类型。注册期校验类型与层级，非法注册直接抛异常（冷路径 fail fast）。
        /// </summary>
        public static void Register(Type uiType, Type holderType, UILayer layer = UILayer.UI, int cacheTime = 0,
            bool needUpdate = false)
        {
            Validate(uiType, holderType, layer);
            var info = new UIMetaInfo(uiType.TypeHandle, holderType.TypeHandle, layer, cacheTime, needUpdate);
            _byHandle[uiType.TypeHandle] = info;
            string fullName = uiType.FullName;
            if (!string.IsNullOrEmpty(fullName)) _byName[fullName] = info;
        }

        public static bool TryGet(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            if (_byHandle.TryGetValue(handle, out info)) return true;
            if (handle.Value == IntPtr.Zero) return false;
            return TryReflectAndRegister(Type.GetTypeFromHandle(handle), out info);
        }

        public static bool TryGet(string typeName, out UIMetaInfo info)
        {
            info = default;
            if (string.IsNullOrEmpty(typeName)) return false;
            if (_byName.TryGetValue(typeName, out info)) return true;
            Type type = AlicizaX.Utility.Assembly.GetType(typeName);
            return type != null && TryReflectAndRegister(type, out info);
        }

        private static void Validate(Type uiType, Type holderType, UILayer layer)
        {
            if (uiType == null) throw new ArgumentNullException(nameof(uiType));
            if (holderType == null) throw new ArgumentNullException(nameof(holderType));
            if (!typeof(UIBase).IsAssignableFrom(uiType))
                throw new ArgumentException($"[UI] {uiType.FullName} must inherit UIBase.", nameof(uiType));
            if (uiType.IsAbstract || uiType.ContainsGenericParameters)
                throw new ArgumentException($"[UI] {uiType.FullName} must be a concrete type.", nameof(uiType));
            if (!typeof(UIHolderObjectBase).IsAssignableFrom(holderType))
                throw new ArgumentException($"[UI] {holderType.FullName} must inherit UIHolderObjectBase.", nameof(holderType));
            if ((uint)layer >= (uint)UILayer.All)
                throw new ArgumentOutOfRangeException(nameof(layer), layer, "[UI] UILayer.All is not a display layer.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegister(Type uiType, out UIMetaInfo info)
        {
            info = default;
            if (uiType == null || !typeof(UIBase).IsAssignableFrom(uiType) ||
                uiType.IsAbstract || uiType.ContainsGenericParameters)
            {
                return false;
            }

            Log.Warning($"[UI] UI not pre-registered: {uiType.FullName}, using reflection fallback.");

            Type holderType = ResolveHolderType(uiType);
            if (holderType == null)
            {
                Log.Error($"[UI] Failed to register UI type {uiType.FullName}: holder type not found.");
                return false;
            }
            if (!typeof(UIHolderObjectBase).IsAssignableFrom(holderType))
            {
                Log.Error($"[UI] Failed to register UI type {uiType.FullName}: holder type is not a UIHolderObjectBase.");
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

            if ((uint)layer >= (uint)UILayer.All)
            {
                Log.Error($"[UI] Failed to register UI type {uiType.FullName}: invalid layer {layer}.");
                return false;
            }

            Register(uiType, holderType, layer, cacheTime, needUpdate);
            info = _byHandle[uiType.TypeHandle];
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
