using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Text;
using UnityEngine;
#endif
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public sealed class UIRouter : IUIRouter
#if UNITY_EDITOR
        , IUIRouterDebug
#endif
    {
        private const int MaxHistoryCount = 64;
#if UNITY_EDITOR
        private const int MaxWarningCount = 64;
#endif

        private readonly IUIService _uiService;
        private readonly List<UIRouteEntry> _history = new();
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

        public UIRouter(IUIService uiService)
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

        public void OnDestroyService()
        {
            _history.Clear();
            _warnings.Clear();
        }
#endif

        public UniTask<bool> NavigateTo<T>() where T : UIBase
        {
            return NavigateToInternal<T>(Array.Empty<object>());
        }

        public UniTask<bool> NavigateTo<T>(params object[] args) where T : UIBase
        {
            return NavigateToInternal<T>(args);
        }

        private async UniTask<bool> NavigateToInternal<T>(object[] args) where T : UIBase
        {
            bool navigationExited = false;
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return false;
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                bool isRoot = _history.Count == 0;
                if (!TryCreateEntry<T>(args, isRoot, out UIRouteEntry entry))
                {
                    return false;
                }

                if (IsSameRoute(GetCurrentInternal(), entry))
                {
                    UIBase refreshed = await ShowByRouter(handle, entry.Args);
                    if (refreshed == null)
                    {
                        return false;
                    }

                    UIRouteEntry current = GetCurrentInternal();
                    current.Args = entry.Args;
                    return true;
                }

                UIRouteEntry oldCurrent = GetCurrentInternal();
                UniTask<UIBase> openTask = ShowByRouter(handle, entry.Args);
                if (ShouldCompleteNavigateAfterShowStarted(oldCurrent, handle))
                {
                    _history.Add(entry);
                    TrimHistory();
                    ExitNavigation();
                    navigationExited = true;
                    return await CompletePendingRouteShow(entry, openTask);
                }

                UIBase opened = await openTask;
                if (opened == null)
                {
                    return false;
                }

                if (oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
                {
                    bool closeResult = await _uiService.CloseUIAsync(oldCurrent.TypeHandle);
                    if (!closeResult)
                    {
                        await RollbackNavigateOpen(entry, oldCurrent);
                        return false;
                    }
                }

                _history.Add(entry);
                TrimHistory();

                return true;
            }
            finally
            {
                if (!navigationExited)
                {
                    ExitNavigation();
                }
            }
        }

        public UniTask<bool> Replace<T>() where T : UIBase
        {
            return ReplaceInternal<T>(Array.Empty<object>());
        }

        public UniTask<bool> Replace<T>(params object[] args) where T : UIBase
        {
            return ReplaceInternal<T>(args);
        }

        private async UniTask<bool> ReplaceInternal<T>(object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return false;
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                UIRouteEntry oldCurrent = GetCurrentInternal();
                bool isRoot = oldCurrent?.IsRoot ?? true;
                if (!TryCreateEntry<T>(args, isRoot, out UIRouteEntry entry))
                {
                    return false;
                }

                UIBase opened = await ShowByRouter(handle, entry.Args);
                if (opened == null)
                {
                    return false;
                }

                if (oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
                {
                    bool closeResult = await _uiService.CloseUIAsync(oldCurrent.TypeHandle);
                    if (!closeResult)
                    {
                        await RollbackNavigateOpen(entry, oldCurrent);
                        return false;
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

                return true;
            }
            finally
            {
                ExitNavigation();
            }
        }

        public async UniTask<bool> Back()
        {
            await EnterNavigation();
            try
            {
                if (_dirty || _history.Count <= 1)
                {
                    return false;
                }

                UIRouteEntry current = _history[_history.Count - 1];
                UIRouteEntry target = _history[_history.Count - 2];
                bool closeResult = await _uiService.CloseUIAsync(current.TypeHandle);
                if (!closeResult)
                {
                    return false;
                }

                if (IsAdjacentRouteOpenAfterCurrentClose(current, target, true))
                {
                    _history.RemoveAt(_history.Count - 1);
                    return true;
                }

                UIBase opened = await ShowByRouter(target.TypeHandle, target.Args);
                if (opened == null)
                {
                    bool rollbackResult = await ShowByRouter(current.TypeHandle, current.Args) != null;
                    if (!rollbackResult)
                    {
                        MarkDirty(current.TypeHandle, "Back rollback failed after target page restore failed.");
                    }

                    return false;
                }

                _history.RemoveAt(_history.Count - 1);
                return true;
            }
            finally
            {
                ExitNavigation();
            }
        }

        public async UniTask<bool> BackToRoot()
        {
            await EnterNavigation();
            try
            {
                if (_dirty || _history.Count == 0)
                {
                    return false;
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
                    return true;
                }

                return await BackToIndexLocked(targetIndex, "BackToRoot");
            }
            finally
            {
                ExitNavigation();
            }
        }

        public UniTask<bool> BackTo<T>() where T : UIBase
        {
            return BackTo<T>(true, Array.Empty<object>());
        }

        public async UniTask<bool> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return false;
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                int targetIndex = FindLastHistoryIndex(handle);
                if (targetIndex < 0)
                {
                    if (!openIfMissing)
                    {
                        return false;
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

        public UniTask<bool> ResetTo<T>() where T : UIBase
        {
            return ResetToInternal<T>(Array.Empty<object>());
        }

        public UniTask<bool> ResetTo<T>(params object[] args) where T : UIBase
        {
            return ResetToInternal<T>(args);
        }

        private async UniTask<bool> ResetToInternal<T>(object[] args) where T : UIBase
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
            _history.Clear();
            _dirty = false;
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

        private async UniTask<bool> ResetToLocked<T>(object[] args) where T : UIBase
        {
            RuntimeTypeHandle handle = typeof(T).TypeHandle;
            if (!TryCreateEntry<T>(args, true, out UIRouteEntry entry))
            {
                return false;
            }

            UIRouteEntry oldCurrent = GetCurrentInternal();
            UIBase opened = await ShowByRouter(handle, entry.Args);
            if (opened == null)
            {
                return false;
            }

            if (oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
            {
                bool closeResult = await _uiService.CloseUIAsync(oldCurrent.TypeHandle);
                if (!closeResult)
                {
                    await RollbackNavigateOpen(entry, oldCurrent);
                    return false;
                }
            }

            _history.Clear();
            _history.Add(entry);
            _dirty = false;
            return true;
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

        private static bool ShouldCompleteNavigateAfterShowStarted(UIRouteEntry previousRoute, RuntimeTypeHandle targetHandle)
        {
            return previousRoute != null
                   && !RuntimeTypeHandleComparer.Instance.Equals(previousRoute.TypeHandle, targetHandle)
                   && IsLifecycleOcclusionWindow(targetHandle);
        }

        private static bool IsLifecycleOcclusionWindow(RuntimeTypeHandle handle)
        {
            return UIMetaRegistry.TryGet(handle, out UIMetaRegistry.UIMetaInfo metaInfo)
                   && metaInfo.OcclusionMode == UIOcclusionMode.Lifecycle;
        }

        private static bool IsSameRoute(UIRouteEntry left, UIRouteEntry right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (!RuntimeTypeHandleComparer.Instance.Equals(left.TypeHandle, right.TypeHandle))
            {
                return false;
            }

            return true;
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

        private async UniTask<bool> BackToIndexLocked(int targetIndex, string operationName)
        {
            UIRouteEntry target = _history[targetIndex];
            UIRouteEntry current = GetCurrentInternal();
            if (current != null && !RuntimeTypeHandleComparer.Instance.Equals(current.TypeHandle, target.TypeHandle))
            {
                bool closeResult = await _uiService.CloseUIAsync(current.TypeHandle);
                if (!closeResult)
                {
                    return false;
                }
            }

            bool targetWasDirectlyBelowCurrent = targetIndex == _history.Count - 2;
            if (IsAdjacentRouteOpenAfterCurrentClose(current, target, targetWasDirectlyBelowCurrent))
            {
                _history.RemoveRange(targetIndex + 1, _history.Count - targetIndex - 1);
                return true;
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

                return false;
            }

            _history.RemoveRange(targetIndex + 1, _history.Count - targetIndex - 1);
            return true;
        }

        private bool IsAdjacentRouteOpenAfterCurrentClose(UIRouteEntry current, UIRouteEntry target, bool targetWasDirectlyBelowCurrent)
        {
            return targetWasDirectlyBelowCurrent
                   && current != null
                   && target != null
                   && !RuntimeTypeHandleComparer.Instance.Equals(current.TypeHandle, target.TypeHandle)
                   && _uiService.IsOpen(target.TypeHandle);
        }

        private void TrimHistory()
        {
            while (_history.Count > MaxHistoryCount)
            {
                int removeIndex = 0;
                for (int i = 0; i < _history.Count; i++)
                {
                    if (!_history[i].IsRoot)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                _history.RemoveAt(removeIndex);
            }
        }

        private UniTask<UIBase> ShowByRouter(RuntimeTypeHandle handle, object[] args)
        {
            return _uiService.ShowUI(handle, args);
        }

        private async UniTask<bool> CompletePendingRouteShow(UIRouteEntry entry, UniTask<UIBase> openTask)
        {
            UIBase opened;
            try
            {
                opened = await openTask;
            }
            catch
            {
                await RemovePendingRouteIfCurrent(entry);
                throw;
            }

            if (opened != null)
            {
                return true;
            }

            await RemovePendingRouteIfCurrent(entry);
            return false;
        }

        private async UniTask RemovePendingRouteIfCurrent(UIRouteEntry entry)
        {
            await EnterNavigation();
            try
            {
                int lastIndex = _history.Count - 1;
                if (lastIndex >= 0 && ReferenceEquals(_history[lastIndex], entry))
                {
                    _history.RemoveAt(lastIndex);
                }
            }
            finally
            {
                ExitNavigation();
            }
        }

        private async UniTask<bool> RollbackOpenedEntry(UIRouteEntry entry)
        {
            bool rollbackResult = await _uiService.CloseUIAsync(entry.TypeHandle);
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
