using UnityEngine;

namespace AlicizaX.Resource.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ResourceOwner : MonoBehaviour
    {
        public ResourceScope Scope { get; internal set; }

        private void OnDestroy()
        {
            Scope.Dispose();
        }
    }
}
