using AlicizaX.ObjectPool;
using UnityEngine;

namespace AlicizaX
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/ObjectPool")]
    [UnityEngine.Scripting.Preserve]
    [DefaultExecutionOrder(-900)]
    public sealed class ObjectPoolComponent : MonoBehaviour
    {
        private IObjectPoolService _mObjectPoolService;

        public int Count => _mObjectPoolService.Count;

        private void Awake()
        {
            _mObjectPoolService = AppServices.RegisterApp<IObjectPoolService>(new ObjectPoolService());
            Application.lowMemory += OnLowMemory;
        }

        private void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
            _mObjectPoolService = null;
        }

        private void OnLowMemory()
        {
            if (_mObjectPoolService is ObjectPoolService svc)
                svc.OnLowMemory();
        }

        internal int GetAllObjectPools(bool sort, ObjectPoolBase[] results)
        {
            if (_mObjectPoolService is ObjectPoolService svc)
                return svc.GetAllObjectPools(sort, results);

            return 0;
        }
    }
}
