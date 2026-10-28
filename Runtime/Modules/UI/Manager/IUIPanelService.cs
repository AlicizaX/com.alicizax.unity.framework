using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>面板开关与查询：异步/同步打开、关闭、缓存与深度排序由实现方负责。</summary>
    public interface IUIPanelService
    {
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

        /// <summary>独立取消本次打开请求。最后一个加载等待者取消后才撤销资源加载。</summary>
        UniTask<T> ShowUI<T>(CancellationToken cancellationToken, params object[] userDatas) where T : UIWindow;

        /// <summary>异步显示 UI（使用字符串类型名）。未知类型或资源失败时记录错误并返回 null。</summary>
        UniTask<UIWindow> ShowUI(string type, params object[] userDatas);

        /// <summary>使用字符串类型名，独立取消本次打开请求。</summary>
        UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, params object[] userDatas);

        /// <summary>异步显示 UI（使用运行时类型句柄）。未知类型或资源失败时记录错误并返回 null。</summary>
        UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, params object[] userDatas);

        /// <summary>使用运行时类型句柄，独立取消本次打开请求。</summary>
        UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, params object[] userDatas);

        /// <summary>
        /// 同步显示 UI：同步完成资源加载与 OnOpen；开动画后台推进。
        /// 异步资源仍在加载时记录错误并返回 null。
        /// </summary>
        T ShowUISync<T>(params object[] userDatas) where T : UIWindow;

        // ───────────────────────────────────────────────
        //  Close / Get 系列
        // ───────────────────────────────────────────────

        /// <summary>
        /// 关闭指定类型的窗口实例。页面返回请显式调用导航 Back。
        /// await 等到 Closed（不可见且关动画结束），然后才缓存或 DestroyNow。
        /// force 关完必拆；skipTransition 仅对尚未开始的关动画生效。
        /// </summary>
        UniTask CloseUI<T>(bool force = false, bool skipTransition = false) where T : UIWindow;

        /// <summary>关闭指定类型的窗口实例，不执行导航返回。</summary>
        UniTask CloseUI(RuntimeTypeHandle handle, bool force = false, bool skipTransition = false);

        /// <summary>是否逻辑打开（OnOpen 返回后至 Closing 开始前）。</summary>
        bool IsOpen<T>() where T : UIWindow;

        /// <summary>是否逻辑打开（OnOpen 返回后至 Closing 开始前）。</summary>
        bool IsOpen(RuntimeTypeHandle handle);

        /// <summary>关闭当前最上层且满足谓词的 UI。</summary>
        UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false);

        /// <summary>从最高显示层开始查找当前可见且满足谓词的 UI Holder。</summary>
        bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder);

        /// <summary>获取 Opening / Opened / 缓存中的指定类型 UI；Closing 或已销毁时返回 null。</summary>
        T GetUI<T>() where T : UIWindow;
    }
}
