using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public enum UIShowResultState : byte
    {
        Failed,
        Opened,
        Cancelled,
    }

    public readonly struct UIShowResult
    {
        public readonly UIBase View;
        public readonly UIShowResultState State;

        public UIShowResult(UIBase view, UIShowResultState state)
        {
            View = view;
            State = state;
        }

        public bool IsAccepted => State == UIShowResultState.Opened;

        public UniTask AwaitViewTransition()
        {
            return View != null ? View.AwaitViewTransition() : UniTask.CompletedTask;
        }

        public static UIShowResult Failed => new(null, UIShowResultState.Failed);
        public static UIShowResult Cancelled => new(null, UIShowResultState.Cancelled);
    }

    public readonly struct UIShowResult<T> where T : UIBase
    {
        public readonly T View;
        public readonly UIShowResultState State;

        public UIShowResult(T view, UIShowResultState state)
        {
            View = view;
            State = state;
        }

        public bool IsAccepted => State == UIShowResultState.Opened;

        public UniTask AwaitViewTransition()
        {
            return View != null ? View.AwaitViewTransition() : UniTask.CompletedTask;
        }
    }

    public readonly struct UICloseHandle
    {
        private readonly UniTask<bool> _closeTask;

        public UICloseHandle(UniTask<bool> closeTask)
        {
            _closeTask = closeTask;
        }

        /// <summary>
        /// 等待关闭流程（含关场转场）完成。
        /// </summary>
        public async UniTask AwaitViewTransition()
        {
            await _closeTask;
        }
    }

    public static class UIShowAwaitExtensions
    {
        public static async UniTask AwaitViewTransition(this UniTask<UIShowResult> showTask)
        {
            UIShowResult result = await showTask;
            await result.AwaitViewTransition();
        }

        public static async UniTask AwaitViewTransition<T>(this UniTask<UIShowResult<T>> showTask) where T : UIBase
        {
            UIShowResult<T> result = await showTask;
            await result.AwaitViewTransition();
        }

        public static async UniTask AwaitViewTransition<T>(this UniTask<T> showTask) where T : UIBase
        {
            T view = await showTask;
            if (view != null)
            {
                await view.AwaitViewTransition();
            }
        }
    }
}
