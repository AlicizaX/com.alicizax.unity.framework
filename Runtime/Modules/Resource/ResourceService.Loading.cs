using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    internal sealed partial class ResourceService
    {
        private sealed class LoadingOperationState : MemoryObject
        {
            public ulong Key;
            public int Generation;
            public ResourceAssetKind AssetKind;
            public AssetHandle AssetHandle;
            public SubAssetsHandle SubAssetsHandle;
            public Exception Error;
            public int WaiterCount;
            public int AssetId = -1;
            public uint AssetGeneration;
            public bool IsDone;
            public bool Succeeded;

            public HandleBase Handle => AssetHandle != null ? AssetHandle : SubAssetsHandle;

            public override void Clear()
            {
                Key = 0;
                Generation = 0;
                AssetKind = ResourceAssetKind.Unknown;
                AssetHandle = null;
                SubAssetsHandle = null;
                Error = null;
                WaiterCount = 0;
                AssetId = -1;
                AssetGeneration = 0;
                IsDone = false;
                Succeeded = false;
            }
        }

        private ResourceLeaseHandle AcquireLeaseSync(in ResourceKey key, ResourceLeaseKind kind)
        {
            if (!CanLoadResources || (!key.HasResolvedIds && string.IsNullOrEmpty(key.Location)))
                return ResourceLeaseHandle.Invalid;
            if (TryGetCachedAssetId(key, out int assetId))
                return AcquireLease(assetId, kind);
            if (key.HasResolvedIds)
                return ResourceLeaseHandle.Invalid;

            LoadingOperationState operation = JoinLoading(key, true, 0);
            try
            {
                if (!operation.IsDone && operation.Handle is { IsValid: true, IsDone: false })
                {
                    HandleBase joined = null;
                    try
                    {
                        joined = operation.AssetKind == ResourceAssetKind.SubAssets
                            ? Loader.LoadSubAssets(NormalizePackageName(key.PackageName), key.Location, true)
                            : Loader.LoadAsset(NormalizePackageName(key.PackageName), key.Location, NormalizeAssetType(key.AssetType, operation.AssetKind), true, 0);
                    }
                    finally
                    {
                        DisposeHandle(joined);
                    }
                }
                PollLoading(operation);
                if (operation.Error != null)
                    ExceptionDispatchInfo.Capture(operation.Error).Throw();
                return CanLoadResources && IsLoadingResultCurrent(operation) ? AcquireLease(operation.AssetId, kind) : ResourceLeaseHandle.Invalid;
            }
            finally
            {
                LeaveLoading(operation);
            }
        }

        private async UniTask<ResourceLeaseHandle> AcquireLeaseAsync(ResourceKey key, ResourceLeaseKind kind, CancellationToken cancellationToken,
            uint priority = 0, LoadAssetUpdateCallback progressCallback = null, object userData = null, ResourceLoadLifetime lifetime = default)
        {
            if (cancellationToken.IsCancellationRequested || !lifetime.IsCurrent || !CanLoadResources || (!key.HasResolvedIds && string.IsNullOrEmpty(key.Location)))
                return ResourceLeaseHandle.Invalid;
            if (TryGetCachedAssetId(key, out int assetId))
                return AcquireLease(assetId, kind);
            if (key.HasResolvedIds)
                return ResourceLeaseHandle.Invalid;

            LoadingOperationState operation = JoinLoading(key, false, priority);
            try
            {
                float lastProgress = -1f;
                while (!operation.IsDone)
                {
                    if (cancellationToken.IsCancellationRequested || !lifetime.IsCurrent || !CanLoadResources)
                        return ResourceLeaseHandle.Invalid;
                    PollLoading(operation);
                    if (progressCallback != null && !operation.IsDone)
                    {
                        float progress = operation.Handle.Progress;
                        if (lastProgress < 0f || progress - lastProgress >= ProgressCallbackThreshold)
                        {
                            lastProgress = progress;
                            progressCallback(key.Location, progress, userData);
                        }
                    }
                    if (!operation.IsDone)
                        await UniTask.Yield();
                }
                if (operation.Error != null)
                    ExceptionDispatchInfo.Capture(operation.Error).Throw();
                if (cancellationToken.IsCancellationRequested || !lifetime.IsCurrent || !CanLoadResources || !IsLoadingResultCurrent(operation))
                    return ResourceLeaseHandle.Invalid;
                progressCallback?.Invoke(key.Location, 1f, userData);
                if (cancellationToken.IsCancellationRequested || !lifetime.IsCurrent || !CanLoadResources || !IsLoadingResultCurrent(operation))
                    return ResourceLeaseHandle.Invalid;
                return AcquireLease(operation.AssetId, kind);
            }
            finally
            {
                LeaveLoading(operation);
            }
        }

        private LoadingOperationState JoinLoading(in ResourceKey key, bool synchronous, uint priority)
        {
            ResourceAssetKind kind = NormalizeAssetKind(key.AssetType, key.AssetKind);
            Type type = NormalizeAssetType(key.AssetType, kind);
            ResourceHandleKind handleKind = kind == ResourceAssetKind.SubAssets ? ResourceHandleKind.SubAssetsHandle : ResourceHandleKind.AssetHandle;
            ulong recordKey = GetAssetRecordKey(key.PackageName, key.Location, type, kind, handleKind);
            if (_assetLoadingOperationByKey.TryGetValue(recordKey, out LoadingOperationState operation))
            {
                operation.WaiterCount++;
                return operation;
            }

            operation = MemoryPool.Acquire<LoadingOperationState>();
            operation.Key = recordKey;
            operation.Generation = _assetUnloadGeneration;
            operation.AssetKind = kind;
            operation.WaiterCount = 1;
            _assetLoadingOperationByKey.Add(recordKey, operation);
            RetainResourceKey(recordKey);
            try
            {
                HandleBase handle;
                if (handleKind == ResourceHandleKind.SubAssetsHandle)
                    handle = Loader.LoadSubAssets(NormalizePackageName(key.PackageName), key.Location, synchronous);
                else
                    handle = Loader.LoadAsset(NormalizePackageName(key.PackageName), key.Location, type, synchronous, priority);
                if (operation.IsDone)
                    DisposeHandle(handle);
                else
                {
                    operation.AssetHandle = handle as AssetHandle;
                    operation.SubAssetsHandle = handle as SubAssetsHandle;
                    PollLoading(operation);
                }
            }
            catch (Exception exception)
            {
                operation.Error = exception;
                FinishLoading(operation, false);
            }
            return operation;
        }

        private void PollLoading(LoadingOperationState operation)
        {
            if (operation.IsDone)
                return;
            HandleBase handle = operation.Handle;
            if (handle == null || !handle.IsValid)
            {
                FinishLoading(operation, false);
                return;
            }
            if (!handle.IsDone)
                return;
            bool success = !_isDestroying && operation.Generation == _assetUnloadGeneration && handle.Status == EOperationStatus.Succeeded &&
                           (operation.SubAssetsHandle != null || operation.AssetHandle.AssetObject != null);
            FinishLoading(operation, success);
        }

        private void FinishLoading(LoadingOperationState operation, bool success)
        {
            if (operation.IsDone)
                return;
            if (success)
            {
                operation.AssetId = CreateAssetRecord(operation);
                operation.AssetGeneration = GetAssetSlotRef(operation.AssetId).Generation;
            }
            HandleBase handle = operation.Handle;
            operation.AssetHandle = null;
            operation.SubAssetsHandle = null;
            operation.Succeeded = success;
            operation.IsDone = true;
            _assetLoadingOperationByKey.Remove(operation.Key);
            ReleaseResourceKey(operation.Key);
            if (!success)
                DisposeHandle(handle);
        }

        private bool IsLoadingResultCurrent(LoadingOperationState operation)
        {
            return !_isDestroying && operation.Generation == _assetUnloadGeneration && operation.Succeeded &&
                   GetAssetSlotRef(operation.AssetId).Generation == operation.AssetGeneration &&
                   GetAssetSlotRef(operation.AssetId).State != ResourceAssetState.Released;
        }

        private void LeaveLoading(LoadingOperationState operation)
        {
            if (--operation.WaiterCount != 0)
                return;
            if (!operation.IsDone)
                FinishLoading(operation, false);
            if (IsLoadingResultCurrent(operation))
            {
                ref AssetSlot slot = ref GetAssetSlotRef(operation.AssetId);
                slot.PendingRefCount--;
                UpdateAssetStateAndIdleQueue(operation.AssetId, ref slot);
            }
            MemoryPool.Release(operation);
        }

        private void ShutdownLoadingOperations()
        {
            foreach (LoadingOperationState operation in _assetLoadingOperationByKey.Values)
            {
                HandleBase handle = operation.Handle;
                operation.AssetHandle = null;
                operation.SubAssetsHandle = null;
                operation.IsDone = true;
                operation.Succeeded = false;
                ReleaseResourceKey(operation.Key);
                DisposeHandle(handle);
            }
            _assetLoadingOperationByKey.Clear();
        }

        private static void DisposeHandle(HandleBase handle)
        {
            if (handle is { IsValid: true })
                handle.Dispose();
        }

        internal static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
