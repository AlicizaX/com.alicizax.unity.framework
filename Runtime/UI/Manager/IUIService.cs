using System;
using AlicizaX.Timer.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 模块接口：负责 UI 的创建、显示、关闭与查询。
    /// 支持异步与同步（预加载）两种打开方式。
    /// </summary>
    public interface IUIService : IService
    {
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
        /// 异步显示 UI（无参重载，避免 params 空数组分配）
        /// </summary>
        UniTask<T> ShowUI<T>() where T : UIBase;

        /// <summary>
        /// 异步显示 UI（推荐方式）
        /// </summary>
        UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIBase;

        /// <summary>
        /// 异步显示 UI（使用字符串类型名）。类型未注册或元数据无效时返回 null 结果。
        /// </summary>
        UniTask<UIBase> ShowUI(string type, params object[] userDatas);

        /// <summary>
        /// 同步显示 UI（无参重载，避免 params 空数组分配）
        /// </summary>
        T ShowUISync<T>() where T : UIBase;

        /// <summary>
        /// 同步显示 UI（仅限资源已预加载时使用，避免死锁）
        /// </summary>
        T ShowUISync<T>(params object[] userDatas) where T : UIBase;

        // ───────────────────────────────────────────────
        //  Close / Get 系列
        // ───────────────────────────────────────────────

        /// <summary>
        /// 关闭指定类型 UI。
        /// </summary>
        void CloseUI<T>(bool force = false) where T : UIBase;

        /// <summary>
        /// 关闭指定类型 UI。
        /// </summary>
        void CloseUI(RuntimeTypeHandle handle, bool force = false);

        /// <summary>
        /// 获取当前已打开的指定类型 UI。
        /// </summary>
        T GetUI<T>() where T : UIBase;

    }
}
