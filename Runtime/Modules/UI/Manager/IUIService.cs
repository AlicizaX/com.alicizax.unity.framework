using System;
using AlicizaX.Timer.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 模块接口：负责 UI 的创建、显示、关闭与查询。
    /// 页面导航用 Router；叠加弹窗/独立面板用 ShowUI/CloseUI。
    /// 同一逻辑页不要混用两条路径。
    /// </summary>
    public interface IUIService : IService
    {
        /// <summary>
        /// 页面级导航。历史栈、Back/Replace 由 Router 维护；实例生命周期仍由本服务负责。
        /// </summary>
        IUIRouter Router { get; }

        /// <summary>
        /// 初始化 UI 模块。
        /// </summary>
        /// <param name="root">UI 根节点（通常为 Canvas 根）</param>
        /// <param name="isOrthographic">摄像机是否正交模式</param>
        void Initialize(Transform root, bool isOrthographic);

        /// <summary>
        /// UI 摄像机
        /// </summary>
        Camera UICamera { get; set; }

        /// <summary>
        /// UI 根节点
        /// </summary>
        Transform UICanvasRoot { get; set; }

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
        //  Show 系列：异步为主，同步为辅
        // ───────────────────────────────────────────────

        /// <summary>
        /// 异步显示 UI（无参重载，避免 params 空数组分配）。
        /// await 到逻辑打开完成；转场默认并行，可用 .AwaitViewTransition() 等待。
        /// 同类型已打开/打开中时只刷新 userData（latest wins）。
        /// </summary>
        UniTask<T> ShowUI<T>() where T : UIBase;

        /// <summary>
        /// 异步显示 UI，并返回精确状态（Opened / Failed / Cancelled）。
        /// await 到逻辑打开完成；转场默认并行，可用 .AwaitViewTransition() 等待。
        /// </summary>
        UniTask<UIShowResult<T>> ShowUIResult<T>() where T : UIBase;

        /// <summary>
        /// 异步显示 UI（推荐方式）。
        /// await 到逻辑打开完成；转场默认并行，可用 .AwaitViewTransition() 等待。
        /// 同类型已打开/打开中时只刷新 userData（latest wins）。
        /// </summary>
        UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIBase;

        /// <summary>
        /// 异步显示 UI，并返回精确状态（Opened / Failed / Cancelled）。
        /// await 到逻辑打开完成；转场默认并行，可用 .AwaitViewTransition() 等待。
        /// </summary>
        UniTask<UIShowResult<T>> ShowUIResult<T>(params object[] userDatas) where T : UIBase;

        /// <summary>
        /// 异步显示 UI（使用字符串类型名）。类型未注册或元数据无效时返回 null 结果。
        /// </summary>
        UniTask<UIBase> ShowUI(string type, params object[] userDatas);

        /// <summary>
        /// 异步显示 UI，并返回精确状态（Opened / Failed / Cancelled）。
        /// </summary>
        UniTask<UIShowResult> ShowUIResult(string type, params object[] userDatas);

        /// <summary>
        /// 异步显示 UI（使用运行时类型句柄）。类型无效或元数据无效时返回 null 结果。
        /// </summary>
        UniTask<UIBase> ShowUI(RuntimeTypeHandle handle, params object[] userDatas);

        /// <summary>
        /// 异步显示 UI，并返回精确状态（Opened / Failed / Cancelled）。
        /// </summary>
        UniTask<UIShowResult> ShowUIResult(RuntimeTypeHandle handle, params object[] userDatas);

        /// <summary>
        /// 同步显示 UI（无参重载，避免 params 空数组分配）。
        /// 同步完成资源加载与初始化；Open 视觉过渡默认在后台推进。
        /// 同类型已在打开中/已打开时只刷新 userData（latest wins），不重复打开。
        /// </summary>
        T ShowUISync<T>() where T : UIBase;

        /// <summary>
        /// 同步显示 UI。
        /// 同步完成资源加载与初始化；Open 视觉过渡默认在后台推进。
        /// 同类型已在打开中/已打开时只刷新 userData（latest wins），不重复打开。
        /// </summary>
        T ShowUISync<T>(params object[] userDatas) where T : UIBase;

        // ───────────────────────────────────────────────
        //  Close / Get 系列
        // ───────────────────────────────────────────────

        /// <summary>
        /// 关闭指定类型 UI。若是 Router 当前页则走 Router（维护历史），否则只关层实例。
        /// 逻辑关闭在后台推进；可用返回值 AwaitViewTransition 等待关场动画。
        /// </summary>
        UICloseHandle CloseUI<T>(bool force = false) where T : UIBase;

        /// <summary>
        /// 关闭指定类型 UI。若是 Router 当前页则走 Router（维护历史），否则只关层实例。
        /// </summary>
        UICloseHandle CloseUI(RuntimeTypeHandle handle, bool force = false);

        /// <summary>
        /// 异步关闭指定类型 UI。路由页走 Router；叠加窗走层关闭。返回是否完成关闭。
        /// </summary>
        UniTask<bool> CloseUIAsync<T>(bool force = false) where T : UIBase;

        /// <summary>
        /// 异步关闭指定类型 UI。路由页走 Router；叠加窗走层关闭。返回是否完成关闭。
        /// </summary>
        UniTask<bool> CloseUIAsync(RuntimeTypeHandle handle, bool force = false);

        /// <summary>
        /// 是否处于 Opened 稳定态。Opening/Closing 返回 false；需要结果态请用 ShowUIResult。
        /// </summary>
        bool IsOpen<T>() where T : UIBase;

        /// <summary>
        /// 是否处于 Opened 稳定态。Opening/Closing 返回 false；需要结果态请用 ShowUIResult。
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
        T GetUI<T>() where T : UIBase;
    }
}
