using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 页面导航：history 为定长值类型数组，同类型最多一条。没有命令队列：每次导航递增版本号，
    /// 只在目标窗口加载期间挂起；提交前（加载中或打开钩子里）被后来的导航取代则返回 Cancelled，并撤销它发起的打开；
    /// 后来的导航以同一窗口为目标、或 ShowUI 也请求了该窗口时由对方接手。
    /// </summary>
    internal sealed partial class UIService
    {
        private const int MaxHistoryCount = 64;

        private enum Navigation : byte { Push, Replace, Reset, Back }

        private readonly UIRouteEntry[] _history = new UIRouteEntry[MaxHistoryCount];
        private int _historyCount;
        private int _navigationVersion;
        private int _sequence;
        private RuntimeTypeHandle _pendingHandle;

        public bool CanBack => _historyCount > 1;
        public Type Current => _historyCount == 0 ? null : _history[_historyCount - 1].UIType;
        public UIRouteEntry CurrentEntry => _historyCount == 0 ? default : _history[_historyCount - 1];

        private UIWindow CurrentPage => _historyCount == 0 ? null : GetWindow(_history[_historyCount - 1].TypeHandle);

        private static UIRouteResult Result(UIRouteStatus status) => UIRouteResult.From(status);

        public UniTask<UIRouteResult> NavigateTo<T>(object userData = null) where T : UIWindow =>
            Navigate(Navigation.Push, typeof(T), userData);

        public UniTask<UIRouteResult> Replace<T>(object userData = null) where T : UIWindow =>
            Navigate(Navigation.Replace, typeof(T), userData);

        public UniTask<UIRouteResult> ResetTo<T>(object userData = null) where T : UIWindow =>
            Navigate(Navigation.Reset, typeof(T), userData);

        public UniTask<UIRouteResult> Back()
        {
            // Back 打断尚未完成加载的打开：撤销那次打开即视为返回成功，history 不动
            if (_pendingHandle.Value != IntPtr.Zero) return Finish(UIRouteStatus.Success);
            return _historyCount < 2
                ? Finish(UIRouteStatus.NotFound)
                : Commit(Navigation.Back, _history[_historyCount - 2]);
        }

        public UniTask<UIRouteResult> BackToRoot()
        {
            if (_historyCount == 0) return Finish(UIRouteStatus.NotFound);
            if (_historyCount > 1) return Commit(Navigation.Back, _history[0]);
            if (!BeginNavigation()) return UniTask.FromResult(Result(UIRouteStatus.Cancelled));
            UIWindow current = CurrentPage;
            return UniTask.FromResult(new UIRouteResult(UIRouteStatus.Success, new UIViewTransition(current)));
        }

        public UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, object userData = null) where T : UIWindow
        {
            int index = FindHistoryIndex(typeof(T).TypeHandle);
            if (index >= 0) return Commit(Navigation.Back, _history[index]);
            return openIfMissing
                ? Commit(Navigation.Reset, CreateEntry(typeof(T), userData))
                : Finish(UIRouteStatus.NotFound);
        }

        /// <summary>关闭当前页面并结束历史。force 关完必拆，不进缓存。</summary>
        public UniTask<UIRouteResult> CloseCurrent(bool force = false)
        {
            if (!BeginNavigation()) return UniTask.FromResult(Result(UIRouteStatus.Cancelled));
            UIWindow current = CurrentPage;
            if (current == null) return UniTask.FromResult(Result(UIRouteStatus.NotFound));
            ClearHistory();
            // history 在调用时已清空，关闭本身不会被之后的导航撤销
            return UniTask.FromResult(new UIRouteResult(UIRouteStatus.Success,
                CloseWindow(current, force, snap: false, affectNavigation: false)));
        }

        public UniTask<UIRouteResult> ResetHistory()
        {
            if (!BeginNavigation()) return UniTask.FromResult(Result(UIRouteStatus.Cancelled));
            ClearHistory();
            return UniTask.FromResult(UIRouteResult.Ok);
        }

        public UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, object userData = null)
        {
            if (!BeginNavigation()) return UniTask.FromResult(Result(UIRouteStatus.Cancelled));
            if (currentPageType == null || !IsOpen(currentPageType.TypeHandle))
                return UniTask.FromResult(Result(UIRouteStatus.NotFound));
            ClearHistory();
            _history[_historyCount++] = CreateEntry(currentPageType, userData);
            return UniTask.FromResult(UIRouteResult.Ok);
        }

        public UniTask<UIRouteResult> SyncFromCurrentUI(RuntimeTypeHandle handle, object userData = null) =>
            SyncFromCurrentUI(handle.Value == IntPtr.Zero ? null : Type.GetTypeFromHandle(handle), userData);

        private UniTask<UIRouteResult> Navigate(Navigation operation, Type type, object userData) =>
            Commit(operation, CreateEntry(type, userData));

        private UniTask<UIRouteResult> Finish(UIRouteStatus status) =>
            UniTask.FromResult(Result(BeginNavigation() ? status : UIRouteStatus.Cancelled));

        /// <summary>开始一次导航：递增版本号，取代加载中的那次导航。</summary>
        private bool BeginNavigation(RuntimeTypeHandle target = default)
        {
            _navigationVersion++;
            if (_pendingHandle.Value != IntPtr.Zero && !_pendingHandle.Equals(target))
            {
                UIWindow pending = GetWindow(_pendingHandle);
                _pendingHandle = default;
                if (pending != null) CloseWindow(pending, force: false, snap: true, affectNavigation: false);
            }
            return _lifetime == UILifetime.Running;
        }

        /// <summary>打开目标并提交 history。目标已加载时同步完成；首次加载时挂起一次。</summary>
        private async UniTask<UIRouteResult> Commit(Navigation operation, UIRouteEntry target)
        {
            if (!UIMetaRegistry.TryGet(target.TypeHandle, out UIMetaRegistry.UIMetaInfo info) || !info.IsWindow)
                return Result(UIRouteStatus.OpenFailed);
            if (KeepCount(operation, target.TypeHandle) >= MaxHistoryCount) return Result(UIRouteStatus.RejectedLimit);
            if (!BeginNavigation(target.TypeHandle)) return Result(UIRouteStatus.Cancelled);
            int version = _navigationVersion;
            UIWindow existing = GetWindow(target.TypeHandle);
            if (existing == null || existing.State is UIState.Closed or UIState.Closing) _pendingHandle = target.TypeHandle;
            UIWindow window = OpenWindow(info, target.UserData, sync: false);
            if (window == null)
            {
                _pendingHandle = default;
                return Result(UIRouteStatus.OpenFailed);
            }
            if (window.State == UIState.Loading) await window.AwaitLoaded();
            if (version != _navigationVersion) return Result(UIRouteStatus.Cancelled);
            _pendingHandle = default;
            if (window.State is not (UIState.Opening or UIState.Opened))
                return Result(window.State == UIState.Destroyed ? UIRouteStatus.OpenFailed : UIRouteStatus.Cancelled);

            // 只保留一个页面打开：被截断的页面逐条出栈后直接关闭，之前的页面播关闭转场。
            // 先出栈再关：关闭钩子里发起的新导航看到的是完整的 history 前缀。
            UIWindow previous = CurrentPage;
            int keep = KeepCount(operation, target.TypeHandle);
            for (int i = _historyCount - 1; i >= keep; i--)
            {
                UIWindow dropped = GetWindow(_history[i].TypeHandle);
                _history[i] = default;
                _historyCount = i;
                if (dropped == null || dropped == window || dropped == previous) continue;
                CloseWindow(dropped, force: false, snap: true, affectNavigation: false);
                // 关闭钩子里发起了新导航：它已提交自己的 history，本次不再改动
                if (version != _navigationVersion) return Result(UIRouteStatus.Cancelled);
            }
            // 栈顶的前一页面在新条目提交后再关，否则它同步销毁时会把 history 整个清掉
            _history[_historyCount++] = target;
            if (previous != null && previous != window)
            {
                CloseWindow(previous, force: false, snap: false, affectNavigation: false);
                if (version != _navigationVersion) return Result(UIRouteStatus.Cancelled);
            }
            return new UIRouteResult(UIRouteStatus.Success, new UIViewTransition(window));
        }

        private UIRouteEntry CreateEntry(Type type, object userData) => new(type, userData, ++_sequence);

        /// <summary>提交后保留的 history 条数（不含新条目）。O(H)。</summary>
        private int KeepCount(Navigation operation, RuntimeTypeHandle handle)
        {
            if (operation == Navigation.Reset) return 0;
            int existing = FindHistoryIndex(handle);
            if (existing >= 0) return existing;
            return operation == Navigation.Replace && _historyCount > 0 ? _historyCount - 1 : _historyCount;
        }

        /// <summary>O(H)，H ≤ 64。</summary>
        private int FindHistoryIndex(RuntimeTypeHandle handle)
        {
            for (int i = 0; i < _historyCount; i++)
                if (_history[i].TypeHandle.Equals(handle)) return i;
            return -1;
        }

        private bool IsCurrentPage(UIWindow window) =>
            _historyCount > 0 && _history[_historyCount - 1].TypeHandle.Equals(window.MetaInfo.Handle);

        private void ClearHistory()
        {
            Array.Clear(_history, 0, _historyCount);
            _historyCount = 0;
        }

        private void StopNavigation()
        {
            ClearHistory();
            _navigationVersion++;
            _pendingHandle = default;
        }
    }
}
