using Cysharp.Text;

namespace AlicizaX
{
    internal interface IServiceLifecycle
    {
        void Initialize(ServiceWorld world, ServiceScope scope);
        void Destroy();
    }

    public abstract class ServiceBase : IService, IServiceLifecycle
    {
        private ServiceWorld _world;
        private ServiceScope _scope;

        protected bool IsInitialized { get; private set; }

        void IServiceLifecycle.Initialize(ServiceWorld world, ServiceScope scope)
        {
            if (IsInitialized)
                throw new System.InvalidOperationException(ZString.Format("{0} is already initialized.", GetType().FullName));

            _world = world;
            _scope = scope;
            IsInitialized = true;
            OnInitialize();
        }

        void IServiceLifecycle.Destroy()
        {
            if (!IsInitialized) return;

            OnDestroyService();
            IsInitialized = false;
            _world = null;
            _scope = null;
        }

        protected T Require<T>() where T : class, IService
            => _world.Require<T>(_scope);

        protected bool TryGet<T>(out T service) where T : class, IService
            => _world.TryGet(_scope, out service);

        protected T RequireApp<T>() where T : class, IService
            => _world.App.Require<T>();

        protected T RequireScene<T>() where T : class, IService
            => _world.Scene.Require<T>();

        protected T RequireGameplay<T>() where T : class, IService
            => _world.Gameplay.Require<T>();

        protected abstract void OnInitialize();

        protected abstract void OnDestroyService();
    }
}
