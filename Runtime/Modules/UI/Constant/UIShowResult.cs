using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public readonly struct UICloseHandle
    {
        private readonly UniTask<bool> _closeTask;

        public UICloseHandle(UniTask<bool> closeTask) => _closeTask = closeTask;
        public UniTask<bool> AwaitTransition() => _closeTask;
    }

    public static class UIShowAwaitExtensions
    {
        public static async UniTask AwaitTransition<T>(this UniTask<T> showTask) where T : UIBase
        {
            T view = await showTask;
            if (view != null) await view.AwaitTransition();
        }
    }
}
