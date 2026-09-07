using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlicizaX.UI.Runtime
{
    public static class UIResRegistry
    {
        public readonly struct UIResInfo
        {
            public readonly string Location;
            public readonly EUIResLoadType LoadType;
            public readonly UIBackend Backend;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public UIResInfo(string location, EUIResLoadType loadType, UIBackend backend)
            {
                Location = location;
                LoadType = loadType;
                Backend = backend;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIResInfo> _typeHandleMap = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register(Type holderType, string location, EUIResLoadType loadType, UIBackend backend = UIBackend.UGUI)
        {
            RuntimeTypeHandle handle = holderType.TypeHandle;
            _typeHandleMap[handle] = new UIResInfo(location, loadType, backend);
        }

        public static bool TryGet(RuntimeTypeHandle handle, out UIResInfo info)
        {
            if (_typeHandleMap.TryGetValue(handle, out info))
            {
                return true;
            }

            return TryReflectAndRegister(Type.GetTypeFromHandle(handle), out info);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegister(Type holderType, out UIResInfo info)
        {
            if (holderType == null)
            {
                info = default;
                return false;
            }

            return TryReflectAndRegisterInternal(holderType, out info);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegisterInternal(Type holderType, out UIResInfo info)
        {
            IList<CustomAttributeData> attributes = CustomAttributeData.GetCustomAttributes(holderType);
            for (int i = 0; i < attributes.Count; i++)
            {
                CustomAttributeData attribute = attributes[i];
                if (attribute.AttributeType.Name != nameof(UIResAttribute))
                {
                    continue;
                }

                IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
                string resLocation = args.Count > 0 ? (string)(args[0].Value ?? string.Empty) : string.Empty;
                EUIResLoadType resLoadType = args.Count > 1 && args[1].Value != null
                    ? (EUIResLoadType)Convert.ToByte(args[1].Value)
                    : EUIResLoadType.AssetBundle;
                UIBackend declaredBackend = args.Count > 2 && args[2].Value != null
                    ? (UIBackend)Convert.ToByte(args[2].Value)
                    : UIBackend.UGUI;
                UIBackend backend = typeof(UIToolkitHolderBase).IsAssignableFrom(holderType)
                    ? UIBackend.UIToolkit
                    : declaredBackend;

                Register(holderType, resLocation, resLoadType, backend);
                info = _typeHandleMap[holderType.TypeHandle];
                return true;
            }

            Log.Error($"[UI] Failed to register UI resource for {holderType.FullName}: UIResAttribute not found.");
            info = default;
            return false;
        }
    }
}
