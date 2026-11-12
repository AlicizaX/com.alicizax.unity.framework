using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 页面级导航：维护 history，实际开关窗由 <see cref="IUIService"/> 完成，经 <see cref="IUIService.Navigation"/> 取得。
    /// history 中同类型最多一条；导航到已存在类型时按该条截断。同一时刻只保留一个页面打开。
    /// 新的导航会取代尚未提交的上一次导航，被取代的调用返回 Cancelled。
    /// 用 CloseUI 等方式从导航之外关掉当前页面，history 随之清空。
    /// </summary>
    public interface IUINavigationService
    {
        /// <summary>压入新页面；T 已在 history 中时截断到该条并以新参数刷新。返回的结果在逻辑打开后完成，转场见 AwaitTransition。</summary>
        UniTask<UIRouteResult> NavigateTo<T>(object userData = null) where T : UIWindow;

        /// <summary>用 T 替换栈顶页面。</summary>
        UniTask<UIRouteResult> Replace<T>(object userData = null) where T : UIWindow;

        /// <summary>返回上一页；打断尚未提交的打开时，撤销那次打开并直接返回成功。</summary>
        UniTask<UIRouteResult> Back();

        /// <summary>关闭当前页面并结束历史。force 关完必拆，不进缓存。</summary>
        UniTask<UIRouteResult> CloseCurrent(bool force = false);

        /// <summary>回到 history 第一条。</summary>
        UniTask<UIRouteResult> BackToRoot();

        /// <summary>回到 history 中的 T；已存在时沿用条目原参数，userData 只在 T 不在 history 且 openIfMissing 时使用。</summary>
        UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, object userData = null) where T : UIWindow;

        /// <summary>清空 history 后以 T 作为唯一页面。</summary>
        UniTask<UIRouteResult> ResetTo<T>(object userData = null) where T : UIWindow;

        /// <summary>只清空 history，不关闭任何窗口。</summary>
        UniTask<UIRouteResult> ResetHistory();

        /// <summary>把已打开的窗口登记为唯一的 history 条目（不打开、不关闭窗口）；窗口未打开时返回 NotFound。</summary>
        UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, object userData = null);

        /// <summary>history 至少两条时可返回。</summary>
        bool CanBack { get; }

        /// <summary>当前路由条目；history 为空时为 default（<see cref="UIRouteEntry.IsValid"/> 为 false）。</summary>
        UIRouteEntry CurrentEntry { get; }
    }
}
