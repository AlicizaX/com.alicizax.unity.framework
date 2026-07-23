using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Text;
using UnityEngine;
#endif
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public sealed class UIRouter : IUIRouter, IUIRouterInternal
#if UNITY_EDITOR
        , IUIRouterDebug
#endif
    {
        private const int MaxHistoryCount = 64;
#if UNITY_EDITOR
        private const int MaxWarningCount = 64;
#endif

        private readonly UIService _uiService;
        private readonly List<UIRouteEntry> _history = new();
        private readonly RuntimeTypeHandle[] _closeManyHandles = new RuntimeTypeHandle[MaxHistoryCount];
        private readonly UICloseManyMode[] _closeManyModes = new UICloseManyMode[MaxHistoryCount];
#if UNITY_EDITOR
        private readonly List<UIRouteWarningInfo> _warnings = new();
#endif

        private int _sequence;
#if UNITY_EDITOR
        private int _warningSequence;
        private const float NavigationWaitWarningSeconds = 1f;
#endif
        private bool _navigating;
        private UniTaskCompletionSource _navigationWaiter;
        private bool _dirty;

        internal UIRouter(UIService uiService)
        {
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
        }

        public bool CanBack => !_dirty && _history.Count > 1;

        public Type Current => _history.Count == 0 ? null : _history[_history.Count - 1].UIType;

        public UIRouteEntry CurrentEntry => _history.Count == 0 ? null : _history[_history.Count - 1].Clone();

#if UNITY_EDITOR
        int IUIRouterDebug.HistoryCount => _history.Count;
        int IUIRouterDebug.WarningCount => _warnings.Count;
        Type IUIRouterDebug.Current => Current;
        bool IUIRouterDebug.CanBack => CanBack;
        bool IUIRouterDebug.IsDirty => _dirty;
#endif

        public UniTask<UIRouteResult> NavigateTo<T>() where T : UIBase
        {
            return NavigateToInternal<T>(Array.Empty<object>());
        }

        public UniTask<UIRouteResult> NavigateTo<T>(params object[] args) where T : UIBase
        {
            return NavigateToInternal<T>(args);
        }

        private async UniTask<UIRouteResult> NavigateToInternal<T>(object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return UIRouteResult.From(UIRouteStatus.RejectedDirty);
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                bool isRoot = _history.Count == 0;
                if (!TryCreateEntry<T>(args, isRoot, out UIRouteEntry entry))
                {
                    return UIRouteResult.From(UIRouteStatus.InvalidTarget);
                }

                if (IsSamePageType(GetCurrentInternal(), entry))
                {
                    UIBase refreshed = await ShowByRouter(handle, entry.Args);
                    if (refreshed == null)
                    {
                        return UIRouteResult.From(UIRouteStatus.OpenFailed);
                    }

                    UIRouteEntry current = GetCurrentInternal();
                    current.Args = entry.Args;
                    return UIRouteResult.Ok;
                }

                if (_history.Count >= MaxHistoryCount)
                {
                    return UIRouteResult.From(UIRouteStatus.RejectedLimit);
                }

                UIRouteEntry oldCurrent = GetCurrentInternal();
                UIBase opened = await ShowByRouter(handle, entry.Args);
                if (opened == null)
                {
                    return UIRouteResult.From(UIRouteStatus.OpenFailed);
                }

                if (oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
                {
                    UIRouteResult closeResult = await CloseByRouter(oldCurrent.TypeHandle);
                    if (!closeResult.Success)
                    {
                        await RollbackNavigateOpen(entry, oldCurrent);
                        return closeResult.Status == UIRouteStatus.RejectedBusy
                            ? closeResult
                            : UIRouteResult.From(UIRouteStatus.CloseFailed);
                    }
                }

                _history.Add(entry);
                return UIRouteResult.Ok;
            }
            finally
            {
                ExitNavigation();
            }
        }

        public UniTask<UIRouteResult> Replace<T>() where T : UIBase
        {
            return ReplaceInternal<T>(Array.Empty<object>());
        }

        public UniTask<UIRouteResult> Replace<T>(params object[] args) where T : UIBase
        {
            return ReplaceInternal<T>(args);
        }

        private async UniTask<UIRouteResult> ReplaceInternal<T>(object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return UIRouteResult.From(UIRouteStatus.RejectedDirty);
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                UIRouteEntry oldCurrent = GetCurrentInternal();
                bool isRoot = oldCurrent?.IsRoot ?? true;
                if (!TryCreateEntry<T>(args, isRoot, out UIRouteEntry entry))
                {
                    return UIRouteResult.From(UIRouteStatus.InvalidTarget);
                }

                UIBase opened = await ShowByRouter(handle, entry.Args);
                if (opened == null)
                {
                    return UIRouteResult.From(UIRouteStatus.OpenFailed);
                }

                if (oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
                {
                    UIRouteResult closeResult = await CloseByRouter(oldCurrent.TypeHandle);
                    if (!closeResult.Success)
                    {
                        await RollbackNavigateOpen(entry, oldCurrent);
                        return closeResult.Status == UIRouteStatus.RejectedBusy
                            ? closeResult
                            : UIRouteResult.From(UIRouteStatus.CloseFailed);
                    }
                }

                if (_history.Count == 0)
                {
                    _history.Add(entry);
                }
                else
                {
                    _history[_history.Count - 1] = entry;
                }

                return UIRouteResult.Ok;
            }
            finally
            {
                ExitNavigation();
            }
        }

        public async UniTask<UIRouteResult> Back()
        {
            await EnterNavigation();
            try
            {
                return await BackLocked();
            }
            finally
            {
                ExitNavigation();
            }
        }

        public UniTask<UIRouteResult> CloseCurrent()
        {
            return CloseCurrent(false);
        }

        public async UniTask<UIRouteResult> CloseCurrent(bool force)
        {
            await EnterNavigation();
            try
            {
                return await CloseCurrentLocked(force);
            }
            finally
            {
                ExitNavigation();
            }
        }

        bool IUIRouterInternal.IsCurrent(RuntimeTypeHandle handle)
        {
            UIRouteEntry current = GetCurrentInternal();
            return current != null && RuntimeTypeHandleComparer.Instance.Equals(current.TypeHandle, handle);
        }

        async UniTask<UIRouteResult> IUIRouterInternal.CloseCurrent(RuntimeTypeHandle expectedHandle, bool force)
        {
            await EnterNavigation();
            try
            {
                UIRouteEntry current = GetCurrentInternal();
                if (current == null || !RuntimeTypeHandleComparer.Instance.Equals(current.TypeHandle, expectedHandle))
                {
                    return UIRouteResult.From(UIRouteStatus.NotFound);
                }

                return await CloseCurrentLocked(force);
            }
            finally
            {
                ExitNavigation();
            }
        }

        public async UniTask<UIRouteResult> BackToRoot()
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return UIRouteResult.From(UIRouteStatus.RejectedDirty);
                }

                if (_history.Count == 0)
                {
                    return UIRouteResult.From(UIRouteStatus.NotFound);
                }

                int targetIndex = FindLastRootIndex();
                if (targetIndex < 0)
                {
#if UNITY_EDITOR
                    AddWarning(Current?.TypeHandle ?? default, "BackToRoot found no root entry; fallback to history index 0.");
#endif
                    targetIndex = 0;
                }

                if (targetIndex == _history.Count - 1)
                {
                    return UIRouteResult.Ok;
                }

                return await BackToIndexLocked(targetIndex, "BackToRoot");
            }
            finally
            {
                ExitNavigation();
            }
        }

        public UniTask<UIRouteResult> BackTo<T>() where T : UIBase
        {
            return BackTo<T>(true, Array.Empty<object>());
        }

        public async UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return UIRouteResult.From(UIRouteStatus.RejectedDirty);
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                int targetIndex = FindLastHistoryIndex(handle);
                if (targetIndex < 0)
                {
                    if (!openIfMissing)
                    {
                        return UIRouteResult.From(UIRouteStatus.NotFound);
                    }

                    return await ResetToLocked<T>(args);
                }

                return await BackToIndexLocked(targetIndex, "BackTo");
            }
            finally
            {
                ExitNavigation();
            }
        }

        public UniTask<UIRouteResult> ResetTo<T>() where T : UIBase
        {
            return ResetToInternal<T>(Array.Empty<object>());
        }

        public UniTask<UIRouteResult> ResetTo<T>(params object[] args) where T : UIBase
        {
            return ResetToInternal<T>(args);
        }

        private async UniTask<UIRouteResult> ResetToInternal<T>(object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                return await ResetToLocked<T>(args);
            }
            finally
            {
                ExitNavigation();
            }
        }

        private async UniTask EnterNavigation()
        {
#if UNITY_EDITOR
            float waitStartTime = Time.realtimeSinceStartup;
            bool waitWarningLogged = false;
#endif
            while (_navigating)
            {
#if UNITY_EDITOR
                if (!waitWarningLogged && Time.realtimeSinceStartup - waitStartTime >= NavigationWaitWarningSeconds)
                {
                    waitWarningLogged = true;
                    AddWarning(Current?.TypeHandle ?? default, "Navigation waited more than 1 second for the current navigation operation to finish.");
                }
#endif
                _navigationWaiter ??= new UniTaskCompletionSource();
                await _navigationWaiter.Task;
            }

            _navigating = true;
        }

        private void ExitNavigation()
        {
            _navigating = false;
            UniTaskCompletionSource waiter = _navigationWaiter;
            _navigationWaiter = null;
            waiter?.TrySetResult();
        }

        public void ResetHistory()
        {
            if (RejectHistoryMutationDuringNavigation("ResetHistory", Current?.TypeHandle ?? default))
            {
                return;
            }

            ClearHistoryCore();
        }


        internal void ForceResetHistoryForDestroy()
        {
            _navigating = false;
            _navigationWaiter?.TrySetResult();
            _navigationWaiter = null;
            ClearHistoryCore();
        }

        private void ClearHistoryCore()
        {
            _history.Clear();
            _dirty = false;
#if UNITY_EDITOR
            _warnings.Clear();
#endif
        }

        public void SyncFromCurrentUI(Type currentPageType, params object[] args)
        {
            if (currentPageType == null)
            {
                return;
            }

            SyncFromCurrentUI(currentPageType.TypeHandle, args);
        }

        public void SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args)
        {
            if (RejectHistoryMutationDuringNavigation("SyncFromCurrentUI", handle))
            {
                return;
            }

            if (!_uiService.IsOpen(handle))
            {
#if UNITY_EDITOR
                AddWarning(handle, "SyncFromCurrentUI rejected because the target page is not Opened.");
#endif
                return;
            }

            if (!TryCreateEntry(handle, args, true, out UIRouteEntry entry))
            {
                return;
            }

            _history.Clear();
            _history.Add(entry);
            _dirty = false;
        }

#if UNITY_EDITOR
        bool IUIRouterDebug.FillHistoryInfo(int index, UIRouteDebugInfo info)
        {
            if (info == null || (uint)index >= (uint)_history.Count)
            {
                return false;
            }

            FillEntryInfo(index, _history[index], info);
            return true;
        }

        bool IUIRouterDebug.FillWarningInfo(int index, UIRouteWarningInfo info)
        {
            if (info == null || (uint)index >= (uint)_warnings.Count)
            {
                return false;
            }

            UIRouteWarningInfo warning = _warnings[index];
            info.Clear();
            info.Index = index;
            info.Sequence = warning.Sequence;
            info.UITypeName = warning.UITypeName;
            info.Message = warning.Message;
            return true;
        }

        void IUIRouterDebug.ClearWarnings()
        {
            _warnings.Clear();
        }
#endif

        private async UniTask<UIRouteResult> ResetToLocked<T>(object[] args) where T : UIBase
        {
            if (_dirty)
            {
                return UIRouteResult.From(UIRouteStatus.RejectedDirty);
            }

            RuntimeTypeHandle handle = typeof(T).TypeHandle;
            if (!TryCreateEntry<T>(args, true, out UIRouteEntry entry))
            {
                return UIRouteResult.From(UIRouteStatus.InvalidTarget);
            }

            UIRouteEntry oldCurrent = GetCurrentInternal();
            UIBase opened = await ShowByRouter(handle, entry.Args);
            if (opened == null)
            {
                return UIRouteResult.From(UIRouteStatus.OpenFailed);
            }

            if (oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
            {
                UIRouteResult closeResult = await CloseByRouter(oldCurrent.TypeHandle);
                if (!closeResult.Success)
                {
                    await RollbackNavigateOpen(entry, oldCurrent);
                    return closeResult.Status == UIRouteStatus.RejectedBusy
                        ? closeResult
                        : UIRouteResult.From(UIRouteStatus.CloseFailed);
                }
            }

            _history.Clear();
            _history.Add(entry);
            _dirty = false;
            return UIRouteResult.Ok;
        }

        private bool TryCreateEntry<T>(object[] args, bool isRoot, out UIRouteEntry entry) where T : UIBase
        {
            Type uiType = typeof(T);
            return CreateEntry(uiType, uiType.TypeHandle, args, isRoot, out entry);
        }

        private bool TryCreateEntry(RuntimeTypeHandle handle, object[] args, bool isRoot, out UIRouteEntry entry)
        {
            entry = null;
            Type uiType = Type.GetTypeFromHandle(handle);
            if (uiType == null || !typeof(UIBase).IsAssignableFrom(uiType))
            {
#if UNITY_EDITOR
                AddWarning(handle, "Navigation rejected because the type is not a UIBase page.");
#endif
                return false;
            }

            return CreateEntry(uiType, handle, args, isRoot, out entry);
        }

        private bool CreateEntry(Type uiType, RuntimeTypeHandle handle, object[] args, bool isRoot, out UIRouteEntry entry)
        {
            object[] argsCopy = UIRouteEntry.CopyArgs(args);
            entry = new UIRouteEntry
            {
                UIType = uiType,
                TypeHandle = handle,
                Args = argsCopy,
                IsRoot = isRoot,
                Sequence = ++_sequence,
            };
            return true;
        }

        private static bool IsSamePageType(UIRouteEntry left, UIRouteEntry right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return RuntimeTypeHandleComparer.Instance.Equals(left.TypeHandle, right.TypeHandle);
        }

        private UIRouteEntry GetCurrentInternal()
        {
            return _history.Count == 0 ? null : _history[_history.Count - 1];
        }

        private int FindLastHistoryIndex(RuntimeTypeHandle handle)
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (RuntimeTypeHandleComparer.Instance.Equals(_history[i].TypeHandle, handle))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindLastRootIndex()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].IsRoot)
                {
                    return i;
                }
            }

            return -1;
        }

        private async UniTask<UIRouteResult> BackLocked(bool force = false)
        {
            if (_dirty)
            {
                return UIRouteResult.From(UIRouteStatus.RejectedDirty);
            }

            if (_history.Count <= 1)
            {
                return UIRouteResult.From(UIRouteStatus.NotFound);
            }

            UIRouteEntry current = _history[_history.Count - 1];
            UIRouteEntry target = _history[_history.Count - 2];
            UIRouteResult closeResult = await CloseByRouter(current.TypeHandle, force);
            if (!closeResult.Success)
            {
                return closeResult.Status == UIRouteStatus.RejectedBusy
                    ? closeResult
                    : UIRouteResult.From(UIRouteStatus.CloseFailed);
            }

            if (IsAdjacentRouteOpenAfterCurrentClose(current, target, true))
            {
                _history.RemoveAt(_history.Count - 1);
                return UIRouteResult.Ok;
            }

            UIBase opened = await ShowByRouter(target.TypeHandle, target.Args);
            if (opened == null)
            {
                bool rollbackResult = await ShowByRouter(current.TypeHandle, current.Args) != null;
                if (!rollbackResult)
                {
                    MarkDirty(current.TypeHandle, "Back rollback failed after target page restore failed.");
                }

                return UIRouteResult.From(UIRouteStatus.OpenFailed);
            }

            _history.RemoveAt(_history.Count - 1);
            return UIRouteResult.Ok;
        }

        private async UniTask<UIRouteResult> CloseCurrentLocked(bool force)
        {
            if (_dirty)
            {
                return UIRouteResult.From(UIRouteStatus.RejectedDirty);
            }

            if (_history.Count == 0)
            {
                return UIRouteResult.From(UIRouteStatus.NotFound);
            }

            if (_history.Count > 1)
            {
                return await BackLocked(force);
            }

            UIRouteEntry current = GetCurrentInternal();
            UIRouteResult closeResult = await CloseByRouter(current.TypeHandle, force);
            if (!closeResult.Success)
            {
                return closeResult.Status == UIRouteStatus.RejectedBusy
                    ? closeResult
                    : UIRouteResult.From(UIRouteStatus.CloseFailed);
            }

            _history.RemoveAt(_history.Count - 1);
            return UIRouteResult.Ok;
        }

        private async UniTask<UIRouteResult> BackToIndexLocked(int targetIndex, string operationName)
        {
            UIRouteEntry target = _history[targetIndex];
            UIRouteEntry current = GetCurrentInternal();
            int closeCount = BuildDeepBackCloseHandles(targetIndex, target.TypeHandle, current?.TypeHandle ?? default, _closeManyHandles, _closeManyModes);
            bool batchClosedRoutes = false;
            if (closeCount > 0)
            {
                UICloseManyResult closeResult;
                try
                {
                    closeResult = await _uiService.CloseManyAsync(_closeManyHandles, _closeManyModes, closeCount);
                }
                finally
                {
                    ClearCloseManyBuffer(closeCount);
                }

                if (!closeResult.Success)
                {
                    if (IsTransientCloseManyBusyFailure(closeResult))
                    {
                        return UIRouteResult.From(UIRouteStatus.RejectedBusy);
                    }

                    RuntimeTypeHandle dirtyHandle = closeResult.FailedHandle.Value == IntPtr.Zero
                        ? target.TypeHandle
                        : closeResult.FailedHandle;
                    MarkDirty(dirtyHandle, operationName + " batch close failed: " + closeResult.FailureReason);
                    return UIRouteResult.From(UIRouteStatus.CloseFailed);
                }

                batchClosedRoutes = true;
            }

            bool targetWasDirectlyBelowCurrent = targetIndex == _history.Count - 2;
            if (IsAdjacentRouteOpenAfterCurrentClose(current, target, targetWasDirectlyBelowCurrent)
                || IsTargetOpenAfterBatchClose(target, batchClosedRoutes))
            {
                _history.RemoveRange(targetIndex + 1, _history.Count - targetIndex - 1);
                return UIRouteResult.Ok;
            }

            UIBase opened = await ShowByRouter(target.TypeHandle, target.Args);
            if (opened == null)
            {
                if (current != null && !RuntimeTypeHandleComparer.Instance.Equals(current.TypeHandle, target.TypeHandle))
                {
                    bool rollbackResult = await ShowByRouter(current.TypeHandle, current.Args) != null;
                    if (!rollbackResult)
                    {
                        MarkDirty(target.TypeHandle, operationName + " rollback failed after target page restore failed.");
                    }
                }

                return UIRouteResult.From(UIRouteStatus.OpenFailed);
            }

            _history.RemoveRange(targetIndex + 1, _history.Count - targetIndex - 1);
            return UIRouteResult.Ok;
        }

        private bool IsTargetOpenAfterBatchClose(UIRouteEntry target, bool batchClosedRoutes)
        {
            return batchClosedRoutes
                   && target != null
                   && _uiService.IsOpen(target.TypeHandle);
        }

        private static bool IsTransientCloseManyBusyFailure(UICloseManyResult result)
        {
            return result.ClosedCount == 0
                   && result.FailureReason == UICloseFailureReason.LayerTransactionBusy;
        }

        private int BuildDeepBackCloseHandles(
            int targetIndex,
            RuntimeTypeHandle targetHandle,
            RuntimeTypeHandle currentHandle,
            RuntimeTypeHandle[] handles,
            UICloseManyMode[] modes)
        {
            int count = 0;
            for (int i = _history.Count - 1; i > targetIndex; i--)
            {
                RuntimeTypeHandle handle = _history[i].TypeHandle;
                if (RuntimeTypeHandleComparer.Instance.Equals(handle, targetHandle))
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    if (RuntimeTypeHandleComparer.Instance.Equals(handles[j], handle))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    handles[count] = handle;
                    modes[count] = RuntimeTypeHandleComparer.Instance.Equals(handle, currentHandle)
                        ? UICloseManyMode.Transition
                        : UICloseManyMode.SilentFinalize;
                    count++;
                }
            }

            return count;
        }

        private void ClearCloseManyBuffer(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _closeManyHandles[i] = default;
                _closeManyModes[i] = default;
            }
        }

        private bool IsAdjacentRouteOpenAfterCurrentClose(UIRouteEntry current, UIRouteEntry target, bool targetWasDirectlyBelowCurrent)
        {
            return targetWasDirectlyBelowCurrent
                   && current != null
                   && target != null
                   && !RuntimeTypeHandleComparer.Instance.Equals(current.TypeHandle, target.TypeHandle)
                   && _uiService.IsOpen(target.TypeHandle);
        }

        private UniTask<UIBase> ShowByRouter(RuntimeTypeHandle handle, object[] args)
        {
            return _uiService.ShowUI(handle, args);
        }

        private async UniTask<UIRouteResult> CloseByRouter(RuntimeTypeHandle handle, bool force = false)
        {
            bool closed = await _uiService.CloseUIFromRouterAsync(handle, force);
            if (closed)
            {
                return UIRouteResult.Ok;
            }

            return _uiService.IsLayerCloseBlocked(handle)
                ? UIRouteResult.From(UIRouteStatus.RejectedBusy)
                : UIRouteResult.From(UIRouteStatus.CloseFailed);
        }

        private async UniTask<bool> RollbackOpenedEntry(UIRouteEntry entry)
        {
            bool rollbackResult = (await CloseByRouter(entry.TypeHandle)).Success;
            if (!rollbackResult)
            {
                MarkDirty(entry.TypeHandle, "Rollback failed after navigation transaction failed.");
            }

            return rollbackResult;
        }

        private async UniTask<bool> RollbackNavigateOpen(UIRouteEntry openedEntry, UIRouteEntry oldCurrent)
        {
            bool rollbackSucceeded = true;
            if (oldCurrent == null || !RuntimeTypeHandleComparer.Instance.Equals(openedEntry.TypeHandle, oldCurrent.TypeHandle))
            {
                rollbackSucceeded = await RollbackOpenedEntry(openedEntry);
            }

            if (oldCurrent != null)
            {
                UIBase currentRestored = await ShowByRouter(oldCurrent.TypeHandle, oldCurrent.Args);
                if (currentRestored == null)
                {
                    MarkDirty(oldCurrent.TypeHandle, "Rollback failed to restore previous current route.");
                    return false;
                }
            }

            return rollbackSucceeded;
        }

        private void MarkDirty(RuntimeTypeHandle handle, string message)
        {
            _dirty = true;
#if UNITY_EDITOR
            AddWarning(handle, message);
#endif
        }

        private bool RejectHistoryMutationDuringNavigation(string operationName, RuntimeTypeHandle handle)
        {
            if (!_navigating)
            {
                return false;
            }

#if UNITY_EDITOR
            AddWarning(handle, operationName + " ignored while navigation is in progress.");
#endif
            return true;
        }

#if UNITY_EDITOR
        private void AddWarning(RuntimeTypeHandle handle, string message)
        {
            if (_warnings.Count >= MaxWarningCount)
            {
                _warnings.RemoveAt(0);
            }

            _warnings.Add(new UIRouteWarningInfo
            {
                Sequence = ++_warningSequence,
                UITypeName = GetTypeName(handle),
                Message = message,
            });
        }

        private static string GetTypeName(RuntimeTypeHandle handle)
        {
            if (handle.Value == IntPtr.Zero)
            {
                return null;
            }

            return Type.GetTypeFromHandle(handle)?.Name;
        }

        private static void FillEntryInfo(int index, UIRouteEntry entry, UIRouteDebugInfo info)
        {
            info.Clear();
            info.Index = index;
            info.UITypeName = entry.UIType?.Name;
            info.IsRoot = entry.IsRoot;
            info.Sequence = entry.Sequence;
            info.ArgsPreview = FormatArgs(entry.Args);
        }

        private static string FormatArgs(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(64);
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                object arg = args[i];
                builder.Append(arg == null ? "null" : arg.ToString());
            }

            return builder.ToString();
        }
#endif
    }
}
