using System;
using System.Collections.Generic;
using System.Threading;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    internal static class ResourceTypeId<T> where T : Object
    {
        internal static readonly int Value = ResourceTypeIdCounter.Next();
    }

    internal static class ResourceTypeIdCounter
    {
        private const int MaxTypeId = (1 << 15) - 1;
        private static int s_lastTypeId;

        internal static int Next()
        {
            int id = Interlocked.Increment(ref s_lastTypeId);
            if (id > MaxTypeId)
                throw new InvalidOperationException("Resource asset type id space exhausted.");
            return id;
        }
    }

    internal struct ResourceAssetKey
    {
        public int PackageId;
        public int LocationId;
        public Type AssetType;
        public bool SubAssets;
    }

    internal sealed class ResourceKeyRegistry
    {
        private const int MaxPackageId = (1 << 16) - 1;

        private readonly Dictionary<string, int> _packageIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _locationIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _packageNames = new List<string> { null };
        private readonly List<string> _locationNames = new List<string> { null };
        private readonly ResourceUlongIntMap _assetIds = new ResourceUlongIntMap();
        private readonly ResourceSlotTable<ResourceAssetKey> _keys = new ResourceSlotTable<ResourceAssetKey>();

        internal ResourceKeyRegistry()
        {
            _keys.Allocate();
        }

        internal int Count => _keys.Count;

        internal ref ResourceAssetKey this[int assetId] => ref _keys[assetId];

        internal string PackageName(int packageId) => _packageNames[packageId];

        internal string LocationName(int locationId) => _locationNames[locationId];

        internal int PackageId(string packageName)
        {
            if (_packageIds.TryGetValue(packageName, out int id))
                return id;
            id = _packageNames.Count;
            if (id > MaxPackageId)
                throw new InvalidOperationException("Resource package id space exhausted.");
            _packageNames.Add(packageName);
            _packageIds.Add(packageName, id);
            return id;
        }

        internal int LocationId(string location)
        {
            if (_locationIds.TryGetValue(location, out int id))
                return id;
            id = _locationNames.Count;
            _locationNames.Add(location);
            _locationIds.Add(location, id);
            return id;
        }

        internal int AssetId(int packageId, string location, int typeId, Type assetType, bool subAssets)
        {
            int locationId = LocationId(location);
            ulong packed = ((ulong)(uint)locationId << 32) | ((ulong)(uint)packageId << 16) | ((ulong)(uint)typeId << 1) | (subAssets ? 1UL : 0UL);
            if (_assetIds.TryGetValue(packed, out int assetId))
                return assetId;

            assetId = _keys.Allocate();
            ref ResourceAssetKey key = ref _keys[assetId];
            key.PackageId = packageId;
            key.LocationId = locationId;
            key.AssetType = assetType;
            key.SubAssets = subAssets;
            _assetIds.Set(packed, assetId);
            return assetId;
        }
    }

    internal sealed class ResourceSlotTable<T> where T : struct
    {
        private const int PageBits = 8;
        private const int PageSize = 1 << PageBits;
        private const int PageMask = PageSize - 1;

        private T[][] _pages = new T[4][];
        private int[] _freeSlots = Array.Empty<int>();
        private int _freeCount;

        internal int Count { get; private set; }

        internal int LiveCount => Count - _freeCount;

        internal ref T this[int index] => ref _pages[index >> PageBits][index & PageMask];

        internal int Allocate()
        {
            if (_freeCount > 0)
                return _freeSlots[--_freeCount];

            int index = Count++;
            int page = index >> PageBits;
            if ((index & PageMask) == 0)
            {
                if (page == _pages.Length)
                    Array.Resize(ref _pages, page << 1);
                _pages[page] = new T[PageSize];
                Array.Resize(ref _freeSlots, index + PageSize);
            }

            return index;
        }

        internal void Free(int index)
        {
            _freeSlots[_freeCount++] = index;
        }
    }
}
