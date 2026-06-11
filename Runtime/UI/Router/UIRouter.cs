using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Text;
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
#endif
        private bool _navigating;
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

        public UniTask<bool> NavigateTo<T>(params object[] args) where T : UIBase
        {
            return NavigateTo<T>(UIRouteOptionsPreset.Page, args);
        }

        public async UniTask<bool> NavigateTo<T>(UIRouteOptions options, params object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return false;
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                if (!TryCreateEntry(handle, args, false, out UIRouteEntry entry))
                {
                    return false;
                }

                if (options.SuppressDuplicate && IsSameRoute(GetCurrentInternal(), entry))
                {
                    UIBase refreshed = await ShowByRouter(handle, entry.Args);
                    if (refreshed == null)
                    {
                        return false;
                    }

                    UIRouteEntry current = GetCurrentInternal();
                    current.Args = UIRouteEntry.CopyArgs(entry.Args);
                    return true;
                }

                UIRouteEntry oldCurrent = GetCurrentInternal();
                UIRouteEntry existingTarget = FindLastHistoryEntry(handle);
                UIBase opened = await ShowByRouter(handle, entry.Args);
                if (opened == null)
                {
                    return false;
                }

                if (options.CloseCurrent && oldCurrent != null && !RuntimeTypeHandleComparer.Instance.Equals(oldCurrent.TypeHandle, handle))
                {
                    bool closeResult = await _uiService.CloseUIAsync(oldCurrent.TypeHandle);
                    if (!closeResult)
                    {
                        await RollbackNavigateOpen(entry, existingTarget, oldCurrent);
                        return false;
                    }
                }

                if (options.AddToHistory)
                {
                    _history.Add(entry);
                    TrimHistory();
                }

                return true;
            }
            finally
            {
                ExitNavigation();
            }
        }

        public async UniTask<bool> Replace<T>(params object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                if (_dirty)
                {
                    return false;
                }

                RuntimeTypeHandle handle = typeof(T).TypeHandle;
                if (!TryCreateEntry(handle, args, _history.Count == 0, out UIRouteEntry entry))
                {
                    return false;
                }

                UIRouteEntry oldCurrent = GetCurrentInternal();
                UIRouteEntry existingTarget = FindLastHistoryEntry(handle);
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
                        await RollbackNavigateOpen(entry, existingTarget, oldCurrent);
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

                    return await ResetToLocked(handle, args);
                }

                return await BackToIndexLocked(targetIndex, "BackTo");
            }
            finally
            {
                ExitNavigation();
            }
        }

        public async UniTask<bool> ResetTo<T>(params object[] args) where T : UIBase
        {
            await EnterNavigation();
            try
            {
                return await ResetToLocked(typeof(T).TypeHandle, args);
            }
            finally
            {
                ExitNavigation();
            }
        }

        private async UniTask EnterNavigation()
        {
            while (_navigating)
            {
                await UniTask.Yield();
            }

            _navigating = true;
        }

        private void ExitNavigation()
        {
            _navigating = false;
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

        private async UniTask<bool> ResetToLocked(RuntimeTypeHandle handle, object[] args)
        {
            if (!TryCreateEntry(handle, args, true, out UIRouteEntry entry))
            {
                return false;
            }

            List<UIRouteEntry> oldHistory = new List<UIRouteEntry>(_history);
            bool targetExisted = ContainsHistory(oldHistory, handle);
            UIBase opened = await ShowByRouter(handle, entry.Args);
            if (opened == null)
            {
                return false;
            }

            for (int i = oldHistory.Count - 1; i >= 0; i--)
            {
                UIRouteEntry oldEntry = oldHistory[i];
                if (RuntimeTypeHandleComparer.Instance.Equals(oldEntry.TypeHandle, handle))
                {
                    continue;
                }

                bool closeResult = await _uiService.CloseUIAsync(oldEntry.TypeHandle);
                if (!closeResult)
                {
                    if (targetExisted)
                    {
                        MarkDirty(handle, "ResetTo failed after old page close failed while target page already existed in history.");
                    }
                    else
                    {
                        bool rollbackResult = await RollbackOpenedEntry(entry);
                        if (!rollbackResult)
                        {
                            MarkDirty(handle, "ResetTo rollback failed after old page close failed.");
                        }
                    }

                    return false;
                }
            }

            _history.Clear();
            _history.Add(entry);
            _dirty = false;
            return true;
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

        private UIRouteEntry FindLastHistoryEntry(RuntimeTypeHandle handle)
        {
            int index = FindLastHistoryIndex(handle);
            return index < 0 ? null : _history[index];
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

        private static bool ContainsHistory(List<UIRouteEntry> entries, RuntimeTypeHandle handle)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (RuntimeTypeHandleComparer.Instance.Equals(entries[i].TypeHandle, handle))
                {
                    return true;
                }
            }

            return false;
        }

        private async UniTask<bool> BackToIndexLocked(int targetIndex, string operationName)
        {
            UIRouteEntry target = _history[targetIndex];
            List<UIRouteEntry> closedEntries = new List<UIRouteEntry>();
            for (int i = _history.Count - 1; i > targetIndex; i--)
            {
                UIRouteEntry entry = _history[i];
                bool closeResult = await _uiService.CloseUIAsync(entry.TypeHandle);
                if (!closeResult)
                {
                    bool rollbackResult = await RestoreClosedEntries(closedEntries);
                    if (!rollbackResult)
                    {
                        MarkDirty(entry.TypeHandle, operationName + " rollback failed after close failed.");
                    }

                    return false;
                }

                closedEntries.Add(entry);
            }

            UIBase opened = await ShowByRouter(target.TypeHandle, target.Args);
            if (opened == null)
            {
                bool rollbackResult = await RestoreClosedEntries(closedEntries);
                if (!rollbackResult)
                {
                    MarkDirty(target.TypeHandle, operationName + " rollback failed after target page restore failed.");
                }

                return false;
            }

            _history.RemoveRange(targetIndex + 1, _history.Count - targetIndex - 1);
            return true;
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

        private async UniTask<UIBase> ShowByRouter(RuntimeTypeHandle handle, object[] args)
        {
            return await _uiService.ShowUI(handle, args);
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

        private async UniTask<bool> RollbackNavigateOpen(UIRouteEntry openedEntry, UIRouteEntry existingTarget, UIRouteEntry oldCurrent)
        {
            if (existingTarget == null)
            {
                return await RollbackOpenedEntry(openedEntry);
            }

            UIBase targetRestored = await ShowByRouter(existingTarget.TypeHandle, existingTarget.Args);
            if (targetRestored == null)
            {
                MarkDirty(openedEntry.TypeHandle, "Rollback failed to restore existing target route.");
                return false;
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

            return true;
        }

        private async UniTask<bool> RestoreClosedEntries(List<UIRouteEntry> closedEntries)
        {
            for (int i = closedEntries.Count - 1; i >= 0; i--)
            {
                UIRouteEntry entry = closedEntries[i];
                UIBase restored = await ShowByRouter(entry.TypeHandle, entry.Args);
                if (restored == null)
                {
                    return false;
                }
            }

            return true;
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
