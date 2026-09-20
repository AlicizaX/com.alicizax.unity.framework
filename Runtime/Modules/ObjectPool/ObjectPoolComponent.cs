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

        public int Count => _mObjectPoolService != null ? _mObjectPoolService.Count : 0;

        private void Awake()
        {
            if (AppServices.App.TryGet<IObjectPoolService>(out _))
                return;
            _mObjectPoolService = AppServices.App.Register<IObjectPoolService>(new ObjectPoolService());
        }

        private void OnDestroy()
        {
            if (_mObjectPoolService != null
                && AppServices.HasWorld
                && AppServices.App.TryGet<IObjectPoolService>(out IObjectPoolService registered)
                && ReferenceEquals(registered, _mObjectPoolService))
            {
                AppServices.App.Unregister(_mObjectPoolService);
            }

            _mObjectPoolService = null;
        }

        internal int GetAllObjectPools(bool sort, ObjectPoolBase[] results)
        {
            if (_mObjectPoolService is ObjectPoolService svc)
                return svc.GetAllObjectPools(sort, results);

            return 0;
        }
    }
}
