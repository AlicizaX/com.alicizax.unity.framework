using UnityEngine;

namespace AlicizaX.Resource.Runtime
{
    [DisallowMultipleComponent]
    public sealed class MaterialInstanceReference : MonoBehaviour
    {
        private Material _material;

        public void Set(Material material)
        {
            if (_material != null && _material != material)
            {
                Object.Destroy(_material);
            }

            _material = material;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }
        }
    }
}
