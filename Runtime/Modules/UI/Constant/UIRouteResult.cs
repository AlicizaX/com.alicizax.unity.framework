using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public enum UIRouteStatus : byte
    {
        Success = 0,
        RejectedLimit,
        NotFound,
        OpenFailed,
        CloseFailed,
        Cancelled,
    }

    public readonly struct UIRouteResult
    {
        public readonly UIRouteStatus Status;
        private readonly UniTask _transition;

        public UIRouteResult(UIRouteStatus status, UniTask transition = default)
        {
            Status = status;
            _transition = transition;
        }

        public UniTask AwaitTransition() => _transition;

        public bool Success => Status == UIRouteStatus.Success;

        public static UIRouteResult Ok { get; } = new(UIRouteStatus.Success);

        public static UIRouteResult From(UIRouteStatus status) => new(status);

        public static implicit operator bool(UIRouteResult result) => result.Success;
    }

#if UNITY_EDITOR
    internal interface IUINavigationDebug
    {
        int HistoryCount { get; }

        int WarningCount { get; }

        Type Current { get; }

        bool CanBack { get; }

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
