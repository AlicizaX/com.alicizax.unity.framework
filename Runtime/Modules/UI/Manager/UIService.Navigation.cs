using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        public IUINavigationService Navigation => _navigator;

        /// <summary>
        /// 页面导航：history 中同类型最多一条，同一时刻只保留一个页面打开。
        /// 每次导航递增版本号；等待期间版本号变了，说明已被后来的导航取代，本次返回 Cancelled。
        /// 导航之外关掉或销毁当前页面时，<see cref="Forget"/> 清空 history；导航自己开关页面走服务内部的打开与关闭，不经过它。
        /// </summary>
        private sealed class Navigator : IUINavigationService
        {
            private enum Operation : byte
            {
                Push,
                Replace,
                Reset,
            }

            internal readonly List<UIRouteEntry> History = new(8);

            private readonly UIService _ui;

            /// <summary>已发起、尚未提交的那次打开的窗口类型；先于窗口实例记下，打开钩子里发起的导航才能撤销它。</summary>
            private Type _pending;

            private int _version;

            internal Navigator(UIService ui) => _ui = ui;

            public bool CanBack => History.Count > 1;

            public UIRouteEntry CurrentEntry => History.Count == 0 ? default : History[History.Count - 1];

            private UIWindow CurrentPage => History.Count == 0 ? null : _ui.GetWindow(CurrentEntry.UIType.TypeHandle);

            public UniTask<UIRouteResult> NavigateTo<T>(object userData = null) where T : UIWindow =>
                Commit(Operation.Push, new UIRouteEntry(typeof(T), userData));

            public UniTask<UIRouteResult> Replace<T>(object userData = null) where T : UIWindow =>
                Commit(Operation.Replace, new UIRouteEntry(typeof(T), userData));

            public UniTask<UIRouteResult> ResetTo<T>(object userData = null) where T : UIWindow =>
                Commit(Operation.Reset, new UIRouteEntry(typeof(T), userData));

            public UniTask<UIRouteResult> Back()
            {
                // Back 打断尚未提交的打开：撤销那次打开即视为返回成功，history 不动
                if (_pending != null) return Finish(UIRouteStatus.Success);
                return History.Count < 2
                    ? Finish(UIRouteStatus.NotFound)
                    : Commit(Operation.Push, History[History.Count - 2]);
            }

            public UniTask<UIRouteResult> BackToRoot()
            {
                if (History.Count == 0) return Finish(UIRouteStatus.NotFound);
                if (History.Count > 1) return Commit(Operation.Push, History[0]);
                Begin(null);
                return UniTask.FromResult(new UIRouteResult(UIRouteStatus.Success, new UIViewTransition(CurrentPage)));
            }

            public UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, object userData = null) where T : UIWindow
            {
                int index = IndexOf(typeof(T));
                if (index >= 0) return Commit(Operation.Push, History[index]);
                return openIfMissing
                    ? Commit(Operation.Reset, new UIRouteEntry(typeof(T), userData))
                    : Finish(UIRouteStatus.NotFound);
            }

            public UniTask<UIRouteResult> CloseCurrent(bool force = false)
            {
                Begin(null);
                UIWindow current = CurrentPage;
                if (current == null) return UniTask.FromResult(new UIRouteResult(UIRouteStatus.NotFound));
                return UniTask.FromResult(new UIRouteResult(UIRouteStatus.Success, _ui.CloseWindow(current, force, snap: false)));
            }

            public UniTask<UIRouteResult> ResetHistory()
            {
                Begin(null);
                History.Clear();
                return UniTask.FromResult(UIRouteResult.Ok);
            }

            public UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, object userData = null)
            {
                Begin(null);
                if (currentPageType == null || !_ui.IsOpen(currentPageType.TypeHandle))
                    return UniTask.FromResult(new UIRouteResult(UIRouteStatus.NotFound));
                History.Clear();
                History.Add(new UIRouteEntry(currentPageType, userData));
                return UniTask.FromResult(UIRouteResult.Ok);
            }

            /// <summary>服务停止：进行中的导航按被取代处理（返回 Cancelled），history 清空。</summary>
            internal void Stop()
            {
                _version++;
                _pending = null;
                History.Clear();
            }

            /// <summary>导航之外关掉或销毁了窗口：它是当前页面时清空 history。</summary>
            internal void Forget(UIWindow window)
            {
                if (History.Count > 0 && CurrentEntry.UIType == window.MetaInfo.ViewType) History.Clear();
            }

            /// <summary>ShowUI 也打开了导航正等着提交的窗口：改由 ShowUI 接手，之后的导航不再撤销它。</summary>
            internal void Release(UIWindow window)
            {
                if (_pending == window.MetaInfo.ViewType) _pending = null;
            }

            /// <summary>不打开页面的导航：取代进行中的导航后直接给出结果。</summary>
            private UniTask<UIRouteResult> Finish(UIRouteStatus status)
            {
                Begin(null);
                return UniTask.FromResult(new UIRouteResult(status));
            }

            /// <summary>
            /// 开始一次导航：递增版本号，并撤销尚未提交的那次打开（直接关闭它的窗口）。
            /// 后来的导航以同一窗口为目标时不撤销，由后者接手。
            /// </summary>
            private void Begin(Type target)
            {
                _version++;
                if (_pending == null || _pending == target) return;
                UIWindow pending = _ui.GetWindow(_pending.TypeHandle);
                _pending = null;
                if (pending != null) _ui.Close(pending, force: false, snap: true);
            }

            /// <summary>
            /// 打开目标并提交到 history。新页面逻辑打开后才关闭上一页面；
            /// 提交前（加载中或打开钩子里）被后来的导航取代时返回 Cancelled，history 不变。
            /// </summary>
            private async UniTask<UIRouteResult> Commit(Operation operation, UIRouteEntry target)
            {
                if (!UIMetaRegistry.TryGet(target.UIType.TypeHandle, out UIMetaRegistry.UIMetaInfo info))
                    return new UIRouteResult(UIRouteStatus.OpenFailed);
                Begin(target.UIType);
                int version = _version;
                // 只有由本次导航打开的窗口才登记为待提交；已经打开或正在加载的窗口不是这次导航带来的，取消时不该被关
                UIWindow existing = _ui.GetWindow(info.Handle);
                if (existing == null || existing.State is UIState.Closed or UIState.Closing) _pending = target.UIType;
                UIWindow window = _ui.OpenWindow(info, target.UserData, sync: false);
                if (window == null)
                {
                    _pending = null;
                    return new UIRouteResult(UIRouteStatus.OpenFailed);
                }
                if (window.State == UIState.Loading) await window.AwaitLoaded();
                if (version != _version) return new UIRouteResult(UIRouteStatus.Cancelled);
                _pending = null;
                if (window.State is not (UIState.Opening or UIState.Opened))
                    return new UIRouteResult(window.State == UIState.Destroyed ? UIRouteStatus.OpenFailed : UIRouteStatus.Cancelled);

                // 被截断的条目都在栈顶之下，它们的页面早已关闭；只需截断 history、压入新条目，再关掉之前的栈顶页面
                UIWindow previous = CurrentPage;
                int keep = KeepCount(operation, target.UIType);
                History.RemoveRange(keep, History.Count - keep);
                History.Add(target);
                if (previous != null && previous != window)
                {
                    _ui.Close(previous, force: false, snap: false);
                    if (version != _version) return new UIRouteResult(UIRouteStatus.Cancelled);
                }
                return new UIRouteResult(UIRouteStatus.Success, new UIViewTransition(window));
            }

            /// <summary>提交后要保留的 history 条数（不含新条目）。同类型已在 history 中时截断到它；否则 Replace 去掉栈顶。</summary>
            private int KeepCount(Operation operation, Type type)
            {
                if (operation == Operation.Reset) return 0;
                int existing = IndexOf(type);
                if (existing >= 0) return existing;
                return operation == Operation.Replace && History.Count > 0 ? History.Count - 1 : History.Count;
            }

            private int IndexOf(Type type)
            {
                for (int i = 0; i < History.Count; i++)
                    if (History[i].UIType == type) return i;
                return -1;
            }
        }
    }
}
