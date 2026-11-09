using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 视图类型的注册表。源生成器在启动时注册所有视图；未注册的类型在首次使用时反射补注册。
    /// </summary>
    public static class UIMetaRegistry
    {
        /// <summary>视图类型的注册信息：注册时一次算好，之后只读。重复注册以最后一次为准。</summary>
        public sealed class UIMetaInfo
        {
            public readonly Type ViewType;
            public readonly Type HolderType;
            public readonly RuntimeTypeHandle Handle;
            public readonly UILayer Layer;
            public readonly int CacheTime;
            public readonly bool HasUpdate;
            public readonly bool IsWindow;

            internal UIMetaInfo(Type viewType, Type holderType, UILayer layer, int cacheTime, bool hasUpdate)
            {
                ViewType = viewType;
                HolderType = holderType;
                Handle = viewType.TypeHandle;
                Layer = layer;
                CacheTime = cacheTime;
                HasUpdate = hasUpdate;
                IsWindow = typeof(UIWindow).IsAssignableFrom(viewType);
            }

            public string Name => ViewType.Name;
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _byHandle = new(RuntimeTypeHandleComparer.Instance);
        private static readonly Dictionary<string, UIMetaInfo> _byName = new();
        private static readonly HashSet<string> _unknownNames = new();

        /// <summary>注册 UI 类型。非法注册直接抛异常（冷路径 fail fast）。</summary>
        public static void Register(Type uiType, Type holderType, UILayer layer = UILayer.UI, int cacheTime = UICacheTime.None,
            bool needUpdate = false)
        {
            Validate(uiType, holderType, layer);
            var info = new UIMetaInfo(uiType, holderType, layer, cacheTime, needUpdate);
            _byHandle[info.Handle] = info;
            string fullName = uiType.FullName;
            if (string.IsNullOrEmpty(fullName)) return;
            _byName[fullName] = info;
            _unknownNames.Remove(fullName);
        }

        /// <summary>按类型句柄取注册信息；未预注册时反射补注册。</summary>
        public static bool TryGet(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            if (_byHandle.TryGetValue(handle, out info)) return true;
            return handle.Value != IntPtr.Zero && TryReflectAndRegister(Type.GetTypeFromHandle(handle), out info);
        }

        /// <summary>按类型全名取注册信息；查不到的名字只反射一次。</summary>
        public static bool TryGet(string typeName, out UIMetaInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(typeName)) return false;
            if (_byName.TryGetValue(typeName, out info)) return true;
            if (_unknownNames.Contains(typeName)) return false;
            if (!TryReflectAndRegister(AlicizaX.Utility.Assembly.GetType(typeName), out info))
            {
                _unknownNames.Add(typeName);
                return false;
            }
            _byName[typeName] = info;
            return true;
        }

        /// <summary>取注册信息，取不到时记录错误并返回 null。</summary>
        internal static UIMetaInfo Require(Type type)
        {
            if (TryGet(type.TypeHandle, out UIMetaInfo info)) return info;
            Log.Error("[UI] Unknown UI type: {0}.", type);
            return null;
        }

        internal static UIMetaInfo Require(RuntimeTypeHandle handle)
        {
            if (TryGet(handle, out UIMetaInfo info)) return info;
            Log.Error("[UI] Unknown UI type handle: {0}.", handle.Value);
            return null;
        }

        /// <summary>按类型全名取注册信息，取不到时记录错误并返回 null。</summary>
        internal static UIMetaInfo Require(string typeName)
        {
            if (TryGet(typeName, out UIMetaInfo info)) return info;
            Log.Error("[UI] Unknown UI type: {0}.", typeName);
            return null;
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
            info = null;
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
            int cacheTime = UICacheTime.None;
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
