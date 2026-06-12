using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI page navigation router.
    /// Manages page-level history, navigation, back, replace and reset flows.
    /// UI instance lifecycle, layers, occlusion and cache are still owned by UIService.
    /// </summary>
    public interface IUIRouter
    {
        /// <summary>
        /// Navigate to the target page without user data.
        /// The target is added to history and the previous route page is closed by Router.
        /// </summary>
        UniTask<bool> NavigateTo<T>() where T : UIBase;

        /// <summary>
        /// Navigate to the target page with user data.
        /// Args are shallow-copied into history to keep route snapshots stable.
        /// </summary>
        UniTask<bool> NavigateTo<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// Open the target page and replace the current history entry.
        /// </summary>
        UniTask<bool> Replace<T>() where T : UIBase;

        /// <summary>
        /// Open the target page with user data and replace the current history entry.
        /// </summary>
        UniTask<bool> Replace<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// Back to the previous Router history entry.
        /// Popup priority is not handled here; popup close rules should be handled by caller code.
        /// </summary>
        UniTask<bool> Back();

        /// <summary>
        /// Close the current Router page.
        /// If there is previous history, this is equivalent to Back; otherwise the root page is closed and removed.
        /// </summary>
        UniTask<bool> CloseCurrent();

        /// <summary>
        /// Close the current Router page and optionally force close cacheable UI.
        /// If there is previous history, this is equivalent to Back.
        /// </summary>
        UniTask<bool> CloseCurrent(bool force);

        /// <summary>
        /// Back to the current navigation root.
        /// Deep pages are closed through a Router batch close so only the current page plays close transition.
        /// </summary>
        UniTask<bool> BackToRoot();

        /// <summary>
        /// Back to the latest matching page in history.
        /// If missing, ResetTo is used when openIfMissing is true.
        /// </summary>
        UniTask<bool> BackTo<T>() where T : UIBase;

        /// <summary>
        /// Back to the latest matching page in history.
        /// Args are only used when the target is missing and must be opened.
        /// </summary>
        UniTask<bool> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIBase;

        /// <summary>
        /// Reset navigation history and keep only the target page as root.
        /// </summary>
        UniTask<bool> ResetTo<T>() where T : UIBase;

        /// <summary>
        /// Reset navigation history with user data and keep only the target page as root.
        /// </summary>
        UniTask<bool> ResetTo<T>(params object[] args) where T : UIBase;

        /// <summary>
        /// Clear Router history and dirty state.
        /// Does not close any actual UI instance.
        /// </summary>
        void ResetHistory();

        /// <summary>
        /// Rebuild Router history from an already shown UI type.
        /// The passed page becomes the new root entry.
        /// </summary>
        void SyncFromCurrentUI(Type currentPageType, params object[] args);

        /// <summary>
        /// Rebuild Router history from an already shown UI handle.
        /// The passed page becomes the new root entry.
        /// </summary>
        void SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args);

        /// <summary>
        /// Whether Router history can be consumed to back to a previous page.
        /// This only describes Router history; popup close availability is not included.
        /// </summary>
        bool CanBack { get; }

        /// <summary>
        /// Current Router history top UI type. Returns null when history is empty.
        /// </summary>
        Type Current { get; }

        /// <summary>
        /// Snapshot of the current Router history top entry. Returns null when history is empty.
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
    /// Read-only Router debug interface for Editor tools.
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
