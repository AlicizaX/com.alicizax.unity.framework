using System;
using AlicizaX.ObjectPool;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private struct CloseManyTarget
        {
            public RuntimeTypeHandle Handle;
            public UIMetadata Meta;
            public UICloseManyMode Mode;
            public int CallerIndex;
            public int Layer;
        }

        private struct LayerCloseWork
        {
            public int MinRemovedIndex;
            public bool Changed;
        }

        private struct CloseManyPreflightResult
        {
            public bool Success;
            public int TargetCount;
            public int SkippedCount;
            public int FailedCallerIndex;
            public RuntimeTypeHandle FailedHandle;
            public UICloseFailureReason FailureReason;
        }

        private readonly struct BatchCloseOneResult
        {
            public readonly UICloseFailureReason FailureReason;
            public readonly UIFinalizeClosedResult FinalizeResult;

            public BatchCloseOneResult(UICloseFailureReason failureReason, UIFinalizeClosedResult finalizeResult)
            {
                FailureReason = failureReason;
                FinalizeResult = finalizeResult;
            }
        }

        public async UniTask<UICloseManyResult> CloseManyAsync(RuntimeTypeHandle[] handles, UICloseManyMode[] modes, int count, bool force = false)
        {
            if (handles == null || modes == null || count < 0 || count > handles.Length || count > modes.Length)
            {
                return UICloseManyResult.Fail(0, 0, -1, default, UICloseFailureReason.InvalidArguments);
            }

            if (count == 0)
            {
                return UICloseManyResult.Ok(0, 0);
            }

            int[] normalizedTypeIds = null;
            CloseManyTarget[] targets = null;
            LayerCloseWork[] layerWorks = null;

            int closedCount = 0;
            int skippedCount = 0;
            int targetCount;
            int touchedLayerMask;
            int begunLayerMask = 0;

            try
            {
                normalizedTypeIds = SlotArrayPool<int>.Rent(count);
                targets = SlotArrayPool<CloseManyTarget>.Rent(count);
                layerWorks = SlotArrayPool<LayerCloseWork>.Rent((int)UILayer.All);
                Array.Clear(layerWorks, 0, (int)UILayer.All);

                CloseManyPreflightResult preflight = PreflightCloseMany(
                    handles,
                    modes,
                    count,
                    normalizedTypeIds,
                    targets,
                    out touchedLayerMask);

                if (!preflight.Success)
                {
                    return UICloseManyResult.Fail(0, preflight.SkippedCount, preflight.FailedCallerIndex, preflight.FailedHandle, preflight.FailureReason);
                }

                targetCount = preflight.TargetCount;
                skippedCount = preflight.SkippedCount;
                if (targetCount == 0)
                {
                    return UICloseManyResult.Ok(0, skippedCount);
                }

                for (int layerIndex = 0; layerIndex < (int)UILayer.All; layerIndex++)
                {
                    int layerBit = 1 << layerIndex;
                    if ((touchedLayerMask & layerBit) == 0)
                    {
                        continue;
                    }

                    if (!TryBeginLayerMutation(layerIndex))
                    {
                        EndBegunLayerMutations(begunLayerMask);
                        return UICloseManyResult.Fail(0, skippedCount, -1, default, GetLayerBlockedReason(layerIndex));
                    }

                    begunLayerMask |= layerBit;
                }

                try
                {
                    for (int i = 0; i < targetCount; i++)
                    {
                        targets[i].Meta.StackRemovalPending = true;
                    }

                    for (int i = 0; i < targetCount; i++)
                    {
                        CloseManyTarget target = targets[i];
                        BatchCloseOneResult closeOneResult = await CloseOneForBatchAsync(target.Meta, target.Handle, target.Mode, force);
                        if (closeOneResult.FailureReason != UICloseFailureReason.None)
                        {
                            if (IsMetaInOpenStack(target.Meta))
                            {
                                MarkLayerVisualDirty(target.Layer);
                            }

                            return UICloseManyResult.Fail(closedCount, skippedCount, target.CallerIndex, target.Handle, closeOneResult.FailureReason);
                        }

                        LayerCloseWork work = layerWorks[target.Layer];
                        if (!work.Changed)
                        {
                            work.MinRemovedIndex = closeOneResult.FinalizeResult.RemovedIndex;
                            work.Changed = true;
                        }
                        else if (closeOneResult.FinalizeResult.RemovedIndex >= 0 && closeOneResult.FinalizeResult.RemovedIndex < work.MinRemovedIndex)
                        {
                            work.MinRemovedIndex = closeOneResult.FinalizeResult.RemovedIndex;
                        }

                        layerWorks[target.Layer] = work;
                        closedCount++;
                    }

                    UICloseFailureReason refreshFailure = RefreshChangedLayersAfterCloseMany(layerWorks);
                    if (refreshFailure != UICloseFailureReason.None)
                    {
                        return UICloseManyResult.Fail(closedCount, skippedCount, -1, default, refreshFailure);
                    }

                    return UICloseManyResult.Ok(closedCount, skippedCount);
                }
                finally
                {
                    CleanupCloseManyPendingFlags(targets, targetCount);
                    EndBegunLayerMutations(begunLayerMask);
                }
            }
            finally
            {
                SlotArrayPool<int>.Return(normalizedTypeIds, true);
                SlotArrayPool<CloseManyTarget>.Return(targets, true);
                SlotArrayPool<LayerCloseWork>.Return(layerWorks, true);
            }
        }

        private CloseManyPreflightResult PreflightCloseMany(
            RuntimeTypeHandle[] handles,
            UICloseManyMode[] modes,
            int count,
            int[] normalizedTypeIds,
            CloseManyTarget[] targets,
            out int touchedLayerMask)
        {
            int normalizedCount = 0;
            int targetCount = 0;
            int skippedCount = 0;
            touchedLayerMask = 0;

            for (int callerIndex = 0; callerIndex < count; callerIndex++)
            {
                RuntimeTypeHandle handle = handles[callerIndex];
                if (handle.Value == IntPtr.Zero)
                {
                    return FailPreflight(skippedCount, callerIndex, handle, UICloseFailureReason.InvalidHandle);
                }

                if (!UIMetaRegistry.TryGetRegisteredOnly(handle, out UIMetaRegistry.UIMetaInfo metaInfo))
                {
                    return FailPreflight(skippedCount, callerIndex, handle, UICloseFailureReason.UnknownHandle);
                }

                UICloseManyMode mode = modes[callerIndex];
                if (mode != UICloseManyMode.SilentFinalize && mode != UICloseManyMode.Transition)
                {
                    return FailPreflight(skippedCount, callerIndex, handle, UICloseFailureReason.InvalidArguments);
                }

                if (ContainsTypeId(normalizedTypeIds, normalizedCount, metaInfo.TypeId))
                {
                    skippedCount++;
                    continue;
                }

                normalizedTypeIds[normalizedCount] = metaInfo.TypeId;
                normalizedCount++;

                UIMetadata meta = UIMetadataFactory.TryGetWindowMetadata(handle);
                if (meta == null || meta.InCache)
                {
                    skippedCount++;
                    continue;
                }

                int layerIndex = meta.MetaInfo.UILayer;
                if ((uint)layerIndex >= (uint)_openUI.Length || !IsMetaInOpenStack(meta))
                {
                    skippedCount++;
                    continue;
                }

                UIState state = meta.State;
                if (state == UIState.Uninitialized || state == UIState.Destroying || state == UIState.Destroyed)
                {
                    skippedCount++;
                    continue;
                }

                if (meta.CloseInProgress || state == UIState.Closing)
                {
                    return FailPreflight(skippedCount, callerIndex, handle, UICloseFailureReason.AlreadyClosing);
                }

                if (IsLayerBlockedForMutation(layerIndex))
                {
                    return FailPreflight(skippedCount, callerIndex, handle, GetLayerBlockedReason(layerIndex));
                }

                targets[targetCount++] = new CloseManyTarget
                {
                    Handle = handle,
                    Meta = meta,
                    Mode = mode,
                    CallerIndex = callerIndex,
                    Layer = layerIndex,
                };

                touchedLayerMask |= 1 << layerIndex;
            }

            return new CloseManyPreflightResult
            {
                Success = true,
                TargetCount = targetCount,
                SkippedCount = skippedCount,
                FailedCallerIndex = -1,
                FailedHandle = default,
                FailureReason = UICloseFailureReason.None,
            };
        }

        private static CloseManyPreflightResult FailPreflight(
            int skippedCount,
            int failedCallerIndex,
            RuntimeTypeHandle failedHandle,
            UICloseFailureReason reason)
        {
            return new CloseManyPreflightResult
            {
                Success = false,
                TargetCount = 0,
                SkippedCount = skippedCount,
                FailedCallerIndex = failedCallerIndex,
                FailedHandle = failedHandle,
                FailureReason = reason,
            };
        }

        private static bool ContainsTypeId(int[] typeIds, int count, int typeId)
        {
            for (int i = 0; i < count; i++)
            {
                if (typeIds[i] == typeId)
                {
                    return true;
                }
            }

            return false;
        }

        private async UniTask<BatchCloseOneResult> CloseOneForBatchAsync(
            UIMetadata meta,
            RuntimeTypeHandle expectedHandle,
            UICloseManyMode closeMode,
            bool force)
        {
            if (meta == null)
            {
                return new BatchCloseOneResult(UICloseFailureReason.FinalizeFailed, default);
            }

            if (!RuntimeTypeHandleComparer.Instance.Equals(meta.MetaInfo.RuntimeTypeHandle, expectedHandle) || !IsMetaInOpenStack(meta))
            {
                return new BatchCloseOneResult(UICloseFailureReason.CanceledByNewOperation, default);
            }

            bool interruptedShow = meta.ShowInProgress;
            if (!meta.BeginCloseOperation(out int operationVersion))
            {
                return new BatchCloseOneResult(UICloseFailureReason.BeginCloseFailed, default);
            }

            try
            {
                UIState state = meta.State;
                UIFinalizeClosedMode finalizeMode = GetFinalizeModeForExplicitClose(meta, interruptedShow);

                if (!IsMetaInOpenStack(meta) || meta.OperationVersion != operationVersion)
                {
                    return new BatchCloseOneResult(UICloseFailureReason.CanceledByNewOperation, default);
                }

                if (state == UIState.CreatedUI
                    || state == UIState.Loaded
                    || state == UIState.Initialized
                    || state == UIState.Closed)
                {
                    if (meta.View != null)
                    {
                        meta.View.Visible = false;
                    }

                    UIFinalizeClosedResult finalizeResult = await FinalizeClosedWindowAsync(meta, force, finalizeMode, refreshVisual: false);
                    return new BatchCloseOneResult(finalizeResult.Success ? UICloseFailureReason.None : finalizeResult.FailureReason, finalizeResult);
                }

                if (state == UIState.Opened || state == UIState.Opening)
                {
                    bool closeResult = await meta.View.InternalClose(skipTransition: closeMode == UICloseManyMode.SilentFinalize);
                    if (!closeResult || meta.State != UIState.Closed || meta.OperationVersion != operationVersion || !IsMetaInOpenStack(meta))
                    {
                        return new BatchCloseOneResult(
                            state == UIState.Opening
                                ? UICloseFailureReason.OpenInterruptionFailed
                                : UICloseFailureReason.LifecycleCloseFailed,
                            default);
                    }

                    UIFinalizeClosedResult finalizeResult = await FinalizeClosedWindowAsync(meta, force, finalizeMode, refreshVisual: false);
                    return new BatchCloseOneResult(finalizeResult.Success ? UICloseFailureReason.None : finalizeResult.FailureReason, finalizeResult);
                }

                if (state == UIState.Closing)
                {
                    return new BatchCloseOneResult(UICloseFailureReason.AlreadyClosing, default);
                }

                return new BatchCloseOneResult(UICloseFailureReason.FinalizeFailed, default);
            }
            finally
            {
                meta.EndCloseOperation(operationVersion);
            }
        }

        private UICloseFailureReason RefreshChangedLayersAfterCloseMany(LayerCloseWork[] layerWorks)
        {
            for (int layerIndex = 0; layerIndex < (int)UILayer.All; layerIndex++)
            {
                LayerCloseWork work = layerWorks[layerIndex];
                if (!work.Changed)
                {
                    continue;
                }

                try
                {
                    SortWindowDepth(layerIndex, work.MinRemovedIndex < 0 ? 0 : work.MinRemovedIndex);
                }
                catch
                {
                    MarkLayerVisualDirty(layerIndex);
                    return UICloseFailureReason.VisibilityRefreshFailed;
                }
            }

            return UICloseFailureReason.None;
        }

        private void CleanupCloseManyPendingFlags(CloseManyTarget[] targets, int targetCount)
        {
            // CloseMany 事务结束即清除 pending；层 dirty 由 EndLayerMutation 自动恢复。
            for (int i = 0; i < targetCount; i++)
            {
                UIMetadata meta = targets[i].Meta;
                if (meta != null)
                {
                    meta.StackRemovalPending = false;
                }
            }
        }

        private void EndBegunLayerMutations(int begunLayerMask)
        {
            for (int layerIndex = (int)UILayer.All - 1; layerIndex >= 0; layerIndex--)
            {
                if ((begunLayerMask & (1 << layerIndex)) != 0)
                {
                    EndLayerMutation(layerIndex);
                }
            }
        }
    }
}
