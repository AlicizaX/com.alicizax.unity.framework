using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 页面级导航：维护 history，实际开关窗仍由 <see cref="IUIPanelService"/> 完成。
    /// history 中同类型最多一条；导航到已存在类型时按该条截断。
    /// </summary>
    public interface IUINavigationService
    {
        UniTask<UIRouteResult> NavigateTo<T>(params object[] args) where T : UIWindow;

        UniTask<UIRouteResult> Replace<T>(params object[] args) where T : UIWindow;

        UniTask<UIRouteResult> Back();

        /// <summary>关闭当前页面并结束历史。force 关完必拆，不进缓存。</summary>
        UniTask<UIRouteResult> CloseCurrent(bool force = false);

        UniTask<UIRouteResult> BackToRoot();

        UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIWindow;

        UniTask<UIRouteResult> ResetTo<T>(params object[] args) where T : UIWindow;

        UniTask<UIRouteResult> ResetHistory();

        UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, params object[] args);

        UniTask<UIRouteResult> SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args);

        bool CanBack { get; }

        Type Current { get; }

        /// <summary>当前路由条目（只读视图，不做拷贝）。</summary>
        UIRouteEntry CurrentEntry { get; }
    }
}
