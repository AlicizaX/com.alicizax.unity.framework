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
        // Navigation commands, including history changes, execute in FIFO order.
        UniTask<UIRouteResult> NavigateTo<T>(params object[] args) where T : UIWindow;

        UniTask<UIRouteResult> Replace<T>(params object[] args) where T : UIWindow;

        UniTask<UIRouteResult> Back();

        /// <summary>排队关闭当前页面并结束历史。force 绕过窗口缓存。</summary>
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
        /// await 到逻辑打开完成；转场继续推进，可用 .AwaitTransition() 等待。
        /// 已打开时调用 OnRefresh；只有非空参数数组才覆盖原参数。
        /// </summary>
        UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIWindow;

        /// <summary>
        /// 独立取消本次打开请求。最后一个加载等待者取消后才撤销资源加载。
        /// </summary>
        UniTask<T> ShowUI<T>(CancellationToken cancellationToken, params object[] userDatas) where T : UIWindow;

        /// <summary>
        /// 异步显示 UI（使用字符串类型名）。资源失败或取消时返回 null。
        /// </summary>
        UniTask<UIBase> ShowUI(string type, params object[] userDatas);

        /// <summary>
        /// 使用字符串类型名，独立取消本次打开请求。
        /// </summary>
        UniTask<UIBase> ShowUI(string type, CancellationToken cancellationToken, params object[] userDatas);

        /// <summary>
        /// 异步显示 UI（使用运行时类型句柄）。资源失败或取消时返回 null。
        /// </summary>
        UniTask<UIBase> ShowUI(RuntimeTypeHandle handle, params object[] userDatas);

        /// <summary>
        /// 使用运行时类型句柄，独立取消本次打开请求。
        /// </summary>
        UniTask<UIBase> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, params object[] userDatas);

        /// <summary>
        /// 同步显示 UI。
        /// 同步完成资源加载与初始化；Open 视觉过渡默认在后台推进。
        /// 异步资源仍在加载时抛出 InvalidOperationException；关场动画中可反转重开。
        /// </summary>
        T ShowUISync<T>(params object[] userDatas) where T : UIWindow;

        // ───────────────────────────────────────────────
        //  Close / Get 系列
        // ───────────────────────────────────────────────

        /// <summary>
        /// 关闭指定类型的窗口实例。页面返回请显式调用 Back。
        /// 同步执行关闭钩子，停止事件和更新；AwaitTransition 等待动画及缓存/销毁收尾。
        /// force 绕过缓存；skipTransition 跳过整棵关闭子树的动画。
        /// </summary>
        UICloseHandle CloseUI<T>(bool force = false, bool skipTransition = false) where T : UIWindow;

        /// <summary>
        /// 关闭指定类型的窗口实例，不执行导航返回。
        /// </summary>
        UICloseHandle CloseUI(RuntimeTypeHandle handle, bool force = false, bool skipTransition = false);

        /// <summary>
        /// 是否逻辑打开。打开动画可通过 AwaitTransition 单独等待。
        /// </summary>
        bool IsOpen<T>() where T : UIWindow;

        /// <summary>
        /// 是否逻辑打开。打开动画可通过 AwaitTransition 单独等待。
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
        /// 获取当前已打开的指定类型 UI。
        /// </summary>
        T GetUI<T>() where T : UIWindow;
    }
}
