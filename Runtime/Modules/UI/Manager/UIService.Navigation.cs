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
        private readonly List<UIWindow> _navigationCleanup = new(MaxHistoryCount);
        private NavigationCommand _running;
        private NavigationCommand _pending;
        private UIWindow _currentView;
        private int _sequence;
        private bool _navigating;

        private enum Navigation { Push, Replace, Reset, Back, BackToRoot, BackTo, Close, Clear, Sync }

        private sealed class NavigationCommand
        {
            internal readonly Navigation Operation;
            internal readonly UIRouteEntry Target;
            internal readonly bool Option;
            internal readonly CancellationTokenSource Cts = new();
            internal readonly UniTaskCompletionSource<UIRouteResult> Completion = new();
            internal bool Committed;

            internal NavigationCommand(Navigation operation, UIRouteEntry target, bool option)
            {
                Operation = operation;
                Target = target;
                Option = option;
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
            NavigationCommand running = _running;
            NavigationCommand pending = _pending;
            bool cancelOpening = running != null && !running.Committed &&
                operation == Navigation.Back && IsOpenNavigation(running.Operation);
            _pending = cancelOpening ? null : command;
            if (running != null && !running.Committed) UIHolderObjectBase.Cancel(running.Cts);
            if (pending != null)
            {
                pending.Cts.Dispose();
                pending.Completion.TrySetResult(UIRouteResult.From(UIRouteStatus.Cancelled));
            }
            if (cancelOpening)
            {
                command.Cts.Dispose();
                command.Completion.TrySetResult(UIRouteResult.Ok);
            }
            if (!_navigating && _pending != null)
            {
                _navigating = true;
                DrainNavigation().Forget();
            }
            return command.Completion.Task;
        }

        private async UniTaskVoid DrainNavigation()
        {
            while (_pending != null && !_shuttingDown)
            {
                NavigationCommand command = _pending;
                _pending = null;
                if (command.Completion.Task.Status.IsCompleted()) continue;
                _running = command;
                UIRouteResult result;
                try { result = await ExecuteNavigation(command); }
                catch (Exception error)
                {
                    Log.Exception(error);
                    result = UIRouteResult.From(UIRouteStatus.OpenFailed);
                }
                _running = null;
                _navigationCleanup.Clear();
                command.Cts.Dispose();
                command.Completion.TrySetResult(result);
            }
            _navigating = false;
        }

        private async UniTask<UIRouteResult> ExecuteNavigation(NavigationCommand command)
        {
            if (command.Cts.IsCancellationRequested) return UIRouteResult.From(UIRouteStatus.Cancelled);
            switch (command.Operation)
            {
                case Navigation.Clear:
                    command.Committed = true;
                    ClearHistory();
                    return UIRouteResult.Ok;
                case Navigation.Sync:
                    if (command.Target == null || !IsOpen(command.Target.TypeHandle))
                        return UIRouteResult.From(UIRouteStatus.NotFound);
                    command.Committed = true;
                    ClearHistory();
                    _history.Add(command.Target);
                    _currentView = GetWindow(command.Target.TypeHandle);
                    return UIRouteResult.Ok;
                case Navigation.Close:
                    if (_currentView == null) return UIRouteResult.From(UIRouteStatus.NotFound);
                    command.Committed = true;
                    await CloseWindow(_currentView, command.Option);
                    return UIRouteResult.Ok;
                case Navigation.Back:
                    return _history.Count < 2 ? UIRouteResult.From(UIRouteStatus.NotFound)
                        : await CommitTarget(command, _history[_history.Count - 2], Navigation.Back);
                case Navigation.BackToRoot:
                    if (_history.Count == 0) return UIRouteResult.From(UIRouteStatus.NotFound);
                    if (_history.Count == 1)
                        return new UIRouteResult(UIRouteStatus.Success, _currentView?.AwaitTransition() ?? UniTask.CompletedTask);
                    return await CommitTarget(command, _history[0], Navigation.Back);
                case Navigation.BackTo:
                    int index = FindHistoryIndex(command.Target.TypeHandle);
                    if (index >= 0) return await CommitTarget(command, _history[index], Navigation.Back);
                    return command.Option ? await CommitTarget(command, command.Target, Navigation.Reset)
                        : UIRouteResult.From(UIRouteStatus.NotFound);
                default:
                    return await CommitTarget(command, command.Target, command.Operation);
            }
        }

        private async UniTask<UIRouteResult> CommitTarget(NavigationCommand command, UIRouteEntry target, Navigation operation)
        {
            if (command.Cts.IsCancellationRequested) return UIRouteResult.From(UIRouteStatus.Cancelled);
            UIWindow previous = _currentView;
            int existing = FindHistoryIndex(target.TypeHandle);
            int keep = operation == Navigation.Reset ? 0 : existing >= 0 ? existing
                : operation == Navigation.Replace && _history.Count > 0 ? _history.Count - 1 : _history.Count;
            if (keep >= MaxHistoryCount) return UIRouteResult.From(UIRouteStatus.RejectedLimit);

            _navigationCleanup.Clear();
            if (previous != null) _navigationCleanup.Add(previous);
            for (int i = keep; i < _history.Count; i++)
            {
                UIWindow old = GetWindow(_history[i].TypeHandle);
                if (old != null && old != previous) _navigationCleanup.Add(old);
            }

            UIOpenResult result;
            bool targetWasOpen = IsOpen(target.TypeHandle);
            try
            {
                result = await RequestShow(GetWindowRecord(target.TypeHandle),
                    UIRouteEntry.CopyArgs(target.Args), command.Cts.Token, waitForClose: true);
            }
            catch (Exception error)
            {
                Log.Exception(error);
                return UIRouteResult.From(UIRouteStatus.OpenFailed);
            }

            UIWindow opened = result.View;
            if (command.Cts.IsCancellationRequested)
            {
                if (!targetWasOpen && opened != null)
                    CloseWindow(opened, skipTransition: true, affectNavigation: false).Forget();
                return UIRouteResult.From(UIRouteStatus.Cancelled);
            }
            if (result.Status == UIOpenStatus.Cancelled || result.Status == UIOpenStatus.Ignored)
                return UIRouteResult.From(UIRouteStatus.Cancelled);
            if (result.Status == UIOpenStatus.Failed || opened == null)
                return UIRouteResult.From(UIRouteStatus.OpenFailed);
            if (result.Status != UIOpenStatus.Opened || opened.DestroyRequested || _shuttingDown)
                return UIRouteResult.From(UIRouteStatus.Cancelled);

            command.Committed = true;
            if (_shuttingDown || opened.DestroyRequested)
                return UIRouteResult.From(UIRouteStatus.Cancelled);

            _history.RemoveRange(keep, _history.Count - keep);
            _history.Add(target);
            _currentView = opened;
            for (int i = 0; i < _navigationCleanup.Count; i++)
            {
                UIWindow old = _navigationCleanup[i];
                if (old == opened || old.State == UIState.Destroyed || old.State == UIState.Destroying) continue;
                CloseWindow(old, skipTransition: old != previous, affectNavigation: false).Forget();
            }
            return new UIRouteResult(UIRouteStatus.Success, opened.AwaitTransition());
        }

        private UIRouteEntry CreateEntry(Type type, object[] args) => new()
        {
            UIType = type,
            TypeHandle = type.TypeHandle,
            Args = UIRouteEntry.CopyArgs(args),
            Sequence = ++_sequence,
        };

        private static bool IsOpenNavigation(Navigation operation) =>
            operation is Navigation.Push or Navigation.Replace or Navigation.Reset;

        private int FindHistoryIndex(RuntimeTypeHandle handle)
        {
            for (int i = 0; i < _history.Count; i++)
                if (_history[i].TypeHandle.Equals(handle)) return i;
            return -1;
        }

        private void ClearHistory()
        {
            _currentView = null;
            _history.Clear();
        }

        private void StopNavigation()
        {
            ClearHistory();
            var cancelled = UIRouteResult.From(UIRouteStatus.Cancelled);
            NavigationCommand running = _running;
            NavigationCommand pending = _pending;
            _running = null;
            _pending = null;
            if (running != null)
            {
                UIHolderObjectBase.Cancel(running.Cts);
                running.Completion.TrySetResult(cancelled);
            }
            if (pending != null)
            {
                pending.Cts.Dispose();
                pending.Completion.TrySetResult(cancelled);
            }
            _navigating = false;
        }

#if UNITY_EDITOR
        int IUINavigationDebug.HistoryCount => _history.Count;
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
#endif
    }
}
