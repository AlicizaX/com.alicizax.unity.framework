using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public readonly struct UIViewTransition
    {
        private readonly UIBase _view;

        internal UIViewTransition(UIBase view) => _view = view;

        public UniTask AwaitTransition() => _view != null ? _view.AwaitTransition() : UniTask.CompletedTask;
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
