using System;

namespace AlicizaX.UI.Runtime
{
    public readonly struct UICloseManyResult
    {
        public readonly bool Success;
        public readonly int ClosedCount;
        public readonly int SkippedCount;
        public readonly int FailedCallerIndex;
        public readonly RuntimeTypeHandle FailedHandle;
        public readonly UICloseFailureReason FailureReason;

        public UICloseManyResult(
            bool success,
            int closedCount,
            int skippedCount,
            int failedCallerIndex,
            RuntimeTypeHandle failedHandle,
            UICloseFailureReason failureReason)
        {
            Success = success;
            ClosedCount = closedCount;
            SkippedCount = skippedCount;
            FailedCallerIndex = failedCallerIndex;
            FailedHandle = failedHandle;
            FailureReason = failureReason;
        }

        public static UICloseManyResult Ok(int closedCount, int skippedCount)
        {
            return new UICloseManyResult(true, closedCount, skippedCount, -1, default, UICloseFailureReason.None);
        }

        public static UICloseManyResult Fail(
            int closedCount,
            int skippedCount,
            int failedCallerIndex,
            RuntimeTypeHandle failedHandle,
            UICloseFailureReason reason)
        {
            return new UICloseManyResult(false, closedCount, skippedCount, failedCallerIndex, failedHandle, reason);
        }
    }

    public enum UICloseFailureReason : byte
    {
        None,
        InvalidArguments,
        InvalidHandle,
        UnknownHandle,
        LayerTransactionBusy,
        LayerVisualDirty,
        BeginCloseFailed,
        AlreadyClosing,
        OpenInterruptionFailed,
        LifecycleCloseFailed,
        FinalizeFailed,
        CanceledByNewOperation,
    }

    public enum UICloseManyMode : byte
    {
        SilentFinalize,
        Transition,
    }

    internal enum UIFinalizeClosedMode : byte
    {
        Cache,
        Dispose,
    }

    internal readonly struct UIFinalizeClosedResult
    {
        public readonly bool Success;
        public readonly int RemovedIndex;
        public readonly UICloseFailureReason FailureReason;

        public UIFinalizeClosedResult(
            bool success,
            int removedIndex,
            UICloseFailureReason failureReason)
        {
            Success = success;
            RemovedIndex = removedIndex;
            FailureReason = failureReason;
        }

        public static UIFinalizeClosedResult Fail(UICloseFailureReason reason)
        {
            return new UIFinalizeClosedResult(false, -1, reason);
        }
    }
}
