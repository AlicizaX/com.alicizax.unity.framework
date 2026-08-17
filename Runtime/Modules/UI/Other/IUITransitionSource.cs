using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public interface IUITransitionSource
    {
        UniTask Play(bool open, CancellationToken cancellationToken);

        void Snap(bool open);
    }
}
