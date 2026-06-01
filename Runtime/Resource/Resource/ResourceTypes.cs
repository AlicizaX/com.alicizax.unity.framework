using System;

namespace AlicizaX.Resource.Runtime
{
    public readonly struct ResourceLeaseHandle
    {
        public static readonly ResourceLeaseHandle Invalid = new ResourceLeaseHandle(-1, 0);

        public readonly int Index;
        public readonly uint Generation;

        public ResourceLeaseHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Index >= 0 && Generation != 0;
    }

    public readonly struct ResourceKey
    {
        public readonly int LoadKeyId;
        public readonly int ViewKeyId;
        public readonly string PackageName;
        public readonly string Location;
        public readonly Type AssetType;
        public readonly ResourceAssetKind AssetKind;

        public ResourceKey(string location, string packageName = "", Type assetType = null, ResourceAssetKind assetKind = ResourceAssetKind.Unknown)
        {
            LoadKeyId = 0;
            ViewKeyId = 0;
            PackageName = packageName ?? string.Empty;
            Location = location ?? string.Empty;
            AssetType = assetType;
            AssetKind = assetKind;
        }

        public ResourceKey(int loadKeyId, int viewKeyId)
        {
            LoadKeyId = loadKeyId;
            ViewKeyId = viewKeyId;
            PackageName = string.Empty;
            Location = string.Empty;
            AssetType = null;
            AssetKind = ResourceAssetKind.Unknown;
        }

        public bool HasResolvedIds => LoadKeyId > 0;

        public static ResourceKey Asset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return new ResourceKey(location, packageName, typeof(T), ResourceAssetKind.Asset);
        }
    }

    public enum ResourceAssetKind : byte
    {
        Unknown = 0,
        Asset = 1,
        Sprite = 2,
        Material = 3,
        Prefab = 4,
        SubAssets = 5,
        ExternalHandle = 6,
    }

    public enum ResourceAssetState : byte
    {
        Released = 0,
        Loading = 1,
        Active = 2,
        KeepAlive = 3,
        Idle = 4,
    }

    internal enum ResourceHandleKind : byte
    {
        None = 0,
        AssetHandle = 1,
        SubAssetsHandle = 2,
        ExternalHandleLease = 3,
    }

    internal enum ResourceLeaseKind : byte
    {
        None = 0,
        Direct = 1,
        Binding = 2,
    }

    internal enum ResourceLeaseState : byte
    {
        Free = 0,
        Active = 1,
        Released = 2,
    }

    [Flags]
    internal enum ResourceLeaseOptions : byte
    {
        None = 0,
        KeepAliveOnRelease = 1,
    }

    public struct ResourceAssetInfo
    {
        public int LoadKeyId;
        public string Package;
        public string Location;
        public string TypeName;
        public ResourceAssetKind Kind;
        public ResourceAssetState State;
        public int DirectRefCount;
        public int LegacyDirectRefCount;
        public int BindingRefCount;
        public int KeepAliveRefCount;
        public float KeepAliveExpireIn;
        public float IdleExpireIn;
        public int RefCountTotal;
        public bool IdleReleaseRequested;
        public bool HandleValid;
        public byte HandleKind;
    }

    public struct ResourceBindingInfo
    {
        public bool Active;
        public int BindingIndex;
        public int OwnerId;
        public uint OwnerGeneration;
        public int TargetGameObjectId;
        public int TargetComponentId;
        public long SlotKey;
        public int AssetId;
        public int ViewKeyId;
        public ResourceLeaseHandle Lease;
        public uint Version;
        public ushort SubIndex;
        public ResourceBindingSlotType SlotType;
        public bool HasAppliedAsset;
        public bool HasRuntimeObject;
#if UNITY_EDITOR
        public UnityEngine.Object TargetObject;
#endif
    }

    public struct ResourceOwnerInfo
    {
        public bool Active;
        public int OwnerIndex;
        public int OwnerId;
        public int GameObjectId;
        public uint Generation;
        public int BindingCount;
        public int RegisteredTargetCount;
        public bool HasOwnerObject;
#if UNITY_EDITOR
        public UnityEngine.GameObject OwnerObject;
#endif
    }
}
