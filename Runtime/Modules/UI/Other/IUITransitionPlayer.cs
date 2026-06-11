using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public interface IUITransitionPlayer
    {
        UniTask PlayOpenAsync(CancellationToken cancellationToken = default);

        UniTask PlayCloseAsync(CancellationToken cancellationToken = default);

        void ApplyOpenState();

        void ApplyClosedState();

        void Stop();
    }
}
