using AlicizaX;
using UnityEngine;

namespace AlicizaX.Timer.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Timer")]
    [UnityEngine.Scripting.Preserve]
    [DefaultExecutionOrder(-800)]
    public sealed class TimerComponent : MonoBehaviour
    {
        private void Awake()
        {
            if (AppServices.TryGet<ITimerService>(out _))
            {
                return;
            }

            AppServices.Register<ITimerService>(new TimerService());
        }
    }
}

