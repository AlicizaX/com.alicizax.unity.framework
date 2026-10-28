using System;
using System.Collections.Generic;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 负责 64 位资源键使用的包名/地址/类型字符串表及其引用计数。独立于服务后，键的生命周期
    /// 与资源槽位解耦，也避免服务同时暴露三套并行数组。
    /// </summary>
    internal sealed class ResourceKeyRegistry
    {
        internal const int ResourceKeyHandleBits = 4;
        internal const int ResourceKeyAssetKindBits = 4;
        internal const int ResourceKeyTypeBits = 12;
        internal const int ResourceKeyLocationBits = 32;
        internal const int ResourceKeyPackageBits = 12;
        internal const int ResourceKeyHandleShift = 0;
        internal const int ResourceKeyAssetKindShift = ResourceKeyHandleShift + ResourceKeyHandleBits;
        internal const int ResourceKeyTypeShift = ResourceKeyAssetKindShift + ResourceKeyAssetKindBits;
        internal const int ResourceKeyLocationShift = ResourceKeyTypeShift + ResourceKeyTypeBits;
        internal const int ResourceKeyPackageShift = ResourceKeyLocationShift + ResourceKeyLocationBits;
        internal const int ResourceKeyPackageMax = (1 << ResourceKeyPackageBits) - 1;
        internal const int ResourceKeyLocationMax = int.MaxValue;
        internal const int ResourceKeyTypeMax = (1 << ResourceKeyTypeBits) - 1;
        internal const int ResourceKeyAssetKindMax = (1 << ResourceKeyAssetKindBits) - 1;
        internal const int ResourceKeyHandleMax = (1 << ResourceKeyHandleBits) - 1;

        private readonly Dictionary<string, int> _packageIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _locationIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<Type, int> _typeIds = new Dictionary<Type, int>();

        private string[] _packagesById;
        private string[] _locationsById;
        private Type[] _typesById;
        private int[] _packageRefCounts;
        private int[] _locationRefCounts;
        private int[] _typeRefCounts;
        private int _nextPackageId = 1;
        private int _nextLocationId = 1;
        private int _nextTypeId = 1;
        private int _freePackageId;
        private int _freeLocationId;
        private int _freeTypeId;

        internal int PackageIdCount => _packageIds.Count;

        internal int LocationIdCount => _locationIds.Count;

        internal int TypeIdCount => _typeIds.Count;

        internal ulong GetOrAdd(string packageName, string location, Type assetType, ResourceAssetKind assetKind, ResourceHandleKind handleKind)
        {
            return Pack(
                GetOrAddPackageId(packageName),
                GetOrAddLocationId(location),
                GetOrAddTypeId(assetType),
                assetKind,
                handleKind);
        }

        internal bool TryGet(string packageName, string location, Type assetType, ResourceAssetKind assetKind, ResourceHandleKind handleKind, out ulong key)
        {
            key = 0;
            if (!_packageIds.TryGetValue(packageName, out int packageId) ||
                !_locationIds.TryGetValue(location ?? string.Empty, out int locationId) ||
                !_typeIds.TryGetValue(assetType, out int typeId))
            {
                return false;
            }

            key = Pack(packageId, locationId, typeId, assetKind, handleKind);
            return true;
        }

        internal void Retain(ulong key)
        {
            _packageRefCounts[UnpackPackageId(key)]++;
            _locationRefCounts[UnpackLocationId(key)]++;
            _typeRefCounts[UnpackTypeId(key)]++;
        }

        internal void Release(ulong key)
        {
            ReleasePackageId(UnpackPackageId(key));
            ReleaseLocationId(UnpackLocationId(key));
            ReleaseTypeId(UnpackTypeId(key));
        }

        internal string GetPackageName(int id)
        {
            return _packagesById != null && id > 0 && id < _packagesById.Length ? _packagesById[id] : string.Empty;
        }

        internal string GetLocationName(int id)
        {
            return _locationsById != null && id > 0 && id < _locationsById.Length ? _locationsById[id] : string.Empty;
        }

        internal Type GetAssetType(int id)
        {
            return _typesById != null && id > 0 && id < _typesById.Length ? _typesById[id] : null;
        }

        internal void Clear()
        {
            _packageIds.Clear();
            _locationIds.Clear();
            _typeIds.Clear();
            _packagesById = null;
            _locationsById = null;
            _typesById = null;
            _packageRefCounts = null;
            _locationRefCounts = null;
            _typeRefCounts = null;
            _freePackageId = 0;
            _freeLocationId = 0;
            _freeTypeId = 0;
            _nextPackageId = 1;
            _nextLocationId = 1;
            _nextTypeId = 1;
        }

        internal static int UnpackPackageId(ulong key) => (int)((key >> ResourceKeyPackageShift) & ResourceKeyPackageMax);

        internal static int UnpackLocationId(ulong key) => (int)((key >> ResourceKeyLocationShift) & ResourceKeyLocationMax);

        internal static int UnpackTypeId(ulong key) => (int)((key >> ResourceKeyTypeShift) & ResourceKeyTypeMax);

        internal static ulong Pack(int packageId, int locationId, int typeId, ResourceAssetKind assetKind, ResourceHandleKind handleKind)
        {
            return ((ulong)(uint)packageId << ResourceKeyPackageShift) |
                   ((ulong)(uint)locationId << ResourceKeyLocationShift) |
                   ((ulong)(uint)typeId << ResourceKeyTypeShift) |
                   ((ulong)(byte)assetKind << ResourceKeyAssetKindShift) |
                   ((ulong)(byte)handleKind << ResourceKeyHandleShift);
        }

        private int GetOrAddPackageId(string packageName)
        {
            packageName ??= string.Empty;
            if (_packageIds.TryGetValue(packageName, out int id))
                return id;

            id = AllocateId(ref _nextPackageId, ResourceKeyPackageMax, ref _freePackageId, ref _packageRefCounts);
            _packageIds.Add(packageName, id);
            ReserveName(ref _packagesById, ref _packageRefCounts, id);
            _packagesById[id] = packageName;
            return id;
        }

        private int GetOrAddLocationId(string location)
        {
            location ??= string.Empty;
            if (_locationIds.TryGetValue(location, out int id))
                return id;

            id = AllocateId(ref _nextLocationId, ResourceKeyLocationMax, ref _freeLocationId, ref _locationRefCounts);
            _locationIds.Add(location, id);
            ReserveName(ref _locationsById, ref _locationRefCounts, id);
            _locationsById[id] = location;
            return id;
        }

        private int GetOrAddTypeId(Type assetType)
        {
            assetType ??= typeof(UnityEngine.Object);
            if (_typeIds.TryGetValue(assetType, out int id))
                return id;

            id = AllocateId(ref _nextTypeId, ResourceKeyTypeMax, ref _freeTypeId, ref _typeRefCounts);
            _typeIds.Add(assetType, id);
            ReserveName(ref _typesById, ref _typeRefCounts, id);
            _typesById[id] = assetType;
            return id;
        }

        private void ReleasePackageId(int id)
        {
            if (--_packageRefCounts[id] != 0)
                return;

            _packageIds.Remove(_packagesById[id]);
            _packagesById[id] = null;
            FreeId(id, _packageRefCounts, ref _freePackageId);
        }

        private void ReleaseLocationId(int id)
        {
            if (--_locationRefCounts[id] != 0)
                return;

            _locationIds.Remove(_locationsById[id]);
            _locationsById[id] = null;
            FreeId(id, _locationRefCounts, ref _freeLocationId);
        }

        private void ReleaseTypeId(int id)
        {
            if (--_typeRefCounts[id] != 0)
                return;

            _typeIds.Remove(_typesById[id]);
            _typesById[id] = null;
            FreeId(id, _typeRefCounts, ref _freeTypeId);
        }

        private static int AllocateId(ref int nextId, int maxId, ref int freeId, ref int[] refCounts)
        {
            int id;
            if (freeId > 0)
            {
                id = freeId;
                freeId = -refCounts[id];
                refCounts[id] = 0;
            }
            else
            {
                if (nextId <= 0 || nextId > maxId)
                    throw new GameFrameworkException("Resource key id range exceeded.");

                id = nextId++;
            }

            if (refCounts == null || id >= refCounts.Length)
                Array.Resize(ref refCounts, Math.Max(16, id + 1));

            return id;
        }

        private static void FreeId(int id, int[] refCounts, ref int freeId)
        {
            refCounts[id] = -freeId;
            freeId = id;
        }

        private static void ReserveName<T>(ref T[] values, ref int[] refCounts, int id)
        {
            Grow(ref values, id);
            Grow(ref refCounts, id);
        }

        private static void Grow<T>(ref T[] array, int index)
        {
            if (array == null)
            {
                array = new T[Math.Max(16, index + 1)];
                return;
            }

            if (index < array.Length)
                return;

            Array.Resize(ref array, Math.Max(index + 1, array.Length << 1));
        }
    }
}
