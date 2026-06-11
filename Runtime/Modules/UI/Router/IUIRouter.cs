using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 业务导航路由器。
    /// 管理 Page 级 UI 的导航历史、返回、替换和重建流程，不负责 UI 实例生命周期、层级、遮挡和缓存。
    /// </summary>
    public interface IUIRouter
    {
        /// <summary>
        /// 使用默认 Page 策略无参导航到指定 UI 页面，避免 params 空数组分配。
        /// 默认会加入 history、关闭当前页面、合并重复当前页面。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <returns>导航成功返回 true；业务拒绝、目标打开失败等可恢复失败返回 false。</returns>
        UniTask<bool> NavigateTo<T>() where T : UIBase;

        /// <summary>
        /// 使用默认 Page 策略携带参数导航到指定 UI 页面。
        /// 默认会加入 history、关闭当前页面、合并重复当前页面。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <param name="args">传递给目标 UI 的业务参数。参数会被浅拷贝到 history。</param>
        /// <returns>导航成功返回 true；业务拒绝、目标打开失败等可恢复失败返回 false。</returns>
        UniTask<bool> NavigateTo<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// 无参打开目标页面并替换 history 当前项，避免 params 空数组分配。
        /// 目标打开失败时不修改 history；当前页关闭失败时按事务规则回滚或进入 dirty。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <returns>替换成功返回 true；业务拒绝或事务失败返回 false。</returns>
        UniTask<bool> Replace<T>() where T : UIBase;

        /// <summary>
        /// 携带参数打开目标页面并替换 history 当前项。
        /// 目标打开失败时不修改 history；当前页关闭失败时按事务规则回滚或进入 dirty。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <param name="args">传递给目标 UI 的业务参数。参数会被浅拷贝到 history。</param>
        /// <returns>替换成功返回 true；业务拒绝或事务失败返回 false。</returns>
        UniTask<bool> Replace<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// 返回上一条 Router history。
        /// 不处理弹窗优先关闭；弹窗关闭应由具体业务、输入系统或弹窗机制编排。
        /// </summary>
        /// <returns>返回成功返回 true；没有上一页、Router dirty、关闭或恢复失败返回 false。</returns>
        UniTask<bool> Back();

        /// <summary>
        /// 返回当前导航流程的 root 页面。
        /// 会关闭当前实际 Page，移除 root 之上的 history，并恢复 root entry 记录的参数；已经位于 root 时返回 true。
        /// </summary>
        /// <returns>返回 root 成功返回 true；history 为空、Router dirty、关闭或恢复失败返回 false。</returns>
        UniTask<bool> BackToRoot();

        /// <summary>
        /// 回退到 history 中最近的指定类型页面。
        /// 命中 history 时关闭当前实际 Page、移除目标之上的 history，并恢复旧 entry 参数；未命中且 openIfMissing 为 true 时执行 ResetTo。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <returns>回退或重建成功返回 true；未命中且不允许打开、Router dirty、事务失败返回 false。</returns>
        UniTask<bool> BackTo<T>() where T : UIBase;

        /// <summary>
        /// 回退到 history 中最近的指定类型页面，并可在未命中时用参数重建目标页面。
        /// 命中 history 时关闭当前实际 Page、移除目标之上的 history，并恢复旧 entry 参数；未命中且 openIfMissing 为 true 时执行 ResetTo。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <param name="openIfMissing">history 未命中时是否重建到目标页面。</param>
        /// <param name="args">仅在目标未命中且需要重建时使用；命中 history 时会恢复旧 entry 参数。</param>
        /// <returns>回退或重建成功返回 true；未命中且不允许打开、Router dirty、事务失败返回 false。</returns>
        UniTask<bool> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIBase;

        /// <summary>
        /// 无参重建导航栈，只保留目标页面作为 root entry，避免 params 空数组分配。
        /// 目标打开失败时不破坏旧页面和旧 history。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <returns>重建成功返回 true；目标打开失败或旧页面关闭失败返回 false。</returns>
        UniTask<bool> ResetTo<T>() where T : UIBase;

        /// <summary>
        /// 携带参数重建导航栈，只保留目标页面作为 root entry。
        /// 目标打开失败时不破坏旧页面和旧 history。
        /// </summary>
        /// <typeparam name="T">目标 UI 页面类型。</typeparam>
        /// <param name="args">传递给目标 UI 的业务参数。参数会被浅拷贝到 root entry。</param>
        /// <returns>重建成功返回 true；目标打开失败或旧页面关闭失败返回 false。</returns>
        UniTask<bool> ResetTo<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// 清空 Router history 并清除 dirty 状态。
        /// 只修复 Router 内部历史，不关闭任何实际 UI。
        /// </summary>
        void ResetHistory();

        /// <summary>
        /// 使用当前实际 UI 类型重建 Router history。
        /// 会清空旧 history，并把传入页面作为新的 root entry。
        /// </summary>
        /// <param name="currentPageType">当前实际显示的 Page UI 类型。</param>
        /// <param name="args">当前页面的恢复参数。参数会被浅拷贝到 root entry。</param>
        void SyncFromCurrentUI(Type currentPageType, params object[] args);

        /// <summary>
        /// 使用当前实际 UI 类型句柄重建 Router history。
        /// 会清空旧 history，并把传入页面作为新的 root entry。
        /// </summary>
        /// <param name="handle">当前实际显示的 Page UI 类型句柄。</param>
        /// <param name="args">当前页面的恢复参数。参数会被浅拷贝到 root entry。</param>
        void SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args);

        /// <summary>
        /// 当前是否可以消费 Router history 返回上一页。
        /// 只表示 history 状态，不代表是否存在可关闭弹窗。
        /// </summary>
        bool CanBack { get; }

        /// <summary>
        /// 当前 Router history 顶部的 UI 类型。
        /// history 为空时返回 null。
        /// </summary>
        Type Current { get; }

        /// <summary>
        /// 当前 Router history 顶部 entry 的快照。
        /// history 为空时返回 null。
        /// </summary>
        UIRouteEntry CurrentEntry { get; }

    }

#if UNITY_EDITOR
    /// <summary>
    /// UI Router 只读调试接口。
    /// 仅用于 Editor 面板或开发期诊断，不提供修改 history 的能力。
    /// </summary>
    internal interface IUIRouterDebug
    {
        /// <summary>
        /// 当前 Router history 条目数量。
        /// </summary>
        int HistoryCount { get; }

        /// <summary>
        /// 当前缓存的 Router warning 数量。
        /// </summary>
        int WarningCount { get; }

        /// <summary>
        /// 当前 Router history 顶部 UI 类型；history 为空时返回 null。
        /// </summary>
        Type Current { get; }

        /// <summary>
        /// 当前是否可以通过 Router history 返回上一页。
        /// </summary>
        bool CanBack { get; }

        /// <summary>
        /// Router history 是否已被标记为不可信。
        /// dirty 状态通常由导航事务回滚失败触发。
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// 填充指定 history 下标的调试快照。
        /// </summary>
        /// <param name="index">history 下标。</param>
        /// <param name="info">接收调试数据的对象。方法会先 Clear 再写入。</param>
        /// <returns>填充成功返回 true；下标无效或 info 为空返回 false。</returns>
        bool FillHistoryInfo(int index, UIRouteDebugInfo info);

        /// <summary>
        /// 填充指定 warning 下标的调试快照。
        /// </summary>
        /// <param name="index">warning 展示下标。</param>
        /// <param name="info">接收 warning 数据的对象。方法会先 Clear 再写入。</param>
        /// <returns>填充成功返回 true；下标无效或 info 为空返回 false。</returns>
        bool FillWarningInfo(int index, UIRouteWarningInfo info);

        /// <summary>
        /// 清空当前 Router warning 缓存。
        /// 不会修改 history 或 dirty 状态。
        /// </summary>
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
