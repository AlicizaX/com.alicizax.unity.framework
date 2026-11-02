namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        internal int AssetCount => _records.Count - 1;

        internal int ScopeSlotCount => _scopes.Count;

        internal int GetAssetInfos(ResourceAssetInfo[] results, int startAssetId)
        {
            int count = 0;
            for (int assetId = startAssetId < 1 ? 1 : startAssetId; assetId < _records.Count && count < results.Length; assetId++)
            {
                ref AssetRecord record = ref Record(assetId);
                ref ResourceAssetKey key = ref _keys[assetId];
                results[count++] = new ResourceAssetInfo
                {
                    AssetId = assetId,
                    Package = _keys.PackageName(key.PackageId),
                    Location = _keys.LocationName(key.LocationId),
                    TypeName = key.AssetType.Name,
                    SubAssets = key.SubAssets,
                    State = record.State,
                    RefCount = record.RefCount,
                    IdleSeconds = record.State == ResourceAssetState.Idle ? _now - record.IdleSince : 0f,
                };
            }

            return count;
        }

        internal int GetScopeInfos(ResourceScopeInfo[] results, int startIndex)
        {
            int count = 0;
            for (int index = startIndex; index < _scopes.Count && count < results.Length; index++)
            {
                ref ScopeSlot scope = ref _scopes[index];
                if (!scope.Live)
                    continue;
                results[count++] = new ResourceScopeInfo
                {
                    Index = index,
                    Generation = scope.Generation,
                    Source = scope.Source,
                    Name = scope.Name,
                    EntryCount = scope.EntryCount,
                    BindingCount = _bindings.CountScopeBindings(index),
                    HasOwner = !ReferenceEquals(scope.Owner, null),
                    OwnerObject = scope.Owner != null ? scope.Owner.gameObject : null,
                };
            }

            return count;
        }

        internal ResourceStats GetStats()
        {
            ResourceStats stats = new ResourceStats
            {
                AssetCount = AssetCount,
                LoadingCount = _loadingCount,
                IdleCount = _idleCount,
                LeaseCount = _leaseCount,
                BindingCount = _bindings.Count,
                BindingReferenceCount = _bindings.CountReferences(),
                HitCount = _hitCount,
                IdleHitCount = _idleHitCount,
                JoinCount = _joinCount,
                MissCount = _missCount,
            };

            for (int assetId = 1; assetId < _records.Count; assetId++)
            {
                ref AssetRecord record = ref Record(assetId);
                stats.ReferenceCount += record.RefCount;
                for (LoadWaiter waiter = record.Waiters; waiter != null; waiter = waiter.Next)
                    stats.WaiterCount++;
            }

            for (int index = 0; index < _scopes.Count; index++)
            {
                ref ScopeSlot scope = ref _scopes[index];
                if (!scope.Live)
                    continue;
                stats.ScopeCount++;
                stats.ScopeEntryCount += scope.EntryCount;
            }

            return stats;
        }

        internal int CountIdleList(out bool ordered)
        {
            int count = 0;
            ordered = true;
            float previousIdleSince = float.MinValue;
            for (int assetId = _idleHead; assetId >= 0; assetId = Record(assetId).IdleNext)
            {
                ref AssetRecord record = ref Record(assetId);
                ordered &= record.State == ResourceAssetState.Idle && record.RefCount == 0 && record.IdleSince >= previousIdleSince;
                previousIdleSince = record.IdleSince;
                count++;
            }

            return count;
        }
    }
}
