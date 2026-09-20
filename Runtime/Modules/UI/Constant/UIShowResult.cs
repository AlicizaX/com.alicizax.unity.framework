using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public static class UIShowAwaitExtensions
    {
        public static async UniTask AwaitTransition<T>(this UniTask<T> showTask) where T : UIBase
        {
            T view = await showTask;
            if (view != null) await view.AwaitTransition();
        }
    }
}
