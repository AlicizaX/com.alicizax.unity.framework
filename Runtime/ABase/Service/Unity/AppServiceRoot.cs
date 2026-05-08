using UnityEngine;

namespace AlicizaX
{
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public abstract class AppServiceRoot : MonoBehaviour
    {
        private static AppServiceRoot s_activeRoot;

        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private int _appScopeOrder = ServiceDomainOrder.App;

        private bool _ownsWorld;

        protected virtual void Awake()
        {
            if (s_activeRoot != null && s_activeRoot != this)
            {
                enabled = false;
                return;
            }

            s_activeRoot = this;

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            var createdWorld = !AppServices.HasWorld;
            var world = AppServices.EnsureWorld(_appScopeOrder);
            _ownsWorld = createdWorld;

            if (createdWorld)
                RegisterAppServices(world.App);
        }

        protected virtual void Update()
        {
            if (AppServices.HasWorld) AppServices.RequireWorld().Tick(Time.deltaTime);
        }

        protected virtual void LateUpdate()
        {
            if (AppServices.HasWorld) AppServices.RequireWorld().LateTick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (AppServices.HasWorld) AppServices.RequireWorld().FixedTick(Time.fixedDeltaTime);
        }

        protected virtual void OnDrawGizmos()
        {
            if (AppServices.HasWorld) AppServices.RequireWorld().DrawGizmos();
        }

        protected virtual void OnDestroy()
        {
            if (s_activeRoot == this)
                s_activeRoot = null;

            if (_ownsWorld && AppServices.HasWorld)
            {
                AppServices.Shutdown();
            }

        }

        protected virtual void RegisterAppServices(IServiceRegistry appServices) { }
    }
}
