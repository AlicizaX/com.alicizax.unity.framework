#if ALICIZAX_UI_ANIMATION_SUPPORT
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    [DisallowMultipleComponent]
    public sealed class UIAnimationFlowTransition : AnimationFlow.Runtime.AnimationFlow, IUITransitionPlayer
    {

        [SerializeField] private string openClip = "Open";
        [SerializeField] private string closeClip = "Close";

        public UniTask PlayOpenAsync(CancellationToken cancellationToken = default)
        {
            return PlayAsync(openClip, cancellationToken);
        }

        public UniTask PlayCloseAsync(CancellationToken cancellationToken = default)
        {
            return PlayAsync(closeClip, cancellationToken);
        }


        private UniTask PlayAsync(string clipName, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(clipName))
            {
                return UniTask.CompletedTask;
            }


            return PlayAsync(clipName);
        }
    }
}

#endif
