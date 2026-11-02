using System;

namespace AlicizaX.Resource.Runtime
{
    [Flags]
    public enum ResourceBindingOptions : byte
    {
        None = 0,
        SetNativeSize = 1,
        Synchronous = 2,
    }

    internal enum ResourceBindingSlot : byte
    {
        ImageSprite = 0,
        SpriteRendererSprite = 1,
        ImageMaterial = 2,
        RendererSharedMaterial = 3,
        RendererMaterialInstance = 4,
    }

    internal struct ResourceBindingInfo
    {
        public int Index;
        public int Scope;
        public UnityEngine.Component Target;
        public ulong TargetId;
        public ResourceBindingSlot Slot;
        public int AssetId;
        public uint Version;
        public bool HasRuntimeInstance;
    }
}
