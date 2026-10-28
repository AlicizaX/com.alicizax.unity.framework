using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>UI 根节点访问：初始化、相机、层级节点。</summary>
    public interface IUIRootService
    {
        /// <summary>初始化 UI 模块。</summary>
        /// <param name="root">UI 根节点（通常为 Canvas 根）</param>
        /// <param name="isOrthographic">摄像机是否正交模式</param>
        void Initialize(Transform root, bool isOrthographic);

        /// <summary>UI 摄像机。</summary>
        Camera UICamera { get; }

        /// <summary>UI 根节点。</summary>
        Transform UICanvasRoot { get; }

        /// <summary>获取指定 UI 层级的根节点。</summary>
        RectTransform GetLayer(UILayer layer);
    }
}
