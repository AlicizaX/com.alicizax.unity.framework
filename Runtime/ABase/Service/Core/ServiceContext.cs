namespace AlicizaX
{
    public readonly struct ServiceContext
    {
        internal ServiceContext(ServiceWorld world, ServiceScope scope)
        {
            World = world;
            Scope = scope;
        }

        internal ServiceWorld World { get; }

        internal ServiceScope Scope { get; }


        public T Require<T>() where T : class, IService
            => World.Require<T>(Scope);

        public bool TryGet<T>(out T service) where T : class, IService
            => World.TryGet(Scope, out service);

        internal ServiceScope EnsureScene(int order = ServiceDomainOrder.Scene)
            => World.EnsureScene(order);

        internal bool TryGetScene(out ServiceScope scope)
            => World.TryGetScene(out scope);

        internal ServiceScope ResetScene(int order = ServiceDomainOrder.Scene)
            => World.ResetScene(order);

        internal ServiceScope EnsureGameplay(int order = ServiceDomainOrder.Gameplay)
            => World.EnsureGameplay(order);

        internal bool TryGetGameplay(out ServiceScope scope)
            => World.TryGetGameplay(out scope);

        public T RequireApp<T>() where T : class, IService
            => World.App.Require<T>();

        public T RequireScene<T>() where T : class, IService
            => World.Scene.Require<T>();

        public T RequireGameplay<T>() where T : class, IService
            => World.Gameplay.Require<T>();
    }
}
