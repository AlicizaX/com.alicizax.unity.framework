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
            public readonly string Name;
            public readonly UILayer Layer;
            public readonly int CacheTime;
            public readonly bool HasUpdate;
            public readonly bool IsWindow;
            internal readonly Func<UIBase> Create;

            internal UIMetaInfo(Type viewType, Type holderType, Func<UIBase> create, bool isWindow, UILayer layer, int cacheTime,
                bool hasUpdate)
            {
                ViewType = viewType;
                HolderType = holderType;
                Handle = viewType.TypeHandle;
                Name = viewType.Name;
                Create = create;
                IsWindow = isWindow;
                Layer = layer;
                CacheTime = cacheTime;
                HasUpdate = hasUpdate;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _byHandle = new(RuntimeTypeHandleComparer.Instance);
        private static readonly Dictionary<string, UIMetaInfo> _byName = new();
        private static readonly HashSet<string> _unknownNames = new();

        /// <summary>注册窗口类型。类型关系由泛型约束保证，create 直接构造实例。</summary>
        public static void RegisterWindow<TView, THolder>(Func<TView> create, UILayer layer, int cacheTime, bool hasUpdate)
            where TView : UIWindow<THolder> where THolder : UIHolderObjectBase =>
            Add(new UIMetaInfo(typeof(TView), typeof(THolder), create, true, layer, cacheTime, hasUpdate));

        /// <summary>注册 Widget 类型。类型关系由泛型约束保证，create 直接构造实例。</summary>
        public static void RegisterWidget<TView, THolder>(Func<TView> create, bool hasUpdate)
            where TView : UIWidget<THolder> where THolder : UIHolderObjectBase =>
            Add(new UIMetaInfo(typeof(TView), typeof(THolder), create, false, UILayer.UI, UICacheTime.None, hasUpdate));

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

        private static void Add(UIMetaInfo info)
        {
            string fullName = info.ViewType.FullName;
            _byHandle[info.Handle] = info;
            _byName[fullName] = info;
            _unknownNames.Remove(fullName);
        }

        /// <summary>
        /// 生成器没有覆盖到的类型（它引用不到的类型等）在这里按反射补注册：
        /// Holder 取自 UIWindow&lt;&gt;/UIWidget&lt;&gt; 的类型实参，是否需要 Update 看是否重写了 OnUpdate。
        /// </summary>
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

            ConstructorInfo constructor = uiType.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
            {
                Log.Error($"[UI] Failed to register UI type {uiType.FullName}: public parameterless constructor not found.");
                return false;
            }

            bool isWindow = typeof(UIWindow).IsAssignableFrom(uiType);
            UILayer layer = UILayer.UI;
            int cacheTime = UICacheTime.None;
            if (isWindow)
            {
                IList<CustomAttributeData> attributes = CustomAttributeData.GetCustomAttributes(uiType);
                for (int i = 0; i < attributes.Count; i++)
                {
                    CustomAttributeData attribute = attributes[i];
                    if (attribute.AttributeType != typeof(WindowAttribute)) continue;
                    IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
                    layer = (UILayer)Convert.ToInt32(args[0].Value);
                    cacheTime = (int)args[1].Value;
                }
            }

            info = new UIMetaInfo(uiType, holderType, () => (UIBase)constructor.Invoke(null), isWindow, layer, cacheTime,
                UIBase.OverridesUpdate(uiType));
            Add(info);
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
