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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public UIResInfo(string location, EUIResLoadType loadType)
            {
                Location = location;
                LoadType = loadType;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIResInfo> _typeHandleMap = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register(Type holderType, string location, EUIResLoadType loadType)
        {
            RuntimeTypeHandle handle = holderType.TypeHandle;
            _typeHandleMap[handle] = new UIResInfo(location, loadType);
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

            IList<CustomAttributeData> attributes = CustomAttributeData.GetCustomAttributes(holderType);
            for (int i = 0; i < attributes.Count; i++)
            {
                CustomAttributeData attribute = attributes[i];
                if (attribute.AttributeType != typeof(UIResAttribute))
                {
                    continue;
                }

                IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
                string resLocation = (string)args[0].Value;
                EUIResLoadType resLoadType = (EUIResLoadType)Convert.ToInt32(args[1].Value);

                Register(holderType, resLocation, resLoadType);
                info = _typeHandleMap[holderType.TypeHandle];
                return true;
            }

            Log.Error($"[UI] Failed to register UI resource for {holderType.FullName}: UIResAttribute not found.");
            info = default;
            return false;
        }
    }
}
