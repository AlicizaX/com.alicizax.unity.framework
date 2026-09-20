using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 模块接口：负责 UI 的创建、显示、关闭与查询。
    /// 页面导航用 NavigateTo/Back；叠加弹窗/独立面板用 ShowUI/CloseUI。
    /// 直接关闭当前页面会结束历史；返回上一页请调用 Back。
    /// </summary>
    public interface IUIService : IService
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

        UIRouteEntry CurrentEntry { get; }

        /// <summary>
        /// 初始化 UI 模块。
        /// </summary>
        /// <param name="root">UI 根节点（通常为 Canvas 根）</param>
        /// <param name="isOrthographic">摄像机是否正交模式</param>
        void Initialize(Transform root, bool isOrthographic);

        /// <summary>
        /// UI 摄像机
        /// </summary>
        Camera UICamera { get; }

        /// <summary>
        /// UI 根节点
        /// </summary>
        Transform UICanvasRoot { get; }

        /// <summary>
        /// 获取指定 UI 层级的根节点。
        /// </summary>
        /// <param name="layer">UI 层级</param>
        /// <returns>指定层级对应的根节点</returns>
        RectTransform GetLayer(UILayer layer);

        /// <summary>
        /// 设置 UI 阻挡层，并在指定时长后自动解除。
        /// </summary>
        /// <param name="timeDuration">阻挡持续时间，单位为秒</param>
        void SetUIBlock(float timeDuration);

        /// <summary>
        /// 强制退出 UI 阻挡状态。
        /// </summary>
        void ForceExitBlock();

        // ───────────────────────────────────────────────
        //  Show 系列：同步与异步可任意穿插
        // ───────────────────────────────────────────────

        /// <summary>
        /// 异步显示 UI（异步加载资源）。
        /// await 到 OnOpen 之后返回，不等开动画；用实例 AwaitTransition() 等到 Opened。
        /// Opening 期间再次 Show 沿用最新参数，开动画结束后只 Refresh 一次。
        /// 已打开时调用 OnRefresh；只有非空参数数组才覆盖原参数。
        /// 资源失败或未知类型记录错误并返回 null。Closing 期间 Show 自己忽略并 Warning。
        /// </summary>
        UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIWindow;

        /// <summary>
        /// 独立取消本次打开请求。最后一个加载等待者取消后才撤销资源加载。
        /// </summary>
        UniTask<T> ShowUI<T>(CancellationToken cancellationToken, params object[] userDatas) where T : UIWindow;

        /// <summary>
        /// 异步显示 UI（使用字符串类型名）。未知类型或资源失败时记录错误并返回 null。
        /// </summary>
        UniTask<UIWindow> ShowUI(string type, params object[] userDatas);

        /// <summary>
        /// 使用字符串类型名，独立取消本次打开请求。
        /// </summary>
        UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, params object[] userDatas);

        /// <summary>
        /// 异步显示 UI（使用运行时类型句柄）。未知类型或资源失败时记录错误并返回 null。
        /// </summary>
        UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, params object[] userDatas);

        /// <summary>
        /// 使用运行时类型句柄，独立取消本次打开请求。
        /// </summary>
        UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, params object[] userDatas);

        /// <summary>
        /// 同步显示 UI。
        /// 同步完成资源加载与 OnOpen；开动画后台推进。
        /// 异步资源仍在加载时记录错误并返回 null。
        /// </summary>
        T ShowUISync<T>(params object[] userDatas) where T : UIWindow;

        // ───────────────────────────────────────────────
        //  Close / Get 系列
        // ───────────────────────────────────────────────

        /// <summary>
        /// 关闭指定类型的窗口实例。页面返回请显式调用 Back。
        /// await 等到 Closed（不可见且关动画结束），然后才缓存或 DestroyNow。
        /// force 关完必拆；skipTransition 仅对尚未开始的关动画生效。
        /// </summary>
        UniTask CloseUI<T>(bool force = false, bool skipTransition = false) where T : UIWindow;

        /// <summary>
        /// 关闭指定类型的窗口实例，不执行导航返回。
        /// </summary>
        UniTask CloseUI(RuntimeTypeHandle handle, bool force = false, bool skipTransition = false);

        /// <summary>
        /// 是否逻辑打开（OnOpen 返回后至 Closing 开始前）。
        /// </summary>
        bool IsOpen<T>() where T : UIWindow;

        /// <summary>
        /// 是否逻辑打开（OnOpen 返回后至 Closing 开始前）。
        /// </summary>
        bool IsOpen(RuntimeTypeHandle handle);

        /// <summary>
        /// 关闭当前最上层且满足谓词的 UI。
        /// </summary>
        UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false);

        /// <summary>
        /// 从最高显示层开始查找当前可见且满足谓词的 UI Holder。
        /// </summary>
        bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder);

        /// <summary>
        /// 获取 Opening / Opened / 缓存中的指定类型 UI；Closing 或已销毁时返回 null。
        /// </summary>
        T GetUI<T>() where T : UIWindow;
    }
}
