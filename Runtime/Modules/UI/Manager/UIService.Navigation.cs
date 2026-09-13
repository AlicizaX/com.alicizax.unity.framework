using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
#if UNITY_EDITOR
using System.Text;
#endif

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
#if UNITY_EDITOR
        : IUINavigationDebug
#endif
    {
        private const int MaxHistoryCount = 64;
        private readonly List<UIRouteEntry> _history = new(MaxHistoryCount);
        private readonly List<UIBase> _navigationCleanup = new(MaxHistoryCount);
        private readonly Queue<NavigationCommand> _navigationQueue = new();
        private UniTaskCompletionSource<UIRouteResult> _activeNavigation;
        private UIBase _currentView;
        private int _sequence;
        private int _historyVersion;
        private bool _navigating;
#if UNITY_EDITOR
        private readonly List<UIRouteWarningInfo> _warnings = new();
        private int _warningSequence;
#endif
        private enum Navigation { Push, Replace, Reset, Back, BackToRoot, BackTo, Close, Clear, Sync }

        private readonly struct NavigationCommand
        {
            internal readonly Navigation Operation;
            internal readonly UIRouteEntry Target;
            internal readonly bool Option;
            internal readonly UniTaskCompletionSource<UIRouteResult> Completion;
            internal NavigationCommand(Navigation operation, UIRouteEntry target, bool option)
            {
                Operation = operation;
                Target = target;
                Option = option;
                Completion = new UniTaskCompletionSource<UIRouteResult>();
            }
        }

        public bool CanBack => _history.Count > 1;
        public Type Current => _currentView?.GetType();
        public UIRouteEntry CurrentEntry => _history.Count == 0 ? null : _history[_history.Count - 1].Clone();

        public UniTask<UIRouteResult> NavigateTo<T>(params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.Push, CreateEntry(typeof(T), args));
        public UniTask<UIRouteResult> Replace<T>(params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.Replace, CreateEntry(typeof(T), args));
        public UniTask<UIRouteResult> ResetTo<T>(params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.Reset, CreateEntry(typeof(T), args));
        public UniTask<UIRouteResult> Back() => EnqueueNavigation(Navigation.Back);
        public UniTask<UIRouteResult> BackToRoot() => EnqueueNavigation(Navigation.BackToRoot);
        public UniTask<UIRouteResult> CloseCurrent(bool force = false) => EnqueueNavigation(Navigation.Close, option: force);
        public UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.BackTo, CreateEntry(typeof(T), args), openIfMissing);
        public UniTask<UIRouteResult> ResetHistory() => EnqueueNavigation(Navigation.Clear);
        public UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, params object[] args) =>
            EnqueueNavigation(Navigation.Sync, currentPageType == null ? null : CreateEntry(currentPageType, args));
        public UniTask<UIRouteResult> SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args) =>
            SyncFromCurrentUI(handle.Value == IntPtr.Zero ? null : Type.GetTypeFromHandle(handle), args);

        private UniTask<UIRouteResult> EnqueueNavigation(Navigation operation, UIRouteEntry target = null, bool option = false)
        {
            if (_shuttingDown || !_initialized) return UniTask.FromResult(UIRouteResult.From(UIRouteStatus.Cancelled));
            var command = new NavigationCommand(operation, target, option);
            _navigationQueue.Enqueue(command);
            if (!_navigating)
            {
                _navigating = true;
                DrainNavigation().Forget();
            }
            return command.Completion.Task;
        }

        private async UniTaskVoid DrainNavigation()
        {
            while (_navigationQueue.Count > 0 && !_shuttingDown)
            {
                NavigationCommand command = _navigationQueue.Dequeue();
                _activeNavigation = command.Completion;
                try { command.Completion.TrySetResult(await ExecuteNavigation(command)); }
                catch (Exception error)
                {
                    Log.Exception(error);
                    command.Completion.TrySetResult(UIRouteResult.From(UIRouteStatus.OpenFailed));
                }
                // Completion callbacks can enqueue. Keep the queue owned until they return.
                _activeNavigation = null;
                _navigationCleanup.Clear();
            }
            _navigating = false;
        }

        private async UniTask<UIRouteResult> ExecuteNavigation(NavigationCommand command)
        {
            switch (command.Operation)
            {
                case Navigation.Clear:
                    ClearHistory();
                    return UIRouteResult.Ok;
                case Navigation.Sync:
                    if (command.Target == null || !IsOpen(command.Target.TypeHandle))
                        return UIRouteResult.From(UIRouteStatus.NotFound);
                    ClearHistory();
                    _history.Add(command.Target);
                    _currentView = GetWindow(command.Target.TypeHandle);
                    return UIRouteResult.Ok;
                case Navigation.Close:
                    if (_currentView == null) return UIRouteResult.From(UIRouteStatus.NotFound);
                    bool closed = await CloseWindow(_currentView, command.Option).AwaitTransition();
                    return UIRouteResult.From(closed ? UIRouteStatus.Success : UIRouteStatus.CloseFailed);
                case Navigation.Back:
                    return _history.Count < 2 ? UIRouteResult.From(UIRouteStatus.NotFound)
                        : await CommitTarget(_history[_history.Count - 2], Navigation.Back);
                case Navigation.BackToRoot:
                    return _history.Count == 0 ? UIRouteResult.From(UIRouteStatus.NotFound)
                        : _history.Count == 1 ? new UIRouteResult(UIRouteStatus.Success, _currentView.AwaitTransition())
                        : await CommitTarget(_history[0], Navigation.Back);
                case Navigation.BackTo:
                    int index = FindHistoryIndex(command.Target.TypeHandle);
                    if (index >= 0) return await CommitTarget(_history[index], Navigation.Back);
                    return command.Option ? await CommitTarget(command.Target, Navigation.Reset)
                        : UIRouteResult.From(UIRouteStatus.NotFound);
                default:
                    return await CommitTarget(command.Target, command.Operation);
            }
        }

        private async UniTask<UIRouteResult> CommitTarget(UIRouteEntry target, Navigation operation)
        {
            int version = _historyVersion;
            UIBase previous = _currentView;
            int existing = FindHistoryIndex(target.TypeHandle);
            int keep = operation == Navigation.Reset ? 0 : existing >= 0 ? existing
                : operation == Navigation.Replace && _history.Count > 0 ? _history.Count - 1 : _history.Count;
            if (keep >= MaxHistoryCount) return UIRouteResult.From(UIRouteStatus.RejectedLimit);

            if (previous != null) _navigationCleanup.Add(previous);
            for (int i = keep; i < _history.Count; i++)
            {
                UIBase old = GetWindow(_history[i].TypeHandle);
                if (old != null && old != previous) _navigationCleanup.Add(old);
            }

            UIOpenResult result = await RequestShow(GetWindowRecord(target.TypeHandle),
                UIRouteEntry.CopyArgs(target.Args), CancellationToken.None);
            UIBase opened = result.View;
            if (_shuttingDown) return UIRouteResult.From(UIRouteStatus.Cancelled);
            if (result.Status != UIOpenStatus.Opened || opened.DestroyRequested || opened.State != UIState.Opened
                || GetWindow(target.TypeHandle) != opened)
                return UIRouteResult.From(result.Status == UIOpenStatus.Failed ? UIRouteStatus.OpenFailed : UIRouteStatus.Cancelled);

            if (version != _historyVersion) keep = 0;
            _history.RemoveRange(keep, _history.Count - keep);
            _history.Add(target);
            _currentView = opened;
            _historyVersion++;
            bool closed = true;
            for (int i = 0; i < _navigationCleanup.Count; i++)
            {
                UIBase old = _navigationCleanup[i];
                if (old == opened || old.State == UIState.Destroyed) continue;
                closed &= await CloseWindow(old, skipTransition: old != previous).AwaitTransition();
            }
            if (_shuttingDown || _currentView != opened || opened.DestroyRequested || opened.State != UIState.Opened)
                return UIRouteResult.From(UIRouteStatus.Cancelled);
            return new UIRouteResult(closed ? UIRouteStatus.Success : UIRouteStatus.CloseFailed, result.Transition);
        }

        private UIRouteEntry CreateEntry(Type type, object[] args) => new()
        {
            UIType = type,
            TypeHandle = type.TypeHandle,
            Args = UIRouteEntry.CopyArgs(args),
            Sequence = ++_sequence,
        };

        private int FindHistoryIndex(RuntimeTypeHandle handle)
        {
            for (int i = 0; i < _history.Count; i++)
                if (_history[i].TypeHandle.Equals(handle)) return i;
            return -1;
        }

        private void OnWindowUnavailable(UIBase view)
        {
            if (_currentView == view) ClearHistory();
        }

        private void ClearHistory()
        {
            _currentView = null;
            _history.Clear();
            _historyVersion++;
        }

        private void StopNavigation()
        {
            ClearHistory();
            var cancelled = UIRouteResult.From(UIRouteStatus.Cancelled);
            _activeNavigation?.TrySetResult(cancelled);
            while (_navigationQueue.Count > 0) _navigationQueue.Dequeue().Completion.TrySetResult(cancelled);
        }

#if UNITY_EDITOR
        int IUINavigationDebug.HistoryCount => _history.Count;
        int IUINavigationDebug.WarningCount => _warnings.Count;
        Type IUINavigationDebug.Current => Current;
        bool IUINavigationDebug.CanBack => CanBack;

        bool IUINavigationDebug.FillHistoryInfo(int index, UIRouteDebugInfo info)
        {
            if (info == null || (uint)index >= (uint)_history.Count) return false;
            UIRouteEntry entry = _history[index];
            info.Clear();
            info.Index = index;
            info.UITypeName = entry.UIType.Name;
            info.IsRoot = index == 0;
            info.Sequence = entry.Sequence;
            var args = new StringBuilder();
            for (int i = 0; i < entry.Args.Length; i++)
            {
                if (i > 0) args.Append(", ");
                args.Append(entry.Args[i]?.ToString() ?? "null");
            }
            info.ArgsPreview = args.ToString();
            return true;
        }

        bool IUINavigationDebug.FillWarningInfo(int index, UIRouteWarningInfo info)
        {
            if (info == null || (uint)index >= (uint)_warnings.Count) return false;
            UIRouteWarningInfo warning = _warnings[index];
            info.Index = index;
            info.Sequence = warning.Sequence;
            info.UITypeName = warning.UITypeName;
            info.Message = warning.Message;
            return true;
        }

        void IUINavigationDebug.ClearWarnings() => _warnings.Clear();

        private void AddWarning(RuntimeTypeHandle handle, string message)
        {
            if (_warnings.Count == MaxHistoryCount) _warnings.RemoveAt(0);
            _warnings.Add(new UIRouteWarningInfo
            {
                Sequence = ++_warningSequence,
                UITypeName = handle.Value == IntPtr.Zero ? null : Type.GetTypeFromHandle(handle)?.Name,
                Message = message,
            });
        }
#endif
    }
}
