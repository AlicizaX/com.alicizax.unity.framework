namespace AlicizaX
{
    public abstract class ServiceBase : IService, IServiceLifecycle
    {
        private ServiceRegistry _registry;

        void IServiceLifecycle.Initialize(ServiceRegistry registry)
        {
            _registry = registry;
            OnInitialize();
        }

        void IServiceLifecycle.Dispose()
        {
            OnDispose();
            _registry = null;
        }

        protected T Require<T>() where T : class, IService => _registry.Require<T>();

        protected bool TryGet<T>(out T service) where T : class, IService => _registry.TryGet(out service);

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDispose()
        {
        }
    }
}
