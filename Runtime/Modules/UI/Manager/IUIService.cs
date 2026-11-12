using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 模块门面：根节点与层级、面板开关与查询、输入遮挡层；页面导航见 <see cref="Navigation"/>。
    /// </summary>
    public interface IUIService : IService
    {
        /// <summary>页面导航。</summary>
        IUINavigationService Navigation { get; }

        /// <summary>UI 摄像机。</summary>
        Camera UICamera { get; }

        /// <summary>UI 根节点。</summary>
        Transform UICanvasRoot { get; }

        /// <summary>获取指定 UI 层级的根节点。</summary>
        RectTransform GetLayer(UILayer layer);

        /// <summary>
        /// 打开窗口（必要时异步加载资源）。OnOpen 执行后返回，不等开场转场；需要等转场用实例的 AwaitTransition()。
        /// 已打开时只更新参数并 OnRefresh；userData 为 null 时保留原参数（进入缓存时参数已清空）。
        /// 关闭转场播放中再次打开会从当前视觉反向打开。多次异步打开同一窗口返回同一结果。
        /// 返回 null：未知类型、资源加载失败，或加载完成前已被关闭。
        /// </summary>
        UniTask<T> ShowUI<T>(object userData = null) where T : UIWindow;

        /// <summary>
        /// 可独立取消的打开：令牌取消时只有本次调用返回 null，窗口照常加载与打开。调用前已取消则不发起打开。
        /// </summary>
        UniTask<T> ShowUI<T>(CancellationToken cancellationToken, object userData = null) where T : UIWindow;

        /// <summary>按类型全名打开窗口，规则同 <see cref="ShowUI{T}(object)"/>。</summary>
        UniTask<UIWindow> ShowUI(string type, object userData = null);

        /// <summary>按类型全名打开窗口，取消规则同 <see cref="ShowUI{T}(CancellationToken, object)"/>。</summary>
        UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, object userData = null);

        /// <summary>按运行时类型句柄打开窗口，规则同 <see cref="ShowUI{T}(object)"/>。</summary>
        UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, object userData = null);

        /// <summary>按运行时类型句柄打开窗口，取消规则同 <see cref="ShowUI{T}(CancellationToken, object)"/>。</summary>
        UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, object userData = null);

        /// <summary>同步加载并打开；该窗口正在异步加载时记录警告并返回 null。</summary>
        T ShowUISync<T>(object userData = null) where T : UIWindow;

        /// <summary>
        /// 关闭窗口；返回值可等待到关闭转场结束、窗口已进入缓存或被销毁，不等待时不分配。
        /// force：关完即销毁，不进缓存；snap：关闭转场直接落到终态。
        /// 加载中关闭 = 取消加载并销毁。关闭导航当前页会清空导航历史。
        /// </summary>
        UIViewTransition CloseUI<T>(bool force = false, bool snap = false) where T : UIWindow;

        /// <summary>按运行时类型句柄关闭窗口，规则同 <see cref="CloseUI{T}"/>。</summary>
        UIViewTransition CloseUI(RuntimeTypeHandle handle, bool force = false, bool snap = false);

        /// <summary>是否逻辑打开（Opening 或 Opened）。</summary>
        bool IsOpen<T>() where T : UIWindow;

        /// <summary>是否逻辑打开（Opening 或 Opened）。</summary>
        bool IsOpen(RuntimeTypeHandle handle);

        /// <summary>
        /// 从最高层开始关闭第一个逻辑打开且满足谓词的窗口，predicate 为 null 表示不过滤；它是导航当前页时会清空导航历史。
        /// </summary>
        UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false);

        /// <summary>从最高层开始查找第一个逻辑打开（不含正在关闭）且满足谓词的窗口 Holder，predicate 为 null 表示不过滤。</summary>
        bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder);

        /// <summary>获取打开中或缓存中的指定类型窗口；加载中、正在关闭或已销毁时返回 null。</summary>
        T GetUI<T>() where T : UIWindow;

        /// <summary>显示全屏输入遮挡层，并在指定时长后自动解除。</summary>
        void SetUIBlock(float timeDuration);

        /// <summary>强制解除输入遮挡。</summary>
        void ForceExitBlock();
    }
}
