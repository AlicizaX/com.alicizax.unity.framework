using System;

namespace AlicizaX
{
    public static class AppServices
    {
        private static ServiceWorld _world;

        public static bool HasWorld => _world != null;
        internal static ServiceWorld EnsureWorld(int appScopeOrder = ServiceDomainOrder.App)
        {
            if (_world == null)
                _world = new ServiceWorld(appScopeOrder);
            return _world;
        }

        internal static ServiceWorld RequireWorld()
        {
            if (_world == null)
                throw new InvalidOperationException("ServiceWorld has not been created yet.");
            return _world;
        }

        public static T RegisterAppSelf<T>(T service) where T : class, IService
            => RequireWorld().App.RegisterSelf(service);

        public static TContract RegisterApp<TContract>(IService service)
            where TContract : class, IService
            => RequireWorld().App.Register<TContract>(service);

        internal static TContract RegisterApp<TContract, TExtraContract>(IService service)
            where TContract : class, IService
            where TExtraContract : class, IService
            => RequireWorld().App.Register<TContract, TExtraContract>(service);

        public static bool TryGetApp<T>(out T service) where T : class, IService
        {
            if (_world == null)
            {
                service = null;
                return false;
            }

            return _world.App.TryGet(out service);
        }

        public static T RequireApp<T>() where T : class, IService
            => RequireWorld().App.Require<T>();

        public static bool TryGet<T>(out T service) where T : class, IService
        {
            if (_world == null)
            {
                service = null;
                return false;
            }

            return _world.TryGet(out service);
        }

        public static T Require<T>() where T : class, IService
            => RequireWorld().Require<T>();

        internal static ServiceScope EnsureScene(int order = ServiceDomainOrder.Scene)
            => RequireWorld().EnsureScene(order);

        internal static bool TryGetScene(out ServiceScope scope)
        {
            if (_world == null)
            {
                scope = null;
                return false;
            }

            return _world.TryGetScene(out scope);
        }

        internal static ServiceScope ResetScene(int order = ServiceDomainOrder.Scene)
            => RequireWorld().ResetScene(order);

        internal static bool DestroyScene()
            => _world != null && _world.DestroyScene();

        internal static ServiceScope EnsureGameplay(int order = ServiceDomainOrder.Gameplay)
            => RequireWorld().EnsureGameplay(order);

        internal static bool TryGetGameplay(out ServiceScope scope)
        {
            if (_world == null)
            {
                scope = null;
                return false;
            }

            return _world.TryGetGameplay(out scope);
        }

        internal static bool DestroyGameplay()
            => _world != null && _world.DestroyGameplay();

        public static void Shutdown()
        {
            if (_world == null) return;
            _world.Dispose();
            _world = null;
        }
    }
}
