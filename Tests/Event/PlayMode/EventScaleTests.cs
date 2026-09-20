#if UNITY_EDITOR
using System.Collections;
using UnityEngine.TestTools;

namespace AlicizaX.EventTests
{
    public sealed class EventScaleTests
    {
        [UnityTest] public IEnumerator CallerAllocationControls() => EventScaleScenarios.CallerAllocationControls();
        [UnityTest] public IEnumerator PublisherSources100000() => EventScaleScenarios.PublisherSources(100000);
        [UnityTest] public IEnumerator PublisherSources300000() => EventScaleScenarios.PublisherSources(300000);
        [UnityTest] public IEnumerator LiveSubscriptions100000() => EventScaleScenarios.LiveSubscriptions(100000);
        [UnityTest] public IEnumerator LiveSubscriptions300000() => EventScaleScenarios.LiveSubscriptions(300000);
        [UnityTest] public IEnumerator InterleavedOperations100000() => EventScaleScenarios.InterleavedOperations(100000);
        [UnityTest] public IEnumerator InterleavedOperations300000() => EventScaleScenarios.InterleavedOperations(300000);
        [UnityTest] public IEnumerator PendingMutations100000() => EventScaleScenarios.PendingMutations(100000);
        [UnityTest] public IEnumerator PendingMutations300000() => EventScaleScenarios.PendingMutations(300000);
    }
}
#endif
