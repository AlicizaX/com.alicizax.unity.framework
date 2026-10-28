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
            internal readonly bool Force;
            internal readonly bool OpenIfMissing;
            internal readonly UniTaskCompletionSource<UIRouteResult> Completion = new();
            internal CancellationTokenSource Cts;
            internal bool Committed;

            internal NavigationCommand(Navigation operation, UIRouteEntry target, bool force, bool openIfMissing)
            {
                Operation = operation;
                Target = target;
                Force = force;
                OpenIfMissing = openIfMissing;
            }
        }

        public bool CanBack => _history.Count > 1;
        public Type Current => _currentView?.GetType();

        /// <summary>当前路由条目（内部实例，只读视图，不做拷贝）。</summary>
        public UIRouteEntry CurrentEntry => _history.Count == 0 ? null : _history[_history.Count - 1];

        public UniTask<UIRouteResult> NavigateTo<T>(params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.Push, CreateEntry(typeof(T), args));
        public UniTask<UIRouteResult> Replace<T>(params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.Replace, CreateEntry(typeof(T), args));
        public UniTask<UIRouteResult> ResetTo<T>(params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.Reset, CreateEntry(typeof(T), args));
        public UniTask<UIRouteResult> Back() => EnqueueNavigation(Navigation.Back);
        public UniTask<UIRouteResult> BackToRoot() => EnqueueNavigation(Navigation.BackToRoot);
        public UniTask<UIRouteResult> CloseCurrent(bool force = false) => EnqueueNavigation(Navigation.Close, force: force);
        public UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIWindow =>
            EnqueueNavigation(Navigation.BackTo, CreateEntry(typeof(T), args), openIfMissing: openIfMissing);
        public UniTask<UIRouteResult> ResetHistory() => EnqueueNavigation(Navigation.Clear);
        public UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, params object[] args) =>
            EnqueueNavigation(Navigation.Sync, currentPageType == null ? null : CreateEntry(currentPageType, args));
        public UniTask<UIRouteResult> SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args) =>
            SyncFromCurrentUI(handle.Value == IntPtr.Zero ? null : Type.GetTypeFromHandle(handle), args);

        private UniTask<UIRouteResult> EnqueueNavigation(Navigation operation, UIRouteEntry target = null,
            bool force = false, bool openIfMissing = false)
        {
            if (_shuttingDown || !_initialized) return UniTask.FromResult(UIRouteResult.From(UIRouteStatus.Cancelled));

            NavigationCommand running = _running;
            NavigationCommand pending = _pending;
            _pending = null;

            if (operation == Navigation.Back && running != null && !running.Committed && IsOpenNavigation(running.Operation))
            {
                // Back 打断尚未提交的打开：终止加载并立即返回 Ok。
                CancelNavigation(pending);
                CancelNavigation(running);
                return UniTask.FromResult(UIRouteResult.Ok);
            }

            CancelNavigation(pending);
            var command = new NavigationCommand(operation, target, force, openIfMissing);
            _pending = command;
            if (running != null && !running.Committed) CancelNavigation(running);
            if (!_navigating && _pending != null)
            {
                _navigating = true;
                DrainNavigation().Forget();
            }
            return command.Completion.Task;
        }

        /// <summary>取消排队中或执行中的命令：取消其加载，并让调用方立即得到 Cancelled 结果。</summary>
        private static void CancelNavigation(NavigationCommand command)
        {
            if (command == null) return;
            command.Cts?.Cancel();
            command.Completion.TrySetResult(UIRouteResult.From(UIRouteStatus.Cancelled));
        }

        private async UniTaskVoid DrainNavigation()
        {
            while (_pending != null && !_shuttingDown)
            {
                NavigationCommand command = _pending;
                _pending = null;
                if (command.Completion.Task.Status.IsCompleted()) continue;

                command.Cts = new CancellationTokenSource();
                _running = command;
                UIRouteResult result;
                try
                {
                    result = await ExecuteNavigation(command);
                }
                catch (Exception error)
                {
                    Log.Exception(error);
                    result = UIRouteResult.From(UIRouteStatus.OpenFailed);
                }
                finally
                {
                    command.Cts.Dispose();
                    command.Cts = null;
                    if (_running == command) _running = null;
                }
                _navigationCleanup.Clear();
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
                    await CloseWindow(_currentView, command.Force, token: command.Cts.Token);
                    return command.Cts.IsCancellationRequested
                        ? UIRouteResult.From(UIRouteStatus.Cancelled) : UIRouteResult.Ok;
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
                    return command.OpenIfMissing
                        ? await CommitTarget(command, command.Target, Navigation.Reset)
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

            bool targetWasOpen = IsOpen(target.TypeHandle);
            UIOpenResult result = await RequestShow(GetWindowRecord(target.TypeHandle), target.Args,
                command.Cts.Token, waitForClose: true);

            UIWindow opened = result.View;
            if (command.Cts.IsCancellationRequested)
            {
                if (!targetWasOpen && opened != null)
                    CloseWindow(opened, skipTransition: true, affectNavigation: false).Forget();
                return UIRouteResult.From(UIRouteStatus.Cancelled);
            }
            if (result.Status is UIOpenStatus.Cancelled or UIOpenStatus.Ignored)
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
                if (old == opened || old.State is UIState.Destroyed or UIState.Destroying) continue;
                CloseWindow(old, skipTransition: old != previous, affectNavigation: false).Forget();
            }
            return new UIRouteResult(UIRouteStatus.Success, opened.AwaitTransition());
        }

        private UIRouteEntry CreateEntry(Type type, object[] args) =>
            new(type, UIRouteEntry.CopyArgs(args), ++_sequence);

        private static bool IsOpenNavigation(Navigation operation) =>
            operation is Navigation.Push or Navigation.Replace or Navigation.Reset;

        /// <summary>history 中同类型最多一条：导航到已存在类型时按该条截断。</summary>
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
            _pending = null;
            _running = null;
            if (running != null)
            {
                running.Cts?.Cancel();
                running.Completion.TrySetResult(cancelled);
            }
            if (pending != null) pending.Completion.TrySetResult(cancelled);
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
