using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public interface IUITransitionPlayer
    {
        UniTask PlayOpenAsync();

        UniTask PlayCloseAsync();

        void ApplyOpenState();

        void ApplyClosedState();

        /// <summary>
        /// Stops any running transition. After Stop/ApplyOpenState/ApplyClosedState, an older transition must not continue writing visuals.
        /// </summary>
        void Stop();
    }
}
