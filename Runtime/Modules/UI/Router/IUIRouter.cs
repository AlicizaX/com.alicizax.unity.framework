using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 页面导航路由器。
    /// 管理页面级历史记录、导航、返回、替换和重置流程。
    /// UI 实例生命周期、层级、遮挡和缓存仍由 UIService 负责。
    /// </summary>
    public interface IUIRouter
    {
        /// <summary>
        /// 导航到目标页面，不携带用户数据。
        /// 目标页面会加入历史记录，前一个路由页面由 Router 关闭。
        /// </summary>
        UniTask<bool> NavigateTo<T>() where T : UIBase;

        /// <summary>
        /// 导航到目标页面，并携带用户数据。
        /// Args 会浅拷贝到历史记录中，以保持路由快照稳定。
        /// </summary>
        UniTask<bool> NavigateTo<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// 打开目标页面，并替换当前历史记录项。
        /// </summary>
        UniTask<bool> Replace<T>() where T : UIBase;

        /// <summary>
        /// 打开目标页面并携带用户数据，同时替换当前历史记录项。
        /// </summary>
        UniTask<bool> Replace<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// 返回到 Router 历史记录中的上一个页面。
        /// 此处不处理弹窗优先级；弹窗关闭规则应由调用方代码处理。
        /// </summary>
        UniTask<bool> Back();

        /// <summary>
        /// 关闭当前 Router 页面。
        /// 如果存在上一条历史记录，则等同于 Back；否则关闭并移除根页面。
        /// </summary>
        UniTask<bool> CloseCurrent();

        /// <summary>
        /// 关闭当前 Router 页面，并可选择强制关闭可缓存 UI。
        /// 如果存在上一条历史记录，则等同于 Back。
        /// </summary>
        UniTask<bool> CloseCurrent(bool force);

        /// <summary>
        /// 返回到当前导航根页面。
        /// 深层页面会通过 Router 批量关闭，因此只有当前页面播放关闭过渡。
        /// </summary>
        UniTask<bool> BackToRoot();

        /// <summary>
        /// 返回到历史记录中最近一次匹配的页面。
        /// 如果未找到，并且 openIfMissing 为 true，则使用 ResetTo。
        /// </summary>
        UniTask<bool> BackTo<T>() where T : UIBase;

        /// <summary>
        /// 返回到历史记录中最近一次匹配的页面。
        /// Args 只在目标页面缺失且需要打开时使用。
        /// </summary>
        UniTask<bool> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIBase;

        /// <summary>
        /// 重置导航历史记录，并只保留目标页面作为根页面。
        /// </summary>
        UniTask<bool> ResetTo<T>() where T : UIBase;

        /// <summary>
        /// 使用用户数据重置导航历史记录，并只保留目标页面作为根页面。
        /// </summary>
        UniTask<bool> ResetTo<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// 清空 Router 历史记录和脏状态。
        /// 不会关闭任何实际的 UI 实例。
        /// </summary>
        void ResetHistory();

        /// <summary>
        /// 根据已经显示的 UI 类型重建 Router 历史记录。
        /// 传入的页面会成为新的根记录项。
        /// </summary>
        void SyncFromCurrentUI(Type currentPageType, params object[] args);

        /// <summary>
        /// 根据已经显示的 UI 句柄重建 Router 历史记录。
        /// 传入的页面会成为新的根记录项。
        /// </summary>
        void SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args);

        /// <summary>
        /// Router 历史记录是否可用于返回上一页。
        /// 这里仅描述 Router 历史记录，不包含弹窗是否可关闭。
        /// </summary>
        bool CanBack { get; }

        /// <summary>
        /// 当前 Router 历史记录栈顶的 UI 类型。历史记录为空时返回 null。
        /// </summary>
        Type Current { get; }

        /// <summary>
        /// 当前 Router 历史记录栈顶记录项的快照。历史记录为空时返回 null。
        /// </summary>
        UIRouteEntry CurrentEntry { get; }
    }

    internal interface IUIRouterInternal
    {
        bool IsCurrent(RuntimeTypeHandle handle);

        UniTask<bool> CloseCurrent(RuntimeTypeHandle expectedHandle, bool force);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 供 Editor 工具使用的只读 Router 调试接口。
    /// </summary>
    internal interface IUIRouterDebug
    {
        int HistoryCount { get; }

        int WarningCount { get; }

        Type Current { get; }

        bool CanBack { get; }

        bool IsDirty { get; }

        bool FillHistoryInfo(int index, UIRouteDebugInfo info);

        bool FillWarningInfo(int index, UIRouteWarningInfo info);

        void ClearWarnings();
    }

    public sealed class UIRouteDebugInfo
    {
        public int Index;
        public string UITypeName;
        public bool IsRoot;
        public int Sequence;
        public string ArgsPreview;

        public void Clear()
        {
            Index = 0;
            UITypeName = null;
            IsRoot = false;
            Sequence = 0;
            ArgsPreview = null;
        }
    }

    public sealed class UIRouteWarningInfo
    {
        public int Index;
        public int Sequence;
        public string UITypeName;
        public string Message;

        public void Clear()
        {
            Index = 0;
            Sequence = 0;
            UITypeName = null;
            Message = null;
        }
    }
#endif
}
