using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public sealed class UIDelegateTransitionSource : IUITransitionSource
    {
        private readonly Func<bool, CancellationToken, UniTask> _play;
        private readonly Action<bool> _snap;

        public UIDelegateTransitionSource(Func<bool, CancellationToken, UniTask> play, Action<bool> snap)
        {
            _play = play;
            _snap = snap;
        }

        public UniTask Play(bool open, CancellationToken cancellationToken)
        {
            return _play != null ? _play(open, cancellationToken) : UniTask.CompletedTask;
        }

        public void Snap(bool open)
        {
            _snap?.Invoke(open);
        }
    }
}
