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
        private const int MIN_INITIAL_CAPACITY = 256;

        [SerializeField]
        [Min(MIN_INITIAL_CAPACITY)]
        private int _initialCapacity = 1024;

        private void Awake()
        {
            if (AppServices.TryGet<ITimerService>(out _))
            {
                return;
            }

            AppServices.RegisterApp<ITimerService>(new TimerService(_initialCapacity));
        }

        private void OnValidate()
        {
            if (_initialCapacity < MIN_INITIAL_CAPACITY)
            {
                _initialCapacity = MIN_INITIAL_CAPACITY;
            }
        }
    }
}

