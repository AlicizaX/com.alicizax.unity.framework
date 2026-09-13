using System;
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    // Type definitions are shared. Instance and operation state belongs to UIBase.
    internal sealed class UIMetadata
    {
        private static readonly Dictionary<RuntimeTypeHandle, UIMetadata> Definitions =
            new(RuntimeTypeHandleComparer.Instance);

        public readonly UIMetaRegistry.UIMetaInfo MetaInfo;
        public readonly UIResRegistry.UIResInfo ResInfo;
        public readonly Type UILogicType;
        public string UILogicTypeName => UILogicType.Name;
        public string UIHolderTypeName => Type.GetTypeFromHandle(MetaInfo.HolderRuntimeTypeHandle).Name;

        private UIMetadata(Type type, UIMetaRegistry.UIMetaInfo meta, UIResRegistry.UIResInfo resource)
        {
            UILogicType = type;
            MetaInfo = meta;
            ResInfo = resource;
        }

        internal static UIMetadata Create(Type type)
        {
            if (Definitions.TryGetValue(type.TypeHandle, out var definition)) return definition;
            if (!UIMetaRegistry.TryGet(type.TypeHandle, out var meta)) return null;
            if (!UIResRegistry.TryGet(meta.HolderRuntimeTypeHandle, out var resource)) return null;
            definition = new UIMetadata(type, meta, resource);
            Definitions.Add(type.TypeHandle, definition);
            return definition;
        }

        internal UIBase CreateUI(UIService service)
        {
            try
            {
                var view = (UIBase)Utility.InstanceFactory.CreateInstanceOptimized(UILogicType);
                view.Service = service;
                view.Metadata = this;
                return view;
            }
            catch (Exception error)
            {
                Log.Exception(error);
                return null;
            }
        }
    }
}
