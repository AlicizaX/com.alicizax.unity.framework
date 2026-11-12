using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public enum UIRouteStatus : byte
    {
        Success = 0,
        NotFound,
        OpenFailed,
        Cancelled,
    }

    public readonly struct UIRouteResult
    {
        public readonly UIRouteStatus Status;
        private readonly UIViewTransition _transition;

        public UIRouteResult(UIRouteStatus status, UIViewTransition transition = default)
        {
            Status = status;
            _transition = transition;
        }

        public UniTask AwaitTransition() => _transition.AwaitTransition();

        public bool Success => Status == UIRouteStatus.Success;

        public static UIRouteResult Ok { get; } = new(UIRouteStatus.Success);

        public static implicit operator bool(UIRouteResult result) => result.Success;
    }
}
