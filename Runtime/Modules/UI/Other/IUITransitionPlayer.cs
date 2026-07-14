using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public interface IUITransitionPlayer
    {
        UniTask PlayOpenAsync();

        UniTask PlayCloseAsync();

        void ApplyOpenState();

        void ApplyClosedState();

        void Stop();
    }
}
